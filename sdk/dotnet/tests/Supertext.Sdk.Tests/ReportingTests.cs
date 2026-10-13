using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supertext.Sdk.Tests;

/// <summary>spec/reporting.json and the Commando Center's ping cases (spec/commando/callback-vectors.json).</summary>
public class ReportingTests
{
    private const string ReportUrl = "https://center.test/api/v1/report";
    private const string ApiUrl = "https://api.test/v1/";

    private static readonly JsonElement S = Spec.Load("reporting.json");

    public static IEnumerable<object[]> FingerprintCases() => Spec.Cases("reporting.json", "fingerprint");
    public static IEnumerable<object[]> ApiKeyCheckCases() => Spec.Cases("reporting.json", "apiKeyCheck");
    public static IEnumerable<object[]> EnabledCases() => Spec.Cases("reporting.json", "enabled");
    public static IEnumerable<object[]> ScenarioCases() => Spec.Cases("reporting.json", "scenarios", "name");

    public static IEnumerable<object[]> PingCases()
    {
        var i = 0;
        foreach (var c in S.GetProperty("ping").GetProperty("cases").EnumerateArray())
        {
            yield return [$"ping[{i++}]", c.GetRawText()];
        }
    }

    public static IEnumerable<object[]> EndpointCases()
    {
        var i = 0;
        foreach (var c in Spec.Load("commando/callback-vectors.json").GetProperty("endpoint").GetProperty("cases").EnumerateArray())
        {
            yield return [$"endpoint[{i++}]", c.GetRawText()];
        }
    }

    [Fact]
    public void HasTheSpecDefaults()
    {
        var d = S.GetProperty("defaults");
        Assert.Equal(Reporting.DefaultReportUrl, d.Str("reportUrl"));
        Assert.Equal(TimeSpan.FromSeconds(d.GetProperty("reportTimeoutSeconds").GetInt32()), Reporting.ReportTimeout);
        Assert.Equal(TimeSpan.FromSeconds(d.GetProperty("keyCheckTimeoutSeconds").GetInt32()), Reporting.KeyCheckTimeout);
        Assert.Equal(TimeSpan.FromSeconds(d.GetProperty("failureBackoffSeconds").GetInt32()), Reporting.FailureBackoff);
    }

    [Fact]
    public void SdkVersionMatchesThePackage()
    {
        Assert.Equal(SupertextClient.SdkVersion, typeof(SupertextClient).Assembly.GetName().Version!.ToString(3));
    }

    [Theory]
    [MemberData(nameof(FingerprintCases))]
    public void Fingerprint(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), ApiKeyCheck.FingerprintOf(c.Str("apiKey")));
    }

    [Theory]
    [MemberData(nameof(ApiKeyCheckCases))]
    public async Task CheckApiKey(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var http = new FakeCenter(c.GetProperty("answer"), null);
        var client = new SupertextClient(c.Str("apiKey"), new SupertextClientOptions
        {
            BaseUrl = ApiUrl,
            HttpClient = new HttpClient(http),
            Sleep = (_, _) => Task.CompletedTask,
            TimeProvider = new FixedTime(DateTimeOffset.Parse("2026-10-13T08:00:00.500Z")),
        });
        var check = await client.CheckApiKeyAsync();
        int? httpStatus = c.GetProperty("httpStatus").ValueKind == JsonValueKind.Number ? c.GetProperty("httpStatus").GetInt32() : null;
        Assert.Equal(new ApiKeyCheck(c.Str("status")!, "2026-10-13T08:00:00Z", httpStatus, c.Str("errorCode"), ApiKeyCheck.FingerprintOf(c.Str("apiKey"))), check);
        Assert.Equal(c.TryGetProperty("requests", out var r) ? r.GetInt32() : 1, http.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(EnabledCases))]
    public void Enabled(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        bool? setting = c.GetProperty("setting").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        Assert.Equal(c.GetProperty("out").GetBoolean(), Reporting.ReportsEnabled(setting, c.Str("env")));
    }

    [Theory]
    [MemberData(nameof(PingCases))]
    public void Ping(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var r = PingResponse.For(c.Str("challenge"), S.GetProperty("ping").Str("plugin")!, c.GetProperty("enabled").GetBoolean());
        Assert.Equal(c.GetProperty("status").GetInt32(), r.Status);
        AssertJson(JsonNode.Parse(c.GetProperty("body").GetRawText()), JsonNode.Parse(r.Body));
        Assert.Equal(new Dictionary<string, string> { ["Content-Type"] = "application/json", ["Cache-Control"] = "no-store" }, r.Headers);
    }

    [Theory]
    [MemberData(nameof(EndpointCases))]
    public void PingEndpoint(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var r = PingResponse.For(c.Str("challenge"), "Some-Supertext-Translation", enabled: true);
        Assert.Equal(c.GetProperty("status").GetInt32(), r.Status);
        var expected = JsonNode.Parse(c.GetProperty("body").GetRawText())!.AsObject();
        if (expected.ContainsKey("plugin"))
        {
            expected["plugin"] = "Some-Supertext-Translation";
        }
        AssertJson(expected, JsonNode.Parse(r.Body));
    }

    [Theory]
    [MemberData(nameof(ScenarioCases))]
    public async Task Scenario(string name, string json)
    {
        _ = name;
        var scenario = JsonDocument.Parse(json).RootElement;
        var config = JsonNode.Parse(S.GetProperty("config").GetRawText())!.AsObject();
        if (scenario.TryGetProperty("config", out var overrides))
        {
            foreach (var p in overrides.EnumerateObject())
            {
                config[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            }
        }
        var state = ReporterState.FromJson(scenario.GetProperty("state"));
        foreach (var step in scenario.GetProperty("steps").EnumerateArray())
        {
            var action = step.Str("action")!;
            var http = new FakeCenter(Prop(step, "features"), Prop(step, "report"));
            var reporter = Reporter(config, http, new FixedTime(DateTimeOffset.Parse(step.Str("now")!)));
            var apiKey = step.Str("apiKey");
            var r = action switch
            {
                "afterSettingsSaved" => await reporter.AfterSettingsSavedAsync(state, apiKey),
                "onRequest" => await reporter.OnRequestAsync(state, apiKey),
                "authenticationFailed" => await reporter.AuthenticationFailedAsync(state, apiKey, step.GetProperty("httpStatus").GetInt32()),
                "testConnection" => await reporter.TestConnectionAsync(state, apiKey),
                "reportCheck" => await reporter.ReportCheckAsync(state),
                _ => throw new InvalidOperationException("unknown action " + action),
            };
            var expect = step.GetProperty("expect");
            Assert.Equal(expect.GetProperty("requests").EnumerateArray().Select(e => e.GetString()!), http.Requests);
            Assert.Equal(expect.Str("event"), r.Event);
            Assert.Equal(expect.GetProperty("sent").GetBoolean(), r.Sent);
            var expectedState = ReporterState.FromJson(expect.GetProperty("state"));
            Assert.Equal(expectedState, r.State);
            Assert.Equal(ReporterState.FromJson($$"""{"keyCheck":{{expect.GetProperty("check").GetRawText()}}}""").KeyCheck, r.Check);
            // The stored form has exactly the spec's shape and survives a round trip.
            AssertJson(JsonNode.Parse(expect.GetProperty("state").GetRawText()), JsonNode.Parse(r.State.ToJson()));
            if (expect.TryGetProperty("reportBody", out var body))
            {
                AssertJson(JsonNode.Parse(body.GetRawText()), http.Bodies[0]);
                Assert.Equal("supertext-sdk-dotnet Directus-Supertext-Translation/" + (string?)config["plugin"]!["version"], http.UserAgents[0]);
            }
            state = ReporterState.FromJson(r.State.ToJson());
            Assert.Equal(expectedState, state);
        }
    }

    [Fact]
    public void BuildsReportsWithTheFieldsOfTheCenterSchema()
    {
        var schema = Spec.Load("commando/report-v1.schema.json");
        var reporter = Reporter(JsonNode.Parse(S.GetProperty("config").GetRawText())!.AsObject(), new FakeCenter(null, null), TimeProvider.System);
        var body = reporter.BuildReport(ReportEvents.SettingsSaved, ReporterState.Empty);
        foreach (var field in schema.GetProperty("required").EnumerateArray())
        {
            Assert.True(body.ContainsKey(field.GetString()!), field.GetString());
        }
        foreach (var (field, _) in body)
        {
            Assert.True(schema.GetProperty("properties").TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task NeverSendsTheKey()
    {
        var http = new FakeCenter(Answer("""{"status":200,"body":"{}"}"""), Answer("""{"status":202}"""));
        var reporter = Reporter(JsonNode.Parse(S.GetProperty("config").GetRawText())!.AsObject(), http, TimeProvider.System);
        var r = await reporter.AfterSettingsSavedAsync(null, "secret-key-123");
        Assert.True(r.Sent);
        Assert.DoesNotContain("secret-key-123", string.Join("\n", http.Bodies.Select(b => b!.ToJsonString())));
        Assert.DoesNotContain("secret-key-123", r.State.ToJson());
        Assert.DoesNotContain("secret-key-123", r.ToString());
    }

    [Fact]
    public void TreatsABrokenStoredStateAsEmpty()
    {
        Assert.Equal(ReporterState.Empty, ReporterState.FromJson("not json"));
        Assert.Equal(ReporterState.Empty, ReporterState.FromJson((string?)null));
        Assert.Equal(ReporterState.Empty, ReporterState.FromJson("[1,2]"));
        Assert.Null(ReporterState.FromJson("""{"keyCheck":{"status":"ok"}}""").KeyCheck);
        Assert.Equal("""{"reportedVersions":null,"reportedKeyStatus":null,"lastFailureAt":null,"keyCheck":null}""", ReporterState.Empty.ToJson());
    }

    [Fact]
    public void DetectsTheRuntime()
    {
        var config = JsonNode.Parse(S.GetProperty("config").GetRawText())!.AsObject();
        config.Remove("runtime");
        var reporter = Reporter(config, new FakeCenter(null, null), TimeProvider.System);
        AssertJson(new JsonObject { ["name"] = ".NET", ["version"] = Environment.Version.ToString() }, reporter.BuildReport(ReportEvents.Check, ReporterState.Empty)["runtime"]);
        Assert.EndsWith("|" + Environment.Version, reporter.Versions);
    }

    [Fact]
    public async Task ACancelledCallerTokenIsNotSwallowed()
    {
        var http = new FakeCenter(Answer("""{"status":200,"body":"{}"}"""), Answer("""{"status":202}"""));
        var reporter = Reporter(JsonNode.Parse(S.GetProperty("config").GetRawText())!.AsObject(), http, TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reporter.AfterSettingsSavedAsync(null, "k1", cts.Token));
    }

    private static InstallationReporter Reporter(JsonObject config, FakeCenter http, TimeProvider clock)
    {
        static NameVersion Nv(JsonNode? n) => new((string)n!["name"]!, (string?)n["version"]);
        var client = new HttpClient(http);
        return new InstallationReporter(new ReporterOptions
        {
            Enabled = (bool)config["enabled"]!,
            SiteUrl = (string)config["siteUrl"]!,
            CallbackUrl = (string?)config["callbackUrl"],
            Plugin = Nv(config["plugin"]),
            System = Nv(config["system"]),
            Runtime = config["runtime"] is { } runtime ? Nv(runtime) : null,
            SdkVersion = (string?)config["sdkVersion"],
            ReportUrl = ReportUrl,
            HttpClient = client,
            ClientOptions = new SupertextClientOptions { BaseUrl = ApiUrl, HttpClient = client, Sleep = (_, _) => Task.CompletedTask },
            TimeProvider = clock,
        });
    }

    private static JsonElement? Prop(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v : null;

    private static JsonElement Answer(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void AssertJson(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"expected {expected?.ToJsonString()}, got {actual?.ToJsonString()}");

    /// <summary>A fixed clock.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Fake HTTP: answers GET features and POST report, records which were called.</summary>
    private sealed class FakeCenter(JsonElement? features, JsonElement? report) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<JsonNode?> Bodies { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!.ToString();
            var kind = url.EndsWith("/features", StringComparison.Ordinal) ? "features" : "report";
            Requests.Add(kind);
            if (kind == "report")
            {
                Assert.Equal(ReportUrl, url);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                Bodies.Add(JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken)));
                UserAgents.Add(string.Join(" ", request.Headers.GetValues("User-Agent")));
            }
            else
            {
                Assert.Equal(ApiUrl + "features", url);
                Assert.Equal(HttpMethod.Get, request.Method);
            }
            var answer = kind == "features" ? features : report;
            if (answer is not { ValueKind: JsonValueKind.Object } a)
            {
                throw new InvalidOperationException($"unexpected {kind} request");
            }
            if (a.TryGetProperty("transportError", out var t) && t.GetBoolean())
            {
                throw new HttpRequestException("connection refused");
            }
            var status = a.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(a.Str("body") ?? string.Empty, Encoding.UTF8) };
        }
    }
}
