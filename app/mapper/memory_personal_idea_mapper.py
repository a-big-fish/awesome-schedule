from app.mapper.base_personal_idea_mapper import BasePersonalIdeaMapper
from app.model.personal_idea import PersonalIdea


class MemoryPersonalIdeaMapper(BasePersonalIdeaMapper):
    def __init__(self) -> None:
        self._ideas: dict[str, PersonalIdea] = {}

    def save(self, idea: PersonalIdea) -> PersonalIdea:
        self._ideas[idea.id] = idea.model_copy(deep=True)
        return idea.model_copy(deep=True)

    def find_by_id(self, idea_id: str, owner_id: str) -> PersonalIdea | None:
        idea = self._ideas.get(idea_id)
        return idea.model_copy(deep=True) if idea and idea.owner_id == owner_id else None

    def list_all(self, owner_id: str) -> list[PersonalIdea]:
        return sorted((idea.model_copy(deep=True) for idea in self._ideas.values()
                       if idea.owner_id == owner_id), key=lambda item: (item.created_at, item.id))

    def delete(self, idea_id: str, owner_id: str) -> bool:
        if self.find_by_id(idea_id, owner_id) is None:
            return False
        del self._ideas[idea_id]
        return True
