"""Check the server projection against the portable synthetic calendar fixture."""

import json
from datetime import date, datetime
from pathlib import Path
from types import SimpleNamespace

from app.controller import personal_schedule_controller as controller
from app.model.personal_event import PersonalEvent
from app.service.personal_schedule_service import PersonalScheduleService


def test_server_calendar_projection_contract(monkeypatch):
    fixture = json.loads(
        (Path(__file__).parent / "fixtures" / "calendar_projection_v1.json").read_text(encoding="utf-8")
    )
    now = datetime.fromisoformat(fixture["now"])

    class FixedClock(datetime):
        @classmethod
        def now(cls, tz=None):
            return now.astimezone(tz)

    monkeypatch.setattr(controller, "datetime", FixedClock)
    service = SimpleNamespace(owner_id="fixture", repeat_occurrence=PersonalScheduleService.repeat_occurrence)
    events = [PersonalEvent.model_validate(item) for item in fixture["events"]]
    for day, expected in fixture["days"].items():
        result = controller._dashboard_context(
            service, date.fromisoformat(day), events=events,
            workday_start=fixture["workday_start"], workday_end=fixture["workday_end"],
        )
        assert [
            [item["id"], item["start_minutes"], item["start_minutes"] + item["duration_minutes"],
             bool(item["completed_at"])] for item in result["entries"]
        ] == expected
        assert [item["id"] for item in result["overdue"]] == fixture["overdue"]
