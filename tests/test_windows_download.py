import hashlib
import json

from fastapi.testclient import TestClient

from tests.test_public_multiuser import _public_fixture


def _client(tmp_path):
    app, *_ = _public_fixture(tmp_path)
    app.state.settings.windows_release_dir = str(tmp_path)
    return TestClient(app, base_url="https://testserver")


def test_downloads_are_public_and_installers_support_ranges(tmp_path):
    filename = "AwesomeInterview-Windows-1.6.0-preview.31-x64-Setup.exe"
    payload = b"installer"
    (tmp_path / filename).write_bytes(payload)
    (tmp_path / "release.json").write_text(json.dumps({
        "version": "1.6.0-preview.31", "filename": filename,
        "sha256": hashlib.sha256(payload).hexdigest(), "notes": ["<script>untrusted</script>"],
    }), encoding="utf-8")
    with _client(tmp_path) as client:
        page = client.get("/download/windows")
        assert page.status_code == 200
        assert "1.6.0-preview.31" in page.text
        assert "下载安装程序" in page.text
        assert "&lt;script&gt;untrusted&lt;/script&gt;" in page.text
        assert 'href="/download/windows"' in client.get("/my-schedule/experiences/plaza").text

        head = client.head("/download/windows/installer")
        assert head.status_code == 200
        assert head.content == b""
        assert filename in head.headers["content-disposition"]
        response = client.get("/download/windows/installer", headers={"Range": "bytes=1-3"})
        assert response.status_code == 206
        assert response.content == payload[1:4]


def test_only_update_feed_and_packages_are_exposed(tmp_path):
    filenames = (
        "releases.win.json", "releases.win.json.sig",
        "AwesomeInterviewWindows-1.6.0-preview.31-full.nupkg",
        "AwesomeInterviewWindows-1.6.0-preview.32-delta.nupkg",
    )
    for filename in (*filenames, ".env", "release.json", "releases.linux.json"):
        (tmp_path / filename).write_bytes(b"fixture")
    with _client(tmp_path) as client:
        assert "还没有提供" in client.get("/download/windows").text
        assert client.head("/download/windows/installer").status_code == 404
        for filename in filenames:
            assert client.get(f"/download/windows/updates/{filename}").content == b"fixture"
        for filename in (".env", "release.json", "releases.linux.json", "missing.nupkg"):
            assert client.get(f"/download/windows/updates/{filename}").status_code == 404
