from datetime import date, datetime
from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, Field, ConfigDict

from app.model.personal_event import DEFAULT_EVENT_PRIORITY, LegacyEventPriority, PersonalEventType, PriorityOrder, ReferenceLink

SyncPriority = Annotated[int, Field(strict=True, ge=1, le=100)]


class CalendarEventInput(BaseModel):
    model_config = ConfigDict(extra="forbid")
    title: str = Field(min_length=1, max_length=200)
    type: PersonalEventType
    start_at: datetime
    end_at: datetime
    timezone: str = "Asia/Shanghai"
    skip_weekends: bool = False
    workday_only: bool = False
    repeat_weekdays: list[int] = Field(default_factory=list, max_length=7)
    reminder_offsets_minutes: list[int] = Field(default_factory=list, max_length=3)
    location_text: str | None = None
    location_url: str | None = None
    note: str | None = Field(default=None, max_length=100000)
    position: str | None = None
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)
    source: Literal["manual", "invitation"] = "manual"
    raw_text: str = Field(default="", max_length=100000)
    platform: str | None = None
    room_number: str | None = Field(default=None, max_length=100)
    meeting_url: str | None = None
    source_idea_id: UUID | None = None
    priority: SyncPriority | LegacyEventPriority = DEFAULT_EVENT_PRIORITY


class CalendarSyncInput(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["create", "edit", "priority", "completion", "delete", "defer", "cancel", "close_repeat"]
    event_id: UUID
    occurrence_day: date | None = None
    event: CalendarEventInput | None = None
    completed: bool | None = None
    end_at: datetime | None = None
    confirm_overlap: bool = False
    priority: SyncPriority | None = None
    priority_scale: Literal[20, 100] = 20
    priority_order: PriorityOrder | None = None
