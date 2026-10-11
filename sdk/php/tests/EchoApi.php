<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;

/**
 * A fake API that "translates" by returning the uploaded document, optionally transformed.
 */
final class EchoApi
{
    public FakeTransport $transport;

    /** @var list<string> uploaded documents */
    public array $documents = [];

    /** @var list<array<string, string>> uploaded form fields (without the file) */
    public array $fields = [];

    public int $deleted = 0;

    /** @param (callable(string): string)|null $translate */
    public function __construct(?callable $translate = null)
    {
        $translate ??= static fn (string $html): string => $html;
        $this->transport = FakeTransport::handler(function (HttpRequest $request) use ($translate): HttpResponse {
            $path = (string) preg_replace('#^https://api\.supertext\.com/v1/#', '', $request->url);
            if ($request->method === 'POST' && $path === 'translate/ai/file') {
                [$fields, $file] = Multipart::parse($request);
                $this->fields[] = $fields;
                $this->documents[] = $file;

                return new HttpResponse(200, json_encode(['file_id' => 'f' . \count($this->documents)], JSON_THROW_ON_ERROR));
            }
            if (preg_match('#^translate/ai/file/f(\d+)/status$#', $path) === 1) {
                return new HttpResponse(200, '{"status":"done"}');
            }
            if (preg_match('#^translate/ai/file/f(\d+)/translation$#', $path, $m) === 1) {
                return new HttpResponse(200, $translate($this->documents[(int) $m[1] - 1]));
            }
            if ($request->method === 'DELETE') {
                $this->deleted++;

                return new HttpResponse(204);
            }

            return new HttpResponse(404);
        });
    }
}
