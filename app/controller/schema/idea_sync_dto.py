"""Validated, replayable browser idea commands."""
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field
from app.model.personal_event import ReferenceLink


class IdeaSyncInput(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["create", "edit", "abandon"]
    idea_id: UUID
    title: str = Field(default="", max_length=200)
    note: str = Field(default="", max_length=20000)
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)
