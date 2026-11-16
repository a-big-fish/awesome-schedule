from pathlib import Path

import logging
from contextlib import asynccontextmanager
from time import perf_counter
from uuid import uuid4
from psycopg import OperationalError

from fastapi import Depends, FastAPI, Request
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import FileResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles

from app.controller import (
    admin_dashboard_controller,
    performance_controller,
    account_profile_controller,
    ai_interview_controller,
    auth_controller,
    page_controller,
    personal_resume_controller,
    interview_experience_controller,
    mobile_api_controller,
    resume_controller,
    schedule_controller,
    personal_schedule_controller,
    public_page_controller,
)
from app.core.auth_guard import AuthRequired, InvalidCsrf, require_current_user
from app.core.config import Settings, get_settings
from app.core.dependencies import (
    get_interview_experience_mapper,
    get_my_resume_mapper,
    get_personal_event_mapper,
    get_personal_idea_mapper,
)
from app.core.performance import RequestTimings, node_for_path, request_timings, channel_for_request
from app.core.postgres_pool import SharedScope, close_pools, shared_connection
from app.core.logging_setup import ApplicationLogs
from app.mapper.postgres_auth_mapper import PostgresAuthMapper
from app.service.auth_service import AuthService
from app.service.performance_service import PerformanceService
from app.service.reference_title_service import ReferenceTitleService

BASE_DIR = Path(__file__).resolve().parent
logger = logging.getLogger(__name__)
PERSISTENT_STORAGE = {"postgres", "rustfs", "minio"}


def _warm_metadata_schema() -> None:
    """Create missing metadata tables before the first visitor, not inside their request."""
    for factory in (get_personal_event_mapper, get_personal_idea_mapper,
                    get_my_resume_mapper, get_interview_experience_mapper):
        factory()


def create_app(settings: Settings | None = None, auth_service: AuthService | None = None) -> FastAPI:
    deployment = settings is None
    settings = settings or get_settings()
    public = settings.app_mode == "public_personal"
    if public:
        if settings.storage_backend == "memory":
            raise RuntimeError("公开模式必须使用持久化存储")
        if not settings.public_base_url.startswith("https://"):
            raise RuntimeError("公开模式必须配置 HTTPS PUBLIC_BASE_URL")
        if (settings.registration_mode != "closed" and settings.email_verification_required
                and (not settings.smtp_host or not settings.smtp_from)):
            raise RuntimeError("开放注册前必须配置 SMTP_HOST 和 SMTP_FROM")
        if settings.registration_mode == "invite" and not settings.registration_invite_code:
            raise RuntimeError("邀请码注册必须配置 REGISTRATION_INVITE_CODE")
    performance = PerformanceService(Path(settings.performance_db_path),
                                     enabled=public and settings.performance_enabled)
    reference_titles = ReferenceTitleService()
    application_logs = ApplicationLogs(settings.log_dir, settings.log_max_bytes, settings.log_backup_count)

    @asynccontextmanager
    async def lifespan(_application):
        application_logs.start()
        # Tests inject their own mappers, so only the deployment path pre-creates the schema.
        if deployment and settings.storage_backend in PERSISTENT_STORAGE:
            try:
                await run_in_threadpool(_warm_metadata_schema)
            except Exception as exc:  # noqa: BLE001 - a temporarily unreachable database must not stop startup
                logger.warning("Metadata schema warm-up failed: %s", type(exc).__name__)
        performance.start()
        try:
            yield
        finally:
            performance.stop()
            reference_titles.close()
            close_pools()
            application_logs.stop()

    application = FastAPI(
        title="Awesome Interview" if public else "会议日程规划器",
        version="0.1.0",
        docs_url=None if public else "/docs",
        redoc_url=None if public else "/redoc",
        openapi_url=None if public else "/openapi.json",
        lifespan=lifespan,
    )
    application.state.settings = settings
    application.state.performance = performance
    application.state.reference_titles = reference_titles
    @application.exception_handler(OperationalError)
    async def handle_database_unavailable(request: Request, exc: OperationalError):
        logger.error("Database temporarily unavailable request_id=%s node=%s error=%s",
                       getattr(request.state, "request_id", "unknown"),
                       node_for_path(request.url.path, request.method), type(exc).__name__)
        return JSONResponse({"detail": "数据库连接暂时不可用，请稍后重试"}, status_code=503,
                            headers={"Retry-After": "30", "Cache-Control": "no-store"})

    application.mount("/static", StaticFiles(directory=BASE_DIR / "static"), name="static")

    @application.get("/robots.txt", include_in_schema=False)
    def robots_txt():
        """Expose crawler rules without requiring an authenticated session."""
        return FileResponse(BASE_DIR / "static" / "robots.txt", media_type="text/plain")

    @application.get("/favicon.ico", include_in_schema=False)
    def favicon():
        """Answer the browser's implicit favicon request instead of logging a 404."""
        return FileResponse(BASE_DIR / "static" / "img" / "logo2.png", media_type="image/png")

    @application.middleware("http")
    async def measure_personal_requests(request: Request, call_next):
        node = node_for_path(request.url.path, request.method)
        if node is None:
            return await call_next(request)
        started = perf_counter()
        timings = RequestTimings()
        token = request_timings.set(timings)
        status = 500
        try:
            response = await call_next(request)
            status = response.status_code
            elapsed_ms = (perf_counter() - started) * 1000
            legacy_metric = "experience" if request.url.path.startswith("/my-schedule/experiences") else "personal"
            response.headers["Server-Timing"] = ", ".join(
                [f"app;dur={elapsed_ms:.1f}", f"{legacy_metric};dur={elapsed_ms:.1f}"] +
                [f"{name};dur={duration:.1f}" for name, duration in timings.values.items()])
            return response
        finally:
            elapsed_ms = (perf_counter() - started) * 1000
            performance.record(node, "server", "request", elapsed_ms, status, channel=channel_for_request(request))
            for name, duration in timings.values.items():
                performance.record(node, "server", name, duration, status, channel=channel_for_request(request))
            request_timings.reset(token)
            if elapsed_ms >= 500:
                logger.warning("Slow request node=%s method=%s status=%s duration_ms=%.1f",
                               node, request.method, status, elapsed_ms)
    @application.middleware("http")
    async def log_http_requests(request: Request, call_next):
        request_id = uuid4().hex
        request.state.request_id = request_id
        started = perf_counter()
        status = 500
        failure = None
        try:
            response = await call_next(request)
            status = response.status_code
            response.headers["X-Request-ID"] = request_id
            return response
        except Exception as exc:
            failure = exc
            raise
        finally:
            route = request.scope.get("route")
            path = getattr(route, "path", "unmatched")
            logger.log(logging.ERROR if status >= 500 else logging.INFO,
                       "HTTP request_id=%s method=%s route=%s status=%s duration_ms=%.1f error=%s",
                       request_id, request.method, path, status,
                       (perf_counter() - started) * 1000,
                       type(failure).__name__ if failure else "none",
                       exc_info=(type(failure), failure, failure.__traceback__) if failure else None,
                       extra={"request_status": status})

    if public:
        application.state.auth_service = auth_service or AuthService(PostgresAuthMapper(settings), settings)

        @application.middleware("http")
        async def prevent_private_cache(request: Request, call_next):
            response = await call_next(request)
            if not request.url.path.startswith("/static/"):
                response.headers["Cache-Control"] = "no-store"
                response.headers["Referrer-Policy"] = "no-referrer"
            return response

        @application.exception_handler(AuthRequired)
        async def handle_auth_required(request: Request, exc: AuthRequired):
            if request.method == "GET" and request.url.path.startswith("/my-schedule"):
                return RedirectResponse(f"/auth/login?next={request.url.path}", status_code=303)
            return JSONResponse({"detail": "请先登录"}, status_code=401)

        @application.exception_handler(InvalidCsrf)
        async def handle_invalid_csrf(request: Request, exc: InvalidCsrf):
            return JSONResponse({"detail": "表单已过期，请刷新重试"}, status_code=403)

        application.include_router(public_page_controller.router)
        application.include_router(auth_controller.router)
        application.include_router(personal_schedule_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(personal_resume_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(interview_experience_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(account_profile_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(mobile_api_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(admin_dashboard_controller.router,
                                   dependencies=[Depends(require_current_user)])
        application.include_router(performance_controller.router,
                                   dependencies=[Depends(require_current_user)])
    else:
        application.include_router(page_controller.router)
        application.include_router(schedule_controller.router)
        application.include_router(resume_controller.router)
        application.include_router(ai_interview_controller.router)
        application.include_router(personal_schedule_controller.router)
        application.include_router(interview_experience_controller.router)

    @application.middleware("http")
    async def reuse_one_connection_per_read(request: Request, call_next):
        """Read pages borrow one pooled connection for the whole request instead of one per query."""
        if request.method not in {"GET", "HEAD"}:
            return await call_next(request)
        scope = SharedScope()
        token = shared_connection.set(scope)
        try:
            return await call_next(request)
        finally:
            shared_connection.reset(token)
            scope.close()
    return application


app = create_app()


def main() -> None:
    import uvicorn

    uvicorn.run("app.main:app", host="127.0.0.1", port=8291, reload=True)
