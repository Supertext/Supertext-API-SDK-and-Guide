# supertext-sdk (TypeScript)

Client for the Supertext AI translation API: translate HTML and texts, with language codes, rate-limit retries and HTML packing handled for you.

- Node 18+, Deno and Bun; ESM with type declarations; **no runtime dependencies** (uses the global `fetch`).
- Everything Supertext's CMS, PIM and shop plugins learned against the live API is built in: the auth header, language codes, the exact multipart upload, HTTP 429 retries, polling, cleanup, and packing many texts into one document.

For the protocol and integration advice (what to send as one segment, how to show errors, how to test), see the [integration guide](../../docs/integration-guide.md).

## Install

```sh
npm install supertext-sdk
```

You need an API key. No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

## Quick start

Translate texts and HTML fragments, keyed by your own ids:

```ts
import { SupertextClient } from 'supertext-sdk'

const client = new SupertextClient(process.env.SUPERTEXT_API_KEY ?? '', {
  userAgent: 'My-CMS-Supertext-Plugin/1.0.0', // appended to the User-Agent
})

const translated = await client.translateTexts(
  {
    title: 'Hello & welcome',                                        // plain text
    body: { text: '<p>Our <b>new</b> shop opens on Monday.</p>', html: true },
    teaser: { text: 'Line 1\nLine 2' },                              // plain text, line breaks kept
  },
  'de-CH',
  { sourceLang: 'en', politeness: 'more' },
)
// { title: 'Hallo und willkommen', body: '<p>Unser <b>neuer</b> …</p>', teaser: 'Zeile 1\nZeile 2' }
```

- Keys come back in the same order. Pass a `Map` to get a `Map` back (a plain object reorders keys like `"10"`, `"2"`).
- Plain texts are escaped and read back as plain text; HTML fragments go as they are. Send a whole paragraph (with its inline formatting and links) as **one** segment: Supertext translates each segment on its own.
- Blank texts come back unchanged without a request. Many texts go into one document (up to 900,000 characters, `maxCharacters`); bigger sets are split and translated one after the other.

Translate a whole HTML document:

```ts
const html = await client.translateHtml('<!DOCTYPE html><html>…</html>', 'fr', { sourceLang: 'en' })
```

Lower-level steps, if you need them: `submit()`, `getStatus()`, `waitUntilDone()`, `download()`, `delete()` (never throws). `validateApiKey()` checks the key without cost (for a "test connection" button).

## Language codes

Supertext wants the **source as a bare language** (`en`) and the **target with a region** (`de-DE`, `en-US`); anything else is HTTP 400 `INVALID_LANGUAGE_PAIR`. The client fixes this for you: `de` is sent as `de-DE`, `en-US` as source as `en`, `fr_ch` as `fr-CH`.

If your users need a different region (Swiss German instead of German German, say), let them set it per language and apply it yourself:

```ts
import { LanguageCodes } from 'supertext-sdk'

const overrides = { de: 'de-CH', no: 'nb-NO' } // CMS locale → Supertext code
await client.translateTexts(texts, LanguageCodes.target(cmsLocale, overrides))

LanguageCodes.normalize('zh_hant_tw') // 'zh-Hant-TW'
LanguageCodes.source('en-GB')         // 'en'
LanguageCodes.target('pt')            // 'pt-PT'
LanguageCodes.isValid('german')       // false (for validating a settings field)
LanguageCodes.DEFAULT_REGIONS         // { de: 'de-DE', en: 'en-US', … }
```

## Politeness

`politeness: 'more'` asks for formal address (Sie, vous, Lei), `'less'` for informal (du, tu). `'default'` (or leaving it out) lets Supertext decide and sends nothing.

## Errors

Every failure is a `SupertextError` with a stable `code` (also as constants: `ErrorCode.QuotaExceeded`), an English `message`, and where known `status` (HTTP), `detail` (the API's own text, at most 200 characters), `apiErrorCode` (e.g. `INVALID_LANGUAGE_PAIR`), `sourceLang`/`targetLang`. The API key never appears in any of them. Translate the code for your users:

| Code | What to tell the user |
| --- | --- |
| `missing_api_key` | No API key is configured (nothing was sent). Show where to enter it and the account/API key links above. |
| `authentication_failure` | The API key was rejected (HTTP 401/403). Check or regenerate it (links above). |
| `invalid_language_pair` | Supertext doesn't support this language pair; check the language settings. |
| `not_found` | The translation file is gone (expired or deleted). Try again. |
| `payload_too_large` | The content is too large; translate less at once (or lower `maxCharacters`). |
| `too_many_requests` | Too many requests right now (after 4 retries). Try again in a minute. |
| `service_unavailable` | Supertext is unavailable (HTTP 5xx). Try again later. |
| `unexpected_status` | Unexpected answer from Supertext; show `status` and `detail`. |
| `transport_error` | Supertext couldn't be reached (network, DNS, TLS, timeout); `detail` has the reason. |
| `no_file_id` | Supertext didn't accept the document. Try again. |
| `translation_error` | Supertext couldn't translate the document. |
| `quota_exceeded` | The account's translation limit is used up; upgrade the subscription. |
| `file_deleted` | The file was deleted before the download. Try again. |
| `timeout` | The translation took too long (`timeoutMs`). Try again or translate less at once. |
| `incomplete_response` | The translation came back empty or incomplete (`detail` lists missing keys). Try again. |

```ts
import { SupertextError } from 'supertext-sdk'

try {
  await client.translateTexts(texts, target)
} catch (error) {
  if (error instanceof SupertextError) showMessage(t(`supertext.error.${error.code}`), error.detail)
  else throw error
}
```

## Options

`new SupertextClient(apiKey, options)`; a blank key doesn't throw here, only the first request does (`missing_api_key`). A pasted `Supertext-Auth-Key ` prefix is stripped.

| Option | Default | |
| --- | --- | --- |
| `baseUrl` | `https://api.supertext.com/v1/` (`LIVE_BASE_URL`) | e.g. the stand-in for tests |
| `pollIntervalMs` | `2000` | wait between status polls |
| `timeoutMs` | `180000` | give up waiting for a translation (`timeout`) |
| `requestTimeoutMs` | `30000` | per HTTP request (`transport_error`) |
| `maxRetries` | `4` | retries after HTTP 429 (Retry-After, else 1/2/4/8 s with jitter) |
| `userAgent` | none | appended: `supertext-sdk-typescript My-Plugin/1.0.0` |
| `fetch` | global `fetch` | proxies, tests |
| `sleep`, `now` | timers, `Date.now` | tests |

The client builds the multipart upload itself instead of using `FormData`: the API answers HTTP 415 unless the file part's Content-Type is exactly `text/html`, and some runtimes (Bun) add a charset to it.

Also exported: `authHeader()`, `retryDelayMs()`, `HtmlDocument.build()/parse()` (the document format), `planBatches()`, `errorForStatus()`, `errorForFileStatus()`.

## Testing against the stand-in

The repo's stand-in API (`stand-in/stand-in.mjs`, Node, no dependencies) behaves like the live API where integrations went wrong before, and returns each text with `[<target>] ` in front:

```sh
node stand-in/stand-in.mjs          # http://127.0.0.1:8765/v1/
```

```ts
const client = new SupertextClient('test-key', { baseUrl: 'http://127.0.0.1:8765/v1/' })
```

## Development

```sh
cd sdk/typescript
npm ci
npm run typecheck   # tsc --noEmit
npm test            # Vitest: unit tests and the shared vectors in ../../spec
npm run build       # tsc → dist/
npm run conformance # builds, then runs examples/conformance.mjs against the stand-in
```

The conformance run from the repo root: `node stand-in/conformance.mjs -- node sdk/typescript/examples/conformance.mjs` (after `npm run build`).

## License

MIT
