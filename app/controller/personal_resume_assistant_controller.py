"""Stream resume practice and grounded revision suggestions to the personal workspace."""

import asyncio
import json
import logging
from contextlib import aclosing
from collections.abc import AsyncIterator
from pathlib import Path
from time import perf_counter
from typing import TypeVar

from cryptography.fernet import InvalidToken
from fastapi import APIRouter, Depends, HTTPException, Request, Response
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import ValidationError
from starlette.concurrency import run_in_threadpool

from app.controller.schema.resume_assistant_dto import ResumeAssistantInput, ResumeModelCredentials, ResumeModelInput
from app.core.dependencies import (
    get_ai_interview_provider, get_interview_experience_service,
    get_personal_resume_state_service, get_resume_file_storage, get_resume_parser_service,
)
from app.core.exceptions import AIInterviewError, AIOutputTruncatedError, ResumeFileNotFoundError
from app.core.postgres_pool import release_shared_connection
from app.mapper.base_resume_file_storage import ResumeFileStorage
from app.mapper.resume_model_store import ResumeModel, ResumeModelStore
from app.model.ai_interview import AIInterviewMessage, AIModelConfig
from app.service.ai_providers.base import AIInterviewProvider
from app.service.ai_providers.openai_compatible import OpenAICompatibleProvider
from app.service.ai_providers.resume_model_endpoint import CustomResumeProvider, discover_resume_models, model_endpoint
from app.service.resume_review_store import ResumeReviewStore
from app.service.interview_experience_service import InterviewExperienceService
from app.service.personal_resume_service import PersonalResumeStateService
from app.service.resume_assistant_service import (
    ResumeContext, ResumeSuggestion, ResumeSuggestionReview, assistant_messages, select_experience_questions,
)
from app.service.resume_parser_service import ResumeParserService


logger = logging.getLogger(__name__)
router = APIRouter()
_DEFAULT_MODEL = "default"


def get_resume_model_store(request: Request) -> ResumeModelStore:
    return ResumeModelStore(Path(request.app.state.settings.resume_models_db_path))


def get_custom_resume_provider(request: Request) -> AIInterviewProvider:
    return CustomResumeProvider(request.app.state.settings, review_mode=True)


def get_resume_review_store(
    storage: ResumeFileStorage = Depends(get_resume_file_storage),
) -> ResumeReviewStore:
    return ResumeReviewStore(storage)


def _review_provider(provider: AIInterviewProvider) -> AIInterviewProvider:
    if isinstance(provider, OpenAICompatibleProvider):
        return OpenAICompatibleProvider(provider.settings, provider.transport, review_mode=True)
    return provider


def _owner_id(request: Request) -> str:
    if request.app.state.settings.app_mode == "public_personal":
        return request.state.user.id
    return "local"


def _default_model(provider: AIInterviewProvider, request: Request) -> ResumeModel | None:
    settings = provider.settings if isinstance(provider, OpenAICompatibleProvider) else request.app.state.settings
    if provider.name == "static":
        return None
    if isinstance(provider, OpenAICompatibleProvider) and not (settings.ai_model and settings.ai_api_key and settings.ai_api_base_url):
        return None
    name = "DS" if "deepseek" in settings.ai_model.casefold() else "默认模型"
    return ResumeModel(_DEFAULT_MODEL, name, settings.ai_model)


ModelInput = TypeVar("ModelInput", bound=ResumeModelCredentials)


async def _read_model_input(request: Request, schema: type[ModelInput]) -> ModelInput:
    # Keep submitted credentials out of validation responses.
    try:
        return schema.model_validate(await request.json())
    except (ValidationError, ValueError) as exc:
        raise HTTPException(422, "请检查 API URL、API Key 和模型配置。") from exc


@router.get("/resume/assistant/models")
def list_resume_models(
    request: Request,
    response: Response,
    store: ResumeModelStore = Depends(get_resume_model_store),
    provider: AIInterviewProvider = Depends(get_ai_interview_provider),
):
    response.headers["Cache-Control"] = "no-store"
    default = _default_model(provider, request)
    return {"models": [*([default] if default else []), *store.list(_owner_id(request))]}


@router.post("/resume/assistant/models/discover")
async def discover_models(request: Request, response: Response):
    response.headers["Cache-Control"] = "no-store"
    credentials = await _read_model_input(request, ResumeModelCredentials)
    try:
        models = await asyncio.wait_for(
            discover_resume_models(credentials.base_url, credentials.api_key.get_secret_value()), timeout=20,
        )
    except AIInterviewError as exc:
        raise HTTPException(422, str(exc)) from exc
    except asyncio.TimeoutError as exc:
        raise HTTPException(504, "获取模型列表超时，请稍后重试。") from exc
    return {"models": models}


@router.post("/resume/assistant/models", status_code=201)
async def save_resume_model(
    request: Request,
    response: Response,
    store: ResumeModelStore = Depends(get_resume_model_store),
):
    response.headers["Cache-Control"] = "no-store"
    payload = await _read_model_input(request, ResumeModelInput)
    try:
        model_endpoint(payload.base_url, "chat/completions")
    except AIInterviewError as exc:
        raise HTTPException(422, str(exc)) from exc
    return await run_in_threadpool(
        store.save, _owner_id(request), payload.name, payload.base_url, payload.model, payload.api_key.get_secret_value(),
    )


@router.delete("/resume/assistant/models/{model_id}", status_code=204)
def delete_resume_model(
    model_id: str, request: Request, store: ResumeModelStore = Depends(get_resume_model_store),
):
    if not store.delete(_owner_id(request), model_id):
        raise HTTPException(404, "模型配置不存在。")
    return Response(status_code=204)


def _event(kind: str, **payload) -> str:
    return f"data: {json.dumps({'type': kind, **payload}, ensure_ascii=False)}\n\n"


async def _stream_model_review(
    model_id: str | None, provider: AIInterviewProvider, config: AIModelConfig | None,
    messages: list[AIInterviewMessage], context: ResumeContext, progressive: bool,
    outgoing: asyncio.Queue[tuple[str, bool]], request_id: str | None = None,
) -> None:
    review = ResumeSuggestionReview(context)
    started = perf_counter()
    first_suggestion_ms = None
    count = 0

    async def stage(phase: str) -> None:
        if progressive:
            await outgoing.put((_event("model_progress", model_id=model_id, phase=phase), False))

    async def send_suggestions(suggestions: list[ResumeSuggestion]) -> None:
        nonlocal count, first_suggestion_ms
        for suggestion in suggestions:
            if first_suggestion_ms is None:
                first_suggestion_ms = round((perf_counter() - started) * 1000)
            count += 1
            await outgoing.put((_event("model_suggestion", model_id=model_id,
                                       item=suggestion.model_dump()), False))

    async def analyze() -> str:
        size = 0
        if isinstance(provider, OpenAICompatibleProvider):
            stream = provider.stream(messages, config, on_stage=stage, request_id=request_id, model_id=model_id)
        else:
            stream = provider.stream(messages, config)
        async with aclosing(stream):
            async for chunk in stream:
                size += len(chunk)
                if size > 96000:
                    raise AIOutputTruncatedError("分析结果超出单次响应容量。")
                await send_suggestions(review.append(chunk, progressive=progressive))
        remaining = review.finish()
        if progressive:
            await send_suggestions(remaining)
            return _event("model_done", model_id=model_id, page_count=context.page_count)
        result = {"page_count": context.page_count, "items": [item.model_dump() for item in remaining]}
        if model_id is None:
            return _event("suggestions", **result)
        return _event("model_suggestions", model_id=model_id, **result)

    outcome = "completed"
    message = ""
    try:
        terminal = await asyncio.wait_for(analyze(), timeout=120)
    except asyncio.TimeoutError:
        outcome, message = "timeout", "等待模型分析超时，请稍后重试。"
    except AIOutputTruncatedError as exc:
        outcome, message = "truncated", str(exc)
    except AIInterviewError as exc:
        outcome, message = "provider_error", str(exc)
    except ValueError:
        outcome, message = "invalid_review", "模型返回的建议无法对应简历原文，或输出不完整。"
    except Exception as exc:
        logger.warning("Resume review stream failed request_id=%s error=%s", request_id, type(exc).__name__)
        outcome, message = "error", "模型分析中断，请稍后重试。"
    if outcome != "completed":
        if progressive:
            await send_suggestions(review.recover_partial())
        terminal = _review_error(model_id, message or "模型分析中断，请稍后重试。")
    if progressive:
        logger.info(
            "Resume model review request_id=%s model_id=%s outcome=%s first_suggestion_ms=%s "
            "total_ms=%s suggestions=%s",
            request_id, model_id, outcome, first_suggestion_ms, round((perf_counter() - started) * 1000), count,
        )
    await outgoing.put((terminal, True))



def _review_error(model_id: str | None, message: str) -> str:
    if model_id is None:
        return _event("error", message=message)
    return _event("model_error", model_id=model_id, message=message)



@router.get("/resume/assistant/reviews")
def resume_review_history(
    state: PersonalResumeStateService = Depends(get_personal_resume_state_service),
    parser: ResumeParserService = Depends(get_resume_parser_service),
    reviews: ResumeReviewStore = Depends(get_resume_review_store),
):
    """List saved reviews for the current resume, newest first."""
    current = state.current()
    resume = parser.get_resume(current["id"]) if current else None
    if not resume or resume.owner_id != state.owner_id:
        return {"reviews": []}
    return {"reviews": reviews.list(pdf_path=resume.pdf_path, owner_id=state.owner_id)}


@router.get("/resume/assistant/reviews/{review_id}")
def resume_review_detail(
    review_id: str,
    state: PersonalResumeStateService = Depends(get_personal_resume_state_service),
    parser: ResumeParserService = Depends(get_resume_parser_service),
    reviews: ResumeReviewStore = Depends(get_resume_review_store),
):
    current = state.current()
    resume = parser.get_resume(current["id"]) if current else None
    document = reviews.read(pdf_path=resume.pdf_path, owner_id=state.owner_id, review_id=review_id) \
        if resume and resume.owner_id == state.owner_id else None
    if not document:
        raise HTTPException(404, "记录不存在。")
    return JSONResponse(document, headers={"Cache-Control": "no-store"})


async def _save_review(review_store: ResumeReviewStore, owner_id: str, resume, position: str,
                       collected: dict[str, list[dict]], provider: AIInterviewProvider, request: Request,
                       model_store: ResumeModelStore) -> bool:
    """Persist one review document per optimize run, beside the resume PDF."""
    names = {entry.id: entry for entry in await run_in_threadpool(model_store.list, owner_id)}
    default = _default_model(provider, request)
    if default:
        names.setdefault(_DEFAULT_MODEL, default)
    models = []
    for model_id, items in collected.items():
        entry = names.get(model_id)
        models.append({
            "model_id": model_id,
            "name": entry.name if entry else (model_id or "默认模型"),
            "model": entry.model if entry else "",
            "suggestions": items,
        })
    try:
        await run_in_threadpool(review_store.save, owner_id=owner_id, resume_id=resume.id,
                                pdf_path=resume.pdf_path, position=position, models=models)
        return True
    except Exception:  # noqa: BLE001 - history is a convenience, never fail the stream
        logger.warning("Resume review persistence failed owner=%s resume=%s", owner_id, resume.id)
        return False


@router.post("/resume/assistant")
def resume_assistant(
    payload: ResumeAssistantInput,
    request: Request,
    state: PersonalResumeStateService = Depends(get_personal_resume_state_service),
    parser: ResumeParserService = Depends(get_resume_parser_service),
    experiences: InterviewExperienceService = Depends(get_interview_experience_service),
    provider: AIInterviewProvider = Depends(get_ai_interview_provider),
    custom: AIInterviewProvider = Depends(get_custom_resume_provider),
    store: ResumeModelStore = Depends(get_resume_model_store),
    review_store: ResumeReviewStore = Depends(get_resume_review_store),
) -> StreamingResponse:
    targets = []
    if payload.model_ids is not None:
        for model_id in payload.model_ids:
            if model_id == _DEFAULT_MODEL and _default_model(provider, request):
                targets.append((model_id, _review_provider(provider), None))
                continue
            try:
                config = store.get(state.owner_id, model_id)
            except (OSError, InvalidToken, ValueError) as exc:
                raise HTTPException(503, "无法读取已保存的模型密钥。") from exc
            if config is None:
                raise HTTPException(404, "有模型配置不可用，请重新选择。")
            targets.append((model_id, custom, config))
    elif provider.name == "static":
        raise HTTPException(503, "服务端还没有配置可用的 AI 模型。")
    elif payload.action == "optimize":
        targets.append((None, _review_provider(provider), None))

    if payload.action == "answer" and (not payload.answer or not payload.history):
        raise HTTPException(422, "请先开始练习并填写回答。")
    if payload.action == "review" and not any(turn.role == "user" for turn in payload.history):
        raise HTTPException(422, "至少回答一道问题后可以复盘。")

    current = state.current()
    if not current:
        raise HTTPException(404, "请先上传 PDF 简历。")
    if payload.version != current["version"]:
        raise HTTPException(409, "简历已更换，请同步简历后重试。")
    resume = parser.get_resume(current["id"])
    if not resume or resume.owner_id != state.owner_id:
        raise HTTPException(404, "简历不存在。")

    sources = select_experience_questions(
        experiences, payload.position, payload.focus, resume.candidate_name,
    ) if payload.reference_experiences and payload.action in {"start", "answer"} else []
    release_shared_connection()
    try:
        context = ResumeContext.read(parser, resume)
    except ResumeFileNotFoundError as exc:
        raise HTTPException(404, "无法读取简历，请同步或重新上传。") from exc
    if not context.pages:
        raise HTTPException(422, "这份 PDF 没有可提取的文字，请上传可选中文字的 PDF。")
    messages = assistant_messages(
        context, payload.action, payload.position, payload.focus,
        [(turn.role, turn.content) for turn in payload.history], payload.answer, sources, resume.candidate_name,
    )

    async def stream() -> AsyncIterator[str]:
        if targets:
            outgoing: asyncio.Queue[tuple[str, bool]] = asyncio.Queue(maxsize=8)
            collected: dict[str, list[dict]] = {}
            tasks = [asyncio.create_task(
                _stream_model_review(*target, messages, context, payload.stream_suggestions, outgoing,
                                     getattr(request.state, "request_id", None))
            ) for target in targets]
            try:
                pending_models = len(tasks)
                while pending_models:
                    try:
                        frame, terminal = await asyncio.wait_for(outgoing.get(), timeout=15)
                    except asyncio.TimeoutError:
                        yield ": keep-alive\n\n"
                        continue
                    yield frame
                    try:
                        event = json.loads(frame.removeprefix("data: ").strip())
                    except ValueError:
                        event = {}
                    if event.get("type") == "model_suggestion":
                        collected.setdefault(event.get("model_id"), []).append(event.get("item"))
                    pending_models -= int(terminal)
                if payload.action == "optimize" and collected:
                    if await _save_review(review_store, state.owner_id, resume, payload.position,
                                          collected, provider, request, store):
                        yield _event("saved")
                yield _event("done")
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
            return

        try:
            if payload.reference_experiences and payload.action in {"start", "answer"}:
                unique = {source.id: source.title for source in sources}
                yield _event("sources", sources=[{"id": id_, "title": title} for id_, title in unique.items()])
            reply = ""
            async for chunk in provider.stream(messages):
                reply += chunk
                if len(reply) > 12000:
                    raise AIInterviewError("模型回复过长，请缩小练习范围后重试。")
                yield _event("delta", content=chunk)
            if not reply.strip():
                raise AIInterviewError("AI 暂未返回内容，请重试。")
            yield _event("done")
        except AIInterviewError as exc:
            yield _event("error", message=str(exc))

    return StreamingResponse(stream(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-store", "X-Accel-Buffering": "no"})
