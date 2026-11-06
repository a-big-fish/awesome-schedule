from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from fastapi.testclient import TestClient
from app.core.dependencies import get_interview_experience_mapper
from app.model.interview_experience import InterviewExperience, InterviewQuestion
from app.model.personal_event import PersonalEvent
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _public_fixture, _login, _csrf


def test_guest_whitelist_blocks_bodies_in_html_batches_and_all_detail_paths(tmp_path):
    app, users, _, events, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="guest-owner@example.com", display_name="PRIVATE_AUTHOR_MARKER",
                    password_hash=hash_password("test-password"), email_verified=True)
    users.create_user(user)
    mapper = app.dependency_overrides[get_interview_experience_mapper]()
    item = mapper.save_experience(InterviewExperience(owner_id=user.id, title="允许展示的标题",
        company="PRIVATE_COMPANY_MARKER", position="PRIVATE_POSITION_MARKER",
        feeling="PRIVATE_FEELING_MARKER", raw_text_draft="PRIVATE_RAW_MARKER"))
    mapper.save_question(InterviewQuestion(experience_id=item.id, category="project", asked_order=1,
        question_text="PRIVATE_QUESTION_MARKER", on_site_answer="PRIVATE_ANSWER_MARKER"), user.id)
    now = datetime.now(ZoneInfo('Asia/Shanghai'))
    events.save(PersonalEvent(owner_id=user.id, type="personal", title="PRIVATE_EVENT_MARKER",
                              start_at=now, end_at=now+timedelta(hours=1)))
    with TestClient(app, base_url='https://testserver') as client:
        for suffix in ['', '?batch=true', '?batch=true&page=2']:
            response = client.get('/my-schedule/experiences/plaza'+suffix)
            assert response.status_code == 200
            assert 'PRIVATE_' not in response.text
            assert response.headers['cache-control'] == 'no-store'
        listing = client.get('/my-schedule/experiences/plaza').text
        assert '允许展示的标题' in listing and '用户 ***' in listing and '更新于' in listing
        calendar = client.get('/my-schedule').text
        assert '示例日程' in calendar and 'PRIVATE_' not in calendar
        for path in [f'/my-schedule/experiences/plaza/{item.id}', f'/my-schedule/experiences/{item.id}',
                     '/my-schedule/interviews', '/my-schedule/profile', '/my-schedule/new', '/my-schedule/ideas']:
            denied = client.get(path, follow_redirects=False)
            assert denied.status_code == 303 and denied.headers['location'].startswith('/auth/login')
            assert 'PRIVATE_' not in denied.text
        assert client.post('/my-schedule/events', data={}).status_code == 401
        _login(client, user.email, 'test-password')
        detail = client.get(f'/my-schedule/experiences/plaza/{item.id}')
        assert 'PRIVATE_QUESTION_MARKER' in detail.text and 'PRIVATE_ANSWER_MARKER' in detail.text
        assert 'PRIVATE_AUTHOR_MARKER' in detail.text


def test_embedded_password_login_keeps_next_and_sets_real_session(tmp_path):
    app, users, *_ = _public_fixture(tmp_path)
    user = AuthUser(email='modal@example.com', password_hash=hash_password('test-password'), email_verified=True)
    users.create_user(user)
    with TestClient(app, base_url='https://testserver') as client:
        page = client.get('/auth/login?embed=1&next=/my-schedule/profile')
        assert 'auth-embedded' in page.text and '手机号登录' in page.text and '扫码登录' in page.text
        result = client.post('/auth/login?embed=1', data={'csrf_token': _csrf(page),
            'identifier': user.email, 'password': 'test-password', 'next': '/my-schedule/profile'}, follow_redirects=False)
        assert result.status_code == 303 and result.headers['location'] == '/my-schedule/profile'
        assert 'schedule_session=' in result.headers['set-cookie']
        assert client.get('/my-schedule/profile').status_code == 200
