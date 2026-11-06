from fastapi.testclient import TestClient
from app.core.dependencies import get_personal_idea_mapper
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _public_fixture, _login


def test_idea_independent_lifecycle_prefill_validation_and_duplicate_save(tmp_path):
    app, users, _, events, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="idea@example.com", password_hash=hash_password("test-password"), email_verified=True)
    users.create_user(user)
    mapper = app.dependency_overrides[get_personal_idea_mapper]()
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get('/my-schedule/ideas', follow_redirects=False).status_code == 303
        csrf = _login(client, user.email, "test-password")
        fields = dict(csrf_token=csrf, title="先研究缓存", note="阅读资料后做实验",
                      reference_title="官方文档", reference_url="https://example.com/docs")
        assert client.post('/my-schedule/ideas', data={**fields, 'title': '  '}).status_code == 422
        assert not mapper.list_all(user.id)
        assert client.post('/my-schedule/ideas', data=fields, follow_redirects=False).status_code == 303
        idea = mapper.list_all(user.id)[0]
        assert not events.list_all(user.id)
        prefill = client.get(f'/my-schedule/ideas/{idea.id}/arrange')
        assert prefill.status_code == 200
        assert all(value in prefill.text for value in ['先研究缓存', '阅读资料后做实验', '官方文档', 'https://example.com/docs', idea.id])
        assert not events.list_all(user.id)  # Opening and leaving the form creates nothing.
        event_fields = {**fields, 'source_idea_id': idea.id, 'event_type': 'personal',
                        'start_at': '2026-12-01T08:00', 'end_at': '2026-12-01T18:00'}
        assert client.post('/my-schedule/events', data={**event_fields, 'end_at': '2026-12-01T07:00'}).status_code == 422
        assert not events.list_all(user.id)
        for _ in range(2):
            assert client.post('/my-schedule/events', data=event_fields, follow_redirects=False).status_code == 303
        assert len(events.list_all(user.id)) == 1
        assert '已创建日程' in client.get('/my-schedule/ideas').text
        assert mapper.find_by_id(idea.id, user.id).status == 'pending'
        assert client.post('/my-schedule/ideas', data={**fields, 'idea_id': idea.id, 'title': '修改后的想法'}, follow_redirects=False).status_code == 303
        assert events.list_all(user.id)[0].title == '先研究缓存'
        assert client.post(f'/my-schedule/ideas/{idea.id}/abandon', data={'csrf_token': csrf}, follow_redirects=False).status_code == 303
        assert '修改后的想法' not in client.get('/my-schedule/ideas').text
        assert '修改后的想法' in client.get('/my-schedule/ideas?abandoned=true').text
        assert client.get(f'/my-schedule/ideas/{idea.id}/arrange').status_code == 409
        assert len(events.list_all(user.id)) == 1


def test_ideas_enforce_owner_and_csrf(tmp_path):
    app, users, _, events, _, _ = _public_fixture(tmp_path)
    from app.model.personal_idea import PersonalIdea
    mapper = app.dependency_overrides[get_personal_idea_mapper]()
    other = mapper.save(PersonalIdea(owner_id="another-user", title="私有想法"))
    user = AuthUser(email="isolated@example.com", password_hash=hash_password("test-password"), email_verified=True)
    users.create_user(user)
    with TestClient(app, base_url="https://testserver") as client:
        csrf = _login(client, user.email, 'test-password')
        assert '私有想法' not in client.get('/my-schedule/ideas').text
        assert client.get(f'/my-schedule/ideas/{other.id}/arrange').status_code == 404
        assert client.get(f'/my-schedule/ideas?edit={other.id}').status_code == 404
        assert client.post(f'/my-schedule/ideas/{other.id}/abandon', data={'csrf_token': csrf}).status_code == 404
        assert client.post('/my-schedule/ideas', data={'title': '无令牌'}).status_code == 403
        assert client.post('/my-schedule/events', data={'csrf_token': csrf, 'source_idea_id': other.id}).status_code == 404
        assert not events.list_all(user.id)
