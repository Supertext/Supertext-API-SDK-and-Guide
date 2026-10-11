package com.supertext.sdk;

/**
 * Why a Supertext call failed. The {@link #value()} strings are fixed and shared by all
 * Supertext SDKs (see {@code spec/errors.json}), so they can be stored or compared across languages.
 */
public enum ErrorCode {
    /** No API key configured. No request was sent. */
    MISSING_API_KEY("missing_api_key"),
    /** HTTP 401/403: the key is wrong, revoked or lacks access. */
    AUTHENTICATION_FAILURE("authentication_failure"),
    /** HTTP 400 INVALID_LANGUAGE_PAIR: usually a bare target ("de") or a regional source ("en-US"). */
    INVALID_LANGUAGE_PAIR("invalid_language_pair"),
    /** HTTP 404: unknown file id (expired after 24 h or already deleted). */
    NOT_FOUND("not_found"),
    /** HTTP 413: the document is over the API's size limit. */
    PAYLOAD_TOO_LARGE("payload_too_large"),
    /** HTTP 429 after all retries. */
    TOO_MANY_REQUESTS("too_many_requests"),
    /** HTTP 5xx. */
    SERVICE_UNAVAILABLE("service_unavailable"),
    /** Any other HTTP status. */
    UNEXPECTED_STATUS("unexpected_status"),
    /** No HTTP answer: DNS, TLS, connection refused, request timeout. */
    TRANSPORT_ERROR("transport_error"),
    /** The upload answered without a file_id. */
    NO_FILE_ID("no_file_id"),
    /** File status {@code error}. */
    TRANSLATION_ERROR("translation_error"),
    /** File status {@code limit_exceeded}: the account's translation limit is used up. */
    QUOTA_EXCEEDED("quota_exceeded"),
    /** File status {@code deleted} before the download. */
    FILE_DELETED("file_deleted"),
    /** The file wasn't done within the client's timeout. */
    TIMEOUT("timeout"),
    /** The download was empty, or (translateTexts) segments are missing from the translated document. */
    INCOMPLETE_RESPONSE("incomplete_response");

    private final String value;

    ErrorCode(String value) {
        this.value = value;
    }

    /** The shared string value, e.g. {@code "invalid_language_pair"}. */
    public String value() {
        return value;
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
