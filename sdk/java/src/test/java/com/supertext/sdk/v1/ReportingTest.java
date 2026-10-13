package com.supertext.sdk.v1;

import com.supertext.sdk.Transport;
import com.supertext.sdk.SdkInfo;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
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

/** spec/reporting.json and the Commando Center's ping cases (spec/commando/callback-vectors.json). */
class ReportingTest {

    private static final String REPORT_URL = "https://center.test/api/v1/report";
    private static final Map<String, Object> SPEC = Spec.load("reporting.json");

    /** Answers GET features and POST report from a step's answers, records which were called. */
    static final class FakeHttp implements Transport {
        final List<String> requests = new ArrayList<>();
        final List<Object> bodies = new ArrayList<>();
        final List<Request> raw = new ArrayList<>();
        private final Object features;
        private final Object report;

        FakeHttp(Object features, Object report) {
            this.features = features;
            this.report = report;
        }

        @Override
        public Response send(Request request) throws IOException {
            raw.add(request);
            boolean isFeatures = request.getUri().toString().endsWith("/features");
            String kind = isFeatures ? "features" : "report";
            requests.add(kind);
            if (!isFeatures) {
                assertEquals(REPORT_URL, request.getUri().toString());
                assertEquals("POST", request.getMethod());
                assertEquals(Duration.ofSeconds(3), request.getTimeout());
                bodies.add(Json.parse(new String(request.getBody(), StandardCharsets.UTF_8)));
            } else {
                assertEquals("https://api.test/v1/features", request.getUri().toString());
            }
            Object answer = isFeatures ? features : report;
            if (!(answer instanceof Map)) {
                throw new AssertionError("unexpected " + kind + " request");
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

    private static Clock at(String iso) {
        return Clock.fixed(Instant.parse(iso), ZoneOffset.UTC);
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> map(Object value) {
        return (Map<String, Object>) value;
    }

    private static String str(Object value) {
        return (String) value;
    }

    /** The parsed JSON form of a check, as the spec writes it. */
    private static Object parsed(ApiKeyCheck check) {
        return check == null ? null : Json.parse(Json.write(check.toMap()));
    }

    private static InstallationReporter.Builder configured(Map<String, Object> config) {
        Map<String, Object> plugin = map(config.get("plugin"));
        Map<String, Object> system = map(config.get("system"));
        InstallationReporter.Builder b = InstallationReporter.builder()
                .enabled(!Boolean.FALSE.equals(config.get("enabled")))
                .siteUrl(str(config.get("siteUrl")))
                .plugin(str(plugin.get("name")), str(plugin.get("version")))
                .system(str(system.get("name")), str(system.get("version")))
                .sdkVersion(str(config.get("sdkVersion")))
                .callbackUrl(str(config.get("callbackUrl")));
        if (config.get("runtime") != null) {
            Map<String, Object> runtime = map(config.get("runtime"));
            b.runtime(str(runtime.get("name")), str(runtime.get("version")));
        }
        return b;
    }

    // ------------------------------------------------------------ vectors

    @Test
    void defaults() {
        Map<String, Object> d = map(SPEC.get("defaults"));
        assertEquals(d.get("reportUrl"), Reporting.DEFAULT_REPORT_URL);
        assertEquals(((Number) d.get("reportTimeoutSeconds")).longValue(), Reporting.REPORT_TIMEOUT.getSeconds());
        assertEquals(((Number) d.get("keyCheckTimeoutSeconds")).longValue(), Reporting.KEY_CHECK_TIMEOUT.getSeconds());
        assertEquals(((Number) d.get("failureBackoffSeconds")).longValue(), Reporting.FAILURE_BACKOFF.getSeconds());
    }

    @TestFactory
    Stream<DynamicTest> fingerprint() {
        return Spec.list(SPEC, "fingerprint").stream().map(c -> DynamicTest.dynamicTest("fingerprint " + c.get("apiKey"),
                () -> assertEquals(c.get("out"), ApiKeyCheck.fingerprint(str(c.get("apiKey"))))));
    }

    @TestFactory
    Stream<DynamicTest> apiKeyCheck() {
        return Spec.list(SPEC, "apiKeyCheck").stream().map(c -> DynamicTest.dynamicTest("apiKeyCheck " + Json.write(c.get("answer")), () -> {
            FakeHttp http = new FakeHttp(c.get("answer"), null);
            List<Duration> sleeps = new ArrayList<>();
            SupertextClient client = SupertextClient.builder(str(c.get("apiKey")))
                    .baseUrl("https://api.test/v1/")
                    .transport(http)
                    .sleeper(sleeps::add)
                    .wallClock(at("2026-10-13T08:00:00.500Z"))
                    .build();
            ApiKeyCheck check = client.checkApiKey();
            Map<String, Object> expected = new LinkedHashMap<>();
            expected.put("status", c.get("status"));
            expected.put("checkedAt", "2026-10-13T08:00:00Z");
            expected.put("httpStatus", c.get("httpStatus"));
            expected.put("errorCode", c.get("errorCode"));
            expected.put("fingerprint", ApiKeyCheck.fingerprint(str(c.get("apiKey"))));
            assertEquals(expected, parsed(check));
            int requests = c.get("requests") == null ? 1 : ((Number) c.get("requests")).intValue();
            assertEquals(requests, http.requests.size());
            for (Transport.Request r : http.raw) {
                assertEquals(Duration.ofSeconds(10), r.getTimeout());
            }
        }));
    }

    @TestFactory
    Stream<DynamicTest> enabled() {
        return Spec.list(SPEC, "enabled").stream().map(c -> DynamicTest.dynamicTest("enabled " + Json.write(c),
                () -> assertEquals(c.get("out"), Reporting.reportsEnabled((Boolean) c.get("setting"), str(c.get("env"))))));
    }

    @TestFactory
    Stream<DynamicTest> ping() {
        Map<String, Object> ping = map(SPEC.get("ping"));
        String plugin = str(ping.get("plugin"));
        return Spec.list(ping, "cases").stream().map(c -> DynamicTest.dynamicTest(
                "ping " + Json.write(c.get("challenge")) + " enabled=" + c.get("enabled"), () -> {
                    PingResponse r = PingResponse.of(str(c.get("challenge")), plugin, Boolean.TRUE.equals(c.get("enabled")));
                    assertEquals(((Number) c.get("status")).intValue(), r.getStatus());
                    assertEquals(c.get("body"), Json.parse(r.getBody()));
                    Map<String, String> headers = new LinkedHashMap<>();
                    headers.put("Content-Type", "application/json");
                    headers.put("Cache-Control", "no-store");
                    assertEquals(headers, r.getHeaders());
                }));
    }

    @TestFactory
    Stream<DynamicTest> scenarios() {
        Map<String, Object> baseConfig = map(SPEC.get("config"));
        return Spec.list(SPEC, "scenarios").stream().map(scenario -> DynamicTest.dynamicTest(str(scenario.get("name")), () -> {
            Map<String, Object> config = new LinkedHashMap<>(baseConfig);
            if (scenario.get("config") != null) {
                config.putAll(map(scenario.get("config")));
            }
            ReporterState state = ReporterState.fromJson(Json.write(scenario.get("state")));
            for (Map<String, Object> step : Spec.list(scenario, "steps")) {
                String action = str(step.get("action"));
                FakeHttp http = new FakeHttp(step.get("features"), step.get("report"));
                InstallationReporter reporter = configured(config)
                        .reportUrl(REPORT_URL)
                        .apiBaseUrl("https://api.test/v1/")
                        .transport(http)
                        .sleeper(d -> { })
                        .clock(at(str(step.get("now"))))
                        .build();
                String apiKey = str(step.get("apiKey"));
                ReporterResult r;
                switch (action) {
                    case "afterSettingsSaved":
                        r = reporter.afterSettingsSaved(state, apiKey);
                        break;
                    case "onRequest":
                        r = reporter.onRequest(state, apiKey);
                        break;
                    case "authenticationFailed":
                        r = step.get("httpStatus") == null ? reporter.authenticationFailed(state, apiKey)
                                : reporter.authenticationFailed(state, apiKey, ((Number) step.get("httpStatus")).intValue());
                        break;
                    case "testConnection":
                        r = reporter.testConnection(state, apiKey);
                        break;
                    case "reportCheck":
                        r = reporter.reportCheck(state);
                        break;
                    default:
                        throw new AssertionError("unknown action " + action);
                }
                Map<String, Object> expect = map(step.get("expect"));
                assertEquals(expect.get("requests"), http.requests, action + " requests");
                assertEquals(expect.get("event"), r.getEvent() == null ? null : r.getEvent().value(), action + " event");
                assertEquals(expect.get("sent"), r.isSent(), action + " sent");
                assertEquals(expect.get("check"), parsed(r.getCheck()), action + " check");
                assertEquals(expect.get("state"), Json.parse(r.getState().toJson()), action + " state");
                if (expect.get("reportBody") != null) {
                    assertEquals(expect.get("reportBody"), http.bodies.get(0), action + " report body");
                }
                // The stored form survives a round trip.
                state = ReporterState.fromJson(r.getState().toJson());
                assertEquals(r.getState(), state);
                assertEquals(state, ReporterState.fromJson(Json.write(expect.get("state"))));
            }
        }));
    }

    // ------------------------------------------------------------ spec/commando

    @TestFactory
    Stream<DynamicTest> commandoPingEndpoint() {
        Map<String, Object> endpoint = map(Spec.load("commando/callback-vectors.json").get("endpoint"));
        return Spec.list(endpoint, "cases").stream().map(c -> DynamicTest.dynamicTest("ping endpoint " + Json.write(c.get("challenge")), () -> {
            PingResponse r = PingResponse.of(str(c.get("challenge")), "Some-Supertext-Translation", true);
            assertEquals(((Number) c.get("status")).intValue(), r.getStatus());
            Map<String, Object> expected = new LinkedHashMap<>(map(c.get("body")));
            if (expected.containsKey("plugin")) {
                expected.put("plugin", "Some-Supertext-Translation");
            }
            assertEquals(expected, Json.parse(r.getBody()));
        }));
    }

    @Test
    void reportsHaveTheFieldsOfTheCenterSchema() {
        Map<String, Object> schema = Spec.load("commando/report-v1.schema.json");
        Map<String, Object> body = map(Json.parse(configured(map(SPEC.get("config"))).build()
                .buildReportJson(ReportEvent.SETTINGS_SAVED, ReporterState.EMPTY)));
        for (Object field : (List<?>) schema.get("required")) {
            assertTrue(body.containsKey(field), "missing " + field);
        }
        for (String field : body.keySet()) {
            assertTrue(map(schema.get("properties")).containsKey(field), "unknown " + field);
        }
    }

    // ------------------------------------------------------------ reporter

    @Test
    void neverSendsTheKey() {
        Map<String, Object> ok = new LinkedHashMap<>();
        ok.put("status", 200.0);
        ok.put("body", "{}");
        Map<String, Object> accepted = new LinkedHashMap<>();
        accepted.put("status", 202.0);
        FakeHttp http = new FakeHttp(ok, accepted);
        ReporterResult r = configured(map(SPEC.get("config"))).reportUrl(REPORT_URL).apiBaseUrl("https://api.test/v1/")
                .transport(http).build().afterSettingsSaved(null, "secret-key-123");
        assertTrue(r.isSent());
        assertFalse(Json.write(http.bodies).contains("secret-key-123"));
        assertFalse(r.getState().toJson().contains("secret-key-123"));
        assertFalse(r.toString().contains("secret-key-123"));
        assertTrue(http.raw.get(1).getHeaders().get("User-Agent").startsWith("supertext-sdk-java Directus-Supertext-Translation/0.2.0"));
    }

    @Test
    void neverThrowsWhenTheTransportDoes() {
        InstallationReporter reporter = configured(map(SPEC.get("config"))).reportUrl(REPORT_URL).apiBaseUrl("https://api.test/v1/")
                .transport(request -> {
                    throw new IllegalStateException("boom");
                }).clock(at("2026-10-13T08:00:00Z")).build();
        ReporterResult r = reporter.afterSettingsSaved(ReporterState.EMPTY, "k1");
        assertFalse(r.isSent());
        assertEquals(ApiKeyCheck.Status.ERROR, r.getCheck().getStatus());
        assertEquals("2026-10-13T08:00:00Z", r.getState().getLastFailureAt());
    }

    @Test
    void keepsTheInterruptFlag() {
        InstallationReporter reporter = configured(map(SPEC.get("config"))).reportUrl(REPORT_URL).apiBaseUrl("https://api.test/v1/")
                .transport(request -> {
                    throw new InterruptedException();
                }).build();
        try {
            ReporterResult r = reporter.afterSettingsSaved(ReporterState.EMPTY, "k1");
            assertFalse(r.isSent());
            assertEquals(ApiKeyCheck.Status.UNREACHABLE, r.getCheck().getStatus());
            assertTrue(Thread.currentThread().isInterrupted());
        } finally {
            Thread.interrupted();
        }
    }

    @Test
    void treatsABrokenStoredStateAsEmpty() {
        assertEquals(ReporterState.EMPTY, ReporterState.fromJson("not json"));
        assertEquals(ReporterState.EMPTY, ReporterState.fromJson(null));
        assertEquals(ReporterState.EMPTY, ReporterState.fromJson("[1,2]"));
        assertNull(ReporterState.fromJson("{\"keyCheck\":{\"status\":\"weird\",\"checkedAt\":\"x\"}}").getKeyCheck());
        assertEquals("{\"reportedVersions\":null,\"reportedKeyStatus\":null,\"lastFailureAt\":null,\"keyCheck\":null}",
                ReporterState.EMPTY.toJson());
    }

    @Test
    void detectsTheRuntime() {
        Map<String, Object> config = new LinkedHashMap<>(map(SPEC.get("config")));
        config.remove("runtime");
        Map<String, Object> body = configured(config).build().buildReport(ReportEvent.CHECK, ReporterState.EMPTY);
        Map<String, Object> runtime = new LinkedHashMap<>();
        runtime.put("name", "Java");
        runtime.put("version", System.getProperty("java.version"));
        assertEquals(runtime, body.get("runtime"));
    }

    @Test
    void sdkVersionMatchesThePom() throws IOException {
        String pom = new String(Files.readAllBytes(Paths.get("pom.xml")), StandardCharsets.UTF_8);
        Matcher m = Pattern.compile("<artifactId>supertext-sdk</artifactId>\\s*<version>([^<]+)</version>").matcher(pom);
        assertTrue(m.find());
        assertEquals(m.group(1), SdkInfo.VERSION);
        assertEquals(SdkInfo.VERSION, InstallationReporter.builder().siteUrl("x").plugin("p", "1").system("s", null).build()
                .buildReport(ReportEvent.CHECK, null).get("sdk_version"));
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
