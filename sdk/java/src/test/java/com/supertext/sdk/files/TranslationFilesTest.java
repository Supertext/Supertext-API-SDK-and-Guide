package com.supertext.sdk.files;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.supertext.sdk.v1.Spec;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;

import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;
import org.junit.jupiter.api.function.Executable;

/** Every case in spec/translation-file.json. */
class TranslationFilesTest {

    private static final Map<String, Object> SPEC = Spec.load("translation-file.json");

    private static FileSegment segment(Object o) {
        Map<String, Object> s = cast(o);
        String key = s.containsKey("key") ? (String) s.get("key") : "";
        return new FileSegment(key, (String) s.get("text"), Boolean.TRUE.equals(s.get("html")));
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> cast(Object o) {
        return (Map<String, Object>) o;
    }

    @SuppressWarnings("unchecked")
    private static List<FileSegment> segments(Object o) {
        List<FileSegment> out = new ArrayList<>();
        for (Object s : (List<Object>) o) {
            out.add(segment(s));
        }
        return out;
    }

    private static FileJob job(Object o) {
        Map<String, Object> j = cast(o);
        return new FileJob((String) j.get("plugin"), (String) j.get("reference"), (String) j.get("sourceLanguage"),
                (String) j.get("targetLanguage"), segments(j.get("segments")));
    }

    @SuppressWarnings("unchecked")
    private static List<FileJob> jobs(Object o) {
        List<FileJob> out = new ArrayList<>();
        for (Object j : (List<Object>) o) {
            out.add(job(j));
        }
        return out;
    }

    private static void expectError(Map<String, Object> c, Executable call) {
        TranslationFileException e = assertThrows(TranslationFileException.class, call);
        assertEquals(c.get("error"), e.getCode().value());
    }

    /** A file as the spec writes it (camelCase JSON). */
    private static Map<String, Object> describe(TranslationFile f) {
        Map<String, Object> out = new LinkedHashMap<>();
        out.put("format", f.getFormat().value());
        out.put("plugin", f.getPlugin());
        out.put("reference", f.getReference());
        out.put("sourceLanguage", f.getSourceLanguage());
        out.put("targetLanguage", f.getTargetLanguage());
        List<Object> units = new ArrayList<>();
        for (FileUnit u : f.getUnits()) {
            Map<String, Object> unit = new LinkedHashMap<>();
            unit.put("key", u.getKey());
            unit.put("checksum", u.getChecksum());
            units.add(unit);
        }
        out.put("units", units);
        return out;
    }

    private static Map<String, Object> describe(ImportedSegment s) {
        Map<String, Object> out = new LinkedHashMap<>();
        out.put("key", s.getKey());
        out.put("status", s.getStatus().value());
        out.put("text", s.getText());
        return out;
    }

    @TestFactory
    Stream<DynamicTest> checksum() {
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(SPEC, "checksum")) {
            tests.add(DynamicTest.dynamicTest("checksum " + c.get("segment"),
                    () -> assertEquals(c.get("out"), TranslationFiles.checksum(segment(c.get("segment"))))));
        }
        return tests.stream();
    }

    @TestFactory
    Stream<DynamicTest> exportXliff() {
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(SPEC, "exportXliff")) {
            tests.add(DynamicTest.dynamicTest("exportXliff: " + c.get("name"), () -> {
                if (c.containsKey("error")) {
                    expectError(c, () -> TranslationFiles.exportXliff(jobs(c.get("jobs"))));
                } else {
                    assertEquals(c.get("out"), TranslationFiles.exportXliff(jobs(c.get("jobs"))));
                }
            }));
        }
        return tests.stream();
    }

    @TestFactory
    Stream<DynamicTest> exportHtml() {
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(SPEC, "exportHtml")) {
            tests.add(DynamicTest.dynamicTest("exportHtml: " + c.get("name"), () -> {
                if (c.containsKey("error")) {
                    expectError(c, () -> TranslationFiles.exportHtml(job(c.get("job"))));
                } else {
                    assertEquals(c.get("out"), TranslationFiles.exportHtml(job(c.get("job"))));
                }
            }));
        }
        return tests.stream();
    }

    @TestFactory
    Stream<DynamicTest> read() {
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(SPEC, "read")) {
            tests.add(DynamicTest.dynamicTest("read: " + c.get("name"), () -> {
                String content;
                if (c.containsKey("repeat")) {
                    Map<String, Object> repeat = cast(c.get("repeat"));
                    content = ((String) repeat.get("text")).repeat(((Double) repeat.get("times")).intValue());
                } else {
                    content = (String) c.get("content");
                }
                if (c.containsKey("error")) {
                    expectError(c, () -> TranslationFiles.read(content));
                } else {
                    List<Object> files = new ArrayList<>();
                    for (TranslationFile f : TranslationFiles.read(content)) {
                        files.add(describe(f));
                    }
                    assertEquals(c.get("files"), files);
                }
            }));
        }
        return tests.stream();
    }

    @TestFactory
    Stream<DynamicTest> importTranslations() {
        List<DynamicTest> tests = new ArrayList<>();
        for (Map<String, Object> c : Spec.list(SPEC, "import")) {
            tests.add(DynamicTest.dynamicTest("import: " + c.get("name"), () -> {
                int index = c.containsKey("file") ? ((Double) c.get("file")).intValue() : 0;
                TranslationFile file = TranslationFiles.read((String) c.get("content")).get(index);
                List<Object> out = new ArrayList<>();
                for (ImportedSegment s : TranslationFiles.importTranslations(file, segments(c.get("current")))) {
                    out.add(describe(s));
                }
                assertEquals(c.get("out"), out);
            }));
        }
        return tests.stream();
    }

    @Test
    void aFileNotReturnedByReadIsRefused() {
        TranslationFile file = new TranslationFile(FileFormat.HTML, "P", "r", "de", "fr", Collections.emptyList());
        TranslationFileException e = assertThrows(TranslationFileException.class,
                () -> TranslationFiles.importTranslations(file, Collections.emptyList()));
        assertEquals(TranslationFileErrorCode.INVALID_FILE, e.getCode());
    }

    @Test
    void aDoctypeIsNeverExpanded() {
        String xliff = "<?xml version=\"1.0\"?>\n<!DOCTYPE xliff [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;\">]>\n"
                + "<xliff version=\"1.2\"><file original=\"r\" source-language=\"de\" target-language=\"fr\" product-name=\"P\">"
                + "<body><trans-unit id=\"k\"><source>x</source><target>&b;</target></trans-unit></body></file></xliff>";
        TranslationFileException e = assertThrows(TranslationFileException.class, () -> TranslationFiles.read(xliff));
        assertEquals(TranslationFileErrorCode.INVALID_FILE, e.getCode());
    }

    @Test
    void roundTripThroughHtml() {
        FileJob job = new FileJob("P", "page:1", "de", "fr", List.of(
                new FileSegment("title", "Hallo"), new FileSegment("body", "<p>Ein <b>Test</b></p>", true)));
        TranslationFile file = TranslationFiles.read(TranslationFiles.exportHtml(job)).get(0);
        List<ImportedSegment> imported = TranslationFiles.importTranslations(file, job.getSegments());
        assertEquals(List.of(new ImportedSegment("title", ImportStatus.OK, "Hallo"),
                new ImportedSegment("body", ImportStatus.OK, "<p>Ein <b>Test</b></p>")), imported);
    }
}
