package com.supertext.sdk.v1;

/**
 * A failed Supertext call. {@link #getCode()} says what went wrong (see {@link ErrorCode}); integrations
 * translate the code for their users and may append {@link #getDetail()} (the API's or the network's own
 * explanation, untranslated). The message is English and meant for logs: the code's
 * {@link ErrorCode#message() message}, plus {@code " HTTP <status>."} and {@code " Details: <detail>"}
 * when known. Never contains the API key.
 */
public class SupertextException extends RuntimeException {

    private static final long serialVersionUID = 2L;

    private final ErrorCode code;
    private final Integer status;
    private final String detail;
    private final String apiErrorCode;
    private final String sourceLang;
    private final String targetLang;
    private final String feature;

    /** An error with only a code. */
    public SupertextException(ErrorCode code) {
        this(code, null, null, null, null, null, null, null);
    }

    /**
     * An error with everything that is known; {@code null} (or an empty string) for what isn't.
     *
     * @param status       HTTP status, when the error comes from an HTTP answer
     * @param detail       the API's or the network's own explanation
     * @param apiErrorCode the JSON body's {@code error_code}
     * @param sourceLang   the source language involved (API body, or the code {@link Languages#source} couldn't resolve)
     * @param targetLang   the target language involved (API body, or the code {@link Languages#target} couldn't resolve)
     * @param feature      for {@link ErrorCode#UNSUPPORTED_FEATURE}: the option the API names, e.g. {@code politeness}
     */
    public SupertextException(ErrorCode code, Integer status, String detail, String apiErrorCode,
                              String sourceLang, String targetLang, String feature, Throwable cause) {
        super(message(code, status, emptyToNull(detail)), cause);
        this.code = code;
        this.status = status;
        this.detail = emptyToNull(detail);
        this.apiErrorCode = emptyToNull(apiErrorCode);
        this.sourceLang = emptyToNull(sourceLang);
        this.targetLang = emptyToNull(targetLang);
        this.feature = emptyToNull(feature);
    }

    private static String message(ErrorCode code, Integer status, String detail) {
        String message = code.message();
        if (status != null) {
            message += " HTTP " + status + ".";
        }
        if (detail != null) {
            message += " Details: " + detail;
        }
        return message;
    }

    private static String emptyToNull(String value) {
        return value == null || value.isEmpty() ? null : value;
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

    /**
     * For {@link ErrorCode#INVALID_LANGUAGE_PAIR} and {@link ErrorCode#UNSUPPORTED_FEATURE}: the source language the
     * API reports, if any; for {@link ErrorCode#UNSUPPORTED_LANGUAGE}: the source code that couldn't be resolved.
     */
    public String getSourceLang() {
        return sourceLang;
    }

    /**
     * For {@link ErrorCode#INVALID_LANGUAGE_PAIR} and {@link ErrorCode#UNSUPPORTED_FEATURE}: the target language the
     * API reports, if any; for {@link ErrorCode#UNSUPPORTED_LANGUAGE}: the target code that couldn't be resolved.
     */
    public String getTargetLang() {
        return targetLang;
    }

    /** For {@link ErrorCode#UNSUPPORTED_FEATURE}: the option the API names, e.g. {@code politeness}. May be {@code null}. */
    public String getFeature() {
        return feature;
    }
}
