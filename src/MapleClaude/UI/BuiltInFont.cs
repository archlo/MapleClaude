using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI;

/// <summary>
/// Runtime bitmap font built from a System.Drawing system font. ASCII
/// 0x20–0x7E is pre-rasterised into a single horizontal-strip atlas at
/// construction time for fast startup. Any non-ASCII codepoint (Hangul,
/// Hiragana, CJK ideographs, accented Latin, …) is rendered lazily into
/// its own small per-glyph texture on first use and cached. Pick a font
/// family with broad Unicode coverage (Malgun Gothic, Microsoft Sans
/// Serif) so the lazy fallback finds real glyphs.
///
/// Glyphs are rasterised white-on-transparent; the <see cref="SpriteBatch"/>
/// tint colour determines the final ink colour, and the semi-transparent
/// edges from antialiasing blend correctly under
/// <see cref="BlendState.NonPremultiplied"/>.
/// </summary>
public sealed class BuiltInFont : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly System.Drawing.Font _sysFont;
    private readonly System.Drawing.Font? _fallbackFont;
    private readonly System.Drawing.StringFormat _stringFormat;
    private readonly System.Drawing.Text.TextRenderingHint _hint;
    private readonly Texture2D _asciiAtlas;
    private readonly Rectangle[] _asciiGlyphs = new Rectangle[128];
    private readonly Dictionary<int, GlyphEntry> _lazyGlyphs = new();

    public int LineHeight { get; }

    public BuiltInFont(GraphicsDevice gd, string fontFamily = "Malgun Gothic", float emSize = 11f,
        System.Drawing.GraphicsUnit unit = System.Drawing.GraphicsUnit.Point,
        System.Drawing.Text.TextRenderingHint hint = System.Drawing.Text.TextRenderingHint.AntiAlias,
        string? fallbackFamily = null,
        System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular)
    {
        _gd = gd;
        _hint = hint;
        _sysFont = new System.Drawing.Font(fontFamily, emSize, style, unit);
        // Optional fallback face for codepoints the primary font lacks (Tahoma has no Hangul, so typed
        // Korean falls back to Malgun Gothic). Only the lazy non-ASCII path consults it.
        if (!string.IsNullOrEmpty(fallbackFamily) &&
            !string.Equals(fallbackFamily, fontFamily, StringComparison.OrdinalIgnoreCase))
        {
            try { _fallbackFont = new System.Drawing.Font(fallbackFamily, emSize, style, unit); }
            catch { _fallbackFont = null; }
        }
        _stringFormat = System.Drawing.StringFormat.GenericTypographic;

        using var measureBmp = new System.Drawing.Bitmap(1, 1);
        using var measureG = System.Drawing.Graphics.FromImage(measureBmp);
        measureG.TextRenderingHint = _hint;

        var widths = new int[128];
        var maxH = 0f;
        for (var ch = 32; ch < 128; ch++)
        {
            var s = ((char)ch).ToString();
            var sz = measureG.MeasureString(s, _sysFont, System.Drawing.PointF.Empty, _stringFormat);
            widths[ch] = Math.Max(1, (int)Math.Ceiling(sz.Width)) + 2;
            if (sz.Height > maxH)
            {
                maxH = sz.Height;
            }
        }
        LineHeight = (int)Math.Ceiling(maxH) + 2;

        const int gap = 3;   // blank columns between atlas cells so a neighbour glyph's side-bearing can't bleed in
        var totalW = 0;
        for (var ch = 32; ch < 128; ch++)
        {
            totalW += widths[ch] + gap;
        }
        if (totalW % 4 != 0)
        {
            totalW += 4 - (totalW % 4);
        }

        using var atlasBmp = new System.Drawing.Bitmap(totalW, LineHeight);
        using (var g = System.Drawing.Graphics.FromImage(atlasBmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = _hint;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            var x = 0;
            for (var ch = 32; ch < 128; ch++)
            {
                var w = widths[ch];
                _asciiGlyphs[ch] = new Rectangle(x, 0, w, LineHeight);
                g.DrawString(((char)ch).ToString(), _sysFont, brush, x, 0, _stringFormat);
                x += w + gap;
            }
        }
        _asciiAtlas = BitmapToTexture(atlasBmp);
    }

    private Texture2D BitmapToTexture(System.Drawing.Bitmap bmp)
    {
        var bmpRect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(bmpRect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(data);
        // System.Drawing's Format32bppArgb is BGRA in memory; MonoGame's
        // SurfaceFormat.Color is RGBA. Swap red and blue per pixel.
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var b = bytes[i];
            bytes[i] = bytes[i + 2];
            bytes[i + 2] = b;
        }
        var tex = new Texture2D(_gd, bmp.Width, bmp.Height, false, SurfaceFormat.Color);
        tex.SetData(bytes);
        return tex;
    }

    private GlyphEntry GetGlyph(int rune)
    {
        if (rune is >= 32 and < 128)
        {
            var rect = _asciiGlyphs[rune];
            return new GlyphEntry(_asciiAtlas, rect, rect.Width);
        }
        if (_lazyGlyphs.TryGetValue(rune, out var entry))
        {
            return entry;
        }
        entry = RenderLazyGlyph(rune);
        _lazyGlyphs[rune] = entry;
        return entry;
    }

    private GlyphEntry RenderLazyGlyph(int rune)
    {
        string s;
        try
        {
            s = char.ConvertFromUtf32(rune);
        }
        catch
        {
            return new GlyphEntry(_asciiAtlas, Rectangle.Empty, 0);
        }

        using var measureBmp = new System.Drawing.Bitmap(1, 1);
        using var measureG = System.Drawing.Graphics.FromImage(measureBmp);
        measureG.TextRenderingHint = _hint;
        var font = _fallbackFont ?? _sysFont;   // non-ASCII (Hangul/CJK) prefers the fallback face
        var sz = measureG.MeasureString(s, font, System.Drawing.PointF.Empty, _stringFormat);
        var w = Math.Max(1, (int)Math.Ceiling(sz.Width)) + 2;
        var h = LineHeight;

        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = _hint;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.DrawString(s, font, brush, 0, 0, _stringFormat);
        }
        var tex = BitmapToTexture(bmp);
        return new GlyphEntry(tex, new Rectangle(0, 0, w, h), w);
    }

    public Vector2 Measure(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return new Vector2(0, LineHeight);
        }
        var w = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            w += GetGlyph(rune.Value).Width;
        }
        return new Vector2(w, LineHeight);
    }

    /// <summary>If <paramref name="text"/> exceeds <paramref name="maxWidth"/>, trim runes from the
    /// end and append <paramref name="ellipsis"/> until it fits. Used for the single-line quest-row
    /// names and the detail-panel header.</summary>
    public string TruncateToWidth(string text, float maxWidth, string ellipsis = "…")
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return text ?? string.Empty;
        if (Measure(text).X <= maxWidth) return text;
        var ellW = Measure(ellipsis).X;
        if (ellW > maxWidth) return string.Empty;
        var runes = text.EnumerateRunes().ToList();
        var widths = runes.Select(r => GetGlyph(r.Value).Width).ToList();
        var cum = 0f;
        var end = 0;
        while (end < runes.Count && cum + widths[end] + ellW <= maxWidth)
        {
            cum += widths[end];
            end++;
        }
        if (end == 0) return ellipsis;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < end; i++) sb.Append(runes[i].ToString());
        sb.Append(ellipsis);
        return sb.ToString();
    }

    /// <summary>Greedy word-wrap on whitespace + explicit '\n'. Returns the wrapped lines.
    /// Words longer than <paramref name="maxWidth"/> are split mid-rune so they don't overflow.</summary>
    public IReadOnlyList<string> WrapToWidth(string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return lines;
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            var lineW = 0f;
            foreach (var word in SplitWords(paragraph))
            {
                var ww = Measure(word).X;
                if (lineW == 0 && ww > maxWidth)
                {
                    // Hard-break a single word wider than the column.
                    var buf = new System.Text.StringBuilder();
                    var bufW = 0f;
                    foreach (var rune in word.EnumerateRunes())
                    {
                        var gw = GetGlyph(rune.Value).Width;
                        if (bufW + gw > maxWidth && buf.Length > 0)
                        {
                            lines.Add(buf.ToString());
                            buf.Clear();
                            bufW = 0f;
                        }
                        buf.Append(rune.ToString());
                        bufW += gw;
                    }
                    if (buf.Length > 0) { line.Append(buf); lineW = bufW; }
                    continue;
                }
                if (lineW + ww > maxWidth && line.Length > 0)
                {
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                    lineW = 0f;
                    if (word.Length > 0 && char.IsWhiteSpace(word[0])) continue;  // drop leading space on wrap
                }
                line.Append(word);
                lineW += ww;
            }
            lines.Add(line.ToString().TrimEnd());
        }
        return lines;
    }

    private static IEnumerable<string> SplitWords(string paragraph)
    {
        var i = 0;
        while (i < paragraph.Length)
        {
            var start = i;
            var ws = char.IsWhiteSpace(paragraph[i]);
            while (i < paragraph.Length && char.IsWhiteSpace(paragraph[i]) == ws) i++;
            yield return paragraph[start..i];
        }
    }

    public void Draw(SpriteBatch sb, string s, Vector2 pos, Color color)
    {
        if (string.IsNullOrEmpty(s))
        {
            return;
        }
        var x = pos.X;
        var y = pos.Y;
        foreach (var rune in s.EnumerateRunes())
        {
            var info = GetGlyph(rune.Value);
            if (info.Width > 0 && info.Texture is not null)
            {
                sb.Draw(info.Texture, new Vector2(x, y), info.Source, color);
            }
            x += info.Width;
        }
    }

    /// <summary>Draw at a uniform scale — for small labels (e.g. the char-creation row names)
    /// where the full-size UI font overflows the space between the cyclers.</summary>
    public void Draw(SpriteBatch sb, string s, Vector2 pos, Color color, float scale)
        => Draw(sb, s, pos, color, scale, 0);

    /// <summary>Draw at a uniform scale with per-glyph letter-spacing adjustment. Each glyph's advance
    /// already carries +2px of padding (see ctor); pass a negative <paramref name="tracking"/> (e.g. -2)
    /// to tighten the spacing for compact labels like the tooltip's job-requirement tabs.</summary>
    public void Draw(SpriteBatch sb, string s, Vector2 pos, Color color, float scale, int tracking)
    {
        if (string.IsNullOrEmpty(s))
        {
            return;
        }
        var x = pos.X;
        var y = pos.Y;
        foreach (var rune in s.EnumerateRunes())
        {
            var info = GetGlyph(rune.Value);
            if (info.Width > 0 && info.Texture is not null)
            {
                sb.Draw(info.Texture, new Vector2(x, y), info.Source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            x += info.Width * scale + tracking;
        }
    }

    public void Dispose()
    {
        _asciiAtlas?.Dispose();
        foreach (var entry in _lazyGlyphs.Values)
        {
            if (entry.Texture != _asciiAtlas)
            {
                entry.Texture?.Dispose();
            }
        }
        _sysFont.Dispose();
        _fallbackFont?.Dispose();
    }

    private readonly struct GlyphEntry
    {
        public readonly Texture2D? Texture;
        public readonly Rectangle Source;
        public readonly int Width;
        public GlyphEntry(Texture2D? texture, Rectangle source, int width)
        {
            Texture = texture;
            Source = source;
            Width = width;
        }
    }
}
