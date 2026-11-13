from threading import RLock
from copy import deepcopy

from app.mapper.base_personal_event_mapper import BasePersonalEventMapper, SyncApply, SyncResult
from app.model.personal_event import PersonalEvent


class MemoryPersonalEventMapper(BasePersonalEventMapper):
    def __init__(self) -> None:
        self._events: dict[str, PersonalEvent] = {}
        self._lock = RLock()
        self._sync_receipts = {}

    def execute_sync_once(self, owner_id: str, operation_id: str, request_hash: str,
                          apply: SyncApply) -> SyncResult:
        with self._lock:
            key = (owner_id, operation_id)
            receipt = self._sync_receipts.get(key)
            if receipt:
                if receipt[0] != request_hash:
                    raise ValueError("同步操作标识已被用于其他内容")
                return deepcopy(receipt[1])
            before = deepcopy(self._events)
            try:
                result = apply(self)
                self._sync_receipts[key] = (request_hash, deepcopy(result))
                return result
            except Exception:
                self._events = before
                raise

    def save(self, event: PersonalEvent) -> PersonalEvent:
        with self._lock:
            self._events[event.id] = event.model_copy(deep=True)
            return self._events[event.id].model_copy(deep=True)

    def find_by_id(self, event_id: str, owner_id: str = "local") -> PersonalEvent | None:
        with self._lock:
            event = self._events.get(event_id)
            return event.model_copy(deep=True) if event and event.owner_id == owner_id else None

    def list_all(self, owner_id: str = "local") -> list[PersonalEvent]:
        with self._lock:
            events = [event for event in self._events.values() if event.owner_id == owner_id]
        return [event.model_copy(deep=True) for event in sorted(events, key=lambda item: item.start_at)]

    def delete(self, event_id: str, owner_id: str = "local") -> bool:
        with self._lock:
            if event_id not in self._events or self._events[event_id].owner_id != owner_id:
                return False
            del self._events[event_id]
            return True

    def list_interview_options(self, owner_id: str = "local") -> list[dict]:
        with self._lock:
            events = sorted((event for event in self._events.values()
                             if event.owner_id == owner_id and event.type == "interview"),
                            key=lambda event: (event.start_at, event.id))
            return [{"id": event.id, "title": event.title, "start_at": event.start_at}
                    for event in events]
