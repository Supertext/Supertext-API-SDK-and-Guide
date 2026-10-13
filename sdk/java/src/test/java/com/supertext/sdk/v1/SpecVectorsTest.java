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
}
