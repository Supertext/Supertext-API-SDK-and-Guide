<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;
use Supertext\Sdk\Files\FileJob;
use Supertext\Sdk\Files\FileSegment;
use Supertext\Sdk\Files\TranslationFile;
use Supertext\Sdk\Files\TranslationFileError;
use Supertext\Sdk\Files\TranslationFiles;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\V1\Client;
use Supertext\Sdk\V1\Correction;
use Supertext\Sdk\V1\HtmlDocument;
use Supertext\Sdk\V1\Languages;
use Supertext\Sdk\V1\Protocol;
use Supertext\Sdk\V1\Segment;
use Supertext\Sdk\V1\SupertextException;

/**
 * The shared test vectors in spec/ (the same files every Supertext SDK passes).
 */
final class SpecTest extends TestCase
{
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
     * @param list<array<string, mixed>> $cases
     *
     * @return array<string, array{array<string, mixed>}>
     */
    private static function named(array $cases): array
    {
        $out = [];
        foreach ($cases as $i => $case) {
            $label = isset($case['name']) ? (string) $case['name'] : json_encode($case['in'] ?? $case, JSON_UNESCAPED_UNICODE);
            $out['#' . $i . ' ' . $label] = [$case];
        }

        return $out;
    }

    // ---- languages.json ----

    public function testLanguageLists(): void
    {
        $spec = self::spec('languages.json');
        self::assertSame($spec['sources'], Languages::SOURCES);
        self::assertSame($spec['targets'], Languages::TARGETS);
        self::assertSame($spec['defaultTargets'], Languages::DEFAULT_TARGETS);
        self::assertSame($spec['targetOptions'], array_column(Languages::targetOptions(), 'code'));
        foreach (Languages::targetOptions() as $option) {
            self::assertContains($option, $spec['targets']);
        }
    }

    /** @return array<string, array{array<string, mixed>, string}> */
    public static function languageCases(): array
    {
        $spec = self::spec('languages.json');
        $out = [];
        foreach (['matchTarget', 'matchSource', 'defaultTarget', 'suggestTarget', 'resolveTarget', 'resolveSource', 'supportsPoliteness'] as $function) {
            foreach ($spec[$function] as $i => $case) {
                $out[$function . ' #' . $i . ' ' . json_encode($case, JSON_UNESCAPED_UNICODE)] = [$case, $function];
            }
        }

        return $out;
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('languageCases')]
    public function testLanguages(array $case, string $function): void
    {
        $in = $case['in'];
        $mapping = $case['mapping'] ?? [];
        $call = static fn (): mixed => match ($function) {
            'matchTarget' => Languages::matchTarget($in),
            'matchSource' => Languages::matchSource($in),
            'defaultTarget' => Languages::defaultTarget($in),
            'suggestTarget' => Languages::suggestTarget($in),
            'resolveTarget' => Languages::target($in, $mapping),
            'resolveSource' => Languages::source($in, $mapping),
            'supportsPoliteness' => Languages::supportsPoliteness($in),
        };
        if (isset($case['error'])) {
            try {
                $call();
                self::fail('expected ' . $case['error']);
            } catch (SupertextException $e) {
                self::assertSame($case['error'], $e->errorCode);
                // The code that has no Supertext code, trimmed (null when blank).
                $trimmed = trim($in) === '' ? null : trim($in);
                self::assertSame($trimmed, $function === 'resolveTarget' ? $e->targetLang : $e->sourceLang);
            }

            return;
        }
        self::assertSame($case['out'], $call());
    }

    public function testIsSupported(): void
    {
        foreach (Languages::TARGETS as $language) {
            self::assertTrue(Languages::isSupportedTarget($language['code']));
            self::assertTrue(Languages::isSupportedTarget(strtolower(str_replace('-', '_', $language['code']))));
        }
        foreach (Languages::SOURCES as $language) {
            self::assertTrue(Languages::isSupportedSource(' ' . strtoupper($language['code']) . ' '));
        }
        self::assertFalse(Languages::isSupportedTarget('de'));
        self::assertFalse(Languages::isSupportedTarget(''));
        // Sources: exact only (matchSource also takes the first subtag).
        self::assertFalse(Languages::isSupportedSource('en-US'));
        self::assertFalse(Languages::isSupportedSource(''));
    }

    // ---- auth-header.json ----

    /** @return array<string, array{array<string, mixed>}> */
    public static function authCases(): array
    {
        return self::named(self::spec('auth-header.json')['cases']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('authCases')]
    public function testAuthHeader(array $case): void
    {
        if (isset($case['error'])) {
            try {
                Protocol::authHeader($case['in']);
                self::fail('expected ' . $case['error']);
            } catch (SupertextException $e) {
                self::assertSame($case['error'], $e->errorCode);
            }

            return;
        }
        self::assertSame($case['out'], Protocol::authHeader($case['in']));
    }

    // ---- retry-delay.json ----

    /** @return array<string, array{array<string, mixed>}> */
    public static function retryCases(): array
    {
        return self::named(self::spec('retry-delay.json')['cases']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('retryCases')]
    public function testRetryDelay(array $case): void
    {
        // Lowest and highest jitter, and the real random source.
        foreach ([static fn (int $min, int $max): int => $min, static fn (int $min, int $max): int => $max, null] as $random) {
            $delay = Protocol::retryDelayMs($case['attempt'], $case['retryAfter'], $random);
            self::assertGreaterThanOrEqual($case['min'], $delay);
            self::assertLessThanOrEqual($case['max'], $delay);
        }
    }

    public function testMaxRetriesDefault(): void
    {
        self::assertSame(self::spec('retry-delay.json')['maxRetries'], Protocol::DEFAULT_MAX_RETRIES);
    }

    // ---- html-document.json ----

    /**
     * @param list<array<string, mixed>> $segments
     *
     * @return list<Segment>
     */
    private static function segments(array $segments): array
    {
        return array_map(static fn (array $s): Segment => new Segment((string) ($s['text'] ?? ''), (bool) $s['html']), $segments);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function buildCases(): array
    {
        return self::named(self::spec('html-document.json')['build']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('buildCases')]
    public function testBuild(array $case): void
    {
        self::assertSame($case['html'], HtmlDocument::build(self::segments($case['segments'])));
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function parseCases(): array
    {
        return self::named(self::spec('html-document.json')['parse']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('parseCases')]
    public function testParse(array $case): void
    {
        self::assertSame($case['out'], HtmlDocument::parse($case['html'], self::segments($case['segments'])));
    }

    /** Whatever build() produces, parse() reads back (for texts without whitespace runs). */
    public function testBuildParseRoundTrip(): void
    {
        $segments = [Segment::text("Fish & Chips <today>\n\"fresh\" it's"), Segment::html('<p>A <b>b</b></p>'), Segment::text('Käse 日本語')];
        self::assertSame(
            ["Fish & Chips <today>\n\"fresh\" it's", '<p>A <b>b</b></p>', 'Käse 日本語'],
            HtmlDocument::parse(HtmlDocument::build($segments), $segments),
        );
    }

    // ---- batching.json ----

    public function testDefaultMaxCharacters(): void
    {
        self::assertSame(self::spec('batching.json')['defaultMaxCharacters'], Protocol::DEFAULT_MAX_CHARACTERS);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function batchingCases(): array
    {
        return self::named(self::spec('batching.json')['cases']);
    }

    /**
     * Runs translateTexts against a fake API that echoes the document, and checks which keys went into
     * which upload (the batching as the client really does it).
     *
     * @param array<string, mixed> $case
     */
    #[DataProvider('batchingCases')]
    public function testBatching(array $case): void
    {
        $input = [];
        foreach ($case['segments'] as $s) {
            $input[$s['key']] = new Segment($s['text'], $s['html']);
        }
        $api = new EchoApi();
        $client = new Client('key', transport: $api->transport, sleep: static function (float $s): void {});
        $result = $client->translateTexts($input, 'de-DE', 'en', maxCharacters: $case['maxCharacters']);

        // One upload per expected batch, each with that batch's segments (ids are positions in the batch).
        $uploaded = array_map(static fn (string $doc): int => preg_match_all('/data-st-id="\d+"/', $doc), $api->documents);
        self::assertSame(array_map('count', $case['batches']), $uploaded);
        // The same split by key, from the public helper.
        $nonBlank = array_filter($input, static fn (Segment $s): bool => !$s->isBlank());
        self::assertSame($case['batches'], array_map('array_keys', Client::batches($nonBlank, $case['maxCharacters'])));
        // Everything comes back under its key, in input order; blank segments unchanged.
        self::assertSame(array_keys($input), array_keys($result));
        foreach ($input as $key => $segment) {
            if ($segment->isBlank()) {
                self::assertSame($segment->text, $result[$key]);
            }
        }
    }

    // ---- errors.json ----

    /** @return array<string, array{array<string, mixed>}> */
    public static function httpErrorCases(): array
    {
        $out = [];
        foreach (self::spec('errors.json')['http'] as $i => $case) {
            $out['#' . $i . ' ' . $case['status'] . ' ' . $case['body']] = [$case];
        }

        return $out;
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('httpErrorCases')]
    public function testHttpErrors(array $case): void
    {
        $error = Client::errorFor(new HttpResponse($case['status'], $case['body']));
        self::assertSame($case['code'], $error->errorCode);
        self::assertSame($case['status'], $error->status);
        // Every vector lists all five (null = must be null).
        self::assertSame($case['detail'], $error->detail);
        self::assertSame($case['apiErrorCode'], $error->apiErrorCode);
        self::assertSame($case['sourceLang'], $error->sourceLang);
        self::assertSame($case['targetLang'], $error->targetLang);
        self::assertSame($case['feature'], $error->feature);

        // The same through the client (429 only after the retries).
        $responses = array_fill(0, $case['status'] === 429 ? 5 : 1, new HttpResponse($case['status'], $case['body']));
        $client = new Client('key', transport: FakeTransport::queue($responses), sleep: static function (float $s): void {});
        try {
            $client->validateApiKey();
            self::fail('expected ' . $case['code']);
        } catch (SupertextException $e) {
            self::assertSame($case['code'], $e->errorCode);
        }
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function fileStatusCases(): array
    {
        $out = [];
        foreach (self::spec('errors.json')['fileStatus'] as $case) {
            $out[$case['status']] = [$case];
        }

        return $out;
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('fileStatusCases')]
    public function testFileStatus(array $case): void
    {
        $transport = FakeTransport::queue([
            new HttpResponse(200, json_encode(['status' => $case['status']], JSON_THROW_ON_ERROR)),
            new HttpResponse(200, '{"status":"done"}'),
        ]);
        $client = new Client('key', transport: $transport, sleep: static function (float $s): void {});
        try {
            $client->waitUntilDone('f1');
            self::assertNull($case['code']);
        } catch (SupertextException $e) {
            self::assertSame($case['code'], $e->errorCode);
        }
    }

    public function testEveryErrorCodeHasAConstant(): void
    {
        $constants = (new \ReflectionClass(SupertextException::class))->getConstants();
        foreach (array_keys(self::spec('errors.json')['codes']) as $code) {
            self::assertContains($code, $constants);
        }
    }

    // ---- translation-file.json ----

    /** @param array<string, mixed> $segment */
    private static function fileSegment(array $segment): FileSegment
    {
        return new FileSegment((string) $segment['key'], (string) $segment['text'], (bool) ($segment['html'] ?? false));
    }

    /**
     * @param list<array<string, mixed>> $segments
     *
     * @return list<FileSegment>
     */
    private static function fileSegments(array $segments): array
    {
        return array_map(self::fileSegment(...), $segments);
    }

    /** @param array<string, mixed> $job */
    private static function fileJob(array $job): FileJob
    {
        return new FileJob((string) $job['plugin'], (string) $job['reference'], (string) $job['sourceLanguage'], (string) $job['targetLanguage'], self::fileSegments($job['segments']));
    }

    /** The error code thrown by $fn, or null when it returns. */
    private static function fileErrorCode(callable $fn): ?string
    {
        try {
            $fn();

            return null;
        } catch (TranslationFileError $e) {
            return $e->errorCode;
        }
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function translationFileChecksumCases(): array
    {
        return self::named(self::spec('translation-file.json')['checksum']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('translationFileChecksumCases')]
    public function testTranslationFileChecksum(array $case): void
    {
        self::assertSame($case['out'], TranslationFiles::checksum($case['segment']));
        self::assertSame($case['out'], TranslationFiles::checksum(new FileSegment('k', $case['segment']['text'], $case['segment']['html'] ?? false)));
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function translationFileExportXliffCases(): array
    {
        return self::named(self::spec('translation-file.json')['exportXliff']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('translationFileExportXliffCases')]
    public function testTranslationFileExportXliff(array $case): void
    {
        $export = static fn (): string => TranslationFiles::exportXliff(array_map(self::fileJob(...), $case['jobs']));
        if (isset($case['error'])) {
            self::assertSame($case['error'], self::fileErrorCode($export));
        } else {
            self::assertSame($case['out'], $export());
        }
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function translationFileExportHtmlCases(): array
    {
        return self::named(self::spec('translation-file.json')['exportHtml']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('translationFileExportHtmlCases')]
    public function testTranslationFileExportHtml(array $case): void
    {
        $export = static fn (): string => TranslationFiles::exportHtml(self::fileJob($case['job']));
        if (isset($case['error'])) {
            self::assertSame($case['error'], self::fileErrorCode($export));
        } else {
            self::assertSame($case['out'], $export());
        }
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function translationFileReadCases(): array
    {
        return self::named(self::spec('translation-file.json')['read']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('translationFileReadCases')]
    public function testTranslationFileRead(array $case): void
    {
        $content = isset($case['repeat']) ? str_repeat($case['repeat']['text'], $case['repeat']['times']) : $case['content'];
        if (isset($case['error'])) {
            self::assertSame($case['error'], self::fileErrorCode(static fn () => TranslationFiles::read($content)));

            return;
        }
        $files = array_map(static fn (TranslationFile $f): array => [
            'format' => $f->format,
            'plugin' => $f->plugin,
            'reference' => $f->reference,
            'sourceLanguage' => $f->sourceLanguage,
            'targetLanguage' => $f->targetLanguage,
            'units' => array_map(static fn ($u): array => ['key' => $u->key, 'checksum' => $u->checksum], $f->units),
        ], TranslationFiles::read($content));
        self::assertSame($case['files'], $files);
    }

    /** @return array<string, array{array<string, mixed>}> */
    public static function translationFileImportCases(): array
    {
        return self::named(self::spec('translation-file.json')['import']);
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('translationFileImportCases')]
    public function testTranslationFileImport(array $case): void
    {
        $file = TranslationFiles::read($case['content'])[$case['file'] ?? 0];
        $out = array_map(
            static fn ($s): array => ['key' => $s->key, 'status' => $s->status, 'text' => $s->text],
            TranslationFiles::importTranslations($file, self::fileSegments($case['current'])),
        );
        self::assertSame($case['out'], $out);
    }

    public function testTranslationFileNotFromReadIsRefused(): void
    {
        $file = new TranslationFile(TranslationFile::HTML, 'P', 'r', 'de', 'fr', []);
        self::assertSame(TranslationFileError::INVALID_FILE, self::fileErrorCode(static fn () => TranslationFiles::importTranslations($file, [])));
    }

    // ---- corrections.json ----

    /** @return array<string, array{array<string, mixed>}> */
    public static function correctionScenarios(): array
    {
        return self::named(self::spec('corrections.json')['scenarios']);
    }

    /** @param array<string, mixed> $c */
    #[DataProvider('correctionScenarios')]
    public function testCorrections(array $c): void
    {
        $exchanges = $c['exchanges'];
        $version = $c['generate']['version'] ?? '';
        $ackBody = static function (int $from, int $to) use ($version): string {
            $items = [];
            for ($i = $from; $i <= $to; $i++) {
                $items[] = ['id' => (string) $i, 'version' => $version];
            }

            return (string) json_encode(['corrections' => $items]);
        };
        $transport = FakeTransport::handler(static function (HttpRequest $request) use (&$exchanges, $ackBody): HttpResponse {
            $x = array_shift($exchanges);
            self::assertNotNull($x, 'unexpected ' . $request->method . ' ' . $request->url);
            self::assertSame($x['request']['method'], $request->method);
            self::assertSame('https://review.test/v1/' . $x['request']['path'], $request->url);
            if (isset($x['request']['body'])) {
                self::assertSame($x['request']['body'], $request->body);
            }
            if (isset($x['request']['bodyIds'])) {
                self::assertSame($ackBody($x['request']['bodyIds'][0], $x['request']['bodyIds'][1]), $request->body);
            }
            if ($request->body !== null) {
                self::assertSame('application/json', $request->headers['Content-Type']);
            }

            return new HttpResponse($x['response']['status'], $x['response']['body']);
        });
        $client = new Client('key-1', 'https://review.test/v1/', transport: $transport, sleep: static function (float $s): void {
        });

        $corrections = [];
        if (isset($c['generate'])) {
            for ($i = 1; $i <= $c['generate']['count']; $i++) {
                $corrections[] = new Correction((string) $i, '', null, '', 'default', '', '', '', '', '', $version);
            }
        } else {
            foreach ($c['corrections'] ?? [] as $item) {
                $corrections[] = new Correction($item['id'], '', null, '', 'default', '', '', '', '', '', $item['version']);
            }
        }
        $run = static function () use ($c, $client, $corrections): mixed {
            if ($c['call'] === 'pending') {
                $o = $c['options'];

                return array_map(static fn (Correction $x): array => get_object_vars($x), $client->pendingCorrections($o['targetLang'] ?? null, $o['sourceLang'] ?? null, $o['politeness'] ?? null));
            }

            return $client->acknowledgeCorrections($corrections);
        };
        if (isset($c['error'])) {
            try {
                $run();
                self::fail('expected ' . $c['error']);
            } catch (SupertextException $e) {
                self::assertSame($c['error'], $e->errorCode);
            }
        } else {
            self::assertSame($c['result'], $run());
        }
        self::assertSame([], $exchanges);
    }
}
