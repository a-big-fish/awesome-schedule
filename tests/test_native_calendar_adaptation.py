"""Native parity contracts use isolated users/storage; never connect to production."""
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo

from fastapi.testclient import TestClient

from app.core.dependencies import get_interview_experience_mapper
from app.model.auth import AuthUser
from app.model.interview_experience import InterviewExperience, InterviewQuestion
from app.service.auth_service import hash_password
from test_public_multiuser import _public_fixture, _login


def test_native_calendar_resource_and_idea_lifecycle(tmp_path):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    alice = AuthUser(email="native-alice@example.test", password_hash=hash_password("password"), email_verified=True)
    bob = AuthUser(email="native-bob@example.test", password_hash=hash_password("password"), email_verified=True)
    users.create_user(alice)
    users.create_user(bob)
    with TestClient(app, base_url="https://testserver") as a, TestClient(app, base_url="https://testserver") as b:
        assert a.get("/api/mobile/v1/calendar").status_code == 401
        ah = {"X-CSRF-Token": _login(a, alice.email, "password")}
        bh = {"X-CSRF-Token": _login(b, bob.email, "password")}
        idea = {"title": "原生想法", "note": "没有排期", "reference_links": [{"title": "资料", "url": "https://example.test/a"}]}
        assert a.post("/api/mobile/v1/ideas", json=idea).status_code == 403
        made = a.post("/api/mobile/v1/ideas", json=idea, headers=ah)
        assert made.status_code == 201
        iid = made.json()["id"]
        assert b.put(f"/api/mobile/v1/ideas/{iid}", json=idea, headers=bh).status_code == 404
        state = a.get("/api/mobile/v1/calendar").json()
        assert state["ideas"][0]["arranged_event_id"] is None and not state["events"]
        assert "experiences" not in state and state["profile"]["user_id"] == alice.id
        assert b.get("/api/mobile/v1/calendar").json()["ideas"] == []
        event = {"title": idea["title"], "event_type": "personal", "start_at": "2026-11-16T09:00:00+08:00",
                 "end_at": "2026-11-17T18:00:00+08:00", "source_idea_id": iid,
                 "workday_only": True, "skip_weekends": True, "reference_links": idea["reference_links"]}
        assert b.post("/api/mobile/v1/events", json=event, headers=bh).status_code == 404
        arranged = a.post("/api/mobile/v1/events", json=event, headers=ah)
        assert arranged.status_code == 201 and arranged.json()["workday_only"]
        again = a.post("/api/mobile/v1/events", json=event, headers=ah)
        assert again.json()["id"] == arranged.json()["id"]
        state = a.get("/api/mobile/v1/calendar").json()
        assert len(state["events"]) == 1 and state["ideas"][0]["arranged_event_id"] == arranged.json()["id"]
        # Legacy clients omit new fields: edits must preserve the display rule.
        legacy = {k: v for k, v in event.items() if k not in {"workday_only", "source_idea_id"}}
        assert a.put(f'/api/mobile/v1/events/{arranged.json()["id"]}', json=legacy, headers=ah).json()["workday_only"]
        assert a.post(f"/api/mobile/v1/ideas/{iid}/abandon", json={}, headers=ah).json()["status"] == "abandoned"
        assert a.post("/api/mobile/v1/events", json=event, headers=ah).status_code == 422


def test_native_overdue_repeat_and_explicit_todos(tmp_path):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="native-tasks@example.test", password_hash=hash_password("password"), email_verified=True)
    stranger = AuthUser(email="native-stranger@example.test", password_hash=hash_password("password"), email_verified=True)
    users.create_user(user)
    users.create_user(stranger)
    with TestClient(app, base_url="https://testserver") as client, TestClient(app, base_url="https://testserver") as other:
        headers = {"X-CSRF-Token": _login(client, user.email, "password")}
        other_headers = {"X-CSRF-Token": _login(other, stranger.email, "password")}
        now = datetime.now(ZoneInfo("Asia/Shanghai"))
        event = {"title": "过期任务", "event_type": "personal", "start_at": (now-timedelta(hours=3)).isoformat(),
                 "end_at": (now-timedelta(hours=2)).isoformat(), "reminder_offsets_minutes": [10]}
        result = client.post("/api/mobile/v1/events", json=event, headers=headers)
        eid = result.json()["id"]
        path = f"/api/mobile/v1/events/{eid}/overdue"
        assert client.post(path, json={"action": "defer", "end_at": (now-timedelta(minutes=1)).isoformat()}, headers=headers).status_code == 422
        assert other.post(path, json={"action": "cancel"}, headers=other_headers).status_code == 404
        assert client.post(path, json={"action": "defer", "end_at": (now+timedelta(hours=2)).isoformat()}, headers=headers).status_code == 200
        assert client.post(path, json={"action": "cancel"}, headers=headers).status_code == 422
        # A second, separate overdue fixture can be cancelled without modifying the deferred event.
        cancelled = client.post("/api/mobile/v1/events", json={**event, "title": "可废除", "confirm_overlap": True}, headers=headers).json()
        cancelled = client.post(f'/api/mobile/v1/events/{cancelled["id"]}/overdue', json={"action": "cancel"}, headers=headers).json()
        assert cancelled["cancelled_at"] and cancelled["reminder_offsets_minutes"] == []
        repeat = client.post("/api/mobile/v1/events", json={**event, "start_at": "2026-11-16T09:00:00+08:00", "end_at": "2026-11-16T10:00:00+08:00", "repeat_weekdays": [0, 2]}, headers=headers).json()
        close = f'/api/mobile/v1/events/{repeat["id"]}/close-repeat'
        assert other.post(close, json={}, headers=other_headers).status_code == 404
        assert client.post(close, json={}, headers=headers).json()["repeat_closed_on"]
        todo = client.put("/api/mobile/v1/todos", json={"title": "独立待办", "due_date": "2026-11-13"}, headers=headers).json()
        path = f'/api/mobile/v1/todos/{todo["id"]}/completion'
        assert other.post(path, json={"completed": True}, headers=other_headers).status_code == 404
        first = client.post(path, json={"completed": True}, headers=headers).json()
        second = client.post(path, json={"completed": True}, headers=headers).json()
        assert first["completed_at"] == second["completed_at"]
        assert client.post(path, json={"completed": False}, headers=headers).json()["completed_at"] is None
        assert client.delete(f'/api/mobile/v1/todos/{todo["id"]}', headers=headers).status_code == 204


def test_native_snapshot_batches_questions_and_calendar_skips_notes(tmp_path):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="native-batch@example.test", password_hash=hash_password("password"), email_verified=True)
    users.create_user(user)
    mapper = app.dependency_overrides[get_interview_experience_mapper]()
    for i in range(100):
        note = mapper.save_experience(InterviewExperience(owner_id=user.id, title=f"笔记{i}"))
        mapper.save_question(InterviewQuestion(experience_id=note.id, question_text="问题", category="project", asked_order=1), user.id)
    calls = []
    original = mapper.list_owned_questions
    def batched(owner):
        calls.append(owner)
        return original(owner)
    mapper.list_owned_questions = batched
    def forbidden(*args):
        raise AssertionError("per-note query or calendar note access")
    mapper.list_questions = forbidden
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, user.email, "password")
        result = client.get("/api/mobile/v1/snapshot")
        assert result.status_code == 200 and len(result.json()["experiences"]) == 100
        assert all(len(e["questions"]) == 1 for e in result.json()["experiences"])
        assert calls == [user.id]
        mapper.list_experiences = forbidden
        mapper.list_owned_questions = forbidden
        assert client.get("/api/mobile/v1/calendar").status_code == 200
