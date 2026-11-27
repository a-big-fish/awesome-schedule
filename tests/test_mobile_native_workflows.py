import re
from datetime import date, datetime, timedelta, timezone
from uuid import NAMESPACE_URL, uuid4, uuid5

from fastapi.testclient import TestClient

from app.model.auth import AuthUser
from app.model.interview_experience import InterviewExperience
from app.core.dependencies import get_interview_experience_mapper
from app.service.auth_service import hash_password
from test_public_multiuser import _login, _public_fixture


def _user(mapper, label):
    user = AuthUser(email=f"{label}@example.com", password_hash=hash_password("native-test-password"),
                    email_verified=True)
    mapper.create_user(user)
    return user


def test_native_outbox_replays_todo_receipt_without_recreating_removed_todo(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = _user(mapper, "todo-outbox")
    with TestClient(app, base_url="https://testserver") as client:
        headers = {"X-CSRF-Token": _login(client, user.email, "native-test-password")}
        operation = uuid4()
        todo_id = str(uuid5(NAMESPACE_URL, f"todo:{user.id}:{operation}"))
        command = {"action": "create", "todo_id": todo_id, "title": "合成待办", "due_date": date.today().isoformat()}
        url = f"/api/mobile/v1/todo-sync/{operation}"
        created = client.post(url, json=command, headers=headers)
        assert created.status_code == 200, created.text
        assert created.json()["todo"]["id"] == todo_id
        assert client.post(f"/api/mobile/v1/todo-sync/{uuid4()}",
                           json={"action": "delete", "todo_id": todo_id}, headers=headers).status_code == 200
        assert client.post(url, json=command, headers=headers).json() == created.json()
        assert client.post(url, json={**command, "title": "变更"}, headers=headers).status_code == 422
        assert client.get("/api/mobile/v1/calendar").json()["todos"] == []


def test_native_login_and_experience_edit_remain_owner_scoped(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    writer = _user(mapper, "native-writer")
    reader = _user(mapper, "native-reader")
    with TestClient(app, base_url="https://testserver") as client:
        accept = {"Accept": "application/json"}
        config = client.get("/auth/form-config", headers=accept).json()
        signed_in = client.post("/auth/login", headers=accept, data={
            "csrf_token": config["csrf_token"], "identifier": writer.email, "password": "native-test-password",
        })
        assert signed_in.status_code == 200 and signed_in.json()["next"] == "/my-schedule"
        headers = {"X-CSRF-Token": client.get("/api/mobile/v1/session").json()["csrf_token"]}
        created = client.post("/api/mobile/v1/experiences", json={"title": "合成面经"}, headers=headers)
        assert created.status_code == 201, created.text
        experience_id = created.json()["experience"]["id"]
        detail_url = f"/api/mobile/v1/experiences/{experience_id}"
        updated = client.put(detail_url, json={"title": "更新后的合成面经"}, headers=headers)
        assert updated.status_code == 200 and not updated.json()["experience"]["is_public"]
        assert client.get(detail_url).json()["questions"] == []
        client.cookies.clear()
        assert client.get(detail_url).status_code == 401
        headers = {"X-CSRF-Token": _login(client, reader.email, "native-test-password")}
        assert client.get(detail_url).status_code == 404
        assert client.put(detail_url, json={"title": "其他账号"}, headers=headers).status_code == 404


def test_native_experience_pages_hold_100_notes_and_match_web_filter(tmp_path):
    app, mapper, *_ = _public_fixture(tmp_path)
    user = _user(mapper, "native-page")
    experiences = app.dependency_overrides[get_interview_experience_mapper]()
    for i in range(100):
        experiences.save_experience(InterviewExperience(
            owner_id=user.id, title=f"合成面经 {i:03d}", company="示例公司",
            position="后端开发", is_public=i % 2 == 0,
            updated_at=datetime(2026, 11, 1, tzinfo=timezone.utc) + timedelta(minutes=i),
        ))
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, user.email, "native-test-password")
        url = "/api/mobile/v1/experiences"
        ids = []
        for page in range(1, 6):
            response = client.get(url, params={"page": page}).json()
            assert response["total"] == 100 and response["page_count"] == 5
            assert len(response["items"]) == 20
            assert response["next_page"] == (page + 1 if page < 5 else None)
            ids.extend(row["id"] for row in response["items"])
        assert len(set(ids)) == 100
        assert client.get(url, params={"page": 999}).json()["page"] == 5
        for field in ("合成面经", "示例公司", "后端开发"):
            params = {"q": field, "visibility": "private", "order": "oldest"}
            filtered = client.get(url, params=params).json()
            assert filtered["total"] == 50
            assert filtered["items"][0]["title"] == "合成面经 001"
            web = client.get("/my-schedule/experiences", params=params)
            web_ids = re.findall(r'class="experience-list-item" href="/my-schedule/experiences/([^"]+)"', web.text)
            assert web_ids == [row["id"] for row in filtered["items"]]
        assert client.get(url, params={"q": "未知关键字"}).json()["items"] == []
