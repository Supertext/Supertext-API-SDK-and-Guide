package com.supertext.sdk;

/**
 * Facts about this SDK that don't depend on an API version. Everything that talks to the API is
 * versioned in its own package ({@code com.supertext.sdk.v1}); a later API version gets
 * {@code com.supertext.sdk.v2} next to it.
 */
public final class SdkInfo {

    /** This SDK's version (sent in version reports; the same number as {@code pom.xml}). */
    public static final String VERSION = "0.2.1";

    private SdkInfo() {
    }
}
