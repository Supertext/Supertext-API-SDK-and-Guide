"""Supertext AI translation API client: translate HTML and texts, with language codes,
rate-limit retries and HTML packing handled for you.

    import supertext

    client = supertext.Client(api_key)
    texts = client.translate_texts({"title": "Hello", "body": supertext.Segment("<p>Hi</p>", html=True)}, "de-CH")
"""

from . import language_codes
from .api_key_check import KEY_CHECK_TIMEOUT, ApiKeyCheck, ApiKeyCheckStatus, api_key_fingerprint
from .client import (
    DEFAULT_MAX_CHARACTERS,
    LIVE_BASE_URL,
    Client,
    Politeness,
    auth_header,
    plan_batches,
    retry_delay_ms,
)
from .errors import API_KEY_URL, SIGNUP_URL, ErrorCode, SupertextError
from .html_document import HtmlDocument, Segment
from .reporting import (
    DEFAULT_REPORT_URL,
    FAILURE_BACKOFF,
    REPORT_TIMEOUT,
    InstallationReporter,
    NameVersion,
    PingResponse,
    ReporterResult,
    ReporterState,
    ReportEvent,
    StateInput,
    ping_response,
    reporter_state,
    reports_enabled,
)
from .transport import HttpRequest, HttpResponse, Transport, UrllibTransport
from .version import SDK_VERSION

LanguageCodes = language_codes
"""Alias matching the other Supertext SDKs: ``LanguageCodes.target("de")``."""

__version__ = SDK_VERSION

__all__ = [
    "API_KEY_URL",
    "DEFAULT_MAX_CHARACTERS",
    "DEFAULT_REPORT_URL",
    "FAILURE_BACKOFF",
    "KEY_CHECK_TIMEOUT",
    "LIVE_BASE_URL",
    "REPORT_TIMEOUT",
    "SDK_VERSION",
    "SIGNUP_URL",
    "ApiKeyCheck",
    "ApiKeyCheckStatus",
    "Client",
    "ErrorCode",
    "HtmlDocument",
    "HttpRequest",
    "HttpResponse",
    "InstallationReporter",
    "LanguageCodes",
    "NameVersion",
    "PingResponse",
    "Politeness",
    "ReportEvent",
    "ReporterResult",
    "ReporterState",
    "Segment",
    "StateInput",
    "SupertextError",
    "Transport",
    "UrllibTransport",
    "__version__",
    "api_key_fingerprint",
    "auth_header",
    "language_codes",
    "ping_response",
    "plan_batches",
    "reporter_state",
    "reports_enabled",
    "retry_delay_ms",
]
