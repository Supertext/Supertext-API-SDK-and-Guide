# Changelog

All SDKs in this repository share one version. Format: one section per release, newest first; changes not yet released go under *Unreleased*.

## Unreleased

### Changed

- PHP: ext-curl is no longer required. Only the built-in `CurlTransport` needs it; integrations that pass their own `HttpTransport` (e.g. Contao with Symfony HttpClient) install and run without curl. Without curl and without a transport, the client says so when it's created.

### Added

- Integration guide (`docs/integration-guide.md`) and API reference (`docs/api-reference.md`): the AI file translation protocol and the lessons from Supertext's CMS, PIM and shop plugins.
- SDKs for TypeScript (`supertext-sdk` on npm), PHP (`supertext/supertext-sdk`), Python (`supertext-sdk`), .NET (`Supertext.Sdk`) and Java (`com.supertext:supertext-sdk`): translate texts and HTML with language codes, the auth header, HTTP 429 retries, polling, cleanup and HTML packing handled.
- Shared test vectors (`spec/`), a stand-in API (`stand-in/stand-in.mjs`) and conformance scenarios every SDK passes in CI.
