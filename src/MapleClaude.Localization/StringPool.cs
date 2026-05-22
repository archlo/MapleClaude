using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Localization;

/// <summary>
/// In-memory table of <c>id → string</c> mirroring the original v95 client's
/// <c>CStringPool::GetString</c>. The data ships as an embedded language pack
/// (<c>strings.&lt;lang&gt;.csv</c>) so the single-file exe needs no external
/// file; additional languages drop in alongside the English default.
///
/// Pack format (one entry per line): <c>id;"value"</c> where the value may
/// contain doubled quotes (<c>""</c> → <c>"</c>) and literal escape sequences
/// (<c>\r \n \t</c>) that are converted to real characters on load. Loading is
/// best-effort — a missing pack or a malformed line never throws.
/// </summary>
public sealed class StringPool
{
    private const string DefaultLanguage = "en";

    private readonly ILogger? _logger;
    private readonly Dictionary<int, string> _strings = new();

    /// <summary>The language code actually loaded (falls back to <c>en</c>).</summary>
    public string Language { get; }

    /// <summary>Number of entries loaded.</summary>
    public int Count => _strings.Count;

    public StringPool(ILogger? logger = null, string language = DefaultLanguage)
    {
        _logger = logger;
        Language = LoadPack(language) ? language : (LoadPack(DefaultLanguage) ? DefaultLanguage : language);
        _logger?.LogInformation("StringPool loaded {Count} entries (lang={Lang})", _strings.Count, Language);
    }

    /// <summary>The string for <paramref name="id"/>, or <c>"[id]"</c> if absent.</summary>
    public string Get(int id) => _strings.TryGetValue(id, out var s) ? s : $"[{id}]";

    /// <summary>The string for <paramref name="id"/>, or <paramref name="fallback"/> if absent.</summary>
    public string GetOr(int id, string fallback) => _strings.TryGetValue(id, out var s) ? s : fallback;

    public bool TryGet(int id, out string value) => _strings.TryGetValue(id, out value!);

    /// <summary>
    /// Look up <paramref name="id"/> and substitute its C printf specifiers
    /// (<c>%d %i %u %s %c %x %X %f</c>, with optional width/precision/flags, and
    /// <c>%%</c>) with <paramref name="args"/> in order. Surplus specifiers are
    /// left as-is; surplus args are ignored.
    /// </summary>
    public string Format(int id, params object[] args) => FormatTemplate(Get(id), args);

    // ── Loading ───────────────────────────────────────────────────────────────

    private bool LoadPack(string language)
    {
        var resource = $"MapleClaude.Localization.strings.{language}.csv";
        // typeof(...).Assembly (not GetExecutingAssembly) so JIT inlining of this
        // method into a caller in another assembly can't redirect the lookup.
        var asm = typeof(StringPool).Assembly;
        using var stream = asm.GetManifestResourceStream(resource);
        if (stream is null)
        {
            _logger?.LogWarning("StringPool pack not found: {Resource}", resource);
            return false;
        }

        _strings.Clear();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        var lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (line.Length == 0) continue;
            if (!TryParseLine(line, out var id, out var value))
            {
                _logger?.LogDebug("StringPool: skipped malformed line {Line}", lineNo);
                continue;
            }
            _strings[id] = value;
        }
        return _strings.Count > 0;
    }

    // Parse one `id;"value"` row. Returns false on a shape we don't recognise.
    private static bool TryParseLine(string line, out int id, out string value)
    {
        id = 0;
        value = string.Empty;
        var sep = line.IndexOf(';');
        if (sep <= 0) return false;
        if (!int.TryParse(line.AsSpan(0, sep), out id)) return false;

        var rest = line[(sep + 1)..];
        // Strip the surrounding quotes if present.
        if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"')
        {
            rest = rest[1..^1];
        }
        value = Unescape(rest);
        return true;
    }

    // Convert `""` → `"` and literal `\r \n \t \\` escape sequences to real chars.
    private static string Unescape(string s)
    {
        if (s.IndexOf('"') < 0 && s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"' && i + 1 < s.Length && s[i + 1] == '"')
            {
                sb.Append('"');
                i++;
            }
            else if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                sb.Append(n switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => n,
                });
                i++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // ── printf-style formatting ─────────────────────────────────────────────────

    private static readonly Regex PrintfSpec =
        new(@"%%|%[-+ 0#]*\d*(?:\.\d+)?(?:hh|h|ll|l|L|z|j|t)?[diouxXeEfgGcsp]",
            RegexOptions.Compiled);

    internal static string FormatTemplate(string template, object[] args)
    {
        if (args.Length == 0 || template.IndexOf('%') < 0) return template;
        var argIndex = 0;
        return PrintfSpec.Replace(template, m =>
        {
            if (m.Value == "%%") return "%";
            if (argIndex >= args.Length) return m.Value; // not enough args — leave as-is
            return args[argIndex++]?.ToString() ?? string.Empty;
        });
    }
}
