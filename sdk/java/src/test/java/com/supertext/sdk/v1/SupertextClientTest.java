package com.supertext.sdk.v1;

import com.supertext.sdk.Transport;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.net.ConnectException;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.junit.jupiter.api.Test;

class SupertextClientTest {

    private static final String KEY = "secret-key-123";
    private static final String BASE = "https://api.example.test/v1/";

    private final List<Duration> sleeps = new ArrayList<>();
    private final AtomicLong clock = new AtomicLong();

    /** A client on a fake transport, whose sleeps advance a fake clock. */
    private SupertextClient client(FakeTransport transport) {
        return builder(transport).build();
    }

    private SupertextClient.Builder builder(FakeTransport transport) {
        return SupertextClient.builder(KEY).baseUrl("https://api.example.test/v1").transport(transport)
                .sleeper(d -> {
                    sleeps.add(d);
                    clock.addAndGet(d.toNanos());
                })
                .clock(clock::get);
    }

    /** Parts of a multipart body: name → [headers, value], in order. */
    private static List<String[]> parts(Transport.Request request) {
        String contentType = request.getHeaders().get("Content-Type");
        String boundary = contentType.substring(contentType.indexOf("boundary=") + 9);
        String body = FakeTransport.body(request);
        assertTrue(body.endsWith("--" + boundary + "--\r\n"));
        List<String[]> parts = new ArrayList<>();
        for (String raw : body.split("--" + Pattern.quote(boundary))) {
            if (raw.isEmpty() || raw.equals("--\r\n")) {
                continue;
            }
            String part = raw.substring(2, raw.length() - 2); // leading and trailing CRLF
            int split = part.indexOf("\r\n\r\n");
            parts.add(new String[] {part.substring(0, split), part.substring(split + 4)});
        }
        return parts;
    }

    private static String name(String[] part) {
        Matcher m = Pattern.compile("name=\"([^\"]+)\"").matcher(part[0]);
        assertTrue(m.find());
        return m.group(1);
    }

    private static FakeTransport happyPath(String translation) {
        return new FakeTransport()
                .answer(200, "{\"file_id\":\"f 1\"}")
                .answer(200, "{\"status\":\"translating\"}")
                .answer(200, "{\"status\":\"done\"}")
                .answer(200, translation)
                .answer(200, "{}");
    }

    // ------------------------------------------------------------ upload

    @Test
    void uploadSendsTheFieldsInOrderWithTheLanguageCodesAsGiven() throws Exception {
        FakeTransport transport = new FakeTransport().answer(200, "{\"file_id\":\"abc\"}");
        assertEquals("abc", client(transport).submit("<p>Grüezi</p>", " de-CH ", " en ", Politeness.MORE));

        Transport.Request request = transport.requests.get(0);
        assertEquals("POST", request.getMethod());
        assertEquals(BASE + "translate/ai/file", request.getUri().toString());
        assertTrue(request.getHeaders().get("Content-Type").startsWith("multipart/form-data; boundary="));
        List<String[]> parts = parts(request);
        assertEquals(List.of("target_lang", "source_lang", "politeness", "file"), List.of(name(parts.get(0)), name(parts.get(1)), name(parts.get(2)), name(parts.get(3))));
        assertEquals("de-CH", parts.get(0)[1]);
        assertEquals("en", parts.get(1)[1]);
        assertEquals("more", parts.get(2)[1]);
        assertEquals("Content-Disposition: form-data; name=\"file\"; filename=\"content.html\"\r\nContent-Type: text/html", parts.get(3)[0]);
        assertEquals("<p>Grüezi</p>", parts.get(3)[1]);
    }

    @Test
    void uploadLeavesOutAnEmptySourceAndDefaultPoliteness() throws Exception {
        FakeTransport transport = new FakeTransport().answer(200, "{\"file_id\":\"abc\"}").answer(200, "{\"file_id\":\"def\"}");
        SupertextClient client = client(transport);
        client.submit("x", "fr-CH", null, Politeness.DEFAULT);
        client.submit("x", "fr-CH", "  ", Politeness.LESS);
        List<String[]> first = parts(transport.requests.get(0));
        assertEquals(2, first.size());
        assertEquals("target_lang", name(first.get(0)));
        assertEquals("fr-CH", first.get(0)[1]);
        assertEquals("file", name(first.get(1)));
        List<String[]> second = parts(transport.requests.get(1));
        assertEquals(List.of("target_lang", "politeness", "file"), List.of(name(second.get(0)), name(second.get(1)), name(second.get(2))));
        assertEquals("less", second.get(1)[1]);
    }

    @Test
    void codesAreNotConverted() throws Exception {
        // No default regions any more: "de" and "en-US" go out unchanged, the API decides.
        FakeTransport transport = new FakeTransport().answer(200, "{\"file_id\":\"abc\"}");
        client(transport).submit("x", "de", "en-US", Politeness.DEFAULT);
        List<String[]> parts = parts(transport.requests.get(0));
        assertEquals("de", parts.get(0)[1]);
        assertEquals("en-US", parts.get(1)[1]);
    }

    @Test
    void emptyTargetIsUnsupportedLanguageWithoutARequest() {
        for (String target : new String[] {null, "", "  "}) {
            FakeTransport transport = new FakeTransport();
            SupertextException e = assertThrows(SupertextException.class, () -> client(transport).submit("x", target));
            assertEquals(ErrorCode.UNSUPPORTED_LANGUAGE, e.getCode());
            assertNull(e.getStatus());
            assertTrue(transport.requests.isEmpty());
        }
    }

    @Test
    void uploadWithoutFileIdFails() {
        for (String body : new String[] {"{}", "{\"file_id\":\"\"}", "not json", ""}) {
            FakeTransport transport = new FakeTransport().answer(200, body);
            SupertextException e = assertThrows(SupertextException.class, () -> client(transport).submit("x", "de"));
            assertEquals(ErrorCode.NO_FILE_ID, e.getCode());
        }
    }

    // ------------------------------------------------------------ headers

    @Test
    void headers() throws Exception {
        FakeTransport transport = new FakeTransport().answer(200, "{}").answer(200, "{}");
        client(transport).validateApiKey();
        SupertextClient.builder("Supertext-Auth-Key " + KEY).baseUrl(BASE).transport(transport)
                .userAgent("Typo3-Supertext-Translation/1.2.0").build().validateApiKey();

        Map<String, String> plain = transport.requests.get(0).getHeaders();
        assertEquals("GET", transport.requests.get(0).getMethod());
        assertEquals(BASE + "features", transport.requests.get(0).getUri().toString());
        assertEquals("Supertext-Auth-Key " + KEY, plain.get("Authorization"));
        assertEquals("application/json", plain.get("Accept"));
        assertEquals("supertext-sdk-java", plain.get("User-Agent"));
        assertNull(transport.requests.get(0).getBody());
        assertEquals(Duration.ofSeconds(30), transport.requests.get(0).getTimeout());

        Map<String, String> custom = transport.requests.get(1).getHeaders();
        assertEquals("Supertext-Auth-Key " + KEY, custom.get("Authorization"));
        assertEquals("supertext-sdk-java Typo3-Supertext-Translation/1.2.0", custom.get("User-Agent"));
    }

    @Test
    void missingKeySendsNothing() {
        for (String key : new String[] {null, "", "  ", "Supertext-Auth-Key "}) {
            FakeTransport transport = new FakeTransport();
            SupertextClient client = SupertextClient.builder(key).transport(transport).build(); // doesn't throw
            SupertextException e = assertThrows(SupertextException.class,
                    () -> client.translateTexts(Map.of("a", Segment.text("Hello")), "de"));
            assertEquals(ErrorCode.MISSING_API_KEY, e.getCode());
            assertTrue(transport.requests.isEmpty());
        }
    }

    // ------------------------------------------------------------ 429

    @Test
    void retries429WithRetryAfter() throws Exception {
        FakeTransport transport = new FakeTransport()
                .answer(429, "{\"error_code\":\"RATE_LIMIT_EXCEEDED\"}", Map.of("retry-after", "3"))
                .answer(429, "", Map.of("Retry-After", "0.5"))
                .answer(200, "{}");
        client(transport).validateApiKey();
        assertEquals(3, transport.requests.size());
        assertEquals(List.of(Duration.ofMillis(3000), Duration.ofMillis(500)), sleeps);
    }

    @Test
    void gives429UpAfterMaxRetries() {
        FakeTransport transport = new FakeTransport().handler(
                r -> new Transport.Response(429, Collections.emptyMap(), "{\"error_code\":\"RATE_LIMIT_EXCEEDED\"}"));
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).validateApiKey());
        assertEquals(ErrorCode.TOO_MANY_REQUESTS, e.getCode());
        assertEquals(429, e.getStatus());
        assertEquals("RATE_LIMIT_EXCEEDED", e.getApiErrorCode());
        assertEquals(5, transport.requests.size()); // 1 + 4 retries
        assertEquals(4, sleeps.size());
        long[][] ranges = {{1000, 1249}, {2000, 2249}, {4000, 4249}, {8000, 8249}};
        for (int i = 0; i < 4; i++) {
            long ms = sleeps.get(i).toMillis();
            assertTrue(ms >= ranges[i][0] && ms <= ranges[i][1], "wait " + i + ": " + ms);
        }
    }

    @Test
    void maxRetriesIsConfigurable() {
        FakeTransport transport = new FakeTransport().handler(r -> new Transport.Response(429, Map.of("Retry-After", "1"), ""));
        assertThrows(SupertextException.class, () -> builder(transport).maxRetries(1).build().validateApiKey());
        assertEquals(2, transport.requests.size());
        assertEquals(List.of(Duration.ofSeconds(1)), sleeps);
    }

    // ------------------------------------------------------------ polling

    @Test
    void pollsRightAwayThenEveryInterval() throws Exception {
        FakeTransport transport = new FakeTransport()
                .answer(200, "{\"status\":\"translating\"}")
                .answer(200, "{\"status\":\"queued\"}")
                .answer(200, "{\"status\":\"done\"}");
        client(transport).waitUntilDone("a/b c");
        assertEquals(List.of("GET " + BASE + "translate/ai/file/a%2Fb%20c/status",
                "GET " + BASE + "translate/ai/file/a%2Fb%20c/status",
                "GET " + BASE + "translate/ai/file/a%2Fb%20c/status"), transport.lines());
        assertEquals(List.of(Duration.ofSeconds(2), Duration.ofSeconds(2)), sleeps);
    }

    @Test
    void timesOutWhenTheNextPollWouldStartTooLate() {
        FakeTransport transport = new FakeTransport().handler(r -> new Transport.Response(200, null, "{\"status\":\"translating\"}"));
        SupertextClient client = builder(transport).pollInterval(Duration.ofSeconds(3)).timeout(Duration.ofSeconds(10)).build();
        SupertextException e = assertThrows(SupertextException.class, () -> client.waitUntilDone("f"));
        assertEquals(ErrorCode.TIMEOUT, e.getCode());
        // Polls at 0, 3, 6, 9 s; one at 12 s would be after the 10 s timeout.
        assertEquals(4, transport.requests.size());
        assertEquals(3, sleeps.size());
    }

    // ------------------------------------------------------------ translateHtml

    @Test
    void translateHtmlUploadsWaitsDownloadsAndDeletes() throws Exception {
        FakeTransport transport = happyPath("<p>Hallo</p>");
        assertEquals("<p>Hallo</p>", client(transport).translateHtml("<p>Hello</p>", "de", "en"));
        assertEquals(List.of(
                "POST " + BASE + "translate/ai/file",
                "GET " + BASE + "translate/ai/file/f%201/status",
                "GET " + BASE + "translate/ai/file/f%201/status",
                "GET " + BASE + "translate/ai/file/f%201/translation",
                "DELETE " + BASE + "translate/ai/file/f%201"), transport.lines());
    }

    @Test
    void deletesAfterAFailedTranslation() {
        FakeTransport transport = new FakeTransport()
                .answer(200, "{\"file_id\":\"f1\"}")
                .answer(200, "{\"status\":\"limit_exceeded\"}")
                .answer(500, "boom");
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).translateHtml("<p>x</p>", "de-DE"));
        assertEquals(ErrorCode.QUOTA_EXCEEDED, e.getCode()); // the delete's own failure doesn't replace it
        assertEquals("DELETE " + BASE + "translate/ai/file/f1", transport.lines().get(2));
    }

    @Test
    void deletesAfterAnEmptyDownload() {
        FakeTransport transport = new FakeTransport()
                .answer(200, "{\"file_id\":\"f1\"}")
                .answer(200, "{\"status\":\"done\"}")
                .answer(200, " \n ")
                .fail(new ConnectException("refused"));
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).translateHtml("<p>x</p>", "de-DE"));
        assertEquals(ErrorCode.INCOMPLETE_RESPONSE, e.getCode());
        assertEquals(4, transport.requests.size());
        assertEquals("DELETE", transport.requests.get(3).getMethod());
    }

    @Test
    void deleteNeverThrows() {
        client(new FakeTransport().answer(404, "{}")).delete("gone");
        client(new FakeTransport().fail(new IOException("down"))).delete("x");
        SupertextClient.builder("").transport(new FakeTransport()).build().delete("x");
        Thread.currentThread().interrupt();
        FakeTransport transport = new FakeTransport().answer(200, "{}");
        client(transport).delete("x"); // still attempted, and the interrupt is kept
        assertTrue(Thread.interrupted());
        assertEquals(1, transport.requests.size());
    }

    @Test
    void networkFailureIsTransportError() {
        FakeTransport transport = new FakeTransport().fail(new ConnectException("Connection refused"));
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).validateApiKey());
        assertEquals(ErrorCode.TRANSPORT_ERROR, e.getCode());
        assertNull(e.getStatus());
        assertEquals("Connection refused", e.getDetail());
        assertTrue(e.getCause() instanceof ConnectException);
    }

    @Test
    void theKeyNeverAppearsInErrors() {
        FakeTransport transport = new FakeTransport()
                .answer(401, "<p>Key " + KEY + " is not valid</p>")
                .fail(new IOException("failed with " + KEY));
        SupertextClient client = client(transport);
        SupertextException http = assertThrows(SupertextException.class, client::validateApiKey);
        SupertextException network = assertThrows(SupertextException.class, client::validateApiKey);
        for (SupertextException e : new SupertextException[] {http, network}) {
            assertFalse(e.getMessage().contains(KEY), e.getMessage());
            assertFalse(String.valueOf(e.getDetail()).contains(KEY));
            assertFalse(e.toString().contains(KEY));
        }
        assertEquals(ErrorCode.AUTHENTICATION_FAILURE, http.getCode());
        assertFalse(client.toString().contains(KEY));
    }

    @Test
    void messagesFollowTheCodeStatusAndDetail() {
        FakeTransport transport = new FakeTransport()
                .answer(400, "{\"error_code\":\"FEATURE_ERROR\",\"feature\":\"politeness\",\"source_lang\":\"de\",\"target_lang\":\"en-US\"}");
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).submit("x", "en-US", "de", Politeness.MORE));
        assertEquals(ErrorCode.UNSUPPORTED_FEATURE, e.getCode());
        assertEquals("politeness", e.getFeature());
        assertEquals("de", e.getSourceLang());
        assertEquals("en-US", e.getTargetLang());
        assertEquals("Supertext does not support this option for the language pair. HTTP 400. Details: " + e.getDetail(), e.getMessage());
        assertEquals("No Supertext API key is configured.", new SupertextException(ErrorCode.MISSING_API_KEY).getMessage());
    }

    @Test
    void getFeaturesReadsThePairsAndSkipsMalformedEntries() throws Exception {
        FakeTransport transport = new FakeTransport().answer(200, "{\"supported_features\":["
                + "{\"source_lang\":\"en\",\"target_lang\":\"de-CH\",\"features\":[\"supports_politeness\",7,\"supports_glossary\"]},"
                + "{\"source_lang\":\"en\",\"target_lang\":\"en-US\"},"
                + "{\"source_lang\":\"en\"},"
                + "{\"source_lang\":1,\"target_lang\":\"fr-CH\"},"
                + "\"junk\",null]}")
                .answer(200, "{}")
                .answer(200, "not json")
                .answer(200, "{\"supported_features\":{}}");
        SupertextClient client = client(transport);
        assertEquals(List.of(
                new FeatureSupport("en", "de-CH", List.of("supports_politeness", "supports_glossary")),
                new FeatureSupport("en", "en-US", List.of())), client.getFeatures());
        assertEquals("GET " + BASE + "features", transport.lines().get(0));
        assertEquals(List.of(), client.getFeatures());
        assertEquals(List.of(), client.getFeatures());
        assertEquals(List.of(), client.getFeatures());
    }

    @Test
    void longDetailIsCut() {
        FakeTransport transport = new FakeTransport().answer(500, "x".repeat(500));
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).validateApiKey());
        assertEquals(200, e.getDetail().length());
    }

    // ------------------------------------------------------------ translateTexts

    /** Answers uploads like the stand-in: prefixes every segment's content. */
    private static FakeTransport echo(String prefix, List<String> uploads) {
        Map<String, String> files = new LinkedHashMap<>();
        return new FakeTransport().handler(request -> {
            String path = request.getUri().getPath();
            if (request.getMethod().equals("POST")) {
                List<String[]> parts = parts(request);
                String html = parts.get(parts.size() - 1)[1];
                uploads.add(html);
                String id = "f" + files.size();
                files.put(id, html.replaceAll("(<div data-st-id=\"\\d+\">)", "$1" + prefix));
                return new Transport.Response(200, null, "{\"file_id\":\"" + id + "\"}");
            }
            String id = path.replaceAll(".*/file/([^/]+).*", "$1");
            if (path.endsWith("/status")) {
                return new Transport.Response(200, null, "{\"status\":\"done\"}");
            }
            if (path.endsWith("/translation")) {
                return new Transport.Response(200, null, files.get(id));
            }
            return new Transport.Response(200, null, "{}");
        });
    }

    @Test
    void translateTextsKeepsOrderAndBlanks() throws Exception {
        List<String> uploads = new ArrayList<>();
        FakeTransport transport = echo("[de] ", uploads);
        Map<String, Segment> segments = new LinkedHashMap<>();
        segments.put("title", Segment.text("Fish & Chips"));
        segments.put("blank", Segment.text(" \n"));
        segments.put("body", Segment.html("<p>Our <b>new</b> shop</p>"));
        segments.put("lines", Segment.text("Line 1\r\nLine 2"));

        Map<String, String> result = client(transport).translateTexts(segments, "de", "en-GB", Politeness.LESS);

        assertEquals(List.of("title", "blank", "body", "lines"), new ArrayList<>(result.keySet()));
        assertEquals("[de] Fish & Chips", result.get("title"));
        assertEquals(" \n", result.get("blank"));
        assertEquals("[de] <p>Our <b>new</b> shop</p>", result.get("body"));
        assertEquals("[de] Line 1\nLine 2", result.get("lines"));
        assertEquals(1, uploads.size());
        assertTrue(uploads.get(0).contains("<div data-st-id=\"0\">Fish &amp; Chips</div>"));
        assertTrue(uploads.get(0).contains("<div data-st-id=\"2\">Line 1<br>Line 2</div>"));
    }

    @Test
    void translateTextsSplitsIntoDocumentsOneAfterTheOther() throws Exception {
        List<String> uploads = new ArrayList<>();
        FakeTransport transport = echo("", uploads);
        Map<String, Segment> segments = new LinkedHashMap<>();
        segments.put("a", Segment.text("12345"));
        segments.put("b", Segment.text("12345"));
        segments.put("c", Segment.text("1"));
        Map<String, String> result = client(transport).translateTexts(segments, "de", null, Politeness.DEFAULT, 10);
        assertEquals(Map.of("a", "12345", "b", "12345", "c", "1"), result);
        assertEquals(2, uploads.size());
        List<String> methods = new ArrayList<>();
        for (Transport.Request r : transport.requests) {
            methods.add(r.getMethod());
        }
        // Second document only after the first one's delete.
        assertEquals(List.of("POST", "GET", "GET", "DELETE", "POST", "GET", "GET", "DELETE"), methods);
    }

    @Test
    void nothingToSendMeansNoRequest() throws Exception {
        FakeTransport transport = new FakeTransport();
        Map<String, Segment> segments = new LinkedHashMap<>();
        segments.put("a", Segment.text(""));
        segments.put("b", Segment.html(" \t"));
        assertEquals(Map.of("a", "", "b", " \t"), client(transport).translateTexts(segments, "de"));
        assertTrue(transport.requests.isEmpty());
    }

    @Test
    void missingSegmentsAreIncompleteAfterTheDelete() {
        FakeTransport transport = happyPath("<html><body><div data-st-id=\"1\">zwei</div></body></html>");
        Map<String, Segment> segments = new LinkedHashMap<>();
        segments.put("one", Segment.text("one"));
        segments.put("two", Segment.text("two"));
        segments.put("three", Segment.text("three"));
        SupertextException e = assertThrows(SupertextException.class, () -> client(transport).translateTexts(segments, "de"));
        assertEquals(ErrorCode.INCOMPLETE_RESPONSE, e.getCode());
        assertEquals("missing: one, three", e.getDetail());
        assertEquals("DELETE", transport.requests.get(transport.requests.size() - 1).getMethod());
    }

    @Test
    void politenessParsing() {
        assertEquals(Politeness.DEFAULT, Politeness.fromValue(null));
        assertEquals(Politeness.MORE, Politeness.fromValue(" More "));
        assertEquals(Politeness.DEFAULT, Politeness.fromValue("formal"));
        assertEquals(Politeness.DEFAULT, Politeness.fromValue("default"));
    }
}
