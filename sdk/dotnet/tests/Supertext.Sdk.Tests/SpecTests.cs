using System.Text.Json;
using Supertext.Sdk.V1;

namespace Supertext.Sdk.Tests;

/// <summary>The shared test vectors in spec/*.json, which every Supertext SDK passes.</summary>
public class SpecTests
{
    public static IEnumerable<object[]> AuthCases() => Spec.Cases("auth-header.json", "cases");
    public static IEnumerable<object[]> RetryCases() => Spec.Cases("retry-delay.json", "cases");
    public static IEnumerable<object[]> MatchTargetCases() => Spec.Cases("languages.json", "matchTarget");
    public static IEnumerable<object[]> MatchSourceCases() => Spec.Cases("languages.json", "matchSource");
    public static IEnumerable<object[]> DefaultTargetCases() => Spec.Cases("languages.json", "defaultTarget");
    public static IEnumerable<object[]> SuggestTargetCases() => Spec.Cases("languages.json", "suggestTarget");
    public static IEnumerable<object[]> ResolveTargetCases() => Spec.Cases("languages.json", "resolveTarget");
    public static IEnumerable<object[]> ResolveSourceCases() => Spec.Cases("languages.json", "resolveSource");
    public static IEnumerable<object[]> PolitenessCases() => Spec.Cases("languages.json", "supportsPoliteness");
    public static IEnumerable<object[]> BuildCases() => Spec.Cases("html-document.json", "build", "name");
    public static IEnumerable<object[]> ParseCases() => Spec.Cases("html-document.json", "parse", "name");
    public static IEnumerable<object[]> BatchingCases() => Spec.Cases("batching.json", "cases", "name");
    public static IEnumerable<object[]> HttpErrorCases() => Spec.Cases("errors.json", "http");
    public static IEnumerable<object[]> FileStatusCases() => Spec.Cases("errors.json", "fileStatus");
    public static IEnumerable<object[]> CorrectionCases() => Spec.Cases("corrections.json", "scenarios", "name");

    [Theory]
    [MemberData(nameof(CorrectionCases))]
    public async Task Corrections(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var exchanges = new Queue<JsonElement>(c.GetProperty("exchanges").EnumerateArray());
        var version = c.TryGetProperty("generate", out var gen) ? gen.Str("version")! : "";
        static string AckBody(int from, int to, string version) =>
            "{\"corrections\":[" + string.Join(",", Enumerable.Range(from, to - from + 1).Select(i => $"{{\"id\":\"{i}\",\"version\":\"{version}\"}}")) + "]}";
        var api = new FakeApi
        {
            Respond = r =>
            {
                Assert.True(exchanges.Count > 0, $"unexpected {r.Method} {r.Uri}");
                var x = exchanges.Dequeue();
                var req = x.GetProperty("request");
                Assert.Equal(req.Str("method"), r.Method.Method);
                Assert.Equal("https://review.test/v1/" + req.Str("path"), r.Uri.OriginalString);
                var body = r.Body.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(r.Body);
                if (req.Str("body") is { } expected)
                {
                    Assert.Equal(expected, body);
                }
                if (req.TryGetProperty("bodyIds", out var ids))
                {
                    Assert.Equal(AckBody(ids[0].GetInt32(), ids[1].GetInt32(), version), body);
                }
                if (body is not null)
                {
                    Assert.Equal("application/json", r.ContentType);
                }
                var res = x.GetProperty("response");
                return FakeApi.Text(res.GetProperty("status").GetInt32(), res.Str("body")!);
            },
        };
        var client = new SupertextClient("key-1", new SupertextClientOptions
        {
            BaseUrl = "https://review.test/v1/",
            HttpClient = new HttpClient(api),
            Sleep = (_, _) => Task.CompletedTask,
        });
        var corrections = new List<Correction>();
        if (c.TryGetProperty("generate", out var g))
        {
            for (var i = 1; i <= g.GetProperty("count").GetInt32(); i++)
            {
                corrections.Add(new Correction(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "", null, "", Politeness.Default, "", "", "", "", "", version));
            }
        }
        else if (c.TryGetProperty("corrections", out var list))
        {
            corrections.AddRange(list.EnumerateArray().Select(i => new Correction(i.Str("id")!, "", null, "", Politeness.Default, "", "", "", "", "", i.Str("version")!)));
        }

        async Task<string> Run()
        {
            if (c.Str("call") == "pending")
            {
                var o = c.GetProperty("options");
                Politeness? politeness = o.Str("politeness") switch
                {
                    null => null,
                    "more" => Politeness.More,
                    "less" => Politeness.Less,
                    _ => Politeness.Default,
                };
                var found = await client.PendingCorrectionsAsync(o.Str("targetLang"), o.Str("sourceLang"), politeness);
                return JsonSerializer.Serialize(found.Select(x => new Dictionary<string, string?>
                {
                    ["id"] = x.Id,
                    ["segmentId"] = x.SegmentId,
                    ["sourceLang"] = x.SourceLang,
                    ["targetLang"] = x.TargetLang,
                    ["politeness"] = x.Politeness switch { Politeness.More => "more", Politeness.Less => "less", _ => "default" },
                    ["sourceHtml"] = x.SourceHtml,
                    ["previousHtml"] = x.PreviousHtml,
                    ["html"] = x.Html,
                    ["status"] = x.Status,
                    ["correctedAt"] = x.CorrectedAt,
                    ["version"] = x.Version,
                }));
            }
            return JsonSerializer.Serialize(await client.AcknowledgeCorrectionsAsync(corrections));
        }

        if (c.Str("error") is { } error)
        {
            var e = await Assert.ThrowsAsync<SupertextException>(Run);
            Assert.Equal(error, e.Code);
        }
        else
        {
            Assert.Equal(JsonSerializer.Serialize(c.GetProperty("result")), await Run());
        }
        Assert.Empty(exchanges);
    }

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
    public void SourceLanguagesMatchTheSpec()
    {
        var expected = Spec.Load("languages.json").GetProperty("sources").EnumerateArray()
            .Select(l => new SourceLanguage(l.Str("code")!, l.Str("name")!));
        Assert.Equal(expected, Languages.Sources);
    }

    [Fact]
    public void TargetLanguagesMatchTheSpec()
    {
        var expected = Spec.Load("languages.json").GetProperty("targets").EnumerateArray()
            .Select(l => new TargetLanguage(l.Str("code")!, l.Str("name")!, l.GetProperty("politeness").GetBoolean()));
        Assert.Equal(expected, Languages.Targets);
    }

    [Theory]
    [MemberData(nameof(MatchTargetCases))]
    public void MatchTarget(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), Languages.MatchTarget(c.Str("in")));
        Assert.Equal(c.Str("out") is not null, Languages.IsSupportedTarget(c.Str("in")));
    }

    [Fact]
    public void DefaultTargetsAndOptionsMatchTheSpec()
    {
        var spec = Spec.Load("languages.json");
        var defaults = spec.GetProperty("defaultTargets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        Assert.Equal(defaults, Languages.DefaultTargets);
        var order = spec.GetProperty("targetOptions").EnumerateArray().Select(e => e.GetString()!);
        Assert.Equal(order, Languages.TargetOptions().Select(l => l.Code));
        Assert.Equal(Languages.Targets.OrderBy(l => l.Code), Languages.TargetOptions().OrderBy(l => l.Code));
    }

    [Theory]
    [MemberData(nameof(DefaultTargetCases))]
    public void DefaultTarget(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), Languages.DefaultTarget(c.Str("in")));
    }

    [Theory]
    [MemberData(nameof(SuggestTargetCases))]
    public void SuggestTarget(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), Languages.SuggestTarget(c.Str("in")));
    }

    [Theory]
    [MemberData(nameof(MatchSourceCases))]
    public void MatchSource(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), Languages.MatchSource(c.Str("in")));
    }

    [Theory]
    [MemberData(nameof(ResolveTargetCases))]
    public void ResolveTarget(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        if (c.Str("error") is { } error)
        {
            var e = Assert.Throws<SupertextException>(() => Languages.Target(c.Str("in"), c.Mapping()));
            Assert.Equal(error, e.Code);
            var trimmed = c.Str("in")!.Trim();
            Assert.Equal(trimmed.Length > 0 ? trimmed : null, e.TargetLang);
            Assert.Null(e.SourceLang);
            Assert.Null(e.Status);
        }
        else
        {
            Assert.Equal(c.Str("out"), Languages.Target(c.Str("in"), c.Mapping()));
        }
    }

    [Theory]
    [MemberData(nameof(ResolveSourceCases))]
    public void ResolveSource(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        if (c.Str("error") is { } error)
        {
            var e = Assert.Throws<SupertextException>(() => Languages.Source(c.Str("in"), c.Mapping()));
            Assert.Equal(error, e.Code);
            Assert.Equal(c.Str("in")!.Trim(), e.SourceLang);
            Assert.Null(e.TargetLang);
        }
        else
        {
            Assert.Equal(c.Str("out"), Languages.Source(c.Str("in"), c.Mapping()));
        }
    }

    [Theory]
    [MemberData(nameof(PolitenessCases))]
    public void SupportsPoliteness(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var expected = c.GetProperty("out").ValueKind == JsonValueKind.Null ? (bool?)null : c.GetProperty("out").GetBoolean();
        Assert.Equal(expected, Languages.SupportsPoliteness(c.Str("in")));
    }

    [Fact]
    public void IsSupportedSourceIsAnExactMatch()
    {
        Assert.True(Languages.IsSupportedSource(" EN "));
        Assert.True(Languages.IsSupportedSource("gsw"));
        Assert.False(Languages.IsSupportedSource("en-US"));
        Assert.False(Languages.IsSupportedSource(""));
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
        Assert.Equal(c.Str("feature"), e.Feature);
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
