"""Keep legacy route tests on the local app regardless of deployment .env."""

import os
from pathlib import Path


os.environ["APP_MODE"] = "local"
# Test traffic must not land in the deployment's performance database, or the admin
# dashboard stops describing real use. Automated samples get their own file instead.
os.environ.setdefault("PERFORMANCE_DB_PATH",
                      str(Path(__file__).resolve().parents[1] / ".performance-tests.sqlite3"))

# The resume pipeline is the other shared backend. Without this, a test that exercises an
# upload route without injecting its own service writes a synthetic ~800 byte PDF into the
# deployment's RustFS bucket and a matching row into its PostgreSQL metadata under owner
# "local" (504 such rows had accumulated before this guard).
_TEST_RUNTIME = Path(__file__).resolve().parents[1] / "data" / "test-runtime"

import app.core.dependencies as dependencies  # noqa: E402
from app.mapper.local_resume_file_storage import LocalResumeFileStorage  # noqa: E402
from app.mapper.memory_resume_mapper import MemoryResumeMapper  # noqa: E402

_TEST_RESUME_STORAGE = LocalResumeFileStorage(_TEST_RUNTIME / "resumes")
_TEST_RESUME_MAPPER = MemoryResumeMapper()
# Patch the internal builders, not the cached factories: FastAPI matches dependency
# overrides by function identity, and replacing `get_resume_mapper` itself would stop the
# per-test overrides in _public_fixture from matching.
dependencies._build_resume_file_storage = lambda settings: _TEST_RESUME_STORAGE
dependencies._build_resume_mapper = lambda settings: _TEST_RESUME_MAPPER
for factory in (dependencies.get_resume_file_storage,
                dependencies.get_resume_mapper,
                dependencies.get_resume_parser_service,
                dependencies.get_ai_interview_service,
                dependencies.get_my_interview_service):
    factory.cache_clear()
