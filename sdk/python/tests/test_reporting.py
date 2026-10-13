"""spec/reporting.json and the Commando Center's ping cases (spec/commando/callback-vectors.json)."""

from __future__ import annotations

import json
import platform
import re
from pathlib import Path
from typing import Any

import pytest
from conftest import spec

import supertext
from supertext import (
    DEFAULT_REPORT_URL,
    FAILURE_BACKOFF,
    KEY_CHECK_TIMEOUT,
    REPORT_TIMEOUT,
    SDK_VERSION,
    ApiKeyCheck,
    Client,
    HttpRequest,
    HttpResponse,
    InstallationReporter,
    NameVersion,
    ReporterResult,
    ReporterState,
    api_key_fingerprint,
    ping_response,
    reporter_state,
    reports_enabled,
)
from supertext.api_key_check import parse_iso

S = spec("reporting.json")
CALLBACK = spec("commando/callback-vectors.json")
REPORT_URL = "https://center.test/api/v1/report"


class FakeHttp:
    """Answers GET features and POST report from a step's answers; records which were called."""

    def __init__(self, features: dict[str, Any] | None, report: dict[str, Any] | None) -> None:
        self.answers = {"features": features, "report": report}
        self.requests: list[str] = []
        self.bodies: list[Any] = []
        self.sent: list[HttpRequest] = []

    def __call__(self, request: HttpRequest) -> HttpResponse:
        kind = "features" if request.url.endswith("/features") else "report"
        self.requests.append(kind)
        self.sent.append(request)
        if kind == "report":
            assert request.url == REPORT_URL
            assert request.method == "POST"
            assert request.body is not None
            self.bodies.append(json.loads(request.body))
        answer = self.answers[kind]
        if not answer:
            raise AssertionError(f"unexpected {kind} request")
        if answer.get("transportError"):
            raise OSError("connection refused")
        return HttpResponse(answer.get("status", 200), str(answer.get("body", "")).encode())


def epoch(iso: str) -> float:
    seconds = parse_iso(iso)
    assert seconds is not None
    return seconds


def reporter(config: dict[str, Any], http: FakeHttp, now: float) -> InstallationReporter:
    runtime = config.get("runtime")
    return InstallationReporter(
        enabled=config["enabled"],
        site_url=config["siteUrl"],
        plugin=NameVersion(**config["plugin"]),
        system=NameVersion(**config["system"]),
        runtime=NameVersion(**runtime) if runtime else None,
        sdk_version=config.get("sdkVersion"),
        callback_url=config.get("callbackUrl"),
        report_url=REPORT_URL,
        transport=http,
        client_options={"base_url": "https://api.test/v1/", "sleep": lambda _: None},
        now=lambda: now,
    )


# -- Defaults and version ---------------------------------------------------------------------


def test_defaults() -> None:
    assert S["defaults"]["reportUrl"] == DEFAULT_REPORT_URL
    assert S["defaults"]["reportTimeoutSeconds"] == REPORT_TIMEOUT
    assert S["defaults"]["keyCheckTimeoutSeconds"] == KEY_CHECK_TIMEOUT
    assert S["defaults"]["failureBackoffSeconds"] == FAILURE_BACKOFF


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
    http = FakeHttp(case["answer"], None)
    timeouts: list[float] = []

    def transport(request: HttpRequest) -> HttpResponse:
        timeouts.append(request.timeout)
        return http(request)

    client = Client(
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

    Client("k1", transport=transport, request_timeout=4.0).check_api_key()
    assert seen == [4.0]


def test_key_check_never_raises_on_unexpected_errors() -> None:
    def transport(request: HttpRequest) -> HttpResponse:
        raise RuntimeError("boom")

    assert Client("k1", transport=transport).check_api_key().status == "error"


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


# -- Enabled and ping -------------------------------------------------------------------------


@pytest.mark.parametrize("case", S["enabled"], ids=lambda c: f"{c['setting']!r} {c['env']!r}")
def test_enabled(case: dict[str, Any]) -> None:
    assert reports_enabled(case["setting"], case["env"]) is case["out"]


def test_enabled_reads_the_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SUPERTEXT_VERSION_REPORTS", "off")
    assert reports_enabled(True) is False
    monkeypatch.delenv("SUPERTEXT_VERSION_REPORTS")
    assert reports_enabled() is True


@pytest.mark.parametrize("case", S["ping"]["cases"], ids=lambda c: f"{c['challenge']!r} enabled={c['enabled']}")
def test_ping(case: dict[str, Any]) -> None:
    response = ping_response(case["challenge"], S["ping"]["plugin"], case["enabled"])
    assert response.status == case["status"]
    assert json.loads(response.body) == case["body"]
    assert dict(response.headers) == {"Content-Type": "application/json", "Cache-Control": "no-store"}


@pytest.mark.parametrize("case", CALLBACK["endpoint"]["cases"], ids=lambda c: repr(c["challenge"]))
def test_ping_endpoint_commando_vectors(case: dict[str, Any]) -> None:
    response = ping_response(case["challenge"], "Some-Supertext-Translation", True)
    assert response.status == case["status"]
    expected = dict(case["body"])
    if "plugin" in expected:
        expected["plugin"] = "Some-Supertext-Translation"
    assert json.loads(response.body) == expected


# -- The reporter's state machine -------------------------------------------------------------


def run(r: InstallationReporter, step: dict[str, Any], state: ReporterState) -> ReporterResult:
    action = step["action"]
    if action == "afterSettingsSaved":
        return r.after_settings_saved(state, step["apiKey"])
    if action == "onRequest":
        return r.on_request(state, step["apiKey"])
    if action == "authenticationFailed":
        return r.authentication_failed(state, step["apiKey"], step["httpStatus"])
    if action == "testConnection":
        return r.test_connection(state, step["apiKey"])
    assert action == "reportCheck"
    return r.report_check(state)


@pytest.mark.parametrize("scenario", S["scenarios"], ids=lambda s: s["name"])
def test_scenario(scenario: dict[str, Any]) -> None:
    config = {**S["config"], **scenario.get("config", {})}
    state = reporter_state(json.loads(json.dumps(scenario["state"])))
    for step in scenario["steps"]:
        http = FakeHttp(step.get("features"), step.get("report"))
        result = run(reporter(config, http, epoch(step["now"])), step, state)
        expect = step["expect"]
        assert http.requests == expect["requests"], step["action"]
        assert result.event == expect["event"]
        assert result.sent is expect["sent"]
        assert (result.check.to_dict() if result.check else None) == expect["check"]
        assert result.state.to_dict() == expect["state"]
        if "reportBody" in expect:
            assert http.bodies[0] == expect["reportBody"]
        # The stored form survives a JSON round trip.
        state = reporter_state(result.state.to_json())
        assert state.to_dict() == expect["state"]
        assert json.loads(result.state.to_json()) == expect["state"]


def test_report_fields_match_the_center_schema() -> None:
    schema = spec("commando/report-v1.schema.json")
    r = reporter(S["config"], FakeHttp(None, None), 0)
    body = r.build_report("settings_saved", None)
    for name in schema["required"]:
        assert name in body
    for name in body:
        assert name in schema["properties"]


# -- Reporter details -------------------------------------------------------------------------


def test_never_sends_the_key() -> None:
    http = FakeHttp({"status": 200, "body": "{}"}, {"status": 202})
    r = reporter(S["config"], http, epoch("2026-10-13T08:00:00Z"))
    result = r.after_settings_saved(None, "secret-key-123")
    assert result.sent
    assert "secret-key-123" not in json.dumps(http.bodies)
    assert "secret-key-123" not in repr(r) + repr(result) + result.state.to_json()


def test_report_request() -> None:
    http = FakeHttp(None, {"status": 202})
    r = reporter(S["config"], http, epoch("2026-10-13T08:00:00Z"))
    r.report_check(None)
    request = http.sent[0]
    assert request.timeout == REPORT_TIMEOUT
    assert request.headers["Content-Type"] == "application/json"
    assert request.headers["User-Agent"] == "supertext-sdk-python Directus-Supertext-Translation/0.2.0"


def test_never_raises_on_unexpected_transport_errors() -> None:
    def transport(request: HttpRequest) -> HttpResponse:
        raise RuntimeError("boom")

    r = InstallationReporter(
        enabled=True,
        site_url="https://cms.example.ch",
        plugin=NameVersion("X-Supertext-Translation", "1.0.0"),
        system=NameVersion("X"),
        report_url=REPORT_URL,
        transport=transport,
        now=lambda: epoch("2026-10-13T08:00:00Z"),
    )
    result = r.after_settings_saved(None, "k1")
    assert result.check is not None and result.check.status == "error"
    assert result.event == "installed" and not result.sent
    assert result.state.last_failure_at == "2026-10-13T08:00:00Z"


def test_report_url_from_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    plugin, system = NameVersion("X", "1"), NameVersion("Y")
    monkeypatch.setenv("SUPERTEXT_REPORT_URL", "https://other.test/report")
    assert InstallationReporter(enabled=True, site_url="s", plugin=plugin, system=system).report_url == (
        "https://other.test/report"
    )
    monkeypatch.delenv("SUPERTEXT_REPORT_URL")
    assert InstallationReporter(enabled=True, site_url="s", plugin=plugin, system=system).report_url == (
        DEFAULT_REPORT_URL
    )


def test_broken_stored_state_is_empty() -> None:
    empty = ReporterState()
    assert reporter_state("not json") == empty
    assert reporter_state(None) == empty
    assert reporter_state([1, 2]) == empty
    assert reporter_state({"reportedVersions": 3, "keyCheck": {"status": "ok"}}) == empty
    assert reporter_state(empty.to_dict()) == empty


def test_detects_the_runtime() -> None:
    config = {**S["config"], "runtime": None}
    body = reporter(config, FakeHttp(None, None), 0).build_report("check", None)
    assert body["runtime"] == {"name": "Python", "version": platform.python_version()}
    assert body["sdk_version"] == "0.2.0"
