from abc import ABC, abstractmethod
from typing import Callable

from app.model.personal_idea import PersonalIdea


class BasePersonalIdeaMapper(ABC):
    def execute_sync_once(self, owner_id: str, operation_id: str, request_hash: str,
                          apply: Callable[["BasePersonalIdeaMapper"], dict]) -> dict:
        """Commit an idea and its replay receipt atomically."""
        raise NotImplementedError

    @abstractmethod
    def save(self, idea: PersonalIdea) -> PersonalIdea: ...

    @abstractmethod
    def find_by_id(self, idea_id: str, owner_id: str) -> PersonalIdea | None: ...

    @abstractmethod
    def list_all(self, owner_id: str) -> list[PersonalIdea]: ...

    @abstractmethod
    def delete(self, idea_id: str, owner_id: str) -> bool: ...
