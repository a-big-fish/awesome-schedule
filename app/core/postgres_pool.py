"""Small process-wide PostgreSQL pool shared by all metadata mappers."""

import atexit
from contextlib import contextmanager
from contextvars import ContextVar
from dataclasses import dataclass
from threading import Lock

from psycopg.conninfo import make_conninfo
from psycopg_pool import ConnectionPool

_pools: dict[tuple, ConnectionPool] = {}
_lock = Lock()


@dataclass
class SharedScope:
    """One pooled connection reused by the read queries of a single HTTP request."""
    key: tuple | None = None
    connection: object | None = None
    scope: object | None = None

    def close(self) -> None:
        """Give the connection back; an unfinished read transaction is rolled back by the pool."""
        scope, self.scope = self.scope, None
        self.connection = None
        self.key = None
        if scope is not None:
            try:
                scope.__exit__(None, None, None)
            except Exception:  # noqa: BLE001 - a dirty release must not break the response
                pass


shared_connection: ContextVar[SharedScope | None] = ContextVar("shared_connection", default=None)


def release_shared_connection() -> None:
    """Return the request's connection before a long non-database phase (render, storage, export).

    The scope stays in place, so a later query in the same request simply borrows again.
    """
    shared = shared_connection.get()
    if shared is not None:
        shared.close()


def get_pool(conninfo: str, **kwargs) -> ConnectionPool:
    # Physical connection establishment and pool acquisition both fail within 3s.
    conninfo = make_conninfo(conninfo, connect_timeout=3)
    key = (conninfo, tuple(sorted(kwargs.items())))
    with _lock:
        pool = _pools.get(key)
        if pool is None or pool.closed:
            pool = ConnectionPool(conninfo, kwargs=kwargs, min_size=3, max_size=3,
                                  timeout=3, max_lifetime=20 * 60, max_idle=10 * 60,
                                  check=ConnectionPool.check_connection,
                                  open=True, name="metadata")
            _pools[key] = pool
        return pool


@contextmanager
def connection(conninfo: str, **kwargs):
    from app.core.performance import _TimedDatabase, measure_phase, request_timings
    # Keyed on the mapper's own arguments: every mapper reuses the deployment DSN verbatim.
    key = (conninfo, tuple(sorted(kwargs.items())))
    shared = shared_connection.get()
    if shared is not None and (shared.connection is None or shared.key == key):
        # Reuse one borrow per request: each mapper still commits or rolls back its own block,
        # but the borrow, validation and release happen once instead of once per query.
        if shared.connection is None:
            scope = get_pool(conninfo, **kwargs).connection(timeout=3)
            with measure_phase("db_pool_wait"):
                raw = scope.__enter__()
            shared.key, shared.scope = key, scope
            shared.connection = _TimedDatabase(raw) if request_timings.get() is not None else raw
        yield shared.connection
        return
    scope = get_pool(conninfo, **kwargs).connection(timeout=3)
    with measure_phase("db_pool_wait"):
        raw = scope.__enter__()
    try:
        yield _TimedDatabase(raw) if request_timings.get() is not None else raw
    except BaseException:
        import sys
        with measure_phase("db_query"):
            suppressed = scope.__exit__(*sys.exc_info())
        if not suppressed:
            raise
    else:
        with measure_phase("db_query"):
            scope.__exit__(None, None, None)


def close_pools() -> None:
    with _lock:
        pools = list(_pools.values())
        _pools.clear()
    for pool in pools:
        pool.close(timeout=3)


atexit.register(close_pools)
