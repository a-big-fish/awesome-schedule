"""One read request keeps one pooled connection; writes keep borrowing their own."""

import functools
from unittest.mock import MagicMock

import pytest

from app.core import postgres_pool
from app.core.postgres_pool import SharedScope, connection, shared_connection

DSN = "postgresql://user:secret@db.local:5432/interview-system"


class FakePool:
    def __init__(self):
        self.borrows = 0
        self.scopes: list[MagicMock] = []

    def connection(self, timeout=3):
        self.borrows += 1
        scope = MagicMock()
        scope.__enter__ = MagicMock(return_value=MagicMock(name=f"raw-{self.borrows}"))
        scope.__exit__ = MagicMock(return_value=False)
        self.scopes.append(scope)
        return scope


@pytest.fixture
def fake_pool(monkeypatch):
    pool = FakePool()
    monkeypatch.setattr(postgres_pool, "get_pool", lambda *args, **kwargs: pool)
    return pool


def test_one_read_request_borrows_a_single_connection_for_every_query(fake_pool):
    token = shared_connection.set(SharedScope())
    try:
        with connection(DSN, row_factory=dict) as first:
            first.execute("SELECT payload FROM personal_ideas")
        with connection(DSN, row_factory=dict) as second:
            second.execute("SELECT id FROM personal_schedule_events")
        assert fake_pool.borrows == 1
        assert second is first
        assert fake_pool.scopes[0].__exit__.call_count == 0  # released only when the request ends
        shared_connection.get().close()
        assert fake_pool.scopes[0].__exit__.call_count == 1
    finally:
        shared_connection.reset(token)


def test_a_failed_query_keeps_the_shared_connection_for_the_rest_of_the_request(fake_pool):
    token = shared_connection.set(SharedScope())
    try:
        with pytest.raises(RuntimeError):
            with connection(DSN, row_factory=dict):
                raise RuntimeError("query failed")
        with connection(DSN, row_factory=dict) as recovered:
            recovered.execute("SELECT 1")
        assert fake_pool.borrows == 1
        assert fake_pool.scopes[0].__exit__.call_count == 0
        shared_connection.get().close()
    finally:
        shared_connection.reset(token)


def test_a_connection_is_never_shared_across_pool_shapes(fake_pool):
    token = shared_connection.set(SharedScope())
    try:
        with connection(DSN, row_factory=dict) as first:
            first.execute("SELECT 1")
        with connection(DSN, row_factory=tuple) as other:
            other.execute("SELECT 1")
        assert fake_pool.borrows == 2 and other is not first
        shared_connection.get().close()
    finally:
        shared_connection.reset(token)


def test_without_a_request_scope_every_borrow_is_released_immediately(fake_pool):
    with connection(DSN, row_factory=dict) as borrowed:
        borrowed.execute("SELECT 1")
    assert fake_pool.borrows == 1
    assert fake_pool.scopes[0].__exit__.call_count == 1


def test_write_requests_keep_their_own_connection(fake_pool):
    """A POST never sets the scope, so each mapper block still borrows and releases."""
    assert shared_connection.get() is None
    with connection(DSN, row_factory=dict) as borrowed:
        borrowed.execute("INSERT INTO personal_schedule_events VALUES (1)")
    with connection(DSN, row_factory=dict) as again:
        again.execute("INSERT INTO personal_schedule_events VALUES (2)")
    assert fake_pool.borrows == 2 and again is not borrowed


def test_releasing_before_a_slow_phase_makes_the_next_query_borrow_again(fake_pool):
    token = shared_connection.set(SharedScope())
    try:
        with connection(DSN, row_factory=dict) as first:
            first.execute("SELECT 1")
        assert fake_pool.borrows == 1
        postgres_pool.release_shared_connection()
        assert fake_pool.scopes[0].__exit__.call_count == 1
        with connection(DSN, row_factory=dict) as again:
            again.execute("SELECT 1")
        assert fake_pool.borrows == 2 and again is not first
    finally:
        shared_connection.reset(token)


def test_page_rendering_releases_the_connection_before_the_template_runs(monkeypatch):
    from starlette.requests import Request
    import app.controller.page_controller as pages
    released = []
    monkeypatch.setattr(pages, "release_shared_connection", lambda: released.append(True))
    request = Request({"type": "http", "method": "GET", "path": "/my-schedule", "headers": [],
                       "query_string": b"", "scheme": "https", "server": ("testserver", 443), "app": None})
    pages.templates.TemplateResponse(request, "_personal_idea_bubble.html", {})
    assert released == [True]


def test_schema_warm_up_builds_every_cached_mapper(monkeypatch):
    import app.main as main
    calls = []
    for name in ("get_personal_event_mapper", "get_personal_idea_mapper",
                 "get_my_resume_mapper", "get_interview_experience_mapper"):
        monkeypatch.setattr(main, name, functools.partial(calls.append, name))
    main._warm_metadata_schema()
    assert calls == ["get_personal_event_mapper", "get_personal_idea_mapper",
                     "get_my_resume_mapper", "get_interview_experience_mapper"]


def test_tests_do_not_warm_the_deployment_schema(monkeypatch, tmp_path):
    """create_app(settings) is the test path; only create_app() prepares the real database."""
    import app.main as main
    from app.core.config import Settings
    from app.mapper.memory_auth_mapper import MemoryAuthMapper
    from app.service.auth_service import AuthService

    calls = []
    monkeypatch.setattr(main, "_warm_metadata_schema", lambda: calls.append("warm"))
    settings = Settings(_env_file=None, app_mode="public_personal", storage_backend="postgres",
                        public_base_url="https://testserver", registration_mode="closed",
                        performance_enabled=False, log_dir=str(tmp_path))
    app = main.create_app(settings, AuthService(MemoryAuthMapper(), settings))
    from fastapi.testclient import TestClient
    with TestClient(app, base_url="https://testserver"):
        pass
    assert calls == []
