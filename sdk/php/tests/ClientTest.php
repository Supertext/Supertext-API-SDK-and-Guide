<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Client;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\TransportException;
use Supertext\Sdk\Segment;
use Supertext\Sdk\SupertextException;

final class ClientTest extends TestCase
{
    private const KEY = 'secret-key-123';

    /** @var list<float> seconds slept */
    private array $sleeps = [];

    private float $now = 0.0;

    /** A client whose sleep advances a fake clock. */
    private function client(FakeTransport $transport, mixed ...$options): Client
    {
        $this->sleeps = [];
        $this->now = 0.0;

        return new Client(
            $options['apiKey'] ?? self::KEY,
            transport: $transport,
            sleep: function (float $seconds): void {
                $this->sleeps[] = $seconds;
                $this->now += $seconds;
            },
            clock: fn (): float => $this->now,
            userAgent: $options['userAgent'] ?? null,
            pollInterval: $options['pollInterval'] ?? 2.0,
            timeout: $options['timeout'] ?? 180.0,
            maxRetries: $options['maxRetries'] ?? 4,
            baseUrl: $options['baseUrl'] ?? 'https://api.supertext.com/v1/',
        );
    }

    private static function ok(string $body = '{}'): HttpResponse
    {
        return new HttpResponse(200, $body);
    }

    public function testUploadFieldsOrderAndContentType(): void
    {
        $transport = FakeTransport::queue([self::ok('{"file_id":"abc"}')]);
        $id = $this->client($transport)->submit('<p>Hi</p>', 'de_ch', 'en-GB', 'more');

        self::assertSame('abc', $id);
        $request = $transport->requests[0];
        self::assertSame('POST', $request->method);
        self::assertSame('https://api.supertext.com/v1/translate/ai/file', $request->url);
        [$fields, $file, $names, $fileHeaders] = Multipart::parse($request);
        self::assertSame(['target_lang', 'source_lang', 'politeness', 'file'], $names);
        self::assertSame(['target_lang' => 'de-CH', 'source_lang' => 'en', 'politeness' => 'more'], $fields);
        self::assertSame("Content-Disposition: form-data; name=\"file\"; filename=\"content.html\"\r\nContent-Type: text/html", $fileHeaders);
        self::assertSame('<p>Hi</p>', $file);
    }

    public function testUploadWithoutSourceAndDefaultPoliteness(): void
    {
        $transport = FakeTransport::queue([self::ok('{"file_id":"abc"}')]);
        $this->client($transport)->submit('<p>Hi</p>', 'de');
        [$fields, , $names] = Multipart::parse($transport->requests[0]);
        self::assertSame(['target_lang', 'file'], $names);
        self::assertSame(['target_lang' => 'de-DE'], $fields);

        $transport = FakeTransport::queue([self::ok('{"file_id":"abc"}')]);
        $this->client($transport)->submit('<p>Hi</p>', 'fr', '', 'less');
        [$fields] = Multipart::parse($transport->requests[0]);
        self::assertSame(['target_lang' => 'fr-FR', 'politeness' => 'less'], $fields);
    }

    public function testOtherPolitenessValuesAreNotSent(): void
    {
        foreach (['default', 'formal', ''] as $politeness) {
            $transport = FakeTransport::queue([self::ok('{"file_id":"abc"}')]);
            $this->client($transport)->submit('<p>Hi</p>', 'de', null, $politeness);
            [$fields] = Multipart::parse($transport->requests[0]);
            self::assertSame(['target_lang' => 'de-DE'], $fields);
        }
    }

    public function testMissingFileId(): void
    {
        foreach (['{}', '{"file_id":""}', 'not json', '{"file_id":null}'] as $body) {
            try {
                $this->client(FakeTransport::queue([self::ok($body)]))->submit('<p>Hi</p>', 'de');
                self::fail('expected no_file_id for ' . $body);
            } catch (SupertextException $e) {
                self::assertSame(SupertextException::NO_FILE_ID, $e->errorCode);
            }
        }
    }

    public function testHeaders(): void
    {
        $transport = FakeTransport::queue([self::ok(), self::ok()]);
        $this->client($transport, apiKey: ' Supertext-Auth-Key ' . self::KEY . ' ')->validateApiKey();
        $this->client($transport, userAgent: 'Typo3-Supertext-Translation/1.2.0')->validateApiKey();

        [$plain, $custom] = $transport->requests;
        self::assertSame('GET', $plain->method);
        self::assertSame('https://api.supertext.com/v1/features', $plain->url);
        self::assertSame('Supertext-Auth-Key ' . self::KEY, $plain->headers['Authorization']);
        self::assertSame('application/json', $plain->headers['Accept']);
        self::assertSame('supertext-sdk-php', $plain->headers['User-Agent']);
        self::assertSame('supertext-sdk-php Typo3-Supertext-Translation/1.2.0', $custom->headers['User-Agent']);
        self::assertSame(30.0, $plain->timeout);
    }

    public function testBaseUrlGetsOneTrailingSlash(): void
    {
        $transport = FakeTransport::queue([self::ok()]);
        $this->client($transport, baseUrl: 'http://127.0.0.1:8765/v1')->validateApiKey();
        self::assertSame('http://127.0.0.1:8765/v1/features', $transport->requests[0]->url);
    }

    public function testRetriesRateLimitWithRetryAfter(): void
    {
        $transport = FakeTransport::queue([
            new HttpResponse(429, '{"error_code":"RATE_LIMIT_EXCEEDED"}', ['retry-after' => '3']),
            new HttpResponse(429, '', ['Retry-After' => '0.5']),
            self::ok(),
        ]);
        $this->client($transport)->validateApiKey();
        self::assertCount(3, $transport->requests);
        self::assertSame([3.0, 0.5], $this->sleeps);
    }

    public function testRateLimitExhausted(): void
    {
        $transport = FakeTransport::queue(array_fill(0, 5, new HttpResponse(429, '{"error_code":"RATE_LIMIT_EXCEEDED"}')));
        try {
            $this->client($transport)->validateApiKey();
            self::fail('expected too_many_requests');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TOO_MANY_REQUESTS, $e->errorCode);
            self::assertSame(429, $e->status);
            self::assertSame('RATE_LIMIT_EXCEEDED', $e->apiErrorCode);
        }
        self::assertCount(5, $transport->requests);
        // Backoff 1, 2, 4, 8 s plus jitter below 250 ms.
        self::assertCount(4, $this->sleeps);
        foreach ([1, 2, 4, 8] as $i => $base) {
            self::assertGreaterThanOrEqual($base, $this->sleeps[$i]);
            self::assertLessThan($base + 0.25, $this->sleeps[$i]);
        }
    }

    public function testMaxRetriesOption(): void
    {
        $transport = FakeTransport::queue(array_fill(0, 2, new HttpResponse(429)));
        $this->expectExceptionObject(new SupertextException(SupertextException::TOO_MANY_REQUESTS, 429));
        try {
            $this->client($transport, maxRetries: 1)->validateApiKey();
        } finally {
            self::assertCount(2, $transport->requests);
        }
    }

    public function testPollingUntilDone(): void
    {
        $transport = FakeTransport::queue([
            self::ok('{"status":"translating"}'),
            self::ok('{"status":"queued"}'),
            self::ok('{}'),
            self::ok('{"status":"done"}'),
        ]);
        $this->client($transport, pollInterval: 0.5)->waitUntilDone('a/b c');
        self::assertSame([0.5, 0.5, 0.5], $this->sleeps);
        self::assertSame('https://api.supertext.com/v1/translate/ai/file/a%2Fb%20c/status', $transport->requests[0]->url);
    }

    public function testPollingTimeout(): void
    {
        // Timeout 5 s, interval 2 s: polls at 0, 2, 4; a poll at 6 would start after the timeout.
        $transport = FakeTransport::handler(static fn (): HttpResponse => self::ok('{"status":"translating"}'));
        try {
            $this->client($transport, pollInterval: 2.0, timeout: 5.0)->waitUntilDone('f');
            self::fail('expected timeout');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TIMEOUT, $e->errorCode);
            self::assertNull($e->status);
        }
        self::assertCount(3, $transport->requests);
        self::assertSame([2.0, 2.0], $this->sleeps);

        // Exactly at the timeout still polls: 0, 2, 4, 6.
        $transport = FakeTransport::handler(static fn (): HttpResponse => self::ok('{"status":"translating"}'));
        try {
            $this->client($transport, pollInterval: 2.0, timeout: 6.0)->waitUntilDone('f');
        } catch (SupertextException) {
        }
        self::assertCount(4, $transport->requests);
    }

    public function testTranslateHtmlHappyPath(): void
    {
        $transport = FakeTransport::queue([
            self::ok('{"file_id":"f1"}'),
            self::ok('{"status":"translating"}'),
            self::ok('{"status":"done"}'),
            self::ok('<p>Hallo</p>'),
            new HttpResponse(204),
        ]);
        self::assertSame('<p>Hallo</p>', $this->client($transport)->translateHtml('<p>Hello</p>', 'de', 'en'));
        self::assertSame([
            'POST translate/ai/file',
            'GET translate/ai/file/f1/status',
            'GET translate/ai/file/f1/status',
            'GET translate/ai/file/f1/translation',
            'DELETE translate/ai/file/f1',
        ], $transport->calls());
    }

    public function testFileIsDeletedAfterStatusError(): void
    {
        $transport = FakeTransport::queue([
            self::ok('{"file_id":"f1"}'),
            self::ok('{"status":"error"}'),
            new HttpResponse(500, 'boom'), // the delete fails too: must not hide the real error
        ]);
        try {
            $this->client($transport)->translateHtml('<p>Hello</p>', 'de');
            self::fail('expected translation_error');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TRANSLATION_ERROR, $e->errorCode);
        }
        self::assertSame('DELETE translate/ai/file/f1', $transport->calls()[2]);
    }

    public function testFileIsDeletedAfterTimeoutAndEmptyDownload(): void
    {
        $transport = FakeTransport::queue([self::ok('{"file_id":"f1"}'), self::ok('{"status":"done"}'), self::ok(" \n "), new TransportException('down')]);
        try {
            $this->client($transport)->translateHtml('<p>Hello</p>', 'de');
            self::fail('expected incomplete_response');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::INCOMPLETE_RESPONSE, $e->errorCode);
        }
        self::assertSame('DELETE translate/ai/file/f1', $transport->calls()[3]);

        $transport = FakeTransport::handler(static fn ($r): HttpResponse => match ($r->method) {
            'POST' => self::ok('{"file_id":"f2"}'),
            'DELETE' => new HttpResponse(404),
            default => self::ok('{"status":"translating"}'),
        });
        try {
            $this->client($transport, timeout: 1.0)->translateHtml('<p>Hello</p>', 'de');
            self::fail('expected timeout');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TIMEOUT, $e->errorCode);
        }
        self::assertSame('DELETE translate/ai/file/f2', $transport->calls()[\count($transport->requests) - 1]);
    }

    public function testDeleteNeverThrows(): void
    {
        $this->client(FakeTransport::queue([new HttpResponse(500)]))->delete('f');
        $this->client(FakeTransport::queue([new \RuntimeException('network down')]))->delete('f');
        $this->client(FakeTransport::queue([]), apiKey: '')->delete('f');
        $this->addToAssertionCount(1);
    }

    public function testNoUploadNoDelete(): void
    {
        $transport = FakeTransport::queue([new HttpResponse(401, '')]);
        try {
            $this->client($transport)->translateHtml('<p>Hello</p>', 'de');
            self::fail('expected authentication_failure');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::AUTHENTICATION_FAILURE, $e->errorCode);
            self::assertSame(401, $e->status);
        }
        self::assertCount(1, $transport->requests);
    }

    public function testMissingKeySendsNothing(): void
    {
        foreach (['', '   ', 'Supertext-Auth-Key '] as $key) {
            $transport = FakeTransport::queue([]);
            try {
                $this->client($transport, apiKey: $key)->translateTexts(['a' => 'Hello'], 'de');
                self::fail('expected missing_api_key');
            } catch (SupertextException $e) {
                self::assertSame(SupertextException::MISSING_API_KEY, $e->errorCode);
                self::assertStringContainsString('https://www.supertext.com/en/integrations/api', $e->getMessage());
            }
            self::assertSame([], $transport->requests);
        }
    }

    public function testTransportError(): void
    {
        $transport = FakeTransport::queue([new TransportException('Could not resolve host: api.supertext.com')]);
        try {
            $this->client($transport)->validateApiKey();
            self::fail('expected transport_error');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TRANSPORT_ERROR, $e->errorCode);
            self::assertNull($e->status);
            self::assertSame('Could not resolve host: api.supertext.com', $e->detail);
            self::assertInstanceOf(TransportException::class, $e->getPrevious());
        }
    }

    public function testKeyNeverAppearsInErrors(): void
    {
        $cases = [
            new HttpResponse(401, 'Invalid key Supertext-Auth-Key ' . self::KEY),
            new HttpResponse(500, '<p>' . self::KEY . '</p>'),
            new TransportException('proxy said: ' . self::KEY),
        ];
        foreach ($cases as $answer) {
            try {
                $this->client(FakeTransport::queue([$answer]))->validateApiKey();
                self::fail('expected an error');
            } catch (SupertextException $e) {
                self::assertStringNotContainsString(self::KEY, $e->getMessage());
                self::assertStringNotContainsString(self::KEY, (string) $e->detail);
                self::assertStringNotContainsString(self::KEY, (string) $e);
            }
        }
        self::assertStringNotContainsString(self::KEY, print_r($this->client(FakeTransport::queue([])), true));
    }

    public function testTranslateTexts(): void
    {
        $api = new EchoApi(static fn (string $html): string => str_replace(['Hello', 'World'], ['Hallo', 'Welt'], $html));
        $result = $this->client($api->transport)->translateTexts([
            'title' => 'Hello & <you>',
            'blank' => "  \n",
            'body' => Segment::html('<p>World <b>bold</b></p>'),
            'lines' => new Segment("Hello\nWorld"),
            7 => 'numeric key',
        ], 'de', 'en-US', 'less');

        self::assertSame([
            'title' => 'Hallo & <you>',
            'blank' => "  \n",
            'body' => '<p>Welt <b>bold</b></p>',
            'lines' => "Hallo\nWelt",
            7 => 'numeric key',
        ], $result);
        self::assertCount(1, $api->documents);
        self::assertSame(['target_lang' => 'de-DE', 'source_lang' => 'en', 'politeness' => 'less'], $api->fields[0]);
        self::assertSame(1, $api->deleted);
    }

    public function testTranslateTextsSeveralDocumentsInOrder(): void
    {
        $api = new EchoApi();
        $result = $this->client($api->transport)->translateTexts(['a' => '12345', 'b' => '12345', 'c' => '1'], 'de', maxCharacters: 10);
        self::assertSame(['a' => '12345', 'b' => '12345', 'c' => '1'], $result);
        self::assertCount(2, $api->documents);
        self::assertSame(2, $api->deleted);
    }

    public function testTranslateTextsNothingToSend(): void
    {
        $transport = FakeTransport::queue([]);
        self::assertSame(['a' => '', 'b' => ' '], $this->client($transport)->translateTexts(['a' => '', 'b' => Segment::html(' ')], 'de'));
        self::assertSame([], $this->client($transport)->translateTexts([], 'de'));
        self::assertSame([], $transport->requests);
    }

    public function testTranslateTextsMissingSegments(): void
    {
        // The "translation" loses the segments with ids 1 and 2.
        $api = new EchoApi(static fn (string $html): string => (string) preg_replace('/<div data-st-id="[12]">.*?<\/div>/', '', $html));
        try {
            $this->client($api->transport)->translateTexts(['a' => 'one', 'b' => 'two', 'c' => 'three'], 'de');
            self::fail('expected incomplete_response');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::INCOMPLETE_RESPONSE, $e->errorCode);
            self::assertSame('missing: b, c', $e->detail);
        }
        self::assertSame(1, $api->deleted);
    }
}
