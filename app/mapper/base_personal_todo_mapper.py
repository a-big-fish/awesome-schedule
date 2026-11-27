from abc import ABC, abstractmethod
from typing import Callable

from app.model.personal_todo import PersonalTodo


class BasePersonalTodoMapper(ABC):
    def execute_sync_once(self, owner_id: str, operation_id: str, digest: str,
                          apply: Callable[["BasePersonalTodoMapper"], dict]) -> dict:
        """Commit a task command and its idempotency receipt together."""
        raise NotImplementedError

    @abstractmethod
    def save(self, todo: PersonalTodo) -> PersonalTodo: ...

    @abstractmethod
    def find_by_id(self, todo_id: str, owner_id: str) -> PersonalTodo | None: ...

    @abstractmethod
    def list_all(self, owner_id: str) -> list[PersonalTodo]: ...

    @abstractmethod
    def delete(self, todo_id: str, owner_id: str) -> bool: ...
