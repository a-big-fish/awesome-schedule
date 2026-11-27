"""Public Windows installer and update endpoints."""

from pathlib import Path

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import FileResponse, HTMLResponse

from app.controller.page_controller import templates
from app.service.windows_release_service import WindowsRelease, current_windows_release, update_file


router = APIRouter(prefix="/download/windows", tags=["downloads"])
_REVALIDATE = {"Cache-Control": "no-cache"}


def _current_release(request: Request) -> tuple[WindowsRelease, Path] | None:
    settings = request.app.state.settings
    return current_windows_release(
        Path(settings.windows_release_dir), allow_bundled_preview=settings.app_mode == "local",
    )


@router.get("", response_class=HTMLResponse)
def windows_download_page(request: Request) -> HTMLResponse:
    published = _current_release(request)
    release, path = published if published else (None, None)
    return templates.TemplateResponse(request, "windows_download.html", {
        "release": release,
        "size_mib": f"{path.stat().st_size / (1024 * 1024):.1f}" if path else "",
    }, headers=_REVALIDATE)


@router.api_route("/installer", methods=["GET", "HEAD"])
def download_windows_installer(request: Request) -> FileResponse:
    published = _current_release(request)
    if not published:
        raise HTTPException(404, "当前没有可下载的 Windows 安装包。")
    release, path = published
    return FileResponse(path, filename=release.filename, headers=_REVALIDATE)


@router.api_route("/updates/{filename}", methods=["GET", "HEAD"])
def windows_update_file(request: Request, filename: str) -> FileResponse:
    path = update_file(Path(request.app.state.settings.windows_release_dir), filename)
    if path is None:
        raise HTTPException(404, "Windows 更新文件不存在。")
    return FileResponse(path, headers=_REVALIDATE)
