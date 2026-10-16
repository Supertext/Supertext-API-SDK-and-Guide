<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\TransportException;
use Supertext\Sdk\V1\ApiKeyCheck;
use Supertext\Sdk\V1\Client;
use Supertext\Sdk\V1\Protocol;

/**
 * spec/api-key-check.json: the API key check Supertext Plugin Health reports.
 */
final class ApiKeyCheckTest extends TestCase
{
    private const API_URL = 'https://api.test/v1/';

    /** @return array<string, mixed> */
    private static function spec(): array
    {
        $json = file_get_contents(__DIR__ . '/../../../spec/api-key-check.json');
        self::assertIsString($json);
        $data = json_decode($json, true, 512, JSON_THROW_ON_ERROR);
        self::assertIsArray($data);

        return $data;
    }

    /** @return array<string, array{array<string, mixed>}> */
    private static function cases(string $section): array
    {
        $out = [];
        foreach (self::spec()[$section] as $i => $case) {
            $out['#' . $i . ' ' . json_encode($case, JSON_UNESCAPED_UNICODE)] = [$case];
        }

        return $out;
    }

    public function testTimeout(): void
    {
        self::assertSame(self::spec()['defaults']['keyCheckTimeoutSeconds'], ApiKeyCheck::TIMEOUT);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function fingerprintCases(): array
    {
        return self::cases('fingerprint');
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('fingerprintCases')]
    public function testFingerprint(array $case): void
    {
        self::assertSame($case['out'], Protocol::apiKeyFingerprint($case['apiKey']));
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function apiKeyCheckCases(): array
    {
        return self::cases('apiKeyCheck');
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('apiKeyCheckCases')]
    public function testApiKeyCheck(array $case): void
    {
        $answer = $case['answer'];
        $calls = 0;
        $transport = FakeTransport::handler(static function (HttpRequest $request) use ($answer, &$calls): HttpResponse {
            $calls++;
            if ($answer === null) {
                throw new \LogicException('unexpected request ' . $request->url);
            }
            if (($answer['transportError'] ?? false) === true) {
                throw new TransportException('connection refused');
            }

            return new HttpResponse((int) ($answer['status'] ?? 200), (string) ($answer['body'] ?? ''));
        });
        $checkedAt = strtotime('2026-10-13T08:00:00Z');
        self::assertIsInt($checkedAt);
        $client = new Client(
            $case['apiKey'],
            baseUrl: self::API_URL,
            transport: $transport,
            sleep: static function (float $seconds): void {
            },
            now: static fn (): float => $checkedAt + 0.5,
        );
        $check = $client->checkApiKey();
        self::assertSame([
            'status' => $case['status'],
            'checkedAt' => '2026-10-13T08:00:00Z',
            'httpStatus' => $case['httpStatus'],
            'errorCode' => $case['errorCode'],
            'fingerprint' => Protocol::apiKeyFingerprint($case['apiKey']),
        ], $check->toArray());
        self::assertSame($case['requests'] ?? 1, $calls);
        foreach ($transport->requests as $request) {
            self::assertSame((float) ApiKeyCheck::TIMEOUT, $request->timeout);
            self::assertSame('GET', $request->method);
            self::assertStringEndsWith('/features', $request->url);
        }
    }

    public function testApiKeyCheckUsesAShorterRequestTimeout(): void
    {
        $transport = FakeTransport::queue([new HttpResponse(200, '{}')]);
        (new Client('k1', requestTimeout: 4.0, transport: $transport))->checkApiKey();
        self::assertSame(4.0, $transport->requests[0]->timeout);
    }

    public function testRoundTrip(): void
    {
        $check = new ApiKeyCheck(ApiKeyCheck::INVALID, '2026-10-13T08:00:00Z', 401, 'INVALID_API_KEY', 'abc');
        self::assertEquals($check, ApiKeyCheck::fromArray($check->toArray()));
        self::assertSame($check->toArray(), json_decode(json_encode($check, JSON_THROW_ON_ERROR), true, 512, JSON_THROW_ON_ERROR));
    }
}
