import asyncio
import json
import logging
from collections.abc import AsyncIterator, Awaitable, Callable
from time import perf_counter

import httpx

from app.core.config import Settings
from app.core.exceptions import AIInterviewError, AIOutputTruncatedError
from app.model.ai_interview import AIInterviewMessage, AIModelConfig
from app.service.ai_providers.base import AIInterviewProvider


logger = logging.getLogger(__name__)


async def _rejects_thinking(response: httpx.Response) -> bool:
    """Retry only an explicit compatibility rejection, before any output."""
    error_body = bytearray()
    async for chunk in response.aiter_bytes():
        error_body.extend(chunk[:4096 - len(error_body)])
        if len(error_body) >= 4096:
            break
    try:
        error = json.loads(error_body).get("error", {})
        if isinstance(error, dict) and error.get("param") in ("thinking", "thinking.type"):
            return True
    except (AttributeError, ValueError):
        pass
    message = error_body.decode(errors="replace").lower()
    return "thinking" in message and any(
        phrase in message for phrase in ("unsupported", "not supported", "unknown field", "unrecognized",
                                         "unexpected", "extra inputs", "extra fields")
    )



class OpenAICompatibleProvider(AIInterviewProvider):
    """Adapter for OpenAI-compatible streaming chat completion APIs."""

    def __init__(
        self,
        settings: Settings,
        transport: httpx.AsyncBaseTransport | None = None,
        *,
        review_mode: bool = False,
    ) -> None:
        self.settings = settings
        self.transport = transport
        self.review_mode = review_mode

    def _make_transport(self) -> httpx.AsyncBaseTransport | None:
        return self.transport

    @property
    def name(self) -> str:
        return "openai_compatible"

    @staticmethod
    def _chat_url(base_url: str) -> str:
        normalized_url = base_url.rstrip("/")
        if normalized_url.endswith("/chat/completions"):
            return normalized_url
        if not normalized_url.endswith("/v1"):
            normalized_url = f"{normalized_url}/v1"
        return f"{normalized_url}/chat/completions"

    @staticmethod
    def _resolve_model_settings(
        settings: Settings,
        model_config: AIModelConfig | None,
    ) -> tuple[str, str, str]:
        """Combine request-level overrides with server-side provider settings."""
        override = model_config or AIModelConfig()
        base_url = override.base_url or settings.ai_api_base_url
        model = override.model or settings.ai_model
        api_key = (
            override.api_key.get_secret_value()
            if override.api_key
            else settings.ai_api_key
        )
        return base_url, model, api_key

    async def stream(
        self,
        messages: list[AIInterviewMessage],
        model_config: AIModelConfig | None = None,
        *,
        on_stage: Callable[[str], Awaitable[None]] | None = None,
        request_id: str | None = None,
        model_id: str | None = None,
    ) -> AsyncIterator[str]:
        base_url, model, api_key = self._resolve_model_settings(self.settings, model_config)
        if not base_url:
            raise AIInterviewError("AI API URL 未配置，请检查模型配置")
        if not model:
            raise AIInterviewError("AI 模型未配置，请检查模型配置")
        if not api_key:
            raise AIInterviewError("AI API Key 未配置，请检查模型配置或 .env 配置")

        chat_url = self._chat_url(base_url)
        host = httpx.URL(chat_url).host
        token_parameter = "max_tokens" if self.review_mode and host == "api.deepseek.com" else "max_completion_tokens"
        payload = {
            "model": model,
            "messages": [message.model_dump() for message in messages],
            token_parameter: self.settings.ai_max_completion_tokens,
            "stream": True,
        }
        if self.review_mode and model.casefold().startswith(("deepseek-v4", "deepseek-flash")):
            payload["thinking"] = {"type": "disabled"}
        headers = {"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"}
        started = perf_counter()
        headers_ms = reasoning_ms = content_ms = None
        reasoning_chars = content_chars = status = 0
        finish = "eof"

        def elapsed_ms() -> int:
            return round((perf_counter() - started) * 1000)

        try:
            async with httpx.AsyncClient(
                timeout=self.settings.ai_timeout_seconds,
                transport=self._make_transport(),
            ) as client:
                while True:
                    async with client.stream("POST", chat_url, json=payload, headers=headers) as response:
                        status = response.status_code
                        headers_ms = elapsed_ms()
                        if ("thinking" in payload and response.status_code in {400, 422}
                                and await _rejects_thinking(response)):
                            logger.info("Resume model compatibility retry request_id=%s host=%s model=%r",
                                        request_id, host, model)
                            payload.pop("thinking")
                            continue
                        response.raise_for_status()
                        async for line in response.aiter_lines():
                            if not line.startswith("data:"):
                                continue
                            payload_text = line[5:].strip()
                            if payload_text == "[DONE]":
                                if finish == "eof":
                                    finish = "done"
                                break
                            if not payload_text:
                                continue
                            try:
                                body = json.loads(payload_text)
                                if body.get("error"):
                                    finish = "upstream_error"
                                    raise AIInterviewError("AI 服务中断了回复，请稍后重试。")
                                choices = body.get("choices")
                                if not choices:
                                    continue
                                choice = choices[0]
                                delta = choice.get("delta") or {}
                                reasoning = delta.get("reasoning_content") or delta.get("reasoning")
                                content = delta.get("content")
                                reason = choice.get("finish_reason")
                            except (AttributeError, IndexError, KeyError, TypeError, json.JSONDecodeError) as exc:
                                finish = "invalid_stream"
                                raise AIInterviewError("AI 服务返回的数据格式无效") from exc
                            if self.review_mode and isinstance(reasoning, str) and reasoning:
                                reasoning_chars += len(reasoning)
                                if reasoning_ms is None:
                                    reasoning_ms = elapsed_ms()
                                    if on_stage and content_ms is None:
                                        await on_stage("reasoning")
                            if isinstance(content, str) and content:
                                content_chars += len(content)
                                if content_ms is None and content.strip():
                                    content_ms = elapsed_ms()
                                    if on_stage:
                                        await on_stage("answer")
                                yield content
                            if reason:
                                finish = reason if reason in ("stop", "length", "content_filter", "tool_calls",
                                                              "aborted", "insufficient_system_resource") else "other"
                            if self.review_mode and reason == "length":
                                message = ("模型达到输出上限，已返回的建议可能不完整。" if content_ms is not None else
                                           "模型达到输出上限，尚未生成建议正文。")
                                raise AIOutputTruncatedError(message)
                        break
        except httpx.HTTPStatusError as exc:
            finish = "http_error"
            if self.review_mode and exc.response.status_code in {401, 403}:
                raise AIInterviewError("模型服务拒绝访问，请检查 API Key 和模型权限。") from exc
            if self.review_mode and exc.response.status_code == 429:
                raise AIInterviewError("该接口当前请求过多或额度不足，请稍后重试。") from exc
            raise AIInterviewError("AI 服务返回错误，请稍后重试") from exc
        except httpx.TimeoutException as exc:
            finish = "timeout"
            if self.review_mode:
                raise AIInterviewError("等待模型服务返回数据超时。") from exc
            raise AIInterviewError("无法连接 AI 服务，请检查网络和配置") from exc
        except httpx.HTTPError as exc:
            finish = "network_error"
            raise AIInterviewError("无法连接 AI 服务，请检查网络和配置") from exc
        except asyncio.CancelledError:
            finish = "cancelled"
            raise
        finally:
            if self.review_mode:
                logger.info(
                    "Resume model upstream request_id=%s model_id=%s host=%s model=%r status=%s headers_ms=%s "
                    "first_reasoning_ms=%s first_content_ms=%s total_ms=%s finish=%s "
                    "reasoning_chars=%s content_chars=%s token_budget=%s",
                    request_id, model_id, host, model, status, headers_ms, reasoning_ms, content_ms, elapsed_ms(), finish,
                    reasoning_chars, content_chars, self.settings.ai_max_completion_tokens,
                )

        if (self.review_mode and content_ms is None) or not content_chars:
            message = "模型只返回了推理进度，尚未生成建议正文。" if reasoning_chars else "AI 服务返回了空回复"
            raise AIInterviewError(message)
