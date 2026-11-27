from unittest.mock import Mock

from fastapi.testclient import TestClient
from psycopg.errors import ConnectionTimeout

from app.core.config import Settings
from app.main import create_app
from app.mapper import postgres_auth_mapper
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _login, _public_fixture


def test_unreachable_database_does_not_prevent_startup(monkeypatch, tmp_path):
    bootstrap = Mock(side_effect=ConnectionTimeout("offline"))
    monkeypatch.setattr(postgres_auth_mapper, "ensure_postgres_database", bootstrap)
    settings = Settings(_env_file=None, app_mode="public_personal", storage_backend="postgres",
                        public_base_url="https://testserver", registration_mode="closed",
                        performance_enabled=False, log_dir=str(tmp_path))
    app = create_app(settings)
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get("/static/js/personal_database_status.js").status_code == 200
        bootstrap.assert_not_called()
        client.cookies.set("schedule_session", "outage-test-token")
        assert client.get("/api/mobile/v1/session").status_code == 503
        bootstrap.assert_called_once_with(settings)


def test_database_timeout_serves_a_cache_shell_and_leaves_session_recoverable(tmp_path, monkeypatch):
    app, users, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="database-test@example.com", password_hash=hash_password("test-password"),
                    email_verified=True)
    users.create_user(user)
    with TestClient(app, base_url="https://testserver") as client:
        csrf = _login(client, user.email, "test-password")
        html = {"Accept": "text/html"}
        signed_in = client.get("/my-schedule", headers=html)
        session_meta = next(line for line in signed_in.text.splitlines() if 'name="calendar-session-key"' in line)
        assert user.id in signed_in.text
        with monkeypatch.context() as down:
            down.setattr(users, "authenticate_session", Mock(side_effect=ConnectionTimeout("offline")))
            fallback = client.get("/my-schedule?view=week", headers=html)
            assert fallback.status_code == 503
            assert fallback.headers["Cache-Control"] == "no-store"
            assert session_meta in fallback.text
            assert "数据库连接超时" in fallback.text
            assert 'id="local-calendar-template"' in fallback.text
            assert 'name="calendar-user"' not in fallback.text
            assert user.id not in fallback.text and csrf not in fallback.text
            api = client.get("/my-schedule/calendar-data?format=json",
                             headers={"Accept": "application/json"})
            assert api.status_code == 503 and api.json()["detail"].startswith("数据库连接超时")
            assert not fallback.headers.get("Set-Cookie")
        assert client.get("/api/mobile/v1/session").json()["csrf_token"] == csrf
        assert client.get("/my-schedule", headers=html).status_code == 200
