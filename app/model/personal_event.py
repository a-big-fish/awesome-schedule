from datetime import datetime
from typing import Annotated, Literal
from uuid import uuid4
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError
from urllib.parse import urlsplit

from pydantic import BaseModel, BeforeValidator, Field, HttpUrl, model_validator

from interviewschedule.utils.time_utils import ensure_timezone

PersonalEventType = Literal[
    "interview", "exam", "course", "recruiting", "meeting", "personal"
]
DEFAULT_EVENT_PRIORITY = 10
PersonalEventPriority = Annotated[int, Field(strict=True, ge=1, le=20)]
LegacyEventPriority = Literal["high", "normal", "low"]
PriorityOrder = Annotated[str, Field(min_length=1, max_length=256, pattern=r"^[0-9a-z]*[1-9a-z]$")]


def normalize_legacy_priority(value, scale: int = 20):
    if isinstance(value, str):
        return {"low": 1, "normal": DEFAULT_EVENT_PRIORITY, "high": 18}.get(value, value)
    if scale == 100 and type(value) is int:
        return (value + 4) // 5
    return value


def is_map_url(value: str) -> bool:
    parsed = urlsplit(value)
    host = (parsed.hostname or "").lower()
    return host in {"map.baidu.com", "maps.baidu.com", "amap.com", "www.amap.com", "uri.amap.com",
                    "ditu.amap.com", "surl.amap.com", "j.map.baidu.com", "map.qq.com", "maps.apple.com",
                    "maps.google.com", "maps.app.goo.gl"} or (
        host in {"google.com", "www.google.com", "www.google.cn"} and parsed.path.startswith("/maps"))


class ReferenceLink(BaseModel):
    title: str = Field(default="", max_length=200)
    url: HttpUrl = Field(max_length=2048)

    @model_validator(mode="after")
    def validate_reference(self):
        self.title = self.title.strip()
        if self.url.username or self.url.password:
            raise ValueError("参考链接不能包含账号或密码")
        return self


class PersonalEvent(BaseModel):
    """An event owned by the job seeker's independent schedule."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    owner_id: str = "local"
    type: PersonalEventType
    title: str = Field(min_length=1, max_length=200)
    priority: Annotated[PersonalEventPriority, BeforeValidator(normalize_legacy_priority)] = DEFAULT_EVENT_PRIORITY
    priority_scale: Literal[20] = 20
    priority_order: PriorityOrder | None = None
    start_at: datetime
    end_at: datetime
    timezone: str = "Asia/Shanghai"
    skip_weekends: bool = False
    workday_only: bool = False
    repeat_weekdays: list[int] = Field(default_factory=list)
    repeat_closed_on: str | None = None
    repeat_completions: dict[str, datetime] = Field(default_factory=dict)
    repeat_streak: int = 0
    repeat_missed: int = 0
    location_text: str | None = None
    location_url: HttpUrl | None = None
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)
    note: str | None = None
    source: Literal["manual", "invitation", "migrated"] = "manual"
    raw_invitation: str | None = None
    position: str | None = None
    platform: str | None = None
    room_number: str | None = None
    meeting_url: HttpUrl | None = None
    resume_id: str | None = None
    legacy_entry_id: str | None = None
    attachment_refs: list[str] = Field(default_factory=list)
    reminder_offsets_minutes: list[int] = Field(default_factory=list)
    completed_at: datetime | None = None
    cancelled_at: datetime | None = None
    created_at: datetime = Field(default_factory=datetime.now)
    updated_at: datetime = Field(default_factory=datetime.now)

    @classmethod
    def from_storage(cls, payload: dict) -> "PersonalEvent":
        if "priority" in payload and payload.get("priority_scale") != 20:
            payload = {**payload, "priority": normalize_legacy_priority(payload["priority"], 100), "priority_scale": 20}
        return cls.model_validate(payload)

    @model_validator(mode="before")
    @classmethod
    def migrate_legacy_link(cls, data):
        if isinstance(data, dict) and data.get("location_url") and not is_map_url(str(data["location_url"])):
            data = dict(data)
            links = list(data.get("reference_links") or [])
            url = str(data["location_url"])
            if not any(str(link.url if isinstance(link, ReferenceLink) else link.get("url")) == url for link in links):
                links.append({"url": url})
            data["reference_links"] = links
            data["location_url"] = None
        return data

    @model_validator(mode="after")
    def validate_event(self) -> "PersonalEvent":
        try:
            ZoneInfo(self.timezone)
        except (ZoneInfoNotFoundError, ValueError) as exc:
            raise ValueError("时区无效") from exc
        self.start_at = ensure_timezone(self.start_at)
        self.end_at = ensure_timezone(self.end_at)
        self.created_at = ensure_timezone(self.created_at)
        self.updated_at = ensure_timezone(self.updated_at)
        if self.completed_at is not None:
            self.completed_at = ensure_timezone(self.completed_at)
        if self.cancelled_at is not None:
            self.cancelled_at = ensure_timezone(self.cancelled_at)
        if self.start_at.astimezone(ZoneInfo(self.timezone)).weekday() >= 5:
            self.skip_weekends = False
        if self.end_at <= self.start_at:
            raise ValueError("结束时间必须晚于开始时间")
        if self.type != "interview" and self.resume_id:
            raise ValueError("只有面试事件可以关联简历")
        if self.repeat_weekdays:
            if len(set(self.repeat_weekdays)) != len(self.repeat_weekdays) or any(day not in range(7) for day in self.repeat_weekdays):
                raise ValueError("重复日期必须为周一至周日")
            local_start = self.start_at.astimezone(ZoneInfo(self.timezone))
            local_end = self.end_at.astimezone(ZoneInfo(self.timezone))
            if local_start.date() != local_end.date():
                raise ValueError("重复任务的开始和结束时间必须在同一天")
            self.skip_weekends = False
        allowed_reminders = {10, 60, 1440}
        if len(self.reminder_offsets_minutes) != len(set(self.reminder_offsets_minutes)) or not set(self.reminder_offsets_minutes) <= allowed_reminders:
            raise ValueError("提醒时间无效")
        self.reminder_offsets_minutes.sort()
        return self
