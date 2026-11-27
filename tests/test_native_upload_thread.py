"""Large resume work must not execute on the async HTTP event-loop thread."""
import asyncio
import io
import threading
from types import SimpleNamespace

from fastapi import UploadFile

from app.service.resume_parser_service import ResumeParserService


def test_resume_upload_offloads_existing_parser_without_changing_owner(monkeypatch, tmp_path):
    parser = ResumeParserService(storage_dir=tmp_path)
    thread = threading.get_ident()
    calls = []

    def parse(content, file_name, **options):
        calls.append((threading.get_ident(), content, file_name, options["owner_id"]))
        return SimpleNamespace(id="generated-fixture")

    monkeypatch.setattr(parser, "parse_pdf", parse)
    result = asyncio.run(parser.parse_upload(UploadFile(filename="fixture.pdf", file=io.BytesIO(b"fixture")), owner_id="synthetic-owner"))
    assert result.id == "generated-fixture"
    assert calls[0][0] != thread
    assert calls[0][1:] == (b"fixture", "fixture.pdf", "synthetic-owner")
