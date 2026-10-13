package com.supertext.sdk.v1;

/**
 * Why a Supertext call failed. The {@link #value()} strings are fixed and shared by all
 * Supertext SDKs (see {@code spec/errors.json}), so they can be stored or compared across languages.
 */
public enum ErrorCode {
    /** No API key configured. No request was sent. */
    MISSING_API_KEY("missing_api_key", "No Supertext API key is configured."),
    /** HTTP 401/403: the key is wrong, revoked or lacks access. */
    AUTHENTICATION_FAILURE("authentication_failure", "Supertext rejected the API key. Please check it."),
    /** HTTP 400 or 404 INVALID_LANGUAGE_PAIR: a code Supertext doesn't offer (e.g. {@code de} or {@code es-ES} as target). */
    INVALID_LANGUAGE_PAIR("invalid_language_pair", "Supertext does not support this language pair."),
    /**
     * No request was sent: the language has no Supertext code (no mapping and no exact match, see
     * {@link Languages#target}). Ask the administrator to map it.
     */
    UNSUPPORTED_LANGUAGE("unsupported_language",
            "This language has no Supertext language code. Map it to a Supertext language."),
    /**
     * HTTP 400 FEATURE_ERROR: the language pair exists, but not with this option (e.g. politeness for
     * {@code en-US}); {@link SupertextException#getFeature()} names it.
     */
    UNSUPPORTED_FEATURE("unsupported_feature", "Supertext does not support this option for the language pair."),
    /** HTTP 404: unknown file id (expired after 24 h or already deleted). */
    NOT_FOUND("not_found", "The Supertext file was not found (expired or already deleted)."),
    /** HTTP 413: the document is over the API's size limit. */
    PAYLOAD_TOO_LARGE("payload_too_large", "The document is too large for Supertext to translate."),
    /** HTTP 429 after all retries. */
    TOO_MANY_REQUESTS("too_many_requests", "Too many requests to Supertext. Please try again shortly."),
    /** HTTP 5xx. */
    SERVICE_UNAVAILABLE("service_unavailable", "The Supertext service is unavailable."),
    /** Any other HTTP status. */
    UNEXPECTED_STATUS("unexpected_status", "Supertext sent an unexpected HTTP status."),
    /** No HTTP answer: DNS, TLS, connection refused, request timeout. */
    TRANSPORT_ERROR("transport_error", "Could not reach Supertext."),
    /** The upload answered without a file_id. */
    NO_FILE_ID("no_file_id", "Supertext did not return a file id."),
    /** File status {@code error}. */
    TRANSLATION_ERROR("translation_error", "Supertext failed to translate the document."),
    /** File status {@code limit_exceeded}: the account's translation limit is used up. */
    QUOTA_EXCEEDED("quota_exceeded", "Your Supertext translation limit is used up."),
    /** File status {@code deleted} before the download. */
    FILE_DELETED("file_deleted", "The Supertext file was deleted before it could be downloaded."),
    /** The file wasn't done within the client's timeout. */
    TIMEOUT("timeout", "Timed out waiting for the Supertext translation to finish."),
    /** The download was empty, or (translateTexts) segments are missing from the translated document. */
    INCOMPLETE_RESPONSE("incomplete_response", "The Supertext translation is incomplete.");

    private final String value;
    private final String message;

    ErrorCode(String value, String message) {
        this.value = value;
        this.message = message;
    }

    /** The shared string value, e.g. {@code "invalid_language_pair"}. */
    public String value() {
        return value;
    }

    /** The English message for this code (the same in every Supertext SDK), meant for logs. */
    public String message() {
        return message;
    }

    /** The code for a shared string value. */
    public static ErrorCode fromValue(String value) {
        for (ErrorCode code : values()) {
            if (code.value.equals(value)) {
                return code;
            }
        }
        throw new IllegalArgumentException("Unknown Supertext error code: " + value);
    }

    @Override
    public String toString() {
        return value;
    }
}
