from datetime import datetime
from zoneinfo import ZoneInfo
from app.model.personal_idea import PersonalIdea
from uuid import uuid5, NAMESPACE_URL


class PersonalIdeaService:
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

    def pending_summary(self, schedule):
        """Exclude converted ideas and expose a bounded title-only preview."""
        items = self.list()
        event_ids = {item.id: str(uuid5(NAMESPACE_URL, f"idea:{self.owner_id}:{item.id}"))
                     for item in items}
        arranged = schedule.mapper.existing_ids(list(event_ids.values()), self.owner_id)
        pending = [item for item in items if event_ids[item.id] not in arranged]
        return {"total": len(pending), "items": [{"id": item.id, "title": item.title}
                                                 for item in pending[:5]]}

    def abandon(self, idea_id):
        idea = self.get(idea_id)
        idea.status = "abandoned"
        idea.updated_at = datetime.now(ZoneInfo("Asia/Shanghai"))
        return self.mapper.save(idea)
