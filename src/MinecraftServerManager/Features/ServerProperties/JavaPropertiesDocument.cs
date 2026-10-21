using System.Globalization;
using System.Text;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

/// <summary>
/// Reads and writes Java <c>.properties</c> files (server.properties, eula.txt) following the
/// java.util.Properties escaping rules, while preserving comments and key order.
/// </summary>
public sealed class JavaPropertiesDocument
{
    private sealed class Entry(string key, string value)
    {
        public string Key { get; } = key;
        public string Value { get; set; } = value;
    }

    // Each line is either a raw string (comment or blank line) or an Entry.
    private readonly List<object> _lines = [];
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, string>> Entries =>
        _lines.OfType<Entry>().Select(e => KeyValuePair.Create(e.Key, e.Value));

    public int Count => _entries.Count;

    public string? Get(string key) => _entries.TryGetValue(key, out var entry) ? entry.Value : null;

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public void Set(string key, string value)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            return;
        }
        var entry = new Entry(key, value);
        _entries[key] = entry;
        _lines.Add(entry);
    }

    public bool Remove(string key)
    {
        if (!_entries.Remove(key, out var entry)) return false;
        _lines.Remove(entry);
        return true;
    }

    public void AddComment(string text) => _lines.Add("#" + text);

    public static JavaPropertiesDocument Load(string path)
    {
        if (!File.Exists(path)) return new JavaPropertiesDocument();
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Older servers wrote the file as ISO-8859-1.
            text = Encoding.Latin1.GetString(bytes);
        }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return Parse(text);
    }

    public void Save(string path) => FileUtil.WriteAllTextAtomic(path, ToText());

    public static JavaPropertiesDocument Parse(string text)
    {
        var doc = new JavaPropertiesDocument();
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var count = lines.Length;
        if (count > 0 && lines[^1].Length == 0) count--;

        for (var i = 0; i < count; i++)
        {
            var trimmed = lines[i].TrimStart(' ', '\t', '\f');
            if (trimmed.Length == 0 || trimmed[0] is '#' or '!')
            {
                doc._lines.Add(lines[i]);
                continue;
            }

            var logical = new StringBuilder(trimmed);
            while (EndsWithContinuation(logical) && i + 1 < count)
            {
                logical.Length--;
                logical.Append(lines[++i].TrimStart(' ', '\t', '\f'));
            }
            if (EndsWithContinuation(logical)) logical.Length--;

            var (key, value) = SplitEntry(logical.ToString());
            doc.Set(key, value);
        }
        return doc;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            if (line is Entry entry)
                sb.Append(Escape(entry.Key, isKey: true)).Append('=').Append(Escape(entry.Value, isKey: false));
            else
                sb.Append((string)line);
            sb.Append(Environment.NewLine);
        }
        return sb.ToString();
    }

    private static bool EndsWithContinuation(StringBuilder sb)
    {
        var slashes = 0;
        for (var i = sb.Length - 1; i >= 0 && sb[i] == '\\'; i--) slashes++;
        return slashes % 2 == 1;
    }

    private static (string Key, string Value) SplitEntry(string line)
    {
        var keyEnd = line.Length;
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c is '=' or ':' or ' ' or '\t' or '\f')
            {
                keyEnd = i;
                break;
            }
        }

        var valueStart = keyEnd;
        while (valueStart < line.Length && line[valueStart] is ' ' or '\t' or '\f') valueStart++;
        if (valueStart < line.Length && line[valueStart] is '=' or ':')
        {
            valueStart++;
            while (valueStart < line.Length && line[valueStart] is ' ' or '\t' or '\f') valueStart++;
        }

        return (Unescape(line[..keyEnd]), Unescape(line[valueStart..]));
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }
            c = s[++i];
            switch (c)
            {
                case 't': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 'f': sb.Append('\f'); break;
                case 'u' when i + 4 < s.Length
                    && ushort.TryParse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string Escape(string s, bool isKey)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case ' ' when i == 0 || isKey: sb.Append("\\ "); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\f': sb.Append("\\f"); break;
                case '\\' or '=' or ':' or '#' or '!': sb.Append('\\').Append(c); break;
                case < ' ' or > '~': sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
