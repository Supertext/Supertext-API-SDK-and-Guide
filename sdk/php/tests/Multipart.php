<?php

declare(strict_types=1);

namespace Supertext\Sdk\Tests;

use PHPUnit\Framework\Assert;
use Supertext\Sdk\Http\HttpRequest;

/**
 * Strict reader for the client's multipart uploads.
 */
final class Multipart
{
    /**
     * @return array{array<string, string>, string, list<string>, string} fields in order, file content, field names in order, file part headers
     */
    public static function parse(HttpRequest $request): array
    {
        $type = $request->headers['Content-Type'] ?? '';
        Assert::assertMatchesRegularExpression('/^multipart\/form-data; boundary=(.+)$/', $type);
        $boundary = substr($type, \strlen('multipart/form-data; boundary='));
        $body = (string) $request->body;
        Assert::assertStringEndsWith("\r\n--{$boundary}--\r\n", $body);
        $parts = explode("--{$boundary}\r\n", substr($body, 0, -\strlen("\r\n--{$boundary}--\r\n")));
        Assert::assertSame('', array_shift($parts));

        $fields = [];
        $names = [];
        $file = '';
        $fileHeaders = '';
        foreach ($parts as $part) {
            [$headers, $content] = explode("\r\n\r\n", $part, 2);
            Assert::assertMatchesRegularExpression('/^Content-Disposition: form-data; name="([a-z_]+)"/', $headers);
            preg_match('/name="([a-z_]+)"/', $headers, $m);
            $names[] = $m[1];
            if ($m[1] === 'file') {
                $file = $content;
                $fileHeaders = $headers;
            } else {
                $content = substr($content, 0, -2); // the CRLF before the next boundary
                Assert::assertSame("Content-Disposition: form-data; name=\"{$m[1]}\"", $headers);
                $fields[$m[1]] = $content;
            }
        }

        return [$fields, $file, $names, $fileHeaders];
    }
}
