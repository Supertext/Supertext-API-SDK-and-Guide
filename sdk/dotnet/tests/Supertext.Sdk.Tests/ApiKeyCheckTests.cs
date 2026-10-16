using System.Net;
using System.Text;
using System.Text.Json;
using Supertext.Sdk.V1;

namespace Supertext.Sdk.Tests;

/// <summary>spec/api-key-check.json: the API key check Supertext Plugin Health reports.</summary>
public class ApiKeyCheckTests
{
    private const string ApiUrl = "https://api.test/v1/";

    private static readonly JsonElement S = Spec.Load("api-key-check.json");

    public static IEnumerable<object[]> FingerprintCases() => Spec.Cases("api-key-check.json", "fingerprint");
    public static IEnumerable<object[]> ApiKeyCheckCases() => Spec.Cases("api-key-check.json", "apiKeyCheck");

    [Fact]
    public void HasTheSpecTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(S.GetProperty("defaults").GetProperty("keyCheckTimeoutSeconds").GetInt32()), ApiKeyCheck.Timeout);
    }

    [Fact]
    public void SdkVersionMatchesThePackage()
    {
        Assert.Equal(SdkInfo.Version, typeof(SupertextClient).Assembly.GetName().Version!.ToString(3));
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
        var http = new FakeFeatures(c.GetProperty("answer"));
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
        Assert.Equal(c.TryGetProperty("requests", out var r) ? r.GetInt32() : 1, http.Requests);
    }

    /// <summary>A fixed clock.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Fake HTTP: answers GET features, counts the requests.</summary>
    private sealed class FakeFeatures(JsonElement answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            Assert.Equal(ApiUrl + "features", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Get, request.Method);
            if (answer is not { ValueKind: JsonValueKind.Object } a)
            {
                throw new InvalidOperationException("unexpected request");
            }
            if (a.TryGetProperty("transportError", out var t) && t.GetBoolean())
            {
                throw new HttpRequestException("connection refused");
            }
            var status = a.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(a.Str("body") ?? string.Empty, Encoding.UTF8) });
        }
    }
}
