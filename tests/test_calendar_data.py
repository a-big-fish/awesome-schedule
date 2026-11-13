from datetime import datetime
from zoneinfo import ZoneInfo

from fastapi.testclient import TestClient

from app.model.auth import AuthUser
from app.model.personal_event import PersonalEvent
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _public_fixture, _login


def test_calendar_snapshot_is_owner_scoped_one_read_and_excludes_raw_files(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='snapshot@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(user)
    for owner in [user.id, 'another-user']:
        events.save(PersonalEvent(owner_id=owner, type='interview', title=owner,
            start_at=datetime(2026, 11, 13, 8, tzinfo=ZoneInfo('Asia/Shanghai')),
            end_at=datetime(2026, 11, 13, 9, tzinfo=ZoneInfo('Asia/Shanghai')),
            raw_invitation='private raw text', resume_id='private-resume'))
    with TestClient(app, base_url='https://testserver') as client:
        assert client.get('/my-schedule/calendar-data', follow_redirects=False).status_code in (303, 401)
        _login(client, user.email, 'password')
        calls = []
        original = events.list_all
        def counted(owner_id):
            calls.append(owner_id)
            return original(owner_id)
        events.list_all = counted
        response = client.get('/my-schedule/calendar-data')
        body = response.json()
        assert calls == [user.id]
        assert response.headers['cache-control'] == 'no-store'
        assert body['user_id'] == user.id and len(body['events']) == 1
        assert not {'resume_id', 'raw_invitation', 'attachment_refs', 'owner_id'} & body['events'][0].keys()
        assert 'personal-calendar' in body['shell']


def test_calendar_writes_return_authoritative_parent_and_delete_patch(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='patch@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(user)
    headers = {'X-Calendar-Cache': '1'}
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, user.email, 'password')
        data = {'csrf_token': csrf, 'title': '重复任务', 'event_type': 'personal',
                'start_at': '2026-11-13T08:00', 'end_at': '2026-11-13T09:00',
                'repeat_enabled': 'true', 'repeat_weekdays': '4'}
        created = client.post('/my-schedule/events', headers=headers, data=data)
        assert created.status_code == 200
        event_id = created.json()['event']['id']
        done = client.post(f'/my-schedule/events/{event_id}~2026-11-13/completion', headers=headers,
                           data={'csrf_token': csrf, 'completed': 'true'})
        assert done.json()['event']['id'] == event_id
        assert '2026-11-13' in done.json()['event']['repeat_completions']
        closed = client.post(f'/my-schedule/events/{event_id}/close-repeat', headers=headers, data={'csrf_token': csrf})
        assert closed.json()['event']['repeat_closed_on']
        deleted = client.post(f'/my-schedule/events/{event_id}/delete', headers=headers, data={'csrf_token': csrf})
        assert deleted.json()['deleted_id'] == event_id and deleted.json()['event'] is None
        assert events.list_all(user.id) == []
