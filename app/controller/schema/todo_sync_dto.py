"""Commands for the native dated-task queue."""

from datetime import date
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


class TodoSyncInput(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["create", "completion", "delete"]
    todo_id: UUID
    title: str | None = Field(default=None, max_length=200)
    due_date: date | None = None
    completed: bool | None = Field(default=None, strict=True)
