import asyncio
import json

import httpx
import pymupdf
from fastapi.testclient import TestClient

from app.controller import personal_resume_assistant_controller as assistant_controller
from app.controller.personal_resume_assistant_controller import get_custom_resume_provider, get_resume_model_store
from app.core.config import Settings
from app.mapper.resume_model_store import ResumeModelStore
from app.service.ai_providers.resume_model_endpoint import CustomResumeProvider, discover_resume_models, model_endpoint
from app.service.ai_providers.static import StaticAIInterviewProvider
from app.core.dependencies import get_ai_interview_provider, get_interview_experience_mapper
from app.model.auth import AuthUser
from app.model.interview_experience import InterviewExperience, InterviewQuestion
from app.service.ai_providers.base import AIInterviewProvider
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _login, _public_fixture


class RecordingProvider(AIInterviewProvider):
    name = "recording"

    def __init__(self):
        self.messages = []
        self.reply = "你如何验证缓存策略生效？"

    async def stream(self, messages, model_config=None):
        self.messages = messages
        yield self.reply[:5]
        yield self.reply[5:]


def _stream_events(response):
    assert response.status_code == 200, response.text
    return [json.loads(line.removeprefix("data: ")) for line in response.text.splitlines()
            if line.startswith("data: ")]


def _upload_resume(client, csrf, content):
    uploaded = client.post("/my-schedule/resume", follow_redirects=False, data={"csrf_token": csrf},
                           files={"resume": ("test.pdf", content, "application/pdf")})
    assert uploaded.status_code == 303
    return client.get("/my-schedule/resume/current").json()["resume"]["version"]


def test_assistant_uses_owned_resume_and_notes_and_returns_grounded_suggestions(tmp_path):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    provider = RecordingProvider()
    app.dependency_overrides[get_ai_interview_provider] = lambda: provider
    alice = AuthUser(email="resume-owner@example.test", password_hash=hash_password("test-password"), email_verified=True)
    bob = AuthUser(email="other-owner@example.test", password_hash=hash_password("test-password"), email_verified=True)
    users.create_user(alice)
    users.create_user(bob)
    notes = app.dependency_overrides[get_interview_experience_mapper]()
    for user, question in [(alice, "Redis 缓存过期后怎样减少回源？"), (bob, "Redis 的其他账号私有题目")]:
        note = notes.save_experience(InterviewExperience(
            owner_id=user.id, title="后端一面", position="后端", is_public=False,
        ))
        notes.save_question(InterviewQuestion(experience_id=note.id, category="project",
                                              asked_order=1, question_text=question), user.id)

    with pymupdf.open() as pdf:
        for index in range(3):
            page = pdf.new_page()
            if index == 1:
                page.insert_text((70, 72), "contact@example.test\nImplemented Redis caching")
        content = pdf.tobytes()

    api = "/my-schedule/resume/assistant"
    with TestClient(app, base_url="https://testserver") as owner, TestClient(app, base_url="https://testserver") as other:
        assert owner.post(api, json={"version": "unknown", "action": "start"}).status_code == 401
        csrf = _login(owner, alice.email, "test-password")
        version = _upload_resume(owner, csrf, content)
        assert "data-resume-assistant-toggle" in owner.get("/my-schedule/interviews").text
        request = {"version": version, "action": "start", "position": "后端", "focus": "Redis"}
        headers = {"X-CSRF-Token": csrf}
        assert owner.post(api, json=request).status_code == 403
        assert owner.post(api, headers=headers, json={**request, "version": "outdated"}).status_code == 409
        bob_csrf = _login(other, bob.email, "test-password")
        assert other.post(api, json=request, headers={"X-CSRF-Token": bob_csrf}).status_code == 404

        first = _stream_events(owner.post(api, json=request, headers=headers))
        assert "".join(event["content"] for event in first if event["type"] == "delta") == provider.reply
        assert first[-1]["type"] == "done"
        assert "Implemented Redis caching" in provider.messages[1].content
        assert "contact@example.test" not in provider.messages[1].content
        assert "怎样减少回源" not in provider.messages[1].content

        continued = {**request, "action": "answer", "reference_experiences": True,
                     "history": [{"role": "assistant", "content": provider.reply}], "answer": "我比较了缓存命中率。"}
        second = _stream_events(owner.post(api, json=continued, headers=headers))
        assert second[0]["type"] == "sources"
        assert [item["title"] for item in second[0]["sources"]] == ["后端一面"]
        assert "怎样减少回源" in provider.messages[1].content
        assert "其他账号私有题目" not in provider.messages[1].content
        assert "我比较了缓存命中率" in provider.messages[-1].content

        provider.reply = json.dumps({"suggestions": [
            {"category": "evidence", "page": 2, "original": "Implemented Redis caching",
             "issue": "补充缓存命中率的测量口径。", "rewrite": "Improved metrics by 95%"},
            {"category": "wording", "page": 1, "original": "Unverified sentence", "issue": "重写。"},
        ]}, ensure_ascii=False)
        response = _stream_events(owner.post(api, json={**request, "action": "optimize"}, headers=headers))
        assert response[-1]["type"] == "done"
        suggestions = response[0]["items"]
        assert len(suggestions) == 2
        assert suggestions[0]["category"] == "structure"
        assert suggestions[1]["page"] == 2 and suggestions[1]["rewrite"] == ""


def test_custom_models_compare_identical_context_concurrently_and_keep_all_suggestions(tmp_path, monkeypatch):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    store = ResumeModelStore(tmp_path / "models.sqlite3")
    app.dependency_overrides[get_resume_model_store] = lambda: store
    app.dependency_overrides[get_ai_interview_provider] = StaticAIInterviewProvider
    requests = {}

    async def completion(request):
        payload = json.loads(request.content)
        requests[request.url.host] = (request.headers["Authorization"], payload)
        if len(requests) == 1:
            for _ in range(100):
                if len(requests) > 1:
                    break
                await asyncio.sleep(0.01)
            else:
                return httpx.Response(500)
        if request.url.host == "second.example":
            return httpx.Response(401)
        suggestions = [{"category": "wording", "page": 1, "original": "Built a cache",
                        "issue": f"Synthetic suggestion {index}"} for index in range(7)]
        line = json.dumps({"choices": [{"delta": {"content": json.dumps({"suggestions": suggestions})}}]})
        return httpx.Response(200, text=f"data: {line}\n\ndata: [DONE]\n\n")

    async def list_models(url, key):
        def handler(request):
            assert str(request.url) == "https://first.example/v1/models"
            assert request.headers["Authorization"] == "Bearer secret-first"
            return httpx.Response(200, json={"data": [{"id": "chat-a"}, {"id": "chat-a"}, {"id": "chat-b"}]})
        return await discover_resume_models(url, key, httpx.MockTransport(handler))

    monkeypatch.setattr(assistant_controller, "discover_resume_models", list_models)
    app.dependency_overrides[get_custom_resume_provider] = lambda: CustomResumeProvider(
        Settings(_env_file=None, ai_max_completion_tokens=8192), httpx.MockTransport(completion), review_mode=True,
    )
    for email in ("owner@example.test", "other@example.test"):
        users.create_user(AuthUser(email=email, password_hash=hash_password("test-password"), email_verified=True))

    with pymupdf.open() as pdf:
        pdf.new_page().insert_text((70, 72), "contact@example.test\nBuilt a cache")
        content = pdf.tobytes()

    endpoint = "/my-schedule/resume/assistant"
    models = endpoint + "/models"
    with TestClient(app, base_url="https://testserver") as owner, TestClient(app, base_url="https://testserver") as other:
        assert owner.get(models, follow_redirects=False).status_code == 303
        csrf = _login(owner, "owner@example.test", "test-password")
        headers = {"X-CSRF-Token": csrf}
        initial = owner.get(models)
        assert initial.json() == {"models": []}
        assert initial.headers["Cache-Control"] == "no-store"
        credentials = {"base_url": "https://first.example", "api_key": "secret-first"}
        assert owner.post(models + "/discover", headers=headers, json=credentials).json() == {"models": ["chat-a", "chat-b"]}
        assert owner.post(models, json={**credentials, "model": "chat-a"}).status_code == 403

        model_ids = []
        for index, (base_url, api_key) in enumerate([
            ("https://first.example/v1", "secret-first"),
            ("https://second.example/v1/chat/completions", "secret-second"),
        ]):
            added = owner.post(models, headers=headers, json={
                "name": f"API {index}", "model": f"chat-{index}", "base_url": base_url, "api_key": api_key,
            })
            assert added.status_code == 201
            assert api_key not in added.text
            model_ids.append(added.json()["id"])

        invalid = owner.post(models, headers=headers, json={**credentials, "model": ""})
        assert invalid.status_code == 422 and "secret-first" not in invalid.text
        assert b"secret-first" not in store.path.read_bytes()
        assert [item["id"] for item in owner.get(models).json()["models"]] == model_ids

        other_headers = {"X-CSRF-Token": _login(other, "other@example.test", "test-password")}
        assert other.get(models).json() == {"models": []}
        assert other.delete(f"{models}/{model_ids[0]}", headers=other_headers).status_code == 404

        version = _upload_resume(owner, csrf, content)
        payload = {"version": version, "action": "optimize"}
        for invalid_ids in ([], [model_ids[0]] * 2, [*model_ids, "extra"]):
            assert owner.post(endpoint, headers=headers, json={**payload, "model_ids": invalid_ids}).status_code == 422
        forbidden = other.post(endpoint, headers=other_headers, json={**payload, "model_ids": model_ids})
        assert forbidden.status_code == 404

        events = _stream_events(owner.post(endpoint, headers=headers, json={**payload, "model_ids": model_ids}))
        results = {event["model_id"]: event for event in events[:-1]}
        assert events[-1] == {"type": "done"}
        assert results[model_ids[0]]["type"] == "model_suggestions"
        assert len(results[model_ids[0]]["items"]) == 7
        assert results[model_ids[1]]["type"] == "model_error"
        auth1, first = requests["first.example"]
        auth2, second = requests["second.example"]
        assert auth1 == "Bearer secret-first" and auth2 == "Bearer secret-second"
        assert first["model"] == "chat-0" and second["model"] == "chat-1"
        assert first["messages"] == second["messages"]
        assert "contact@example.test" not in json.dumps(first)
        assert owner.delete(f"{models}/{model_ids[0]}", headers=headers).status_code == 204


def test_custom_api_requires_https_and_public_addresses():
    import pytest
    from app.core.exceptions import AIInterviewError

    for url in ("http://example.com/v1", "https://[::1]", "https://127.0.0.1", "https://local:pass@example.com",
                "https://example.com/v1?key=secret", "https://[::ffff:127.0.0.1]"):
        with pytest.raises(AIInterviewError):
            model_endpoint(url, "models")

    def redirect(request):
        assert request.url.host == "provider.example"
        return httpx.Response(302, headers={"Location": "https://127.0.0.1/"})

    with pytest.raises(AIInterviewError, match="HTTP 302"):
        asyncio.run(discover_resume_models("https://provider.example", "test-key", httpx.MockTransport(redirect)))
