from datetime import datetime
from zoneinfo import ZoneInfo
from app.model.personal_idea import PersonalIdea
from uuid import uuid5, NAMESPACE_URL
from hashlib import sha256
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from app.controller.schema.idea_sync_dto import IdeaSyncInput


class PersonalIdeaService:
    def apply_sync(self, operation_id: str, command: "IdeaSyncInput") -> dict:
        """Return the authoritative write result without a follow-up list read."""
        digest = sha256(command.model_dump_json().encode()).hexdigest()

        def apply(mapper) -> dict:
            service = PersonalIdeaService(mapper, self.owner_id)
            idea_id = str(command.idea_id)
            if command.action == "create":
                expected = str(uuid5(NAMESPACE_URL, f"idea-create:{self.owner_id}:{operation_id}"))
                if idea_id != expected:
                    raise ValueError("新建事项标识无效")
                idea = PersonalIdea(id=idea_id, owner_id=self.owner_id, title=command.title,
                                    note=command.note, reference_links=command.reference_links)
                idea = mapper.save(idea)
            elif command.action == "edit":
                idea = service.save(command.title, command.note, command.reference_links, idea_id)
            else:
                idea = service.abandon(idea_id)
            return {"schema_version": 1, "user_id": self.owner_id, "operation_id": operation_id,
                    "idea": idea.model_dump(mode="json", exclude={"owner_id"}),
                    "server_time": datetime.now(ZoneInfo("Asia/Shanghai")).isoformat()}

        return self.mapper.execute_sync_once(self.owner_id, operation_id, digest, apply)

    def __init__(self, mapper, owner_id):
        self.mapper, self.owner_id = mapper, owner_id

    def get(self, idea_id):
        idea = self.mapper.find_by_id(idea_id, self.owner_id)
        if not idea:
            raise LookupError("待安排事项不存在")
        return idea

    def list(self, abandoned=False):
        return [item for item in reversed(self.mapper.list_all(self.owner_id))
                if item.status == ("abandoned" if abandoned else "pending")]

    def save(self, title, note, reference_links, idea_id=None):
        idea = self.get(idea_id) if idea_id else PersonalIdea(owner_id=self.owner_id, title=title)
        if idea.status != "pending":
            raise ValueError("已放弃事项不能修改或安排")
        updated = PersonalIdea.model_validate({**idea.model_dump(), "title": title, "note": note,
                                              "reference_links": reference_links,
                                              "updated_at": datetime.now(ZoneInfo("Asia/Shanghai"))})
        return self.mapper.save(updated)

    def arranged_event_ids(self, schedule, items=None) -> dict[str, str]:
        """Map arranged ideas to their event ids without loading whole event payloads."""
        candidates = {item.id: str(uuid5(NAMESPACE_URL, f"idea:{self.owner_id}:{item.id}"))
                      for item in (self.list() if items is None else items)}
        arranged = schedule.mapper.existing_ids(list(candidates.values()), self.owner_id)
        return {idea_id: event_id for idea_id, event_id in candidates.items() if event_id in arranged}

    def pending_summary(self, schedule):
        """Exclude converted ideas and expose a bounded title-only preview."""
        items = self.list()
        arranged = self.arranged_event_ids(schedule, items)
        pending = [item for item in items if item.id not in arranged]
        return {"total": len(pending), "items": [{"id": item.id, "title": item.title}
                                                 for item in pending[:5]]}

    def abandon(self, idea_id):
        idea = self.get(idea_id)
        idea.status = "abandoned"
        idea.updated_at = datetime.now(ZoneInfo("Asia/Shanghai"))
        return self.mapper.save(idea)
