<?php

declare(strict_types=1);

/*
 * Conformance program for stand-in/conformance.mjs: reads one request as JSON on stdin, translates it
 * with translateTexts() and prints {"translations": {...}} or {"error": {"code", "status"}}.
 *
 *   node stand-in/conformance.mjs -- php sdk/php/examples/conformance.php
 */

use Supertext\Sdk\Client;
use Supertext\Sdk\Politeness;
use Supertext\Sdk\Segment;
use Supertext\Sdk\SupertextException;

require file_exists(__DIR__ . '/../../../vendor/autoload.php') ? __DIR__ . '/../../../vendor/autoload.php' : __DIR__ . '/../autoload.php';

$input = json_decode((string) stream_get_contents(STDIN), true, 512, JSON_THROW_ON_ERROR);
if (!is_array($input) || !is_array($input['segments'] ?? null)) {
    fwrite(STDERR, "expected a JSON object with segments\n");
    exit(2);
}

// Keys are kept as a list of pairs: PHP would turn numeric string keys into integers in an array.
$keys = [];
$segments = [];
foreach ($input['segments'] as $segment) {
    $keys[] = (string) $segment['key'];
    $segments[] = new Segment((string) $segment['text'], (bool) ($segment['html'] ?? false));
}

$client = new Client(
    (string) getenv('SUPERTEXT_API_KEY'),
    baseUrl: (string) getenv('SUPERTEXT_API_URL'),
    pollInterval: 0.1,
);

try {
    $translated = $client->translateTexts(
        $segments,
        (string) $input['targetLang'],
        isset($input['sourceLang']) ? (string) $input['sourceLang'] : null,
        isset($input['politeness']) ? (string) $input['politeness'] : Politeness::DEFAULT,
    );
    $translations = new stdClass();
    foreach ($keys as $index => $key) {
        $translations->{$key} = $translated[$index];
    }
    $output = ['translations' => $translations];
} catch (SupertextException $e) {
    $output = ['error' => ['code' => $e->errorCode, 'status' => $e->status]];
}

echo json_encode($output, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR), "\n";
