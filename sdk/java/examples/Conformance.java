import com.supertext.sdk.Politeness;
import com.supertext.sdk.Segment;
import com.supertext.sdk.SupertextClient;
import com.supertext.sdk.SupertextException;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Conformance program for stand-in/conformance.mjs (see spec/conformance.json): reads one request
 * as JSON on stdin, translates it with translateTexts and prints the result as JSON.
 *
 * <pre>
 * mvn -q package -DskipTests
 * node ../../stand-in/conformance.mjs -- java -cp target/classes examples/Conformance.java
 * </pre>
 *
 * Single-file source program (Java 11+), so it carries its own small JSON reader and writer: the SDK has no JSON dependency.
 */
public final class Conformance {

    public static void main(String[] args) throws Exception {
        PrintStream out = new PrintStream(System.out, true, "UTF-8");
        @SuppressWarnings("unchecked")
        Map<String, Object> request = (Map<String, Object>) new Reader(readAll(System.in)).parse();

        Map<String, Segment> segments = new LinkedHashMap<>();
        for (Object item : (List<?>) request.get("segments")) {
            Map<?, ?> s = (Map<?, ?>) item;
            segments.put((String) s.get("key"), new Segment((String) s.get("text"), Boolean.TRUE.equals(s.get("html"))));
        }
        SupertextClient client = SupertextClient.builder(System.getenv("SUPERTEXT_API_KEY"))
                .baseUrl(System.getenv("SUPERTEXT_API_URL"))
                .pollInterval(Duration.ofMillis(100))
                .build();
        try {
            Map<String, String> translations = client.translateTexts(segments, (String) request.get("targetLang"),
                    (String) request.get("sourceLang"), Politeness.fromValue((String) request.get("politeness")));
            StringBuilder json = new StringBuilder("{\"translations\":{");
            String separator = "";
            for (Map.Entry<String, String> entry : translations.entrySet()) {
                json.append(separator).append(quote(entry.getKey())).append(':').append(quote(entry.getValue()));
                separator = ",";
            }
            out.println(json.append("}}"));
        } catch (SupertextException e) {
            out.println("{\"error\":{\"code\":" + quote(e.getCode().value()) + ",\"status\":"
                    + (e.getStatus() == null ? "null" : e.getStatus()) + "}}");
        }
    }

    private static String readAll(InputStream in) throws IOException {
        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        in.transferTo(buffer);
        return buffer.toString(StandardCharsets.UTF_8);
    }

    private static String quote(String s) {
        StringBuilder out = new StringBuilder("\"");
        for (char c : s.toCharArray()) {
            switch (c) {
                case '"': out.append("\\\""); break;
                case '\\': out.append("\\\\"); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                case '\t': out.append("\\t"); break;
                default:
                    if (c < 0x20) {
                        out.append(String.format("\\u%04x", (int) c));
                    } else {
                        out.append(c);
                    }
            }
        }
        return out.append('"').toString();
    }

    /** Just enough JSON for the request: objects, arrays, strings, booleans, null, numbers. */
    private static final class Reader {
        private final String s;
        private int i;

        Reader(String s) {
            this.s = s;
        }

        Object parse() {
            skip();
            char c = s.charAt(i);
            if (c == '{') {
                Map<String, Object> map = new LinkedHashMap<>();
                i++;
                skip();
                if (s.charAt(i) == '}') {
                    i++;
                    return map;
                }
                do {
                    skip();
                    String key = (String) parse();
                    skip();
                    i++; // ':'
                    map.put(key, parse());
                    skip();
                } while (s.charAt(i++) == ',');
                return map;
            }
            if (c == '[') {
                List<Object> list = new ArrayList<>();
                i++;
                skip();
                if (s.charAt(i) == ']') {
                    i++;
                    return list;
                }
                do {
                    list.add(parse());
                    skip();
                } while (s.charAt(i++) == ',');
                return list;
            }
            if (c == '"') {
                StringBuilder out = new StringBuilder();
                i++;
                while ((c = s.charAt(i++)) != '"') {
                    if (c != '\\') {
                        out.append(c);
                        continue;
                    }
                    char e = s.charAt(i++);
                    switch (e) {
                        case 'n': out.append('\n'); break;
                        case 'r': out.append('\r'); break;
                        case 't': out.append('\t'); break;
                        case 'b': out.append('\b'); break;
                        case 'f': out.append('\f'); break;
                        case 'u': out.append((char) Integer.parseInt(s.substring(i, i + 4), 16)); i += 4; break;
                        default: out.append(e);
                    }
                }
                return out.toString();
            }
            for (String word : new String[] {"true", "false", "null"}) {
                if (s.startsWith(word, i)) {
                    i += word.length();
                    return word.equals("null") ? null : Boolean.valueOf(word);
                }
            }
            int start = i;
            while (i < s.length() && "-+.eE0123456789".indexOf(s.charAt(i)) >= 0) {
                i++;
            }
            return Double.valueOf(s.substring(start, i));
        }

        private void skip() {
            while (i < s.length() && Character.isWhitespace(s.charAt(i))) {
                i++;
            }
        }
    }
}
