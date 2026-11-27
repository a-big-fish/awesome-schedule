from datetime import date, datetime
from zoneinfo import ZoneInfo
from fastapi.testclient import TestClient
from app.controller.personal_schedule_controller import _event_view, _week_day_segments, _month_day_entries
from app.model.personal_event import PersonalEvent
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _public_fixture, _login

ZONE=ZoneInfo('Asia/Shanghai')
def test_workday_clips_daily_segments_without_changing_original_schedule():
    event=PersonalEvent(owner_id='local', title='跨天工作',type='personal',
        start_at=datetime(2026,11,12,16,tzinfo=ZONE),end_at=datetime(2026,11,16,10,tzinfo=ZONE),
        skip_weekends=True,workday_only=True)
    item={**_event_view(event,datetime.now(ZONE)), 'display_workday_start':'09:30','display_workday_end':'17:30'}
    friday=_week_day_segments([item],date(2026,11,13))[0]
    assert friday['start_minutes']==570 and friday['duration_minutes']==480
    assert _week_day_segments([item],date(2026,11,14))==[]
    monday=_week_day_segments([item],date(2026,11,16))[0]
    assert monday['start_minutes']==570 and monday['duration_minutes']==30
    assert event.start_at.hour==16 and event.end_at.hour==10
    assert item['start_time']==event.start_at
    month=_month_day_entries([item],[date(2026,11,day) for day in range(9,16)])
    assert month[date(2026,11,13)] and not month[date(2026,11,14)]

def test_work_hours_and_weekends_are_independent_and_nonintersecting_days_hidden():
    event=PersonalEvent(owner_id='local',title='晚间事项',type='personal',start_at=datetime(2026,11,14,19,tzinfo=ZONE),end_at=datetime(2026,11,15,10,tzinfo=ZONE),workday_only=True)
    item=_event_view(event,datetime.now(ZONE))
    assert _week_day_segments([item],date(2026,11,14))==[]
    assert _week_day_segments([item],date(2026,11,15))[0]['duration_minutes']==120
    days=[date(2026,11,day) for day in range(9,16)]
    assert not _month_day_entries([item],days)[date(2026,11,14)]
    item['workday_only']=False
    assert _week_day_segments([item],date(2026,11,14))[0]['start_minutes']==1140

def test_create_edit_and_calendar_use_account_work_hours(tmp_path):
    app,auth,_,events,*_= _public_fixture(tmp_path)
    user=AuthUser(email='hours@example.com',password_hash=hash_password('test-password'),email_verified=True,workday_start='09:30',workday_end='17:30')
    auth.create_user(user)
    with TestClient(app,base_url='https://testserver') as client:
        csrf=_login(client,user.email,'test-password')
        data={'csrf_token':csrf,'title':'工作时间裁剪','event_type':'personal','start_at':'2026-11-12T16:00','end_at':'2026-11-16T10:00','workday_only':'true','skip_weekends':'true'}
        assert client.post('/my-schedule/events',data=data,follow_redirects=False).status_code==303
        event=events.list_all(user.id)[0];assert event.workday_only
        page=client.get('/my-schedule?view=day&date_value=2026-11-16')
        assert page.status_code==200 and '--start-min: 570' in page.text
        edit=client.get(f'/my-schedule/events/{event.id}/edit')
        assert 'name="workday_only"' in edit.text
        data.pop('workday_only')
        assert client.post(f'/my-schedule/events/{event.id}/edit',data=data,follow_redirects=False).status_code==303
        assert events.find_by_id(event.id,user.id).workday_only is False
