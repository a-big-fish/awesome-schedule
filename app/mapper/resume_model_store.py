"""Local, account-scoped storage for resume review model credentials."""

import os
import sqlite3
from contextlib import closing
from dataclasses import dataclass
from pathlib import Path
from tempfile import NamedTemporaryFile
from uuid import uuid4

from cryptography.fernet import Fernet
from pydantic import SecretStr

from app.model.ai_interview import AIModelConfig


@dataclass(frozen=True)
class ResumeModel:
    id: str
    name: str
    model: str


class ResumeModelStore:
    """Encrypt API keys with a separate, server-local key file."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.key_path = path.with_suffix(".key")

    def _connect(self) -> sqlite3.Connection:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        connection = sqlite3.connect(self.path)
        try:
            connection.execute(
                """CREATE TABLE IF NOT EXISTS resume_models (
                    id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, name TEXT NOT NULL,
                    base_url TEXT NOT NULL, model TEXT NOT NULL, api_key BLOB NOT NULL
                )"""
            )
        except BaseException:
            connection.close()
            raise
        return connection

    def list(self, owner_id: str) -> list[ResumeModel]:
        with closing(self._connect()) as db:
            return [
                ResumeModel(*row) for row in db.execute(
                    "SELECT id, name, model FROM resume_models WHERE owner_id = ? ORDER BY rowid",
                    (owner_id,),
                )
            ]

    def save(self, owner_id: str, name: str, base_url: str, model: str, api_key: str) -> ResumeModel:
        entry = ResumeModel(uuid4().hex, name or model, model)
        with closing(self._connect()) as db, db:
            db.execute("BEGIN IMMEDIATE")
            if not self.key_path.exists():
                if db.execute("SELECT 1 FROM resume_models LIMIT 1").fetchone():
                    raise FileNotFoundError("Resume model encryption key is missing")
                with NamedTemporaryFile(dir=self.path.parent, delete=False) as pending_key:
                    pending_key.write(Fernet.generate_key())
                try:
                    os.replace(pending_key.name, self.key_path)
                finally:
                    Path(pending_key.name).unlink(missing_ok=True)
            encrypted = Fernet(self.key_path.read_bytes()).encrypt(api_key.encode())
            db.execute(
                "INSERT INTO resume_models VALUES (?, ?, ?, ?, ?, ?)",
                (entry.id, owner_id, entry.name, base_url, model, encrypted),
            )
        return entry

    def get(self, owner_id: str, model_id: str) -> AIModelConfig | None:
        with closing(self._connect()) as db:
            row = db.execute(
                "SELECT base_url, model, api_key FROM resume_models WHERE owner_id = ? AND id = ?",
                (owner_id, model_id),
            ).fetchone()
        if row is None:
            return None
        base_url, model, encrypted = row
        api_key = Fernet(self.key_path.read_bytes()).decrypt(encrypted).decode()
        return AIModelConfig(base_url=base_url, model=model, api_key=SecretStr(api_key))

    def delete(self, owner_id: str, model_id: str) -> bool:
        with closing(self._connect()) as db, db:
            deleted = db.execute(
                "DELETE FROM resume_models WHERE owner_id = ? AND id = ?", (owner_id, model_id)
            )
            return deleted.rowcount > 0
