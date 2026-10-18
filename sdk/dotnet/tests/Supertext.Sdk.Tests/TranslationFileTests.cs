using System.Text;
using System.Text.Json;
using Supertext.Sdk.Files;

namespace Supertext.Sdk.Tests;

/// <summary>The shared test vectors in spec/translation-file.json.</summary>
public class TranslationFileTests
{
    public static IEnumerable<object[]> ChecksumCases() => Spec.Cases("translation-file.json", "checksum");
    public static IEnumerable<object[]> ExportXliffCases() => Spec.Cases("translation-file.json", "exportXliff", "name");
    public static IEnumerable<object[]> ExportHtmlCases() => Spec.Cases("translation-file.json", "exportHtml", "name");
    public static IEnumerable<object[]> ReadCases() => Spec.Cases("translation-file.json", "read", "name");
    public static IEnumerable<object[]> ImportCases() => Spec.Cases("translation-file.json", "import", "name");

    private static FileSegment Segment(JsonElement s) =>
        new(s.Str("key") ?? string.Empty, s.Str("text")!, s.TryGetProperty("html", out var h) && h.GetBoolean());

    private static List<FileSegment> Segments(JsonElement array) => array.EnumerateArray().Select(Segment).ToList();

    private static FileJob Job(JsonElement j) =>
        new(j.Str("plugin")!, j.Str("reference")!, j.Str("sourceLanguage")!, j.Str("targetLanguage")!, Segments(j.GetProperty("segments")));

    [Theory]
    [MemberData(nameof(ChecksumCases))]
    public void Checksum(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        Assert.Equal(c.Str("out"), TranslationFiles.Checksum(Segment(c.GetProperty("segment"))));
    }

    [Theory]
    [MemberData(nameof(ExportXliffCases))]
    public void ExportXliff(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var jobs = c.GetProperty("jobs").EnumerateArray().Select(Job).ToList();
        if (c.Str("error") is { } error)
        {
            Assert.Equal(error, Assert.Throws<TranslationFileException>(() => TranslationFiles.ExportXliff(jobs)).Code);
        }
        else
        {
            Assert.Equal(c.Str("out"), TranslationFiles.ExportXliff(jobs));
        }
    }

    [Theory]
    [MemberData(nameof(ExportHtmlCases))]
    public void ExportHtml(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var job = Job(c.GetProperty("job"));
        if (c.Str("error") is { } error)
        {
            Assert.Equal(error, Assert.Throws<TranslationFileException>(() => TranslationFiles.ExportHtml(job)).Code);
        }
        else
        {
            Assert.Equal(c.Str("out"), TranslationFiles.ExportHtml(job));
        }
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    public void Read(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        string content;
        if (c.TryGetProperty("repeat", out var repeat))
        {
            var text = repeat.Str("text")!;
            var times = repeat.GetProperty("times").GetInt32();
            content = new StringBuilder(text.Length * times).Insert(0, text, times).ToString();
        }
        else
        {
            content = c.Str("content")!;
        }
        if (c.Str("error") is { } error)
        {
            Assert.Equal(error, Assert.Throws<TranslationFileException>(() => TranslationFiles.Read(content)).Code);
            return;
        }
        var expected = c.GetProperty("files").EnumerateArray().Select(f => new
        {
            Format = f.Str("format"),
            Plugin = f.Str("plugin"),
            Reference = f.Str("reference"),
            SourceLanguage = f.Str("sourceLanguage"),
            TargetLanguage = f.Str("targetLanguage"),
            Units = string.Join("|", f.GetProperty("units").EnumerateArray().Select(u => $"{u.Str("key")}={u.Str("checksum") ?? "null"}")),
        }).ToList();
        var actual = TranslationFiles.Read(content).Select(f => new
        {
            Format = (string?)f.Format,
            Plugin = (string?)f.Plugin,
            Reference = (string?)f.Reference,
            SourceLanguage = (string?)f.SourceLanguage,
            TargetLanguage = (string?)f.TargetLanguage,
            Units = string.Join("|", f.Units.Select(u => $"{u.Key}={u.Checksum ?? "null"}")),
        }).ToList();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(ImportCases))]
    public void Import(string name, string json)
    {
        _ = name;
        var c = JsonDocument.Parse(json).RootElement;
        var index = c.TryGetProperty("file", out var f) ? f.GetInt32() : 0;
        var file = TranslationFiles.Read(c.Str("content")!)[index];
        var expected = c.GetProperty("out").EnumerateArray()
            .Select(o => new ImportedSegment(o.Str("key")!, o.Str("status")!, o.Str("text"))).ToList();
        Assert.Equal(expected, TranslationFiles.ImportTranslations(file, Segments(c.GetProperty("current"))));
    }

    [Fact]
    public void AFileNotReturnedByReadIsRefused()
    {
        var file = new TranslationFile(TranslationFileFormats.Html, "P", "r", "de", "fr", Array.Empty<FileUnit>());
        Assert.Equal(TranslationFileErrorCodes.InvalidFile,
            Assert.Throws<TranslationFileException>(() => TranslationFiles.ImportTranslations(file, Array.Empty<FileSegment>())).Code);
    }

    [Fact]
    public void ACopyOfAReadFileIsRefused()
    {
        var job = new FileJob("P", "r", "de", "fr", [new FileSegment("a", "Hallo")]);
        var file = TranslationFiles.Read(TranslationFiles.ExportHtml(job))[0];
        Assert.Single(TranslationFiles.ImportTranslations(file, job.Segments));
        Assert.Throws<TranslationFileException>(() => TranslationFiles.ImportTranslations(file with { }, job.Segments));
    }

    [Fact]
    public void ADoctypeWithEntitiesIsNeverExpanded()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;\">]>"
            + "<xliff><file original=\"r\" source-language=\"de\" target-language=\"fr\" product-name=\"P\"><body/></file></xliff>";
        Assert.Equal(TranslationFileErrorCodes.InvalidFile, Assert.Throws<TranslationFileException>(() => TranslationFiles.Read(xml)).Code);
    }

    [Fact]
    public void ALargeHtmlPositionDoesNotAllocateOneEntryPerPosition()
    {
        var html = "<!DOCTYPE html>\n<html><head>"
            + "<meta name=\"supertext:plugin\" content=\"P\"><meta name=\"supertext:reference\" content=\"r\">"
            + "<meta name=\"supertext:source-language\" content=\"de\"><meta name=\"supertext:target-language\" content=\"fr\">"
            + "<meta name=\"supertext:segment\" content=\"999999999 00000000 a\"></head><body></body></html>";
        var file = TranslationFiles.Read(html)[0];
        var result = TranslationFiles.ImportTranslations(file, [new FileSegment("a", "Hallo")]);
        Assert.Equal(ImportStatuses.Untranslated, result[0].Status);
    }
}
