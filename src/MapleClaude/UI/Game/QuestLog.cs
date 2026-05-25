using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace MapleClaude.UI.Game;

/// <summary>
/// Quest log — the authentic v95 <c>CUIQuestInfo</c> list panel, drawn from
/// <c>UIWindow2.img/Quest/list</c> (layered backgrounds 235×396). Four category tabs sit at the top
/// (their baked labels: <b>Available</b>, <b>In Progress</b>, <b>Completed</b>, <b>Party Quest</b>),
/// each an origin-baked <c>Tab/enabled|disabled/&lt;i&gt;</c> sprite. The selected category's quests
/// are listed below with a scroll; an empty category shows its <c>notice&lt;i&gt;</c> message.
///
/// Data: In Progress / Completed come from the server quest records we hold. "Available" needs the
/// not-yet-started quest set (Quest.wz Check requirements) which isn't tracked client-side yet, so
/// that tab + Party Quest render their empty-state notice for now.
/// </summary>
public sealed class QuestLog : GamePanel
{
    public sealed class QuestEntry
    {
        public int    Id;
        public string Name     = string.Empty;
        public string Progress = string.Empty;
        public bool   Complete;
    }

    // Tab indices.
    private const int TabAvailable = 0, TabInProgress = 1, TabCompleted = 2, TabParty = 3;

    private readonly WzSprite? _bg, _bg2;
    private readonly WzSprite?[] _tabOn  = new WzSprite?[4];
    private readonly WzSprite?[] _tabOff = new WzSprite?[4];
    private readonly WzSprite?[] _notice = new WzSprite?[4];
    private readonly Button? _btClose;
    private readonly BuiltInFont? _font;

    // Tab hit-rects (from Tab/enabled origins): x, width; all at y=25, h=22.
    private static readonly int[] TabX = { 9, 59, 117, 169 };
    private static readonly int[] TabW = { 49, 57, 51, 57 };

    private int _tab = TabInProgress;
    private int _scroll;
    private int _selected = -1;
    private bool _dragging;
    private Vector2 _dragOff;

    private readonly List<QuestEntry> _quests = new();

    public Action<int>? OnResign { get; set; }

    public void SetQuests(IEnumerable<QuestEntry> quests)
    {
        _quests.Clear();
        _quests.AddRange(quests);
        _selected = -1;
    }

    public void UpdateQuest(int id, byte state, string progress, string name)
    {
        var existing = _quests.Find(q => q.Id == id);
        if (state == 0) { if (existing != null) _quests.Remove(existing); return; }
        existing ??= AddQuest(id, name);
        if (!string.IsNullOrEmpty(name)) existing.Name = name;
        existing.Progress = progress;
        existing.Complete = state == 2;
    }

    private QuestEntry AddQuest(int id, string name)
    {
        var q = new QuestEntry { Id = id, Name = string.IsNullOrEmpty(name) ? $"Quest {id}" : name };
        _quests.Add(q);
        return q;
    }

    private const int EntryH = 34;
    private const int ListTop = 52;
    private const int ListBot = 372;
    private int VisibleRows => (ListBot - ListTop) / EntryH;

    private int PanelW => _bg?.Width ?? 235;
    private int PanelH => _bg?.Height ?? 396;

    public QuestLog(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(50, 50);

        var list = ui?.GetItem("UIWindow2.img/Quest/list") as WzProperty;
        _bg  = Canvas(loader, list, "backgrnd");
        _bg2 = Canvas(loader, list, "backgrnd2");
        var on  = (list?.Get("Tab") as WzProperty)?.Get("enabled")  as WzProperty;
        var off = (list?.Get("Tab") as WzProperty)?.Get("disabled") as WzProperty;
        for (var i = 0; i < 4; i++) { _tabOn[i] = Canvas(loader, on, i.ToString()); _tabOff[i] = Canvas(loader, off, i.ToString()); }
        for (var i = 0; i < 4; i++) _notice[i] = Canvas(loader, list, $"notice{i}");

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
    }

    private List<QuestEntry> CurrentList => _tab switch
    {
        TabInProgress => _quests.Where(q => !q.Complete).ToList(),
        TabCompleted  => _quests.Where(q => q.Complete).ToList(),
        _             => new List<QuestEntry>(),   // Available / Party Quest: not tracked yet
    };

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);
        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
        _btClose?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_bg != null) { _bg.Draw(sb, Position); _bg2?.Draw(sb, Position); }
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(15, 15, 25, 230));
            _font?.Draw(sb, "QUEST", new Vector2(px + 100, py + 5), new Color(220, 200, 150));
        }

        // Tabs.
        for (var i = 0; i < 4; i++)
        {
            var spr = i == _tab ? _tabOn[i] : _tabOff[i];
            if (spr != null) spr.Draw(sb, Position);
        }

        var list = CurrentList;
        if (list.Count == 0)
        {
            // Empty-state message for this category (notice0..3 are centred in the panel).
            if (_notice[_tab] != null) _notice[_tab]!.Draw(sb, Position);
            else _font?.Draw(sb, "(none)", new Vector2(px + PanelW / 2f - 18, py + 180), new Color(150, 150, 150));
        }
        else
        {
            var maxSc = Math.Max(0, list.Count - VisibleRows);
            _scroll = Math.Clamp(_scroll, 0, maxSc);
            for (var i = 0; i < VisibleRows; i++)
            {
                var idx = i + _scroll;
                if (idx >= list.Count) break;
                DrawEntry(sb, white, list[idx], px + 8, py + ListTop + i * EntryH, list[idx].Id == _selected);
            }
            if (_scroll > 0)
                _font?.Draw(sb, "▲", new Vector2(px + PanelW / 2f - 4, py + ListTop - 12), new Color(200, 200, 220));
            if (_scroll < maxSc)
                _font?.Draw(sb, "▼", new Vector2(px + PanelW / 2f - 4, py + ListBot + 2), new Color(200, 200, 220));
        }

        _btClose?.Draw(sb);
    }

    private void DrawEntry(SpriteBatch sb, Texture2D white, QuestEntry q, int x, int y, bool selected)
    {
        var w = PanelW - 16;
        if (selected) sb.Draw(white, new Rectangle(x, y, w, EntryH - 2), new Color(255, 245, 200, 90));

        // Status dot (gold = in progress, green = complete).
        sb.Draw(white, new Rectangle(x + 4, y + 5, 8, 8), q.Complete ? new Color(80, 200, 80) : new Color(225, 180, 70));
        _font?.Draw(sb, q.Name, new Vector2(x + 18, y + 2), new Color(40, 36, 30));
        if (!string.IsNullOrEmpty(q.Progress))
        {
            var prog = q.Progress.Length > 32 ? q.Progress[..32] + "…" : q.Progress;
            _font?.Draw(sb, prog, new Vector2(x + 18, y + 17), new Color(90, 90, 90));
        }
        if (selected && !q.Complete)
            _font?.Draw(sb, "[Del] resign", new Vector2(x + w - 78, y + 17), new Color(190, 90, 60));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (!down) return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);

        // Tab switch.
        for (var i = 0; i < 4; i++)
            if (new Rectangle((int)Position.X + TabX[i], (int)Position.Y + 25, TabW[i], 22).Contains(x, y))
            { _tab = i; _scroll = 0; _selected = -1; return true; }

        // Quest row select.
        var list = CurrentList;
        for (var i = 0; i < VisibleRows; i++)
        {
            var idx = i + _scroll;
            if (idx >= list.Count) break;
            var row = new Rectangle((int)Position.X + 8, (int)Position.Y + ListTop + i * EntryH, PanelW - 16, EntryH - 2);
            if (row.Contains(x, y)) { _selected = list[idx].Id; return true; }
        }

        // Title-strip drag.
        if (new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22).Contains(x, y))
        { _dragging = true; _dragOff = new Vector2(x - Position.X, y - Position.Y); return true; }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageUp)   { _scroll = Math.Max(0, _scroll - VisibleRows); return true; }
        if (key == Keys.PageDown) { _scroll = Math.Min(Math.Max(0, CurrentList.Count - VisibleRows), _scroll + VisibleRows); return true; }
        if (key == Keys.Delete && _selected >= 0)
        {
            var q = _quests.Find(e => e.Id == _selected);
            if (q is { Complete: false }) OnResign?.Invoke(_selected);
            return true;
        }
        return true;
    }

    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? root, string name) =>
        root?.Get(name) is WzCanvas c ? loader.Load(c) : null;
}
