package com.supertext.sdk;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.List;
import java.util.Map;

/** Loads the shared test vectors from the repository's spec/ folder. */
final class Spec {

    private Spec() {
    }

    static Path dir() {
        Path dir = Paths.get(System.getProperty("supertext.spec.dir", "../../spec"));
        if (!Files.isDirectory(dir)) {
            throw new IllegalStateException("spec folder not found: " + dir.toAbsolutePath());
        }
        return dir;
    }

    @SuppressWarnings("unchecked")
    static Map<String, Object> load(String name) {
        try {
            return (Map<String, Object>) Json.parse(new String(Files.readAllBytes(dir().resolve(name)), StandardCharsets.UTF_8));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    @SuppressWarnings("unchecked")
    static List<Map<String, Object>> list(Map<String, Object> map, String key) {
        return (List<Map<String, Object>>) map.get(key);
    }

    @SuppressWarnings("unchecked")
    static Map<String, String> strings(Object map) {
        return (Map<String, String>) map;
    }
}
