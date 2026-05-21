using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Quest log panel. Toggle with Q key.
/// WZ: <c>UIWindow.img/QuestLog/</c>
/// </summary>
public sealed class QuestLog : GamePanel
{
    private sealed record QuestEntry(string Name, string Npc, string Desc, int Level, bool Complete);

    private readonly WzSprite? _background;
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;

    // Tabs
    private int _tab; // 0=In Progress, 1=Complete
    private int _scroll;

    private static readonly QuestEntry[] _allQuests =
    [
        new("A Strange Letter",       "Shanks",       "Deliver a letter to the chief of Amherst.",          1,  false),
        new("Ernie's Concern",        "Ernie",        "Collect 10 Blue Snail Shells for Ernie.",            3,  false),
        new("The Dangerous Forest",   "Chief Stan",   "Hunt 30 Orange Mushrooms in the Dangerous Forest.",  7,  false),
        new("Cold Wind from Ariant",  "Manji",        "Collect 20 Wooden Boards for Manji.",               10,  false),
        new("Lith Harbor",            "Shanks",       "Arrive at Lith Harbor by talking to Shanks.",        1,  true),
        new("Beginner's Tutorial",    "Maple Guide",  "Complete the tutorial quests on Maple Island.",      1,  true),
        new("Recover the Mineral",    "Dances With Bears", "Collect 50 Steely from Leatties.",             15,  true),
    ];

    private const int PanelW = 302;
    private const int PanelH = 396;
    private const int EntryH  = 58;
    private const int ListTop = 58;   // below title + tabs
    private const int ListBot = 360;  // above scroll arrows
    private const int VisibleRows = (ListBot - ListTop) / EntryH; // ≈5

    public QuestLog(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(50, 50);

        var ql = ui?.GetItem("UIWindow.img/QuestLog") as WzProperty;
        _background = ql?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        var closeRoot = ql?.Get("BtClose") as WzProperty;
        if (closeRoot != null)
            _btClose = new Button(loader, closeRoot)
            {
                Position = Position + new Vector2(284, 4),
                OnClick  = () => IsVisible = false,
            };
    }

    private QuestEntry[] CurrentList => _tab == 0
        ? [.. _allQuests.Where(q => !q.Complete)]
        : [.. _allQuests.Where(q =>  q.Complete)];

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        // Background
        if (_background != null)
            _background.Draw(sb, Position + new Vector2(150, 195));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        // Title
        _font?.Draw(sb, "Quest Log", new Vector2(px + 110, py + 5), new Color(220, 200, 150));

        // Tabs
        DrawTab(sb, white, px + 10,  py + 24, 130, "In Progress", _tab == 0);
        DrawTab(sb, white, px + 148, py + 24, 130, "Complete",    _tab == 1);

        // Separator
        sb.Draw(white, new Rectangle(px, py + 46, PanelW, 1), new Color(80, 70, 50));

        // Quest list
        var list  = CurrentList;
        var maxSc = Math.Max(0, list.Length - VisibleRows);
        _scroll   = Math.Clamp(_scroll, 0, maxSc);

        for (var i = 0; i < VisibleRows; i++)
        {
            var idx = i + _scroll;
            if (idx >= list.Length) break;
            DrawEntry(sb, white, list[idx], px + 6, py + ListTop + i * EntryH);
        }

        // Scroll arrows
        if (_scroll > 0)
            DrawArrow(sb, white, new Rectangle(px + 138, py + ListBot + 4, 12, 10), true);
        if (_scroll < maxSc)
            DrawArrow(sb, white, new Rectangle(px + 154, py + ListBot + 4, 12, 10), false);

        // Empty state
        if (list.Length == 0)
            _font?.Draw(sb, "(none)", new Vector2(px + 120, py + 180), new Color(140, 140, 140));

        _btClose?.Draw(sb);
    }

    private void DrawTab(SpriteBatch sb, Texture2D white, int x, int y, int w, string label, bool active)
    {
        var bg = active ? new Color(40, 40, 70, 220) : new Color(20, 20, 40, 160);
        sb.Draw(white, new Rectangle(x, y, w, 20), bg);
        DrawBorder(sb, white, new Rectangle(x, y, w, 20));
        var c = active ? new Color(255, 220, 100) : new Color(180, 180, 180);
        _font?.Draw(sb, label, new Vector2(x + 8, y + 3), c);
    }

    private void DrawEntry(SpriteBatch sb, Texture2D white, QuestEntry q, int x, int y)
    {
        sb.Draw(white, new Rectangle(x, y, PanelW - 12, EntryH - 2), new Color(25, 25, 45, 200));
        DrawBorder(sb, white, new Rectangle(x, y, PanelW - 12, EntryH - 2));

        // Status dot
        var dotColor = q.Complete ? new Color(80, 220, 80) : new Color(220, 180, 80);
        sb.Draw(white, new Rectangle(x + 4, y + 4, 8, 8), dotColor);

        // Name
        _font?.Draw(sb, q.Name, new Vector2(x + 16, y + 3), new Color(255, 230, 120));
        // NPC
        _font?.Draw(sb, $"NPC: {q.Npc}", new Vector2(x + 16, y + 18), new Color(160, 200, 240));
        // Description (truncated to fit)
        var desc = q.Desc.Length > 42 ? q.Desc[..42] + "…" : q.Desc;
        _font?.Draw(sb, desc, new Vector2(x + 16, y + 33), new Color(190, 190, 190));
        // Level req (right-aligned)
        var lvlStr = $"Lv.{q.Level}+";
        _font?.Draw(sb, lvlStr, new Vector2(x + PanelW - 50, y + 3), new Color(140, 200, 140));
    }

    private static void DrawArrow(SpriteBatch sb, Texture2D white, Rectangle r, bool up)
    {
        sb.Draw(white, r, new Color(80, 70, 50));
        // Simple triangle indicator using inner rect
        var inner = new Rectangle(r.X + 3, up ? r.Y + 4 : r.Y + 2, r.Width - 6, r.Height - 4);
        sb.Draw(white, inner, up ? new Color(180, 180, 100) : new Color(180, 180, 100));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        // Tab click
        if (down)
        {
            if (new Rectangle(px + 10, py + 24, 130, 20).Contains(x, y))  { _tab = 0; _scroll = 0; return true; }
            if (new Rectangle(px + 148, py + 24, 130, 20).Contains(x, y)) { _tab = 1; _scroll = 0; return true; }

            // Scroll arrows
            var list = CurrentList;
            if (new Rectangle(px + 138, py + ListBot + 4, 12, 10).Contains(x, y) && _scroll > 0)
                { _scroll--; return true; }
            if (new Rectangle(px + 154, py + ListBot + 4, 12, 10).Contains(x, y) && _scroll < Math.Max(0, list.Length - VisibleRows))
                { _scroll++; return true; }
        }

        return new Rectangle(px, py, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageUp)   { _scroll = Math.Max(0, _scroll - VisibleRows); return true; }
        if (key == Keys.PageDown) { _scroll = Math.Min(Math.Max(0, CurrentList.Length - VisibleRows), _scroll + VisibleRows); return true; }
        return true;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r)
    {
        var c = new Color(80, 70, 50);
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
