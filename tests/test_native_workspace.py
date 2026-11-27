"""Native workspace acceptance uses generated documents and isolated owner-scoped stores."""
import base64
from datetime import datetime, timedelta
from uuid import uuid4, uuid5, NAMESPACE_URL
from zoneinfo import ZoneInfo

import pymupdf
import pytest
from fastapi.testclient import TestClient

from app.core.dependencies import get_interview_experience_mapper
from app.model.auth import AuthUser
from app.model.interview_experience import InterviewExperience
from app.model.personal_event import PersonalEvent
from app.service.auth_service import hash_password
from app.service.native_workspace_service import NativeWorkspaceService
from test_public_multiuser import _public_fixture, _login

PREFIX = "/api/mobile/v1/workspace"


def test_workspace_monitor_nodes_do_not_expose_ids_or_queries():
    from app.core.performance import node_for_path
    for path, method, node in (("/resume", "GET", "resume_state"), ("/resume", "POST", "resume_upload"),
                               ("/resume/document", "GET", "resume_file"), ("/interviews?q=private", "GET", "interviews"),
                               ("/experiences/private-id/draft", "PUT", "draft"), ("/experiences/private-id/document", "GET", "pdf")):
        assert node_for_path(PREFIX + path, method) == node


def user(mapper, name="native"):
    item = AuthUser(email=f"{name}@example.test", password_hash=hash_password("workspace-pass"), email_verified=True)
    mapper.create_user(item)
    return item


def pdf(text="Generated resume fixture", pages=2, width=595, height=842):
    with pymupdf.open() as document:
        for _ in range(pages):
            document.new_page(width=width, height=height).insert_text((40, 70), text)
        return document.tobytes()


def test_native_workspace_requires_session_and_csrf(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    owner = user(mapper)
    with TestClient(app, base_url="https://testserver") as client:
        for path in ("/interviews", "/resume", "/resume/document"):
            assert client.get(PREFIX + path, follow_redirects=False).status_code in {303, 401}
        _login(client, owner.email, "workspace-pass")
        assert client.post(PREFIX + "/resume", files={"resume": ("fixture.pdf", pdf(), "application/pdf")}).status_code == 403
        assert client.delete(PREFIX + "/experiences/anything").status_code == 403


def test_interview_filter_paging_excludes_meetings_and_other_owners(tmp_path):
    app, mapper, _, events, *_ = _public_fixture(tmp_path)
    owner, other = user(mapper), user(mapper, "other")
    tomorrow = datetime.now(ZoneInfo("Asia/Shanghai")) + timedelta(days=1)
    for index in range(22):
        events.save(PersonalEvent(owner_id=owner.id, type="interview", title=f"算法{index:02d}", start_at=tomorrow, end_at=tomorrow+timedelta(hours=1)))
    for event_type, account in (("meeting", owner), ("interview", other)):
        events.save(PersonalEvent(owner_id=account.id, type=event_type, title="不得显示", start_at=tomorrow, end_at=tomorrow+timedelta(hours=1)))
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, owner.email, "workspace-pass")
        result = client.get(PREFIX + "/interviews", params={"q": "算法", "status": "upcoming", "page": 99}).json()
        assert result["total"] == 22 and result["page"] == 2 and len(result["items"]) == 2
        assert all(row["owner_id"] == owner.id and "raw_invitation" not in row for row in result["items"])
        assert client.get(PREFIX + "/interviews?status=finished").json()["items"] == []


def test_native_draft_version_tree_validation_and_delete_isolation(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    owner, other = user(mapper), user(mapper, "other")
    store = app.dependency_overrides[get_interview_experience_mapper]()
    alien = store.save_experience(InterviewExperience(owner_id=other.id, title="其他账号"))
    with TestClient(app, base_url="https://testserver") as client:
        headers = {"X-CSRF-Token": _login(client, owner.email, "workspace-pass")}
        created = client.post("/api/mobile/v1/experiences", json={"title": "原生草稿"}, headers=headers).json()["experience"]
        assert created["is_public"] is False
        root, child = str(uuid4()), str(uuid4())
        payload = {"version": created["updated_at"], "raw_text_draft": "1、项目\n1.1 追问", "feeling": "感受", "category_minutes": {"project": 30}, "total_minutes": 45,
                   "questions": [{"id": root, "category": "project", "question_text": "项目", "on_site_answer": "现场", "review_answer": "复盘"},
                                 {"id": child, "parent_question_id": root, "category": "other", "question_text": "追问"}]}
        path = PREFIX + f"/experiences/{created['id']}/draft"
        saved = client.put(path, json=payload, headers=headers)
        assert saved.status_code == 200, saved.text
        full = saved.json()["experience"]
        assert full["raw_text_draft"] == payload["raw_text_draft"] and full["total_minutes"] == 45
        assert full["questions"][1]["parent_question_id"] == root and full["questions"][1]["category"] == "project"
        assert client.put(path, json=payload, headers=headers).status_code == 409
        bad = {**payload, "version": full["updated_at"], "questions": [{"id": root, "parent_question_id": child, "category": "project", "question_text": "循环"}, payload["questions"][1]]}
        assert client.put(path, json=bad, headers=headers).status_code == 422
        assert client.put(path, json={**payload, "total_minutes": -1}, headers=headers).status_code == 422
        assert client.put(PREFIX + f"/experiences/{alien.id}/draft", json=payload, headers=headers).status_code == 404
        assert client.delete(PREFIX + f"/experiences/{alien.id}", headers=headers).status_code == 404
        assert client.delete(PREFIX + f"/experiences/{created['id']}", headers=headers).status_code == 204
        assert client.get(f"/api/mobile/v1/experiences/{created['id']}").status_code == 404


def test_resume_upload_version_preview_and_rejected_upload_preserves_current(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    owner, other = user(mapper), user(mapper, "other")
    with TestClient(app, base_url="https://testserver") as client:
        headers = {"X-CSRF-Token": _login(client, owner.email, "workspace-pass")}
        assert client.get(PREFIX + "/resume/document").status_code == 404
        response = client.post(PREFIX + "/resume", files={"resume": ("generated.pdf", pdf(), "application/pdf")}, headers=headers)
        assert response.status_code == 201, response.text
        first = response.json()["resume"]
        preview = client.get(PREFIX + "/resume/document")
        assert preview.status_code == 200 and preview.headers["cache-control"] == "no-store"
        document = preview.json()
        assert document["id"] == first["id"] and document["version"] == first["version"]
        assert len(document["pages"]) == 2
        assert base64.b64decode(document["pages"][0]).startswith(b"\x89PNG\r\n\x1a\n")
        assert client.post(PREFIX + "/resume", files={"resume": ("bad.txt", b"no", "text/plain")}, headers=headers).status_code == 422
        assert client.post(PREFIX + "/resume", files={"resume": ("bad.pdf", b"broken", "application/pdf")}, headers=headers).status_code == 422
        assert client.get(PREFIX + "/resume").json()["resume"]["version"] == first["version"]
        second = client.post(PREFIX + "/resume", files={"resume": ("new.pdf", pdf("Changed", 1), "application/pdf")}, headers=headers).json()["resume"]
        assert second["version"] != first["version"]
        client.cookies.clear()
        _login(client, other.email, "workspace-pass")
        assert client.get(PREFIX + "/resume").json()["resume"] is None
        assert client.get(PREFIX + "/resume/document").status_code == 404


def test_export_preview_options_share_web_export_and_do_not_mutate_source(tmp_path, monkeypatch):
    app, mapper, *_ = _public_fixture(tmp_path)
    owner = user(mapper)
    store = app.dependency_overrides[get_interview_experience_mapper]()
    note = store.save_experience(InterviewExperience(owner_id=owner.id, title="原始标题", company="公司", position="岗位"))
    captured = {}

    def capture(item, rows, style, **flags):
        captured.update(item=item, rows=rows, style=style, flags=flags)
        return pdf()

    monkeypatch.setattr("app.service.native_workspace_service.export_pdf", capture)
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, owner.email, "workspace-pass")
        path = PREFIX + f"/experiences/{note.id}/document"
        result = client.get(path, params={"style": "flat", "title": "导出标题", "hide_company": True, "hide_position": True, "hide_on_site_answer": True, "hide_review_answer": True})
        assert result.status_code == 200
        assert captured["item"].title == "导出标题" and captured["item"].company is None and captured["item"].position is None
        assert captured["style"] == "flat" and captured["flags"] == {"hide_on_site_answer": True, "hide_review_answer": True, "hide_interviewer_answer": False}
        source = store.find_experience(note.id, owner.id)
        assert source.title == "原始标题" and source.company == "公司"
        assert client.get(path + "?style=unknown").status_code == 422
        assert client.get(path, params={"title": "x" * 201}).status_code == 422


def test_pdf_preview_rejects_corruption_encryption_and_unbounded_documents():
    for content in (b"broken", pdf(pages=81), pdf(pages=1, width=3000, height=5000)):
        with pytest.raises(ValueError):
            NativeWorkspaceService.render_pdf(content)
    with pymupdf.open(stream=pdf(), filetype="pdf") as document:
        encrypted = document.tobytes(encryption=pymupdf.PDF_ENCRYPT_AES_256, owner_pw="owner", user_pw="user")
    with pytest.raises(ValueError):
        NativeWorkspaceService.render_pdf(encrypted)


def test_native_calendar_preserves_manual_meeting_link_and_room_through_edit(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    owner = user(mapper)
    operation = str(uuid4())
    event_id = str(uuid5(NAMESPACE_URL, f"calendar:{owner.id}:{operation}"))
    event = {"title": "Generated meeting", "type": "meeting", "start_at": "2026-11-23T10:00:00+08:00", "end_at": "2026-11-23T11:00:00+08:00",
             "meeting_url": "https://example.test/join", "room_number": "123 456"}
    with TestClient(app, base_url="https://testserver") as client:
        headers = {"X-CSRF-Token": _login(client, owner.email, "workspace-pass")}
        created = client.post(f"/my-schedule/calendar-sync/{operation}", json={"action": "create", "event_id": event_id, "event": event}, headers=headers)
        assert created.status_code == 200, created.text
        assert created.json()["event"]["meeting_url"] == event["meeting_url"]
        assert created.json()["event"]["room_number"] == "123 456"
        edited = client.post(f"/my-schedule/calendar-sync/{uuid4()}", json={"action": "edit", "event_id": event_id, "event": {**event, "title": "Edited"}}, headers=headers)
        assert edited.status_code == 200 and edited.json()["event"]["meeting_url"] == event["meeting_url"]
