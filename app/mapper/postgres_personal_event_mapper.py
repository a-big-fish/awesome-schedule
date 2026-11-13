from typing import Callable

import psycopg
from psycopg.rows import DictRow, dict_row
from psycopg.types.json import Json

from app.core.performance import timed_postgres_connect
from app.core.config import Settings
from app.core.postgres import ensure_postgres_database
from app.mapper.base_personal_event_mapper import BasePersonalEventMapper, SyncApply, SyncResult
from app.model.personal_event import PersonalEvent


class _SyncConnection:
    """Keep nested mapper calls inside the outer receipt transaction."""
    def __init__(self, connection):
        self.connection = connection

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False

    def execute(self, *args, **kwargs):
        return self.connection.execute(*args, **kwargs)

    def commit(self):
        pass  # The event and receipt must commit together.


class PostgresPersonalEventMapper(BasePersonalEventMapper):
    def execute_sync_once(self, owner_id: str, operation_id: str, request_hash: str,
                          apply: SyncApply) -> SyncResult:
        with self._connect() as connection:
            # Serialize this owner's sync writes across workers, including lost-response retries.
            connection.execute("SELECT pg_advisory_xact_lock(hashtextextended(%s, 0))", (owner_id,))
            receipt = connection.execute(
                "SELECT request_hash, payload FROM personal_calendar_sync_receipts "
                "WHERE owner_id = %s AND operation_id = %s", (owner_id, operation_id),
            ).fetchone()
            if receipt:
                if receipt["request_hash"] != request_hash:
                    raise ValueError("同步操作标识已被用于其他内容")
                return receipt["payload"]
            bound = object.__new__(PostgresPersonalEventMapper)
            bound.settings = self.settings
            bound._connection_factory = lambda: _SyncConnection(connection)
            result = apply(bound)
            connection.execute(
                "INSERT INTO personal_calendar_sync_receipts (owner_id, operation_id, request_hash, payload) "
                "VALUES (%s, %s, %s, %s)", (owner_id, operation_id, request_hash, Json(result)),
            )
            connection.commit()
            return result

    def existing_ids(self, event_ids: list[str], owner_id: str) -> set[str]:
        if not event_ids:
            return set()
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT id FROM personal_schedule_events WHERE owner_id = %s AND id = ANY(%s)",
                (owner_id, event_ids),
            ).fetchall()
        return {row["id"] for row in rows}

    def __init__(
        self,
        settings: Settings | None = None,
        connection_factory: Callable[[], psycopg.Connection[DictRow]] | None = None,
    ) -> None:
        self.settings = settings
        self._connection_factory = connection_factory or self._default_connection
        if settings:
            ensure_postgres_database(settings)
        self._ensure_schema()

    def _default_connection(self) -> psycopg.Connection[DictRow]:
        if not self.settings:
            raise RuntimeError("PostgreSQL settings are required")
        return timed_postgres_connect(self.settings.postgres_dsn, row_factory=dict_row)

    def _connect(self) -> psycopg.Connection[DictRow]:
        return self._connection_factory()

    def _ensure_schema(self) -> None:
        with self._connect() as connection:
            connection.execute(
                "CREATE TABLE IF NOT EXISTS personal_schedule_events ("
                "id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, payload JSONB NOT NULL, "
                "start_at TIMESTAMPTZ NOT NULL, legacy_entry_id TEXT UNIQUE)"
            )
            connection.execute(
                "CREATE INDEX IF NOT EXISTS idx_personal_events_owner_start "
                "ON personal_schedule_events (owner_id, start_at)"
            )
            connection.execute(
                "CREATE TABLE IF NOT EXISTS personal_calendar_sync_receipts ("
                "owner_id TEXT NOT NULL, operation_id TEXT NOT NULL, request_hash TEXT NOT NULL, "
                "payload JSONB NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now(), "
                "PRIMARY KEY (owner_id, operation_id))"
            )
            connection.commit()

    def save(self, event: PersonalEvent) -> PersonalEvent:
        payload = event.model_dump(mode="json")
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO personal_schedule_events "
                "(id, owner_id, payload, start_at, legacy_entry_id) VALUES (%s, %s, %s, %s, %s) "
                "ON CONFLICT (id) DO UPDATE SET owner_id = EXCLUDED.owner_id, "
                "payload = EXCLUDED.payload, start_at = EXCLUDED.start_at, "
                "legacy_entry_id = EXCLUDED.legacy_entry_id",
                (event.id, event.owner_id, Json(payload), event.start_at, event.legacy_entry_id),
            )
            connection.commit()
        return event.model_copy(deep=True)

    def find_by_id(self, event_id: str, owner_id: str = "local") -> PersonalEvent | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT payload FROM personal_schedule_events WHERE id = %s AND owner_id = %s",
                (event_id, owner_id),
            ).fetchone()
        return PersonalEvent.model_validate(row["payload"]) if row else None

    def list_all(self, owner_id: str = "local") -> list[PersonalEvent]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT payload FROM personal_schedule_events WHERE owner_id = %s "
                "ORDER BY start_at, id",
                (owner_id,),
            ).fetchall()
        return [PersonalEvent.model_validate(row["payload"]) for row in rows]

    def delete(self, event_id: str, owner_id: str = "local") -> bool:
        with self._connect() as connection:
            cursor = connection.execute(
                "DELETE FROM personal_schedule_events WHERE id = %s AND owner_id = %s",
                (event_id, owner_id),
            )
            deleted = cursor.rowcount > 0
            connection.commit()
        return deleted

    def list_interview_options(self, owner_id: str = "local") -> list[dict]:
        with self._connect() as connection:
            return connection.execute(
                "SELECT id, payload->>'title' AS title, start_at "
                "FROM personal_schedule_events WHERE owner_id = %s "
                "AND payload->>'type' = 'interview' ORDER BY start_at, id",
                (owner_id,),
            ).fetchall()
