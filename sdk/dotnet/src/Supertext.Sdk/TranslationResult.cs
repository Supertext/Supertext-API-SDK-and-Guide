using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Supertext.Sdk;

/// <summary>
/// Key → translated text, enumerated in the order the segments were given (a plain
/// <c>Dictionary</c> doesn't guarantee that). Starts with the source texts, so blank ones come back unchanged.
/// </summary>
internal sealed class TranslationResult : IReadOnlyDictionary<string, string>
{
    private readonly List<string> keys = [];
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public TranslationResult(IEnumerable<KeyValuePair<string, Segment>> segments)
    {
        foreach (var (key, segment) in segments)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!values.TryAdd(key, segment.Text))
            {
                throw new ArgumentException($"The key \"{key}\" occurs more than once.", nameof(segments));
            }
            keys.Add(key);
        }
    }

    public void Set(string key, string value) => values[key] = value;

    public string this[string key] => values[key];

    public IEnumerable<string> Keys => keys;

    public IEnumerable<string> Values => keys.Select(key => values[key]);

    public int Count => keys.Count;

    public bool ContainsKey(string key) => values.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
        keys.Select(key => new KeyValuePair<string, string>(key, values[key])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
