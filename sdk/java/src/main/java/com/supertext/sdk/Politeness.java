package com.supertext.sdk;

import java.util.Locale;

/** Form of address in the translation. */
public enum Politeness {
    /** Let Supertext decide (not sent). */
    DEFAULT("default"),
    /** Formal, e.g. German "Sie", French "vous". */
    MORE("more"),
    /** Informal, e.g. German "du", French "tu". */
    LESS("less");

    private final String value;

    Politeness(String value) {
        this.value = value;
    }

    /** The API value: {@code default}, {@code more} or {@code less}. */
    public String value() {
        return value;
    }

    /**
     * Parses {@code more} or {@code less} (case-insensitive). Anything else, including {@code default},
     * {@code null} and unknown values, is {@link #DEFAULT}: politeness is then simply not sent.
     */
    public static Politeness fromValue(String value) {
        if (value == null || value.isBlank()) {
            return DEFAULT;
        }
        String v = value.trim().toLowerCase(Locale.ROOT);
        for (Politeness p : values()) {
            if (p.value.equals(v)) {
                return p;
            }
        }
        return DEFAULT;
    }
}
