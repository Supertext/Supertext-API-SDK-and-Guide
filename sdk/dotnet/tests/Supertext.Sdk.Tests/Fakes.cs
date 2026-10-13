using System.Net;
using System.Text;
using System.Text.Json;
using Supertext.Sdk.V1;

namespace Supertext.Sdk.Tests;

/// <summary>Loads the shared test vectors from the repository's spec/ folder.</summary>
internal static class Spec
{
    private static readonly Lazy<string> Dir = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "spec");
            if (File.Exists(Path.Combine(candidate, "languages.json")))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("spec/ not found above " + AppContext.BaseDirectory);
    });

    public static JsonElement Load(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir.Value, name)));
        return doc.RootElement.Clone();
    }

    /// <summary>Cases as xUnit rows: (name, case as JSON text), so test explorers show them separately.</summary>
    public static IEnumerable<object[]> Cases(string file, string property, string? nameProperty = null)
    {
        var i = 0;
        foreach (var item in Load(file).GetProperty(property).EnumerateArray())
        {
            var name = nameProperty is not null && item.TryGetProperty(nameProperty, out var n) ? n.GetString()! : $"{property}[{i}]";
            i++;
            yield return [name, item.GetRawText()];
        }
    }

    public static string? Str(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static Dictionary<string, string>? Mapping(this JsonElement e) =>
        e.TryGetProperty("mapping", out var o) ? o.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!) : null;
}

/// <summary>A request as the fake transport saw it.</summary>
internal sealed record Recorded(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? ContentType, byte[] Body)
{
    public string Path => Uri.AbsolutePath;

    /// <summary>The multipart parts in order: (part headers, content).</summary>
    public List<(Dictionary<string, string> Headers, string Content)> Parts()
    {
        var boundary = ContentType!.Split("boundary=")[1].Trim('"');
        var text = Encoding.UTF8.GetString(Body);
        var parts = new List<(Dictionary<string, string>, string)>();
        foreach (var chunk in text.Split("--" + boundary).Skip(1))
        {
            if (chunk.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }
            var split = chunk.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var headers = chunk[..split].Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2))
                .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var content = chunk[(split + 4)..];
            parts.Add((headers, content.EndsWith("\r\n", StringComparison.Ordinal) ? content[..^2] : content));
        }
        return parts;
    }
}

/// <summary>
/// A scriptable in-memory Supertext API. Every request is recorded; answers come from
/// <see cref="Respond"/> if set, otherwise from a happy-path stand-in.
/// </summary>
internal sealed class FakeApi : HttpMessageHandler
{
    public List<Recorded> Requests { get; } = [];

    /// <summary>Overrides the answer for a request; return null to fall back to the default behaviour.</summary>
    public Func<Recorded, HttpResponseMessage?>? Respond { get; set; }

    /// <summary>Statuses the status endpoint answers, one per poll; "done" when empty.</summary>
    public Queue<string> Statuses { get; } = new();

    /// <summary>Turns the uploaded document into the "translation".</summary>
    public Func<string, string> Translate { get; set; } = html => html.Replace("data-st-id=\"0\">", "data-st-id=\"0\">[T] ");

    public string? LastUpload { get; private set; }

    public IEnumerable<Recorded> Of(HttpMethod method) => Requests.Where(r => r.Method == method);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, // User-Agent products go on the wire separated by spaces, other values by commas.
            h => string.Join(h.Key == "User-Agent" ? " " : ",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var recorded = new Recorded(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body);
        Requests.Add(recorded);

        var custom = Respond?.Invoke(recorded);
        if (custom is not null)
        {
            return custom;
        }
        var path = recorded.Path;
        if (request.Method == HttpMethod.Post)
        {
            LastUpload = recorded.Parts().Single(p => p.Headers["Content-Disposition"].Contains("name=\"file\"")).Content;
            return Json(200, """{"file_id":"f/1"}""");
        }
        if (path.EndsWith("/status", StringComparison.Ordinal))
        {
            return Json(200, $$"""{"status":"{{(Statuses.Count > 0 ? Statuses.Dequeue() : "done")}}"}""");
        }
        if (path.EndsWith("/translation", StringComparison.Ordinal))
        {
            return Text(200, Translate(LastUpload!));
        }
        return Json(200, "{}");
    }

    public static HttpResponseMessage Json(int status, string body, string? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }
        return response;
    }

    public static HttpResponseMessage Text(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
}

/// <summary>A clock that only moves when the fake sleep says so.</summary>
internal sealed class FakeTime : TimeProvider
{
    private long ticks;

    public List<TimeSpan> Sleeps { get; } = [];

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => ticks;

    public Task Sleep(TimeSpan duration, CancellationToken cancellationToken)
    {
        Sleeps.Add(duration);
        ticks += duration.Ticks;
        return Task.CompletedTask;
    }
}

/// <summary>Random that always returns the lowest or highest value, to check the jitter bounds.</summary>
internal sealed class FixedRandom(bool highest) : Random
{
    public override int Next(int minValue, int maxValue) => highest ? maxValue - 1 : minValue;
}
