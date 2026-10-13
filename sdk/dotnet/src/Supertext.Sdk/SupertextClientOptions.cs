namespace Supertext.Sdk;

/// <summary>Formality of the translation, for languages that distinguish it.</summary>
public enum Politeness
{
    /// <summary>Let Supertext decide (nothing is sent).</summary>
    Default,

    /// <summary>Formal (German "Sie", French "vous").</summary>
    More,

    /// <summary>Informal (German "du", French "tu").</summary>
    Less,
}

/// <summary>Options for <see cref="SupertextClient"/>. All have working defaults.</summary>
public sealed class SupertextClientOptions
{
    /// <summary>API base URL; a trailing slash is added if missing. Default <see cref="SupertextClient.LiveBaseUrl"/>.</summary>
    public string BaseUrl { get; init; } = SupertextClient.LiveBaseUrl;

    /// <summary>Wait between status polls. Default 2 s.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait for a file to be translated. Default 180 s.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>Timeout of a single HTTP request, including reading the answer. Default 30 s.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries after HTTP 429 (the API limits requests per second per key). Default 4.</summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>
    /// Appended to the User-Agent header, e.g. <c>Umbraco-Supertext-Translation/1.2.0</c>, so
    /// Supertext can tell integrations apart.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// The HttpClient to use (e.g. from <c>IHttpClientFactory</c>). Its own <c>Timeout</c> should be at
    /// least <see cref="RequestTimeout"/>. Default: a shared client owned by the SDK.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>How the client waits (polling, retries). Default <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; replace it in tests.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Sleep { get; init; }

    /// <summary>Clock for the polling timeout and <see cref="ApiKeyCheck.CheckedAt"/>. Default <see cref="TimeProvider.System"/>; replace it in tests.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>A copy with another clock (the reporter's, for the key check).</summary>
    internal SupertextClientOptions WithTimeProvider(TimeProvider timeProvider) => new()
    {
        BaseUrl = BaseUrl,
        PollInterval = PollInterval,
        Timeout = Timeout,
        RequestTimeout = RequestTimeout,
        MaxRetries = MaxRetries,
        UserAgent = UserAgent,
        HttpClient = HttpClient,
        Sleep = Sleep,
        TimeProvider = timeProvider,
    };
}
