"""The shared test vectors in spec/*.json."""

from __future__ import annotations

from typing import Any

import pytest
from conftest import FakeTransport, json_response, make_client, spec, text_response

from supertext.v1 import (
    DEFAULT_MAX_CHARACTERS,
    ErrorCode,
    HtmlDocument,
    Languages,
    Segment,
    SupertextError,
    auth_header,
    languages,
    plan_batches,
    retry_delay_ms,
)
from supertext.v1.errors import error_for_response

LANGUAGES = spec("languages.json")
HTML = spec("html-document.json")
ERRORS = spec("errors.json")
BATCHING = spec("batching.json")


def test_language_lists() -> None:
    assert [{"code": lang.code, "name": lang.name} for lang in Languages.SOURCES] == LANGUAGES["sources"]
    assert [
        {"code": lang.code, "name": lang.name, "politeness": lang.politeness} for lang in Languages.TARGETS
    ] == LANGUAGES["targets"]
    assert languages.SOURCES is Languages.SOURCES
    assert isinstance(Languages.TARGETS, tuple)
    with pytest.raises(AttributeError):
        Languages.TARGETS[0].code = "xx"  # type: ignore[misc]


@pytest.mark.parametrize("case", LANGUAGES["matchTarget"], ids=lambda c: repr(c["in"]))
def test_match_target(case: dict[str, Any]) -> None:
    assert Languages.match_target(case["in"]) == case["out"]
    assert Languages.is_supported_target(case["in"]) is (case["out"] is not None)


@pytest.mark.parametrize("case", LANGUAGES["matchSource"], ids=lambda c: repr(c["in"]))
def test_match_source(case: dict[str, Any]) -> None:
    assert Languages.match_source(case["in"]) == case["out"]


def test_is_supported_source() -> None:
    assert Languages.is_supported_source("EN")
    assert Languages.is_supported_source("gsw")
    assert not Languages.is_supported_source("en-US")
    assert not Languages.is_supported_source("")


@pytest.mark.parametrize("case", LANGUAGES["resolveTarget"], ids=lambda c: f"{c['in']!r} {c.get('mapping')}")
def test_resolve_target(case: dict[str, Any]) -> None:
    if "error" in case:
        with pytest.raises(SupertextError) as raised:
            Languages.target(case["in"], case.get("mapping"))
        assert raised.value.code == case["error"]
        assert raised.value.target_lang == (case["in"].strip() or None)
        assert raised.value.source_lang is None
    else:
        assert Languages.target(case["in"], case.get("mapping")) == case["out"]


@pytest.mark.parametrize("case", LANGUAGES["resolveSource"], ids=lambda c: f"{c['in']!r} {c.get('mapping')}")
def test_resolve_source(case: dict[str, Any]) -> None:
    if "error" in case:
        with pytest.raises(SupertextError) as raised:
            Languages.source(case["in"], case.get("mapping"))
        assert raised.value.code == case["error"]
        assert raised.value.source_lang == case["in"].strip()
        assert raised.value.target_lang is None
    else:
        assert Languages.source(case["in"], case.get("mapping")) == case["out"]


@pytest.mark.parametrize("case", LANGUAGES["supportsPoliteness"], ids=lambda c: repr(c["in"]))
def test_supports_politeness(case: dict[str, Any]) -> None:
    assert Languages.supports_politeness(case["in"]) is case["out"]


@pytest.mark.parametrize("case", spec("auth-header.json")["cases"], ids=lambda c: repr(c["in"]))
def test_auth_header(case: dict[str, Any]) -> None:
    if "error" in case:
        with pytest.raises(SupertextError) as raised:
            auth_header(case["in"])
        assert raised.value.code == case["error"]
    else:
        assert auth_header(case["in"]) == case["out"]


@pytest.mark.parametrize("case", spec("retry-delay.json")["cases"], ids=lambda c: f"{c['attempt']}-{c['retryAfter']}")
@pytest.mark.parametrize("rand", [0.0, 0.5, 0.999999999])
def test_retry_delay(case: dict[str, Any], rand: float) -> None:
    delay = retry_delay_ms(case["attempt"], case["retryAfter"], rand=lambda: rand)
    assert isinstance(delay, int)
    assert case["min"] <= delay <= case["max"]


def test_retry_delay_default_randomness() -> None:
    assert all(1000 <= retry_delay_ms(0) <= 1249 for _ in range(200))


@pytest.mark.parametrize("case", HTML["build"], ids=lambda c: c["name"])
def test_build(case: dict[str, Any]) -> None:
    segments = [Segment(s["text"], s["html"]) for s in case["segments"]]
    assert HtmlDocument.build(segments) == case["html"]


@pytest.mark.parametrize("case", HTML["parse"], ids=lambda c: c["name"])
def test_parse(case: dict[str, Any]) -> None:
    segments = [Segment("", s["html"]) for s in case["segments"]]
    assert HtmlDocument.parse(case["html"], segments) == case["out"]


@pytest.mark.parametrize("case", ERRORS["http"], ids=lambda c: f"{c['status']} {c['body'][:30]}")
def test_http_errors(case: dict[str, Any]) -> None:
    error = error_for_response(case["status"], case["body"])
    assert error.code == case["code"]
    assert error.status == case["status"]
    assert error.detail == case["detail"]
    assert error.api_error_code == case["apiErrorCode"]
    assert error.source_lang == case["sourceLang"]
    assert error.target_lang == case["targetLang"]
    assert error.feature == case["feature"]


@pytest.mark.parametrize("case", [c for c in ERRORS["http"] if c["status"] != 429], ids=lambda c: str(c["status"]))
def test_http_errors_through_the_client(case: dict[str, Any]) -> None:
    client, _ = make_client(FakeTransport([text_response(case["body"], case["status"])]))
    with pytest.raises(SupertextError) as raised:
        client.validate_api_key()
    assert raised.value.code == case["code"]
    assert raised.value.status == case["status"]


@pytest.mark.parametrize("case", ERRORS["fileStatus"], ids=lambda c: c["status"])
def test_file_status(case: dict[str, Any]) -> None:
    # The fake answers with the status once, then "done", so non-terminal statuses keep polling.
    transport = FakeTransport([json_response({"status": case["status"]}), json_response({"status": "done"})])
    client, _ = make_client(transport)
    if case["code"] is None:
        client.wait_until_done("f1")
    else:
        with pytest.raises(SupertextError) as raised:
            client.wait_until_done("f1")
        assert raised.value.code == case["code"]


def test_error_codes_are_complete() -> None:
    codes = {value for name, value in vars(ErrorCode).items() if name.isupper()}
    assert codes == set(ERRORS["codes"])


@pytest.mark.parametrize("case", BATCHING["cases"], ids=lambda c: c["name"])
def test_batching(case: dict[str, Any]) -> None:
    segments = {s["key"]: Segment(s["text"], s["html"]) for s in case["segments"]}
    assert plan_batches(segments, case["maxCharacters"]) == case["batches"]


def test_default_max_characters() -> None:
    assert BATCHING["defaultMaxCharacters"] == DEFAULT_MAX_CHARACTERS
