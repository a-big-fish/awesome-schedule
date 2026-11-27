"""Discover and contact a user-configured OpenAI-compatible endpoint."""

import asyncio
import ipaddress
import json
import re
import socket
from urllib.parse import urlsplit

import httpx

from app.core.exceptions import AIInterviewError
from app.service.ai_providers.openai_compatible import OpenAICompatibleProvider

_INVALID_URL = "API URL 须为公开的 HTTPS 地址，请移除查询参数、片段和嵌入的凭据。"
_MAX_MODEL_LIST_BYTES = 1024 * 1024


def model_endpoint(base_url: str, resource: str) -> str:
    """Resolve a base API URL or a full endpoint to a sibling API resource."""
    try:
        parts = urlsplit(base_url)
        parsed = httpx.URL(base_url)
        if (parsed.scheme != "https" or parts.scheme != "https" or not parsed.host
                or "@" in parts.netloc or parts.query or parts.fragment
                or "?" in base_url or "#" in base_url
                or any(char.isspace() or ord(char) < 32 or char == "\\" for char in base_url)):
            raise ValueError()
        if parsed.host == "localhost" or parsed.host.endswith(".localhost"):
            raise ValueError()
        try:
            address = ipaddress.ip_address(parsed.host)
        except ValueError:
            address = None
        if address is not None and not _is_public(address):
            raise ValueError()
    except (httpx.InvalidURL, ValueError) as exc:
        raise AIInterviewError(_INVALID_URL) from exc
    path = parts.path.rstrip("/")
    for suffix in ("/chat/completions", "/models"):
        if path.endswith(suffix):
            path = path.removesuffix(suffix)
            break
    if not path and parsed.host != "api.deepseek.com":
        path = "/v1"
    elif re.fullmatch(r"/(?:api/)?openai", path):
        path += "/v1"
    return str(parsed.copy_with(path=f"{path}/{resource}"))


def _is_public(address: ipaddress.IPv4Address | ipaddress.IPv6Address) -> bool:
    if isinstance(address, ipaddress.IPv6Address) and address.ipv4_mapped:
        address = address.ipv4_mapped
    return address.is_global and not address.is_multicast and not address.is_reserved


class PublicModelTransport(httpx.AsyncHTTPTransport):
    """Connect to a vetted IP while retaining the API hostname for TLS and HTTP."""

    async def handle_async_request(self, request: httpx.Request) -> httpx.Response:
        hostname = request.url.raw_host.decode("ascii")
        port = request.url.port or 443
        try:
            records = await asyncio.wait_for(
                asyncio.get_running_loop().getaddrinfo(hostname, port, type=socket.SOCK_STREAM),
                timeout=5,
            )
        except (OSError, asyncio.TimeoutError) as exc:
            raise httpx.ConnectError("Could not resolve the model API hostname", request=request) from exc
        addresses = list(dict.fromkeys(record[4][0] for record in records))
        if not addresses or any(not _is_public(ipaddress.ip_address(ip)) for ip in addresses):
            raise AIInterviewError("API URL 必须指向公开网络。")

        pinned = httpx.Request(
            request.method,
            request.url.copy_with(host=next((ip for ip in addresses if ":" not in ip), addresses[0])),
            headers=request.headers,
            stream=request.stream,
            extensions={**request.extensions, "sni_hostname": hostname},
        )
        return await super().handle_async_request(pinned)


class CustomResumeProvider(OpenAICompatibleProvider):
    """Run each custom API call with its own public-only transport."""

    def _make_transport(self) -> httpx.AsyncBaseTransport:
        return self.transport or PublicModelTransport()

    @staticmethod
    def _chat_url(base_url: str) -> str:
        return model_endpoint(base_url, "chat/completions")


async def discover_resume_models(
    base_url: str, api_key: str, transport: httpx.AsyncBaseTransport | None = None,
) -> list[str]:
    """List accessible model IDs without sending any resume data."""
    endpoint = model_endpoint(base_url, "models")
    try:
        async with httpx.AsyncClient(timeout=12, transport=transport or PublicModelTransport(), trust_env=False) as client:
            async with client.stream("GET", endpoint, headers={"Authorization": f"Bearer {api_key}"}) as response:
                response.raise_for_status()
                body = bytearray()
                async for chunk in response.aiter_bytes():
                    if len(body) + len(chunk) > _MAX_MODEL_LIST_BYTES:
                        raise AIInterviewError("模型列表过大，请使用范围更小的 API 地址。")
                    body.extend(chunk)
        data = json.loads(body)["data"]
        if not isinstance(data, list):
            raise ValueError()
        models = {
            item["id"].strip() for item in data
            if isinstance(item, dict) and isinstance(item.get("id"), str) and 0 < len(item["id"].strip()) <= 200
        }
        return sorted(models, key=str.casefold)
    except httpx.HTTPStatusError as exc:
        if exc.response.status_code in {401, 403}:
            raise AIInterviewError("模型服务拒绝访问，请检查 API Key 和模型权限。") from exc
        raise AIInterviewError(f"无法获取模型列表（HTTP {exc.response.status_code}），请检查 API URL。") from exc
    except httpx.HTTPError as exc:
        raise AIInterviewError("无法连接模型服务，请检查网络和 API URL。") from exc
    except (KeyError, TypeError, ValueError) as exc:
        raise AIInterviewError("模型服务未返回有效的 OpenAI 兼容模型列表。") from exc
