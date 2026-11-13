from datetime import date, datetime, timedelta
from typing import Any
from itertools import zip_longest
from hashlib import sha256
from zoneinfo import ZoneInfo

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Request, UploadFile
from fastapi.responses import HTMLResponse, RedirectResponse, JSONResponse
from pydantic import ValidationError
from starlette.concurrency import run_in_threadpool

from app.controller.page_controller import WEEKDAY_NAMES, _assign_entry_columns, templates
from app.core.dependencies import (
    get_interview_experience_service, get_my_resume_mapper,
    get_personal_schedule_service, get_resume_parser_service, get_personal_resume_state_service,
)
from app.core.exceptions import MeetingParseError, PersonalEventConflictWarning, ResumeParseError
from app.mapper.base_my_resume_mapper import BaseMyResumeMapper
from app.model.my_resume_profile import MyResumeProfile
from app.model.personal_event import PersonalEvent, ReferenceLink
from app.service.personal_schedule_service import PersonalScheduleService
from app.service.interview_experience_service import InterviewExperienceService
from app.model.interview_experience import CATEGORY_LABELS
from app.service.resume_parser_service import ResumeParserService
from app.service.personal_resume_service import PersonalResumeStateService
from app.core.dependencies import get_personal_idea_service
from app.service.personal_idea_service import PersonalIdeaService
from uuid import UUID
from app.controller.schema.calendar_sync_dto import CalendarSyncInput
from app.service.calendar_sync_service import CalendarSyncService

router = APIRouter(prefix="/my-schedule", tags=["personal-schedule"])
DISPLAY_ZONE = ZoneInfo("Asia/Shanghai")
EVENT_LABELS = {
    "interview": "面试",
    "exam": "笔试／考试",
    "course": "课程",
    "recruiting": "招聘活动",
    "meeting": "普通会议",
    "personal": "个人事项",
}


def _reference_form(form) -> list[dict[str, str]]:
    return [{"title": str(title).strip(), "url": str(url).strip()}
            for title, url in zip_longest(form.getlist("reference_title"), form.getlist("reference_url"), fillvalue="")
            if str(title).strip() or str(url).strip()]


def _calendar_event_data(event: PersonalEvent) -> dict[str, Any]:
    """Only calendar metadata belongs in the browser snapshot; never resume/raw files."""
    return event.model_dump(mode="json", exclude={"raw_invitation", "resume_id", "attachment_refs", "owner_id"})


def _calendar_payload(owner_id: str, events: list[PersonalEvent], workday_start: str = "08:00",
                      workday_end: str = "18:00") -> dict[str, Any]:
    return {"schema_version": 1, "user_id": owner_id,
            "server_time": datetime.now(DISPLAY_ZONE).isoformat(),
            "workday_start": workday_start, "workday_end": workday_end,
            "events": [_calendar_event_data(event) for event in events]}


def _calendar_mutation(request: Request, service: PersonalScheduleService,
                       event: PersonalEvent | None = None, deleted_id: str | None = None):
    if request.headers.get("x-calendar-cache") != "1":
        return None
    # A repeated occurrence is patched through its parent rule, not stored as a new task.
    if event is not None and "~" in event.id:
        event = service.get_event(event.id.split("~", 1)[0])
    return JSONResponse({"schema_version": 1, "user_id": service.owner_id,
                         "server_time": datetime.now(DISPLAY_ZONE).isoformat(),
                         "event": _calendar_event_data(event) if event else None,
                         "deleted_id": deleted_id.split("~", 1)[0] if deleted_id else None},
                        headers={"Cache-Control": "no-store"})


def _event_view(event: PersonalEvent, now: datetime) -> dict[str, Any]:
    start = event.start_at.astimezone(DISPLAY_ZONE)
    end = event.end_at.astimezone(DISPLAY_ZONE)
    status = "finished" if now >= end else "ongoing" if now >= start else "upcoming"
    return {
        **event.model_dump(mode="json"),
        "start_time": start,
        "end_time": end,
        "completed_time": event.completed_at.astimezone(DISPLAY_ZONE) if event.completed_at else None,
        "start_minutes": start.hour * 60 + start.minute,
        "duration_minutes": int((end - start).total_seconds() // 60),
        "status": status,
        "status_label": {"finished": "已结束", "ongoing": "进行中", "upcoming": "即将开始"}[status],
        "display_name": event.title,
        "type_label": EVENT_LABELS[event.type],
    }


def _week_day_segments(events: list[dict[str, Any]], day: date) -> list[dict[str, Any]]:
    """Clip events to a local calendar day for the 24-hour week timeline."""
    day_start = datetime.combine(day, datetime.min.time(), DISPLAY_ZONE)
    day_end = day_start + timedelta(days=1)
    segments = []
    for event in events:
        start = event["start_time"]
        end = event["end_time"]
        if (event.get("skip_weekends") and day.weekday() >= 5) or start >= day_end or end <= day_start:
            continue
        clipped_start = max(start, day_start)
        clipped_end = min(end, day_end)
        if event.get("workday_only"):
            work_start = datetime.fromisoformat(f"{day}T{event.get('display_workday_start', '08:00')}").replace(tzinfo=DISPLAY_ZONE)
            work_end = datetime.fromisoformat(f"{day}T{event.get('display_workday_end', '18:00')}").replace(tzinfo=DISPLAY_ZONE)
            clipped_start, clipped_end = max(clipped_start, work_start), min(clipped_end, work_end)
        if clipped_start >= clipped_end:
            continue
        start_minutes = int((clipped_start - day_start).total_seconds() // 60)
        end_minutes = int((clipped_end - day_start).total_seconds() // 60)
        segments.append({
            **event,
            **({"start_time": clipped_start, "end_time": clipped_end} if event.get("workday_only") else {}),
            "start_minutes": start_minutes,
            "duration_minutes": end_minutes - start_minutes,
            "continues_before": start < day_start,
            "continues_after": end > day_end,
        })
    _assign_entry_columns(segments)
    return segments


def _month_day_entries(events: list[dict[str, Any]], dates: list[date]) -> dict[date, list[dict[str, Any]]]:
    """Keep each event on one lane across a month calendar's weekly row."""
    entries: dict[date, list[dict[str, Any]]] = {day: [] for day in dates}
    for offset in range(0, len(dates), 7):
        week = dates[offset:offset + 7]
        week_start = datetime.combine(week[0], datetime.min.time(), DISPLAY_ZONE)
        week_end = week_start + timedelta(days=len(week))
        visible = sorted(
            (item for item in events if item["start_time"] < week_end and item["end_time"] > week_start),
            key=lambda item: (item["start_time"], item["end_time"], item["id"]),
        )
        lane_ends: list[int] = []
        for item in visible:
            first = max(0, (item["start_time"].date() - week[0]).days)
            last = min(len(week) - 1, ((item["end_time"] - timedelta(microseconds=1)).date() - week[0]).days)
            lane = next((index for index, end in enumerate(lane_ends) if end < first), len(lane_ends))
            if lane == len(lane_ends):
                lane_ends.append(last)
            else:
                lane_ends[lane] = last
            for index in range(first, last + 1):
                if item.get("skip_weekends") and week[index].weekday() >= 5:
                    continue
                if item.get("workday_only") and not _week_day_segments([item], week[index]):
                    continue
                day_start = week_start + timedelta(days=index)
                entries[week[index]].append({
                    **item, "month_lane": lane,
                    "continues_before": item["start_time"] < day_start,
                    "continues_after": item["end_time"] > day_start + timedelta(days=1),
                })
    return entries


def _dashboard_context(service: PersonalScheduleService, selected_date: date,
                       selected_view: str = "day", events: list[PersonalEvent] | None = None,
                       show_completed: bool = True, workday_start: str = "08:00",
                       workday_end: str = "18:00") -> dict[str, Any]:
    now = datetime.now(DISPLAY_ZONE)
    all_events = service.list_events() if events is None else events
    first = selected_date.replace(day=1)
    grid_start = first - timedelta(days=first.weekday())
    days = {grid_start + timedelta(days=offset) for offset in range(42)}
    days.update(now.date() + timedelta(days=offset) for offset in range(8))
    days.add(selected_date)
    expanded = []
    for item in all_events:
        if item.repeat_weekdays:
            expanded.extend(occurrence for day in sorted(days)
                            if (occurrence := service.repeat_occurrence(item, day)) is not None)
        else:
            expanded.append(item)
    event_views = [{**_event_view(item, now), "display_workday_start": workday_start,
                    "display_workday_end": workday_end} for item in expanded]
    calendar_views = event_views if show_completed else [item for item in event_views if not item["completed_at"] and not item["cancelled_at"]]
    views = _week_day_segments(calendar_views, selected_date)
    earliest = min((item["start_minutes"] // 60 for item in views), default=6)
    latest = max(((item["start_minutes"] + item["duration_minutes"] + 59) // 60
                  for item in views), default=23)
    start_hour = min(6, earliest)
    end_hour = max(23, latest)
    now_minutes = now.hour * 60 + now.minute
    today_start = datetime.combine(now.date(), datetime.min.time(), DISPLAY_ZONE)
    tomorrow_start = today_start + timedelta(days=1)
    remaining = [item for item in event_views
                 if not item["completed_at"] and not item["cancelled_at"] and (item["end_time"] > now or (item["repeat_weekdays"] and item["start_time"].date() == now.date()))]
    overdue = sorted((item for item in event_views if not item["completed_at"]
                      and not item["cancelled_at"] and not item["repeat_weekdays"] and item["end_time"] <= now),
                     key=lambda item: item["end_time"])
    upcoming_today = [item for item in remaining if item["start_time"] < tomorrow_start and not (item.get("skip_weekends") and now.weekday() >= 5)]
    completed_today = sorted(
        (item for item in event_views if item["completed_time"] and
         item["completed_time"].date() == now.date()),
        key=lambda item: item["completed_time"], reverse=True,
    )
    selected_view = selected_view if selected_view in {"day", "week", "month"} else "day"
    calendar_days: list[dict[str, Any]] = []
    week_bridges: list[int] = []
    if selected_view == "week":
        period_start = selected_date - timedelta(days=selected_date.weekday())
        period_dates = [period_start + timedelta(days=offset) for offset in range(7)]
        prev_date = selected_date - timedelta(days=7)
        next_date = selected_date + timedelta(days=7)
        period_end = datetime.combine(period_dates[-1] + timedelta(days=1),
                                      datetime.min.time(), DISPLAY_ZONE)
        period_begin = datetime.combine(period_start, datetime.min.time(), DISPLAY_ZONE)
        week_count = sum(item["start_time"] < period_end and
                         item["end_time"] > period_begin for item in calendar_views)
        period_title = f"{period_dates[0]:%Y-%m-%d} — {period_dates[-1]:%m-%d} · {week_count} 项安排"
        week_bridges = [
            index + 1 for index, day in enumerate(period_dates[:-1])
            if any(item["start_time"] < datetime.combine(day + timedelta(days=1),
                                                          datetime.min.time(), DISPLAY_ZONE)
                   < item["end_time"] and not (item.get("skip_weekends") and (day.weekday() >= 4)) for item in calendar_views)
        ]
    elif selected_view == "month":
        first = selected_date.replace(day=1)
        period_start = first - timedelta(days=first.weekday())
        period_dates = [period_start + timedelta(days=offset) for offset in range(42)]
        prev_date = (first - timedelta(days=1)).replace(day=1)
        next_date = (first + timedelta(days=32)).replace(day=1)
        month_start = datetime.combine(first, datetime.min.time(), DISPLAY_ZONE)
        month_end = datetime.combine(next_date, datetime.min.time(), DISPLAY_ZONE)
        month_count = sum(item["start_time"] < month_end and item["end_time"] > month_start
                          for item in calendar_views)
        period_title = f"{first.year} 年 {first.month} 月 · {month_count} 项安排"
    else:
        period_dates = []
        prev_date = selected_date - timedelta(days=1)
        next_date = selected_date + timedelta(days=1)
        period_title = f"{selected_date} {WEEKDAY_NAMES[selected_date.weekday()]} · {len(views)} 项安排"
    if period_dates:
        events_by_day = _month_day_entries(calendar_views, period_dates) if selected_view == "month" else {}
        month_week_lanes: dict[date, int] = {}
        if selected_view == "month":
            for offset in range(0, len(period_dates), 7):
                week = period_dates[offset:offset + 7]
                lanes = max((item["month_lane"] + 1 for day in week
                             for item in events_by_day[day]), default=0)
                month_week_lanes.update(dict.fromkeys(week, lanes))
        calendar_days = [{
            "date": day,
            "entries": events_by_day.get(day, []),
            "month_lane_count": month_week_lanes.get(day, 0),
            "week_segments": _week_day_segments(calendar_views, day) if selected_view == "week" else [],
            "in_month": day.month == selected_date.month,
            "is_today": day == now.date(),
            "is_selected": day == selected_date,
        } for day in period_dates]
    board_rows: dict[str, int] = {}
    originals_by_id = {item["id"]: item for item in calendar_views}
    if selected_view == "week":
        board_items = sorted((item for day in calendar_days for item in day["week_segments"]),
                             key=lambda item: (item["start_time"].date() == item["end_time"].date(),
                                               item["start_time"], item["id"]))
        for item in board_items:
            board_rows.setdefault(item["id"].split("~", 1)[0], len(board_rows) + 1)
        for day in calendar_days:
            for item in day["week_segments"]:
                item["board_key"] = item["id"].split("~", 1)[0]
                item["board_row"] = board_rows[item["board_key"]]
                original = originals_by_id[item["id"]]
                item["board_before"] = item["continues_before"] and bool(_week_day_segments([original], day["date"] - timedelta(days=1)))
                item["board_after"] = item["continues_after"] and bool(_week_day_segments([original], day["date"] + timedelta(days=1)))
                item["board_start"] = f"{item['start_minutes'] // 60:02d}:{item['start_minutes'] % 60:02d}"
                end_minutes = item["start_minutes"] + item["duration_minutes"]
                item["board_end"] = f"{end_minutes // 60:02d}:{end_minutes % 60:02d}"
    summary_days = period_dates if selected_view == "week" else [selected_date]
    cross_day_summary = []
    if selected_view != "month":
        for item in calendar_views:
            if item["start_time"].date() == (item["end_time"] - timedelta(microseconds=1)).date():
                continue
            indexes = [i for i, day in enumerate(summary_days) if _week_day_segments([item], day)]
            if indexes:
                cross_day_summary.append({**item, "summary_first": min(indexes) + 1,
                                          "summary_last": max(indexes) + 2})
    return {
        "calendar_bootstrap": _calendar_payload(service.owner_id, all_events, workday_start, workday_end),
        "board_row_count": len(board_rows),
        "cross_day_summary": cross_day_summary,
        "selected_date": selected_date,
        "today_date": now.date(),
        "selected_view": selected_view,
        "show_completed": show_completed,
        "period_title": period_title,
        "calendar_days": calendar_days,
        "week_bridges": week_bridges,
        "selected_weekday": WEEKDAY_NAMES[selected_date.weekday()],
        "prev_date": prev_date.isoformat(),
        "next_date": next_date.isoformat(),
        "entries": views,
        "upcoming_today": upcoming_today,
        "overdue": overdue,
        "next_deadline": min((item["end_time"] for item in remaining), default=None),
        "upcoming_all": remaining,
        "completed_today": completed_today,
        "time_labels": [f"{hour:02d}:00" for hour in range(start_hour, end_hour + 1)],
        "start_hour": start_hour,
        "end_hour": end_hour,
        "grid_height": (end_hour - start_hour) * 88,
        "show_now_line": selected_date == now.date() and start_hour * 60 <= now_minutes <= end_hour * 60,
        "now_minutes": now_minutes,
        "now_time": now.strftime("%H:%M"),
        "event_labels": EVENT_LABELS,
    }


def _new_page(request: Request, *, error: str | None = None, warnings: list[str] | None = None,
              values: dict[str, Any] | None = None, preview: bool = False, status_code: int = 200):
    form_values = dict(values or {})
    now = datetime.now(DISPLAY_ZONE).replace(second=0, microsecond=0)
    user = getattr(request.state, "user", None)
    work_start = user.workday_start if user else "08:00"
    work_end = user.workday_end if user else "18:00"
    date_only = not form_values.get("start_at")
    form_values.setdefault("start_at", f"{now.date().isoformat()}T{work_start}")
    form_values.setdefault("end_at", f"{now.date().isoformat()}T{work_end}")
    if "skip_weekends" not in form_values:
        try:
            form_values["skip_weekends"] = datetime.fromisoformat(form_values["start_at"]).weekday() < 5
        except ValueError:
            form_values["skip_weekends"] = False
    return templates.TemplateResponse(request, "personal_new.html", {
        "error": error,
        "warnings": warnings or [],
        "values": form_values,
        "preview": preview,
        "date_only": date_only,
        "event_labels": EVENT_LABELS,
    }, status_code=status_code)


@router.get("", response_class=HTMLResponse)
def personal_dashboard(
    request: Request,
    date_value: str | None = None,
    view: str = "day",
    show_all: bool = False,
    show_completed: bool = True,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    try:
        selected = date.fromisoformat(date_value) if date_value else datetime.now(DISPLAY_ZONE).date()
    except ValueError:
        selected = datetime.now(DISPLAY_ZONE).date()
    # Always render both sets once; the calendar-only toggle reuses this DOM locally.
    user = getattr(request.state, "user", None)
    context = _dashboard_context(service, selected, view,
        workday_start=user.workday_start if user else "08:00",
        workday_end=user.workday_end if user else "18:00")
    context["show_all"] = show_all
    return templates.TemplateResponse(request, "personal_dashboard.html", context)


@router.get("/calendar-data")
def personal_calendar_data(request: Request,
                           service: PersonalScheduleService = Depends(get_personal_schedule_service)):
    user = getattr(request.state, "user", None)
    work_start, work_end = (user.workday_start, user.workday_end) if user else ("08:00", "18:00")
    events = service.list_events()
    payload = _calendar_payload(service.owner_id, events, work_start, work_end)
    # The empty SSR workspace is a reusable shell, not a separate request per date.
    context = _dashboard_context(service, datetime.now(DISPLAY_ZONE).date(), events=[],
                                 workday_start=work_start, workday_end=work_end)
    context.update(request=request, show_all=False, calendar_bootstrap=None)
    payload["shell"] = templates.get_template("personal_dashboard.html").render(context)
    return JSONResponse(payload, headers={"Cache-Control": "no-store"})


@router.post("/calendar-sync/{operation_id}")
def sync_personal_calendar(operation_id: UUID, command: CalendarSyncInput, request: Request,
                           background: BackgroundTasks,
                           service: PersonalScheduleService = Depends(get_personal_schedule_service),
                           ideas: PersonalIdeaService = Depends(get_personal_idea_service)):
    try:
        result = CalendarSyncService.apply(service, ideas, str(operation_id), command)
    except PersonalEventConflictWarning as exc:
        return JSONResponse({"detail": "日程时间有重叠", "conflicts": exc.event_titles}, status_code=409)
    except LookupError as exc:
        raise HTTPException(404, str(exc)) from exc
    except (ValueError, MeetingParseError) as exc:
        raise HTTPException(422, str(exc)) from exc
    if command.action in {"create", "edit"} and result["event"]:
        def enrich():
            event = service.get_event(result["event"]["id"])
            if event:
                request.app.state.reference_titles.enrich_event(service, event)
        background.add_task(enrich)
    return JSONResponse(result, headers={"Cache-Control": "no-store"})


@router.post("/events/{event_id}/completion")
async def set_personal_event_completion(
    event_id: str, request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    form = await request.form()
    completed = str(form.get("completed", ""))
    if completed not in {"true", "false"}:
        raise HTTPException(status_code=422, detail="完成状态无效")
    try:
        event = service.set_event_completed(event_id, completed == "true")
    except LookupError as exc:
        raise HTTPException(status_code=404, detail="日程不存在") from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    if result := _calendar_mutation(request, service, event):
        return result
    view = str(form.get("view", "day"))
    view = view if view in {"day", "week", "month"} else "day"
    try:
        selected = date.fromisoformat(str(form.get("date_value", "")))
    except ValueError:
        selected = datetime.now(DISPLAY_ZONE).date()
    show_all = "&show_all=1" if str(form.get("show_all", "")) == "1" else ""
    show_completed = "&show_completed=false" if str(form.get("show_completed", "")) == "false" else ""
    return RedirectResponse(
        f"/my-schedule?view={view}&date_value={selected.isoformat()}{show_all}{show_completed}", status_code=303,
    )


@router.post("/events/{event_id}/overdue")
async def resolve_personal_overdue(
    event_id: str, request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    form = await request.form()
    try:
        end_at = datetime.fromisoformat(str(form["end_at"])) if form.get("end_at") else None
        if end_at is not None and end_at.tzinfo is None:
            end_at = end_at.replace(tzinfo=DISPLAY_ZONE)
        event = await run_in_threadpool(service.resolve_overdue, event_id, str(form.get("action", "")), end_at)
    except LookupError as exc:
        raise HTTPException(status_code=404, detail="日程不存在") from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    if result := _calendar_mutation(request, service, event):
        return result
    return RedirectResponse("/my-schedule", status_code=303)


@router.get("/new", response_class=HTMLResponse)
def new_personal_event(request: Request):
    return _new_page(request)


@router.get("/ideas/{idea_id}/arrange", response_class=HTMLResponse)
def arrange_personal_idea(idea_id: str, request: Request,
                          ideas: PersonalIdeaService = Depends(get_personal_idea_service)):
    try:
        idea = ideas.get(idea_id)
    except LookupError as exc:
        raise HTTPException(404, str(exc)) from exc
    if idea.status != "pending":
        raise HTTPException(409, "已放弃事项不能安排")
    return _new_page(request, values={"title": idea.title, "note": idea.note,
        "reference_links": [link.model_dump(mode="json") for link in idea.reference_links],
        "source_idea_id": idea.id})


@router.get("/ideas/summary", response_class=JSONResponse)
def personal_ideas_summary(ideas: PersonalIdeaService = Depends(get_personal_idea_service),
                           schedule: PersonalScheduleService = Depends(get_personal_schedule_service)):
    return JSONResponse(ideas.pending_summary(schedule), headers={"Cache-Control": "no-store"})


@router.get("/ideas", response_class=HTMLResponse)
def personal_ideas(request: Request, abandoned: bool = False, edit: str | None = None,
                   ideas: PersonalIdeaService = Depends(get_personal_idea_service),
                   schedule: PersonalScheduleService = Depends(get_personal_schedule_service)):
    try:
        selected = ideas.get(edit) if edit else None
    except LookupError as exc:
        raise HTTPException(404, str(exc)) from exc
    if selected and selected.status != "pending":
        raise HTTPException(409, "已放弃事项不能修改")
    items = ideas.list(abandoned)
    # Only the deterministic event ids are needed, so the ideas page no longer loads every event.
    scheduled = {} if abandoned else ideas.arranged_event_ids(schedule, items)
    return templates.TemplateResponse(request, "personal_ideas.html", {
        "ideas": items, "abandoned": abandoned, "selected": selected,
        "values": selected, "error": None, "scheduled": scheduled})


@router.post("/ideas", response_class=HTMLResponse)
async def save_personal_idea(request: Request, ideas: PersonalIdeaService = Depends(get_personal_idea_service)):
    form = await request.form()
    values = {"title": str(form.get("title", "")), "note": str(form.get("note", "")),
              "reference_links": _reference_form(form)}
    idea_id = str(form.get("idea_id", "")) or None
    try:
        await run_in_threadpool(ideas.save, **values, idea_id=idea_id)
    except LookupError as exc:
        raise HTTPException(404, str(exc)) from exc
    except ValueError as exc:
        return templates.TemplateResponse(request, "personal_ideas.html", {
            "ideas": ideas.list(), "abandoned": False, "selected": {"id": idea_id} if idea_id else None,
            "values": values, "error": str(exc), "scheduled": {}}, status_code=422)
    return RedirectResponse("/my-schedule/ideas", status_code=303)


@router.post("/ideas/{idea_id}/abandon")
def abandon_personal_idea(idea_id: str, ideas: PersonalIdeaService = Depends(get_personal_idea_service)):
    try:
        ideas.abandon(idea_id)
    except LookupError as exc:
        raise HTTPException(404, str(exc)) from exc
    return RedirectResponse("/my-schedule/ideas", status_code=303)


@router.post("/parse", response_class=HTMLResponse)
async def parse_personal_invitation(
    request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    form = await request.form()
    raw_text = str(form.get("raw_text", ""))
    try:
        event = service.from_invitation(raw_text)
    except (MeetingParseError, ValidationError, ValueError) as exc:
        return _new_page(request, error=str(exc), values={"raw_text": raw_text}, status_code=422)
    return _new_page(request, preview=True, values={
        "source": "invitation",
        "raw_text": raw_text,
        "title": event.title,
        "event_type": event.type,
        "start_at": event.start_at.astimezone(DISPLAY_ZONE).strftime("%Y-%m-%dT%H:%M"),
        "end_at": event.end_at.astimezone(DISPLAY_ZONE).strftime("%Y-%m-%dT%H:%M"),
        "timezone": event.timezone,
        "platform": event.platform or "",
        "meeting_url": str(event.meeting_url) if event.meeting_url else "",
        "position": event.position or "",
    })


@router.post("/events", response_class=HTMLResponse)
async def create_personal_event(
    request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
    ideas: PersonalIdeaService = Depends(get_personal_idea_service),
):
    form = await request.form()
    values = {key: str(value) for key, value in form.items()}
    values["repeat_weekdays"] = [int(day) for day in form.getlist("repeat_weekdays") if str(day).isdigit()] if form.get("repeat_enabled") == "true" else []
    if form.get("repeat_enabled") == "true" and not values["repeat_weekdays"]:
        values["repeat_weekdays"] = [-1]
    values["skip_weekends"] = form.get("skip_weekends") == "true"
    values["workday_only"] = form.get("workday_only") == "true"
    values["reference_links"] = _reference_form(form)
    values["reminder_offsets_minutes"] = form.getlist("reminder_offsets_minutes")
    preview = values.get("source") == "invitation"
    source_idea_id = values.get("source_idea_id")
    if source_idea_id:
        try:
            idea = await run_in_threadpool(ideas.get, source_idea_id)
        except LookupError as exc:
            raise HTTPException(404, str(exc)) from exc
        if idea.status != "pending" or preview:
            raise HTTPException(409, "该事项不能安排")
    try:
        start = datetime.fromisoformat(values["start_at"])
        end = datetime.fromisoformat(values["end_at"])
        common = {
            "title": values["title"],
            "event_type": values["event_type"],
            "skip_weekends": values["skip_weekends"],
            "workday_only": values["workday_only"],
            "repeat_weekdays": values["repeat_weekdays"],
            "timezone": values.get("timezone") or "Asia/Shanghai",
            "position": values.get("position") or None,
            "note": values.get("note") or None,
            "reminder_offsets_minutes": [int(value) for value in values["reminder_offsets_minutes"]],
        }
        confirmed = values.get("confirm_overlap") == "true"
        if preview:
            event = service.create_from_invitation(
                values.get("raw_text", ""),
                confirm_overlap=confirmed,
                title=common["title"], event_type=common["event_type"], skip_weekends=common["skip_weekends"], workday_only=common["workday_only"], repeat_weekdays=common["repeat_weekdays"],
                timezone=common["timezone"], position=common["position"], note=common["note"],
                reminder_offsets_minutes=common["reminder_offsets_minutes"],
                start_at=start, end_at=end,
                platform=values.get("platform") or None,
                meeting_url=values.get("meeting_url") or None,
                location_text=values.get("location_text") or None,
                reference_links=values["reference_links"],
            )
        else:
            event = service.create_manual(
                title=common["title"], event_type=common["event_type"], skip_weekends=common["skip_weekends"], workday_only=common["workday_only"], repeat_weekdays=common["repeat_weekdays"],
                start_at=start, end_at=end, timezone=common["timezone"],
                location_text=values.get("location_text") or None,
                location_url=values.get("location_url") or None,
                reference_links=values["reference_links"],
                note=common["note"], position=common["position"],
                source_idea_id=source_idea_id,
                reminder_offsets_minutes=common["reminder_offsets_minutes"],
                confirm_overlap=confirmed,
            )
    except PersonalEventConflictWarning as exc:
        return _new_page(request, warnings=exc.event_titles, values=values,
                         preview=preview, status_code=409)
    except (KeyError, ValueError, ValidationError, MeetingParseError) as exc:
        return _new_page(request, error=str(exc), values=values,
                         preview=preview, status_code=422)
    request.app.state.reference_titles.enrich_event(service, event)
    if result := _calendar_mutation(request, service, event):
        return result
    event_day = event.start_at.astimezone(DISPLAY_ZONE).date().isoformat()
    return RedirectResponse(url=f"/my-schedule?view=day&date_value={event_day}", status_code=303)


@router.get("/events/{event_id}", response_class=HTMLResponse)
def personal_event_detail(
    event_id: str, request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    event = service.get_event(event_id)
    if not event:
        if request.app.state.settings.app_mode == "public_personal":
            raise HTTPException(status_code=404, detail="日程不存在")
        return RedirectResponse(url="/my-schedule", status_code=303)
    return templates.TemplateResponse(request, "personal_detail.html", {
        "event": event,
        "event_labels": EVENT_LABELS,
        "error": None,
        "warnings": [],
        "editing": False,
    })


@router.post("/events/{event_id}/close-repeat")
def close_personal_repeat(event_id: str, request: Request, service: PersonalScheduleService = Depends(get_personal_schedule_service)):
    try:
        event = service.close_repeat(event_id)
    except LookupError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    if result := _calendar_mutation(request, service, event):
        return result
    return RedirectResponse("/my-schedule", status_code=303)


@router.get("/events/{event_id}/reference-titles")
def personal_reference_titles(event_id: str, request: Request,
                              service: PersonalScheduleService = Depends(get_personal_schedule_service)):
    event = service.get_event(event_id)
    if event is None:
        raise HTTPException(status_code=404, detail="日程不存在")
    rows = []
    for index, link in enumerate(event.reference_links):
        title, pending = (link.title, False) if link.title else request.app.state.reference_titles.lookup(str(link.url))
        rows.append({"index": index, "title": title, "pending": pending})
    return JSONResponse({"links": rows}, headers={"Cache-Control": "no-store"})


@router.get("/reference-title")
def preview_reference_title(request: Request, url: str):
    try:
        link = ReferenceLink(url=url)
    except ValidationError as exc:
        raise HTTPException(status_code=422, detail="参考链接无效") from exc
    title, pending = request.app.state.reference_titles.lookup(str(link.url))
    return JSONResponse({"title": title, "pending": pending}, headers={"Cache-Control": "no-store"})


@router.get("/events/{event_id}/edit", response_class=HTMLResponse)
def personal_event_edit_page(
    event_id: str, request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    event = service.get_event(event_id.split("~", 1)[0])
    if not event:
        raise HTTPException(status_code=404, detail="日程不存在")
    return templates.TemplateResponse(request, "personal_detail.html", {
        "event": event, "event_labels": EVENT_LABELS,
        "error": None, "warnings": [], "editing": True,
    })


@router.post("/events/{event_id}/edit", response_class=HTMLResponse)
async def edit_personal_event(
    event_id: str, request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    form = await request.form()
    values = {key: str(value) for key, value in form.items()}
    values["repeat_weekdays"] = [int(day) for day in form.getlist("repeat_weekdays") if str(day).isdigit()] if form.get("repeat_enabled") == "true" else []
    if form.get("repeat_enabled") == "true" and not values["repeat_weekdays"]:
        values["repeat_weekdays"] = [-1]
    values["skip_weekends"] = form.get("skip_weekends") == "true"
    values["workday_only"] = form.get("workday_only") == "true"
    values["reference_links"] = _reference_form(form)
    values["reminder_offsets_minutes"] = form.getlist("reminder_offsets_minutes")
    try:
        event = service.update_event(
            event_id,
            confirm_overlap=values.get("confirm_overlap") == "true",
            title=values["title"].strip(),
            type=values["event_type"],
            skip_weekends=values["skip_weekends"],
            workday_only=values["workday_only"],
            repeat_weekdays=values["repeat_weekdays"],
            start_at=datetime.fromisoformat(values["start_at"]),
            end_at=datetime.fromisoformat(values["end_at"]),
            timezone=values.get("timezone") or "Asia/Shanghai",
            location_text=values.get("location_text") or None,
            location_url=values.get("location_url") or None,
            reference_links=values["reference_links"],
            note=values.get("note") or None,
            position=values.get("position") or None,
            reminder_offsets_minutes=[int(value) for value in values["reminder_offsets_minutes"]],
        )
    except PersonalEventConflictWarning as exc:
        current = service.get_event(event_id)
        return templates.TemplateResponse(request, "personal_detail.html", {
            "event": current, "event_labels": EVENT_LABELS,
            "error": None, "warnings": exc.event_titles, "values": values, "editing": True,
        }, status_code=409)
    except LookupError as exc:
        if request.app.state.settings.app_mode == "public_personal":
            raise HTTPException(status_code=404, detail="日程不存在") from exc
        return RedirectResponse(url="/my-schedule", status_code=303)
    except (KeyError, ValueError, ValidationError) as exc:
        current = service.get_event(event_id)
        return templates.TemplateResponse(request, "personal_detail.html", {
            "event": current, "event_labels": EVENT_LABELS,
            "error": str(exc), "warnings": [], "values": values, "editing": True,
        }, status_code=422)
    request.app.state.reference_titles.enrich_event(service, event)
    if result := _calendar_mutation(request, service, event):
        return result
    if request.headers.get("x-event-dialog") == "1":
        return JSONResponse({"saved": True})
    event_day = event.start_at.astimezone(DISPLAY_ZONE).date().isoformat()
    return RedirectResponse(url=f"/my-schedule?view=day&date_value={event_day}", status_code=303)


@router.post("/events/{event_id}/delete")
def delete_personal_event(
    event_id: str,
    request: Request,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    deleted = service.delete_event(event_id)
    if not deleted and request.app.state.settings.app_mode == "public_personal":
        raise HTTPException(status_code=404, detail="日程不存在")
    if result := _calendar_mutation(request, service, deleted_id=event_id):
        return result
    return RedirectResponse(url="/my-schedule", status_code=303)


@router.get("/interviews", response_class=HTMLResponse)
def personal_interview_workspace(
    request: Request, date_value: str | None = None,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
):
    try:
        selected = date.fromisoformat(date_value) if date_value else datetime.now(DISPLAY_ZONE).date()
    except ValueError:
        selected = datetime.now(DISPLAY_ZONE).date()
    context = _interview_workspace_context(service, selected,
        getattr(request.state.user, "workday_start", "08:00") if hasattr(request.state, "user") else "08:00",
        getattr(request.state.user, "workday_end", "18:00") if hasattr(request.state, "user") else "18:00")
    context["resume_cache_scope"] = _resume_cache_scope(request)
    return templates.TemplateResponse(request, "personal_interviews.html", context)


@router.get("/interviews/{event_id}", response_class=HTMLResponse)
def personal_interview_questions(
    event_id: str, request: Request,
    category: str = "all",
    experience_id: str | None = None,
    saved: int = 0,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
    experience_service: InterviewExperienceService = Depends(get_interview_experience_service),
):
    """Show the selected interview beside the resume with its question area."""
    event = service.get_event(event_id)
    if not event:
        if request.app.state.settings.app_mode == "public_personal":
            raise HTTPException(status_code=404, detail="日程不存在")
        return RedirectResponse(url="/my-schedule/interviews", status_code=303)
    if event.type != "interview":
        return RedirectResponse(url=f"/my-schedule/events/{event_id}", status_code=303)
    selected = event.start_at.astimezone(DISPLAY_ZONE).date()
    context = {"selected_date": selected}
    context["resume_cache_scope"] = _resume_cache_scope(request)
    context["selected_event"] = _event_view(event, datetime.now(DISPLAY_ZONE))
    experiences = experience_service.for_event(event_id, event=event)
    experience = next((item for item in experiences if item.id == experience_id), None)
    if experience is None:
        experience = experiences[0] if experiences else None
    category = category if category in {"all", "feeling", *CATEGORY_LABELS} else "all"
    context["experience"] = experience
    context["linked_event"] = event
    context["category"] = category
    context["category_labels"] = CATEGORY_LABELS
    context["event_experiences"] = experiences
    context["question_rows"] = experience_service.numbered_questions(experience.id) if experience else []
    context["visible_rows"] = (context["question_rows"] if category == "all" else
                               experience_service.tree_rows(context["question_rows"], category) if category != "feeling" else []) if experience else []
    context["draft_seed"] = {"version": experience.updated_at.isoformat(),
                             "questions": [row["question"].model_dump(mode="json")
                                           for row in context["question_rows"]],
                             "category_minutes": experience.category_minutes,
                             "total_minutes": experience.total_minutes,
                             "feeling": experience.feeling or ""} if experience else None
    context["detail_url"] = f"/my-schedule/interviews/{event_id}?experience_id={experience.id}" if experience else f"/my-schedule/interviews/{event_id}"
    context["saved"] = saved
    return templates.TemplateResponse(request, "personal_interviews.html", context)


def _interview_workspace_context(
    service: PersonalScheduleService,
    selected: date, workday_start: str = "08:00", workday_end: str = "18:00",
) -> dict[str, Any]:
    """Adapt personal events to the original three-column interview layout."""
    events = service.list_events()
    context = _dashboard_context(service, selected, events=events,
                                 workday_start=workday_start, workday_end=workday_end)
    now = datetime.now(DISPLAY_ZONE)
    interview_views = [_event_view(item, now) for item in events if item.type == "interview"]
    context["entries"] = [item for item in interview_views if item["start_time"].date() == selected]
    _assign_entry_columns(context["entries"])
    context["today_interviews"] = [item for item in interview_views if item["start_time"].date() == now.date() and item["status"] != "finished"]
    context["future_interviews"] = [item for item in interview_views if item["start_time"].date() > now.date()]
    context["finished_interviews"] = [item for item in interview_views if item["status"] == "finished"]
    start_hour = min([9, *(item["start_time"].hour for item in context["entries"])])
    end_hour = max([19, *(item["end_time"].hour + (item["end_time"].minute > 0) for item in context["entries"])])
    context["start_hour"] = start_hour
    context["end_hour"] = end_hour
    context["time_labels"] = [
        {"text": f"{minute // 60:02d}:{minute % 60:02d}", "minutes": minute}
        for minute in range(start_hour * 60, end_hour * 60 + 1, 30)
    ]
    context["show_now_line"] = selected == now.date() and start_hour * 60 <= now.hour * 60 + now.minute <= end_hour * 60
    context["now_minutes"] = now.hour * 60 + now.minute
    context["now_time"] = now.strftime("%H:%M")
    context["date_options"] = [
        {"date": day.isoformat(), "label": (
            "昨天" if day == now.date() - timedelta(days=1) else
            "今天" if day == now.date() else
            "明天" if day == now.date() + timedelta(days=1) else
            day.strftime("%m-%d")
        )}
        for day in (selected - timedelta(days=1), selected, selected + timedelta(days=1))
    ]
    return context


def _resume_cache_scope(request: Request) -> str:
    if request.app.state.settings.app_mode != "public_personal":
        return "local"
    return sha256(request.state.csrf_token.encode()).hexdigest()[:32]


@router.get("/resume/current")
def current_resume(request: Request, service: PersonalResumeStateService = Depends(get_personal_resume_state_service)):
    return {"resume": service.current(), "cache_scope": _resume_cache_scope(request)}


@router.post("/resume")
async def upload_personal_resume(
    request: Request,
    resume: UploadFile,
    service: PersonalScheduleService = Depends(get_personal_schedule_service),
    profile_mapper: BaseMyResumeMapper = Depends(get_my_resume_mapper),
    resume_service: ResumeParserService = Depends(get_resume_parser_service),
):
    try:
        parsed = await resume_service.parse_upload(resume, owner_id=service.owner_id)
    except ResumeParseError as exc:
        context = _interview_workspace_context(service, datetime.now(DISPLAY_ZONE).date(),
            getattr(request.state.user, "workday_start", "08:00") if hasattr(request.state, "user") else "08:00",
            getattr(request.state.user, "workday_end", "18:00") if hasattr(request.state, "user") else "18:00")
        context["resume_cache_scope"] = _resume_cache_scope(request)
        context["resume_error"] = str(exc)
        return templates.TemplateResponse(request, "personal_interviews.html", context, status_code=422)
    profile_mapper.save(MyResumeProfile(id=service.profile_id, resume_id=parsed.id))
    return RedirectResponse(url="/my-schedule/interviews", status_code=303)
