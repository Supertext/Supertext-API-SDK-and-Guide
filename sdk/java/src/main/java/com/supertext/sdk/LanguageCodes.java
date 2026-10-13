package com.supertext.sdk;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;

/**
 * Language codes for the Supertext AI file API: the source is a bare language ({@code en}), the
 * target a regional code ({@code de-DE}). Supertext rejects bare targets and regional sources with
 * {@code INVALID_LANGUAGE_PAIR}, the most common integration bug.
 */
public final class LanguageCodes {

    /** Region used for a bare target language ({@code de} → {@code de-DE}). */
    public static final Map<String, String> DEFAULT_REGIONS;

    static {
        String[] pairs = {
            "ar", "ar-SA", "bg", "bg-BG", "cs", "cs-CZ", "da", "da-DK", "de", "de-DE", "el", "el-GR",
            "en", "en-US", "es", "es-ES", "et", "et-EE", "fi", "fi-FI", "fr", "fr-FR", "hr", "hr-HR",
            "hu", "hu-HU", "it", "it-IT", "ja", "ja-JP", "ko", "ko-KR", "lt", "lt-LT", "lv", "lv-LV",
            "nb", "nb-NO", "nl", "nl-NL", "pl", "pl-PL", "pt", "pt-PT", "ro", "ro-RO", "ru", "ru-RU",
            "sk", "sk-SK", "sl", "sl-SI", "sr", "sr-RS", "sv", "sv-SE", "th", "th-TH", "tr", "tr-TR",
            "uk", "uk-UA", "vi", "vi-VN", "zh", "zh-CN",
        };
        Map<String, String> map = new LinkedHashMap<>();
        for (int i = 0; i < pairs.length; i += 2) {
            map.put(pairs[i], pairs[i + 1]);
        }
        DEFAULT_REGIONS = Collections.unmodifiableMap(map);
    }

    private static final Pattern SCRIPT = Pattern.compile("[A-Za-z]{4}");
    private static final Pattern REGION = Pattern.compile("[A-Za-z]{2}|[0-9]{3}");
    private static final Pattern VALID = Pattern.compile("^[a-z]{2,3}(-[A-Z][a-z]{3})?(-([A-Z]{2}|[0-9]{3}))?$");

    private LanguageCodes() {
    }

    /**
     * Canonical BCP-47 spelling: trimmed, {@code _} → {@code -}, language lower case, two-letter
     * subtags upper case ({@code CH}), four-letter subtags title case ({@code Hant}), others unchanged ({@code 419}).
     * Variants and extensions after a script or region are dropped ({@code de_DE_formal} → {@code de-DE},
     * {@code en-US-x-twain} → {@code en-US}); a code whose second subtag is neither a script nor a region is
     * kept ({@code de-CHE}) and isn't valid.
     */
    public static String normalize(String code) {
        if (code == null) {
            return "";
        }
        String trimmed = code.trim().replace('_', '-');
        if (trimmed.isEmpty()) {
            return "";
        }
        String[] parts = trimmed.split("-", -1);
        StringBuilder out = new StringBuilder(parts[0].toLowerCase(Locale.ROOT));
        int next = 1;
        if (next < parts.length && SCRIPT.matcher(parts[next]).matches()) {
            out.append('-').append(titleCase(parts[next++]));
        }
        if (next < parts.length && REGION.matcher(parts[next]).matches()) {
            out.append('-').append(parts[next++].toUpperCase(Locale.ROOT));
        }
        if (next > 1) {
            return out.toString(); // variants and extensions dropped
        }
        for (int i = 1; i < parts.length; i++) {
            String part = parts[i];
            out.append('-');
            if (part.length() == 2) {
                out.append(part.toUpperCase(Locale.ROOT));
            } else if (part.length() == 4) {
                out.append(titleCase(part));
            } else {
                out.append(part);
            }
        }
        return out.toString();
    }

    private static String titleCase(String part) {
        return part.substring(0, 1).toUpperCase(Locale.ROOT) + part.substring(1).toLowerCase(Locale.ROOT);
    }

    /** The bare source language ({@code en-US} → {@code en}); empty for empty (auto-detect). */
    public static String source(String code) {
        return source(code, null);
    }

    /**
     * The bare source language of the matching override, or of the code itself.
     *
     * @param overrides CMS locale → Supertext code; a key matches when it normalizes to the same code. May be {@code null}.
     */
    public static String source(String code, Map<String, String> overrides) {
        String normalized = normalize(override(code, overrides));
        int dash = normalized.indexOf('-');
        return dash < 0 ? normalized : normalized.substring(0, dash);
    }

    /** The regional target code ({@code de} → {@code de-DE}, {@code de_ch} → {@code de-CH}). */
    public static String target(String code) {
        return target(code, null);
    }

    /**
     * The regional target code of the matching override, or of the code itself. A bare language
     * gets its {@link #DEFAULT_REGIONS default region}; an unknown bare language stays as it is.
     */
    public static String target(String code, Map<String, String> overrides) {
        String normalized = normalize(override(code, overrides));
        if (normalized.indexOf('-') < 0) {
            String regional = DEFAULT_REGIONS.get(normalized);
            if (regional != null) {
                return regional;
            }
        }
        return normalized;
    }

    /** Whether the code (after {@link #normalize}) looks like a code Supertext can take. */
    public static boolean isValid(String code) {
        return VALID.matcher(normalize(code)).matches();
    }

    private static String override(String code, Map<String, String> overrides) {
        String value = code == null ? "" : code;
        if (overrides == null || overrides.isEmpty()) {
            return value;
        }
        String wanted = normalize(value);
        for (Map.Entry<String, String> entry : overrides.entrySet()) {
            String replacement = entry.getValue();
            if (normalize(entry.getKey()).equals(wanted) && replacement != null && !replacement.isBlank()) {
                return replacement;
            }
        }
        return value;
    }
}
