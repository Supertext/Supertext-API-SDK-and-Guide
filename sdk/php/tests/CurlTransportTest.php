<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Attributes\RequiresPhpExtension;
use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Client;
use Supertext\Sdk\SupertextException;

/**
 * The default transport without a server (the stand-in conformance run covers real answers).
 */
#[RequiresPhpExtension('curl')]
final class CurlTransportTest extends TestCase
{
    public function testConnectionRefusedIsATransportError(): void
    {
        // Port 1 on localhost: nothing listens there.
        $client = new Client('key', baseUrl: 'http://127.0.0.1:1/v1/', requestTimeout: 5.0);
        try {
            $client->validateApiKey();
            self::fail('expected transport_error');
        } catch (SupertextException $e) {
            self::assertSame(SupertextException::TRANSPORT_ERROR, $e->errorCode);
            self::assertNull($e->status);
            self::assertNotSame('', (string) $e->detail);
        }
    }
}
