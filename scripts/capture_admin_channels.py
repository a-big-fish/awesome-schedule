"""Render the real admin routes in Chrome with isolated synthetic test data."""

import json
import tempfile
from pathlib import Path
from urllib.parse import urlsplit

from fastapi.testclient import TestClient
from playwright.sync_api import sync_playwright
from app.model.auth import AuthUser
from app.service.auth_service import hash_password
from tests.test_public_multiuser import _login, _public_fixture


def main():
    output = Path("app/acceptance/2026-10-29/admin-channels")
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temporary:
        app, users, *_ = _public_fixture(Path(temporary))
        users.create_user(AuthUser(username="preview-admin", password_hash=hash_password("preview-only-123"),
                                   email_verified=True, is_admin=True))
        with TestClient(app, base_url="https://testserver") as client, sync_playwright() as playwright:
            _login(client, "preview-admin", "preview-only-123")
            for channel, durations in {"web": [35, 70, 110], "windows": [45, 90], "android": [55, 125, 1400], "unknown": [95]}.items():
                for duration in durations:
                    app.state.performance.record("calendar", "server", "request", duration,
                                                 503 if duration > 1000 else 200, channel=channel)
            app.state.performance.queue.join()
            browser = playwright.chromium.launch(executable_path=r"C:\Program Files\Google\Chrome\Application\chrome.exe", headless=True)
            page = browser.new_page(viewport={"width": 1440, "height": 1100})

            def route(request_route):
                parsed = urlsplit(request_route.request.url)
                if parsed.hostname != "testserver":
                    request_route.abort()
                    return
                response = client.request(request_route.request.method,
                                          parsed.path + ("?" + parsed.query if parsed.query else ""),
                                          content=request_route.request.post_data_buffer)
                request_route.fulfill(status=response.status_code, body=response.content,
                                      headers={k: v for k, v in response.headers.items()
                                               if k.lower() not in {"content-encoding", "content-length", "set-cookie"}})

            page.route("**/*", route)
            page.goto("https://testserver/my-schedule/admin")
            page.wait_for_selector("[data-performance-channels] button")
            page.locator("[data-performance-dashboard]").evaluate("element => element.scrollIntoView({block:'start'})")
            page.screenshot(path=str(output / "01-all-channels.png"))
            with page.expect_response(lambda response: "channel=android" in response.url):
                page.locator("[data-performance-channel]").select_option("android")
            page.wait_for_function("document.querySelector('[data-performance-channels] [aria-pressed=true]')?.textContent.includes('Android')")
            assert "Android App" in page.locator("[data-performance-requests]").inner_text()
            assert "Windows 应用" not in page.locator("[data-performance-requests]").inner_text()
            page.screenshot(path=str(output / "02-android-filter.png"))
            browser.close()
    (output / "manifest.json").write_text(json.dumps({"date": "2026-10-29", "data": "isolated synthetic data", "renderer": "Chrome + actual FastAPI TestClient routes", "screens": ["01-all-channels.png", "02-android-filter.png"]}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
