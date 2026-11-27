"""Authenticated Android snapshot and dated tasks."""

import math
from datetime import date, datetime, timezone
from uuid import UUID

from fastapi import APIRouter, BackgroundTasks, Depends, Form, HTTPException, Request, Response
from pydantic import BaseModel, Field, ValidationError

from app.core.dependencies import (get_interview_experience_service, get_personal_schedule_service,
                                   get_personal_todo_service, get_personal_idea_service)
from app.core.exceptions import PersonalEventConflictWarning
from app.controller.schema.todo_sync_dto import TodoSyncInput
from app.core.exceptions import MeetingParseError
from app.model.personal_event import PersonalEventType
from app.model.personal_event import ReferenceLink
from app.service.interview_experience_service import InterviewExperienceService
from app.service.personal_schedule_service import PersonalScheduleService
from app.service.personal_todo_service import PersonalTodoService
from app.service.personal_idea_service import PersonalIdeaService


router = APIRouter(prefix="/api/mobile/v1", tags=["mobile"])


class NativeEventInput(BaseModel):
    """Editable event fields sent by the native schedule screen."""

    title: str = Field(min_length=1, max_length=200)
    event_type: PersonalEventType
    start_at: datetime
    end_at: datetime
    timezone: str = "Asia/Shanghai"
    position: str | None = None
    location_text: str | None = None
    note: str | None = None
    reminder_offsets_minutes: list[int] = Field(default_factory=list)
    confirm_overlap: bool = False
    raw_invitation: str | None = None
    skip_weekends: bool | None = None
    workday_only: bool = False
    source_idea_id: UUID | None = None
    repeat_weekdays: list[int] = Field(default_factory=list)
    location_url: str | None = None
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)


class NativeInvitationInput(BaseModel):
    raw_text: str = Field(min_length=1)


class NativeCompletionInput(BaseModel):
    completed: bool = Field(strict=True)


def _event_error(error: Exception) -> HTTPException:
    return HTTPException(status_code=422, detail=str(error))


@router.post("/events/parse")
def parse_event(payload: NativeInvitationInput,
                schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> dict:
    """Parse a pasted invitation for review in the native event editor."""
    try:
        event = schedule.from_invitation(payload.raw_text)
    except (MeetingParseError, ValidationError, ValueError) as error:
        raise _event_error(error) from error
    return event.model_dump(mode="json")


@router.post("/events", status_code=201)
def create_event(payload: NativeEventInput, request: Request, background: BackgroundTasks,
                 schedule: PersonalScheduleService = Depends(get_personal_schedule_service),
                 ideas: PersonalIdeaService = Depends(get_personal_idea_service)) -> dict:
    """Create an event with the same ownership, validation and overlap rules as Web."""
    try:
        if payload.source_idea_id:
            if ideas.get(str(payload.source_idea_id)).status != "pending":
                raise ValueError("已放弃事项不能安排")
            if payload.raw_invitation:
                raise ValueError("待安排事项请使用手动日程表单安排")
        fields = dict(title=payload.title, event_type=payload.event_type,
                      start_at=payload.start_at, end_at=payload.end_at,
                      timezone=payload.timezone, position=payload.position,
                      location_text=payload.location_text, note=payload.note,
                      reminder_offsets_minutes=payload.reminder_offsets_minutes,
                      skip_weekends=payload.skip_weekends,
                      workday_only=payload.workday_only,
                      repeat_weekdays=payload.repeat_weekdays,
                      location_url=payload.location_url,
                      reference_links=[link.model_dump(mode="json") for link in payload.reference_links],
                      confirm_overlap=payload.confirm_overlap)
        event = (schedule.create_from_invitation(payload.raw_invitation, **fields)
                 if payload.raw_invitation else schedule.create_manual(
                     **fields, source_idea_id=str(payload.source_idea_id) if payload.source_idea_id else None))
    except LookupError as error:
        raise HTTPException(404, detail=str(error)) from error
    except PersonalEventConflictWarning as error:
        raise HTTPException(409, detail={"conflicts": error.event_titles}) from error
    except (ValueError, ValidationError, MeetingParseError) as error:
        raise _event_error(error) from error
    background.add_task(request.app.state.reference_titles.enrich_event, schedule, event)
    return event.model_dump(mode="json", exclude={"raw_invitation"})


@router.put("/events/{event_id}")
def update_event(event_id: str, payload: NativeEventInput, request: Request, background: BackgroundTasks,
                 schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> dict:
    """Update one owned event and preserve its existing source metadata."""
    try:
        event = schedule.update_event(
            event_id, title=payload.title.strip(), type=payload.event_type,
            start_at=payload.start_at, end_at=payload.end_at,
            timezone=payload.timezone, position=payload.position,
            location_text=payload.location_text, note=payload.note,
            reminder_offsets_minutes=payload.reminder_offsets_minutes,
            **{key: value for key, value in payload.model_dump(mode="python", exclude_unset=True).items()
               if key in {"skip_weekends", "workday_only", "repeat_weekdays", "location_url", "reference_links"}},
            confirm_overlap=payload.confirm_overlap,
        )
    except LookupError as error:
        raise HTTPException(404, detail=str(error)) from error
    except PersonalEventConflictWarning as error:
        raise HTTPException(409, detail={"conflicts": error.event_titles}) from error
    except (ValueError, ValidationError) as error:
        raise _event_error(error) from error
    background.add_task(request.app.state.reference_titles.enrich_event, schedule, event)
    return event.model_dump(mode="json", exclude={"raw_invitation"})


@router.delete("/events/{event_id}", status_code=204)
def delete_event(event_id: str,
                 schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> None:
    """Delete one event owned by the current account."""
    if not schedule.delete_event(event_id):
        raise HTTPException(404, detail="日程不存在")


@router.post("/events/{event_id}/completion")
def complete_event(event_id: str, payload: NativeCompletionInput,
                   schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> dict:
    """Set completion for an owned event and return its updated native detail."""
    try:
        event = schedule.set_event_completed(event_id, payload.completed)
    except LookupError as error:
        raise HTTPException(404, detail="日程不存在") from error
    except ValueError as error:
        raise _event_error(error) from error
    return event.model_dump(mode="json", exclude={"raw_invitation"})


class NativeOverdueInput(BaseModel):
    action: str = Field(pattern="^(defer|cancel)$")
    end_at: datetime | None = None


@router.post("/events/{event_id}/overdue")
def resolve_overdue(event_id: str, payload: NativeOverdueInput,
                    schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> dict:
    try:
        return schedule.resolve_overdue(event_id, payload.action, payload.end_at).model_dump(
            mode="json", exclude={"raw_invitation"})
    except LookupError as error:
        raise HTTPException(404, str(error)) from error
    except ValueError as error:
        raise _event_error(error) from error


@router.post("/events/{event_id}/close-repeat")
def close_repeat(event_id: str,
                 schedule: PersonalScheduleService = Depends(get_personal_schedule_service)) -> dict:
    try:
        return schedule.close_repeat(event_id).model_dump(mode="json", exclude={"raw_invitation"})
    except LookupError as error:
        raise HTTPException(404, str(error)) from error


def _profile(user) -> dict:
    """Explicit allowlist; never serialize AuthUser/password_hash to clients."""
    return {"user_id": user.id, "display_name": user.display_name,
            "email": user.email, "workday_start": user.workday_start,
            "workday_end": user.workday_end}


class NativeProfileInput(BaseModel):
    display_name: str = Field(min_length=1, max_length=40)
    workday_start: str | None = None
    workday_end: str | None = None


@router.get("/profile")
def profile(request: Request, response: Response) -> dict:
    response.headers["Cache-Control"] = "no-store"
    return _profile(request.state.user)


@router.put("/profile")
def update_profile(payload: NativeProfileInput, request: Request, response: Response) -> dict:
    user = request.state.user
    try:
        updated = request.app.state.auth_service.update_preferences(
            user.id, payload.display_name,
            payload.workday_start if payload.workday_start is not None else user.workday_start,
            payload.workday_end if payload.workday_end is not None else user.workday_end)
    except ValueError as error:
        raise _event_error(error) from error
    response.headers["Cache-Control"] = "no-store"
    return _profile(updated)


def _author(owner_id: str, users: dict) -> str:
    user = users.get(owner_id)
    if user and user.display_name:
        return user.display_name
    if user and user.email:
        return f"热心用户_{user.email.split('@', 1)[0][:3]}"
    if user and user.username:
        return user.username
    if owner_id == "local":
        return "本地用户"
    return f"用户 #{owner_id.replace('-', '')[:8]}"


@router.get("/plaza")
def plaza(request: Request, response: Response, company: str = "", order: str = "newest", page: int = 1,
          service: InterviewExperienceService = Depends(get_interview_experience_service)) -> dict:
    """Typed native equivalent of Web's HTML batches, with the same visibility rules."""
    page = max(1, page)
    rows, total = service.list_plaza(company.strip()[:200], order if order in {"newest", "oldest"} else "newest", page)
    users = request.app.state.auth_service.mapper.get_users(list({row["experience"].owner_id for row in rows}))
    items = []
    for row in rows:
        item = row["experience"]
        items.append({"id": item.id, "title": item.title, "company": item.company,
                      "position": item.position, "interviewed_at": item.interviewed_at,
                      "interview_round": item.interview_round, "author": _author(item.owner_id, users),
                      "question_count": row.get("question_count", 0)})
    response.headers["Cache-Control"] = "no-store"
    return {"items": items, "total": total, "next_page": page + 1 if page * 20 < total else None}


@router.get("/plaza/{experience_id}")
def plaza_detail(experience_id: str, request: Request, response: Response,
                 service: InterviewExperienceService = Depends(get_interview_experience_service)) -> dict:
    result = service.get_public_experience(experience_id)
    if result is None:
        raise HTTPException(404, "面经不存在或未公开")
    item, questions = result
    users = request.app.state.auth_service.mapper.get_users([item.owner_id])
    response.headers["Cache-Control"] = "no-store"
    return {"experience": {**item.model_dump(mode="json", exclude={"raw_text_draft"}),
                           "questions": [q.model_dump(mode="json") for q in questions]},
            "author": _author(item.owner_id, users)}


@router.get("/experiences")
def experiences(request: Request, response: Response, q: str = "", visibility: str = "all",
                order: str = "newest", page: int = 1,
                service: InterviewExperienceService = Depends(get_interview_experience_service)) -> dict:
    """Owner-scoped summary page for the native experience list; never returns drafts or bodies."""
    query = q.strip()[:100]
    visibility = visibility if visibility in {"all", "public", "private"} else "all"
    order = order if order in {"newest", "oldest"} else "newest"
    requested = max(1, page)
    rows, total = service.list_summaries(query=query, visibility=visibility, order=order,
                                        page=requested, page_size=20)
    page_count = max(1, math.ceil(total / 20))
    current = min(requested, page_count)
    if current != requested:
        rows, total = service.list_summaries(query=query, visibility=visibility, order=order,
                                            page=current, page_size=20)
    response.headers["Cache-Control"] = "no-store"
    return {
        "items": [{"id": row["experience"].id, "title": row["experience"].title,
                   "company": row["experience"].company, "position": row["experience"].position,
                   "interview_round": row["experience"].interview_round,
                   "is_public": row["experience"].is_public,
                   "updated_at": row["experience"].updated_at.isoformat(),
                   "question_count": row["question_count"]} for row in rows],
        "total": total, "page": current, "page_size": 20, "has_more": current < page_count,
        "page_count": page_count, "next_page": current + 1 if current < page_count else None,
    }


class NativeExperienceInput(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    company: str | None = Field(default=None, max_length=200)
    position: str | None = Field(default=None, max_length=200)
    interview_round: str | None = Field(default=None, max_length=50)
    linked_event_id: str | None = None
    interviewed_at: datetime | None = None
    is_public: bool = False


@router.get("/experiences/{experience_id}")
def experience_detail(experience_id: str, response: Response,
                      service: InterviewExperienceService = Depends(get_interview_experience_service)) -> dict:
    item = service.get_experience(experience_id)
    if item is None:
        raise HTTPException(404, "面经不存在")
    response.headers["Cache-Control"] = "no-store"
    return {"experience": item.model_dump(mode="json"),
            "questions": [question.model_dump(mode="json") for question in service.list_questions(experience_id)]}


@router.post("/experiences", status_code=201)
@router.put("/experiences/{experience_id}")
def save_experience(payload: NativeExperienceInput, response: Response, experience_id: str | None = None,
                    service: InterviewExperienceService = Depends(get_interview_experience_service)) -> dict:
    fields = payload.model_dump()
    try:
        item = (service.update_experience(experience_id, **fields) if experience_id
                else service.create_experience(**fields))
    except LookupError as error:
        raise HTTPException(404, str(error)) from error
    except ValueError as error:
        raise _event_error(error) from error
    response.headers["Cache-Control"] = "no-store"
    return {"experience": item.model_dump(mode="json")}


@router.get("/snapshot")
def snapshot(
    request: Request,
    response: Response,
    schedule: PersonalScheduleService = Depends(get_personal_schedule_service),
    experiences: InterviewExperienceService = Depends(get_interview_experience_service),
    todos: PersonalTodoService = Depends(get_personal_todo_service),
    ideas: PersonalIdeaService = Depends(get_personal_idea_service),
) -> dict:
    """Return one authoritative, owner-scoped snapshot for read-only offline use."""
    response.headers["Cache-Control"] = "no-store"
    return {
        "schema_version": 1,
        "user_id": request.state.user.id,
        "is_admin": request.state.user.is_admin,
        "server_time": datetime.now(timezone.utc).isoformat(),
        "events": [event.model_dump(mode="json", exclude={"raw_invitation"}) for event in schedule.list_events()],
        "experiences": experiences.snapshot_experiences(),
        "todos": [todo.model_dump(mode="json") for todo in todos.list_todos()],
        "ideas": _ideas_payload(ideas, schedule),
        "profile": _profile(request.state.user),
    }


def _ideas_payload(ideas: PersonalIdeaService, schedule: PersonalScheduleService) -> list[dict]:
    pending = ideas.list()
    arranged = ideas.arranged_event_ids(schedule, pending)
    return [{**item.model_dump(mode="json"), "arranged_event_id": arranged.get(item.id)}
            for item in pending + ideas.list(True)]


@router.get("/calendar")
def calendar(request: Request, response: Response,
             schedule: PersonalScheduleService = Depends(get_personal_schedule_service),
             todos: PersonalTodoService = Depends(get_personal_todo_service),
             ideas: PersonalIdeaService = Depends(get_personal_idea_service)) -> dict:
    """Refresh scheduling resources without reading notes/questions or resume files."""
    response.headers["Cache-Control"] = "no-store"
    return {"schema_version": 1, "user_id": request.state.user.id, "is_admin": request.state.user.is_admin,
            "server_time": datetime.now(timezone.utc).isoformat(),
            "events": [e.model_dump(mode="json", exclude={"raw_invitation"}) for e in schedule.list_events()],
            "todos": [t.model_dump(mode="json") for t in todos.list_todos()],
            "ideas": _ideas_payload(ideas, schedule), "profile": _profile(request.state.user)}


class NativeIdeaInput(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    note: str = Field(default="", max_length=20000)
    reference_links: list[ReferenceLink] = Field(default_factory=list, max_length=20)


@router.post("/ideas", status_code=201)
@router.put("/ideas/{idea_id}")
def save_idea(payload: NativeIdeaInput, idea_id: str | None = None,
              ideas: PersonalIdeaService = Depends(get_personal_idea_service)) -> dict:
    try:
        return ideas.save(**payload.model_dump(mode="json"), idea_id=idea_id).model_dump(mode="json")
    except LookupError as error:
        raise HTTPException(404, str(error)) from error
    except ValueError as error:
        raise _event_error(error) from error


@router.post("/ideas/{idea_id}/abandon")
def abandon_idea(idea_id: str, ideas: PersonalIdeaService = Depends(get_personal_idea_service)) -> dict:
    try:
        return ideas.abandon(idea_id).model_dump(mode="json")
    except LookupError as error:
        raise HTTPException(404, str(error)) from error


class NativeTodoInput(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    due_date: date


@router.post("/todo-sync/{operation_id}")
def sync_native_todo(operation_id: UUID, payload: TodoSyncInput, response: Response,
                     todos: PersonalTodoService = Depends(get_personal_todo_service)) -> dict:
    """Accept one encrypted Android outbox command and return the committed effect."""
    response.headers["Cache-Control"] = "no-store"
    try:
        return todos.apply_sync(str(operation_id), payload)
    except LookupError as error:
        raise HTTPException(404, str(error)) from error
    except ValueError as error:
        raise _event_error(error) from error


@router.put("/todos", status_code=201)
def create_native_todo(payload: NativeTodoInput,
                       todos: PersonalTodoService = Depends(get_personal_todo_service)) -> dict:
    if not payload.title.strip():
        raise HTTPException(422, "请输入待办内容")
    return todos.create(payload.title, payload.due_date).model_dump(mode="json")


@router.post("/todos/{todo_id}/completion")
def complete_native_todo(todo_id: str, payload: NativeCompletionInput,
                         todos: PersonalTodoService = Depends(get_personal_todo_service)) -> dict:
    try:
        return todos.set_completed(todo_id, payload.completed).model_dump(mode="json")
    except LookupError as error:
        raise HTTPException(404, str(error)) from error


@router.get("/session")
def session(request: Request, response: Response) -> dict:
    """Give the native client the current session's CSRF token for task writes."""
    response.headers["Cache-Control"] = "no-store"
    return {"user_id": request.state.user.id, "csrf_token": request.state.csrf_token, "is_admin": request.state.user.is_admin}


@router.post("/todos", status_code=201)
def create_todo(title: str = Form(min_length=1, max_length=200), due_date: date = Form(),
                todos: PersonalTodoService = Depends(get_personal_todo_service)) -> dict:
    if not title.strip():
        raise HTTPException(422, "请输入待办内容")
    return todos.create(title, due_date).model_dump(mode="json")


@router.post("/todos/{todo_id}/toggle")
def toggle_todo(todo_id: str, todos: PersonalTodoService = Depends(get_personal_todo_service)) -> dict:
    try:
        return todos.toggle(todo_id).model_dump(mode="json")
    except LookupError as error:
        raise HTTPException(404, str(error)) from error


@router.delete("/todos/{todo_id}", status_code=204)
def delete_todo(todo_id: str, todos: PersonalTodoService = Depends(get_personal_todo_service)) -> None:
    if not todos.delete(todo_id):
        raise HTTPException(404, "待办不存在")


# Inherit the authenticated mobile router dependencies for every native workspace route.
from app.controller.native_workspace_controller import router as workspace_router
router.include_router(workspace_router)
