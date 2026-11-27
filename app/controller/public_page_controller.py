"""Single-entry public portal; enterprise pages are not registered here."""

from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse, RedirectResponse

from app.controller.page_controller import templates
from app.core.auth_guard import SESSION_COOKIE


router = APIRouter(tags=["public-pages"])


@router.get("/", response_class=HTMLResponse)
def public_home(request: Request):
    # The calendar route checks the session and can serve cached data during an outage.
    destination = "/my-schedule" if request.cookies.get(SESSION_COOKIE) else "/my-schedule/experiences/plaza"
    return RedirectResponse(destination, status_code=303)


@router.get("/copyright", response_class=HTMLResponse)
def copyright_page(request: Request):
    """Show attribution and takedown information for collected experiences."""
    return templates.TemplateResponse(request, "copyright.html", {})
