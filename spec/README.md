# Shared test vectors

Every SDK in this repository loads these files in its tests and must pass every case; integrations that implement the protocol themselves can use them too. The rules each file encodes are in its `description` and in the [integration guide](../docs/integration-guide.md).

| File | What it pins down |
| --- | --- |
| [`languages.json`](languages.json) | The API's source and target languages (from the live `GET features`), matching CMS codes exactly, the integration's mapping, `unsupported_language`, which targets offer politeness |
| [`auth-header.json`](auth-header.json) | The `Authorization` header from a pasted key, prefix stripping, blank keys |
| [`retry-delay.json`](retry-delay.json) | Waits before retrying HTTP 429 (`Retry-After`, backoff, jitter, cap) |
| [`errors.json`](errors.json) | HTTP answers and file statuses → error codes, `detail`, `apiErrorCode`, language pair |
| [`html-document.json`](html-document.json) | Packing segments into the HTML document (byte-exact) and reading them back |
| [`batching.json`](batching.json) | Splitting many segments into documents under the size limit |
| [`reporting.json`](reporting.json) | The API key check (fingerprint, answer → `ok`/`invalid`/`missing`/`unreachable`/`error`), the reporting setting, the ping endpoint's answers, and the installation reporter's state machine and report bodies |
| [`commando/`](commando/) | Copies of the Commando Center's `spec/` (report schema, examples, site URL and callback vectors; source commit in `commando/COMMIT`). Update them with the center, never by hand |
| [`conformance.json`](conformance.json) | End-to-end scenarios against the [stand-in](../stand-in/stand-in.mjs) (`node stand-in/conformance.mjs -- <program>`) |

Changing a vector changes all five SDKs: update the SDKs and their tests in the same commit, and note the behaviour change in `CHANGELOG.md`.
