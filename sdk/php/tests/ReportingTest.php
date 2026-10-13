<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\TransportException;
use Supertext\Sdk\Supertext;
use Supertext\Sdk\V1\ApiKeyCheck;
use Supertext\Sdk\V1\Client;
use Supertext\Sdk\V1\InstallationReporter;
use Supertext\Sdk\V1\Protocol;
use Supertext\Sdk\V1\ReporterState;

/**
 * spec/reporting.json and the Commando Center's ping cases (spec/commando/callback-vectors.json).
 */
final class ReportingTest extends TestCase
{
    private const REPORT_URL = 'https://center.test/api/v1/report';
    private const API_URL = 'https://api.test/v1/';

    /** @return array<string, mixed> */
    private static function spec(string $name): array
    {
        $json = file_get_contents(__DIR__ . '/../../../spec/' . $name);
        self::assertIsString($json);
        $data = json_decode($json, true, 512, JSON_THROW_ON_ERROR);
        self::assertIsArray($data);

        return $data;
    }

    /**
     * Fake HTTP: answers GET features and POST report, records which were called and the report bodies.
     *
     * @param array<string, mixed>|null $features
     * @param array<string, mixed>|null $report
     * @param list<string>              $calls
     * @param list<HttpRequest>         $reports
     */
    private static function fakeHttp(?array $features, ?array $report, array &$calls, array &$reports): FakeTransport
    {
        return FakeTransport::handler(static function (HttpRequest $request) use ($features, $report, &$calls, &$reports): HttpResponse {
            $kind = str_ends_with($request->url, '/features') ? 'features' : 'report';
            $calls[] = $kind;
            if ($kind === 'report') {
                $reports[] = $request;
            }
            $answer = $kind === 'features' ? $features : $report;
            if ($answer === null) {
                throw new \LogicException('unexpected ' . $kind . ' request');
            }
            if (($answer['transportError'] ?? false) === true) {
                throw new TransportException('connection refused');
            }

            return new HttpResponse((int) ($answer['status'] ?? 200), (string) ($answer['body'] ?? ''));
        });
    }

    private static function time(string $iso): float
    {
        $time = strtotime($iso);
        self::assertIsInt($time);

        return (float) $time;
    }

    /**
     * @param array<string, mixed> $config
     */
    private static function reporter(array $config, ?FakeTransport $transport = null, ?float $now = null): InstallationReporter
    {
        return new InstallationReporter(
            enabled: (bool) $config['enabled'],
            siteUrl: (string) $config['siteUrl'],
            pluginName: (string) $config['plugin']['name'],
            pluginVersion: (string) $config['plugin']['version'],
            systemName: (string) $config['system']['name'],
            systemVersion: $config['system']['version'] ?? null,
            runtimeName: $config['runtime']['name'] ?? null,
            runtimeVersion: $config['runtime']['version'] ?? null,
            sdkVersion: $config['sdkVersion'] ?? null,
            callbackUrl: $config['callbackUrl'] ?? null,
            reportUrl: self::REPORT_URL,
            transport: $transport,
            apiBaseUrl: self::API_URL,
            sleep: static function (float $seconds): void {
            },
            now: $now === null ? null : static fn (): float => $now,
        );
    }

    public function testDefaults(): void
    {
        $defaults = self::spec('reporting.json')['defaults'];
        self::assertSame($defaults['reportUrl'], InstallationReporter::DEFAULT_REPORT_URL);
        self::assertSame($defaults['reportTimeoutSeconds'], InstallationReporter::REPORT_TIMEOUT);
        self::assertSame($defaults['keyCheckTimeoutSeconds'], InstallationReporter::KEY_CHECK_TIMEOUT);
        self::assertSame($defaults['failureBackoffSeconds'], InstallationReporter::FAILURE_BACKOFF);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function cases(string $section): array
    {
        $out = [];
        foreach (self::spec('reporting.json')[$section] as $i => $case) {
            $out['#' . $i . ' ' . json_encode($case, JSON_UNESCAPED_UNICODE)] = [$case];
        }

        return $out;
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
        $calls = $reports = [];
        $transport = self::fakeHttp($case['answer'], null, $calls, $reports);
        $client = new Client(
            $case['apiKey'],
            baseUrl: self::API_URL,
            transport: $transport,
            sleep: static function (float $seconds): void {
            },
            now: static fn (): float => self::time('2026-10-13T08:00:00Z') + 0.5,
        );
        $check = $client->checkApiKey();
        self::assertSame([
            'status' => $case['status'],
            'checkedAt' => '2026-10-13T08:00:00Z',
            'httpStatus' => $case['httpStatus'],
            'errorCode' => $case['errorCode'],
            'fingerprint' => Protocol::apiKeyFingerprint($case['apiKey']),
        ], $check->toArray());
        self::assertCount($case['requests'] ?? 1, $calls);
        foreach ($transport->requests as $request) {
            self::assertSame((float) ApiKeyCheck::TIMEOUT, $request->timeout);
            self::assertSame('GET', $request->method);
        }
    }

    public function testApiKeyCheckUsesAShorterRequestTimeout(): void
    {
        $transport = FakeTransport::queue([new HttpResponse(200, '{}')]);
        (new Client('k1', requestTimeout: 4.0, transport: $transport))->checkApiKey();
        self::assertSame(4.0, $transport->requests[0]->timeout);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function enabledCases(): array
    {
        return self::cases('enabled');
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('enabledCases')]
    public function testEnabled(array $case): void
    {
        self::assertSame($case['out'], InstallationReporter::reportsEnabled($case['setting'], $case['env']));
    }

    public function testEnabledReadsTheEnvironment(): void
    {
        putenv('SUPERTEXT_VERSION_REPORTS=off');
        try {
            self::assertFalse(InstallationReporter::reportsEnabled(true));
            self::assertTrue(InstallationReporter::reportsEnabled(true, null));
        } finally {
            putenv('SUPERTEXT_VERSION_REPORTS');
        }
        self::assertTrue(InstallationReporter::reportsEnabled());
        self::assertFalse(InstallationReporter::reportsEnabled(false));
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function pingCases(): array
    {
        $ping = self::spec('reporting.json')['ping'];
        $out = [];
        foreach ($ping['cases'] as $i => $case) {
            $out['#' . $i . ' ' . json_encode($case)] = [$case + ['plugin' => $ping['plugin']]];
        }

        return $out;
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('pingCases')]
    public function testPing(array $case): void
    {
        $response = InstallationReporter::pingResponse($case['challenge'], $case['plugin'], $case['enabled']);
        self::assertSame($case['status'], $response->status);
        self::assertSame($case['body'], json_decode($response->body, true, 512, JSON_THROW_ON_ERROR));
        self::assertSame(['Content-Type' => 'application/json', 'Cache-Control' => 'no-store'], $response->headers);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function scenarios(): array
    {
        $spec = self::spec('reporting.json');
        $out = [];
        foreach ($spec['scenarios'] as $scenario) {
            $scenario['config'] = array_replace($spec['config'], $scenario['config'] ?? []);
            $out[$scenario['name']] = [$scenario];
        }

        return $out;
    }

    /** @param array<string, mixed> $scenario */
    #[DataProvider('scenarios')]
    public function testScenario(array $scenario): void
    {
        $state = ReporterState::from(json_encode($scenario['state'], JSON_THROW_ON_ERROR));
        self::assertSame($scenario['state'], $state->toArray());
        foreach ($scenario['steps'] as $i => $step) {
            $label = '#' . $i . ' ' . $step['action'];
            $calls = $reports = [];
            $transport = self::fakeHttp($step['features'] ?? null, $step['report'] ?? null, $calls, $reports);
            $reporter = self::reporter($scenario['config'], $transport, self::time($step['now']));
            $result = match ($step['action']) {
                'afterSettingsSaved' => $reporter->afterSettingsSaved($state, $step['apiKey']),
                'onRequest' => $reporter->onRequest($state, $step['apiKey']),
                'authenticationFailed' => $reporter->authenticationFailed($state, $step['apiKey'], $step['httpStatus']),
                'testConnection' => $reporter->testConnection($state, $step['apiKey']),
                'reportCheck' => $reporter->reportCheck($state),
                default => self::fail('unknown action ' . $step['action']),
            };
            $expect = $step['expect'];
            self::assertSame($expect['requests'], $calls, $label);
            self::assertSame($expect['event'], $result->event, $label);
            self::assertSame($expect['sent'], $result->sent, $label);
            self::assertSame($expect['check'], $result->check?->toArray(), $label);
            self::assertSame($expect['state'], $result->state->toArray(), $label);
            if (isset($expect['reportBody'])) {
                self::assertCount(1, $reports);
                $report = $reports[0];
                self::assertSame(self::REPORT_URL, $report->url);
                self::assertSame('POST', $report->method);
                self::assertSame((float) InstallationReporter::REPORT_TIMEOUT, $report->timeout);
                self::assertSame('application/json', $report->headers['Content-Type']);
                self::assertSame('supertext-sdk-php ' . $scenario['config']['plugin']['name'] . '/' . $scenario['config']['plugin']['version'], $report->headers['User-Agent']);
                self::assertIsString($report->body);
                self::assertEquals($expect['reportBody'], json_decode($report->body, true, 512, JSON_THROW_ON_ERROR), $label);
            }
            // The stored form survives a JSON round trip, in the same shape.
            $json = json_encode($result->state, JSON_THROW_ON_ERROR);
            self::assertSame($expect['state'], json_decode($json, true, 512, JSON_THROW_ON_ERROR), $label);
            $state = ReporterState::from($json);
            self::assertSame($expect['state'], $state->toArray(), $label);
        }
    }

    // ---- spec/commando ----

    /** @return array<string, array{array<string, mixed>}> */
    public static function endpointCases(): array
    {
        $out = [];
        foreach (self::spec('commando/callback-vectors.json')['endpoint']['cases'] as $i => $case) {
            $out['#' . $i . ' ' . json_encode($case['challenge'])] = [$case];
        }

        return $out;
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('endpointCases')]
    public function testPingEndpoint(array $case): void
    {
        $response = InstallationReporter::pingResponse($case['challenge'], 'Some-Supertext-Translation', true);
        self::assertSame($case['status'], $response->status);
        $expected = $case['body'];
        if (isset($expected['plugin'])) {
            $expected['plugin'] = 'Some-Supertext-Translation';
        }
        self::assertSame($expected, json_decode($response->body, true, 512, JSON_THROW_ON_ERROR));
    }

    public function testReportsHaveTheFieldsOfTheCenterSchema(): void
    {
        $schema = self::spec('commando/report-v1.schema.json');
        $reporter = self::reporter(self::spec('reporting.json')['config']);
        $check = new ApiKeyCheck(ApiKeyCheck::INVALID, '2026-10-13T08:00:00Z', 401, 'INVALID_API_KEY', 'abc');
        foreach ([ReporterState::empty(), new ReporterState(keyCheck: $check)] as $state) {
            $body = $reporter->buildReport(InstallationReporter::EVENT_SETTINGS_SAVED, $state);
            foreach ($schema['required'] as $field) {
                self::assertArrayHasKey($field, $body);
            }
            foreach (array_keys($body) as $field) {
                self::assertArrayHasKey($field, $schema['properties']);
            }
        }
    }

    // ---- reporter ----

    public function testNeverSendsTheKey(): void
    {
        $calls = $reports = [];
        $transport = self::fakeHttp(['status' => 401, 'body' => '{"error_code":"INVALID_API_KEY"}'], ['status' => 202], $calls, $reports);
        $result = self::reporter(self::spec('reporting.json')['config'], $transport)->afterSettingsSaved(null, 'secret-key-123');
        self::assertSame(['features', 'report'], $calls);
        self::assertTrue($result->sent);
        self::assertStringNotContainsString('secret-key-123', (string) $reports[0]->body);
        self::assertStringNotContainsString('secret-key-123', json_encode($result->state, JSON_THROW_ON_ERROR));
        self::assertStringNotContainsString('secret-key-123', print_r($result, true));
    }

    public function testNeverThrows(): void
    {
        $transport = FakeTransport::handler(static function (HttpRequest $request): HttpResponse {
            throw new \Error('boom');
        });
        $reporter = self::reporter(self::spec('reporting.json')['config'], $transport, self::time('2026-10-13T08:00:00Z'));
        $result = $reporter->afterSettingsSaved(null, 'k1');
        self::assertSame(ApiKeyCheck::UNREACHABLE, $result->check?->status);
        self::assertFalse($result->sent);
        self::assertSame('2026-10-13T08:00:00Z', $result->state->lastFailureAt);
        self::assertFalse($reporter->reportCheck($result->state)->sent);
    }

    public function testBrokenStoredStateIsEmpty(): void
    {
        $empty = ['reportedVersions' => null, 'reportedKeyStatus' => null, 'lastFailureAt' => null, 'keyCheck' => null];
        self::assertSame($empty, ReporterState::from('not json')->toArray());
        self::assertSame($empty, ReporterState::from(null)->toArray());
        self::assertSame($empty, ReporterState::from(['reportedVersions' => 5, 'keyCheck' => ['status' => 'ok']])->toArray());
        $state = ReporterState::from(json_decode('{"reportedVersions":"1|2|3","keyCheck":{"status":"ok","checkedAt":"x"}}'));
        self::assertSame('1|2|3', $state->reportedVersions);
        self::assertSame(['status' => 'ok', 'checkedAt' => 'x', 'httpStatus' => null, 'errorCode' => null, 'fingerprint' => ''], $state->keyCheck?->toArray());
        self::assertEquals($state->keyCheck, ApiKeyCheck::fromArray($state->keyCheck->toArray()));
    }

    public function testDefaultRuntimeIsPhp(): void
    {
        $config = self::spec('reporting.json')['config'];
        unset($config['runtime'], $config['sdkVersion']);
        $reporter = self::reporter($config);
        $body = $reporter->buildReport(InstallationReporter::EVENT_CHECK, ReporterState::empty());
        self::assertSame(['name' => 'PHP', 'version' => PHP_VERSION], $body['runtime']);
        self::assertSame(Supertext::SDK_VERSION, $body['sdk_version']);
        self::assertSame('0.2.0|12.0.0|' . PHP_VERSION, $reporter->versions());
    }

    public function testReportUrlFromTheEnvironment(): void
    {
        $args = ['enabled' => true, 'siteUrl' => 'https://x.test', 'pluginName' => 'P', 'pluginVersion' => '1', 'systemName' => 'S'];
        self::assertSame(InstallationReporter::DEFAULT_REPORT_URL, (new InstallationReporter(...$args))->reportUrl);
        putenv('SUPERTEXT_REPORT_URL=https://env.test/report');
        try {
            self::assertSame('https://env.test/report', (new InstallationReporter(...$args))->reportUrl);
            self::assertSame('https://own.test/report', (new InstallationReporter(...$args, reportUrl: 'https://own.test/report'))->reportUrl);
        } finally {
            putenv('SUPERTEXT_REPORT_URL');
        }
    }
}
