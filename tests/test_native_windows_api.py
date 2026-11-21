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
        headers = {"X-CSRF-Token": csrf}
        timed = client.put("/api/mobile/v1/profile", json={"display_name": "新昵称", "workday_start": "09:30", "workday_end": "19:00"}, headers=headers)
        assert timed.status_code == 200 and timed.json()["workday_start"] == "09:30"
        preserved = client.put("/api/mobile/v1/profile", json={"display_name": "保留时段"}, headers=headers)
        assert preserved.json()["workday_end"] == "19:00"
        for start, end in (("bad", "18:00"), ("19:00", "08:00"), ("08:00", "08:00")):
            assert client.put("/api/mobile/v1/profile", json={"display_name": "新昵称", "workday_start": start, "workday_end": end}, headers=headers).status_code == 422
        assert client.get("/api/mobile/v1/profile").json()["workday_start"] == "09:30"
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
        defaulted = client.post("/api/mobile/v1/events", json={**legacy, "title": "保持默认跳过周末", "confirm_overlap": True}, headers=headers)
        assert defaulted.status_code == 201 and defaulted.json()["skip_weekends"] is True
        client.post(f"/api/mobile/v1/events/{event_id}/completion", json={"completed": True}, headers=headers)
        # Existing recurrence/cancellation validation errors must remain controlled 422s.
        invalid = client.post("/api/mobile/v1/events", json={**payload, "title": "无效重复", "repeat_weekdays": [9]}, headers=headers)
        assert invalid.status_code == 422


def test_native_company_search_matches_substrings_only_in_public_companies(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="search@example.com", password_hash=hash_password("native-password"), email_verified=True)
    mapper.create_user(user)
    notes = app.dependency_overrides[get_interview_experience_mapper]()
    for company, public in (("抖音", True), ("音符科技", True), ("其他公司", True), ("音频私密公司", False)):
        item = notes.save_experience(InterviewExperience(owner_id=user.id, title="测试", company=company, is_public=public))
        notes.save_question(InterviewQuestion(experience_id=item.id, category="project", asked_order=1, question_text="项目介绍"), user.id)
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, user.email, "native-password")
        result = client.get("/api/mobile/v1/plaza", params={"company": "音"}).json()
        assert result["total"] == 2
        assert {item["company"] for item in result["items"]} == {"抖音", "音符科技"}


def test_native_owned_experience_page_filters_clamps_and_is_owner_scoped(tmp_path):
    """The native list reuses the Web summary rules without exposing drafts or other owners."""
    app, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="native-list@example.com", password_hash=hash_password("native-password"), email_verified=True)
    other = AuthUser(email="native-list-other@example.com", password_hash=hash_password("native-password"), email_verified=True)
    mapper.create_user(user)
    mapper.create_user(other)
    notes = app.dependency_overrides[get_interview_experience_mapper]()
    created = []
    for index in range(22):
        item = notes.save_experience(InterviewExperience(
            owner_id=user.id, title=f"面经{index:02d}",
            company="顺丰" if index == 0 else "示例公司", position="Java开发",
            is_public=index % 2 == 0))
        created.append(item)
    notes.save_question(InterviewQuestion(experience_id=created[-1].id, category="project", asked_order=1,
                                          question_text="只应计数，不返回正文"), user.id)
    notes.save_experience(InterviewExperience(owner_id=other.id, title="别人的面经", company="别家公司"))
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get("/api/mobile/v1/experiences", follow_redirects=False).status_code in {303, 401}
        csrf = _login(client, user.email, "native-password")
        first = client.get("/api/mobile/v1/experiences")
        assert first.headers["cache-control"] == "no-store"
        body = first.json()
        assert body["total"] == 22 and body["page"] == 1 and body["page_size"] == 20 and body["has_more"] is True
        assert len(body["items"]) == 20
        assert set(body["items"][0]) == {"id", "title", "company", "position", "interview_round",
                                         "is_public", "updated_at", "question_count"}
        assert all("raw_text_draft" not in item and "questions" not in item for item in body["items"])
        counted = next(item for item in body["items"] if item["id"] == created[-1].id)
        assert counted["question_count"] == 1
        second = client.get("/api/mobile/v1/experiences?page=2").json()
        assert len(second["items"]) == 2 and second["has_more"] is False
        clamped = client.get("/api/mobile/v1/experiences?page=99").json()
        assert clamped["page"] == 2 and len(clamped["items"]) == 2
        assert client.get("/api/mobile/v1/experiences?page=0").json()["page"] == 1
        searched = client.get("/api/mobile/v1/experiences", params={"q": "顺丰"}).json()
        assert searched["total"] == 1 and searched["items"][0]["id"] == created[0].id
        assert client.get("/api/mobile/v1/experiences", params={"q": "Java开发"}).json()["total"] == 22
        assert client.get("/api/mobile/v1/experiences", params={"q": "不存在"}).json()["items"] == []
        public_only = client.get("/api/mobile/v1/experiences", params={"visibility": "public"}).json()
        private_only = client.get("/api/mobile/v1/experiences", params={"visibility": "private"}).json()
        assert public_only["total"] == 11 and private_only["total"] == 11
        assert all(item["is_public"] for item in public_only["items"])
        assert not any(item["is_public"] for item in private_only["items"])
        oldest = client.get("/api/mobile/v1/experiences", params={"order": "oldest"}).json()
        newest = client.get("/api/mobile/v1/experiences", params={"order": "newest"}).json()
        assert oldest["items"][0]["id"] != newest["items"][0]["id"]
        combined = client.get("/api/mobile/v1/experiences",
                              params={"q": "示例公司", "visibility": "private", "order": "oldest", "page": 2}).json()
        assert combined["total"] == 11 and combined["page"] == 1 and len(combined["items"]) == 11
        # A second account only sees its own page even though the first one owns 22 notes.
        client.post("/auth/logout", data={"csrf_token": csrf})
        _login(client, other.email, "native-password")
        other_page = client.get("/api/mobile/v1/experiences").json()
        assert other_page["total"] == 1 and other_page["items"][0]["title"] == "别人的面经"
