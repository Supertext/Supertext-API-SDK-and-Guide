package com.supertext.sdk.v1;

import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * Languages of the Supertext AI file translation API v1 ({@code spec/languages.json}, from the live
 * {@code GET features}, October 2026). There is no default region and no guessing: an integration maps its
 * CMS languages to these codes; a CMS code that equals a Supertext code (apart from case and {@code _}/{@code -})
 * is used as it is, anything else is an {@link ErrorCode#UNSUPPORTED_LANGUAGE} error until the administrator
 * maps it. For the live list, use {@link SupertextClient#getFeatures()}.
 *
 * <pre>
 * Map&lt;String, String&gt; mapping = Map.of("de", "de-CH", "no", "nb"); // the integration's language setting
 * String target = Languages.target(cmsLocale, mapping);             // "de" → "de-CH", "fr_ch" → "fr-CH"
 * String source = Languages.source(cmsSourceLocale);                // "en-US" → "en", "" → "" (auto-detect)
 * client.translateHtml(html, target, source);
 * </pre>
 */
public final class Languages {

    /** Source languages (bare codes). */
    public static final List<SourceLanguage> SOURCES = Collections.unmodifiableList(Arrays.asList(
            new SourceLanguage("bg", "Bulgarian"),
            new SourceLanguage("cs", "Czech"),
            new SourceLanguage("da", "Danish"),
            new SourceLanguage("de", "German"),
            new SourceLanguage("el", "Greek"),
            new SourceLanguage("en", "English"),
            new SourceLanguage("es", "Spanish"),
            new SourceLanguage("fi", "Finnish"),
            new SourceLanguage("fr", "French"),
            new SourceLanguage("gsw", "Swiss German"),
            new SourceLanguage("hr", "Croatian"),
            new SourceLanguage("hu", "Hungarian"),
            new SourceLanguage("it", "Italian"),
            new SourceLanguage("ja", "Japanese"),
            new SourceLanguage("ko", "Korean"),
            new SourceLanguage("nb", "Norwegian Bokmål"),
            new SourceLanguage("nl", "Dutch"),
            new SourceLanguage("pl", "Polish"),
            new SourceLanguage("pt", "Portuguese"),
            new SourceLanguage("rm", "Romansh"),
            new SourceLanguage("ru", "Russian"),
            new SourceLanguage("sk", "Slovak"),
            new SourceLanguage("sl", "Slovenian"),
            new SourceLanguage("sq", "Albanian"),
            new SourceLanguage("sr", "Serbian"),
            new SourceLanguage("sv", "Swedish"),
            new SourceLanguage("tr", "Turkish"),
            new SourceLanguage("zh", "Chinese")));

    /** Target languages, exactly as Supertext expects them. */
    public static final List<TargetLanguage> TARGETS = Collections.unmodifiableList(Arrays.asList(
            new TargetLanguage("bg", "Bulgarian", true),
            new TargetLanguage("cs", "Czech", true),
            new TargetLanguage("da", "Danish", true),
            new TargetLanguage("de-AT", "German (Austria)", true),
            new TargetLanguage("de-CH", "German (Switzerland)", true),
            new TargetLanguage("de-DE", "German (Germany)", true),
            new TargetLanguage("el", "Greek", true),
            new TargetLanguage("en-GB", "English (United Kingdom)", false),
            new TargetLanguage("en-US", "English (United States)", false),
            new TargetLanguage("es", "Spanish", true),
            new TargetLanguage("fi", "Finnish", true),
            new TargetLanguage("fr-CH", "French (Switzerland)", true),
            new TargetLanguage("fr-FR", "French (France)", true),
            new TargetLanguage("gsw-u-sd-chbe", "Swiss German (Bern)", false),
            new TargetLanguage("gsw-u-sd-chzh", "Swiss German (Zurich)", false),
            new TargetLanguage("hr", "Croatian", true),
            new TargetLanguage("hu", "Hungarian", true),
            new TargetLanguage("it-CH", "Italian (Switzerland)", true),
            new TargetLanguage("it-IT", "Italian (Italy)", true),
            new TargetLanguage("ja", "Japanese", true),
            new TargetLanguage("ko", "Korean", true),
            new TargetLanguage("nb", "Norwegian Bokmål", true),
            new TargetLanguage("nl", "Dutch", true),
            new TargetLanguage("pl", "Polish", true),
            new TargetLanguage("pt-BR", "Portuguese (Brazil)", true),
            new TargetLanguage("pt-PT", "Portuguese (Portugal)", true),
            new TargetLanguage("rm", "Romansh", false),
            new TargetLanguage("ru", "Russian", true),
            new TargetLanguage("sk", "Slovak", true),
            new TargetLanguage("sl", "Slovenian", true),
            new TargetLanguage("sq", "Albanian", false),
            new TargetLanguage("sr-Cyrl", "Serbian (Cyrillic)", false),
            new TargetLanguage("sr-Latn", "Serbian (Latin)", false),
            new TargetLanguage("sv", "Swedish", true),
            new TargetLanguage("tr", "Turkish", true),
            new TargetLanguage("zh-Hans", "Chinese (Simplified)", true),
            new TargetLanguage("zh-Hant", "Chinese (Traditional)", true)));

    private static final Map<String, SourceLanguage> SOURCE_BY_KEY = new LinkedHashMap<>();
    private static final Map<String, TargetLanguage> TARGET_BY_KEY = new LinkedHashMap<>();

    static {
        for (SourceLanguage language : SOURCES) {
            SOURCE_BY_KEY.put(key(language.getCode()), language);
        }
        for (TargetLanguage language : TARGETS) {
            TARGET_BY_KEY.put(key(language.getCode()), language);
        }
    }

    private Languages() {
    }

    /** Case, {@code _} vs {@code -} and surrounding spaces don't matter when comparing codes. */
    private static String key(String code) {
        return code == null ? "" : code.strip().replace('_', '-').toLowerCase(Locale.ROOT);
    }

    /**
     * The supported target that equals {@code code} (apart from case and {@code _}/{@code -}), else {@code null}.
     * {@code de} → {@code null}, {@code de_ch} → {@code de-CH}.
     */
    public static String matchTarget(String code) {
        TargetLanguage language = TARGET_BY_KEY.get(key(code));
        return language == null ? null : language.getCode();
    }

    /** The supported source for {@code code}: exact match, else its first subtag ({@code en-US} → {@code en}), else {@code null}. */
    public static String matchSource(String code) {
        String k = key(code);
        if (k.isEmpty()) {
            return null;
        }
        SourceLanguage language = SOURCE_BY_KEY.get(k);
        if (language == null) {
            int dash = k.indexOf('-');
            language = SOURCE_BY_KEY.get(dash < 0 ? k : k.substring(0, dash));
        }
        return language == null ? null : language.getCode();
    }

    /** {@link #target(String, Map)} without a mapping. */
    public static String target(String code) {
        return target(code, null);
    }

    /**
     * The target code to send: the mapping's value (sent trimmed and otherwise as it is, even if it isn't in
     * {@link #TARGETS}, so a language Supertext adds later can be used before the SDK knows it), else an exact
     * match ({@link #matchTarget}).
     *
     * @param mapping CMS language → Supertext code, e.g. {@code {de=de-CH, no=nb}}; keys are compared like codes. May be {@code null}.
     * @throws SupertextException {@link ErrorCode#UNSUPPORTED_LANGUAGE} (with {@link SupertextException#getTargetLang()})
     *                            when there is neither; nothing is sent
     */
    public static String target(String code, Map<String, String> mapping) {
        String result = mapped(code, mapping);
        if (result == null) {
            result = matchTarget(code);
        }
        if (result == null) {
            String given = code == null ? "" : code.strip();
            throw new SupertextException(ErrorCode.UNSUPPORTED_LANGUAGE, null, null, null, null, given, null, null);
        }
        return result;
    }

    /** {@link #source(String, Map)} without a mapping. */
    public static String source(String code) {
        return source(code, null);
    }

    /**
     * The source code to send: the mapping's value, else {@link #matchSource}. An empty or blank code gives
     * {@code ""} (auto-detect).
     *
     * @throws SupertextException {@link ErrorCode#UNSUPPORTED_LANGUAGE} (with {@link SupertextException#getSourceLang()})
     *                            when there is neither
     */
    public static String source(String code, Map<String, String> mapping) {
        if (code == null || code.isBlank()) {
            return "";
        }
        // One mapping serves both directions: a mapped target code is sent in its source form (de-CH -> de).
        String fromMapping = mapped(code, mapping);
        String result;
        if (fromMapping != null) {
            String asSource = matchSource(fromMapping);
            result = asSource != null ? asSource : fromMapping;
        } else {
            result = matchSource(code);
        }
        if (result == null) {
            throw new SupertextException(ErrorCode.UNSUPPORTED_LANGUAGE, null, null, null, code.strip(), null, null, null);
        }
        return result;
    }

    /** Whether a supported target offers formal/informal; {@code null} for a code that isn't in {@link #TARGETS}. */
    public static Boolean supportsPoliteness(String targetCode) {
        TargetLanguage language = TARGET_BY_KEY.get(key(targetCode));
        return language == null ? null : language.supportsPoliteness();
    }

    /** Whether {@code code} matches a supported target ({@link #matchTarget} isn't {@code null}). */
    public static boolean isSupportedTarget(String code) {
        return matchTarget(code) != null;
    }

    /** Whether {@code code} is a supported source (exact match, apart from case and {@code _}/{@code -}). */
    public static boolean isSupportedSource(String code) {
        return SOURCE_BY_KEY.containsKey(key(code));
    }

    private static String mapped(String code, Map<String, String> mapping) {
        if (mapping == null) {
            return null;
        }
        String wanted = key(code);
        for (Map.Entry<String, String> entry : mapping.entrySet()) {
            String to = entry.getValue();
            if (entry.getKey() != null && key(entry.getKey()).equals(wanted) && to != null && !to.isBlank()) {
                return to.strip();
            }
        }
        return null;
    }
}
