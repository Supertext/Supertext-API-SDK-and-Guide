namespace Supertext.Sdk.V1;

/// <summary>
/// The error codes of <see cref="SupertextException.Code"/>. The values are fixed and the same in
/// every Supertext SDK, so integrations can map them to their own (translated) messages.
/// </summary>
public static class SupertextErrorCodes
{
    /// <summary>No API key configured. No request was sent.</summary>
    public const string MissingApiKey = "missing_api_key";

    /// <summary>HTTP 401/403: the key is wrong, revoked or lacks access.</summary>
    public const string AuthenticationFailure = "authentication_failure";

    /// <summary>HTTP 400 or 404 <c>INVALID_LANGUAGE_PAIR</c>: a code Supertext doesn't offer (e.g. <c>de</c> or <c>es-ES</c> as target).</summary>
    public const string InvalidLanguagePair = "invalid_language_pair";

    /// <summary>
    /// No request was sent: the language has no Supertext code (no mapping and no exact match, see
    /// <see cref="Languages.Target"/>), or the target is empty. Ask the administrator to map it.
    /// </summary>
    public const string UnsupportedLanguage = "unsupported_language";

    /// <summary>
    /// HTTP 400 <c>FEATURE_ERROR</c>: the language pair exists, but not with this option (e.g. politeness
    /// for <c>en-US</c>, <c>rm</c>, <c>sq</c>); <see cref="SupertextException.Feature"/> names it.
    /// </summary>
    public const string UnsupportedFeature = "unsupported_feature";

    /// <summary>HTTP 404: unknown file id (expired after 24 h or already deleted).</summary>
    public const string NotFound = "not_found";

    /// <summary>HTTP 413: the document is over the API's size limit.</summary>
    public const string PayloadTooLarge = "payload_too_large";

    /// <summary>HTTP 429 after all retries.</summary>
    public const string TooManyRequests = "too_many_requests";

    /// <summary>HTTP 5xx.</summary>
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>Any other HTTP status.</summary>
    public const string UnexpectedStatus = "unexpected_status";

    /// <summary>No HTTP answer: DNS, TLS, connection refused, request timeout.</summary>
    public const string TransportError = "transport_error";

    /// <summary>The upload answered without a <c>file_id</c>.</summary>
    public const string NoFileId = "no_file_id";

    /// <summary>File status <c>error</c>.</summary>
    public const string TranslationError = "translation_error";

    /// <summary>File status <c>limit_exceeded</c>: the account's translation limit is used up.</summary>
    public const string QuotaExceeded = "quota_exceeded";

    /// <summary>File status <c>deleted</c> before the download.</summary>
    public const string FileDeleted = "file_deleted";

    /// <summary>The file wasn't done within the client's timeout.</summary>
    public const string Timeout = "timeout";

    /// <summary>The download was empty, or segments are missing from the translated document.</summary>
    public const string IncompleteResponse = "incomplete_response";
}

/// <summary>
/// An error from the Supertext API or the client. <see cref="Exception.Message"/> is English;
/// use <see cref="Code"/> (one of <see cref="SupertextErrorCodes"/>) to show your own text.
/// Never contains the API key.
/// </summary>
public sealed class SupertextException : Exception
{
    /// <summary>Creates an error.</summary>
    public SupertextException(string code, string message, int? status = null, string? detail = null,
        string? apiErrorCode = null, string? sourceLang = null, string? targetLang = null, string? feature = null,
        Exception? innerException = null)
        : base(string.IsNullOrEmpty(detail) ? message : $"{message} ({detail})", innerException)
    {
        Code = code;
        Status = status;
        Detail = string.IsNullOrEmpty(detail) ? null : detail;
        ApiErrorCode = apiErrorCode;
        SourceLang = sourceLang;
        TargetLang = targetLang;
        Feature = feature;
    }

    /// <summary>One of <see cref="SupertextErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>The HTTP status, for errors caused by an HTTP answer.</summary>
    public int? Status { get; }

    /// <summary>
    /// The API's answer without tags (max. 200 characters), the underlying network error, or the
    /// missing segment keys. Not translated.
    /// </summary>
    public string? Detail { get; }

    /// <summary>The <c>error_code</c> of the API's JSON answer, e.g. <c>INVALID_LANGUAGE_PAIR</c>.</summary>
    public string? ApiErrorCode { get; }

    /// <summary>
    /// For <see cref="SupertextErrorCodes.InvalidLanguagePair"/> and <see cref="SupertextErrorCodes.UnsupportedFeature"/>:
    /// the source the API saw, if it said so. For <see cref="SupertextErrorCodes.UnsupportedLanguage"/> from
    /// <see cref="Languages.Source"/>: the source code that has no Supertext code.
    /// </summary>
    public string? SourceLang { get; }

    /// <summary>
    /// For <see cref="SupertextErrorCodes.InvalidLanguagePair"/> and <see cref="SupertextErrorCodes.UnsupportedFeature"/>:
    /// the target the API saw, if it said so. For <see cref="SupertextErrorCodes.UnsupportedLanguage"/> from
    /// <see cref="Languages.Target"/>: the target code that has no Supertext code.
    /// </summary>
    public string? TargetLang { get; }

    /// <summary>For <see cref="SupertextErrorCodes.UnsupportedFeature"/>: the option the API names, e.g. <c>politeness</c>.</summary>
    public string? Feature { get; }
}
