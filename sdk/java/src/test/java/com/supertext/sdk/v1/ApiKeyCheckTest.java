package com.supertext.sdk.v1;

import com.supertext.sdk.SdkInfo;
import com.supertext.sdk.Transport;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

/** spec/api-key-check.json: the API key check Supertext Plugin Health reports. */
class ApiKeyCheckTest {

    private static final Map<String, Object> SPEC = Spec.load("api-key-check.json");

    /** Answers GET features from a case's answer, records the requests. */
    static final class FakeHttp implements Transport {
        final List<Request> requests = new ArrayList<>();
        private final Object answer;

        FakeHttp(Object answer) {
            this.answer = answer;
        }

        @Override
        public Response send(Request request) throws IOException {
            requests.add(request);
            assertEquals("https://api.test/v1/features", request.getUri().toString());
            assertEquals("GET", request.getMethod());
            if (!(answer instanceof Map)) {
                throw new AssertionError("unexpected request");
            }
            Map<?, ?> a = (Map<?, ?>) answer;
            if (Boolean.TRUE.equals(a.get("transportError"))) {
                throw new IOException("connection refused");
            }
            int status = a.get("status") == null ? 200 : ((Number) a.get("status")).intValue();
            Object body = a.get("body");
            return new Response(status, Collections.emptyMap(), body == null ? "" : (String) body);
        }
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> map(Object value) {
        return (Map<String, Object>) value;
    }

    private static String str(Object value) {
        return (String) value;
    }

    @Test
    void timeout() {
        Map<String, Object> d = map(SPEC.get("defaults"));
        assertEquals(((Number) d.get("keyCheckTimeoutSeconds")).longValue(), ApiKeyCheck.TIMEOUT.getSeconds());
    }

    @TestFactory
    Stream<DynamicTest> fingerprint() {
        return Spec.list(SPEC, "fingerprint").stream().map(c -> DynamicTest.dynamicTest("fingerprint " + c.get("apiKey"),
                () -> assertEquals(c.get("out"), ApiKeyCheck.fingerprint(str(c.get("apiKey"))))));
    }

    @TestFactory
    Stream<DynamicTest> apiKeyCheck() {
        return Spec.list(SPEC, "apiKeyCheck").stream().map(c -> DynamicTest.dynamicTest("apiKeyCheck " + Json.write(c.get("answer")), () -> {
            FakeHttp http = new FakeHttp(c.get("answer"));
            SupertextClient client = SupertextClient.builder(str(c.get("apiKey")))
                    .baseUrl("https://api.test/v1/")
                    .transport(http)
                    .sleeper(d -> { })
                    .wallClock(Clock.fixed(Instant.parse("2026-10-13T08:00:00.500Z"), ZoneOffset.UTC))
                    .build();
            ApiKeyCheck check = client.checkApiKey();
            Map<String, Object> expected = new LinkedHashMap<>();
            expected.put("status", c.get("status"));
            expected.put("checkedAt", "2026-10-13T08:00:00Z");
            expected.put("httpStatus", c.get("httpStatus"));
            expected.put("errorCode", c.get("errorCode"));
            expected.put("fingerprint", ApiKeyCheck.fingerprint(str(c.get("apiKey"))));
            assertEquals(expected, Json.parse(Json.write(check.toMap())));
            int requests = c.get("requests") == null ? 1 : ((Number) c.get("requests")).intValue();
            assertEquals(requests, http.requests.size());
            for (Transport.Request r : http.requests) {
                assertEquals(Duration.ofSeconds(10), r.getTimeout());
            }
        }));
    }

    @Test
    void sdkVersionMatchesThePom() throws IOException {
        String pom = new String(Files.readAllBytes(Paths.get("pom.xml")), StandardCharsets.UTF_8);
        Matcher m = Pattern.compile("<artifactId>supertext-sdk</artifactId>\\s*<version>([^<]+)</version>").matcher(pom);
        assertTrue(m.find());
        assertEquals(m.group(1), SdkInfo.VERSION);
    }

    @Test
    void jsonWriterRoundTrips() {
        Map<String, Object> value = new LinkedHashMap<>();
        value.put("text", "quote \" backslash \\ newline \n tab \t ctrl \u0001 umlaut Ä");
        value.put("int", 200);
        value.put("double", 1.5);
        value.put("whole", 3.0);
        value.put("list", java.util.Arrays.asList(true, null, "x"));
        String json = Json.write(value);
        assertTrue(json.contains("\"whole\":3,"));
        Map<String, Object> back = map(Json.parse(json));
        assertEquals(value.get("text"), back.get("text"));
        assertEquals(200.0, back.get("int"));
        assertEquals(value.get("list"), back.get("list"));
    }
}
