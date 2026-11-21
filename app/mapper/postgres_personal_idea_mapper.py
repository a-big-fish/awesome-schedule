from typing import Callable

import psycopg
from psycopg.rows import DictRow, dict_row
from psycopg.types.json import Json

from app.core.performance import timed_postgres_connect
from app.core.config import Settings
from app.core.postgres import ensure_postgres_database
from app.mapper.base_personal_idea_mapper import BasePersonalIdeaMapper
from app.model.personal_idea import PersonalIdea
from app.mapper.postgres_personal_event_mapper import _SyncConnection


class PostgresPersonalIdeaMapper(BasePersonalIdeaMapper):
    def __init__(self, settings: Settings | None = None,
                 connection_factory: Callable[[], psycopg.Connection[DictRow]] | None = None) -> None:
        self.settings = settings
        self._connection_factory = connection_factory or self._default_connection
        if settings:
            ensure_postgres_database(settings)
        with self._connect() as connection:
            connection.execute(
                "CREATE TABLE IF NOT EXISTS personal_ideas ("
                "id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL, "
                "payload JSONB NOT NULL)"
            )
            connection.execute(
                "CREATE INDEX IF NOT EXISTS idx_personal_ideas_owner_created "
                "ON personal_ideas (owner_id, created_at)"
            )
            connection.execute(
                "CREATE TABLE IF NOT EXISTS personal_idea_sync_receipts ("
                "owner_id TEXT NOT NULL, operation_id TEXT NOT NULL, request_hash TEXT NOT NULL, "
                "payload JSONB NOT NULL, PRIMARY KEY (owner_id, operation_id))"
            )
            connection.commit()

    def execute_sync_once(self, owner_id: str, operation_id: str, request_hash: str,
                          apply: Callable[[BasePersonalIdeaMapper], dict]) -> dict:
        """Keep the write and receipt in one transaction, including across workers."""
        with self._connect() as connection:
            connection.execute("SELECT pg_advisory_xact_lock(hashtextextended(%s, 0))", (owner_id,))
            receipt = connection.execute(
                "SELECT request_hash, payload FROM personal_idea_sync_receipts "
                "WHERE owner_id=%s AND operation_id=%s", (owner_id, operation_id),
            ).fetchone()
            if receipt:
                if receipt["request_hash"] != request_hash:
                    raise ValueError("同步操作标识已被用于其他内容")
                return receipt["payload"]
            bound = object.__new__(PostgresPersonalIdeaMapper)
            bound._connection_factory = lambda: _SyncConnection(connection)
            result = apply(bound)
            connection.execute(
                "INSERT INTO personal_idea_sync_receipts VALUES (%s,%s,%s,%s)",
                (owner_id, operation_id, request_hash, Json(result)),
            )
            connection.commit()
            return result

    def _default_connection(self) -> psycopg.Connection[DictRow]:
        if not self.settings:
            raise RuntimeError("PostgreSQL settings are required")
        return timed_postgres_connect(self.settings.postgres_dsn, row_factory=dict_row)

    def _connect(self) -> psycopg.Connection[DictRow]:
        return self._connection_factory()

    def save(self, idea: PersonalIdea) -> PersonalIdea:
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO personal_ideas (id, owner_id, created_at, payload) "
                "VALUES (%s, %s, %s, %s) ON CONFLICT (id) DO UPDATE SET "
                "payload = EXCLUDED.payload WHERE personal_ideas.owner_id = EXCLUDED.owner_id",
                (idea.id, idea.owner_id, idea.created_at, Json(idea.model_dump(mode="json"))),
            )
            connection.commit()
        return idea.model_copy(deep=True)

    def find_by_id(self, idea_id: str, owner_id: str) -> PersonalIdea | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT payload FROM personal_ideas WHERE id = %s AND owner_id = %s",
                (idea_id, owner_id),
            ).fetchone()
        return PersonalIdea.model_validate(row["payload"]) if row else None

    def list_all(self, owner_id: str) -> list[PersonalIdea]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT payload FROM personal_ideas WHERE owner_id = %s ORDER BY created_at, id",
                (owner_id,),
            ).fetchall()
        return [PersonalIdea.model_validate(row["payload"]) for row in rows]

    def delete(self, idea_id: str, owner_id: str) -> bool:
        with self._connect() as connection:
            cursor = connection.execute(
                "DELETE FROM personal_ideas WHERE id = %s AND owner_id = %s",
                (idea_id, owner_id),
            )
            connection.commit()
        return cursor.rowcount > 0
