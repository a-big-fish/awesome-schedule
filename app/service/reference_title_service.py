"""Bounded, best-effort public-page title lookup; no business request waits for I/O."""

from collections import OrderedDict
from html.parser import HTMLParser
import http.client
import ipaddress
from queue import Queue, Empty, Full
import socket
import ssl
from threading import Event, Lock, Thread
from time import monotonic
from urllib.parse import urljoin, urlsplit


class _TitleParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.inside = False
        self.parts = []

    def handle_starttag(self, tag, attrs):
        if tag == "title":
            self.inside = True

    def handle_endtag(self, tag):
        if tag == "title":
            self.inside = False

    def handle_data(self, data):
        if self.inside:
            self.parts.append(data)


def fetch_page_title(url: str) -> str:
    """Validate each hop and connect to the validated IP to prevent DNS rebinding."""
    for _ in range(4):
        deadline = monotonic() + 5
        parsed = urlsplit(url)
        port = parsed.port or (443 if parsed.scheme == "https" else 80)
        if parsed.scheme not in {"http", "https"} or not parsed.hostname or parsed.username or parsed.password or port not in {80, 443}:
            raise ValueError("Unsupported URL")
        addresses = list(dict.fromkeys(item[4][0] for item in socket.getaddrinfo(parsed.hostname, port, type=socket.SOCK_STREAM)))
        if not addresses or any(not ipaddress.ip_address(ip).is_global or ipaddress.ip_address(ip).is_multicast for ip in addresses):
            raise ValueError("Only public addresses are permitted")
        connection = http.client.HTTPConnection(parsed.hostname, port, timeout=3)
        response = None
        try:
            connection.sock = socket.create_connection((addresses[0], port), timeout=3)
            if parsed.scheme == "https":
                connection.sock = ssl.create_default_context().wrap_socket(connection.sock, server_hostname=parsed.hostname)
            active_socket = connection.sock
            path = parsed.path or "/"
            if parsed.query:
                path += "?" + parsed.query
            connection.request("GET", path, headers={"User-Agent": "AwesomeInterview-LinkTitle/1.0",
                               "Accept": "text/html", "Accept-Encoding": "identity"})
            response = connection.getresponse()
            if response.status in {301, 302, 303, 307, 308}:
                target = response.getheader("Location")
                if not target:
                    return ""
                url = urljoin(url, target)
                continue
            if response.status != 200 or "text/html" not in response.getheader("Content-Type", "").lower():
                return ""
            chunks = []
            size = 0
            while size < 128 * 1024:
                remaining = deadline - monotonic()
                if remaining <= 0:
                    raise TimeoutError("Title response timeout")
                active_socket.settimeout(min(3, remaining))
                chunk = response.read1(min(16384, 128 * 1024 - size))
                if not chunk:
                    break
                chunks.append(chunk)
                size += len(chunk)
            body = b"".join(chunks)
            encoding = response.headers.get_content_charset() or "utf-8"
            try:
                text = body.decode(encoding, errors="replace")
            except LookupError:
                text = body.decode("utf-8", errors="replace")
            parser = _TitleParser()
            parser.feed(text)
            return " ".join("".join(parser.parts).split())[:200]
        finally:
            if response is not None:
                response.close()
            connection.close()
    return ""


class ReferenceTitleService:
    def __init__(self, fetcher=fetch_page_title):
        self.fetcher = fetcher
        self.queue = Queue(maxsize=16)
        self.cache = OrderedDict()
        self.pending = set()
        self.lock = Lock()
        self.stopped = Event()
        self.workers = []

    def enrich_event(self, schedule, event):
        """Queue missing-title persistence without waiting for network or storage."""
        if not event or not any(not link.title for link in event.reference_links):
            return
        def enrich():
            titles = {}
            for link in event.reference_links:
                if not link.title:
                    url = str(link.url)
                    with self.lock:
                        cached = self.cache.get(url)
                    try:
                        title = cached[0] if cached and cached[0] else self.fetcher(url)
                    except Exception:
                        continue
                    if title:
                        titles[url] = title
            current = schedule.get_event(event.id)
            if not current:
                return
            changed = False
            for link in current.reference_links:
                if not link.title and str(link.url) in titles:
                    link.title = titles[str(link.url)]
                    changed = True
            if changed:
                schedule.mapper.save(current)
        try:
            self.queue.put_nowait(enrich)
        except Full:
            return
        self._start_workers()

    def _start_workers(self):
        with self.lock:
            if not self.workers and not self.stopped.is_set():
                self.workers = [Thread(target=self._run, daemon=True, name="reference-title") for _ in range(2)]
                for worker in self.workers:
                    worker.start()

    def lookup(self, url: str) -> tuple[str, bool]:
        with self.lock:
            cached = self.cache.get(url)
            if cached and monotonic() - cached[1] < (3600 if cached[0] else 60):
                self.cache.move_to_end(url)
                return cached[0], False
            if url in self.pending:
                return "", True
            if self.stopped.is_set():
                return "", False
            try:
                self.queue.put_nowait(url)
            except Full:
                return "", False
            self.pending.add(url)
            if not self.workers:
                self.workers = [Thread(target=self._run, daemon=True, name="reference-title") for _ in range(2)]
                for worker in self.workers:
                    worker.start()
            return "", True

    def _run(self):
        while not self.stopped.is_set():
            try:
                url = self.queue.get(timeout=.2)
            except Empty:
                continue
            if callable(url):
                try:
                    url()
                except Exception:
                    pass  # Best effort: title lookup must never affect a saved event.
                finally:
                    self.queue.task_done()
                continue
            try:
                title = self.fetcher(url)
            except Exception:
                title = ""
            with self.lock:
                self.pending.discard(url)
                self.cache[url] = (title, monotonic())
                while len(self.cache) > 512:
                    self.cache.popitem(last=False)
            self.queue.task_done()

    def close(self):
        self.stopped.set()
        for worker in self.workers:
            worker.join(timeout=.2)
