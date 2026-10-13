using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Supertext.Sdk;

/// <summary>The values of <see cref="ApiKeyCheck.Status"/>. Fixed and the same in every Supertext SDK.</summary>
public static class ApiKeyCheckStatus
{
    /// <summary>Supertext accepted the key (HTTP 2xx).</summary>
    public const string Ok = "ok";

    /// <summary>Supertext rejected the key (HTTP 401/403).</summary>
    public const string Invalid = "invalid";

    /// <summary>No key configured; nothing was sent.</summary>
    public const string Missing = "missing";

    /// <summary>No HTTP answer, or HTTP 5xx.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Any other answer (e.g. 404, or 429 after all retries).</summary>
    public const string Error = "error";
}

/// <summary>
/// The result of the automatic API key check (<see cref="SupertextClient.CheckApiKeyAsync"/>), to show to
/// administrators on the settings or status page. Never contains the key.
/// </summary>
/// <param name="Status">One of <see cref="ApiKeyCheckStatus"/>.</param>
/// <param name="CheckedAt">When the check ran: UTC ISO 8601 without fractions (<c>2026-10-13T08:00:00Z</c>).</param>
/// <param name="HttpStatus">The HTTP status Supertext answered with; null without an answer.</param>
/// <param name="ErrorCode">The answer's JSON <c>error_code</c> (at most 100 characters); null if none.</param>
/// <param name="Fingerprint">SHA-256 of the key (lower-case hex), to notice a changed key. Kept locally, never sent. Empty for a blank key.</param>
public sealed record ApiKeyCheck(string Status, string CheckedAt, int? HttpStatus, string? ErrorCode, string Fingerprint)
{
    /// <summary>SHA-256 (lower-case hex) of the key as <see cref="SupertextClient.AuthHeader"/> would send it; empty for a blank key.</summary>
    public static string FingerprintOf(string? apiKey)
    {
        string header;
        try
        {
            header = SupertextClient.AuthHeader(apiKey);
        }
        catch (SupertextException)
        {
            return string.Empty;
        }
        var key = header[("Supertext-Auth-Key".Length + 1)..];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    /// <summary>UTC ISO 8601 without fractions, e.g. <c>2026-10-13T08:00:00Z</c>.</summary>
    public static string IsoSeconds(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The check for an exception thrown by a request (spec/reporting.json → apiKeyCheck).</summary>
    internal static ApiKeyCheck FromException(Exception error, string checkedAt, string fingerprint)
    {
        if (error is not SupertextException e)
        {
            return new ApiKeyCheck(ApiKeyCheckStatus.Error, checkedAt, null, null, fingerprint);
        }
        var errorCode = string.IsNullOrEmpty(e.ApiErrorCode) ? null : e.ApiErrorCode.Length > 100 ? e.ApiErrorCode[..100] : e.ApiErrorCode;
        var status = e.Code == SupertextErrorCodes.MissingApiKey ? ApiKeyCheckStatus.Missing
            : e.Status is 401 or 403 ? ApiKeyCheckStatus.Invalid
            : e.Status is null or >= 500 ? ApiKeyCheckStatus.Unreachable
            : ApiKeyCheckStatus.Error;
        return new ApiKeyCheck(status, checkedAt, e.Status, errorCode, fingerprint);
    }
}
