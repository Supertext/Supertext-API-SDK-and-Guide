package com.supertext.sdk.v1;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.stream.Stream;

import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

/** Every case in spec/*.json (the shared test vectors of all Supertext SDKs). */
class SpecVectorsTest {

    // ------------------------------------------------------------ languages.json

    @Test
    void languageListsMatchTheSpec() {
        Map<String, Object> spec = Spec.load("languages.json");
        List<SourceLanguage> sources = new ArrayList<>();
        for (Map<String, Object> l : Spec.list(spec, "sources")) {
            sources.add(new SourceLanguage((String) l.get("code"), (String) l.get("name")));
        }
        List<TargetLanguage> targets = new ArrayList<>();
        for (Map<String, Object> l : Spec.list(spec, "targets")) {
            targets.add(new TargetLanguage((String) l.get("code"), (String) l.get("name"), (Boolean) l.get("politeness")));
        }
        assertEquals(sources, Languages.SOURCES);
        assertEquals(targets, Languages.TARGETS);
        assertEquals(Spec.strings(spec.get("defaultTargets")), Languages.DEFAULT_TARGETS);
        List<String> order = new ArrayList<>();
        for (TargetLanguage l : Languages.targetOptions()) {
            order.add(l.getCode());
        }
        assertEquals(spec.get("targetOptions"), order);
        assertEquals(new java.util.HashSet<>(Languages.TARGETS), new java.util.HashSet<>(Languages.targetOptions()));
    }

    /** Runs {@code call}; expects either {@code c.out} or the error {@code c.error}. */
    private static void outOrError(Map<String, Object> c, java.util.function.Supplier<String> call) {
        if (c.containsKey("error")) {
            SupertextException e = assertThrows(SupertextException.class, call::get);
            assertEquals(c.get("error"), e.getCode().value());
            assertNull(e.getStatus());
        } else {
            assertEquals(c.get("out"), call.get());
        }
    }

    @TestFactory
    Stream<DynamicTest> languages() {
        Map<String, Object> spec = Spec.load("languages.json");
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(spec, "matchTarget")) {
            String in = (String) c.get("in");
            tests.add(DynamicTest.dynamicTest("matchTarget '" + in + "'", () -> {
                assertEquals(c.get("out"), Languages.matchTarget(in));
                assertEquals(c.get("out") != null, Languages.isSupportedTarget(in));
            }));
        }
        for (Map<String, Object> c : Spec.list(spec, "defaultTarget")) {
            tests.add(DynamicTest.dynamicTest("defaultTarget '" + c.get("in") + "'",
                    () -> assertEquals(c.get("out"), Languages.defaultTarget((String) c.get("in")))));
        }
        for (Map<String, Object> c : Spec.list(spec, "suggestTarget")) {
            tests.add(DynamicTest.dynamicTest("suggestTarget '" + c.get("in") + "'",
                    () -> assertEquals(c.get("out"), Languages.suggestTarget((String) c.get("in")))));
        }
        for (Map<String, Object> c : Spec.list(spec, "matchSource")) {
            tests.add(DynamicTest.dynamicTest("matchSource '" + c.get("in") + "'",
                    () -> assertEquals(c.get("out"), Languages.matchSource((String) c.get("in")))));
        }
        for (Map<String, Object> c : Spec.list(spec, "resolveTarget")) {
            String in = (String) c.get("in");
            Map<String, String> mapping = Spec.strings(c.get("mapping"));
            tests.add(DynamicTest.dynamicTest("target '" + in + "' " + mapping, () -> {
                outOrError(c, () -> Languages.target(in, mapping));
                if (c.containsKey("error")) {
                    SupertextException e = assertThrows(SupertextException.class, () -> Languages.target(in, mapping));
                    assertEquals(in.strip().isEmpty() ? null : in.strip(), e.getTargetLang());
                    assertNull(e.getSourceLang());
                }
            }));
        }
        for (Map<String, Object> c : Spec.list(spec, "resolveSource")) {
            String in = (String) c.get("in");
            Map<String, String> mapping = Spec.strings(c.get("mapping"));
            tests.add(DynamicTest.dynamicTest("source '" + in + "' " + mapping, () -> {
                outOrError(c, () -> Languages.source(in, mapping));
                if (c.containsKey("error")) {
                    SupertextException e = assertThrows(SupertextException.class, () -> Languages.source(in, mapping));
                    assertEquals(in.strip(), e.getSourceLang());
                    assertNull(e.getTargetLang());
                }
            }));
        }
        for (Map<String, Object> c : Spec.list(spec, "supportsPoliteness")) {
            tests.add(DynamicTest.dynamicTest("supportsPoliteness " + c.get("in"),
                    () -> assertEquals(c.get("out"), Languages.supportsPoliteness((String) c.get("in")))));
        }
        return tests.stream();
    }

    // ------------------------------------------------------------ auth-header.json

    @TestFactory
    Stream<DynamicTest> authHeader() {
        return Spec.list(Spec.load("auth-header.json"), "cases").stream().map(c -> DynamicTest.dynamicTest(
                "authHeader " + String.valueOf(c.get("in")).replace("\n", "\\n"), () -> {
                    String in = (String) c.get("in");
                    if (c.containsKey("error")) {
                        SupertextException e = assertThrows(SupertextException.class, () -> SupertextClient.authHeader(in));
                        assertEquals(c.get("error"), e.getCode().value());
                    } else {
                        assertEquals(c.get("out"), SupertextClient.authHeader(in));
                    }
                }));
    }

    // ------------------------------------------------------------ retry-delay.json

    /** Randomness that always gives the lowest or the highest jitter. */
    private static Random fixed(boolean highest) {
        return new Random() {
            private static final long serialVersionUID = 1L;

            @Override
            public int nextInt(int bound) {
                return highest ? bound - 1 : 0;
            }
        };
    }

    @TestFactory
    Stream<DynamicTest> retryDelay() {
        Map<String, Object> spec = Spec.load("retry-delay.json");
        assertEquals(4, ((Double) spec.get("maxRetries")).intValue());
        return Spec.list(spec, "cases").stream().map(c -> DynamicTest.dynamicTest(
                "retryDelay attempt " + c.get("attempt") + " retryAfter " + c.get("retryAfter"), () -> {
                    int attempt = ((Double) c.get("attempt")).intValue();
                    String retryAfter = (String) c.get("retryAfter");
                    long min = ((Double) c.get("min")).longValue();
                    long max = ((Double) c.get("max")).longValue();
                    assertEquals(min, SupertextClient.retryDelayMs(attempt, retryAfter, fixed(false)));
                    assertEquals(max, SupertextClient.retryDelayMs(attempt, retryAfter, fixed(true)));
                    for (int i = 0; i < 50; i++) {
                        long delay = SupertextClient.retryDelayMs(attempt, retryAfter);
                        assertTrue(delay >= min && delay <= max, "delay " + delay);
                    }
                }));
    }

    // ------------------------------------------------------------ errors.json

    @TestFactory
    Stream<DynamicTest> httpErrors() {
        return Spec.list(Spec.load("errors.json"), "http").stream().map(c -> DynamicTest.dynamicTest(
                "HTTP " + c.get("status") + " " + c.get("body"), () -> {
                    int status = ((Double) c.get("status")).intValue();
                    String body = (String) c.get("body");
                    FakeTransport transport = new FakeTransport().answer(status, body);
                    SupertextClient client = SupertextClient.builder("key-1234").transport(transport).maxRetries(0).build();
                    SupertextException e = assertThrows(SupertextException.class, client::validateApiKey);
                    assertEquals(c.get("code"), e.getCode().value());
                    assertEquals(status, e.getStatus());
                    assertEquals(c.get("apiErrorCode"), e.getApiErrorCode());
                    assertEquals(c.get("sourceLang"), e.getSourceLang());
                    assertEquals(c.get("targetLang"), e.getTargetLang());
                    assertEquals(c.get("detail"), e.getDetail());
                    assertEquals(c.get("feature"), e.getFeature());
                }));
    }

    @TestFactory
    Stream<DynamicTest> fileStatuses() {
        return Spec.list(Spec.load("errors.json"), "fileStatus").stream().map(c -> DynamicTest.dynamicTest(
                "status " + c.get("status"), () -> {
                    String status = (String) c.get("status");
                    FakeTransport transport = new FakeTransport()
                            .answer(200, "{\"status\":\"" + status + "\"}")
                            .answer(200, "{\"status\":\"done\"}");
                    SupertextClient client = SupertextClient.builder("key").transport(transport).sleeper(d -> { }).build();
                    if (c.get("code") == null) {
                        client.waitUntilDone("f1");
                    } else {
                        SupertextException e = assertThrows(SupertextException.class, () -> client.waitUntilDone("f1"));
                        assertEquals(c.get("code"), e.getCode().value());
                        assertNull(e.getStatus());
                    }
                }));
    }

    @Test
    void errorCodesMatchTheSpec() {
        @SuppressWarnings("unchecked")
        Map<String, Object> codes = (Map<String, Object>) Spec.load("errors.json").get("codes");
        List<String> ours = new ArrayList<>();
        for (ErrorCode code : ErrorCode.values()) {
            ours.add(code.value());
        }
        assertEquals(new ArrayList<>(codes.keySet()), ours);
    }

    // ------------------------------------------------------------ html-document.json

    private static List<Segment> segments(Object list, boolean withText) {
        List<Segment> out = new ArrayList<>();
        for (Object o : (List<?>) list) {
            Map<?, ?> m = (Map<?, ?>) o;
            out.add(new Segment(withText ? (String) m.get("text") : "", Boolean.TRUE.equals(m.get("html"))));
        }
        return out;
    }

    @TestFactory
    Stream<DynamicTest> htmlDocument() {
        Map<String, Object> spec = Spec.load("html-document.json");
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(spec, "build")) {
            tests.add(DynamicTest.dynamicTest("build: " + c.get("name"),
                    () -> assertEquals(c.get("html"), HtmlDocument.build(segments(c.get("segments"), true)))));
        }
        for (Map<String, Object> c : Spec.list(spec, "parse")) {
            tests.add(DynamicTest.dynamicTest("parse: " + c.get("name"),
                    () -> assertEquals(c.get("out"), HtmlDocument.parse((String) c.get("html"), segments(c.get("segments"), false)))));
        }
        return tests.stream();
    }

    // ------------------------------------------------------------ batching.json

    @TestFactory
    Stream<DynamicTest> batching() {
        Map<String, Object> spec = Spec.load("batching.json");
        assertEquals(SupertextClient.DEFAULT_MAX_CHARACTERS, ((Double) spec.get("defaultMaxCharacters")).intValue());
        return Spec.list(spec, "cases").stream().map(c -> DynamicTest.dynamicTest((String) c.get("name"), () -> {
            Map<String, Segment> segments = new LinkedHashMap<>();
            for (Object o : (List<?>) c.get("segments")) {
                Map<?, ?> m = (Map<?, ?>) o;
                segments.put((String) m.get("key"), new Segment((String) m.get("text"), Boolean.TRUE.equals(m.get("html"))));
            }
            int max = ((Double) c.get("maxCharacters")).intValue();
            assertEquals(c.get("batches"), SupertextClient.batches(segments, max));
        }));
    }

    // ------------------------------------------------------------ corrections.json

    private static String ackBody(int from, int to, String version) {
        StringBuilder out = new StringBuilder("{\"corrections\":[");
        for (int i = from; i <= to; i++) {
            out.append(i == from ? "" : ",").append("{\"id\":\"").append(i).append("\",\"version\":\"").append(version).append("\"}");
        }
        return out.append("]}").toString();
    }

    @TestFactory
    Stream<DynamicTest> corrections() {
        return Spec.list(Spec.load("corrections.json"), "scenarios").stream().map(c -> DynamicTest.dynamicTest(
                "corrections " + c.get("name"), () -> {
                    java.util.Deque<Map<String, Object>> exchanges = new java.util.ArrayDeque<>(Spec.list(c, "exchanges"));
                    @SuppressWarnings("unchecked")
                    Map<String, Object> generate = (Map<String, Object>) c.get("generate");
                    String version = generate == null ? "" : (String) generate.get("version");
                    FakeTransport transport = new FakeTransport().handler(request -> {
                        Map<String, Object> x = exchanges.poll();
                        if (x == null) {
                            throw new AssertionError("unexpected " + request.getMethod() + " " + request.getUri());
                        }
                        @SuppressWarnings("unchecked")
                        Map<String, Object> req = (Map<String, Object>) x.get("request");
                        assertEquals(req.get("method"), request.getMethod());
                        assertEquals("https://review.test/v1/" + req.get("path"), request.getUri().toString());
                        String body = FakeTransport.body(request);
                        if (req.containsKey("body")) {
                            assertEquals(req.get("body"), body);
                        }
                        if (req.containsKey("bodyIds")) {
                            List<?> ids = (List<?>) req.get("bodyIds");
                            assertEquals(ackBody(((Double) ids.get(0)).intValue(), ((Double) ids.get(1)).intValue(), version), body);
                        }
                        if (body != null) {
                            assertEquals("application/json", request.getHeaders().get("Content-Type"));
                        }
                        @SuppressWarnings("unchecked")
                        Map<String, Object> res = (Map<String, Object>) x.get("response");
                        return new com.supertext.sdk.Transport.Response(((Double) res.get("status")).intValue(), java.util.Collections.emptyMap(), (String) res.get("body"));
                    });
                    SupertextClient client = SupertextClient.builder("key-1").baseUrl("https://review.test/v1/").transport(transport)
                            .sleeper(ms -> { }).build();
                    List<Correction> corrections = new java.util.ArrayList<>();
                    if (generate != null) {
                        for (int i = 1; i <= ((Double) generate.get("count")).intValue(); i++) {
                            corrections.add(new Correction(String.valueOf(i), "", null, "", Politeness.DEFAULT, "", "", "", "", "", version));
                        }
                    } else if (c.containsKey("corrections")) {
                        for (Map<String, Object> item : Spec.list(c, "corrections")) {
                            corrections.add(new Correction((String) item.get("id"), "", null, "", Politeness.DEFAULT, "", "", "", "", "", (String) item.get("version")));
                        }
                    }
                    org.junit.jupiter.api.function.ThrowingSupplier<Object> run = () -> {
                        if ("pending".equals(c.get("call"))) {
                            @SuppressWarnings("unchecked")
                            Map<String, Object> o = (Map<String, Object>) c.get("options");
                            String p = (String) o.get("politeness");
                            List<Object> out = new java.util.ArrayList<>();
                            for (Correction x : client.pendingCorrections((String) o.get("targetLang"), (String) o.get("sourceLang"),
                                    p == null ? null : Politeness.fromValue(p))) {
                                Map<String, Object> m = new java.util.LinkedHashMap<>();
                                m.put("id", x.getId());
                                m.put("segmentId", x.getSegmentId());
                                m.put("sourceLang", x.getSourceLang());
                                m.put("targetLang", x.getTargetLang());
                                m.put("politeness", x.getPoliteness().value());
                                m.put("sourceHtml", x.getSourceHtml());
                                m.put("previousHtml", x.getPreviousHtml());
                                m.put("html", x.getHtml());
                                m.put("status", x.getStatus());
                                m.put("correctedAt", x.getCorrectedAt());
                                m.put("version", x.getVersion());
                                out.add(m);
                            }
                            return out;
                        }
                        return (double) client.acknowledgeCorrections(corrections);
                    };
                    if (c.containsKey("error")) {
                        SupertextException e = assertThrows(SupertextException.class, run::get);
                        assertEquals(c.get("error"), e.getCode().value());
                    } else {
                        assertEquals(c.get("result"), run.get());
                    }
                    assertEquals(0, exchanges.size());
                }));
    }
}
