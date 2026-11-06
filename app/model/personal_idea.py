"""Unscheduled ideas, independent of calendar events and completion."""
from datetime import datetime
from typing import Literal
from uuid import uuid4
from zoneinfo import ZoneInfo
from pydantic import BaseModel, Field, field_validator
from app.model.personal_event import ReferenceLink


class PersonalIdea(BaseModel):
    id: str = Field(default_factory=lambda: str(uuid4()))
    owner_id: str
    title: str = Field(min_length=1, max_length=200)
    note: str = Field(default="", max_length=20000)
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)
    status: Literal["pending", "abandoned"] = "pending"
    created_at: datetime = Field(default_factory=lambda: datetime.now(ZoneInfo("Asia/Shanghai")))
    updated_at: datetime = Field(default_factory=lambda: datetime.now(ZoneInfo("Asia/Shanghai")))

    @field_validator("title", mode="before")
    @classmethod
    def strip_title(cls, value):
        return value.strip() if isinstance(value, str) else value
