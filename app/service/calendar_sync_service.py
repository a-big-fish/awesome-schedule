from __future__ import annotations

from datetime import datetime
from hashlib import sha256
from typing import TYPE_CHECKING
from uuid import NAMESPACE_URL, uuid5
from zoneinfo import ZoneInfo

if TYPE_CHECKING:
    from app.controller.schema.calendar_sync_dto import CalendarSyncInput
from app.mapper.base_personal_event_mapper import BasePersonalEventMapper, SyncResult
from app.model.personal_event import normalize_legacy_priority
from app.service.personal_idea_service import PersonalIdeaService
from app.service.personal_schedule_service import PersonalScheduleService


class CalendarSyncService:
    """Apply an ordered browser outbox item once, without a write-after-read HTTP round trip."""
    @staticmethod
    def apply(service: PersonalScheduleService, ideas: PersonalIdeaService, operation_id: str,
              command: CalendarSyncInput) -> SyncResult:
        # Keep the fingerprints of requests queued or acknowledged by older clients.
        optional_fields = {"room_number", "priority"}
        supplied = command.event.model_fields_set if command.event else set()
        omitted = optional_fields - supplied
        room_supplied = "room_number" in supplied
        exclude = {"event": omitted} if command.event and omitted else {}
        # The transport hint was absent from receipts created before the scale change.
        if command.priority_scale == 100 or "priority_scale" not in command.model_fields_set:
            exclude["priority_scale"] = True
        if "priority" not in command.model_fields_set:
            exclude["priority"] = True
        if "priority_order" not in command.model_fields_set:
            exclude["priority_order"] = True
        digest = sha256(command.model_dump_json(exclude=exclude).encode()).hexdigest()

        def execute(mapper: BasePersonalEventMapper) -> SyncResult:
            schedule = PersonalScheduleService(mapper, service.profile_mapper, service.parser, service.owner_id)
            event_id = str(command.event_id)
            target = event_id + (f"~{command.occurrence_day}" if command.occurrence_day else "")
            event = None
            if command.action in {"create", "edit"}:
                if command.event is None:
                    raise ValueError("缺少日程内容")
                values = command.event.model_dump(mode="python", exclude={"source", "raw_text", "source_idea_id"} | omitted)
                if "priority" in values:
                    values["priority"] = normalize_legacy_priority(values["priority"], command.priority_scale)
                values["reference_links"] = [link.model_dump(mode="json") for link in command.event.reference_links]
                if command.action == "create":
                    idea_id = command.event.source_idea_id
                    key = f"idea:{service.owner_id}:{idea_id}" if idea_id else f"calendar:{service.owner_id}:{operation_id}"
                    if event_id != str(uuid5(NAMESPACE_URL, key)):
                        raise ValueError("新建日程标识无效")
                    if idea_id and ideas.get(str(idea_id)).status != "pending":
                        raise ValueError("已放弃事项不能安排")
                    if schedule.get_event(event_id):
                        raise ValueError("日程已存在，请使用编辑操作")
                    values["event_type"] = values.pop("type")
                    if command.event.source == "invitation":
                        event = schedule.from_invitation(command.event.raw_text, **values)
                        event.id = event_id
                        event = schedule.save_event(event, command.confirm_overlap)
                    else:
                        values.pop("platform", None)
                        if not room_supplied:
                            values.pop("meeting_url", None)
                        event = schedule.create_manual(**values, source_idea_id=str(idea_id) if idea_id else None,
                            event_id=event_id, confirm_overlap=command.confirm_overlap)
                else:
                    values.pop("platform", None)
                    if not room_supplied:
                        values.pop("meeting_url", None)
                    event = schedule.update_event(event_id, confirm_overlap=command.confirm_overlap, **values)
            elif command.action == "priority":
                if command.priority is None:
                    raise ValueError("缺少优先级")
                priority = normalize_legacy_priority(command.priority, command.priority_scale)
                event = schedule.set_event_priority(event_id, priority, command.priority_order)
            elif command.action == "delete":
                schedule.delete_event(event_id)  # Repeating a successful delete remains successful.
            elif command.action == "completion":
                if command.completed is None:
                    raise ValueError("缺少完成状态")
                event = schedule.set_event_completed(target, command.completed)
                if event.repeat_weekdays:
                    event = schedule.get_event(event_id)
            elif command.action in {"defer", "cancel"}:
                event = schedule.resolve_overdue(target, command.action, command.end_at)
            elif command.action == "close_repeat":
                event = schedule.close_repeat(event_id)
            return {"schema_version": 1, "user_id": service.owner_id, "operation_id": operation_id,
                    "server_time": datetime.now(ZoneInfo("Asia/Shanghai")).isoformat(),
                    "event": event.model_dump(mode="json", exclude={"raw_invitation", "resume_id", "attachment_refs", "owner_id"}) if event else None,
                    "deleted_id": event_id if command.action == "delete" else None}

        return service.mapper.execute_sync_once(service.owner_id, operation_id, digest, execute)
