from datetime import datetime, timedelta
from threading import Event
from unittest.mock import Mock

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.model.personal_event import PersonalEvent
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from app.service.reference_title_service import ReferenceTitleService, fetch_page_title, _TitleParser
from tests.test_public_multiuser import _public_fixture, _login


def test_legacy_links_and_reference_validation():
    payload = dict(type="personal", title="资料", start_at=datetime(2026, 11, 2, 8), end_at=datetime(2026, 11, 2, 18))
    legacy = PersonalEvent(**payload, location_url="https://www.bilibili.com/video/example")
    assert legacy.location_url is None and len(legacy.reference_links) == 1
    assert len(PersonalEvent.model_validate_json(legacy.model_dump_json()).reference_links) == 1
    mapped = PersonalEvent(**payload, location_url="https://map.baidu.com/")
    assert mapped.location_url and mapped.reference_links == []
    for url in ["javascript:alert(1)", "https://user:password@example.com/"]:
        with pytest.raises(ValidationError):
            PersonalEvent(**payload, reference_links=[{"url": url}])
    with pytest.raises(ValidationError):
        PersonalEvent(**payload, reference_links=[{"url": "https://example.com"}] * 21)


def test_title_worker_returns_immediately_caches_and_handles_failure():
    release = Event()
    fetcher = Mock(side_effect=lambda url: release.wait(1) and "网页标题")
    service = ReferenceTitleService(fetcher)
    try:
        assert service.lookup("https://example.com/") == ("", True)
        assert service.lookup("https://example.com/") == ("", True)
        release.set()
        service.queue.join()
        assert service.lookup("https://example.com/") == ("网页标题", False)
        assert fetcher.call_count == 1
        fetcher.side_effect = TimeoutError()
        service.lookup("https://example.com/failure")
        service.queue.join()
        assert service.lookup("https://example.com/failure") == ("", False)
    finally:
        release.set()
        service.close()


def test_save_enqueues_title_enrichment_without_waiting_and_preserves_manual_names():
    entered, release = Event(), Event()
    def fetch(url):
        entered.set()
        release.wait(2)
        return "自动标题"
    titles = ReferenceTitleService(fetch)
    now = datetime.now().astimezone()
    event = PersonalEvent(type="personal", title="测试", start_at=now,
                          end_at=now + timedelta(hours=1),
                          reference_links=[{"url": "https://example.com/"}])
    schedule = Mock()
    schedule.get_event.return_value = event.model_copy(deep=True)
    try:
        titles.enrich_event(schedule, event)
        assert entered.wait(1)
        schedule.mapper.save.assert_not_called()
        release.set()
        titles.queue.join()
        assert schedule.mapper.save.call_args.args[0].reference_links[0].title == "自动标题"
        schedule.mapper.save.reset_mock()
        schedule.get_event.return_value = event.model_copy(deep=True)
        schedule.get_event.return_value.reference_links[0].title = "用户刚填写的标题"
        titles.enrich_event(schedule, event)
        titles.queue.join()
        schedule.mapper.save.assert_not_called()
    finally:
        release.set()
        titles.close()


def test_private_dns_is_blocked_before_network_connection(monkeypatch):
    connect = Mock()
    monkeypatch.setattr("socket.getaddrinfo", lambda *args, **kwargs: [(2, 1, 6, '', ('127.0.0.1', 80))])
    monkeypatch.setattr("socket.create_connection", connect)
    with pytest.raises(ValueError, match="public"):
        fetch_page_title("http://example.com")
    connect.assert_not_called()
    parser = _TitleParser()
    parser.feed('<html><title> 视频 &amp; 教程 </title><body>ignored</body></html>')
    assert ''.join(parser.parts).strip() == "视频 & 教程"


def test_redirect_to_private_network_is_rechecked(monkeypatch):
    def addresses(host, *args, **kwargs):
        return [(2, 1, 6, '', ('127.0.0.1' if host == 'internal.example' else '93.184.216.34', 80))]
    connect = Mock(return_value=Mock())
    response = Mock(status=302)
    response.getheader.return_value = 'http://internal.example/private'
    connection = Mock()
    connection.getresponse.return_value = response
    monkeypatch.setattr('socket.getaddrinfo', addresses)
    monkeypatch.setattr('socket.create_connection', connect)
    monkeypatch.setattr('http.client.HTTPConnection', Mock(return_value=connection))
    with pytest.raises(ValueError, match='public'):
        fetch_page_title('http://example.com')
    connect.assert_called_once_with(('93.184.216.34', 80), timeout=3)
    connection.close.assert_called_once()


def test_multiple_links_survive_validation_edit_and_removal(tmp_path):
    app, mapper, _, events, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="links-edit@example.com", password_hash=hash_password("test-password"), email_verified=True)
    mapper.create_user(user)
    with TestClient(app, base_url="https://testserver") as client:
        csrf = _login(client, user.email, "test-password")
        fields = {"csrf_token":csrf, "event_type":"personal", "title":"多条资料", "start_at":"2026-12-01T08:00", "end_at":"2026-12-01T18:00", "reference_title":["视频", "文档"], "reference_url":["https://example.com/video", "https://example.com/docs"]}
        assert client.post('/my-schedule/events', data=fields, follow_redirects=False).status_code == 303
        event = events.list_all(user.id)[0]
        assert [link.title for link in event.reference_links] == ['视频', '文档']
        fields['reference_url'] = ['https://example.com/video', 'javascript:alert(1)']
        invalid = client.post(f'/my-schedule/events/{event.id}/edit', data=fields)
        assert invalid.status_code == 422 and 'https://example.com/video' in invalid.text
        assert len(events.find_by_id(event.id, user.id).reference_links) == 2
        fields['reference_url'] = ['https://example.com/updated']
        fields['reference_title'] = ['更新资料']
        assert client.post(f'/my-schedule/events/{event.id}/edit', data=fields, follow_redirects=False).status_code == 303
        assert len(events.find_by_id(event.id, user.id).reference_links) == 1
        fields['reference_url'] = ['']
        fields['reference_title'] = ['']
        assert client.post(f'/my-schedule/events/{event.id}/edit', data=fields, follow_redirects=False).status_code == 303
        assert events.find_by_id(event.id, user.id).reference_links == []


def test_reference_endpoint_is_owner_scoped_and_manual_titles_take_priority(tmp_path):
    app, mapper, _, events, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="links@example.com", password_hash=hash_password("test-password"), email_verified=True)
    mapper.create_user(user)
    now = datetime.now().astimezone()
    event = events.save(PersonalEvent(owner_id=user.id, type="personal", title="参考资料", start_at=now,
                                    end_at=now+timedelta(hours=1), reference_links=[{"title":"手动名称", "url":"https://example.com/"}, {"url":"https://example.com/video"}]))
    app.state.reference_titles.lookup = Mock(return_value=("抓取标题", False))
    with TestClient(app, base_url="https://testserver") as client:
        assert client.get(f"/my-schedule/events/{event.id}/reference-titles", follow_redirects=False).status_code == 303
        _login(client, user.email, "test-password")
        assert client.get('/my-schedule/reference-title', params={'url': 'https://example.com/'}).json()['title'] == '抓取标题'
        app.state.reference_titles.lookup.reset_mock()
        result = client.get(f"/my-schedule/events/{event.id}/reference-titles").json()
        assert [link["title"] for link in result["links"]] == ["手动名称", "抓取标题"]
        app.state.reference_titles.lookup.assert_called_once_with("https://example.com/video")
        page = client.get(f"/my-schedule/events/{event.id}").text
        assert "参考资料" in page and "<dt>地点</dt>" not in page
        stranger = events.save(event.model_copy(update={"id":"other-event", "owner_id":"other"}))
        assert client.get(f"/my-schedule/events/{stranger.id}/reference-titles").status_code == 404
