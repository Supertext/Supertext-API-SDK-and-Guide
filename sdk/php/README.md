# Supertext SDK for PHP

Client for the Supertext AI translation API: translate HTML and texts, with language codes, rate-limit retries and HTML packing handled for you.

PHP 8.1+, no other dependencies. The built-in HTTP transport uses ext-curl; if you plug in your own HTTP client (see [Your own HTTP client](#your-own-http-client)), curl isn't needed. For the protocol and the lessons behind it, see the [integration guide](../../docs/integration-guide.md).

## Install

```bash
composer require supertext/supertext-sdk
```

Without Composer, copy `sdk/php` into your project and `require 'path/to/sdk/php/autoload.php';`.

You need an API key. No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).

## Quick start

```php
use Supertext\Sdk\Client;
use Supertext\Sdk\Segment;

$client = new Client(getenv('SUPERTEXT_API_KEY'), userAgent: 'MyCms-Supertext/1.0.0');

// Keyed texts: plain text (escaped, line breaks kept) or HTML fragments.
$translations = $client->translateTexts([
    'title' => 'Hello & welcome',
    'body'  => Segment::html('<p>Our <b>new</b> shop opens on <a href="https://example.com">Monday</a>.</p>'),
], targetLang: 'de', sourceLang: 'en');
// ['title' => 'Hallo und willkommen', 'body' => '<p>Unser <b>neuer</b> Shop …</p>']

// A whole HTML document.
$html = $client->translateHtml('<html><body><p>Hello</p></body></html>', 'fr-CH');
```

`translateTexts()` returns the same keys in the same order. Blank texts come back unchanged and aren't sent; if nothing is left, no request is made. Texts are packed into as few documents as possible (`maxCharacters`, default 900,000) and the documents are translated one after the other. Note that PHP turns numeric string keys (`"1"`) into integers; the result uses the same keys as your array.

Send a whole paragraph, heading or field as **one** segment, with formatting and links as inline tags. Supertext translates every segment on its own: splitting a sentence at its formatting breaks the translation.

The steps are available on their own too: `submit()` → `waitUntilDone()` → `download()` → `delete()` (never throws), `getStatus()`, and `validateApiKey()` (a cost-free check, e.g. for a settings page). `HtmlDocument::build()` / `parse()` pack and unpack segments if you drive the steps yourself.

## Language codes

Supertext wants the **source as a bare language** (`en`) and the **target as a regional code** (`de-DE`); anything else is rejected with `invalid_language_pair`. The client fixes this up for you, so you can pass CMS locales as they are (`de`, `de_ch`, `en-US`, `pt-br`):

```php
use Supertext\Sdk\LanguageCodes;

LanguageCodes::target('de');      // "de-DE" (bare languages get a default region)
LanguageCodes::target('de_ch');   // "de-CH"
LanguageCodes::target('de_DE_formal'); // "de-DE" (variants dropped; formal address: politeness)
LanguageCodes::source('en-GB');   // "en"
LanguageCodes::isValid('german'); // false
```

If a site needs another region than the default (a Swiss site writing `de`), let administrators set overrides and apply them before calling the client:

```php
$overrides = ['de' => 'de-CH', 'no' => 'nb-NO']; // CMS locale → Supertext code
$client->translateTexts($texts, LanguageCodes::target($cmsLocale, $overrides), $cmsSourceLocale);
```

## Politeness

`Politeness::DEFAULT` (default), `Politeness::MORE` (formal: Sie, vous, Lei) or `Politeness::LESS` (informal: du, tu). Only `more` and `less` are sent; any other value lets Supertext decide.

```php
$client->translateTexts($texts, 'de-CH', 'en', Politeness::MORE);
```

## Errors

Everything fails with `Supertext\Sdk\SupertextException`. `$e->errorCode` is one of the constants below (the same strings in every Supertext SDK); `$e->status` (HTTP status), `$e->detail` (short text from the answer), `$e->apiErrorCode` (e.g. `INVALID_LANGUAGE_PAIR`) and, for language pairs, `$e->sourceLang` / `$e->targetLang` help with logs. Messages are English and never contain the API key.

| Code | What to tell the user |
| --- | --- |
| `missing_api_key` | No API key is configured (nothing was sent). Show the account and API key links above. |
| `authentication_failure` | The API key is wrong, revoked or lacks access. Show the account and API key links above. |
| `invalid_language_pair` | Supertext doesn't support this language pair. Check the language settings or overrides. |
| `not_found` | The file expired or was deleted. Try again. |
| `payload_too_large` | The content is too large. Lower `maxCharacters` or split it. |
| `too_many_requests` | Too many requests, even after retries. Try again shortly. |
| `service_unavailable` | Supertext is temporarily unavailable. Try again later. |
| `unexpected_status` | Supertext answered unexpectedly (see `status` and `detail`). |
| `transport_error` | Supertext could not be reached (network, DNS, TLS, timeout). |
| `no_file_id` | The upload got no file id. Try again. |
| `translation_error` | Supertext could not translate the document. |
| `quota_exceeded` | The account's translation limit is used up. Upgrade the subscription. |
| `file_deleted` | The file was deleted before the download. Try again. |
| `timeout` | The translation took longer than `timeout`. Try again, or raise the timeout. |
| `incomplete_response` | The translation was empty or incomplete (`detail` lists missing keys). Try again. |

## Options

All constructor arguments after the key are optional and best passed by name:

| Option | Default | |
| --- | --- | --- |
| `baseUrl` | `https://api.supertext.com/v1/` | API base URL (a trailing slash is added) |
| `pollInterval` | `2.0` | Seconds between status polls |
| `timeout` | `180.0` | Seconds to wait for a translation, from the first poll |
| `requestTimeout` | `30.0` | Seconds per HTTP request |
| `maxRetries` | `4` | Retries after HTTP 429 (waits `Retry-After`, else 1, 2, 4, 8 s plus jitter; at most 30 s) |
| `userAgent` | `null` | Appended to `User-Agent: supertext-sdk-php`, e.g. `MyCms-Supertext/1.0.0` |
| `transport` | `CurlTransport` (needs ext-curl) | Any `Http\HttpTransport`, e.g. wrapping your framework's HTTP client ([below](#your-own-http-client)) |
| `sleep`, `clock` | real time | Injectable for tests |

A blank key doesn't fail in the constructor, only on the first request (`missing_api_key`), so you can create the client before the integration is configured. Long translations block the PHP process while polling; run them in a queue or CLI job, or raise `max_execution_time`.

## Your own HTTP client

CMSs and frameworks usually bring an HTTP client with their proxy, TLS and logging settings (Contao, Symfony and Craft use Symfony HttpClient or Guzzle). Wrap it in an `HttpTransport` and pass it as `transport`; then ext-curl isn't needed. Without curl and without a transport, `new Client()` throws a `LogicException` saying so.

Return every HTTP answer, including 4xx and 5xx, as an `HttpResponse`; throw only when there is no answer at all (the client turns that into `transport_error`). Example for Symfony HttpClient:

```php
use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\HttpTransport;
use Symfony\Contracts\HttpClient\HttpClientInterface;

final class SymfonyHttpTransport implements HttpTransport
{
    public function __construct(private readonly HttpClientInterface $http)
    {
    }

    public function send(HttpRequest $request): HttpResponse
    {
        $response = $this->http->request($request->method, $request->url, [
            'headers' => $request->headers,
            'body' => $request->body ?? '',
            'max_duration' => $request->timeout,
        ]);

        // false: return 4xx/5xx answers instead of throwing.
        $headers = array_map(static fn (array $values): string => implode(', ', $values), $response->getHeaders(false));

        return new HttpResponse($response->getStatusCode(), $response->getContent(false), $headers);
    }
}

$client = new Client($apiKey, transport: new SymfonyHttpTransport($httpClient));
```

For Guzzle, send with `'http_errors' => false` and `'timeout' => $request->timeout`, and build the `HttpResponse` from the status, `(string) $response->getBody()` and the headers the same way.

## Testing against the stand-in

The repository ships a stand-in API (Node 18+) that behaves like the live API where integrations went wrong before:

```bash
node stand-in/stand-in.mjs   # prints http://127.0.0.1:8765/v1/
```

```php
$client = new Client('any-key', baseUrl: 'http://127.0.0.1:8765/v1/', pollInterval: 0.1);
```

Texts come back with `[de-DE] ` in front. In unit tests, pass a fake `transport` and a no-op `sleep` instead.

## Development

From the repository root:

```bash
composer install
vendor/bin/phpunit -c sdk/php/phpunit.xml.dist
vendor/bin/phpstan analyse -c sdk/php/phpstan.neon
find sdk/php -name '*.php' -not -path '*/vendor/*' -print0 | xargs -0 -n1 php -l
node stand-in/conformance.mjs -- php sdk/php/examples/conformance.php
```

The tests run every case in `spec/` (the test vectors shared by all Supertext SDKs).

## License

MIT
