from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
import pytest
from fastapi.testclient import TestClient
from app.main import app
from app.core.dependencies import get_personal_schedule_service
from app.mapper.memory_my_resume_mapper import MemoryMyResumeMapper
from app.mapper.memory_personal_event_mapper import MemoryPersonalEventMapper
from app.service.personal_schedule_service import PersonalScheduleService
from app.controller.personal_schedule_controller import _dashboard_context


def test_daily_completion_statistics_close_and_owner_isolation():
    zone = ZoneInfo("Asia/Shanghai")
    today = datetime.now(zone).date()
    start = today - timedelta(days=3)
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    task = service.create_manual(title="重复验收", event_type="personal", repeat_weekdays=list(range(7)),
        start_at=datetime.combine(start, datetime.min.time(), zone),
        end_at=datetime.combine(start, datetime.min.time(), zone) + timedelta(minutes=1))
    for offset in [2, 1, 0]:
        day = today - timedelta(days=offset)
        service.set_event_completed(f"{task.id}~{day}", True)
    occurrence = service.get_event(f"{task.id}~{today}")
    assert occurrence.repeat_streak == 3 and occurrence.repeat_missed == 1
    service.set_event_completed(occurrence.id, False)
    assert not service.get_event(occurrence.id).completed_at
    assert service.get_event(f"{task.id}~{today - timedelta(days=1)}").completed_at
    other = PersonalScheduleService(service.mapper, service.profile_mapper, owner_id="another-owner")
    assert other.get_event(occurrence.id) is None
    with pytest.raises(LookupError): other.close_repeat(occurrence.id)
    app.dependency_overrides[get_personal_schedule_service] = lambda: service
    try:
        with TestClient(app) as client:
            detail = client.get(f"/my-schedule/events/{occurrence.id}")
            assert "关闭该重复任务" in detail.text and "累积未完成" in detail.text
            edit = client.get(f"/my-schedule/events/{occurrence.id}/edit")
            assert start.isoformat() in edit.text
            response = client.post(f"/my-schedule/events/{occurrence.id}/close-repeat", follow_redirects=False)
            assert response.status_code == 303
        assert not service.get_event(f"{task.id}~{today + timedelta(days=1)}")
        assert service.get_event(f"{task.id}~{today - timedelta(days=1)}")
    finally:
        app.dependency_overrides.pop(get_personal_schedule_service, None)


def test_repeat_weekday_selection_and_cross_day_validation():
    service = PersonalScheduleService(MemoryPersonalEventMapper(), MemoryMyResumeMapper())
    with pytest.raises(ValueError, match="同一天"):
        service.create_manual(title="无效", event_type="personal", repeat_weekdays=[0],
            start_at=datetime(2026,11,2,23), end_at=datetime(2026,11,3,1))
    task = service.create_manual(title="仅周一", event_type="personal", repeat_weekdays=[0],
        start_at=datetime(2026,11,2,8), end_at=datetime(2026,11,2,9))
    assert _dashboard_context(service, datetime(2026,11,9).date())["entries"]
    assert not _dashboard_context(service, datetime(2026,11,10).date())["entries"]
