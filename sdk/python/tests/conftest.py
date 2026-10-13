"""Shared test helpers: the spec vectors and a scripted fake transport."""

from __future__ import annotations

import json
import re
from collections.abc import Iterable
from pathlib import Path
from typing import Any

from supertext.v1 import HttpRequest, HttpResponse, SupertextClient

SPEC = Path(__file__).resolve().parents[3] / "spec"


def spec(name: str) -> Any:
    return json.loads((SPEC / name).read_text(encoding="utf-8"))


Answer = HttpResponse | Exception


class FakeTransport:
    """Answers requests from a script and records them.

    Each answer is an HttpResponse or an exception to raise. DELETEs don't use the script: they
    get ``delete_answer`` (default 200), so tests only script what matters.
    """

    def __init__(self, answers: Iterable[Answer] = (), delete_answer: Answer | None = None) -> None:
        self.answers = list(answers)
        self.delete_answer = delete_answer
        self.requests: list[HttpRequest] = []

    def __call__(self, request: HttpRequest) -> HttpResponse:
        self.requests.append(request)
        if request.method == "DELETE":
            answer: Answer = self.delete_answer or HttpResponse(200, b"{}")
        elif self.answers:
            answer = self.answers.pop(0)
        else:
            raise AssertionError(f"unexpected request {request.method} {request.url}")
        if isinstance(answer, Exception):
            raise answer
        return answer

    @property
    def calls(self) -> list[str]:
        return [f"{r.method} {r.url.split('/v1/', 1)[-1]}" for r in self.requests]


def json_response(data: object, status: int = 200, headers: dict[str, str] | None = None) -> HttpResponse:
    return HttpResponse(status, json.dumps(data).encode(), headers or {})


def text_response(text: str, status: int = 200) -> HttpResponse:
    return HttpResponse(status, text.encode("utf-8"))


class FakeClock:
    """Monotonic clock advanced only by the fake sleep."""

    def __init__(self) -> None:
        self.now = 1000.0
        self.sleeps: list[float] = []

    def __call__(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.sleeps.append(seconds)
        self.now += seconds


def make_client(
    transport: FakeTransport, api_key: str = "secret-key-123", **options: Any
) -> tuple[SupertextClient, FakeClock]:
    clock = FakeClock()
    client = SupertextClient(
        api_key, base_url="https://api.test/v1", transport=transport, sleep=clock.sleep, clock=clock, **options
    )
    return client, clock


def multipart_parts(request: HttpRequest) -> list[tuple[str, dict[str, str], str]]:
    """(name, part headers, value) for each part of a multipart body, in order."""
    content_type = request.headers["Content-Type"]
    match = re.fullmatch(r"multipart/form-data; boundary=(\S+)", content_type)
    assert match, content_type
    boundary = match.group(1)
    assert request.body is not None
    body = request.body.decode("utf-8")
    assert body.endswith(f"--{boundary}--\r\n")
    parts = []
    for raw in body.split(f"--{boundary}")[1:-1]:
        assert raw.startswith("\r\n") and raw.endswith("\r\n")
        head, value = raw[2:-2].split("\r\n\r\n", 1)
        headers = dict(line.split(": ", 1) for line in head.split("\r\n"))
        name = re.search(r'name="([^"]+)"', headers["Content-Disposition"])
        assert name
        parts.append((name.group(1), headers, value))
    return parts
