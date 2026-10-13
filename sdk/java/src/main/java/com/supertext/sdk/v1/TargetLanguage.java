package com.supertext.sdk.v1;

import java.util.Objects;

/** A target language of the API, exactly as Supertext expects it (e.g. {@code de-CH}, {@code es}), see {@link Languages#TARGETS}. */
public final class TargetLanguage {

    private final String code;
    private final String name;
    private final boolean politeness;

    public TargetLanguage(String code, String name, boolean politeness) {
        this.code = Objects.requireNonNull(code, "code");
        this.name = Objects.requireNonNull(name, "name");
        this.politeness = politeness;
    }

    /** The code to send, e.g. {@code de-CH}. */
    public String getCode() {
        return code;
    }

    /** English name, e.g. {@code German (Switzerland)}. */
    public String getName() {
        return name;
    }

    /**
     * Whether formal/informal ({@link Politeness#MORE}/{@link Politeness#LESS}) is offered. Otherwise the API
     * answers FEATURE_ERROR ({@link ErrorCode#UNSUPPORTED_FEATURE}).
     */
    public boolean supportsPoliteness() {
        return politeness;
    }

    @Override
    public boolean equals(Object o) {
        if (!(o instanceof TargetLanguage)) {
            return false;
        }
        TargetLanguage other = (TargetLanguage) o;
        return code.equals(other.code) && name.equals(other.name) && politeness == other.politeness;
    }

    @Override
    public int hashCode() {
        return Objects.hash(code, name, politeness);
    }

    @Override
    public String toString() {
        return code + " (" + name + (politeness ? ", politeness" : "") + ")";
    }
}
