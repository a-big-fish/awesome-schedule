from datetime import date, datetime
from hashlib import sha256
from typing import TYPE_CHECKING
from uuid import NAMESPACE_URL, uuid5
from zoneinfo import ZoneInfo

from app.mapper.base_personal_todo_mapper import BasePersonalTodoMapper
from app.model.personal_todo import PersonalTodo

if TYPE_CHECKING:
    from app.controller.schema.todo_sync_dto import TodoSyncInput


class PersonalTodoService:
    def __init__(self, mapper: BasePersonalTodoMapper, owner_id: str) -> None:
        self.mapper = mapper
        self.owner_id = owner_id

    def list_todos(self) -> list[PersonalTodo]:
        return self.mapper.list_all(self.owner_id)

    def create(self, title: str, due_date: date) -> PersonalTodo:
        todo = PersonalTodo(owner_id=self.owner_id, title=title.strip(), due_date=due_date)
        return self.mapper.save(todo)

    def toggle(self, todo_id: str) -> PersonalTodo:
        todo = self.mapper.find_by_id(todo_id, self.owner_id)
        if todo is None:
            raise LookupError("待办不存在")
        now = datetime.now(ZoneInfo("Asia/Shanghai"))
        todo.completed_at = None if todo.completed_at else now
        todo.updated_at = now
        return self.mapper.save(todo)

    def set_completed(self, todo_id: str, completed: bool) -> PersonalTodo:
        """Explicit completion is safe to repeat after an ambiguous response."""
        todo = self.mapper.find_by_id(todo_id, self.owner_id)
        if todo is None:
            raise LookupError("待办不存在")
        if (todo.completed_at is not None) == completed:
            return todo
        now = datetime.now(ZoneInfo("Asia/Shanghai"))
        todo.completed_at = now if completed else None
        todo.updated_at = now
        return self.mapper.save(todo)

    def delete(self, todo_id: str) -> bool:
        return self.mapper.delete(todo_id, self.owner_id)

    def apply_sync(self, operation_id: str, command: "TodoSyncInput") -> dict:
        """Apply an offline dated-task write with a replayable receipt."""
        def apply(mapper: BasePersonalTodoMapper) -> dict:
            service = PersonalTodoService(mapper, self.owner_id)
            todo_id = str(command.todo_id)
            todo = None
            if command.action == "create":
                expected = str(uuid5(NAMESPACE_URL, f"todo:{self.owner_id}:{operation_id}"))
                title = (command.title or "").strip()
                if todo_id != expected or not title or command.due_date is None:
                    raise ValueError("待办标识、标题或日期无效")
                todo = mapper.save(PersonalTodo(id=todo_id, owner_id=self.owner_id, title=title, due_date=command.due_date))
            elif command.action == "completion":
                if command.completed is None:
                    raise ValueError("缺少完成状态")
                todo = service.set_completed(todo_id, command.completed)
            else:
                service.delete(todo_id)
            return {"operation_id": operation_id, "user_id": self.owner_id,
                    "todo": todo.model_dump(mode="json") if todo else None,
                    "deleted_id": todo_id if command.action == "delete" else None}

        digest = sha256(command.model_dump_json().encode()).hexdigest()
        return self.mapper.execute_sync_once(self.owner_id, operation_id, digest, apply)
