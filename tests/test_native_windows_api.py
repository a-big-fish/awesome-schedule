"""Native contracts reuse Web ownership, public visibility and profile validation."""
from fastapi.testclient import TestClient

from app.model.auth import AuthUser
from app.model.interview_experience import InterviewExperience, InterviewQuestion
from app.service.auth_service import hash_password
from app.core.dependencies import get_interview_experience_mapper
from test_public_multiuser import _public_fixture, _login


def test_native_profile_is_allowlisted_and_csrf_protected(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="native-profile@example.com", password_hash=hash_password("native-password"), email_verified=True)
    mapper.create_user(user)
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get("/api/mobile/v1/profile", follow_redirects=False).status_code in {303, 401}
        csrf = _login(client, user.email, "native-password")
        result = client.get("/api/mobile/v1/profile")
        assert set(result.json()) == {"user_id", "display_name", "email", "workday_start", "workday_end"}
        assert result.headers["cache-control"] == "no-store"
        assert client.put("/api/mobile/v1/profile", json={"display_name": "新昵称"}).status_code == 403
        updated = client.put("/api/mobile/v1/profile", json={"display_name": "新昵称", "email": "other@example.com"}, headers={"X-CSRF-Token": csrf})
        assert updated.status_code == 200 and updated.json()["display_name"] == "新昵称"
        assert updated.json()["email"] == user.email
        assert client.put("/api/mobile/v1/profile", json={"display_name": "\n"}, headers={"X-CSRF-Token": csrf}).status_code == 422


def test_native_public_batches_and_detail_do_not_expose_private_notes(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="native-plaza@example.com", display_name="同名也允许", password_hash=hash_password("native-password"), email_verified=True)
    mapper.create_user(user)
    notes = app.dependency_overrides[get_interview_experience_mapper]()
    public = notes.save_experience(InterviewExperience(owner_id=user.id, title="公开内容", company="示例公司", raw_text_draft="未提交的原文"))
    private = notes.save_experience(InterviewExperience(owner_id=user.id, title="私密内容", company="示例公司", is_public=False))
    missing = notes.save_experience(InterviewExperience(owner_id=user.id, title="未填写公司"))
    for item in (public, private, missing):
        notes.save_question(InterviewQuestion(experience_id=item.id, category="project", asked_order=1, question_text="项目介绍"), user.id)
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get("/api/mobile/v1/plaza", follow_redirects=False).status_code in {303, 401}
        _login(client, user.email, "native-password")
        result = client.get("/api/mobile/v1/plaza").json()
        assert result["total"] == 1 and result["items"][0]["id"] == public.id
        assert result["items"][0]["author"] == "同名也允许" and result["next_page"] is None
        assert "email" not in result["items"][0]
        assert client.get("/api/mobile/v1/plaza?company=不存在").json()["items"] == []
        detail = client.get(f"/api/mobile/v1/plaza/{public.id}").json()
        assert "raw_text_draft" not in detail["experience"] and len(detail["experience"]["questions"]) == 1
        assert client.get(f"/api/mobile/v1/plaza/{private.id}").status_code == 404
        assert client.get(f"/api/mobile/v1/plaza/{missing.id}").status_code == 404


def test_native_event_extended_fields_and_legacy_update_preservation(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="native-event@example.com", password_hash=hash_password("native-password"), email_verified=True)
    mapper.create_user(user)
    payload = {"title": "周末跳过", "event_type": "personal", "start_at": "2026-11-05T08:00:00+08:00", "end_at": "2026-11-09T18:00:00+08:00",
               "skip_weekends": True, "reference_links": [{"title": "教程", "url": "https://example.com/video"}]}
    with TestClient(app, base_url="https://testserver") as client:
        csrf = _login(client, user.email, "native-password")
        headers = {"X-CSRF-Token": csrf}
        created = client.post("/api/mobile/v1/events", json=payload, headers=headers)
        assert created.status_code == 201 and created.json()["skip_weekends"] is True
        event_id = created.json()["id"]
        legacy = {key: value for key, value in payload.items() if key not in {"skip_weekends", "reference_links"}}
        updated = client.put(f"/api/mobile/v1/events/{event_id}", json=legacy, headers=headers)
        assert updated.status_code == 200 and updated.json()["skip_weekends"] is True
        assert updated.json()["reference_links"][0]["title"] == "教程"
        client.post(f"/api/mobile/v1/events/{event_id}/completion", json={"completed": True}, headers=headers)
        # Existing recurrence/cancellation validation errors must remain controlled 422s.
        invalid = client.post("/api/mobile/v1/events", json={**payload, "title": "无效重复", "repeat_weekdays": [9]}, headers=headers)
        assert invalid.status_code == 422
