package com.supertext.sdk.v1;

import java.util.Collections;
import java.util.List;
import java.util.Objects;

/** One language pair from {@code GET features} ({@link SupertextClient#getFeatures()}). */
public final class FeatureSupport {

    private final String sourceLang;
    private final String targetLang;
    private final List<String> features;

    public FeatureSupport(String sourceLang, String targetLang, List<String> features) {
        this.sourceLang = Objects.requireNonNull(sourceLang, "sourceLang");
        this.targetLang = Objects.requireNonNull(targetLang, "targetLang");
        this.features = Collections.unmodifiableList(List.copyOf(features));
    }

    public String getSourceLang() {
        return sourceLang;
    }

    public String getTargetLang() {
        return targetLang;
    }

    /** E.g. {@code supports_politeness}, {@code supports_glossary}. */
    public List<String> getFeatures() {
        return features;
    }

    @Override
    public boolean equals(Object o) {
        if (!(o instanceof FeatureSupport)) {
            return false;
        }
        FeatureSupport other = (FeatureSupport) o;
        return sourceLang.equals(other.sourceLang) && targetLang.equals(other.targetLang) && features.equals(other.features);
    }

    @Override
    public int hashCode() {
        return Objects.hash(sourceLang, targetLang, features);
    }

    @Override
    public String toString() {
        return sourceLang + " → " + targetLang + " " + features;
    }
}
