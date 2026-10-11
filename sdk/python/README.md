# Supertext SDK for Python

Client for the [Supertext](https://www.supertext.com) AI translation API: translate HTML and texts, with language codes, rate-limit retries and HTML packing handled for you.

- Python 3.10+, standard library only (no dependencies), fully typed.
- Packs what Supertext's CMS, PIM and shop plugins learned against the live API. Background: [integration guide](../../docs/integration-guide.md).

## Install

```sh
pip install supertext-sdk
```

```python
import supertext
```

## API key

The client needs a Supertext AI API key. No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

Paste it as shown there, with or without the `Supertext-Auth-Key` prefix. Keep it out of your code, e.g. in the `SUPERTEXT_API_KEY` environment variable. `client.validate_api_key()` checks it without cost.

## Quick start

### Texts (fields of a page, product, …)

```python
import os
import supertext
from supertext import Segment

client = supertext.Client(os.environ["SUPERTEXT_API_KEY"], user_agent="MyCms-Supertext/1.0.0")

translations = client.translate_texts(
    {
        "title": "Hello & welcome",                                   # plain text
        "body": Segment("<p>Our <b>new</b> shop opens on Monday.</p>", html=True),
        "teaser": Segment("Line 1\nLine 2"),                          # line breaks survive
    },
    target_lang="de-CH",
    source_lang="en",          # leave out to let Supertext detect it
    politeness="more",
)
translations["title"]  # "Hallo und willkommen"
```

- The result has the same keys in the same order.
- Plain text is escaped on the way out and unescaped on the way back; HTML stays HTML.
- One segment per whole text (field, paragraph): Supertext translates each segment on its own, so never split a sentence at its formatting.
- Blank texts come back unchanged and aren't sent; if nothing is left, no request is made.
- Large inputs are split into several documents of at most `max_characters` (default 900,000) and translated one after the other.

### A whole HTML document

```python
html = client.translate_html("<html><body><h1>Hello</h1></body></html>", "fr")
```

Both calls run the whole protocol: upload, poll the status, download, and always delete the file at Supertext afterwards. The single steps are available too: `submit`, `get_status`, `wait_until_done`, `download`, `delete`.

## Language codes

Supertext wants the **source as a bare language** (`en`) and the **target as a regional code** (`de-DE`). The client corrects the codes you pass, so `"de"` as target becomes `de-DE` and `"en-US"` as source becomes `en`. `"de_ch"` becomes `de-CH`.

If your site uses bare locales but means a specific region, map them yourself before calling:

```python
from supertext import LanguageCodes

overrides = {"de": "de-CH", "no": "nb-NO"}          # CMS locale → Supertext code
LanguageCodes.target("de", overrides)               # "de-CH"
LanguageCodes.source("no", overrides)               # "nb"
LanguageCodes.target("pt")                          # "pt-PT" (LanguageCodes.DEFAULT_REGIONS)
LanguageCodes.is_valid("de_ch")                     # True
LanguageCodes.normalize(" zh_hant_tw ")             # "zh-Hant-TW"
```

## Politeness

`politeness="more"` asks for formal address (Sie, vous, Lei), `"less"` for informal (du, tu), `"default"` (the default) lets Supertext decide.

## Errors

Every failure raises `supertext.SupertextError`. Decide what to show from `error.code` (constants in `supertext.ErrorCode`); `error.message` is English, `error.status` the HTTP status if any, `error.detail` the API's own text. For `invalid_language_pair`, `error.source_lang` / `error.target_lang` carry what the API reported; `error.api_error_code` is the API's `error_code`. The API key never appears in errors.

| `code` | What to tell the user |
| --- | --- |
| `missing_api_key` | No API key is configured (nothing was sent). Show the account and API key links above. |
| `authentication_failure` | The API key is wrong, revoked or lacks access (HTTP 401/403). Show the links above. |
| `invalid_language_pair` | Supertext doesn't support this language pair. Check the language settings. |
| `not_found` | The file is gone at Supertext (expired after 24 h or deleted). Try again. |
| `payload_too_large` | The content is too large; lower `max_characters` or split it. |
| `too_many_requests` | Too many requests, even after retrying. Try again shortly. |
| `service_unavailable` | Supertext is unavailable (HTTP 5xx). Try again later. |
| `unexpected_status` | Supertext answered with an unexpected HTTP status (see `status`/`detail`). |
| `transport_error` | Supertext couldn't be reached (network, DNS, TLS, request timeout). |
| `no_file_id` | The upload answer had no file id. Try again. |
| `translation_error` | Supertext couldn't translate the document. |
| `quota_exceeded` | The account's translation limit is used up. |
| `file_deleted` | The file was deleted at Supertext before the download. Try again. |
| `timeout` | The translation took longer than `timeout`. Try again, or raise `timeout`. |
| `incomplete_response` | The translation came back empty or with texts missing (`detail`: `missing: key1, key2`). |

```python
try:
    translations = client.translate_texts(texts, "de-CH")
except supertext.SupertextError as error:
    if error.code == supertext.ErrorCode.AUTHENTICATION_FAILURE:
        ...
```

## Options

```python
supertext.Client(
    api_key,
    base_url=supertext.LIVE_BASE_URL,  # "https://api.supertext.com/v1/"
    poll_interval=2.0,                 # seconds between status polls
    timeout=180.0,                     # seconds until a translation gives up (timeout)
    request_timeout=30.0,              # seconds per HTTP request (transport_error)
    max_retries=4,                     # HTTP 429 retries (Retry-After, else 1/2/4/8 s + jitter; max 30 s)
    user_agent=None,                   # appended to "supertext-sdk-python", e.g. "MyCms-Supertext/1.0.0"
    transport=None,                    # callable(HttpRequest) -> HttpResponse; default: urllib
    sleep=time.sleep,                  # injectable for tests
    clock=time.monotonic,              # injectable for tests
)
```

A blank key doesn't fail in the constructor, only on the first request. A custom `transport` raises `OSError` when there's no HTTP answer; the client turns that into `transport_error`.

## Testing your integration

Use the stand-in API from this repository instead of the live API: it checks the request the way the live API does and returns `[de-DE] …` in front of every text.

```sh
node stand-in/stand-in.mjs            # http://127.0.0.1:8765/v1/
```

```python
client = supertext.Client("any-key", base_url="http://127.0.0.1:8765/v1/", poll_interval=0.1)
```

For unit tests, pass a fake `transport` (and `sleep`) instead.

## Developing this SDK

From `sdk/python`:

```sh
pip install pytest mypy ruff
ruff check . && ruff format --check .
mypy --strict
pytest
```

Conformance against the stand-in, from the repository root:

```sh
node stand-in/conformance.mjs -- python3 sdk/python/examples/conformance.py
```

## License

MIT
