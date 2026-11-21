from app.mapper.base_personal_idea_mapper import BasePersonalIdeaMapper
from app.model.personal_idea import PersonalIdea
from threading import RLock
from copy import deepcopy
from typing import Callable


class MemoryPersonalIdeaMapper(BasePersonalIdeaMapper):
    def __init__(self) -> None:
        self._ideas: dict[str, PersonalIdea] = {}
        self._receipts: dict[tuple[str, str], tuple[str, dict]] = {}
        self._lock = RLock()

    def execute_sync_once(self, owner_id: str, operation_id: str, request_hash: str,
                          apply: Callable[[BasePersonalIdeaMapper], dict]) -> dict:
        """Serialize writes and replay the original acknowledgement."""
        with self._lock:
            key = (owner_id, operation_id)
            if key in self._receipts:
                digest, result = self._receipts[key]
                if digest != request_hash:
                    raise ValueError("同步操作标识已被用于其他内容")
                return deepcopy(result)
            result = apply(self)
            self._receipts[key] = (request_hash, deepcopy(result))
            return result

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
