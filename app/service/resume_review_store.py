"""Persist resume review results next to the resume they belong to."""

import json
from datetime import datetime, timezone
from uuid import uuid4

from app.mapper.base_resume_file_storage import ResumeFileStorage


def review_prefix(pdf_storage_key: str) -> str:
    """Directory holding the resume's saved reviews, named after the resume."""
    directory, _, file_name = pdf_storage_key.rpartition("/")
    stem = file_name.rsplit(".", 1)[0]
    if directory.rpartition("/")[2] == stem:  # already resumes/<year>/<month>/<id>/<id>.pdf
        return f"{directory}/reviews"
    return f"{directory}/{stem}/reviews"


class ResumeReviewStore:
    """One JSON document per review, kept beside the resume's PDF in object storage."""

    def __init__(self, storage: ResumeFileStorage) -> None:
        self.storage = storage

    def save(self, *, owner_id: str, resume_id: str, pdf_path: str, position: str,
             models: list[dict]) -> dict:
        created = datetime.now(timezone.utc)
        review_id = f"{created:%Y%m%dT%H%M%S}-{uuid4().hex[:8]}"
        document = {
            "schema_version": 1,
            "review_id": review_id,
            "resume_id": resume_id,
            "owner_id": owner_id,
            "created_at": created.isoformat(),
            "position": position,
            "suggestion_count": sum(len(model.get("suggestions") or []) for model in models),
            "models": models,
        }
        self.storage.save_document(
            json.dumps(document, ensure_ascii=False, separators=(",", ":")).encode("utf-8"),
            f"{review_prefix(pdf_path)}/{review_id}.json",
        )
        return document

    def list(self, *, pdf_path: str, owner_id: str, limit: int = 20) -> list[dict]:
        """Newest first, metadata only, so the list stays small."""
        keys = self.storage.list_documents(review_prefix(pdf_path))
        summaries = []
        for key in reversed(keys[-limit:]):
            document = self._read(key)
            if document.get("owner_id") != owner_id:
                continue
            summaries.append({
                "id": document["review_id"],
                "created_at": document["created_at"],
                "position": document.get("position", ""),
                "suggestion_count": document.get("suggestion_count", 0),
                "models": [model.get("name") or model.get("model_id") for model in document.get("models", [])],
            })
        return summaries

    def read(self, *, pdf_path: str, owner_id: str, review_id: str) -> dict | None:
        for key in self.storage.list_documents(review_prefix(pdf_path)):
            if key.rsplit("/", 1)[-1] != f"{review_id}.json":
                continue
            document = self._read(key)
            return document if document.get("owner_id") == owner_id else None
        return None

    def _read(self, key: str) -> dict:
        try:
            document = json.loads(self.storage.read_document(key))
        except (FileNotFoundError, ValueError, UnicodeDecodeError):
            return {}
        return document if isinstance(document, dict) else {}
