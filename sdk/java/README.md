# Supertext SDK for Java

Client for the Supertext AI translation API: translate HTML and texts, with language codes, rate-limit retries and HTML packing handled for you.

Java 11+, no runtime dependencies (uses `java.net.http.HttpClient`). For the protocol and the lessons behind it, see the [integration guide](../../docs/integration-guide.md).

## Install

```xml
<dependency>
  <groupId>com.supertext</groupId>
  <artifactId>supertext-sdk</artifactId>
  <version>0.1.0</version>
</dependency>
```

Gradle: `implementation("com.supertext:supertext-sdk:0.1.0")`.

## API key

No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

Pass the key as administrators paste it: a leading `Supertext-Auth-Key ` is stripped. Keep it out of your code, e.g. in `SUPERTEXT_API_KEY`.

## Quick start

```java
import com.supertext.sdk.*;
import java.util.LinkedHashMap;
import java.util.Map;

SupertextClient client = SupertextClient.builder(System.getenv("SUPERTEXT_API_KEY"))
        .userAgent("MyCms-Supertext/1.0.0")
        .build();

// Texts: packed into one HTML document (or several, if large), read back by key.
Map<String, Segment> segments = new LinkedHashMap<>();
segments.put("title", Segment.text("Hello & welcome"));
segments.put("body", Segment.html("<p>Our <b>new</b> shop opens on Monday.</p>"));
Map<String, String> german = client.translateTexts(segments, "de-CH", "en", Politeness.MORE);
german.get("title"); // "Hallo und willkommen"

// A whole HTML document: upload, wait, download, delete.
String html = client.translateHtml("<p data-st-id=\"1\">Good morning</p>", "fr", "en");
```

Plain-text segments are escaped and keep their line breaks; HTML segments are sent as they are. Keep a whole paragraph in **one** segment with formatting as inline tags (`<b>`, `<i>`, `<a href>`): Supertext translates every segment on its own, so a segment per formatted run breaks sentences apart. Blank texts come back unchanged and aren't sent.

The calls block and throw `InterruptedException` when the thread is interrupted; the uploaded file is still deleted. Documents are translated one after the other (the API limits requests per second).

Lower-level calls: `validateApiKey()` (free check of the key; throws), `checkApiKey()` (never throws, see below), `submit`, `getStatus`, `waitUntilDone`, `download`, `delete` (never throws).

## Language codes

Supertext wants the **source as a bare language** (`en`) and the **target with a region** (`de-DE`); anything else is `invalid_language_pair`. The client fixes codes for you: targets get a default region (`de` → `de-DE`, `fr_ch` → `fr-CH`), sources are cut to the language (`en-US` → `en`). An empty source means auto-detect.

If your CMS's locales don't map one to one, pass overrides (CMS locale → Supertext code) yourself:

```java
Map<String, String> overrides = Map.of("de", "de-CH", "no", "nb-NO");
String target = LanguageCodes.target(cmsLocale, overrides);   // "de" → "de-CH"
String source = LanguageCodes.source(cmsLocale, overrides);   // "no" → "nb"
LanguageCodes.isValid("de_ch");                                // true
LanguageCodes.normalize(" zh_hant_tw ");                       // "zh-Hant-TW"
LanguageCodes.normalize("de_DE_formal");                       // "de-DE" (variants dropped; formal address: Politeness)
```

## Politeness

`Politeness.DEFAULT` (not sent), `MORE` (formal: Sie, vous, Lei) or `LESS` (informal: du, tu). `Politeness.fromValue("more")` parses settings; any other value (including `default`) gives `DEFAULT`, so nothing is sent.

## Errors

Every failure is a `SupertextException` (unchecked) with `getCode()` (`ErrorCode`), `getStatus()` (HTTP status or `null`), `getDetail()`, `getApiErrorCode()` and, for language pairs, `getSourceLang()`/`getTargetLang()`. The message is English, for logs; it never contains the API key. `ErrorCode.value()` gives the string shared by all Supertext SDKs.

| Code | Tell the user |
| --- | --- |
| `missing_api_key` | No API key configured (add the account and key links above). Nothing was sent. |
| `authentication_failure` | The API key was rejected: check it, or generate a new one (links above). |
| `invalid_language_pair` | Supertext doesn't support this language combination. Check the language settings. |
| `not_found` | The translation job no longer exists. Try again. |
| `payload_too_large` | The content is too large to translate in one go. |
| `too_many_requests` | Supertext is busy. Try again in a moment. |
| `service_unavailable` | Supertext is temporarily unavailable. Try again later. |
| `unexpected_status` | Supertext answered unexpectedly (HTTP status in the details). |
| `transport_error` | Supertext couldn't be reached (network, DNS, TLS, timeout). |
| `no_file_id` | Supertext didn't accept the document. Try again. |
| `translation_error` | Supertext couldn't translate the content. |
| `quota_exceeded` | The Supertext account's translation limit is used up. |
| `file_deleted` | The translation was deleted before it could be fetched. Try again. |
| `timeout` | The translation took too long. Try again, or raise the timeout. |
| `incomplete_response` | The translation came back incomplete. Try again. |

## API key check, version reports and ping

Behaviour shared by all Supertext SDKs: `spec/reporting.json`.

**Key check.** `client.checkApiKey()` calls `GET features` (at most 10 s, the usual 429 retries) and never throws. It returns an immutable `ApiKeyCheck`: `getStatus()` (`OK` 2xx, `INVALID` 401/403, `MISSING` blank key, nothing sent, `UNREACHABLE` no answer or 5xx, `ERROR` anything else), `getCheckedAt()` (`2026-10-13T08:00:00Z`), `getHttpStatus()`, `getErrorCode()` and `getFingerprint()` (SHA-256 of the key, to notice a changed key; `ApiKeyCheck.fingerprint(key)`). Show the result on the settings page. If it is `INVALID` or `MISSING`, add the links: no Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

**Version reports.** `InstallationReporter` sends the plugin, system, runtime and SDK versions and the key check's result (never the key) to the Supertext Commando Center. It is stateless: load the stored `ReporterState`, pass it in, store the state you get back. No call throws; a failed report waits an hour.

```java
InstallationReporter reporter = InstallationReporter.builder()
        .enabled(Reporting.reportsEnabled(settings.versionReports())) // Boolean setting (null = on); SUPERTEXT_VERSION_REPORTS wins
        .siteUrl("https://cms.example.ch")
        .plugin("Magnolia-Supertext-Translation", "0.2.0")            // repo name, official version
        .system("Magnolia", "6.4.10")
        .callbackUrl("https://cms.example.ch/.supertext/ping")        // omit for hosted apps without one
        .build();                                                     // runtime: Java + java.version; reportUrl: SUPERTEXT_REPORT_URL or the live center

ReporterState state = ReporterState.fromJson(stored);              // null or broken → empty
ReporterResult r = reporter.afterSettingsSaved(state, apiKey);      // on every settings save
store(r.getState().toJson());
r.getCheck();                                                       // the latest ApiKeyCheck
```

| Call | When |
| --- | --- |
| `afterSettingsSaved(state, key)` | Settings saved: checks the key, reports `installed` or `settings_saved` |
| `onRequest(state, key)` | Back-end requests: re-checks a changed key, reports `installed`, `upgraded` or `api_key_check` when due |
| `authenticationFailed(state, key, httpStatus)` | A translation got 401/403: marks the key invalid (no request to Supertext) |
| `testConnection(state, key)` | "Test connection": checks the key, reports a change |
| `reportCheck(state)` | Commands and tests: sends `check` now |

`buildReport(event, state)` / `buildReportJson(...)` give the body; `versions()` the `<plugin>|<system>|<runtime>` string. Options for tests: `transport` (shared with the key check), `apiBaseUrl`, `sleeper`, `clock`. Constants in `Reporting` (`DEFAULT_REPORT_URL`, `REPORT_TIMEOUT`, `KEY_CHECK_TIMEOUT`, `FAILURE_BACKOFF`); `SupertextClient.SDK_VERSION` is this SDK's version.

**Ping.** The callback URL answers the center's `GET <callback>?challenge=<c>` without login:

```java
PingResponse ping = PingResponse.of(request.getParameter("challenge"), "Magnolia-Supertext-Translation", enabled);
response.setStatus(ping.getStatus());                 // 200, 400 (bad challenge) or 404 (reports off)
ping.getHeaders().forEach(response::setHeader);       // Content-Type: application/json, Cache-Control: no-store
response.getWriter().write(ping.getBody());
```

## Options

```java
SupertextClient.builder(apiKey)
        .baseUrl(SupertextClient.LIVE_BASE_URL)   // e.g. a stand-in for tests
        .pollInterval(Duration.ofSeconds(2))       // between status polls
        .timeout(Duration.ofSeconds(180))          // per document
        .requestTimeout(Duration.ofSeconds(30))    // per HTTP request → transport_error
        .maxRetries(4)                             // after HTTP 429 (Retry-After in seconds, else 1/2/4/8/16 s + jitter; at most 30 s)
        .userAgent("MyCms-Supertext/1.0.0")       // appended to "supertext-sdk-java"
        .transport(new JdkHttpTransport(myHttpClient)) // or your own Transport (tests, other HTTP stacks)
        .sleeper(d -> Thread.sleep(d.toMillis()))  // replace in tests
        .clock(System::nanoTime)                   // replace in tests
        .wallClock(Clock.systemUTC())              // checkedAt of checkApiKey(); replace in tests
        .build();
```

`translateTexts(segments, target, source, politeness, maxCharacters)` sets the document size (default `SupertextClient.DEFAULT_MAX_CHARACTERS`, 900,000).

## Testing against the stand-in

The repository's stand-in behaves like the live API where integrations went wrong (auth header, language codes, `text/html`, 429):

```sh
node stand-in/stand-in.mjs            # from the repo root; prints http://127.0.0.1:8765/v1/
```

```java
SupertextClient client = SupertextClient.builder("test-key").baseUrl("http://127.0.0.1:8765/v1/").build();
```

Translations come back with `[de-DE] ` in front of each segment.

## Development

```sh
cd sdk/java
mvn -B verify                                   # compile (release 11), unit tests incl. spec/*.json
node ../../stand-in/conformance.mjs -- java -cp target/classes examples/Conformance.java
```

The tests read the shared vectors from `../../spec/`. Licence: MIT.
