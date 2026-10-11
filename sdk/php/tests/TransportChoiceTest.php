<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Attributes\RequiresPhpExtension;
use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Client;
use Supertext\Sdk\Http\CurlTransport;
use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\HttpTransport;

/**
 * ext-curl is optional: only the built-in CurlTransport needs it. CI also runs this suite without curl.
 */
final class TransportChoiceTest extends TestCase
{
    public function testOwnTransportWorksWithoutCurl(): void
    {
        $transport = new class () implements HttpTransport {
            public int $calls = 0;

            public function send(HttpRequest $request): HttpResponse
            {
                $this->calls++;

                return new HttpResponse(200, '{}');
            }
        };
        $client = new Client('key', transport: $transport);
        $client->validateApiKey();
        self::assertSame(1, $transport->calls);
    }

    public function testWithoutCurlTheDefaultTransportSaysWhatToDo(): void
    {
        if (CurlTransport::isAvailable()) {
            self::markTestSkipped('ext-curl is loaded (the curl-less CI job runs this)');
        }
        $this->expectException(\LogicException::class);
        $this->expectExceptionMessage('pass your own Supertext\Sdk\Http\HttpTransport');
        new Client('key');
    }

    #[RequiresPhpExtension('curl')]
    public function testWithCurlTheDefaultTransportIsCurl(): void
    {
        self::assertTrue(CurlTransport::isAvailable());
        $client = new Client('key');
        $property = new \ReflectionProperty(Client::class, 'transport');
        self::assertInstanceOf(CurlTransport::class, $property->getValue($client));
    }
}
