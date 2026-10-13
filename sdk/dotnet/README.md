# Supertext SDK for .NET

Client for the Supertext AI translation API: translate HTML and texts, with language codes, rate-limit retries and HTML packing handled for you. NuGet package `Supertext.Sdk`, namespace `Supertext.Sdk`, `net8.0`, no dependencies.

It packages what Supertext's CMS plugins learned against the live API (October 2026). Background and the protocol: [integration guide](../../docs/integration-guide.md).

## Install

```sh
dotnet add package Supertext.Sdk
```

You need an API key. No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role). Paste the key with or without its `Supertext-Auth-Key` prefix.

## Quick start

```csharp
using Supertext.Sdk;

var client = new SupertextClient(Environment.GetEnvironmentVariable("SUPERTEXT_API_KEY"),
    new SupertextClientOptions { UserAgent = "My-CMS-Plugin/1.0.0" });

// Keyed texts: packed into one HTML document, translated, unpacked. Results come back in input order.
var translated = await client.TranslateTextsAsync(new Dictionary<string, Segment>
{
    ["title"] = "Hello & welcome",                                        // plain text (escaped, \n → <br>)
    ["body"] = new Segment("<p>Our <b>new</b> shop opens on Monday.</p>", Html: true),
}, targetLang: "de-CH", sourceLang: "en", politeness: Politeness.More);

Console.WriteLine(translated["title"]);

// A whole HTML document: upload, poll, download, delete.
string html = await client.TranslateHtmlAsync("<p>Hello</p>", "fr");
```

Send each paragraph (heading, list item) as **one** segment, with formatting and links as inline tags. Every segment is translated on its own; splitting a sentence at its formatting breaks the translation.

`TranslateTextsAsync` takes any `IEnumerable<KeyValuePair<string, Segment>>`. Blank texts come back unchanged and cost no request. Large inputs are split into several documents of at most `maxCharacters` (default 900,000), sent one after the other.

The lower-level steps are public too: `SubmitAsync`, `GetStatusAsync`, `WaitUntilDoneAsync`, `DownloadAsync`, `DeleteAsync` (never throws), and `ValidateApiKeyAsync` (a cost-free key check for your settings screen). Every method takes a `CancellationToken`.

## Language codes

Supertext wants the **source as a bare language** (`en`) and the **target as a regional code** (`de-DE`); anything else is rejected with `invalid_language_pair`. The client fixes this itself: `de` → `de-DE`, `en-US` (source) → `en`, `de_ch` → `de-CH`. A null or empty source means auto-detect.

If your CMS locales should map differently (e.g. your `de` site is Swiss German), resolve them yourself first:

```csharp
var overrides = new Dictionary<string, string> { ["de"] = "de-CH", ["no"] = "nb-NO" };
var target = LanguageCodes.Target(cmsLocale, overrides);   // "de" → "de-CH"
var source = LanguageCodes.Source(cmsLocale, overrides);   // "no" → "nb"
LanguageCodes.IsValid("de_ch");                            // true
LanguageCodes.Normalize("zh_hant_tw");                     // "zh-Hant-TW"
```

## Politeness

`Politeness.Default` (nothing is sent), `Politeness.More` (formal: Sie, vous) or `Politeness.Less` (informal: du, tu).

## Errors

Everything the API or the client can't handle is a `SupertextException` with `Code` (constants in `SupertextErrorCodes`), an English `Message`, and `Status`, `Detail`, `ApiErrorCode`, `SourceLang`/`TargetLang` where known. The API key never appears in messages or details. Cancelling your token throws `OperationCanceledException` as usual.

| Code | Meaning | What to tell the user |
| --- | --- | --- |
| `missing_api_key` | No key configured; nothing was sent | Enter the API key in the settings (with the account and API-key links above) |
| `authentication_failure` | HTTP 401/403 | The API key is wrong or revoked: check it (links above) |
| `invalid_language_pair` | HTTP 400 (404 for an unknown language) `INVALID_LANGUAGE_PAIR` | Supertext doesn't support this language combination; check the language mapping |
| `not_found` | HTTP 404 | The file expired or was deleted; try again |
| `payload_too_large` | HTTP 413 | The content is too large; translate less at once |
| `too_many_requests` | HTTP 429 after all retries | Too many requests; try again in a moment |
| `service_unavailable` | HTTP 5xx | Supertext is unavailable; try again later |
| `unexpected_status` | Any other HTTP status | Unexpected answer from Supertext (show `Status`/`Detail`) |
| `transport_error` | No answer (DNS, TLS, refused, request timeout) | Supertext can't be reached; check the server's network |
| `no_file_id` | Upload answered without a file id | Unexpected answer from Supertext; try again |
| `translation_error` | File status `error` | Supertext couldn't translate this content |
| `quota_exceeded` | File status `limit_exceeded` | The account's translation limit is used up |
| `file_deleted` | File status `deleted` | The file was deleted before download; try again |
| `timeout` | Not done within `Timeout` | The translation is taking too long; try again or translate less at once |
| `incomplete_response` | Empty download or missing texts (`Detail`: `missing: key1, key2`) | The translation came back incomplete; try again |

## Options

`SupertextClientOptions` (all optional):

| Option | Default | |
| --- | --- | --- |
| `BaseUrl` | `https://api.supertext.com/v1/` | A trailing slash is added if missing |
| `PollInterval` | 2 s | Wait between status polls |
| `Timeout` | 180 s | Give up waiting for a file |
| `RequestTimeout` | 30 s | Per HTTP request, including reading the answer |
| `MaxRetries` | 4 | Retries after HTTP 429 (`Retry-After` in seconds, else 1/2/4/8/16 s … with jitter; max. 30 s) |
| `UserAgent` | none | Appended to `supertext-sdk-dotnet`, e.g. `My-CMS-Plugin/1.0.0` |
| `HttpClient` | shared internal client | E.g. from `IHttpClientFactory`; its `Timeout` should be ≥ `RequestTimeout` |
| `Sleep` | `Task.Delay` | Replace in tests |
| `TimeProvider` | `TimeProvider.System` | Clock for the polling timeout; replace in tests |

Also public: `SupertextClient.AuthHeader(key)`, `SupertextClient.RetryDelayMs(attempt, retryAfter)`, `HtmlDocument.Build` / `Parse` if you build documents yourself.

## Testing against the stand-in

The repository's stand-in API (`stand-in/stand-in.mjs`, Node 18+) behaves like the live API where integrations went wrong before, and prefixes each translated segment with `[de-DE]`:

```sh
node stand-in/stand-in.mjs          # http://127.0.0.1:8765/v1/
```

Point `BaseUrl` at it. For unit tests, pass an `HttpClient` with your own `HttpMessageHandler`, plus `Sleep` and `TimeProvider` fakes (see `tests/`).

## Development

From the repository root:

```sh
dotnet build sdk/dotnet -warnaserror
dotnet test sdk/dotnet                       # unit tests + spec/*.json vectors
dotnet build sdk/dotnet/examples/Conformance -c Release
node stand-in/conformance.mjs -- dotnet sdk/dotnet/examples/Conformance/bin/Release/net8.0/Conformance.dll
dotnet pack sdk/dotnet/src/Supertext.Sdk -c Release -o sdk/dotnet/artifacts
```

## License

MIT
