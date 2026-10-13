using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Supertext.Sdk;

/// <summary>
/// Client for the Supertext AI file translation API v1: upload an HTML document, poll its status,
/// download the translation, delete the file. Thread-safe; reuse one instance per API key.
/// </summary>
/// <remarks>
/// What the Supertext plugins learned against the live API is built in: the Authorization prefix,
/// language codes (bare source, regional target), the exact multipart format, HTTP 429 retries,
/// and one <c>data-st-id</c> element per text (see <see cref="TranslateTextsAsync"/>).
/// </remarks>
public sealed partial class SupertextClient
{
    /// <summary>The live API.</summary>
    public const string LiveBaseUrl = "https://api.supertext.com/v1/";

    /// <summary>Characters per document in <see cref="TranslateTextsAsync"/>; the API accepts up to 1,000,000.</summary>
    public const int DefaultMaxCharacters = 900_000;

    private const string AuthPrefix = "Supertext-Auth-Key";
    private const string SdkUserAgent = "supertext-sdk-dotnet";

    private const string KeyHelp = "No Supertext account yet? Create one at https://www.supertext.com/person/en/account/signin. "
        + "Generate your API key at https://www.supertext.com/en/integrations/api (supertext.com → Integrations → API; requires the Admin role).";

    // One shared client for everyone who doesn't bring their own (avoids socket exhaustion).
    // Infinite timeout: RequestTimeout is applied per request instead.
    private static readonly Lazy<HttpClient> SharedHttpClient = new(() => new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan });

    private readonly string apiKey;
    private readonly Uri baseUri;
    private readonly HttpClient http;
    private readonly SupertextClientOptions options;
    private readonly Func<TimeSpan, CancellationToken, Task> sleep;
    private readonly TimeProvider clock;
    private readonly string userAgent;

    /// <summary>
    /// Creates a client. A blank key doesn't throw here: the first request throws
    /// <see cref="SupertextErrorCodes.MissingApiKey"/> without sending anything.
    /// </summary>
    /// <param name="apiKey">The key as shown by Supertext, with or without the <c>Supertext-Auth-Key</c> prefix.</param>
    /// <param name="options">Optional settings.</param>
    public SupertextClient(string? apiKey, SupertextClientOptions? options = null)
    {
        this.apiKey = apiKey ?? string.Empty;
        this.options = options ?? new SupertextClientOptions();
        var baseUrl = this.options.BaseUrl;
        baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);
        http = this.options.HttpClient ?? SharedHttpClient.Value;
        sleep = this.options.Sleep ?? Task.Delay;
        clock = this.options.TimeProvider ?? TimeProvider.System;
        userAgent = string.IsNullOrWhiteSpace(this.options.UserAgent) ? SdkUserAgent : SdkUserAgent + " " + this.options.UserAgent.Trim();
    }

    /// <summary>
    /// The Authorization header value for a key as pasted by an administrator. Supertext shows the key
    /// with its prefix, so a pasted prefix is stripped and exactly one is sent.
    /// </summary>
    /// <exception cref="SupertextException"><see cref="SupertextErrorCodes.MissingApiKey"/> for a blank key.</exception>
    public static string AuthHeader(string? apiKey)
    {
        var key = (apiKey ?? string.Empty).Trim();
        if (key.StartsWith(AuthPrefix, StringComparison.OrdinalIgnoreCase)
            && (key.Length == AuthPrefix.Length || char.IsWhiteSpace(key[AuthPrefix.Length])))
        {
            key = key[AuthPrefix.Length..].Trim();
        }
        if (key.Length == 0)
        {
            throw new SupertextException(SupertextErrorCodes.MissingApiKey, "No Supertext API key configured. No request was sent. " + KeyHelp);
        }
        return AuthPrefix + " " + key;
    }

    /// <summary>
    /// Milliseconds to wait before retrying an HTTP 429. A <c>Retry-After</c> in seconds wins (capped
    /// at 30 s); otherwise 1, 2, 4, 8, 16 s … plus 0–249 ms jitter, capped at 30 s.
    /// </summary>
    /// <param name="attempt">0-based retry number.</param>
    /// <param name="retryAfter">The raw <c>Retry-After</c> header, if any. HTTP dates and other values fall back to the backoff.</param>
    /// <param name="random">Source of the jitter (default <see cref="Random.Shared"/>).</param>
    public static int RetryDelayMs(int attempt, string? retryAfter, Random? random = null)
    {
        // Only plain seconds ("3", "1.5"); HTTP dates, signs, exponents, ".5" or "1." fall back to the backoff.
        var trimmed = retryAfter?.Trim(' ', '\t', '\n', '\r', '\f');
        if (trimmed is not null && RetryAfterSeconds().IsMatch(trimmed)
            && double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return (int)Math.Min(30_000, Math.Round(seconds * 1000));
        }
        var backoff = 1000 * Math.Pow(2, Math.Max(attempt, 0)) + (random ?? Random.Shared).Next(0, 250);
        return (int)Math.Min(30_000, backoff);
    }

    /// <summary>Checks that the key works (<c>GET features</c>, no cost).</summary>
    /// <exception cref="SupertextException">E.g. <see cref="SupertextErrorCodes.AuthenticationFailure"/>.</exception>
    public async Task ValidateApiKeyAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Get, "features", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Uploads an HTML document for translation and returns its file id.</summary>
    /// <param name="html">The document (UTF-8 HTML).</param>
    /// <param name="targetLang">Target language; passed through <see cref="LanguageCodes.Target"/> (<c>de</c> → <c>de-DE</c>).</param>
    /// <param name="sourceLang">Source language; passed through <see cref="LanguageCodes.Source"/> (<c>en-US</c> → <c>en</c>). Null or empty: Supertext detects it.</param>
    /// <param name="politeness">Formal or informal address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<string> SubmitAsync(string html, string targetLang, string? sourceLang = null,
        Politeness politeness = Politeness.Default, CancellationToken cancellationToken = default)
    {
        var target = LanguageCodes.Target(targetLang);
        var source = LanguageCodes.Source(sourceLang);
        var bytes = Encoding.UTF8.GetBytes(html);

        // Rebuilt for every attempt: HttpContent can't be sent twice.
        HttpContent BuildForm()
        {
            var form = new MultipartFormDataContent();
            AddField(form, "target_lang", target);
            if (source.Length > 0)
            {
                AddField(form, "source_lang", source);
            }
            if (politeness != Politeness.Default)
            {
                AddField(form, "politeness", politeness == Politeness.More ? "more" : "less");
            }
            // The part's Content-Type must be exactly "text/html": with a charset the API answers 415,
            // so no StringContent here.
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = "\"content.html\"" };
            form.Add(file);
            return form;
        }

        var response = await SendAsync(HttpMethod.Post, "translate/ai/file", BuildForm, cancellationToken).ConfigureAwait(false);
        var fileId = ReadJsonString(response.Body, "file_id");
        if (string.IsNullOrEmpty(fileId))
        {
            throw new SupertextException(SupertextErrorCodes.NoFileId, "Supertext did not return a file id.", response.Status);
        }
        return fileId;
    }

    /// <summary>
    /// The file's status as the API reports it: <c>translating</c>, <c>done</c>, <c>error</c>,
    /// <c>limit_exceeded</c>, <c>deleted</c> (or a status added later). Empty if the answer had none.
    /// </summary>
    public async Task<string> GetStatusAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, FilePath(fileId) + "/status", null, cancellationToken).ConfigureAwait(false);
        return ReadJsonString(response.Body, "status") ?? string.Empty;
    }

    /// <summary>
    /// Polls until the file is <c>done</c>: right away, then every <see cref="SupertextClientOptions.PollInterval"/>.
    /// Unknown statuses keep polling.
    /// </summary>
    /// <exception cref="SupertextException"><see cref="SupertextErrorCodes.TranslationError"/>, <see cref="SupertextErrorCodes.QuotaExceeded"/>,
    /// <see cref="SupertextErrorCodes.FileDeleted"/>, <see cref="SupertextErrorCodes.Timeout"/>, or an HTTP error.</exception>
    public async Task WaitUntilDoneAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var started = clock.GetTimestamp();
        while (true)
        {
            switch (await GetStatusAsync(fileId, cancellationToken).ConfigureAwait(false))
            {
                case "done":
                    return;
                case "error":
                    throw new SupertextException(SupertextErrorCodes.TranslationError, "Supertext could not translate the document.");
                case "limit_exceeded":
                    throw new SupertextException(SupertextErrorCodes.QuotaExceeded, "The Supertext account's translation limit is used up.");
                case "deleted":
                    throw new SupertextException(SupertextErrorCodes.FileDeleted, "The Supertext file was deleted before it could be downloaded.");
            }
            // Give up when the next poll would start after the timeout.
            if (clock.GetElapsedTime(started) + options.PollInterval > options.Timeout)
            {
                throw new SupertextException(SupertextErrorCodes.Timeout,
                    $"Supertext did not finish the translation within {options.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.");
            }
            await sleep(options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Downloads the translated document.</summary>
    /// <exception cref="SupertextException"><see cref="SupertextErrorCodes.IncompleteResponse"/> if it is empty, or an HTTP error.</exception>
    public async Task<string> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, FilePath(fileId) + "/translation", null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.Body))
        {
            throw new SupertextException(SupertextErrorCodes.IncompleteResponse, "The translated document was empty.", response.Status);
        }
        return response.Body;
    }

    /// <summary>Deletes the file at Supertext. Never throws: files expire after 24 h anyway.</summary>
    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, FilePath(fileId), null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Deliberately swallowed: cleanup must not hide the real result or error.
        }
    }

    /// <summary>
    /// Translates a whole HTML document: upload, wait, download, and always delete the file afterwards.
    /// Elements with a <c>data-st-id</c> attribute are translated each on its own.
    /// </summary>
    /// <inheritdoc cref="SubmitAsync" path="/param"/>
    public async Task<string> TranslateHtmlAsync(string html, string targetLang, string? sourceLang = null,
        Politeness politeness = Politeness.Default, CancellationToken cancellationToken = default)
    {
        var fileId = await SubmitAsync(html, targetLang, sourceLang, politeness, cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitUntilDoneAsync(fileId, cancellationToken).ConfigureAwait(false);
            return await DownloadAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Not the caller's token: the file should go even if the caller cancelled.
            await DeleteAsync(fileId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Translates keyed texts. They are packed into as few HTML documents as fit
    /// <paramref name="maxCharacters"/> (one <c>data-st-id</c> element per text), translated one
    /// document after the other, and returned under their keys in input order. Blank texts come back
    /// unchanged; if nothing is left to translate, no request is made.
    /// </summary>
    /// <param name="segments">Key → text, in the order you want them back (e.g. a <c>Dictionary</c> or a list of pairs).</param>
    /// <param name="targetLang">Target language; passed through <see cref="LanguageCodes.Target"/>.</param>
    /// <param name="sourceLang">Source language; passed through <see cref="LanguageCodes.Source"/>. Null or empty: auto-detect.</param>
    /// <param name="politeness">Formal or informal address.</param>
    /// <param name="maxCharacters">Packed characters per document.</param>
    /// <param name="cancellationToken">Cancels the translation.</param>
    /// <exception cref="SupertextException"><see cref="SupertextErrorCodes.IncompleteResponse"/> (detail <c>missing: key1, key2</c>) if texts are missing in the answer, or any other error.</exception>
    /// <exception cref="ArgumentException">A key occurs twice.</exception>
    public async Task<IReadOnlyDictionary<string, string>> TranslateTextsAsync(
        IEnumerable<KeyValuePair<string, Segment>> segments, string targetLang, string? sourceLang = null,
        Politeness politeness = Politeness.Default, int maxCharacters = DefaultMaxCharacters, CancellationToken cancellationToken = default)
    {
        var input = segments.ToList();
        var result = new TranslationResult(input);
        foreach (var batch in Batches(input, maxCharacters))
        {
            var batchSegments = batch.Select(pair => pair.Value).ToList();
            var translated = await TranslateHtmlAsync(HtmlDocument.Build(batchSegments), targetLang, sourceLang, politeness, cancellationToken).ConfigureAwait(false);
            var parsed = HtmlDocument.Parse(translated, batchSegments);
            var missing = batch.Where((_, i) => parsed[i] is null).Select(pair => pair.Key).ToList();
            if (missing.Count > 0)
            {
                throw new SupertextException(SupertextErrorCodes.IncompleteResponse,
                    "Texts are missing from the translated document.", detail: "missing: " + string.Join(", ", missing));
            }
            for (var i = 0; i < batch.Count; i++)
            {
                result.Set(batch[i].Key, parsed[i]!);
            }
        }
        return result;
    }

    /// <summary>
    /// Splits the non-blank segments into documents: keep adding while the packed length (code points)
    /// stays within <paramref name="maxCharacters"/>; a segment longer than that goes alone.
    /// </summary>
    internal static List<List<KeyValuePair<string, Segment>>> Batches(IEnumerable<KeyValuePair<string, Segment>> segments, int maxCharacters)
    {
        var batches = new List<List<KeyValuePair<string, Segment>>>();
        List<KeyValuePair<string, Segment>>? current = null;
        long length = 0;
        foreach (var pair in segments)
        {
            if (IsBlank(pair.Value.Text))
            {
                continue;
            }
            var size = CodePoints(HtmlDocument.Content(pair.Value));
            if (current is null || (current.Count > 0 && length + size > maxCharacters))
            {
                current = [];
                batches.Add(current);
                length = 0;
            }
            current.Add(pair);
            length += size;
        }
        return batches;
    }

    internal static bool IsBlank(string text) => text.AsSpan().Trim(" \t\r\n\f").IsEmpty;

    private static int CodePoints(string s)
    {
        var count = s.Length;
        for (var i = 1; i < s.Length; i++)
        {
            if (char.IsSurrogatePair(s[i - 1], s[i]))
            {
                count--;
            }
        }
        return count;
    }

    private static string FilePath(string fileId) => "translate/ai/file/" + Uri.EscapeDataString(fileId);

    private static void AddField(MultipartFormDataContent form, string name, string value)
    {
        // No Content-Type on plain fields, and quoted names (name="target_lang") as browsers send them:
        // .NET leaves names unquoted by default, which stricter multipart parsers reject.
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(value));
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
        form.Add(part);
    }

    private sealed record Response(int Status, string Body);

    /// <summary>Sends one request (retrying 429s), reads the whole answer, and maps errors.</summary>
    private async Task<Response> SendAsync(HttpMethod method, string path, Func<HttpContent>? content, CancellationToken cancellationToken)
    {
        // Throws missing_api_key before anything is sent.
        var authorization = AuthHeader(apiKey);
        var uri = new Uri(baseUri, path);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri) { Content = content?.Invoke() };
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            int status;
            string body;
            string? retryAfter = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(options.RequestTimeout);
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    // NonValidated: keep values like "1.5" that the typed RetryAfter header would drop.
                    if (response.Headers.NonValidated.TryGetValues("Retry-After", out var values))
                    {
                        retryAfter = values.FirstOrDefault();
                    }
                    body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException e)
                {
                    throw new SupertextException(SupertextErrorCodes.TransportError, "Could not reach Supertext.",
                        detail: $"Request timed out after {options.RequestTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s", innerException: e);
                }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException)
                {
                    throw new SupertextException(SupertextErrorCodes.TransportError, "Could not reach Supertext.", detail: Redact(e.Message), innerException: Safe(e));
                }
            }

            if (status is >= 200 and < 300)
            {
                return new Response(status, body);
            }
            if (status == 429 && attempt < options.MaxRetries)
            {
                await sleep(TimeSpan.FromMilliseconds(RetryDelayMs(attempt, retryAfter)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            throw HttpError(status, body);
        }
    }

    /// <summary>Maps a non-2xx answer to a <see cref="SupertextException"/> (spec/errors.json).</summary>
    internal SupertextException HttpError(int status, string body)
    {
        var detail = Tags().Replace(body, string.Empty).Trim(' ', '\t', '\n', '\r', '\f');
        if (detail.Length > 200)
        {
            // Don't cut a surrogate pair in half.
            detail = detail[..(char.IsHighSurrogate(detail[199]) ? 199 : 200)];
        }
        detail = Redact(detail);

        string? apiErrorCode = null, sourceLang = null, targetLang = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                apiErrorCode = StringProperty(json.RootElement, "error_code");
                sourceLang = StringProperty(json.RootElement, "source_lang");
                targetLang = StringProperty(json.RootElement, "target_lang");
            }
        }
        catch (JsonException)
        {
            // Not JSON: no API error code.
        }

        var invalidPair = (status == 400 || status == 404) && (apiErrorCode == "INVALID_LANGUAGE_PAIR" || body.Contains("INVALID_LANGUAGE_PAIR", StringComparison.Ordinal));
        var (code, message) = status switch
        {
            _ when invalidPair => (SupertextErrorCodes.InvalidLanguagePair,
                "Supertext rejected the language pair. The source must be a bare language (en), the target a regional code (de-DE)."),
            401 or 403 => (SupertextErrorCodes.AuthenticationFailure, "Supertext rejected the API key. " + KeyHelp),
            404 => (SupertextErrorCodes.NotFound, "Supertext does not know this file (expired or already deleted)."),
            413 => (SupertextErrorCodes.PayloadTooLarge, "The document is too large for Supertext."),
            429 => (SupertextErrorCodes.TooManyRequests, "Too many requests to Supertext. Please try again shortly."),
            >= 500 and < 600 => (SupertextErrorCodes.ServiceUnavailable, "The Supertext service is unavailable."),
            _ => (SupertextErrorCodes.UnexpectedStatus, $"Supertext answered with HTTP {status.ToString(CultureInfo.InvariantCulture)}."),
        };
        return new SupertextException(code, message, status, detail, apiErrorCode,
            invalidPair ? sourceLang : null, invalidPair ? targetLang : null);
    }

    /// <summary>Removes the API key from text that came from elsewhere (server answers, exception messages).</summary>
    private string Redact(string text)
    {
        string key;
        try
        {
            key = AuthHeader(apiKey)[(AuthPrefix.Length + 1)..];
        }
        catch (SupertextException)
        {
            return text;
        }
        return text.Replace(key, "***", StringComparison.Ordinal);
    }

    /// <summary>The exception to keep as inner exception, unless it mentions the key (ToString includes it).</summary>
    private Exception? Safe(Exception e) => Redact(e.ToString()) == e.ToString() ? e : null;

    private static string? ReadJsonString(string body, string property)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object ? StringProperty(json.RootElement, property) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            // Strings only: a numeric or null error_code/source_lang is not an API code.
            JsonValueKind.String => value.GetString(),
            _ => null,
        } : null;

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?\z")]
    private static partial Regex RetryAfterSeconds();
}
