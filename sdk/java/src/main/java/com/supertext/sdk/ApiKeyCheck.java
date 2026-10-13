package com.supertext.sdk;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.regex.Pattern;

/**
 * The result of the automatic API key check (spec/reporting.json → apiKeyCheck), from
 * {@link SupertextClient#checkApiKey()}. Plugins run it on every settings save, when a key from the
 * environment changed and on "Test connection", and show it to administrators. Immutable.
 *
 * <p>Holds the key's SHA-256 {@link #getFingerprint() fingerprint} (to notice a changed key), never the key.</p>
 */
public final class ApiKeyCheck {

    /** What the check found. {@link #value()} is the string shared by all Supertext SDKs. */
    public enum Status {
        /** Supertext accepted the key (2xx). */
        OK("ok"),
        /** Supertext rejected the key (401/403). */
        INVALID("invalid"),
        /** No key configured; nothing was sent. */
        MISSING("missing"),
        /** No answer, or a 5xx answer. */
        UNREACHABLE("unreachable"),
        /** Any other answer. */
        ERROR("error");

        private final String value;

        Status(String value) {
            this.value = value;
        }

        /** {@code ok}, {@code invalid}, {@code missing}, {@code unreachable} or {@code error}. */
        public String value() {
            return value;
        }

        /** The status for a value, or {@code null} for an unknown one. */
        public static Status fromValue(String value) {
            for (Status status : values()) {
                if (status.value.equals(value)) {
                    return status;
                }
            }
            return null;
        }
    }

    private static final Pattern PASTED_PREFIX = Pattern.compile("^supertext-auth-key(\\s+|$)", Pattern.CASE_INSENSITIVE);
    private static final DateTimeFormatter ISO_SECONDS = DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss'Z'").withZone(ZoneOffset.UTC);

    private final Status status;
    private final String checkedAt;
    private final Integer httpStatus;
    private final String errorCode;
    private final String fingerprint;

    /**
     * @param checkedAt   UTC ISO 8601 without fractions ({@code 2026-10-13T08:00:00Z})
     * @param httpStatus  the HTTP status of the answer, or {@code null} without one
     * @param errorCode   the answer's JSON {@code error_code}, or {@code null}; cut to 100 characters
     * @param fingerprint {@link #fingerprint(String)} of the key ({@code ""} for a blank key)
     */
    public ApiKeyCheck(Status status, String checkedAt, Integer httpStatus, String errorCode, String fingerprint) {
        this.status = Objects.requireNonNull(status, "status");
        this.checkedAt = Objects.requireNonNull(checkedAt, "checkedAt");
        this.httpStatus = httpStatus;
        this.errorCode = errorCode == null ? null : truncate(errorCode, 100);
        this.fingerprint = fingerprint == null ? "" : fingerprint;
    }

    /**
     * SHA-256 (lower-case hex) of the key as {@link SupertextClient#authHeader(String)} would send it
     * (trimmed, a pasted {@code Supertext-Auth-Key } prefix removed); {@code ""} for a blank key.
     */
    public static String fingerprint(String apiKey) {
        String key = apiKey == null ? "" : apiKey.trim();
        key = PASTED_PREFIX.matcher(key).replaceFirst("").trim();
        if (key.isEmpty()) {
            return "";
        }
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(key.getBytes(StandardCharsets.UTF_8));
            StringBuilder hex = new StringBuilder(64);
            for (byte b : hash) {
                hex.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
            }
            return hex.toString();
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is not available", e); // every JDK has it
        }
    }

    /** A check that failed with this exception ({@code checkedAt} and {@code fingerprint} from the caller). */
    static ApiKeyCheck fromError(Throwable error, String checkedAt, String fingerprint) {
        if (!(error instanceof SupertextException)) {
            return new ApiKeyCheck(Status.ERROR, checkedAt, null, null, fingerprint);
        }
        SupertextException e = (SupertextException) error;
        Integer httpStatus = e.getStatus();
        Status status;
        if (e.getCode() == ErrorCode.MISSING_API_KEY) {
            status = Status.MISSING;
        } else if (httpStatus != null && (httpStatus == 401 || httpStatus == 403)) {
            status = Status.INVALID;
        } else if (httpStatus == null || httpStatus >= 500) {
            status = Status.UNREACHABLE;
        } else {
            status = Status.ERROR;
        }
        String apiErrorCode = e.getApiErrorCode();
        return new ApiKeyCheck(status, checkedAt, httpStatus, apiErrorCode == null || apiErrorCode.isEmpty() ? null : apiErrorCode, fingerprint);
    }

    /** {@code 2026-10-13T08:00:00Z}: UTC, fractions of a second dropped. */
    public static String isoSeconds(Instant instant) {
        return ISO_SECONDS.format(instant.truncatedTo(ChronoUnit.SECONDS));
    }

    public Status getStatus() {
        return status;
    }

    /** When the check ran: UTC ISO 8601 without fractions. */
    public String getCheckedAt() {
        return checkedAt;
    }

    /** The HTTP status Supertext answered with; {@code null} without an answer. */
    public Integer getHttpStatus() {
        return httpStatus;
    }

    /** The answer's JSON {@code error_code} (at most 100 characters); {@code null} if none. */
    public String getErrorCode() {
        return errorCode;
    }

    /** SHA-256 of the key (lower-case hex); {@code ""} for a blank key. Keep it locally, it is never sent. */
    public String getFingerprint() {
        return fingerprint;
    }

    /** The stored form: {@code status}, {@code checkedAt}, {@code httpStatus}, {@code errorCode}, {@code fingerprint}. */
    Map<String, Object> toMap() {
        Map<String, Object> map = new LinkedHashMap<>();
        map.put("status", status.value());
        map.put("checkedAt", checkedAt);
        map.put("httpStatus", httpStatus);
        map.put("errorCode", errorCode);
        map.put("fingerprint", fingerprint);
        return map;
    }

    /** The stored form back; {@code null} when it isn't a valid check. */
    static ApiKeyCheck fromMap(Object value) {
        if (!(value instanceof Map)) {
            return null;
        }
        Map<?, ?> map = (Map<?, ?>) value;
        Object status = map.get("status");
        Object checkedAt = map.get("checkedAt");
        if (!(status instanceof String) || !(checkedAt instanceof String) || Status.fromValue((String) status) == null) {
            return null;
        }
        Object httpStatus = map.get("httpStatus");
        Object errorCode = map.get("errorCode");
        Object fingerprint = map.get("fingerprint");
        return new ApiKeyCheck(Status.fromValue((String) status), (String) checkedAt,
                httpStatus instanceof Number ? Integer.valueOf(((Number) httpStatus).intValue()) : null,
                errorCode instanceof String ? (String) errorCode : null,
                fingerprint instanceof String ? (String) fingerprint : "");
    }

    private static String truncate(String text, int codePoints) {
        return text.codePointCount(0, text.length()) <= codePoints ? text : text.substring(0, text.offsetByCodePoints(0, codePoints));
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) {
            return true;
        }
        if (!(o instanceof ApiKeyCheck)) {
            return false;
        }
        ApiKeyCheck other = (ApiKeyCheck) o;
        return status == other.status && checkedAt.equals(other.checkedAt) && Objects.equals(httpStatus, other.httpStatus)
                && Objects.equals(errorCode, other.errorCode) && fingerprint.equals(other.fingerprint);
    }

    @Override
    public int hashCode() {
        return Objects.hash(status, checkedAt, httpStatus, errorCode, fingerprint);
    }

    @Override
    public String toString() {
        return "ApiKeyCheck{status=" + status.value() + ", checkedAt=" + checkedAt + ", httpStatus=" + httpStatus
                + ", errorCode=" + errorCode + "}";
    }
}
