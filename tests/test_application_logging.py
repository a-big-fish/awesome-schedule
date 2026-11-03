import logging

from fastapi.testclient import TestClient
from psycopg import OperationalError

from app.core.config import Settings
from app.core.logging_setup import ApplicationLogs
from app.main import create_app


def test_files_split_errors_and_strip_access_query_and_exception_values(tmp_path):
    logs = ApplicationLogs(str(tmp_path), 100000, 2)
    logs.start()
    try:
        logging.getLogger("app.test").info("ordinary-operation")
        logging.getLogger("app.test").warning("slow-only")
        logging.getLogger("uvicorn.access").info('%s - "%s %s HTTP/%s" %d',
                                                "localhost", "GET", "/example?token=private", "1.1", 503)
        try:
            raise ValueError("secret-exception-value")
        except ValueError:
            logging.getLogger("app.test").exception("failed-operation")
    finally:
        logs.stop()
    full = (tmp_path / "app.log").read_text(encoding="utf-8")
    errors = (tmp_path / "error.log").read_text(encoding="utf-8")
    assert "ordinary-operation" in full and "slow-only" in full
    assert "ordinary-operation" not in errors and "slow-only" not in errors
    assert "failed-operation" in errors and "ValueError" in errors
    assert 'GET /example HTTP' in errors
    assert "token=private" not in full and "secret-exception-value" not in full
    assert full.count("ordinary-operation") == 1


def test_request_503_and_unhandled_error_have_safe_traceable_logs(tmp_path):
    application = create_app(Settings(app_mode="local", storage_backend="memory", log_dir=str(tmp_path)))

    @application.get("/test/db")
    def unavailable():
        raise OperationalError("do-not-log-database-password")

    @application.get("/test/failure")
    def broken():
        raise ValueError("do-not-log-form-body")

    with TestClient(application, raise_server_exceptions=False) as client:
        response = client.get("/test/db?private=value")
        assert response.status_code == 503
        assert response.headers["X-Request-ID"]
        assert client.get("/test/failure").status_code == 500
        assert client.get("/robots.txt").status_code == 200
    full = (tmp_path / "app.log").read_text(encoding="utf-8")
    errors = (tmp_path / "error.log").read_text(encoding="utf-8")
    assert "status=503" in errors and "status=500" in errors
    assert response.headers["X-Request-ID"] in errors
    assert "route=/robots.txt status=200" in full
    assert "route=/robots.txt status=200" not in errors
    assert "do-not-log" not in errors and "private=value" not in errors


def test_log_rotation_and_restart_remove_handlers(tmp_path):
    root = logging.getLogger()
    before = list(root.handlers)
    for _ in range(2):
        logs = ApplicationLogs(str(tmp_path), 1024, 2)
        logs.start()
        for number in range(30):
            logging.getLogger("app.test").error("rotation-%s %s", number, "x" * 100)
        logs.stop()
        assert root.handlers == before
    assert (tmp_path / "app.log.1").exists()
    assert (tmp_path / "error.log.1").exists()
    assert not (tmp_path / "error.log.3").exists()
