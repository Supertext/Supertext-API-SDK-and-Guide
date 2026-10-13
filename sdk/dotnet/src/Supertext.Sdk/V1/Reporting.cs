using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Supertext.Sdk.V1;

/// <summary>
/// Version reporting to the Supertext Commando Center, the automatic API key check and the callback ping
/// (spec/reporting.json). See <see cref="InstallationReporter"/> and <see cref="PingResponse"/>.
/// </summary>
public static partial class Reporting
{
    /// <summary>The live Commando Center. <c>SUPERTEXT_REPORT_URL</c> in the environment overrides it.</summary>
    public const string DefaultReportUrl = "https://commandocenter-production.up.railway.app/api/v1/report";

    /// <summary>Timeout of a report.</summary>
    public static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Longest timeout of the API key check (<see cref="SupertextClient.CheckApiKeyAsync"/>).</summary>
    public static readonly TimeSpan KeyCheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>No new report within this time after a failed one (except <see cref="InstallationReporter.ReportCheckAsync"/>).</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(3600);

    /// <summary>
    /// Whether version reports are on: <c>SUPERTEXT_VERSION_REPORTS</c> from the environment
    /// (0/false/off/no, 1/true/on/yes) wins over the setting; the setting defaults to on.
    /// </summary>
    /// <param name="setting">The "Send version information to Supertext" setting; null if not set.</param>
    public static bool ReportsEnabled(bool? setting) =>
        ReportsEnabled(setting, Environment.GetEnvironmentVariable("SUPERTEXT_VERSION_REPORTS"));

    /// <summary>Like <see cref="ReportsEnabled(bool?)"/>, with the environment value passed in.</summary>
    public static bool ReportsEnabled(bool? setting, string? env)
    {
        switch ((env ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "0" or "false" or "off" or "no":
                return false;
            case "1" or "true" or "on" or "yes":
                return true;
            default:
                return setting ?? true;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{16,64}\z")]
    internal static partial Regex Challenge();
}

/// <summary>The events of a report (<see cref="ReporterResult.Event"/>).</summary>
public static class ReportEvents
{
    /// <summary>First report of this installation.</summary>
    public const string Installed = "installed";

    /// <summary>The plugin's settings were saved.</summary>
    public const string SettingsSaved = "settings_saved";

    /// <summary>The plugin, system or runtime version changed.</summary>
    public const string Upgraded = "upgraded";

    /// <summary>The API key's status changed.</summary>
    public const string ApiKeyCheck = "api_key_check";

    /// <summary>Sent on request (<see cref="InstallationReporter.ReportCheckAsync"/>).</summary>
    public const string Check = "check";
}

/// <summary>A name and an optional version (system, runtime, plugin).</summary>
public sealed record NameVersion(string Name, string? Version = null);

/// <summary>
/// What the plugin stores between requests. Store <see cref="ToJson"/> as it is and load it with
/// <see cref="FromJson(string?)"/>.
/// </summary>
/// <param name="ReportedVersions"><c>&lt;plugin&gt;|&lt;system&gt;|&lt;runtime&gt;</c> versions last reported successfully.</param>
/// <param name="ReportedKeyStatus">The key status last reported successfully.</param>
/// <param name="LastFailureAt">When the last report failed (no new attempt for an hour).</param>
/// <param name="KeyCheck">The latest key check.</param>
public sealed record ReporterState(string? ReportedVersions, string? ReportedKeyStatus, string? LastFailureAt, ApiKeyCheck? KeyCheck)
{
    /// <summary>Nothing reported, nothing checked.</summary>
    public static ReporterState Empty { get; } = new(null, null, null, null);

    /// <summary>The state as JSON: <c>{"reportedVersions":…,"reportedKeyStatus":…,"lastFailureAt":…,"keyCheck":{…}|null}</c>.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            WriteString(w, "reportedVersions", ReportedVersions);
            WriteString(w, "reportedKeyStatus", ReportedKeyStatus);
            WriteString(w, "lastFailureAt", LastFailureAt);
            if (KeyCheck is { } k)
            {
                w.WriteStartObject("keyCheck");
                w.WriteString("status", k.Status);
                w.WriteString("checkedAt", k.CheckedAt);
                if (k.HttpStatus is { } httpStatus)
                {
                    w.WriteNumber("httpStatus", httpStatus);
                }
                else
                {
                    w.WriteNull("httpStatus");
                }
                WriteString(w, "errorCode", k.ErrorCode);
                w.WriteString("fingerprint", k.Fingerprint);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("keyCheck");
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A stored state; null, broken JSON or unknown values give an empty state (or fields).</summary>
    public static ReporterState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FromJson(doc.RootElement);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>A stored state from parsed JSON; anything but an object gives an empty state.</summary>
    public static ReporterState FromJson(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }
        ApiKeyCheck? keyCheck = null;
        if (json.TryGetProperty("keyCheck", out var k) && k.ValueKind == JsonValueKind.Object
            && Str(k, "status") is { } status && Str(k, "checkedAt") is { } checkedAt)
        {
            int? httpStatus = k.TryGetProperty("httpStatus", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var n) ? n : null;
            keyCheck = new ApiKeyCheck(status, checkedAt, httpStatus, Str(k, "errorCode"), Str(k, "fingerprint") ?? string.Empty);
        }
        return new ReporterState(Str(json, "reportedVersions"), Str(json, "reportedKeyStatus"), Str(json, "lastFailureAt"), keyCheck);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void WriteString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WriteString(name, value);
        }
    }
}

/// <summary>The outcome of an <see cref="InstallationReporter"/> call.</summary>
/// <param name="State">Store this.</param>
/// <param name="Check">The latest key check (show it on the settings or status page).</param>
/// <param name="Event">The event that was reported (or tried), one of <see cref="ReportEvents"/>; null when nothing was due.</param>
/// <param name="Sent">Whether the center accepted the report.</param>
public sealed record ReporterResult(ReporterState State, ApiKeyCheck? Check, string? Event, bool Sent);

/// <summary>Options for <see cref="InstallationReporter"/>.</summary>
public sealed class ReporterOptions
{
    /// <summary>The "Send version information to Supertext" setting combined with the environment: see <see cref="Reporting.ReportsEnabled(bool?)"/>.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Base URL of the installation, as the system knows it.</summary>
    public required string SiteUrl { get; init; }

    /// <summary><c>Name</c> = the plugin's GitHub repository name; <c>Version</c> from its official version source.</summary>
    public required NameVersion Plugin { get; init; }

    /// <summary>The CMS, PIM, shop or application, in its own spelling.</summary>
    public required NameVersion System { get; init; }

    /// <summary>Default: <c>.NET</c> and <see cref="Environment.Version"/>.</summary>
    public NameVersion? Runtime { get; init; }

    /// <summary>Default: <see cref="SdkInfo.Version"/>.</summary>
    public string? SdkVersion { get; init; }

    /// <summary>The plugin's public ping endpoint; null for hosted apps without one.</summary>
    public string? CallbackUrl { get; init; }

    /// <summary>Default: <c>SUPERTEXT_REPORT_URL</c> from the environment, else <see cref="Reporting.DefaultReportUrl"/>.</summary>
    public string? ReportUrl { get; init; }

    /// <summary>Options of the client that runs the key check (base URL, HttpClient, user agent, fakes for tests).</summary>
    public SupertextClientOptions? ClientOptions { get; init; }

    /// <summary>HttpClient for the report. Default: <see cref="ClientOptions"/>' HttpClient, else a shared client.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Clock (also used for the key check). Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// Reports versions and the API key status to the Supertext Commando Center (spec/reporting.json).
/// Stateless: load the plugin's stored <see cref="ReporterState"/>, pass it in, store the one you get back.
/// Never throws (except when the caller's token is cancelled), and never sends the API key.
/// </summary>
public sealed class InstallationReporter
{
    private readonly ReporterOptions options;
    private readonly TimeProvider clock;
    private readonly HttpClient http;
    private readonly NameVersion runtime;

    /// <summary>Creates a reporter.</summary>
    public InstallationReporter(ReporterOptions options)
    {
        this.options = options;
        clock = options.TimeProvider ?? TimeProvider.System;
        http = options.HttpClient ?? options.ClientOptions?.HttpClient ?? SupertextClient.SharedHttpClient.Value;
        runtime = options.Runtime ?? new NameVersion(".NET", Environment.Version.ToString());
        ReportUrl = NonEmpty(options.ReportUrl) ?? NonEmpty(Environment.GetEnvironmentVariable("SUPERTEXT_REPORT_URL")) ?? Reporting.DefaultReportUrl;
    }

    /// <summary>Where reports go.</summary>
    public string ReportUrl { get; }

    /// <summary><c>&lt;plugin&gt;|&lt;system&gt;|&lt;runtime&gt;</c> versions.</summary>
    public string Versions => string.Join('|', options.Plugin.Version ?? string.Empty, options.System.Version ?? string.Empty, runtime.Version ?? string.Empty);

    /// <summary>After the plugin's settings were saved: checks the key, reports <c>installed</c> or <c>settings_saved</c>.</summary>
    public async Task<ReporterResult> AfterSettingsSavedAsync(ReporterState? state, string? apiKey, CancellationToken cancellationToken = default)
    {
        var current = (state ?? ReporterState.Empty) with { KeyCheck = await CheckAsync(apiKey, cancellationToken).ConfigureAwait(false) };
        return await ReportIfDueAsync(current, current.ReportedVersions is null ? ReportEvents.Installed : ReportEvents.SettingsSaved, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>On back-end requests that run the plugin's code: checks a changed key, reports what changed.</summary>
    public async Task<ReporterResult> OnRequestAsync(ReporterState? state, string? apiKey, CancellationToken cancellationToken = default)
    {
        var current = state ?? ReporterState.Empty;
        if (current.KeyCheck is null || current.KeyCheck.Fingerprint != ApiKeyCheck.FingerprintOf(apiKey))
        {
            current = current with { KeyCheck = await CheckAsync(apiKey, cancellationToken).ConfigureAwait(false) };
        }
        return await ReportIfDueAsync(current, DueEvent(current), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A translation was rejected with 401/403: the key is invalid (no request to Supertext).</summary>
    public Task<ReporterResult> AuthenticationFailedAsync(ReporterState? state, string? apiKey, int httpStatus = 401, CancellationToken cancellationToken = default)
    {
        var keyCheck = new ApiKeyCheck(ApiKeyCheckStatus.Invalid, ApiKeyCheck.IsoSeconds(clock.GetUtcNow()), httpStatus, null, ApiKeyCheck.FingerprintOf(apiKey));
        var current = (state ?? ReporterState.Empty) with { KeyCheck = keyCheck };
        return ReportIfDueAsync(current, DueEvent(current), cancellationToken);
    }

    /// <summary>The "Test connection" button: checks the key, reports a change.</summary>
    public async Task<ReporterResult> TestConnectionAsync(ReporterState? state, string? apiKey, CancellationToken cancellationToken = default)
    {
        var current = (state ?? ReporterState.Empty) with { KeyCheck = await CheckAsync(apiKey, cancellationToken).ConfigureAwait(false) };
        return await ReportIfDueAsync(current, DueEvent(current), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a <c>check</c> report now (commands, tests), even after a recent failure.</summary>
    public Task<ReporterResult> ReportCheckAsync(ReporterState? state, CancellationToken cancellationToken = default) =>
        SendAsync(state ?? ReporterState.Empty, ReportEvents.Check, cancellationToken);

    /// <summary>The report body for an event (one of <see cref="ReportEvents"/>) and state.</summary>
    public JsonObject BuildReport(string reportEvent, ReporterState state)
    {
        var body = new JsonObject
        {
            ["schema"] = 1,
            ["event"] = reportEvent,
            ["site_url"] = options.SiteUrl,
            ["plugin"] = new JsonObject { ["name"] = options.Plugin.Name, ["version"] = options.Plugin.Version ?? string.Empty },
            ["system"] = NameVersionJson(options.System),
            ["runtime"] = NameVersionJson(runtime),
            ["sdk_version"] = options.SdkVersion ?? SdkInfo.Version,
        };
        if (state.KeyCheck is { } check)
        {
            body["api_key_configured"] = check.Fingerprint.Length > 0;
            var c = new JsonObject { ["status"] = check.Status, ["checked_at"] = check.CheckedAt };
            if (check.HttpStatus is { } httpStatus)
            {
                c["http_status"] = httpStatus;
            }
            if (check.ErrorCode is { } errorCode)
            {
                c["error_code"] = errorCode;
            }
            body["api_key_check"] = c;
        }
        if (!string.IsNullOrEmpty(options.CallbackUrl))
        {
            body["callback"] = new JsonObject { ["url"] = options.CallbackUrl };
        }
        return body;
    }

    private string? DueEvent(ReporterState state)
    {
        if (state.ReportedVersions is null)
        {
            return ReportEvents.Installed;
        }
        if (state.ReportedVersions != Versions)
        {
            return ReportEvents.Upgraded;
        }
        if (state.KeyCheck is not null && state.KeyCheck.Status != state.ReportedKeyStatus)
        {
            return ReportEvents.ApiKeyCheck;
        }
        return null;
    }

    private Task<ApiKeyCheck> CheckAsync(string? apiKey, CancellationToken cancellationToken) =>
        new SupertextClient(apiKey, (options.ClientOptions ?? new SupertextClientOptions()).WithTimeProvider(clock)).CheckApiKeyAsync(cancellationToken);

    private Task<ReporterResult> ReportIfDueAsync(ReporterState state, string? reportEvent, CancellationToken cancellationToken)
    {
        if (reportEvent is null || !options.Enabled)
        {
            return Task.FromResult(new ReporterResult(state, state.KeyCheck, null, false));
        }
        if (state.LastFailureAt is not null
            && DateTimeOffset.TryParse(state.LastFailureAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var failed)
            && clock.GetUtcNow() - failed < Reporting.FailureBackoff)
        {
            return Task.FromResult(new ReporterResult(state, state.KeyCheck, null, false));
        }
        return SendAsync(state, reportEvent, cancellationToken);
    }

    private async Task<ReporterResult> SendAsync(ReporterState state, string reportEvent, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new ReporterResult(state, state.KeyCheck, null, false);
        }
        var ok = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Reporting.ReportTimeout);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(BuildReport(reportEvent, state).ToJsonString()));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ReportUrl, UriKind.Absolute)) { Content = content };
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", $"supertext-sdk-dotnet {options.Plugin.Name}/{options.Plugin.Version}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            ok = (int)response.StatusCode is >= 200 and < 300;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ok = false; // Never fails the caller: the center is optional.
        }
        var next = ok
            ? state with { ReportedVersions = Versions, ReportedKeyStatus = state.KeyCheck?.Status ?? state.ReportedKeyStatus, LastFailureAt = null }
            : state with { LastFailureAt = ApiKeyCheck.IsoSeconds(clock.GetUtcNow()) };
        return new ReporterResult(next, next.KeyCheck, reportEvent, ok);
    }

    private static JsonObject NameVersionJson(NameVersion value) =>
        string.IsNullOrEmpty(value.Version)
            ? new JsonObject { ["name"] = value.Name }
            : new JsonObject { ["name"] = value.Name, ["version"] = value.Version };

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The answer of the plugin's public ping endpoint (<c>GET &lt;callback&gt;?challenge=&lt;c&gt;</c>), which the
/// Commando Center calls to confirm a reported callback URL.
/// </summary>
/// <param name="Status">200, 400 (bad or missing challenge) or 404 (reporting off).</param>
/// <param name="Headers"><c>Content-Type: application/json</c> and <c>Cache-Control: no-store</c>.</param>
/// <param name="Body">The JSON body.</param>
public sealed record PingResponse(int Status, IReadOnlyDictionary<string, string> Headers, string Body)
{
    /// <summary>
    /// The answer for a request's <c>challenge</c> parameter. Only while reporting is on (else 404); the
    /// challenge is echoed only if it matches <c>^[A-Za-z0-9_-]{16,64}$</c>.
    /// </summary>
    /// <param name="challenge">The <c>challenge</c> query parameter; null if missing.</param>
    /// <param name="plugin">The plugin's GitHub repository name.</param>
    /// <param name="enabled">Whether version reporting is on (<see cref="Reporting.ReportsEnabled(bool?)"/>).</param>
    public static PingResponse For(string? challenge, string plugin, bool enabled)
    {
        var headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["Cache-Control"] = "no-store",
        });
        if (!enabled)
        {
            return new PingResponse(404, headers, """{"ok":false}""");
        }
        if (challenge is null || !Reporting.Challenge().IsMatch(challenge))
        {
            return new PingResponse(400, headers, """{"ok":false}""");
        }
        var body = new JsonObject { ["ok"] = true, ["plugin"] = plugin, ["challenge"] = challenge };
        return new PingResponse(200, headers, body.ToJsonString());
    }
}
