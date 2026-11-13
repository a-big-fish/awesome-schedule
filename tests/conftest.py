"""Keep legacy route tests on the local app regardless of deployment .env."""

import os
from pathlib import Path


os.environ["APP_MODE"] = "local"
# Test traffic must not land in the deployment's performance database, or the admin
# dashboard stops describing real use. Automated samples get their own file instead.
os.environ.setdefault("PERFORMANCE_DB_PATH",
                      str(Path(__file__).resolve().parents[1] / ".performance-tests.sqlite3"))
