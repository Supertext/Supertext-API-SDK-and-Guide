# Changelog

All SDKs in this repository share one version. Format: one section per release, newest first; changes not yet released go under *Unreleased*.

## Unreleased

## 0.1.0 — 2026-10-13

### Changed

- Language codes: locale variants and extensions are dropped (`de_DE_formal` → `de-DE`, `en-US-x-twain` → `en-US`); WordPress's formal/informal locales no longer reach Supertext as an unknown code.
- TypeScript: test tooling moved to Vitest 5, which fixes critical advisories in Vitest's tinypool dependency (development only; the package has no dependencies). Working on the SDK needs Node 22.12+; the package still runs on Node 18+, checked in CI with the conformance scenarios.
- PHP: ext-curl is no longer required. Only the built-in `CurlTransport` needs it; integrations that pass their own `HttpTransport` (e.g. Contao with Symfony HttpClient) install and run without curl. Without curl and without a transport, the client says so when it's created.

### Fixed

- All SDKs: an unknown language (e.g. target `xx-YY`) is reported as `invalid_language_pair`, with the codes the API names. The live API answers it with HTTP 404 and `error_code` `INVALID_LANGUAGE_PAIR`; the SDKs only recognised HTTP 400 and reported `not_found`.

### Added

- Integration guide (`docs/integration-guide.md`) and API reference (`docs/api-reference.md`): the AI file translation protocol and the lessons from Supertext's CMS, PIM and shop plugins.
- SDKs for TypeScript (`supertext-sdk` on npm), PHP (`supertext/supertext-sdk`), Python (`supertext-sdk`), .NET (`Supertext.Sdk`) and Java (`com.supertext:supertext-sdk`): translate texts and HTML with language codes, the auth header, HTTP 429 retries, polling, cleanup and HTML packing handled.
- Shared test vectors (`spec/`), a stand-in API (`stand-in/stand-in.mjs`) and conformance scenarios every SDK passes in CI.
- All SDKs: Commando Center support, so plugins don't implement it themselves (`spec/reporting.json`, `docs/integration-guide.md` → *Version reports*):
  - `checkApiKey()` on the client: the automatic API key check (`GET features`, 10 s), `ok`/`invalid`/`missing`/`unreachable`/`error` with HTTP status, Supertext's error code and a local key fingerprint. Never throws.
  - `InstallationReporter`: version reports (`installed`, `settings_saved`, `upgraded`, `api_key_check`, `check`) with a 3 s timeout, no new attempt for an hour after a failure, and a plain JSON state the plugin stores.
  - `reportsEnabled()` (setting plus `SUPERTEXT_VERSION_REPORTS`), `pingResponse()` for the plugin's public ping endpoint, and an `SDK_VERSION` constant for the reports.
  - `spec/commando/`: the center's schema and vectors.
