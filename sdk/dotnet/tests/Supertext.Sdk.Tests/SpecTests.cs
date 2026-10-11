using System.Text.Json;

namespace Supertext.Sdk.Tests;

/// <summary>The shared test vectors in spec/*.json, which every Supertext SDK passes.</summary>
public class SpecTests
{
    public static IEnumerable<object[]> AuthCases() => Spec.Cases("auth-header.json", "cases");
    public static IEnumerable<object[]> RetryCases() => Spec.Cases("retry-delay.json", "cases");
    public static IEnumerable<object[]> NormalizeCases() => Spec.Cases("language-codes.json", "normalize");
    public static IEnumerable<object[]> SourceCases() => Spec.Cases("language-codes.json", "source");
    public static IEnumerable<object[]> TargetCases() => Spec.Cases("language-codes.json", "target");
    public static IEnumerable<object[]> IsValidCases() => Spec.Cases("language-codes.json", "isValid");
    public static IEnumerable<object[]> BuildCases() => Spec.Cases("html-document.json", "build", "name");
    public static IEnumerable<object[]> ParseCases() => Spec.Cases("html-document.json", "parse", "name");
    public static IEnumerable<object[]> BatchingCases() => Spec.Cases("batching.json", "cases", "name");
    public static IEnumerable<object[]> HttpErrorCases() => Spec.Cases("errors.json", "http");
    public static IEnumerable<object[]> FileStatusCases() => Spec.Cases("errors.json", "fileStatus");

    [Theory]
    [MemberData(nameof(AuthCases))]
    public void AuthHeader(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        if (c.Str("error") is { } error)
        {
            Assert.Equal(error, Assert.Throws<SupertextException>(() => SupertextClient.AuthHeader(c.Str("in"))).Code);
        }
        else
        {
            Assert.Equal(c.Str("out"), SupertextClient.AuthHeader(c.Str("in")));
        }
    }

    [Theory]
    [MemberData(nameof(RetryCases))]
    public void RetryDelay(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var attempt = c.GetProperty("attempt").GetInt32();
        var retryAfter = c.Str("retryAfter");
        var min = c.GetProperty("min").GetInt32();
        var max = c.GetProperty("max").GetInt32();
        Assert.Equal(min, SupertextClient.RetryDelayMs(attempt, retryAfter, new FixedRandom(highest: false)));
        Assert.Equal(max, SupertextClient.RetryDelayMs(attempt, retryAfter, new FixedRandom(highest: true)));
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(SupertextClient.RetryDelayMs(attempt, retryAfter), min, max);
        }
    }

    [Fact]
    public void DefaultRegionsMatchTheSpec()
    {
        var expected = Spec.Load("language-codes.json").GetProperty("defaultRegions").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        Assert.Equal(expected.OrderBy(kv => kv.Key), LanguageCodes.DefaultRegions.OrderBy(kv => kv.Key));
    }

    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void Normalize(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), LanguageCodes.Normalize(c.Str("in")));
    }

    [Theory]
    [MemberData(nameof(SourceCases))]
    public void Source(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), LanguageCodes.Source(c.Str("in"), c.Overrides()));
    }

    [Theory]
    [MemberData(nameof(TargetCases))]
    public void Target(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), LanguageCodes.Target(c.Str("in"), c.Overrides()));
    }

    [Theory]
    [MemberData(nameof(IsValidCases))]
    public void IsValid(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.GetProperty("out").GetBoolean(), LanguageCodes.IsValid(c.Str("in")));
    }

    private static List<Segment> Segments(JsonElement c) =>
        c.GetProperty("segments").EnumerateArray()
            .Select(s => new Segment(s.Str("text") ?? string.Empty, s.TryGetProperty("html", out var h) && h.GetBoolean()))
            .ToList();

    [Theory]
    [MemberData(nameof(BuildCases))]
    public void Build(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("html"), HtmlDocument.Build(Segments(c)));
    }

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void Parse(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var expected = c.GetProperty("out").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.GetString()).ToList();
        Assert.Equal(expected, HtmlDocument.Parse(c.Str("html")!, Segments(c)));
    }

    [Theory]
    [MemberData(nameof(BatchingCases))]
    public void Batching(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var segments = c.GetProperty("segments").EnumerateArray()
            .Select(s => new KeyValuePair<string, Segment>(s.Str("key")!, new Segment(s.Str("text")!, s.GetProperty("html").GetBoolean())));
        var expected = c.GetProperty("batches").EnumerateArray().Select(b => b.EnumerateArray().Select(k => k.GetString()!).ToList()).ToList();
        var actual = SupertextClient.Batches(segments, c.GetProperty("maxCharacters").GetInt32()).Select(b => b.Select(p => p.Key).ToList()).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DefaultMaxCharactersMatchesTheSpec() =>
        Assert.Equal(SupertextClient.DefaultMaxCharacters, Spec.Load("batching.json").GetProperty("defaultMaxCharacters").GetInt32());

    [Theory]
    [MemberData(nameof(HttpErrorCases))]
    public async Task HttpErrors(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var status = c.GetProperty("status").GetInt32();
        var body = c.Str("body")!;
        var api = new FakeApi { Respond = _ => FakeApi.Text(status, body) };
        var time = new FakeTime();
        var client = new SupertextClient("key", new() { HttpClient = new HttpClient(api), Sleep = time.Sleep, TimeProvider = time });

        var e = await Assert.ThrowsAsync<SupertextException>(() => client.ValidateApiKeyAsync());

        Assert.Equal(c.Str("code"), e.Code);
        Assert.Equal(status, e.Status);
        Assert.Equal(c.Str("apiErrorCode"), e.ApiErrorCode);
        Assert.Equal(c.Str("sourceLang"), e.SourceLang);
        Assert.Equal(c.Str("targetLang"), e.TargetLang);
        Assert.Equal(c.Str("detail"), e.Detail);
        // 429 is retried first (4 times by default), the others are not.
        Assert.Equal(status == 429 ? 5 : 1, api.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(FileStatusCases))]
    public async Task FileStatuses(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var api = new FakeApi();
        api.Statuses.Enqueue(c.Str("status")!);
        var time = new FakeTime();
        var client = new SupertextClient("key", new() { HttpClient = new HttpClient(api), Sleep = time.Sleep, TimeProvider = time });

        if (c.Str("code") is { } code)
        {
            var e = await Assert.ThrowsAsync<SupertextException>(() => client.WaitUntilDoneAsync("f1"));
            Assert.Equal(code, e.Code);
            Assert.Null(e.Status);
        }
        else
        {
            // Not an error: done right away, or keep polling until the (default) "done".
            await client.WaitUntilDoneAsync("f1");
        }
        Assert.Equal(c.Str("status"), await new SupertextClient("key", new() { HttpClient = new HttpClient(Echo(c.Str("status")!)) }).GetStatusAsync("f1"));
    }

    private static FakeApi Echo(string status)
    {
        var api = new FakeApi();
        api.Statuses.Enqueue(status);
        return api;
    }
}
