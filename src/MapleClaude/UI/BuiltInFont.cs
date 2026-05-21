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
    private readonly System.Drawing.StringFormat _stringFormat;
    private readonly Texture2D _asciiAtlas;
    private readonly Rectangle[] _asciiGlyphs = new Rectangle[128];
    private readonly Dictionary<int, GlyphEntry> _lazyGlyphs = new();

    public int LineHeight { get; }

    public BuiltInFont(GraphicsDevice gd, string fontFamily = "Malgun Gothic", float emSize = 11f)
    {
        _gd = gd;
        _sysFont = new System.Drawing.Font(fontFamily, emSize, System.Drawing.FontStyle.Regular);
        _stringFormat = System.Drawing.StringFormat.GenericTypographic;

        using var measureBmp = new System.Drawing.Bitmap(1, 1);
        using var measureG = System.Drawing.Graphics.FromImage(measureBmp);
        measureG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

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

        var totalW = 0;
        for (var ch = 32; ch < 128; ch++)
        {
            totalW += widths[ch];
        }
        if (totalW % 4 != 0)
        {
            totalW += 4 - (totalW % 4);
        }

        using var atlasBmp = new System.Drawing.Bitmap(totalW, LineHeight);
        using (var g = System.Drawing.Graphics.FromImage(atlasBmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            var x = 0;
            for (var ch = 32; ch < 128; ch++)
            {
                var w = widths[ch];
                _asciiGlyphs[ch] = new Rectangle(x, 0, w, LineHeight);
                g.DrawString(((char)ch).ToString(), _sysFont, brush, x, 0, _stringFormat);
                x += w;
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
        measureG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var sz = measureG.MeasureString(s, _sysFont, System.Drawing.PointF.Empty, _stringFormat);
        var w = Math.Max(1, (int)Math.Ceiling(sz.Width)) + 2;
        var h = LineHeight;

        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.DrawString(s, _sysFont, brush, 0, 0, _stringFormat);
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
