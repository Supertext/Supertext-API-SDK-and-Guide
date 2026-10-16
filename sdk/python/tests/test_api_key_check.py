"""spec/api-key-check.json: the API key check Supertext Plugin Health reports."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import pytest
from conftest import spec

import supertext
from supertext import SDK_VERSION
from supertext.v1 import (
    KEY_CHECK_TIMEOUT,
    ApiKeyCheck,
    HttpRequest,
    HttpResponse,
    SupertextClient,
    api_key_fingerprint,
)
from supertext.v1.api_key_check import parse_iso

S = spec("api-key-check.json")


class FakeHttp:
    """Answers GET features; records the requests."""

    def __init__(self, features: dict[str, Any] | None) -> None:
        self.features = features
        self.requests: list[str] = []

    def __call__(self, request: HttpRequest) -> HttpResponse:
        self.requests.append(request.url)
        answer = self.features
        if not answer:
            raise AssertionError(f"unexpected request {request.url}")
        if answer.get("transportError"):
            raise OSError("connection refused")
        return HttpResponse(answer.get("status", 200), str(answer.get("body", "")).encode())


def epoch(iso: str) -> float:
    seconds = parse_iso(iso)
    assert seconds is not None
    return seconds


# -- Defaults and version ---------------------------------------------------------------------


def test_defaults() -> None:
    assert S["defaults"]["keyCheckTimeoutSeconds"] == KEY_CHECK_TIMEOUT


def test_sdk_version_matches_pyproject() -> None:
    pyproject = (Path(__file__).resolve().parents[1] / "pyproject.toml").read_text(encoding="utf-8")
    match = re.search(r'^version = "([^"]+)"', pyproject, re.MULTILINE)
    assert match
    assert match.group(1) == SDK_VERSION == supertext.__version__


# -- Fingerprint and key check ----------------------------------------------------------------


@pytest.mark.parametrize("case", S["fingerprint"], ids=lambda c: repr(c["apiKey"]))
def test_fingerprint(case: dict[str, Any]) -> None:
    assert api_key_fingerprint(case["apiKey"]) == case["out"]


@pytest.mark.parametrize("case", S["apiKeyCheck"], ids=lambda c: json.dumps(c["answer"]))
def test_api_key_check(case: dict[str, Any]) -> None:
    http = FakeHttp(case["answer"])
    timeouts: list[float] = []

    def transport(request: HttpRequest) -> HttpResponse:
        timeouts.append(request.timeout)
        return http(request)

    client = SupertextClient(
        case["apiKey"],
        base_url="https://api.test/v1/",
        transport=transport,
        sleep=lambda _: None,
        now=lambda: epoch("2026-10-13T08:00:00Z") + 0.5,
    )
    check = client.check_api_key()
    assert check == ApiKeyCheck(
        case["status"],
        "2026-10-13T08:00:00Z",
        case["httpStatus"],
        case["errorCode"],
        api_key_fingerprint(case["apiKey"]),
    )
    assert len(http.requests) == case.get("requests", 1)
    assert all(t == KEY_CHECK_TIMEOUT for t in timeouts)


def test_key_check_uses_a_shorter_request_timeout() -> None:
    seen: list[float] = []

    def transport(request: HttpRequest) -> HttpResponse:
        seen.append(request.timeout)
        return HttpResponse(200, b"{}")

    SupertextClient("k1", transport=transport, request_timeout=4.0).check_api_key()
    assert seen == [4.0]


def test_key_check_never_raises_on_unexpected_errors() -> None:
    def transport(request: HttpRequest) -> HttpResponse:
        raise RuntimeError("boom")

    assert SupertextClient("k1", transport=transport).check_api_key().status == "error"


def test_api_key_check_dict_round_trip() -> None:
    check = ApiKeyCheck("invalid", "2026-10-13T08:00:00Z", 401, "INVALID_API_KEY", "ab")
    assert check.to_dict() == {
        "status": "invalid",
        "checkedAt": "2026-10-13T08:00:00Z",
        "httpStatus": 401,
        "errorCode": "INVALID_API_KEY",
        "fingerprint": "ab",
    }
    assert ApiKeyCheck.from_dict(json.loads(json.dumps(check.to_dict()))) == check
    with pytest.raises(ValueError):
        ApiKeyCheck.from_dict({"status": "ok"})
