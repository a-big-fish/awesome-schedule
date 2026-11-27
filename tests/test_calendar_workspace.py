from datetime import date, datetime
from zoneinfo import ZoneInfo

from fastapi.testclient import TestClient

from app.controller.personal_schedule_controller import _dashboard_context
from app.model.auth import AuthUser
from app.model.personal_event import PersonalEvent
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _login, _public_fixture


ZONE = ZoneInfo('Asia/Shanghai')


def test_week_board_keeps_task_rows_and_does_not_connect_skipped_weekends(tmp_path):
    app, auth, _, mapper, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='board@example.com', password_hash=hash_password('password'), email_verified=True)
    auth.create_user(user)
    event = mapper.save(PersonalEvent(owner_id=user.id, type='personal', title='跨周工作',
        start_at=datetime(2026, 11, 9, 8, tzinfo=ZONE),
        end_at=datetime(2026, 11, 17, 18, tzinfo=ZONE), skip_weekends=True, workday_only=True))
    with TestClient(app, base_url='https://testserver') as client:
        _login(client, user.email, 'password')
        page = client.get('/my-schedule?view=week&date_value=2026-11-12')
    from app.service.personal_schedule_service import PersonalScheduleService
    from app.mapper.memory_my_resume_mapper import MemoryMyResumeMapper
    context = _dashboard_context(PersonalScheduleService(mapper, MemoryMyResumeMapper(), owner_id=user.id),
                                 date(2026, 11, 12), 'week')
    copies = [item for day in context['calendar_days'] for item in day['week_segments'] if item['id'] == event.id]
    assert len(copies) == 5
    assert len({item['board_row'] for item in copies}) == 1
    assert copies[-1]['board_after'] is False
    assert copies[0]['board_start'] == '08:00' and copies[0]['board_end'] == '18:00'
    assert 'calendar-week-board' in page.text and 'personal-week-timeline' not in page.text


def test_dialog_save_uses_existing_owner_csrf_conflict_and_field_validation(tmp_path):
    app, auth, _, mapper, *_ = _public_fixture(tmp_path)
    owner = AuthUser(email='dialog@example.com', password_hash=hash_password('password'), email_verified=True)
    stranger = AuthUser(email='other-dialog@example.com', password_hash=hash_password('password'), email_verified=True)
    auth.create_user(owner); auth.create_user(stranger)
    event = mapper.save(PersonalEvent(owner_id=owner.id, type='personal', title='原任务',
        start_at=datetime(2026, 11, 13, 8, tzinfo=ZONE), end_at=datetime(2026, 11, 13, 9, tzinfo=ZONE)))
    mapper.save(PersonalEvent(owner_id=owner.id, type='personal', title='重叠任务',
        start_at=datetime(2026, 11, 13, 8, tzinfo=ZONE), end_at=datetime(2026, 11, 13, 10, tzinfo=ZONE)))
    path = f'/my-schedule/events/{event.id}/edit'
    headers = {'X-Event-Dialog': '1'}
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, owner.email, 'password')
        data = {'title': '修改任务', 'event_type': 'personal', 'start_at': '2026-11-13T08:00',
                'end_at': '2026-11-13T09:00', 'timezone': 'Asia/Shanghai',
                'note': '第一行\n第二行', 'reference_title': '文档', 'reference_url': 'https://example.com/docs'}
        assert client.post(path, headers=headers, data=data).status_code == 403
        data['csrf_token'] = csrf
        assert client.post(path, headers=headers, data=data).status_code == 409
        assert mapper.find_by_id(event.id, owner.id).title == '原任务'
        data['confirm_overlap'] = 'true'
        saved = client.post(path, headers=headers, data=data)
        assert saved.status_code == 200 and saved.json() == {'saved': True}
        stored = mapper.find_by_id(event.id, owner.id)
        assert stored.note == data['note'] and stored.reference_links[0].title == '文档'
        data['end_at'] = '2026-11-13T07:00'
        assert client.post(path, headers=headers, data=data).status_code == 422
    with TestClient(app, base_url='https://testserver') as client:
        data['csrf_token'] = _login(client, stranger.email, 'password')
        assert client.get(path).status_code == 404
        assert client.post(path, headers=headers, data=data).status_code == 404
