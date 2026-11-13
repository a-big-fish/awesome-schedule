from datetime import date, datetime, timedelta
import re
from zoneinfo import ZoneInfo

from fastapi.testclient import TestClient

from app.core.dependencies import get_my_resume_mapper, get_personal_schedule_service, get_resume_parser_service
from app.controller.personal_schedule_controller import _dashboard_context
from app.main import app
from app.mapper.memory_my_resume_mapper import MemoryMyResumeMapper
from app.mapper.memory_personal_event_mapper import MemoryPersonalEventMapper
from app.mapper.memory_resume_mapper import MemoryResumeMapper
from app.model.my_resume_profile import MyResumeProfile
from app.model.resume import Resume
from app.service.personal_schedule_service import PersonalScheduleService
from app.service.resume_parser_service import ResumeParserService


def test_overdue_module_actions_and_calendar_cancelled_style():
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    now = datetime.now(ZoneInfo("Asia/Shanghai"))
    event = service.create_manual(title="逾期样本", event_type="personal",
                                  start_at=now - timedelta(hours=2), end_at=now - timedelta(hours=1))
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            page = client.get("/my-schedule").text
            assert "未完成日程" in page and f'data-defer-open="defer-{event.id}"' in page
            invalid = client.post(f"/my-schedule/events/{event.id}/overdue", data={
                "action": "defer", "end_at": (now - timedelta(hours=3)).isoformat()})
            assert invalid.status_code == 422
            response = client.post(f"/my-schedule/events/{event.id}/overdue", data={"action": "cancel"})
            assert response.status_code == 200
            assert "is-cancelled" in response.text and "已废除" in response.text
            assert 'class="personal-overdue-section"' not in response.text
            assert service.get_event(event.id).cancelled_at is not None
    finally:
        app.dependency_overrides.clear()


def test_personal_manual_create_edit_and_delete_pages():
    profile = MemoryMyResumeMapper()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    app.dependency_overrides[get_my_resume_mapper] = lambda: profile
    try:
        with TestClient(app) as client:
            assert client.get("/my-schedule/new").status_code == 200
            created = client.post("/my-schedule/events", data={
                "source": "manual", "title": "算法笔试", "event_type": "exam",
                "start_at": "2026-10-02T09:00", "end_at": "2026-10-02T10:00",
                "timezone": "Asia/Shanghai", "location_text": "教学楼 A",
            }, follow_redirects=False)
            assert created.status_code == 303
            event = service.list_events()[0]
            assert event.type == "exam"
            assert created.headers["location"] == "/my-schedule?view=day&date_value=2026-10-02"
            non_interview = client.get(f"/my-schedule/interviews/{event.id}", follow_redirects=False)
            assert non_interview.status_code == 303
            assert non_interview.headers["location"] == f"/my-schedule/events/{event.id}"
            page = client.get("/my-schedule?date_value=2026-10-02")
            assert "算法笔试" in page.text
            assert 'class="personal-event-list has-timeline-events"' in page.text
            assert "候选人姓名" not in page.text
            detail = client.get(f"/my-schedule/events/{event.id}")
            assert detail.status_code == 200
            assert "编辑日程" in detail.text and "教学楼 A" in detail.text
            assert 'action="/my-schedule/events/' not in detail.text
            edit_page = client.get(f"/my-schedule/events/{event.id}/edit")
            assert edit_page.status_code == 200
            assert 'name="title"' in edit_page.text
            edited = client.post(f"/my-schedule/events/{event.id}/edit", data={
                "title": "算法笔试第二轮", "event_type": "exam",
                "start_at": "2026-10-02T09:00", "end_at": "2026-10-02T10:00",
                "timezone": "Asia/Shanghai",
            }, follow_redirects=False)
            assert edited.status_code == 303
            assert edited.headers["location"] == "/my-schedule?view=day&date_value=2026-10-02"
            assert service.get_event(event.id).title == "算法笔试第二轮"
            deleted = client.post(f"/my-schedule/events/{event.id}/delete", follow_redirects=False)
            assert deleted.status_code == 303
            assert service.list_events() == []
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)
        app.dependency_overrides.pop(get_my_resume_mapper, None)


def test_new_personal_event_defaults_to_workday_hours():
    before = datetime.now(ZoneInfo("Asia/Shanghai")).replace(second=0, microsecond=0)
    with TestClient(app) as client:
        page = client.get("/my-schedule/new")
    after = datetime.now(ZoneInfo("Asia/Shanghai")).replace(second=0, microsecond=0)
    assert page.status_code == 200
    start = re.search(r'name="start_at" value="([^"]+)"', page.text)
    end = re.search(r'name="end_at" value="([^"]+)"', page.text)
    assert start and end
    start_at = datetime.fromisoformat(start.group(1))
    end_at = datetime.fromisoformat(end.group(1))
    assert start_at.date() in {before.date(), after.date()}
    assert (start_at.hour, start_at.minute) == (8, 0)
    assert (end_at.hour, end_at.minute) == (18, 0)


def test_non_interview_event_form_hides_invitation_parser_on_validation_error():
    with TestClient(app) as client:
        page = client.post("/my-schedule/events", data={
            "title": "课程", "event_type": "course", "start_at": "invalid",
            "end_at": "invalid", "timezone": "Asia/Shanghai",
        })
    assert page.status_code == 422
    assert 'data-invitation-form hidden' in page.text
    assert 'name="event_type" form="personal-event-form"' in page.text


def test_add_schedule_defaults_to_personal_and_keeps_shared_picker():
    with TestClient(app) as client:
        page = client.get("/my-schedule/new").text
    assert 'value="personal" selected' in page
    assert 'data-invitation-form hidden' in page
    assert page.count('data-time-dialog aria-label=') == 1


def test_personal_calendar_has_live_time_line_and_hides_it_on_other_dates():
    today = datetime.now(ZoneInfo("Asia/Shanghai")).date()
    with TestClient(app) as client:
        current = client.get("/my-schedule", params={"date_value": today.isoformat()})
        previous = client.get("/my-schedule", params={
            "date_value": (today - timedelta(days=1)).isoformat(),
        })
    assert 'data-now-grid' in current.text
    assert 'data-time-zone="Asia/Shanghai"' in current.text
    assert 'data-now-line' in current.text
    assert 'data-now-line hidden' in previous.text
    assert 'class="personal-event-list"' in previous.text


def test_dashboard_completion_moves_events_and_grays_calendar_views():
    zone = ZoneInfo("Asia/Shanghai")
    today = datetime.now(zone).date()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    today_event = service.create_manual(
        title="整理简历", event_type="personal",
        start_at=datetime.combine(today, datetime.min.time(), zone) + timedelta(hours=9),
        end_at=datetime.combine(today, datetime.min.time(), zone) + timedelta(hours=10),
    )
    tomorrow = today + timedelta(days=1)
    service.create_manual(
        title="准备面试", event_type="personal",
        start_at=datetime.combine(tomorrow, datetime.min.time(), zone) + timedelta(hours=9),
        end_at=datetime.combine(tomorrow, datetime.min.time(), zone) + timedelta(hours=10),
    )
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            current = client.get("/my-schedule")
            assert "整理简历" in current.text
            assert "显示更多" in current.text
            assert 'class="personal-completed-section"' in current.text
            expanded = client.get("/my-schedule?show_all=1")
            assert "准备面试" in expanded.text

            completed = client.post(f"/my-schedule/events/{today_event.id}/completion", data={
                "completed": "true", "view": "day", "date_value": today.isoformat(),
            }, follow_redirects=False)
            assert completed.status_code == 303
            assert service.get_event(today_event.id).completed_at is not None
            day = client.get("/my-schedule")
            assert 'class="personal-completed-section" open' in day.text
            assert "恢复" in day.text
            assert 'event-card type-personal is-completed' in day.text
            assert 'class="personal-side-row is-completed"' in day.text
            week = client.get("/my-schedule?view=week")
            month = client.get("/my-schedule?view=month")
            assert 'calendar-board-event type-personal is-completed' in week.text
            assert 'personal-period-event type-personal is-completed' in month.text

            restored = client.post(f"/my-schedule/events/{today_event.id}/completion", data={
                "completed": "false", "view": "day", "date_value": today.isoformat(),
            }, follow_redirects=False)
            assert restored.status_code == 303
            assert service.get_event(today_event.id).completed_at is None
            assert 'class="personal-completed-section"' in client.get("/my-schedule").text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_completion_cannot_change_another_users_event():
    mapper = MemoryPersonalEventMapper()
    owner = PersonalScheduleService(mapper, MemoryMyResumeMapper(), owner_id="owner")
    other = PersonalScheduleService(mapper, MemoryMyResumeMapper(), owner_id="other")
    event = owner.create_manual(
        title="私人任务", event_type="personal",
        start_at=datetime(2026, 10, 27, 9, tzinfo=ZoneInfo("Asia/Shanghai")),
        end_at=datetime(2026, 10, 27, 10, tzinfo=ZoneInfo("Asia/Shanghai")),
    )
    try:
        other.set_event_completed(event.id, True)
    except LookupError:
        pass
    else:
        raise AssertionError("其他用户不应能修改该日程")
    assert owner.get_event(event.id).completed_at is None


def test_personal_calendar_week_and_month_views_keep_period_navigation():
    profile = MemoryMyResumeMapper()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    service.create_manual(
        title="周内面试", event_type="interview",
        start_at=datetime(2026, 10, 9, 9, tzinfo=ZoneInfo("Asia/Shanghai")),
        end_at=datetime(2026, 10, 9, 10, tzinfo=ZoneInfo("Asia/Shanghai")),
    )
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            week = client.get("/my-schedule?view=week&date_value=2026-10-10")
            month = client.get("/my-schedule?view=month&date_value=2026-10-10")
        assert week.status_code == month.status_code == 200
        assert 'calendar-week-board' in week.text
        assert '09:00—10:00' in week.text
        assert '2026-10-05 — 10-11 · 1 项安排' in week.text
        assert 'view=week&amp;date_value=2026-10-03' in week.text
        assert 'class="btn secondary personal-today-link" href="/my-schedule?view=week&amp;date_value=' in week.text
        assert '周内面试' in week.text
        assert 'personal-period-month' in month.text
        assert '2026 年 10 月 · 1 项安排' in month.text
        assert 'view=month&amp;date_value=2026-11-01' in month.text
        assert 'class="btn secondary personal-today-link" href="/my-schedule?view=month&amp;date_value=' in month.text
        assert '查看 2026-11-01 的日程' in month.text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_month_calendar_keeps_multiday_tasks_continuous_across_weeks_and_months():
    zone = ZoneInfo("Asia/Shanghai")
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    event = service.create_manual(
        title="连续任务", event_type="personal", skip_weekends=False,
        start_at=datetime(2026, 9, 30, 9, tzinfo=zone),
        end_at=datetime(2026, 10, 6, 0, tzinfo=zone),
    )
    service.create_manual(
        title="重叠事项", event_type="personal", skip_weekends=False, confirm_overlap=True,
        start_at=datetime(2026, 10, 2, 10, tzinfo=zone),
        end_at=datetime(2026, 10, 4, 11, tzinfo=zone),
    )
    context = _dashboard_context(service, date(2026, 10, 1), "month")
    days = {day["date"]: day for day in context["calendar_days"]}
    assert context["period_title"] == "2026 年 10 月 · 2 项安排"
    for day in (date(2026, 9, 30) + timedelta(days=offset) for offset in range(6)):
        assert any(item["id"] == event.id for item in days[day]["entries"])
    assert not days[date(2026, 10, 6)]["entries"]
    friday, saturday = (days[date(2026, 10, day)]["entries"] for day in (2, 3))
    assert {item["id"]: item["month_lane"] for item in friday} == {
        item["id"]: item["month_lane"] for item in saturday
    }
    monday = days[date(2026, 10, 5)]["entries"][0]
    assert monday["continues_before"] and not monday["continues_after"]
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            page = client.get("/my-schedule?view=month&date_value=2026-10-01")
        assert page.status_code == 200
        assert 'personal-period-event type-personal continues-before continues-after' in page.text
        assert '09/30 09:00–10/06 00:00' in page.text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_month_calendar_shows_later_lanes_even_when_earlier_tasks_have_ended():
    zone = ZoneInfo("Asia/Shanghai")
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    for index in range(4):
        service.create_manual(
            title=f"周内任务 {index}", event_type="personal", skip_weekends=False, confirm_overlap=True,
            start_at=datetime(2026, 10, 26, 9, tzinfo=zone),
            end_at=datetime(2026, 10, 31, 0, tzinfo=zone),
        )
    continuing = service.create_manual(
        title="周末仍持续的任务", event_type="personal", skip_weekends=False, confirm_overlap=True,
        start_at=datetime(2026, 10, 27, 9, tzinfo=zone),
        end_at=datetime(2026, 11, 6, 18, tzinfo=zone),
    )
    context = _dashboard_context(service, date(2026, 10, 1), "month")
    days = {day["date"]: day for day in context["calendar_days"]}
    for day in (date(2026, 10, 31), date(2026, 11, 1)):
        assert len(days[day]["entries"]) == 1
        assert days[day]["entries"][0]["month_lane"] == 4
        assert days[day]["month_lane_count"] == 5
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            page = client.get("/my-schedule?view=month&date_value=2026-10-01")
        assert page.status_code == 200
        assert f'style="grid-row: 5;" href="/my-schedule/events/{continuing.id}"' in page.text
        assert 'grid-template-rows: repeat(5, 20px)' in page.text
        assert "另有 " not in page.text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_week_timeline_splits_overnight_events_at_day_and_week_boundaries():
    zone = ZoneInfo("Asia/Shanghai")
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    service.create_manual(
        title="跨周任务", event_type="personal",
        start_at=datetime(2026, 10, 11, 23, tzinfo=zone),
        end_at=datetime(2026, 10, 12, 1, tzinfo=zone),
    )
    service.create_manual(
        title="跨天任务", event_type="personal",
        start_at=datetime(2026, 10, 12, 23, tzinfo=zone),
        end_at=datetime(2026, 10, 13, 1, tzinfo=zone),
    )
    monday_context = _dashboard_context(service, date(2026, 10, 12), "week")
    monday_segment = monday_context["calendar_days"][0]["week_segments"][0]
    monday_late_segment = monday_context["calendar_days"][0]["week_segments"][1]
    tuesday_segment = monday_context["calendar_days"][1]["week_segments"][0]
    assert (monday_segment["start_minutes"], monday_segment["duration_minutes"]) == (0, 60)
    assert monday_segment["continues_before"] is True
    assert (monday_late_segment["start_minutes"], monday_late_segment["duration_minutes"]) == (1380, 60)
    assert monday_late_segment["continues_after"] is True
    assert (tuesday_segment["start_minutes"], tuesday_segment["duration_minutes"]) == (0, 60)
    assert tuesday_segment["continues_before"] is True
    day_start = _dashboard_context(service, date(2026, 10, 12), "day")
    day_next = _dashboard_context(service, date(2026, 10, 13), "day")
    assert day_start["end_hour"] == 24
    assert any(item["title"] == "跨天任务" and item["duration_minutes"] == 60
               for item in day_start["entries"])
    assert any(item["title"] == "跨天任务" and item["start_minutes"] == 0
               for item in day_next["entries"])
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            sunday = client.get("/my-schedule?view=week&date_value=2026-10-11")
            monday = client.get("/my-schedule?view=week&date_value=2026-10-12")
        assert sunday.status_code == monday.status_code == 200
        assert '2026-10-05 — 10-11 · 1 项安排' in sunday.text
        assert '延续到次日' in sunday.text
        assert '从前一天延续' in monday.text
        assert '2026-10-12 — 10-18 · 2 项安排' in monday.text
        assert '延续到次日' in monday.text
        assert '从前一天延续' in monday.text
        assert monday.text.count('class="calendar-task-arrow to-right"') == 1
        assert monday_late_segment['board_row'] == tuesday_segment['board_row']
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_personal_invitation_preview_and_interview_workspace():
    profile = MemoryMyResumeMapper()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    app.dependency_overrides[get_my_resume_mapper] = lambda: profile
    raw = "会议主题：后端面试\n面试时间：2026/10/02 13:00-14:00\n"
    try:
        with TestClient(app) as client:
            preview = client.post("/my-schedule/parse", data={"raw_text": raw})
            assert preview.status_code == 200
            assert "后端面试" in preview.text
            assert service.list_events() == []
            saved = client.post("/my-schedule/events", data={
                "source": "invitation", "raw_text": raw, "title": "后端面试",
                "event_type": "interview", "start_at": "2026-10-02T13:00",
                "end_at": "2026-10-02T14:00", "timezone": "Asia/Shanghai",
            }, follow_redirects=False)
            assert saved.status_code == 303
            assert saved.headers["location"] == "/my-schedule?view=day&date_value=2026-10-02"
            workspace = client.get("/my-schedule/interviews?date_value=2026-10-02")
            assert workspace.status_code == 200
            assert "后端面试" in workspace.text
            assert "我的简历" in workspace.text
            assert 'class="my-interviews-main"' in workspace.text
            assert 'class="my-interview-layout"' in workspace.text
            assert 'class="date-tabs-row"' in workspace.text
            assert 'data-date-scope' not in workspace.text
            assert 'class="plan-group-title today"' in workspace.text
            assert 'class="plan-group-title finished"' in workspace.text
            event = service.list_events()[0]
            assert f'href="/my-schedule/interviews/{event.id}"' in workspace.text
            questions = client.get(f"/my-schedule/interviews/{event.id}")
            assert questions.status_code == 200
            assert 'class="card my-resume-panel"' in questions.text
            assert 'class="card interview-questions-panel"' in questions.text
            assert "还没有问题记录" in questions.text
            assert "返回面试计划" in questions.text
            assert 'class="card schedule-group"' not in questions.text
            assert 'class="card my-plan-panel"' not in questions.text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)
        app.dependency_overrides.pop(get_my_resume_mapper, None)


def test_personal_interview_resume_keeps_full_page_view_but_loads_pdf_independently(tmp_path):
    profile = MemoryMyResumeMapper()
    resumes = MemoryResumeMapper()
    saved = resumes.save(Resume(file_name="sample.pdf", pdf_path="sample.pdf", page_count=1))
    profile.save(MyResumeProfile(resume_id=saved.id))
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    app.dependency_overrides[get_my_resume_mapper] = lambda: profile
    app.dependency_overrides[get_resume_parser_service] = lambda: ResumeParserService(
        resume_mapper=resumes, storage_dir=tmp_path,
    )
    try:
        with TestClient(app) as client:
            page = client.get("/my-schedule/interviews")
            assert page.status_code == 200
            assert 'class="card my-resume-panel"' in page.text
            assert 'class="own-resume-a4"' in page.text
            assert 'class="resume-replace-form"' in page.text
            assert 'data-resume-file hidden' in page.text
            assert 'data-resume-cache' in page.text and '同步简历' in page.text
            assert '/resumes/' + saved.id + '/pdf' not in page.text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)
        app.dependency_overrides.pop(get_my_resume_mapper, None)
        app.dependency_overrides.pop(get_resume_parser_service, None)


def test_personal_overlap_can_be_confirmed_from_form():
    profile = MemoryMyResumeMapper()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    service.create_manual(
        title="课程", event_type="course",
        start_at=datetime(2026, 10, 2, 9, tzinfo=ZoneInfo("Asia/Shanghai")),
        end_at=datetime(2026, 10, 2, 10, tzinfo=ZoneInfo("Asia/Shanghai")),
    )
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    form = {
        "source": "manual", "title": "笔试", "event_type": "exam",
        "start_at": "2026-10-02T09:30", "end_at": "2026-10-02T10:30",
        "timezone": "Asia/Shanghai",
    }
    try:
        with TestClient(app) as client:
            warning = client.post("/my-schedule/events", data=form)
            assert warning.status_code == 409
            assert "课程" in warning.text
            assert 'data-overlap-dialog' in warning.text
            assert len(service.list_events()) == 1
            accepted = client.post("/my-schedule/events", data={**form, "confirm_overlap": "true"}, follow_redirects=False)
            assert accepted.status_code == 303
            assert len(service.list_events()) == 2
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_completed_filter_only_changes_calendar_views():
    zone = ZoneInfo("Asia/Shanghai")
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    event = service.create_manual(
        title="已完成任务", event_type="personal",
        start_at=datetime(2026, 10, 27, 9, tzinfo=zone),
        end_at=datetime(2026, 10, 27, 10, tzinfo=zone),
    )
    service.set_event_completed(event.id, True)
    for view in ("day", "week", "month"):
        context = _dashboard_context(service, date(2026, 10, 27), view, show_completed=False)
        assert not context["entries"]
        assert all(not day["entries"] and not day["week_segments"] for day in context["calendar_days"])
        assert _dashboard_context(service, date(2026, 10, 27), view)["period_title"].endswith("1 项安排")


def test_ordinary_meeting_invitation_has_no_resume():
    from unittest.mock import Mock
    profile = Mock()
    service = PersonalScheduleService(MemoryPersonalEventMapper(), profile)
    raw = ("会议主题: aistack产品介绍\n会议时间: 2026/11/03 10:00-11:00\n"
           "点击链接直接加入会议:\nhttps://xin.asiainfo.com/t/QGVBnUvx\n会议 ID: 564 391 942")
    event = service.from_invitation(raw)
    assert event.title == "aistack产品介绍"
    assert event.type == "meeting"
    assert event.start_at.isoformat() == "2026-11-03T10:00:00+08:00"
    assert event.end_at.isoformat() == "2026-11-03T11:00:00+08:00"
    assert str(event.meeting_url) == "https://xin.asiainfo.com/t/QGVBnUvx"
    assert event.room_number == "564391942"
    assert event.resume_id is None and event.position is None
    profile.get.assert_not_called()
    service.save_event(event)
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            preview = client.post("/my-schedule/parse", data={"raw_text": raw})
            assert preview.status_code == 200
            assert re.search(r'data-invitation-form\s*>', preview.text)
            detail = client.get(f"/my-schedule/events/{event.id}")
            assert detail.status_code == 200
            assert 'data-resume-cache' not in detail.text
            assert 'href="https://xin.asiainfo.com/t/QGVBnUvx" target="_blank" rel="noopener noreferrer">点击入会</a>' in detail.text
            assert '564391942' in detail.text
            redirected = client.get(f"/my-schedule/interviews/{event.id}", follow_redirects=False)
            assert redirected.status_code == 303
            assert redirected.headers["location"] == f"/my-schedule/events/{event.id}"
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_meeting_link_survives_invitation_save_and_edit():
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    raw = "会议主题: 示例会议\n会议时间: 2026/11/03 10:00-11:00\nhttps://example.com/join\n会议 ID: 123 456 789"
    try:
        with TestClient(app) as client:
            fields = {"source": "invitation", "raw_text": raw, "title": "示例会议", "event_type": "meeting",
                      "start_at": "2026-11-03T10:00", "end_at": "2026-11-03T11:00",
                      "meeting_url": "https://example.com/join"}
            assert client.post("/my-schedule/events", data=fields, follow_redirects=False).status_code == 303
            event = service.list_events()[0]
            assert str(event.meeting_url) == "https://example.com/join"
            detail = client.get(f"/my-schedule/events/{event.id}")
            assert 'href="https://example.com/join"' in detail.text and "点击入会" in detail.text
            assert "123456789" in detail.text
            assert 'data-resume-cache' not in detail.text
            assert client.post(f"/my-schedule/events/{event.id}/edit", data={**fields, "title": "更新会议"}, follow_redirects=False).status_code == 303
            assert str(service.get_event(event.id).meeting_url) == "https://example.com/join"
            assert "点击入会" in client.get(f"/my-schedule/events/{event.id}").text
            plain = service.create_manual(title="无链接会议", event_type="meeting",
                       start_at=datetime(2026,11,4,10), end_at=datetime(2026,11,4,11))
            assert "点击入会" not in client.get(f"/my-schedule/events/{plain.id}").text
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_skip_weekends_form_and_calendar_roundtrip():
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            fields = {"event_type": "personal", "title": "工作日任务", "start_at": "2026-11-06T08:00",
                      "end_at": "2026-11-10T18:00", "skip_weekends": "true"}
            assert client.post("/my-schedule/events", data=fields, follow_redirects=False).status_code == 303
            task = service.list_events()[0]
            assert task.skip_weekends
            for view in ["day", "week", "month"]:
                ctx = _dashboard_context(service, date(2026,11,7), view)
                if view == "day":
                    assert not ctx["entries"]
                else:
                    for day in ctx["calendar_days"]:
                        if day["date"].weekday() >= 5:
                            assert not day["entries"] and not day["week_segments"]
            assert _dashboard_context(service, date(2026,11,9))["entries"]
            # A weekend task does not conflict with a task that skips weekends.
            weekend = service.create_manual(title="周末任务", event_type="personal",
                      start_at=datetime(2026,11,7,10), end_at=datetime(2026,11,7,11), skip_weekends=True)
            assert weekend.skip_weekends is False
            fields.pop("skip_weekends")
            fields["confirm_overlap"] = "true"
            assert client.post(f"/my-schedule/events/{task.id}/edit", data=fields, follow_redirects=False).status_code == 303
            assert not service.get_event(task.id).skip_weekends
            assert len(_dashboard_context(service, date(2026,11,7))["entries"]) == 2
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)
