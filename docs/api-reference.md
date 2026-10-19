# Supertext AI file translation API v1: reference

What the SDKs and plugins use. The [integration guide](integration-guide.md) explains how to use it well.

Base URL: `https://api.supertext.com/v1/`

## Headers

| Header | Value |
| --- | --- |
| `Authorization` | `Supertext-Auth-Key <key>` (required on every request) |
| `Accept` | `application/json` |
| `User-Agent` | Your integration and version, e.g. `supertext-sdk-php Typo3-Supertext-Translation/1.2.0` |

No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

## Endpoints

### `GET features`

Cost-free; checks the key and lists the languages. `200` → `{"supported_features": [{"source_lang": "en", "target_lang": "de-CH", "features": ["supports_politeness", "supports_glossary"]}, …]}` (optional `source_lang`/`target_lang` query parameters filter it); `401`/`403` if the key doesn't work. The pairs are the only codes `translate/ai/file` accepts. The SDKs' `checkApiKey()` maps the answer to `ok`, `invalid`, `missing`, `unreachable` or `error` ([`spec/api-key-check.json`](../spec/api-key-check.json)).

### `POST translate/ai/file`

`multipart/form-data`:

| Field | Required | Value |
| --- | --- | --- |
| `target_lang` | yes | A target of `GET features`, exactly: `de-CH`, `en-US`, `es`, `zh-Hans` |
| `source_lang` | no | A source of `GET features` (bare: `en`); omitted = detected |
| `politeness` | no | `more` (formal) or `less` (informal), only for targets with `supports_politeness`; omitted or `default` = default |
| `file` | yes | The HTML document, part Content-Type exactly `text/html`, at most 1,000,000 characters |

`200` → `{"file_id": "<id>"}`

### `GET translate/ai/file/{file_id}/status`

`200` → `{"status": "<status>"}`: `translating`, `done`, `error`, `limit_exceeded`, `deleted`.

### `GET translate/ai/file/{file_id}/translation`

`200` → the translated HTML document (`text/html`). Available once the status is `done`.

### `DELETE translate/ai/file/{file_id}`

Deletes the uploaded file and its translation. Files expire after 24 hours anyway.

## Errors

| HTTP | Body (`error_code`) | Meaning |
| --- | --- | --- |
| 404 | `INVALID_LANGUAGE_PAIR` | A pair `GET features` doesn't list: a bare `de`, `es-ES`, `zh-CN`, a same-language pair, an unknown code (live API, October 2026; older answers were 400) |
| 400 | `INVALID_LANGUAGE_PAIR` | The same, as some API versions answered it |
| 400 | `FEATURE_ERROR` | The pair exists, but not with this option: `feature` names it (`politeness` for `en-US`, `rm`, `sq`, …) |
| 400 | | Missing `Supertext-Auth-Key` prefix, malformed request |
| 401, 403 | | Key missing, wrong or revoked; `403` also for a wrong header name (`Authentication`) |
| 404 | | Unknown file id (expired or deleted) |
| 413 | | Document too large |
| 415 | | File part Content-Type isn't exactly `text/html` |
| 429 | `RATE_LIMIT_EXCEEDED` | Too many requests per second; retry after `Retry-After` seconds or a backoff |
| 5xx | | Service problem; try again later |

How the SDKs map these to error codes: [`spec/errors.json`](../spec/errors.json).

## Review service (Supertext Localization Review)

Not part of Supertext's API: the [review service](https://github.com/Supertext/Localization-Review-Supertext-Translation) answers the endpoints above like Supertext (same paths, fields, statuses and errors, the customer's key passed on) and adds two of its own. Against Supertext's API they answer 404. SDK behaviour: [`spec/corrections.json`](../spec/corrections.json).

### `GET review/corrections`

Query (all optional): `target_lang`, `source_lang`, `politeness` (`default`, `more`, `less`), `after` (paging), `limit` (1–500, default 500). Answer `200` `{ "corrections": [{ "id", "st_id", "source_lang" (null: detected), "target_lang", "politeness", "source_html", "previous_html", "html", "status", "corrected_at", "version" }], "next_after": "<id>" | null }`: corrections of this key and plugin (the User-Agent's plugin name) that the plugin hasn't received yet. `st_id` is the `data-st-id` the plugin sent; `previous_html` what it received last for that place. Pass `next_after` as `after` for the next page.

### `POST review/corrections/acknowledge`

JSON body `{ "corrections": [{ "id", "version" }] }` (at most 1000): the plugin wrote these into the CMS. Answer `200` `{ "acknowledged": n }`; only a still-current `version` counts.

## Commando Center (version reports)

Not part of the Supertext API and not part of the SDKs: plugins report their versions with Supertext Plugin Health. The protocol (report schema, the plugin's ping endpoint) and Plugin Health are in [Commando-Center-Supertext-Translation](https://github.com/Supertext/Commando-Center-Supertext-Translation) (`docs/DEVELOPER.md` → *Report protocol*, `plugin-health/`).
