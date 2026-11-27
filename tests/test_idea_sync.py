"""Private local-first bootstrap and replay-safe idea writes."""
from uuid import NAMESPACE_URL, uuid4, uuid5
from unittest.mock import MagicMock

from fastapi.testclient import TestClient

from app.core.dependencies import get_personal_idea_mapper
from app.mapper.postgres_personal_idea_mapper import PostgresPersonalIdeaMapper
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _login, _public_fixture


def test_idea_sync_replay_preserves_later_edit_and_abandon(tmp_path):
    app, users, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="idea-sync@example.test", password_hash=hash_password("test-pass"), email_verified=True)
    users.create_user(user)
    mapper = app.dependency_overrides[get_personal_idea_mapper]()
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get("/my-schedule/ideas-data", follow_redirects=False).status_code == 303
        csrf = _login(client, user.email, "test-pass")
        headers = {"X-CSRF-Token": csrf}
        operation = str(uuid4())
        idea_id = str(uuid5(NAMESPACE_URL, f"idea-create:{user.id}:{operation}"))
        payload = {"action": "create", "idea_id": idea_id, "title": "合成想法", "note": "合成备注",
                   "reference_links": [{"title": "文档", "url": "https://example.test/docs"}]}
        url = f"/my-schedule/idea-sync/{operation}"
        assert client.post(url, json=payload).status_code == 403
        created = client.post(url, json=payload, headers=headers)
        assert created.status_code == 200
        assert created.json()["idea"]["id"] == idea_id
        assert "owner_id" not in created.json()["idea"]
        edited = {**payload, "action": "edit", "title": "修改后的合成想法"}
        assert client.post(f"/my-schedule/idea-sync/{uuid4()}", json=edited, headers=headers).status_code == 200
        assert client.post(url, json=payload, headers=headers).json() == created.json()
        assert mapper.find_by_id(idea_id, user.id).title == edited["title"]
        assert client.post(url, json=edited, headers=headers).status_code == 422
        assert client.post(f"/my-schedule/idea-sync/{uuid4()}", json={"action":"abandon", "idea_id":idea_id}, headers=headers).status_code == 200
        data = client.get("/my-schedule/ideas-data").json()
        assert data["user_id"] == user.id and len(data["ideas"]) == 1
        assert data["ideas"][0]["status"] == "abandoned"
        assert "data-idea-form" in data["shell"] and "personal-event-form" in data["arrange_shell"]
        local = client.get("/my-schedule/ideas-data?format=json").json()
        assert local["ideas"] == data["ideas"]
        assert not {"shell", "arrange_shell"} & local.keys()
        assert client.post(f"/my-schedule/idea-sync/{uuid4()}", json=edited, headers=headers).status_code == 422
        other = AuthUser(email="other-sync@example.test", password_hash=hash_password("test-pass"), email_verified=True)
        users.create_user(other)
        client.post("/auth/logout", data={"csrf_token":csrf})
        other_csrf = _login(client, other.email, "test-pass")
        assert client.get("/my-schedule/ideas-data").json()["ideas"] == []
        assert client.post(f"/my-schedule/idea-sync/{uuid4()}", json=edited, headers={"X-CSRF-Token":other_csrf}).status_code == 404


def test_postgres_idea_write_and_receipt_share_one_commit():
    connection = MagicMock()
    connection.__enter__.return_value = connection
    connection.execute.return_value.fetchone.return_value = None
    mapper = object.__new__(PostgresPersonalIdeaMapper)
    mapper._connection_factory = lambda: connection

    def apply(bound):
        with bound._connect() as nested:
            nested.execute("UPDATE fixture SET value=1")
            nested.commit()
        return {"operation_id": "test"}

    assert mapper.execute_sync_once("owner", "test", "digest", apply) == {"operation_id": "test"}
    assert connection.commit.call_count == 1
    assert "personal_idea_sync_receipts" in connection.execute.call_args.args[0]
