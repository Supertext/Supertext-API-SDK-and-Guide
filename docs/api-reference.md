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

Cost-free; checks the key. `200` with a JSON body if the key works, `401`/`403` if not.

### `POST translate/ai/file`

`multipart/form-data`:

| Field | Required | Value |
| --- | --- | --- |
| `target_lang` | yes | Regional code (`de-CH`, `en-US`, `pt-BR`) |
| `source_lang` | no | Bare language (`en`); omitted = detected |
| `politeness` | no | `more` (formal) or `less` (informal); omitted = default |
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
| 400 | `INVALID_LANGUAGE_PAIR` | Unsupported pair; usually a bare target (`de`) or a regional source (`en-US`) |
| 400 | | Missing `Supertext-Auth-Key` prefix, malformed request |
| 401, 403 | | Key missing, wrong or revoked; `403` also for a wrong header name (`Authentication`) |
| 404 | | Unknown file id (expired or deleted) |
| 413 | | Document too large |
| 415 | | File part Content-Type isn't exactly `text/html` |
| 429 | `RATE_LIMIT_EXCEEDED` | Too many requests per second; retry after `Retry-After` seconds or a backoff |
| 5xx | | Service problem; try again later |

How the SDKs map these to error codes: [`spec/errors.json`](../spec/errors.json).
