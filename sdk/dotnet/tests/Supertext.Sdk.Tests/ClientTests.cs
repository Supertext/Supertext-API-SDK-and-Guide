using System.Net;
using Supertext.Sdk.V1;

namespace Supertext.Sdk.Tests;

/// <summary>The client against a fake transport: wire format, retries, polling, cleanup, errors.</summary>
public class ClientTests
{
    private const string Key = "secret-key-123";

    private readonly FakeApi api = new();
    private readonly FakeTime time = new();

    private SupertextClient Client(string? key = Key, string? userAgent = null, TimeSpan? timeout = null, TimeSpan? requestTimeout = null, string baseUrl = "https://api.example.test/v1") =>
        new(key, new SupertextClientOptions
        {
            BaseUrl = baseUrl,
            HttpClient = new HttpClient(api),
            Sleep = time.Sleep,
            TimeProvider = time,
            UserAgent = userAgent,
            Timeout = timeout ?? TimeSpan.FromSeconds(180),
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30),
        });

    [Fact]
    public async Task UploadSendsTheExactMultipartFieldsInOrder()
    {
        var fileId = await Client().SubmitAsync("<p>Grüezi</p>", "de-CH", "en", Politeness.More);

        Assert.Equal("f/1", fileId);
        var upload = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal("https://api.example.test/v1/translate/ai/file", upload.Uri.ToString());
        Assert.StartsWith("multipart/form-data", upload.ContentType);
        var parts = upload.Parts();
        Assert.Equal(["form-data; name=\"target_lang\"", "form-data; name=\"source_lang\"", "form-data; name=\"politeness\"", "form-data; name=\"file\"; filename=\"content.html\""],
            parts.Select(p => p.Headers["Content-Disposition"]));
        Assert.Equal(["de-CH", "en", "more", "<p>Grüezi</p>"], parts.Select(p => p.Content));
        // Exactly text/html: a charset parameter gets HTTP 415 from the live API.
        Assert.Equal("text/html", parts[3].Headers["Content-Type"]);
        Assert.All(parts.Take(3), p => Assert.False(p.Headers.ContainsKey("Content-Type")));
    }

    [Fact]
    public async Task UploadLeavesOutEmptySourceAndDefaultPoliteness()
    {
        await Client().SubmitAsync("<p>x</p>", "fr-CH");
        await Client().SubmitAsync("<p>x</p>", "pt-BR", "  ");

        Assert.All(api.Requests, r => Assert.Equal(["target_lang", "file"], r.Parts().Select(p => p.Headers["Content-Disposition"].Split('"')[1])));
        Assert.Equal(["fr-CH", "pt-BR"], api.Requests.Select(r => r.Parts()[0].Content));
    }

    [Fact]
    public async Task UploadSendsTheLanguageCodesAsGivenOnlyTrimmed()
    {
        await Client().SubmitAsync("<p>x</p>", " de ", " en-US ");

        Assert.Equal(["de", "en-US"], Assert.Single(api.Requests).Parts().Take(2).Select(p => p.Content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EmptyTargetIsRefusedWithoutARequest(string target)
    {
        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().TranslateHtmlAsync("<p>x</p>", target));

        Assert.Equal(SupertextErrorCodes.UnsupportedLanguage, e.Code);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task GetFeaturesListsTheLanguagePairsAndSkipsMalformedEntries()
    {
        api.Respond = _ => FakeApi.Json(200, """
            {"supported_features":[{"source_lang":"en","target_lang":"de-CH","features":["supports_politeness",3]},
            {"source_lang":"en"},"x",{"source_lang":"de","target_lang":"en-US"}]}
            """);

        var features = await Client().GetFeaturesAsync();

        Assert.Equal(2, features.Count);
        Assert.Equal(("en", "de-CH"), (features[0].SourceLang, features[0].TargetLang));
        Assert.Equal(["supports_politeness"], features[0].Features);
        Assert.Equal(("de", "en-US"), (features[1].SourceLang, features[1].TargetLang));
        Assert.Empty(features[1].Features);
        Assert.Equal("https://api.example.test/v1/features", Assert.Single(api.Requests).Uri.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"supported_features":{}}""")]
    [InlineData("not json")]
    public async Task GetFeaturesWithoutAListIsEmpty(string body)
    {
        api.Respond = _ => FakeApi.Json(200, body);

        Assert.Empty(await Client().GetFeaturesAsync());
    }

    [Fact]
    public async Task LessPolitenessIsSent()
    {
        await Client().SubmitAsync("<p>x</p>", "de-DE", "", Politeness.Less);

        var parts = Assert.Single(api.Requests).Parts();
        Assert.Equal(["target_lang", "politeness", "file"], parts.Select(p => p.Headers["Content-Disposition"].Split('"')[1]));
        Assert.Equal("less", parts[1].Content);
    }

    [Fact]
    public async Task EveryRequestCarriesAuthorizationAcceptAndUserAgent()
    {
        await Client("  Supertext-Auth-Key " + Key + " ").ValidateApiKeyAsync();
        await Client(userAgent: "Typo3-Supertext-Translation/1.2.0").ValidateApiKeyAsync();

        Assert.All(api.Requests, r =>
        {
            Assert.Equal("https://api.example.test/v1/features", r.Uri.ToString());
            Assert.Equal("Supertext-Auth-Key " + Key, r.Headers["Authorization"]);
            Assert.Equal("application/json", r.Headers["Accept"]);
        });
        Assert.Equal("supertext-sdk-dotnet", api.Requests[0].Headers["User-Agent"]);
        Assert.Equal("supertext-sdk-dotnet Typo3-Supertext-Translation/1.2.0", api.Requests[1].Headers["User-Agent"]);
    }

    [Fact]
    public async Task BlankKeyThrowsWithoutSendingAnything()
    {
        var client = Client(key: "  ");

        var e = await Assert.ThrowsAsync<SupertextException>(() => client.TranslateHtmlAsync("<p>x</p>", "de-CH"));

        Assert.Equal(SupertextErrorCodes.MissingApiKey, e.Code);
        Assert.Contains("https://www.supertext.com/en/integrations/api", e.Message);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task RateLimitIsRetriedWithRetryAfter()
    {
        var uploads = 0;
        api.Respond = r => r.Method == HttpMethod.Post && uploads++ < 2 ? FakeApi.Json(429, """{"error_code":"RATE_LIMIT_EXCEEDED"}""", retryAfter: uploads == 1 ? "3" : "1.5") : null;

        Assert.Equal("f/1", await Client().SubmitAsync("<p>x</p>", "de-CH"));

        Assert.Equal(3, api.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1.5)], time.Sleeps);
        // The body is sent again on every attempt.
        Assert.All(api.Requests, r => Assert.Contains("<p>x</p>", r.Parts()[^1].Content));
    }

    [Fact]
    public async Task RateLimitGivesUpAfterMaxRetries()
    {
        api.Respond = _ => FakeApi.Json(429, """{"error_code":"RATE_LIMIT_EXCEEDED"}""");

        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().ValidateApiKeyAsync());

        Assert.Equal(SupertextErrorCodes.TooManyRequests, e.Code);
        Assert.Equal(429, e.Status);
        Assert.Equal("RATE_LIMIT_EXCEEDED", e.ApiErrorCode);
        Assert.Equal(5, api.Requests.Count);
        Assert.Equal(4, time.Sleeps.Count);
        // No Retry-After: 1, 2, 4, 8 s plus up to 249 ms jitter.
        for (var i = 0; i < 4; i++)
        {
            Assert.InRange(time.Sleeps[i].TotalMilliseconds, 1000 << i, (1000 << i) + 249);
        }
    }

    [Fact]
    public async Task PollsUntilDoneWithTheInterval()
    {
        api.Statuses.Enqueue("translating");
        api.Statuses.Enqueue("queued");
        api.Statuses.Enqueue("done");

        await Client().WaitUntilDoneAsync("f1");

        Assert.Equal(3, api.Requests.Count);
        Assert.Equal("/v1/translate/ai/file/f1/status", api.Requests[0].Path);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], time.Sleeps);
    }

    [Fact]
    public async Task PollingTimesOutWhenTheNextPollWouldStartTooLate()
    {
        for (var i = 0; i < 10; i++)
        {
            api.Statuses.Enqueue("translating");
        }

        // Polls at 0, 2 and 4 s; a poll at 6 s would be after the 4 s timeout.
        var e = await Assert.ThrowsAsync<SupertextException>(() => Client(timeout: TimeSpan.FromSeconds(4)).WaitUntilDoneAsync("f1"));

        Assert.Equal(SupertextErrorCodes.Timeout, e.Code);
        Assert.Equal(3, api.Requests.Count);
        Assert.Equal(2, time.Sleeps.Count);
    }

    [Fact]
    public async Task FileIdIsUrlEncoded()
    {
        await Client().DeleteAsync("a b/c");

        Assert.Equal("/v1/translate/ai/file/a%20b%2Fc", Assert.Single(api.Requests).Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Delete, api.Requests[0].Method);
    }

    [Fact]
    public async Task TranslateHtmlRunsTheWholeProtocol()
    {
        api.Statuses.Enqueue("translating");

        var html = await Client().TranslateHtmlAsync("<div data-st-id=\"0\">Hello</div>", "de-CH");

        Assert.Equal("<div data-st-id=\"0\">[T] Hello</div>", html);
        Assert.Equal(["POST /v1/translate/ai/file", "GET /v1/translate/ai/file/f%2F1/status", "GET /v1/translate/ai/file/f%2F1/status",
            "GET /v1/translate/ai/file/f%2F1/translation", "DELETE /v1/translate/ai/file/f%2F1"],
            api.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}"));
    }

    [Theory]
    [InlineData("error", SupertextErrorCodes.TranslationError)]
    [InlineData("limit_exceeded", SupertextErrorCodes.QuotaExceeded)]
    [InlineData("deleted", SupertextErrorCodes.FileDeleted)]
    public async Task FileIsDeletedAfterAFailedTranslation(string status, string code)
    {
        api.Statuses.Enqueue(status);

        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().TranslateHtmlAsync("<p>x</p>", "de-CH"));

        Assert.Equal(code, e.Code);
        Assert.Single(api.Of(HttpMethod.Delete));
    }

    [Fact]
    public async Task FileIsDeletedAfterATransportErrorAndDeleteNeverThrows()
    {
        api.Respond = r => r.Method == HttpMethod.Get ? throw new HttpRequestException("Connection refused")
            : r.Method == HttpMethod.Delete ? FakeApi.Json(500, "{}") : null;

        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().TranslateHtmlAsync("<p>x</p>", "de-CH"));

        Assert.Equal(SupertextErrorCodes.TransportError, e.Code);
        Assert.Equal("Connection refused", e.Detail);
        Assert.Null(e.Status);
        Assert.Single(api.Of(HttpMethod.Delete));
    }

    [Fact]
    public async Task DeleteSwallowsEveryFailure()
    {
        api.Respond = _ => throw new HttpRequestException("boom");
        await Client().DeleteAsync("f1");

        api.Respond = _ => FakeApi.Json(404, "{}");
        await Client().DeleteAsync("f1");

        await Client(key: "").DeleteAsync("f1");
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task EmptyDownloadIsIncomplete()
    {
        api.Translate = _ => " \n ";

        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().TranslateHtmlAsync("<p>x</p>", "de-CH"));

        Assert.Equal(SupertextErrorCodes.IncompleteResponse, e.Code);
        Assert.Single(api.Of(HttpMethod.Delete));
    }

    [Fact]
    public async Task UploadWithoutFileId()
    {
        api.Respond = r => r.Method == HttpMethod.Post ? FakeApi.Json(200, """{"file_id":""}""") : null;

        var e = await Assert.ThrowsAsync<SupertextException>(() => Client().TranslateHtmlAsync("<p>x</p>", "de-CH"));

        Assert.Equal(SupertextErrorCodes.NoFileId, e.Code);
        Assert.Empty(api.Of(HttpMethod.Delete));
    }

    [Fact]
    public async Task RequestTimeoutIsATransportError()
    {
        var slow = new HttpClient(new SlowHandler());
        var client = new SupertextClient(Key, new() { HttpClient = slow, RequestTimeout = TimeSpan.FromMilliseconds(50) });

        var e = await Assert.ThrowsAsync<SupertextException>(() => client.ValidateApiKeyAsync());

        Assert.Equal(SupertextErrorCodes.TransportError, e.Code);
        Assert.Contains("timed out", e.Detail);
    }

    [Fact]
    public async Task CallerCancellationIsNotATransportError()
    {
        var client = new SupertextClient(Key, new() { HttpClient = new HttpClient(new SlowHandler()) });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ValidateApiKeyAsync(cts.Token));
    }

    [Fact]
    public async Task TheKeyNeverAppearsInErrors()
    {
        api.Respond = _ => FakeApi.Text(401, $"<p>Unknown key {Key}</p>");
        var auth = await Assert.ThrowsAsync<SupertextException>(() => Client("Supertext-Auth-Key " + Key).ValidateApiKeyAsync());

        api.Respond = _ => throw new HttpRequestException($"Failed for {Key}");
        var transport = await Assert.ThrowsAsync<SupertextException>(() => Client().ValidateApiKeyAsync());

        foreach (var e in new[] { auth, transport })
        {
            Assert.DoesNotContain(Key, e.Message);
            Assert.DoesNotContain(Key, e.Detail ?? "");
            Assert.DoesNotContain(Key, e.ToString());
        }
        Assert.Equal(SupertextErrorCodes.AuthenticationFailure, auth.Code);
        Assert.Contains("https://www.supertext.com/person/en/account/signin", auth.Message);
        Assert.DoesNotContain(Key, Client().ToString());
    }

    [Fact]
    public async Task TranslateTextsPacksSendsAndUnpacksInInputOrder()
    {
        api.Translate = html => html.Replace("Hello &amp; welcome", "Hallo &amp; willkommen").Replace("<b>new</b>", "<b>neu</b>")
            .Replace("Line 1<br>Line 2", "Zeile 1<br>Zeile 2");

        var result = await Client().TranslateTextsAsync(new Dictionary<string, Segment>
        {
            ["title"] = "Hello & welcome",
            ["blank"] = "  ",
            ["body"] = new Segment("<p>A <b>new</b> shop</p>", Html: true),
            ["lines"] = "Line 1\nLine 2",
        }, "de-CH", "en", Politeness.More);

        Assert.Equal(["title", "blank", "body", "lines"], result.Keys);
        Assert.Equal(["Hallo & willkommen", "  ", "<p>A <b>neu</b> shop</p>", "Zeile 1\nZeile 2"], result.Values);
        Assert.Equal(
            "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"></head><body>\n<div data-st-id=\"0\">Hello &amp; welcome</div>\n"
            + "<div data-st-id=\"1\"><p>A <b>new</b> shop</p></div>\n<div data-st-id=\"2\">Line 1<br>Line 2</div>\n</body></html>\n",
            api.LastUpload);
        Assert.Equal(["de-CH", "en", "more"], api.Requests[0].Parts().Take(3).Select(p => p.Content));
    }

    [Fact]
    public async Task TranslateTextsSendsDocumentsOneAfterTheOther()
    {
        var result = await Client().TranslateTextsAsync(
            [new("a", "12345"), new("b", "12345"), new("c", "1")], "de-CH", maxCharacters: 10);

        Assert.Equal(2, api.Of(HttpMethod.Post).Count());
        Assert.Equal(2, api.Of(HttpMethod.Delete).Count());
        Assert.Equal(["[T] 12345", "12345", "[T] 1"], result.Values);
    }

    [Fact]
    public async Task MissingSegmentsAreIncompleteAfterTheFileIsDeleted()
    {
        api.Translate = _ => "<div data-st-id=\"1\">zwei</div>";

        var e = await Assert.ThrowsAsync<SupertextException>(() =>
            Client().TranslateTextsAsync([new("a", "eins"), new("b", "zwei"), new("c", "drei")], "de-CH"));

        Assert.Equal(SupertextErrorCodes.IncompleteResponse, e.Code);
        Assert.Equal("missing: a, c", e.Detail);
        Assert.Single(api.Of(HttpMethod.Delete));
    }

    [Fact]
    public async Task NothingToTranslateSendsNothing()
    {
        var result = await Client(key: "").TranslateTextsAsync([new("a", ""), new("b", new Segment(" \t\r\n", true))], "de-CH");

        Assert.Empty(api.Requests);
        Assert.Equal(["", " \t\r\n"], result.Values);
    }

    [Fact]
    public async Task DuplicateKeysAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Client().TranslateTextsAsync([new("a", "x"), new("a", "y")], "de-CH"));
        Assert.Empty(api.Requests);
    }

    /// <summary>Never answers until cancelled.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
