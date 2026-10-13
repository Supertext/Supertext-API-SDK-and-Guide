package com.supertext.sdk.v1;

import java.util.Objects;

/** A source language of the API (a bare code such as {@code en}), see {@link Languages#SOURCES}. */
public final class SourceLanguage {

    private final String code;
    private final String name;

    public SourceLanguage(String code, String name) {
        this.code = Objects.requireNonNull(code, "code");
        this.name = Objects.requireNonNull(name, "name");
    }

    /** The code to send, e.g. {@code en}. */
    public String getCode() {
        return code;
    }

    /** English name, e.g. {@code English}. */
    public String getName() {
        return name;
    }

    @Override
    public boolean equals(Object o) {
        return o instanceof SourceLanguage && code.equals(((SourceLanguage) o).code) && name.equals(((SourceLanguage) o).name);
    }

    @Override
    public int hashCode() {
        return Objects.hash(code, name);
    }

    @Override
    public String toString() {
        return code + " (" + name + ")";
    }
}
