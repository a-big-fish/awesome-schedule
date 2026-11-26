import asyncio
import json
import logging

import httpx
import pymupdf
import pytest
from fastapi.testclient import TestClient

from app.controller import personal_resume_assistant_controller as assistant_controller
from app.controller.personal_resume_assistant_controller import get_custom_resume_provider, get_resume_model_store
from app.core.config import Settings
from app.core.exceptions import AIInterviewError
from app.service.resume_assistant_service import ResumeContext
from app.mapper.resume_model_store import ResumeModelStore
from app.service.ai_providers.resume_model_endpoint import CustomResumeProvider, discover_resume_models, model_endpoint
from app.service.ai_providers.openai_compatible import OpenAICompatibleProvider
from app.service.ai_providers.static import StaticAIInterviewProvider
from app.core.dependencies import get_ai_interview_provider, get_interview_experience_mapper
from app.model.ai_interview import AIInterviewMessage, AIModelConfig
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
        streamed = _stream_events(owner.post(endpoint, headers=headers, json={
            **payload, "model_ids": model_ids, "stream_suggestions": True,
        }))
        first_model = [event for event in streamed if event.get("model_id") == model_ids[0]]
        assert first_model.pop(0)["phase"] == "answer"
        assert [event["type"] for event in first_model] == ["model_suggestion"] * 7 + ["model_done"]
        assert first_model[-1]["page_count"] == 1
        assert streamed[-1] == {"type": "done"}
        assert owner.delete(f"{models}/{model_ids[0]}", headers=headers).status_code == 204


def test_review_stream_validates_complete_items_and_keeps_them_when_provider_interrupts():
    valid = {"category": "evidence", "page": 1, "original": "Built a cache",
             "issue": '保留引号 "参考" 和括号 }。', "rewrite": "Improved by 90%"}
    invalid = {**valid, "page": 2}
    first = json.dumps(valid, ensure_ascii=False)
    second = json.dumps({**valid, "issue": "说明测试方法。"}, ensure_ascii=False)

    async def check():
        release = asyncio.Event()
        provider = RecordingProvider()

        async def chunks(*args):
            yield '```json\n{"sugge'
            assert outgoing.empty()
            yield 'stions": [' + json.dumps(invalid) + ', ' + first[:-2]
            assert outgoing.empty()
            yield first[-2:]
            await release.wait()
            yield ', ' + second
            raise AIInterviewError("模拟接口中断")

        provider.stream = chunks
        outgoing = asyncio.Queue()
        task = asyncio.create_task(assistant_controller._stream_model_review(
            "test-model", provider, None, [], ResumeContext(1, {1: "Built a cache"}),
            True, outgoing,
        ))
        try:
            frame, terminal = await asyncio.wait_for(outgoing.get(), timeout=2)
            item = json.loads(frame.removeprefix("data: "))
            assert not terminal and item["type"] == "model_suggestion"
            assert item["item"]["issue"] == valid["issue"]
            assert item["item"]["rewrite"] == ""
            assert not task.done()
            release.set()
            remaining = []
            while not terminal:
                frame, terminal = await asyncio.wait_for(outgoing.get(), timeout=2)
                remaining.append(json.loads(frame.removeprefix("data: ")))
            assert [event["type"] for event in remaining] == ["model_suggestion", "model_error"]
            assert remaining[0]["item"]["issue"] == "说明测试方法。"
            assert remaining[1]["message"] == "模拟接口中断"
        finally:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)

    asyncio.run(check())


@pytest.mark.parametrize("final_content, expected_issue", [
    ('{"suggestions":[{"category":"evidence","page":1,"original":"Built a cache",'
     '"issue":"给出验证口径。","rewrite":""}]}', "给出验证口径。"),
    ('{"suggestions":[{"category":"evidence","page":1,"original":"Built a cache",'
     '"issue":"说明缓存的对照测量方法","rewrite":"替', "说明缓存的对照测量方法"),
    ('{"suggestions":[{"category":"evidence","page":1,"original":"Built a cache",'
     '"issue":"说明缓存的对照测量方法', "说明缓存的对照测量方法…"),
    ("", None),
])
def test_truncated_review_keeps_final_chunk_and_diagnoses_reasoning_only(final_content, expected_issue, caplog):
    requests = []
    private_reasoning = "synthetic private reasoning"

    async def upstream(request):
        payload = json.loads(request.content)
        requests.append(payload)
        if len(requests) == 1:
            assert payload["reasoning_effort"] == "none" and "thinking" not in payload
            return httpx.Response(400, json={"error": {"param": "reasoning_effort"}})
        assert "reasoning_effort" not in payload and "thinking" not in payload
        frames = [
            {"choices": [{"delta": {"reasoning_content": private_reasoning}}]},
            {"choices": [{"delta": {"content": final_content}, "finish_reason": "length"}]},
        ]
        return httpx.Response(200, text="".join(f"data: {json.dumps(frame)}\n\n" for frame in frames))

    async def check():
        provider = CustomResumeProvider(
            Settings(_env_file=None, ai_max_completion_tokens=8192), httpx.MockTransport(upstream), review_mode=True,
        )
        config = AIModelConfig(base_url="https://mock.example/v1", model="DeepSeek-V4.1-Flash",
                               api_key="synthetic-secret")
        outgoing = asyncio.Queue()
        await assistant_controller._stream_model_review(
            "test-model", provider, config, [], ResumeContext(1, {1: "Built a cache"}),
            True, outgoing, "test-request",
        )
        events = []
        while not outgoing.empty():
            frame, _ = outgoing.get_nowait()
            events.append(json.loads(frame.removeprefix("data: ")))
        assert events[0]["phase"] == "reasoning"
        assert all(private_reasoning not in json.dumps(event) for event in events)
        assert events[-1]["type"] == "model_error"
        suggestions = [event["item"] for event in events if event["type"] == "model_suggestion"]
        if expected_issue:
            assert events[1]["phase"] == "answer"
            assert [item["issue"] for item in suggestions] == [expected_issue]
            assert suggestions[0]["rewrite"] == ""
        else:
            assert not suggestions
            assert "尚未生成建议正文" in events[-1]["message"]

    with caplog.at_level(logging.INFO, logger="app"):
        asyncio.run(check())
    assert len(requests) == 2
    assert "request_id=test-request" in caplog.text and "finish=length" in caplog.text
    assert "first_reasoning_ms=" in caplog.text and "first_suggestion_ms=" in caplog.text
    assert "outcome=truncated" in caplog.text
    assert private_reasoning not in caplog.text and "synthetic-secret" not in caplog.text


def test_review_payload_disables_reasoning_on_deepseek_and_on_gateways():
    """Reasoning must be suppressed: it costs 10-25s per model before any suggestion."""
    payloads = []

    async def upstream(request):
        payloads.append(json.loads(request.content))
        frame = {"choices": [{"delta": {"content": '{"suggestions":[]}'}, "finish_reason": "stop"}]}
        return httpx.Response(200, text=f"data: {json.dumps(frame)}\n\n")

    async def check():
        official = OpenAICompatibleProvider(
            Settings(_env_file=None, ai_model="deepseek-v4.1-flash", ai_api_base_url="https://api.deepseek.com",
                     ai_api_key="synthetic", ai_reasoning_effort="none"),
            httpx.MockTransport(upstream), review_mode=True)
        async for _ in official.stream([AIInterviewMessage(role="user", content="hi")]):
            pass
        gateway = CustomResumeProvider(
            Settings(_env_file=None, ai_reasoning_effort="none"),
            httpx.MockTransport(upstream), review_mode=True)
        config = AIModelConfig(base_url="https://gateway.example/v1", model="DeepSeek-V4.1-Flash",
                               api_key="synthetic")
        async for _ in gateway.stream([AIInterviewMessage(role="user", content="hi")], config):
            pass

    asyncio.run(check())
    assert payloads[0]["thinking"] == {"type": "disabled"}  # documented only by the official host
    assert payloads[0]["reasoning_effort"] == "none"
    assert "thinking" not in payloads[1]  # custom gateways ignore it and stay slow
    assert payloads[1]["reasoning_effort"] == "none"


def test_profile_page_exposes_model_management(tmp_path):
    app, users, _, _, _, _ = _public_fixture(tmp_path)
    user = AuthUser(email="models-profile@example.com", password_hash=hash_password("test-password"),
                    email_verified=True)
    users.create_user(user)
    with TestClient(app, base_url="https://testserver") as client:
        _login(client, user.email, "test-password")
        page = client.get("/my-schedule/profile")
        assert page.status_code == 200
        for marker in ('id="resume-models"', 'data-resume-models-list', 'data-resume-models-dialog',
                       'data-resume-models-discover', 'personal_resume_models.js', 'AI 模型'):
            assert marker in page.text


def test_custom_api_requires_https_and_public_addresses():
    for url in ("http://example.com/v1", "https://[::1]", "https://127.0.0.1", "https://local:pass@example.com",
                "https://example.com/v1?key=secret", "https://[::ffff:127.0.0.1]"):
        with pytest.raises(AIInterviewError):
            model_endpoint(url, "models")

    def redirect(request):
        assert request.url.host == "provider.example"
        return httpx.Response(302, headers={"Location": "https://127.0.0.1/"})

    with pytest.raises(AIInterviewError, match="HTTP 302"):
        asyncio.run(discover_resume_models("https://provider.example", "test-key", httpx.MockTransport(redirect)))
