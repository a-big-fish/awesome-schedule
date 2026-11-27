"""Native note drafts use the same optimistic version contract as the Web editor."""

from datetime import datetime

from pydantic import BaseModel, Field


class NativeDraftInput(BaseModel):
    version: datetime
    questions: list[dict] = Field(max_length=500)
    category_minutes: dict[str, int] | None = None
    total_minutes: int | None = Field(default=None, ge=0, le=1440)
    feeling: str | None = Field(default=None, max_length=20000)
    raw_text_draft: str | None = Field(default=None, max_length=100000)
