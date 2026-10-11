package com.supertext.sdk;

import java.util.Objects;

/** One text to translate: plain text (escaped, line breaks kept) or an HTML fragment (sent as it is). */
public final class Segment {

    private final String text;
    private final boolean html;

    public Segment(String text, boolean html) {
        this.text = Objects.requireNonNull(text, "text");
        this.html = html;
    }

    /** A plain-text segment. */
    public static Segment text(String text) {
        return new Segment(text, false);
    }

    /** An HTML segment: formatting and links as inline tags ({@code <b>}, {@code <a href>}). */
    public static Segment html(String html) {
        return new Segment(html, true);
    }

    public String getText() {
        return text;
    }

    public boolean isHtml() {
        return html;
    }

    @Override
    public boolean equals(Object o) {
        if (!(o instanceof Segment)) {
            return false;
        }
        Segment other = (Segment) o;
        return html == other.html && text.equals(other.text);
    }

    @Override
    public int hashCode() {
        return Objects.hash(text, html);
    }

    @Override
    public String toString() {
        return (html ? "Segment.html(" : "Segment.text(") + text + ")";
    }
}
