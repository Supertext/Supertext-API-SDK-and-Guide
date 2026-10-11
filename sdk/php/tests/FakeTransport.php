<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use Supertext\Sdk\Http\HttpRequest;
use Supertext\Sdk\Http\HttpResponse;
use Supertext\Sdk\Http\HttpTransport;

/**
 * Records requests and answers them from a handler (or a queue of responses/exceptions).
 */
final class FakeTransport implements HttpTransport
{
    /** @var list<HttpRequest> */
    public array $requests = [];

    /** @var list<HttpResponse|\Throwable>|null */
    private ?array $queue = null;

    /** @var (callable(HttpRequest): HttpResponse)|null */
    private $handler = null;

    /** @param list<HttpResponse|\Throwable> $responses */
    public static function queue(array $responses): self
    {
        $fake = new self();
        $fake->queue = $responses;

        return $fake;
    }

    /** @param callable(HttpRequest): HttpResponse $handler */
    public static function handler(callable $handler): self
    {
        $fake = new self();
        $fake->handler = $handler;

        return $fake;
    }

    public function send(HttpRequest $request): HttpResponse
    {
        $this->requests[] = $request;
        if ($this->handler !== null) {
            return ($this->handler)($request);
        }
        $next = array_shift($this->queue);
        if ($next === null) {
            throw new \LogicException('unexpected request ' . $request->method . ' ' . $request->url);
        }
        if ($next instanceof \Throwable) {
            throw $next;
        }

        return $next;
    }

    /** @return list<string> "METHOD path" relative to the base URL */
    public function calls(string $base = 'https://api.supertext.com/v1/'): array
    {
        return array_map(static fn (HttpRequest $r): string => $r->method . ' ' . substr($r->url, \strlen($base)), $this->requests);
    }
}
