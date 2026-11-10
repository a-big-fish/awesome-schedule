from abc import ABC, abstractmethod

from app.model.personal_event import PersonalEvent


class BasePersonalEventMapper(ABC):
    def existing_ids(self, event_ids: list[str], owner_id: str) -> set[str]:
        """Find arranged ideas without returning their event contents."""
        wanted = set(event_ids)
        return {event.id for event in self.list_all(owner_id) if event.id in wanted}

    @abstractmethod
    def save(self, event: PersonalEvent) -> PersonalEvent:
        """Create or update an event."""

    @abstractmethod
    def find_by_id(self, event_id: str, owner_id: str = "local") -> PersonalEvent | None:
        """Find one event owned by the given profile."""

    @abstractmethod
    def list_all(self, owner_id: str = "local") -> list[PersonalEvent]:
        """List the owner's events ordered by start time."""

    @abstractmethod
    def list_interview_options(self, owner_id: str = "local") -> list[dict]:
        """Return only id, title and start_at for the owner's interview selector."""

    @abstractmethod
    def delete(self, event_id: str, owner_id: str = "local") -> bool:
        """Delete an event owned by the given profile."""
