using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Speech bubbles shown above a character for a few seconds after they chat. Authentic v95 look: a
/// 9-slice frame from <c>UI.wz/ChatBalloon.img/&lt;type&gt;</c> (fixed corners, tiled/stretched edges,
/// stretched centre) with a downward <c>arrow</c> that points at the speaker's head. Keyed by char id,
/// so a new message from the same character replaces the old bubble.
/// </summary>
public sealed class ChatBalloonLayer
{
    private const float Ttl = 4f;          // seconds a bubble stays up (v95 = 4000 ms)
    private const int MaxTextWidth = 160;  // wrap long messages to keep the bubble sane

    private readonly BuiltInFont? _font;
    private readonly WzSprite? _nw, _n, _ne, _w, _c, _e, _sw, _s, _se, _arrow;
    private readonly Dictionary<int, Balloon> _active = new();

    private sealed class Balloon { public required string[] Lines; public required int Width; public float Life; }

    public ChatBalloonLayer(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        if (ui?.GetItem("ChatBalloon.img/0") is WzProperty b)
        {
            WzSprite? P(string k) => b.Get(k) is WzCanvas c ? loader.Load(c) : null;
            _nw = P("nw"); _n = P("n"); _ne = P("ne");
            _w  = P("w");  _c = P("c"); _e  = P("e");
            _sw = P("sw"); _s = P("s"); _se = P("se");
            _arrow = P("arrow");
        }
    }

    /// <summary>Show (or replace + reset) the bubble for a character.</summary>
    public void Set(int charId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = Wrap(text);
        var width = 0;
        foreach (var ln in lines) width = Math.Max(width, (int)(_font?.Measure(ln).X ?? 0));
        _active[charId] = new Balloon { Lines = lines, Width = width, Life = Ttl };
    }

    public void Update(float dt)
    {
        if (_active.Count == 0) return;
        List<int>? expired = null;
        foreach (var (id, b) in _active)
        {
            b.Life -= dt;
            if (b.Life <= 0f) (expired ??= new()).Add(id);
        }
        if (expired != null) foreach (var id in expired) _active.Remove(id);
    }

    /// <summary>Draw each active bubble. <paramref name="headScreenPos"/> maps a char id to the screen
    /// point just above its head (the arrow tip), or null when it shouldn't be drawn (off-screen/unknown).</summary>
    public void Draw(SpriteBatch sb, Func<int, Vector2?> headScreenPos)
    {
        if (_active.Count == 0 || _c is null) return;
        foreach (var (id, b) in _active)
        {
            if (headScreenPos(id) is { } tip) DrawBubble(sb, b, tip);
        }
    }

    private string[] Wrap(string text)
    {
        if (_font is null) return [text];
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' '))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (_font.Measure(trial).X > MaxTextWidth && cur.Length > 0) { lines.Add(cur); cur = word; }
            else cur = trial;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines.Count == 0 ? [text] : lines.ToArray();
    }

    private void DrawBubble(SpriteBatch sb, Balloon b, Vector2 tip)
    {
        var lineH = _font?.LineHeight ?? 13;
        int bl = _w?.Width ?? 6, br = _e?.Width ?? 6, bt = _n?.Height ?? 6, bb = _s?.Height ?? 6;
        var innerW = Math.Max(b.Width, 8);
        var innerH = b.Lines.Length * lineH;
        var win = new Rectangle(0, 0, innerW + bl + br, innerH + bt + bb);
        var arrowH = _arrow?.Height ?? 6;
        win.X = (int)(tip.X - win.Width / 2f);
        win.Y = (int)(tip.Y - arrowH - win.Height);

        if (_c != null) Stretch(sb, _c, win.X + bl, win.Y + bt, win.Width - bl - br, win.Height - bt - bb);
        _nw?.Draw(sb, new Vector2(win.X, win.Y));
        _ne?.Draw(sb, new Vector2(win.Right - (_ne?.Width ?? 0), win.Y));
        _sw?.Draw(sb, new Vector2(win.X, win.Bottom - (_sw?.Height ?? 0)));
        _se?.Draw(sb, new Vector2(win.Right - (_se?.Width ?? 0), win.Bottom - (_se?.Height ?? 0)));
        if (_n != null) Stretch(sb, _n, win.X + bl, win.Y, win.Width - bl - br, _n.Height);
        if (_s != null) Stretch(sb, _s, win.X + bl, win.Bottom - _s.Height, win.Width - bl - br, _s.Height);
        if (_w != null) Stretch(sb, _w, win.X, win.Y + bt, _w.Width, win.Height - bt - bb);
        if (_e != null) Stretch(sb, _e, win.Right - _e.Width, win.Y + bt, _e.Width, win.Height - bt - bb);
        _arrow?.Draw(sb, new Vector2(tip.X - (_arrow?.Width ?? 0) / 2f, win.Bottom));

        for (var i = 0; i < b.Lines.Length; i++)
        {
            var tw = _font?.Measure(b.Lines[i]).X ?? 0;
            _font?.Draw(sb, b.Lines[i], new Vector2(win.X + bl + (innerW - tw) / 2f, win.Y + bt + i * lineH), Color.Black);
        }
    }

    private static void Stretch(SpriteBatch sb, WzSprite s, int x, int y, int w, int h)
    {
        if (w > 0 && h > 0) sb.Draw(s.Texture, new Rectangle(x, y, w, h), Color.White);
    }
}
