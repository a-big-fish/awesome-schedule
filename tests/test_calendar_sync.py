from uuid import uuid4, uuid5, NAMESPACE_URL
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from copy import deepcopy

import pytest

from fastapi.testclient import TestClient

from app.model.auth import AuthUser
from app.model.personal_event import PersonalEvent
from app.mapper.postgres_personal_event_mapper import PostgresPersonalEventMapper
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _public_fixture, _login


def test_priority_sync_defaults_to_normal_and_legacy_edits_preserve_existing_priority(tmp_path):
    app, users, _, _, *_ = _public_fixture(tmp_path)
    user = AuthUser(email="priority@example.com", password_hash=hash_password("password"), email_verified=True)
    users.create_user(user)
    operation = str(uuid4())
    event_id = str(uuid5(NAMESPACE_URL, f"calendar:{user.id}:{operation}"))
    event = {"title": "日程", "type": "personal",
             "start_at": "2026-11-13T08:00:00+08:00", "end_at": "2026-11-13T09:00:00+08:00"}
    with TestClient(app, base_url="https://testserver") as client:
        headers = {"X-CSRF-Token": _login(client, user.email, "password")}

        def post(action, values, operation_id):
            return client.post(f"/my-schedule/calendar-sync/{operation_id}",
                               json={"action": action, "event_id": event_id, "event": values}, headers=headers)

        assert post("create", event, operation).json()["event"]["priority"] == 10
        assert post("edit", {**event, "priority": "high"}, uuid4()).json()["event"]["priority"] == 18
        assert post("edit", {**event, "title": "旧客户端"}, uuid4()).json()["event"]["priority"] == 18
        for invalid in (0, 21, 101, "invalid", 1.5, 10.0, True):
            assert post("edit", {**event, "priority": invalid}, uuid4()).status_code == 422
        legacy = {"action": "priority", "event_id": event_id, "priority": 37, "priority_scale": 100}
        assert client.post(f"/my-schedule/calendar-sync/{uuid4()}", json=legacy,
                           headers=headers).json()["event"]["priority"] == 8
        command = {"action": "priority", "event_id": event_id, "priority": 8,
                   "priority_scale": 20, "priority_order": "m2r"}
        path = f"/my-schedule/calendar-sync/{uuid4()}"
        saved = client.post(path, json=command, headers=headers)
        assert saved.json()["event"]["priority"] == 8
        assert saved.json()["event"]["priority_order"] == "m2r"
        assert client.post(path, json=command, headers=headers).json() == saved.json()
        assert saved.json()["event"]["title"] == "旧客户端"
        assert client.get("/my-schedule/calendar-data").json()["events"][0]["priority_order"] == "m2r"
        edit = client.get(f"/my-schedule/events/{event_id}/edit")
        assert 'type="range" name="priority" min="1" max="20" step="1" value="8"' in edit.text
        direct = client.post(f"/my-schedule/events/{event_id}/priority", data={
            "csrf_token": headers["X-CSRF-Token"], "priority": "16",
        }, follow_redirects=False)
        assert direct.status_code == 303
        after = client.get("/my-schedule/calendar-data").json()["events"][0]
        assert (after["priority"], after["priority_order"], after["priority_scale"]) == (16, "m2r", 20)


def test_stored_priority_migration_preserves_new_scores():
    event = {"type": "personal", "title": "任务", "start_at": "2026-11-13T08:00:00+08:00",
             "end_at": "2026-11-13T09:00:00+08:00"}
    assert PersonalEvent.from_storage({**event, "priority": 95}).priority == 19
    assert PersonalEvent.from_storage({**event, "priority": 18}).priority == 4
    assert PersonalEvent.from_storage({**event, "priority": 18, "priority_scale": 20}).priority == 18


def test_sync_retries_replay_committed_receipt_and_never_resurrect_deleted_event(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='outbox@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(user)
    operation = str(uuid4())
    event_id = str(uuid5(NAMESPACE_URL, f'calendar:{user.id}:{operation}'))
    command = {'action': 'create', 'event_id': event_id, 'event': {
        'title': '队列任务', 'type': 'personal', 'start_at': '2026-11-13T08:00:00+08:00',
        'end_at': '2026-11-13T09:00:00+08:00'}}
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, user.email, 'password')
        headers = {'X-CSRF-Token': csrf}
        def post(op, body):
            return client.post(f'/my-schedule/calendar-sync/{op}', json=body, headers=headers)
        first = post(operation, command)
        assert first.status_code == 200, first.text
        assert post(operation, command).json() == first.json()
        assert len(events.list_all(user.id)) == 1
        changed = {**command, 'event': {**command['event'], 'title': '不允许复用相同操作号'}}
        assert post(operation, changed).status_code == 422
        assert events.find_by_id(event_id, user.id).title == '队列任务'
        completion = str(uuid4())
        done = {'action': 'completion', 'event_id': event_id, 'completed': True}
        completed = post(completion, done)
        assert completed.status_code == 200
        assert post(completion, done).json() == completed.json()
        deletion = str(uuid4())
        deleted = post(deletion, {'action': 'delete', 'event_id': event_id})
        assert deleted.status_code == 200 and deleted.json()['deleted_id'] == event_id
        assert post(operation, command).json() == first.json()  # Late retry returns receipt only.
        assert post(deletion, {'action': 'delete', 'event_id': event_id}).json() == deleted.json()
        assert not events.list_all(user.id)
        assert client.post(f'/my-schedule/calendar-sync/{uuid4()}', json=command).status_code == 403


def test_sync_rejection_is_atomic_and_a_corrected_draft_can_retry(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='rollback@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(user)
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, user.email, 'password')
        def create(title, confirm=False, op=None):
            op = op or str(uuid4())
            event_id = str(uuid5(NAMESPACE_URL, f'calendar:{user.id}:{op}'))
            body = {'action': 'create', 'event_id': event_id, 'confirm_overlap': confirm,
                    'event': {'title': title, 'type': 'personal', 'start_at': '2026-11-13T08:00:00+08:00',
                              'end_at': '2026-11-13T09:00:00+08:00'}}
            return client.post(f'/my-schedule/calendar-sync/{op}', json=body, headers={'X-CSRF-Token': csrf}), op
        assert create('已有任务')[0].status_code == 200
        response, op = create('重叠任务')
        assert response.status_code == 409 and response.json()['conflicts'] == ['已有任务']
        assert len(events.list_all(user.id)) == 1
        assert create('重叠任务', True, op)[0].status_code == 200
        assert len(events.list_all(user.id)) == 2
        # The mapper transaction also restores changes if a callback fails after writing.
        original = events.list_all(user.id)[0]
        def fail_after_save(mapper):
            mapper.delete(original.id, user.id)
            raise ValueError('rejected')
        try:
            events.execute_sync_once(user.id, str(uuid4()), 'hash', fail_after_save)
        except ValueError:
            pass
        assert events.find_by_id(original.id, user.id)


def test_sync_cannot_create_over_or_modify_another_accounts_event(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    first = AuthUser(email='first@example.com', password_hash=hash_password('password'), email_verified=True)
    second = AuthUser(email='second@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(first); users.create_user(second)
    op = str(uuid4())
    event_id = str(uuid5(NAMESPACE_URL, f'calendar:{first.id}:{op}'))
    body = {'action': 'create', 'event_id': event_id, 'event': {'title': '私有任务', 'type': 'personal',
            'start_at': '2026-11-13T08:00:00+08:00', 'end_at': '2026-11-13T09:00:00+08:00'}}
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, first.email, 'password')
        assert client.post(f'/my-schedule/calendar-sync/{op}', json=body, headers={'X-CSRF-Token': csrf}).status_code == 200
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, second.email, 'password')
        headers = {'X-CSRF-Token': csrf}
        assert client.post(f'/my-schedule/calendar-sync/{op}', json=body, headers=headers).status_code == 422
        body['action'] = 'edit'
        assert client.post(f'/my-schedule/calendar-sync/{uuid4()}', json=body, headers=headers).status_code == 404
        assert events.find_by_id(event_id, first.id).title == '私有任务'


def test_sync_repeat_completion_close_and_overdue_operations(tmp_path):
    app, users, _, events, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='actions@example.com', password_hash=hash_password('password'), email_verified=True)
    users.create_user(user)
    now = datetime.now(ZoneInfo('Asia/Shanghai'))
    with TestClient(app, base_url='https://testserver') as client:
        csrf = _login(client, user.email, 'password')
        def post(body, op=None):
            return client.post(f'/my-schedule/calendar-sync/{op or uuid4()}', json=body, headers={'X-CSRF-Token': csrf})
        def create(title, repeat=False):
            op = str(uuid4())
            event_id = str(uuid5(NAMESPACE_URL, f'calendar:{user.id}:{op}'))
            start = (now - timedelta(days=2)).replace(hour=8, minute=0)
            response = post({'action':'create', 'event_id':event_id, 'confirm_overlap':True,
                'event':{'title':title, 'type':'personal', 'start_at':start.isoformat(),
                         'end_at':(start+timedelta(hours=1)).isoformat(),
                         'repeat_weekdays':list(range(7)) if repeat else []}}, op)
            assert response.status_code == 200, response.text
            return event_id
        repeat = create('每日任务', True)
        day = now.date().isoformat()
        done = post({'action':'completion', 'event_id':repeat, 'occurrence_day':day, 'completed':True})
        assert day in done.json()['event']['repeat_completions']
        restored = post({'action':'completion', 'event_id':repeat, 'occurrence_day':day, 'completed':False})
        assert day not in restored.json()['event']['repeat_completions']
        assert post({'action':'close_repeat', 'event_id':repeat}).json()['event']['repeat_closed_on']
        expired = create('延期任务')
        op = str(uuid4())
        command = {'action':'defer', 'event_id':expired, 'end_at':(now+timedelta(days=1)).isoformat()}
        deferred = post(command, op)
        assert deferred.status_code == 200 and post(command, op).json() == deferred.json()
        cancelled = create('废除任务')
        assert post({'action':'cancel', 'event_id':cancelled}).json()['event']['cancelled_at']
        assert post({'action':'completion', 'event_id':cancelled, 'completed':True}).status_code == 422


def test_postgres_receipt_and_event_share_one_commit_and_rollback_together():
    """Transaction-aware connection double: an inner commit would make this test fail."""
    class Database:
        events = None
        receipts = None
        fail_receipt = False
        commits = 0

        def __init__(self):
            self.events, self.receipts = {}, {}

        def connection(self):
            db = self
            class Connection:
                def __enter__(self):
                    self.before = deepcopy((db.events, db.receipts))
                    return self

                def __exit__(self, kind, *_):
                    if kind:
                        db.events, db.receipts = self.before

                def commit(self):
                    db.commits += 1
                    self.before = deepcopy((db.events, db.receipts))

                def execute(self, sql, params):
                    row = None
                    if sql.startswith('SELECT request_hash'):
                        row = db.receipts.get(params)
                    elif sql.startswith('INSERT INTO personal_schedule_events'):
                        db.events[params[0]] = params[2].obj
                    elif sql.startswith('INSERT INTO personal_calendar_sync_receipts'):
                        if db.fail_receipt:
                            raise RuntimeError('lost connection before receipt insert')
                        db.receipts[params[:2]] = {'request_hash':params[2], 'payload':params[3].obj}
                    class Cursor:
                        def fetchone(self):
                            return row
                    return Cursor()
            return Connection()

    db = Database()
    def mapper():
        instance = object.__new__(PostgresPersonalEventMapper)
        instance.settings = None
        instance._connection_factory = db.connection
        return instance
    event = PersonalEvent(owner_id='owner', type='personal', title='原子写入',
                          start_at=datetime(2026,11,13,8,tzinfo=ZoneInfo('Asia/Shanghai')),
                          end_at=datetime(2026,11,13,9,tzinfo=ZoneInfo('Asia/Shanghai')))
    def apply(bound):
        bound.save(event)
        return {'event_id': event.id}
    db.fail_receipt = True
    with pytest.raises(RuntimeError):
        mapper().execute_sync_once('owner','operation','hash',apply)
    assert not db.events and not db.receipts and db.commits == 0
    db.fail_receipt = False
    assert mapper().execute_sync_once('owner','operation','hash',apply) == {'event_id':event.id}
    assert db.commits == 1 and len(db.events) == len(db.receipts) == 1
    def must_not_reapply(_):
        raise AssertionError('receipt lost across mapper instances')
    assert mapper().execute_sync_once('owner','operation','hash',must_not_reapply) == {'event_id':event.id}
