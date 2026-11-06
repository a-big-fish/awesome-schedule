from abc import ABC, abstractmethod

from app.model.personal_idea import PersonalIdea


class BasePersonalIdeaMapper(ABC):
    @abstractmethod
    def save(self, idea: PersonalIdea) -> PersonalIdea: ...

    @abstractmethod
    def find_by_id(self, idea_id: str, owner_id: str) -> PersonalIdea | None: ...

    @abstractmethod
    def list_all(self, owner_id: str) -> list[PersonalIdea]: ...

    @abstractmethod
    def delete(self, idea_id: str, owner_id: str) -> bool: ...
