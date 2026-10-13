# Working on this repository

Part of Supertext's translation plugins project. This repo is not a plugin: it holds the **integration guide** and the **SDKs** (TypeScript, PHP, Python, .NET, Java) for the Supertext AI file translation API, distilled from the ~30 `Supertext/<System>-Supertext-Translation` plugins. See `docs/DEVELOPER.md`.

## Documentation rule (always)

Every change that affects behaviour, the public API, options or the repo structure updates in the same commit: `docs/integration-guide.md` (and `docs/api-reference.md` for protocol facts), the affected SDK READMEs, `docs/DEVELOPER.md` (layout, commands, CI, releasing), the root `README.md` (short overview) and `CHANGELOG.md` under *Unreleased*. Before finishing any task, check the docs still match the code.

## One behaviour, five SDKs (always)

- The vectors in `spec/` define the behaviour. Every SDK's tests load them; every SDK passes `node stand-in/conformance.mjs -- <its conformance program>`.
- A behaviour change starts with the vector, then **all five SDKs**, their tests, the guide and the changelog, in one commit. Never let one SDK drift.
- Same concepts and defaults everywhere (`translateTexts`, `translateHtml`, single steps, `LanguageCodes`, `HtmlDocument`, one error type with the fixed string codes), named the way each language expects.
- No runtime dependencies in the SDKs. No logging inside the SDKs. The API key never appears in messages, details, `toString` or logs.

## Supertext account and API key links (always)

Wherever the docs or a README mention the API key, show both links: "No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role)."

## Shared Supertext protocol (lessons from the live API, October 2026)

- **Auth header:** `Authorization: Supertext-Auth-Key <key>`. `Authentication` gets 403; no prefix gets 400. Strip a pasted prefix and send exactly one.
- **Upload:** multipart; the file part's Content-Type must be exactly `text/html` (a charset gives 415). Max 1,000,000 characters per document.
- **Rate limit:** HTTP 429 `RATE_LIMIT_EXCEEDED`; retry up to 4 times (`Retry-After`, else 1/2/4/8 s with jitter, cap 30 s). Documents one after the other.
- **Rich text:** each `data-st-id` element is translated on its own: one per paragraph with inline tags, never one per formatted run.
- **Language codes:** source = bare language (`en`), target = regional code (`de-DE`, `en-US`); Supertext rejects bare targets (`INVALID_LANGUAGE_PAIR`, HTTP 400). A language it doesn't know at all (`xx-YY`) gets HTTP 404 with the same `error_code`. Bare locales get a regional default (`spec/language-codes.json`).

When a plugin learns something new about the API, add it here, to the guide, and (if the SDKs should handle it) to the spec and all SDKs.

## Before committing

Run the commands for every SDK you touched (`docs/DEVELOPER.md` → *Tests and CI*), including conformance. Spec changes touch all five. Workflow changes must pass actionlint and zizmor; third-party actions are pinned to commit SHAs.

## Releases (always)

`.github/workflows/release.yml` releases when `CHANGELOG.md` gets a new `## X.Y.Z — YYYY-MM-DD` section and the version files (`sdk/typescript/package.json`, `sdk/python/pyproject.toml`, `sdk/dotnet/src/Supertext.Sdk/Supertext.Sdk.csproj`, `sdk/java/pom.xml`) carry the same number. Never tag or create a release by hand. Publishing to npm, PyPI, NuGet, Maven Central and Packagist isn't automated yet (`docs/DEVELOPER.md` → *Releasing*).

## Repo setup (always)

`LICENSE` (MIT, also copied into each `sdk/*` folder for the packages), `SECURITY.md`, `.github/dependabot.yml` (npm, Composer, NuGet, Maven, GitHub Actions), `checks.yml` and `links.yml`, CI per SDK. On GitHub: About box, `main` protected, Wiki and Projects off, Dependabot alerts, private vulnerability reporting, secret scanning with push protection and CodeQL default setup on. Claude sessions can't change GitHub repo settings (HTTP 403): the repo is in Remy's setup script (`set-github-about`).

## Checks and alerts (always)

Before starting work, look at open findings: code scanning alerts (`gh api 'repos/Supertext/Supertext-API-SDK-and-Guide/code-scanning/alerts?state=open'`), secret scanning alerts (`…/secret-scanning/alerts?state=open`), open Dependabot PRs and the issue "Broken links in the docs". Fix what the task touches or what is quick.

## Plugin list (always)

`README.md` carries the shared list of all Supertext plugins between the `<!-- supertext-plugins:start -->` and `<!-- supertext-plugins:end -->` markers, identical to the plugin repos (current block: the project's `plugin-conventions.md`). Update it here too when the list changes.
