package com.supertext.sdk;

/**
 * A failed Supertext call. {@link #getCode()} says what went wrong (see {@link ErrorCode});
 * the message is English and meant for logs. Never contains the API key.
 */
public class SupertextException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    private final ErrorCode code;
    private final Integer status;
    private final String detail;
    private final String apiErrorCode;
    private final String sourceLang;
    private final String targetLang;

    public SupertextException(ErrorCode code, String message) {
        this(code, message, null, null, null, null, null, null);
    }

    public SupertextException(ErrorCode code, String message, Integer status, String detail, String apiErrorCode,
                              String sourceLang, String targetLang, Throwable cause) {
        super(message, cause);
        this.code = code;
        this.status = status;
        this.detail = detail;
        this.apiErrorCode = apiErrorCode;
        this.sourceLang = sourceLang;
        this.targetLang = targetLang;
    }

    /** What went wrong. */
    public ErrorCode getCode() {
        return code;
    }

    /** The HTTP status, or {@code null} when the error is not an HTTP answer (e.g. a file status). */
    public Integer getStatus() {
        return status;
    }

    /** The answer's body without tags, trimmed, at most 200 characters; or the network error's message. May be {@code null}. */
    public String getDetail() {
        return detail;
    }

    /** The {@code error_code} of a JSON error answer, e.g. {@code INVALID_LANGUAGE_PAIR}. May be {@code null}. */
    public String getApiErrorCode() {
        return apiErrorCode;
    }

    /** For {@link ErrorCode#INVALID_LANGUAGE_PAIR}: the source language the API reports, if any. */
    public String getSourceLang() {
        return sourceLang;
    }

    /** For {@link ErrorCode#INVALID_LANGUAGE_PAIR}: the target language the API reports, if any. */
    public String getTargetLang() {
        return targetLang;
    }
}
