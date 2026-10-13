"""Client behaviour against a fake transport."""

from __future__ import annotations

import re
import socket
import threading
from collections.abc import Iterator
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest
from conftest import FakeTransport, json_response, make_client, multipart_parts, text_response

import supertext
from supertext import v1
from supertext.v1 import ErrorCode, HttpResponse, Segment, SupertextClient, SupertextError

KEY = "secret-key-123"


def translated(*contents: str) -> HttpResponse:
    """A translated document with the given segment contents."""
    body = "".join(f'<div data-st-id="{i}">{c}</div>\n' for i, c in enumerate(contents))
    return text_response(f"<!DOCTYPE html>\n<html><body>\n{body}</body></html>\n")


def happy(*contents: str, file_id: str = "f1") -> list[HttpResponse]:
    return [
        json_response({"file_id": file_id}),
        json_response({"status": "translating"}),
        json_response({"status": "done"}),
        translated(*contents),
    ]


# -- Upload -----------------------------------------------------------------------------------


def test_upload_fields_order_and_content_type() -> None:
    transport = FakeTransport([json_response({"file_id": "abc"})])
    client, _ = make_client(transport)
    assert client.submit("<p>Grüezi</p>", "de-CH", "en", "more") == "abc"
    request = transport.requests[0]
    assert request.method == "POST"
    assert request.url == "https://api.test/v1/translate/ai/file"
    parts = multipart_parts(request)
    assert [(name, value) for name, _, value in parts] == [
        ("target_lang", "de-CH"),
        ("source_lang", "en"),
        ("politeness", "more"),
        ("file", "<p>Grüezi</p>"),
    ]
    _, file_headers, _ = parts[3]
    assert file_headers["Content-Disposition"] == 'form-data; name="file"; filename="content.html"'
    assert file_headers["Content-Type"] == "text/html"
    assert set(file_headers) == {"Content-Disposition", "Content-Type"}
    assert all("Content-Type" not in headers for _, headers, _ in parts[:3])


def test_upload_sends_language_codes_as_given() -> None:
    transport = FakeTransport([json_response({"file_id": "abc"})])
    client, _ = make_client(transport)
    client.submit("x", " de ", " en-US ")
    fields = {name: value for name, _, value in multipart_parts(transport.requests[0])}
    assert fields["target_lang"] == "de"
    assert fields["source_lang"] == "en-US"


@pytest.mark.parametrize("target", ["", "   "])
def test_empty_target_sends_nothing(target: str) -> None:
    transport = FakeTransport()
    client, _ = make_client(transport)
    with pytest.raises(SupertextError) as raised:
        client.translate_texts({"a": "Hello"}, target)
    assert raised.value.code == ErrorCode.UNSUPPORTED_LANGUAGE
    assert transport.requests == []


def test_get_features() -> None:
    body = {
        "supported_features": [
            {"source_lang": "de", "target_lang": "en-US", "features": ["supports_glossary", 3]},
            {"source_lang": "en", "target_lang": "de-CH"},
            {"source_lang": "en"},
            "junk",
            {"source_lang": 1, "target_lang": "fr-CH", "features": []},
        ]
    }
    transport = FakeTransport([json_response(body)])
    client, _ = make_client(transport)
    assert client.get_features() == [
        v1.FeatureSupport("de", "en-US", ("supports_glossary",)),
        v1.FeatureSupport("en", "de-CH", ()),
    ]
    assert transport.calls == ["GET features"]


@pytest.mark.parametrize("body", [{}, {"supported_features": "x"}, []])
def test_get_features_without_a_list(body: object) -> None:
    client, _ = make_client(FakeTransport([json_response(body)]))
    assert client.get_features() == []


def test_get_features_raises_on_http_errors() -> None:
    client, _ = make_client(FakeTransport([text_response("", 401)]))
    with pytest.raises(SupertextError) as raised:
        client.get_features()
    assert raised.value.code == ErrorCode.AUTHENTICATION_FAILURE


@pytest.mark.parametrize("source", [None, "", "  "])
@pytest.mark.parametrize("politeness", ["default", "bogus"])
def test_upload_leaves_out_empty_source_and_default_politeness(source: str | None, politeness: str) -> None:
    transport = FakeTransport([json_response({"file_id": "abc"})])
    client, _ = make_client(transport)
    client.submit("x", "fr-CH", source, politeness)  # type: ignore[arg-type]
    assert [name for name, _, _ in multipart_parts(transport.requests[0])] == ["target_lang", "file"]


@pytest.mark.parametrize("body", [{}, {"file_id": ""}, {"file_id": None}, "not json"])
def test_no_file_id(body: object) -> None:
    answer = text_response(body) if isinstance(body, str) else json_response(body)
    client, _ = make_client(FakeTransport([answer]))
    with pytest.raises(SupertextError) as raised:
        client.submit("x", "de-DE")
    assert raised.value.code == ErrorCode.NO_FILE_ID


# -- Headers ----------------------------------------------------------------------------------


def test_headers() -> None:
    transport = FakeTransport([json_response({})])
    client, _ = make_client(transport, api_key=f"Supertext-Auth-Key {KEY}")
    client.validate_api_key()
    request = transport.requests[0]
    assert request.url == "https://api.test/v1/features"
    assert request.method == "GET"
    assert request.headers["Authorization"] == f"Supertext-Auth-Key {KEY}"
    assert request.headers["Accept"] == "application/json"
    assert request.headers["User-Agent"] == "supertext-sdk-python"
    assert request.timeout == 30.0


def test_user_agent_option_and_request_timeout() -> None:
    transport = FakeTransport([json_response({})])
    client, _ = make_client(transport, user_agent="Wagtail-Supertext-Translation/1.2.0", request_timeout=5)
    client.validate_api_key()
    assert transport.requests[0].headers["User-Agent"] == "supertext-sdk-python Wagtail-Supertext-Translation/1.2.0"
    assert transport.requests[0].timeout == 5


@pytest.mark.parametrize("base_url", ["https://x.test/v1", "https://x.test/v1/", "https://x.test/v1//"])
def test_base_url_gets_one_trailing_slash(base_url: str) -> None:
    transport = FakeTransport([json_response({})])
    SupertextClient(KEY, base_url=base_url, transport=transport).validate_api_key()
    assert transport.requests[0].url == "https://x.test/v1/features"


def test_default_base_url_is_live() -> None:
    assert SupertextClient(KEY).base_url == v1.LIVE_BASE_URL == "https://api.supertext.com/v1/"


def test_file_id_is_url_encoded() -> None:
    transport = FakeTransport([json_response({"status": "done"}), text_response("<p>x</p>")])
    client, _ = make_client(transport)
    client.get_status("a/b c?")
    client.download("a/b c?")
    client.delete("a/b c?")
    assert transport.calls == [
        "GET translate/ai/file/a%2Fb%20c%3F/status",
        "GET translate/ai/file/a%2Fb%20c%3F/translation",
        "DELETE translate/ai/file/a%2Fb%20c%3F",
    ]


# -- Missing key and secrecy ------------------------------------------------------------------


@pytest.mark.parametrize("api_key", [None, "", "   ", "Supertext-Auth-Key "])
def test_missing_key_sends_nothing(api_key: str | None) -> None:
    transport = FakeTransport()
    client = SupertextClient(api_key, transport=transport)  # does not raise
    with pytest.raises(SupertextError) as raised:
        client.translate_texts({"a": "Hello"}, "de-CH")
    assert raised.value.code == ErrorCode.MISSING_API_KEY
    assert transport.requests == []
    assert "https://www.supertext.com/en/integrations/api" in raised.value.message


def test_key_never_in_errors() -> None:
    answers: list[HttpResponse | Exception] = [
        text_response(f"<p>Unauthorized: Supertext-Auth-Key {KEY}</p>", 401),
        text_response(f"bad key {KEY}", 400),
        OSError(f"connection refused while sending {KEY}"),
    ]
    client, _ = make_client(FakeTransport(answers))
    for _ in answers:
        with pytest.raises(SupertextError) as raised:
            client.validate_api_key()
        error = raised.value
        for text in (str(error), repr(error), error.message, error.detail or "", repr(error.args)):
            assert KEY not in text
    assert KEY not in repr(client)


# -- Retries ----------------------------------------------------------------------------------


def test_429_retried_with_retry_after() -> None:
    transport = FakeTransport(
        [
            json_response({"error_code": "RATE_LIMIT_EXCEEDED"}, 429, {"retry-after": "3"}),
            json_response({"error_code": "RATE_LIMIT_EXCEEDED"}, 429, {"retry-after": "0.5"}),
            json_response({"file_id": "f1"}),
        ]
    )
    client, clock = make_client(transport)
    assert client.submit("x", "de-DE") == "f1"
    assert clock.sleeps == [3.0, 0.5]
    assert len(transport.requests) == 3


def test_429_backoff_and_exhaustion() -> None:
    transport = FakeTransport([json_response({"error_code": "RATE_LIMIT_EXCEEDED"}, 429)] * 5)
    client, clock = make_client(transport)
    with pytest.raises(SupertextError) as raised:
        client.submit("x", "de-DE")
    assert raised.value.code == ErrorCode.TOO_MANY_REQUESTS
    assert raised.value.status == 429
    assert raised.value.api_error_code == "RATE_LIMIT_EXCEEDED"
    assert len(transport.requests) == 5
    assert len(clock.sleeps) == 4
    for sleep, base in zip(clock.sleeps, [1, 2, 4, 8], strict=True):
        assert base <= sleep <= base + 0.249


def test_max_retries_option() -> None:
    transport = FakeTransport([json_response({}, 429)] * 2)
    client, clock = make_client(transport, max_retries=1)
    with pytest.raises(SupertextError):
        client.validate_api_key()
    assert len(transport.requests) == 2
    assert len(clock.sleeps) == 1


# -- Transport failures -----------------------------------------------------------------------


@pytest.mark.parametrize("error", [OSError("connection refused"), TimeoutError("timed out"), socket.gaierror(-2, "x")])
def test_transport_error(error: OSError) -> None:
    client, _ = make_client(FakeTransport([error]))
    with pytest.raises(SupertextError) as raised:
        client.validate_api_key()
    assert raised.value.code == ErrorCode.TRANSPORT_ERROR
    assert raised.value.status is None
    assert raised.value.detail


# -- Polling ----------------------------------------------------------------------------------


def test_polling_first_poll_right_away_then_interval() -> None:
    transport = FakeTransport([json_response({"status": s}) for s in ("translating", "queued", "translating", "done")])
    client, clock = make_client(transport, poll_interval=0.5)
    client.wait_until_done("f1")
    assert clock.sleeps == [0.5, 0.5, 0.5]
    assert len(transport.requests) == 4


def test_polling_timeout() -> None:
    transport = FakeTransport([json_response({"status": "translating"})] * 100)
    client, clock = make_client(transport, poll_interval=2, timeout=7)
    with pytest.raises(SupertextError) as raised:
        client.wait_until_done("f1")
    assert raised.value.code == ErrorCode.TIMEOUT
    # Polls at 0, 2, 4, 6; a poll at 8 would start after the 7 s timeout.
    assert len(transport.requests) == 4
    assert clock.sleeps == [2, 2, 2]


def test_polling_exactly_at_timeout_still_polls() -> None:
    transport = FakeTransport([json_response({"status": "translating"})] * 3 + [json_response({"status": "done"})])
    client, _ = make_client(transport, poll_interval=2, timeout=6)
    client.wait_until_done("f1")  # polls at 0, 2, 4, 6
    assert len(transport.requests) == 4


# -- translate_html ---------------------------------------------------------------------------


def test_translate_html_happy_path() -> None:
    transport = FakeTransport(happy("Hallo"))
    client, _ = make_client(transport)
    html = client.translate_html("<p>Hello</p>", "de-CH")
    assert "Hallo" in html
    assert transport.calls == [
        "POST translate/ai/file",
        "GET translate/ai/file/f1/status",
        "GET translate/ai/file/f1/status",
        "GET translate/ai/file/f1/translation",
        "DELETE translate/ai/file/f1",
    ]


@pytest.mark.parametrize(
    ("answers", "code"),
    [
        ([json_response({"status": "error"})], ErrorCode.TRANSLATION_ERROR),
        ([json_response({"status": "limit_exceeded"})], ErrorCode.QUOTA_EXCEEDED),
        ([json_response({"status": "deleted"})], ErrorCode.FILE_DELETED),
        ([json_response({}, 500)], ErrorCode.SERVICE_UNAVAILABLE),
        ([OSError("reset")], ErrorCode.TRANSPORT_ERROR),
        ([json_response({"status": "done"}), text_response("")], ErrorCode.INCOMPLETE_RESPONSE),
        ([json_response({"status": "done"}), text_response(" \n\t ")], ErrorCode.INCOMPLETE_RESPONSE),
        ([json_response({"status": "done"}), text_response("", 404)], ErrorCode.NOT_FOUND),
    ],
)
@pytest.mark.parametrize(
    "delete_answer", [None, json_response({}, 500), OSError("down"), RuntimeError("bug in transport")]
)
def test_delete_after_failure_and_never_raising(
    answers: list[HttpResponse | Exception], code: str, delete_answer: HttpResponse | Exception | None
) -> None:
    transport = FakeTransport([json_response({"file_id": "f1"}), *answers], delete_answer=delete_answer)
    client, _ = make_client(transport)
    with pytest.raises(SupertextError) as raised:
        client.translate_html("<p>x</p>", "de-DE")
    assert raised.value.code == code
    assert transport.calls[-1] == "DELETE translate/ai/file/f1"


def test_delete_never_raises_on_its_own() -> None:
    client, _ = make_client(FakeTransport(delete_answer=json_response({}, 404)))
    client.delete("gone")


def test_no_delete_when_upload_fails() -> None:
    transport = FakeTransport([json_response({"error_code": "INVALID_LANGUAGE_PAIR"}, 400)])
    client, _ = make_client(transport)
    with pytest.raises(SupertextError) as raised:
        client.translate_html("x", "xx")
    assert raised.value.code == ErrorCode.INVALID_LANGUAGE_PAIR
    assert transport.calls == ["POST translate/ai/file"]


# -- translate_texts --------------------------------------------------------------------------


def test_translate_texts() -> None:
    transport = FakeTransport(happy("Hallo &amp; willkommen", "<p>Ein <b>Text</b></p>", "Zeile 1<br>Zeile 2"))
    client, _ = make_client(transport)
    result = client.translate_texts(
        {
            "title": "Hello & welcome",
            "blank": "  ",
            "body": Segment("<p>A <b>text</b></p>", html=True),
            "lines": Segment("Line 1\nLine 2"),
        },
        "de-CH",
        "en",
    )
    assert list(result) == ["title", "blank", "body", "lines"]
    assert result == {
        "title": "Hallo & willkommen",
        "blank": "  ",
        "body": "<p>Ein <b>Text</b></p>",
        "lines": "Zeile 1\nZeile 2",
    }
    upload = multipart_parts(transport.requests[0])
    assert upload[-1][2] == (
        '<!DOCTYPE html>\n<html><head><meta charset="utf-8"></head><body>\n'
        '<div data-st-id="0">Hello &amp; welcome</div>\n'
        '<div data-st-id="1"><p>A <b>text</b></p></div>\n'
        '<div data-st-id="2">Line 1<br>Line 2</div>\n'
        "</body></html>\n"
    )


def test_translate_texts_nothing_to_send() -> None:
    transport = FakeTransport()
    client, _ = make_client(transport, api_key="")  # not even the missing key matters
    assert client.translate_texts({"a": "", "b": Segment(" \n ", html=True)}, "de-CH") == {"a": "", "b": " \n "}
    assert transport.requests == []


def test_translate_texts_missing_segments() -> None:
    transport = FakeTransport(happy("eins"))
    client, _ = make_client(transport)
    with pytest.raises(SupertextError) as raised:
        client.translate_texts({"a": "one", "b": "two", "c": "three"}, "de-DE")
    assert raised.value.code == ErrorCode.INCOMPLETE_RESPONSE
    assert raised.value.detail == "missing: b, c"
    assert transport.calls[-1] == "DELETE translate/ai/file/f1"


def test_translate_texts_batches_one_after_the_other() -> None:
    transport = FakeTransport([*happy("A1", "B1", file_id="f1"), *happy("C1", file_id="f2")])
    client, _ = make_client(transport)
    result = client.translate_texts({"a": "12345", "b": "12345", "c": "1"}, "de-DE", max_characters=10)
    assert result == {"a": "A1", "b": "B1", "c": "C1"}
    assert [c for c in transport.calls if not c.startswith("GET")] == [
        "POST translate/ai/file",
        "DELETE translate/ai/file/f1",
        "POST translate/ai/file",
        "DELETE translate/ai/file/f2",
    ]


def test_version() -> None:
    assert re.fullmatch(r"\d+\.\d+\.\d+.*", supertext.__version__)


# -- Default transport against a local server ---------------------------------------------------


class _Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/v1/features":
            body, status = b'{"features": []}', 200
        else:
            body, status = b"<h1>Nope</h1>", 404
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("X-Echo-Auth", self.headers.get("Authorization", ""))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:
        pass


@pytest.fixture
def server() -> Iterator[str]:
    httpd = HTTPServer(("127.0.0.1", 0), _Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{httpd.server_address[1]}/v1"
    httpd.shutdown()
    httpd.server_close()


def test_urllib_transport(server: str) -> None:
    client = SupertextClient(KEY, base_url=server)
    client.validate_api_key()
    with pytest.raises(SupertextError) as raised:
        client.get_status("x")
    assert raised.value.code == ErrorCode.NOT_FOUND
    assert raised.value.detail == "Nope"


def test_urllib_transport_connection_refused() -> None:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    with pytest.raises(SupertextError) as raised:
        SupertextClient(KEY, base_url=f"http://127.0.0.1:{port}/v1", request_timeout=2).validate_api_key()
    assert raised.value.code == ErrorCode.TRANSPORT_ERROR
