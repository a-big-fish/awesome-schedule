"""Resolve the published installer and explicitly allowed Velopack files."""

import logging
import re
from pathlib import Path

from pydantic import BaseModel, Field, ValidationError


logger = logging.getLogger(__name__)
_REPOSITORY = Path(__file__).resolve().parents[2]
_PREVIEW_ZIPS = (
    ("1.6.0-W Preview 31", "AwesomeSchedule-NativeDemo-1.6.0-W-preview31-win-x64.zip"),
    ("1.6.0-W Preview 30", "AwesomeInterview-NativeDemo-1.6.0-W-preview30-win-x64.zip"),
)
_INSTALLER_NAME = r"^Awesome(?:Interview|Schedule)[A-Za-z0-9.-]*\.(?:zip|exe)$"
_UPDATE_NAME = re.compile(r"(?:releases\.win\.json(?:\.sig)?|AwesomeInterviewWindows-[A-Za-z0-9.-]+-(?:full|delta)\.nupkg)")


class WindowsRelease(BaseModel):
    """Public metadata for the current Windows installer."""

    version: str = Field(min_length=1, max_length=72)
    filename: str = Field(max_length=160, pattern=_INSTALLER_NAME)
    sha256: str = Field(pattern=r"^[0-9A-Fa-f]{64}$")
    notes: list[str] = Field(default_factory=list, max_length=12)


def _file_in(root: Path, filename: str) -> Path | None:
    try:
        directory = root.resolve()
        path = (directory / filename).resolve()
    except (OSError, RuntimeError):
        return None
    return path if path.parent == directory and path.is_file() else None


def current_windows_release(root: Path, *, allow_bundled_preview: bool) -> tuple[WindowsRelease, Path] | None:
    """Load a deployment release, or the existing Preview 30 ZIP in a checkout."""
    manifest = _file_in(root, "release.json")
    if manifest is not None:
        try:
            with manifest.open("rb") as source:
                metadata = WindowsRelease.model_validate_json(source.read(64 * 1024 + 1))
            if path := _file_in(root, metadata.filename):
                return metadata, path
            logger.warning("Windows download manifest references a missing installer")
        except (OSError, ValidationError):
            logger.warning("Windows download manifest could not be read")
    if not allow_bundled_preview:
        return None

    artifacts = _REPOSITORY / "windows" / "artifacts"
    for version, filename in _PREVIEW_ZIPS:
        package = _file_in(artifacts, filename)
        checksum = _file_in(artifacts, filename + ".sha256")
        if package and checksum:
            try:
                return WindowsRelease(
                    version=version, filename=package.name,
                    sha256=checksum.read_text(encoding="ascii").split()[0],
                ), package
            except (OSError, UnicodeError, IndexError, ValidationError):
                logger.warning("Windows preview checksum could not be read for %s", version)
    return None


def update_file(root: Path, filename: str) -> Path | None:
    """Return only feed, signature, and update package files from the release directory."""
    if _UPDATE_NAME.fullmatch(filename):
        return _file_in(root, filename)
    return None
