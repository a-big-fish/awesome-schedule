"""Bounded asynchronous application and error log files."""

import copy
import logging
import queue
import traceback
from datetime import datetime
from logging.handlers import QueueHandler, QueueListener, RotatingFileHandler
from pathlib import Path
from zoneinfo import ZoneInfo

from uvicorn.logging import AccessFormatter


class TimestampedAccessFormatter(AccessFormatter):
    """Console access logs with tab separators and Beijing timestamps."""

    def __init__(self):
        super().__init__(
            '%(levelname)s:\t%(asctime)s\t%(client_addr)s - "%(request_line)s" %(status_code)s',
            datefmt="%m.%d:%H:%M", use_colors=False,
        )

    def formatTime(self, record, datefmt=None):
        return datetime.fromtimestamp(record.created, ZoneInfo("Asia/Shanghai")).strftime(
            datefmt or "%m.%d:%H:%M"
        )


class SafeFormatter(logging.Formatter):
    def formatException(self, exc_info):
        # Preserve source locations, but exclude exception values (SQL/credentials).
        return "\n".join(
            f'  File "{frame.filename}", line {frame.lineno}, in {frame.name}'
            for frame in traceback.extract_tb(exc_info[2])
        ) + "\n" + exc_info[0].__name__

    def format(self, record):
        record = copy.copy(record)
        if record.exc_info:
            record.exc_text = None
        if record.name == "uvicorn.access" and isinstance(record.args, tuple) and len(record.args) == 5:
            args = list(record.args)
            args[2] = str(args[2]).split("?", 1)[0]
            record.args = tuple(args)
        return super().format(record)


class _ErrorFilter(logging.Filter):
    def filter(self, record):
        if record.levelno >= logging.ERROR or getattr(record, "request_status", 0) >= 500:
            return True
        return (record.name == "uvicorn.access" and isinstance(record.args, tuple)
                and len(record.args) == 5 and str(record.args[4]).isdigit()
                and int(record.args[4]) >= 500)


class _BoundedQueueHandler(QueueHandler):
    def __init__(self, target):
        super().__init__(target)
        self.dropped = 0
        self.marker = f"_file_queue_{id(self)}"

    def handle(self, record):
        if not record.name.startswith(("app.", "uvicorn", "psycopg")):
            return False
        # A logger may reach both uvicorn and root. Enqueue each record only once.
        if getattr(record, self.marker, False):
            return False
        setattr(record, self.marker, True)
        return super().handle(record)

    def enqueue(self, record):
        try:
            self.queue.put_nowait(record)
        except queue.Full:
            self.dropped += 1

    def prepare(self, record):
        record = copy.copy(record)
        if record.name == "uvicorn.access" and isinstance(record.args, tuple) and len(record.args) == 5:
            try:
                record.request_status = int(record.args[4])
            except (ValueError, TypeError):
                pass
        return super().prepare(record)


class ApplicationLogs:
    def __init__(self, directory: str, max_bytes: int, backups: int):
        self.directory = Path(directory)
        self.max_bytes = max(1024, max_bytes)
        self.backups = max(1, backups)
        self.listener = None

    def start(self):
        self.directory.mkdir(parents=True, exist_ok=True)
        self.console_formatters = []
        for handler in logging.getLogger("uvicorn.access").handlers:
            if isinstance(handler, logging.StreamHandler) and not isinstance(handler, logging.FileHandler):
                self.console_formatters.append((handler, handler.formatter))
                handler.setFormatter(TimestampedAccessFormatter())
        formatter = SafeFormatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
        self.files = []
        for name in ("app.log", "error.log"):
            handler = RotatingFileHandler(self.directory / name, maxBytes=self.max_bytes,
                                          backupCount=self.backups, encoding="utf-8")
            handler.setFormatter(formatter)
            if name == "error.log":
                handler.addFilter(_ErrorFilter())
            self.files.append(handler)
        self.queue = queue.Queue(maxsize=10000)
        self.handler = _BoundedQueueHandler(self.queue)
        self.handler.setLevel(logging.INFO)
        self.handler.setFormatter(SafeFormatter("%(message)s"))
        self.loggers = [logging.getLogger(name) for name in
                        ("", "uvicorn", "uvicorn.access")]
        self.old_level = logging.getLogger().level
        logging.getLogger().setLevel(min(self.old_level, logging.INFO))
        for logger in self.loggers:
            logger.addHandler(self.handler)
        self.listener = QueueListener(self.queue, *self.files, respect_handler_level=True)
        self.listener.start()

    def stop(self):
        if self.listener is None:
            return
        for logger in self.loggers:
            logger.removeHandler(self.handler)
        for handler, formatter in self.console_formatters:
            handler.setFormatter(formatter)
        # Drain before the sentinel, including when the bounded queue was full.
        self.queue.join()
        self.listener.stop()
        logging.getLogger().setLevel(self.old_level)
        for handler in self.files:
            handler.close()
        self.handler.close()
        self.listener = None
