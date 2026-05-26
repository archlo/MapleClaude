using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace MapleClaude.UI.Game;

/// <summary>
/// Quest log — authentic v95 <c>CUIQuestInfo</c> rebuilt from <c>UIWindow2.img/Quest/list</c>.
/// 235×396 panel with four top tabs (Available / In Progress / Completed / Party Quest), an empty-state
/// <c>notice{i}</c> per tab, three bottom buttons (<c>BtMyLevel</c>, <c>BtAllLevel</c>, <c>BtIconInfo</c>),
/// and a vertical scrollbar pinned to the right edge of the inner panel. Rows are 22px high
/// (CUIQuestInfo IDB stride) with single-line names truncated to the row width with an ellipsis;
/// quests are partitioned into collapsible groups labelled by <c>QuestInfo.img/{id}/parent</c>
/// (e.g. "Job (2)", "Victoria Island (10)"). Selecting a row fires <see cref="OnSelectQuest"/> so
/// the parent stage can open the companion <c>QuestDetail</c> panel beside this one.
/// </summary>
public sealed class QuestLog : GamePanel
{
    /// <summary>One row in the list. <see cref="Parent"/> drives the group header; <see cref="LvMin"/>/
    /// <see cref="LvMax"/> feed the detail panel's "Over Level / Under Level" header.</summary>
    public sealed class QuestEntry
    {
        public int    Id;
        public string Name     = string.Empty;
        public string Progress = string.Empty;
        public string Parent   = string.Empty;
        public int    LvMin;
        public int    LvMax;
        public bool   Complete;
        public bool   Available;
    }

    // Tab indices (display order matches CUIQuestInfo).
    private const int TabAvailable = 0, TabInProgress = 1, TabCompleted = 2, TabParty = 3;

    // CUIQuestInfo IDB: list hit-area (14, 52)..(216, 372), 22-px row stride; the inner panel is
    // backgrnd2 at offset (-6, -23), so the right-edge scrollbar gutter starts at 218 inside the bar.
    private const int PanelW0 = 235, PanelH0 = 396;
    private const int ListLeft = 14, ListTop = 52, ListRight = 216, ListBottom = 372;
    private const int RowH = 22;
    private const int ScrollGutterX = 217, ScrollW = 12;
    private const int HeaderIndent = 14, RowIndent = 26;

    private readonly WzSprite? _bg, _bg2;
    private readonly WzSprite?[] _tabOn  = new WzSprite?[4];
    private readonly WzSprite?[] _tabOff = new WzSprite?[4];
    private readonly WzSprite?[] _notice = new WzSprite?[4];
    private readonly WzSprite?[] _bulletIcons = new WzSprite?[10];
    private readonly Button? _btClose;
    private readonly Button? _btMyLevel, _btAllLevel, _btIconInfo;
    private readonly BuiltInFont? _font;

    // Tab hit-rects (from Tab/enabled origins): x, width; all at y=25, h=22.
    private static readonly int[] TabX = { 9, 59, 117, 169 };
    private static readonly int[] TabW = { 49, 57, 51, 57 };

    private int _tab = TabInProgress;
    private float _scroll;                      // top of the viewport in flat-row units
    private int _selected = -1;
    private bool _dragging;                     // panel drag
    private bool _draggingThumb;                // scrollbar thumb drag
    private float _thumbGrabDy;                 // y-offset within the thumb at drag start
    private Vector2 _dragOff;
    private bool _myLevelOnly = true;           // BtMyLevel default; BtAllLevel disables the level filter
    private int _prevWheel;

    // Per-tab collapse state, keyed by group label.
    private readonly Dictionary<int, HashSet<string>> _collapsedByTab = new()
    {
        [TabAvailable] = new(), [TabInProgress] = new(),
        [TabCompleted] = new(), [TabParty] = new(),
    };

    private readonly List<QuestEntry> _quests = new();
    private readonly List<QuestEntry> _available = new();

    /// <summary>Server-driven resign (Del) — kept for backward compat with GameStage.</summary>
    public Action<int>? OnResign { get; set; }

    /// <summary>Fires when a row is clicked. The stage opens the detail panel.</summary>
    public Action<int>? OnSelectQuest { get; set; }

    /// <summary>True when the WZ <c>BtAllLevel</c> button is active and we should NOT apply the
    /// level filter in the Available tab. The actual filter happens upstream
    /// (GameStage.CanStartQuest); this flag is just the toggle exposed to it.</summary>
    public bool ShowAllLevels => !_myLevelOnly;

    /// <summary>Fires when the BtMyLevel / BtAllLevel toggle flips. The stage rebuilds the Available
    /// set with or without the level gate.</summary>
    public Action? OnLevelFilterChanged { get; set; }

    public void SetQuests(IEnumerable<QuestEntry> quests)
    {
        _quests.Clear();
        _quests.AddRange(quests);
        _selected = -1;
        _scroll = 0;
    }

    public void SetAvailable(IEnumerable<QuestEntry> quests)
    {
        _available.Clear();
        _available.AddRange(quests);
        if (_tab == TabAvailable) { _selected = -1; _scroll = 0; }
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

    private int PanelW => _bg?.Width  ?? PanelW0;
    private int PanelH => _bg?.Height ?? PanelH0;

    /// <summary>Outer panel width — used by the parent stage to anchor the companion detail panel.</summary>
    public int OuterWidth => PanelW;

    public QuestLog(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(50, 50);

        var quest = ui?.GetItem("UIWindow2.img/Quest") as WzProperty;
        var list  = quest?.Get("list") as WzProperty;
        _bg  = Canvas(loader, list, "backgrnd");
        _bg2 = Canvas(loader, list, "backgrnd2");
        var on  = (list?.Get("Tab") as WzProperty)?.Get("enabled")  as WzProperty;
        var off = (list?.Get("Tab") as WzProperty)?.Get("disabled") as WzProperty;
        for (var i = 0; i < 4; i++) { _tabOn[i] = Canvas(loader, on, i.ToString()); _tabOff[i] = Canvas(loader, off, i.ToString()); }
        for (var i = 0; i < 4; i++) _notice[i] = Canvas(loader, list, $"notice{i}");

        // Small status bullets (icon0..icon9). The first frame of icon2/3/5/6/7/8/9 sub-properties
        // is the resolvable canvas; the iconN that are direct canvases (0, 1, 4) load straight.
        var icons = (quest?.Get("icon") as WzProperty);
        for (var i = 0; i < 10; i++)
        {
            if (icons?.Get($"icon{i}") is WzCanvas c) _bulletIcons[i] = loader.Load(c);
            else if (icons?.Get($"icon{i}") is WzProperty pr && pr.Get("0") is WzCanvas c0) _bulletIcons[i] = loader.Load(c0);
        }

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };

        // Bottom-bar buttons baked into the WZ. BtMyLevel: show only quests at my level (default).
        // BtAllLevel: ignore the level gate. BtIconInfo opens a legend (deferred — click is no-op).
        _btMyLevel  = ButtonFromList(loader, list, "BtMyLevel",  () => SetLevelFilter(true));
        _btAllLevel = ButtonFromList(loader, list, "BtAllLevel", () => SetLevelFilter(false));
        _btIconInfo = ButtonFromList(loader, list, "BtIconInfo", () => { /* legend popup deferred */ });
    }

    private static Button? ButtonFromList(WzTextureLoader loader, WzProperty? list, string name, Action onClick)
        => list?.Get(name) is WzProperty p ? new Button(loader, p) { OnClick = onClick } : null;

    private void SetLevelFilter(bool myLevelOnly)
    {
        if (_myLevelOnly == myLevelOnly) return;
        _myLevelOnly = myLevelOnly;
        OnLevelFilterChanged?.Invoke();
    }

    private List<QuestEntry> CurrentList => _tab switch
    {
        TabAvailable  => _available,
        TabInProgress => _quests.Where(q => !q.Complete).ToList(),
        TabCompleted  => _quests.Where(q => q.Complete).ToList(),
        _             => new List<QuestEntry>(),
    };

    // --- Group model: partition the current tab by Parent, preserve insertion order. ---
    private readonly struct Group
    {
        public readonly string Label;
        public readonly List<QuestEntry> Items;
        public Group(string label) { Label = label; Items = new(); }
    }

    private List<Group> BuildGroups(List<QuestEntry> list)
    {
        var groups = new List<Group>();
        var byLabel = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var q in list)
        {
            var label = string.IsNullOrEmpty(q.Parent) ? "Other" : q.Parent;
            if (!byLabel.TryGetValue(label, out var g))
            {
                g = new Group(label);
                byLabel[label] = g;
                groups.Add(g);
            }
            g.Items.Add(q);
        }
        return groups;
    }

    // One "flat row" is either a group header or a quest inside an expanded group.
    private enum RowKind { Header, Item }
    private readonly struct FlatRow
    {
        public readonly RowKind Kind;
        public readonly string GroupLabel;
        public readonly QuestEntry? Item;
        public readonly int ItemCount;     // for headers
        public readonly bool Collapsed;    // for headers
        public FlatRow(RowKind kind, string label, QuestEntry? item, int count, bool collapsed)
        { Kind = kind; GroupLabel = label; Item = item; ItemCount = count; Collapsed = collapsed; }
    }

    private List<FlatRow> Flatten()
    {
        var rows = new List<FlatRow>();
        var groups = BuildGroups(CurrentList);
        var collapsed = _collapsedByTab[_tab];
        foreach (var g in groups)
        {
            rows.Add(new FlatRow(RowKind.Header, g.Label, null, g.Items.Count, collapsed.Contains(g.Label)));
            if (collapsed.Contains(g.Label)) continue;
            foreach (var q in g.Items) rows.Add(new FlatRow(RowKind.Item, g.Label, q, 0, false));
        }
        return rows;
    }

    private int VisibleRows => (ListBottom - ListTop) / RowH;

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);
        // Bottom-row buttons sit on backgrnd2's bottom edge; the WZ origins place them at the right spots.
        if (_btMyLevel  != null) _btMyLevel.Position  = Position;
        if (_btAllLevel != null) _btAllLevel.Position = Position;
        if (_btIconInfo != null) _btIconInfo.Position = Position;

        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
        if (_draggingThumb)
        {
            if (m.LeftButton == ButtonState.Pressed) DragThumbTo(m.Y);
            else _draggingThumb = false;
        }

        // Mouse wheel inside the list area scrolls by one row at a time.
        if (new Rectangle(px + ListLeft, py + ListTop, ListRight - ListLeft, ListBottom - ListTop).Contains(m.X, m.Y))
        {
            var d = m.ScrollWheelValue - _prevWheel;
            if (d != 0) _scroll = Math.Max(0, _scroll - Math.Sign(d));
        }
        _prevWheel = m.ScrollWheelValue;

        ClampScroll();
        _btClose?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
        _btMyLevel?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
        _btAllLevel?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
        _btIconInfo?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    private void ClampScroll()
    {
        var rows = Flatten().Count;
        var max = Math.Max(0, rows - VisibleRows);
        if (_scroll > max) _scroll = max;
        if (_scroll < 0) _scroll = 0;
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

        var rows = Flatten();
        if (rows.Count == 0)
        {
            if (_notice[_tab] != null) _notice[_tab]!.Draw(sb, Position);
            else _font?.Draw(sb, "(none)", new Vector2(px + PanelW / 2f - 18, py + 180), new Color(150, 150, 150));
        }
        else
        {
            DrawList(sb, white, rows, px, py);
            DrawScrollbar(sb, white, rows.Count, px, py);
        }

        // Bottom-bar buttons + close.
        _btMyLevel?.Draw(sb);
        _btAllLevel?.Draw(sb);
        _btIconInfo?.Draw(sb);
        _btClose?.Draw(sb);
    }

    private void DrawList(SpriteBatch sb, Texture2D white, List<FlatRow> rows, int px, int py)
    {
        var top = py + ListTop;
        var start = (int)Math.Floor(_scroll);
        for (var i = 0; i < VisibleRows; i++)
        {
            var idx = start + i;
            if (idx >= rows.Count) break;
            var row = rows[idx];
            var y = top + i * RowH;
            if (row.Kind == RowKind.Header)
                DrawHeader(sb, white, row, px, y);
            else
                DrawItem(sb, white, row.Item!, px, y);
        }
    }

    private void DrawHeader(SpriteBatch sb, Texture2D white, FlatRow row, int px, int y)
    {
        var x = px + HeaderIndent;
        // Chevron: right ▶ when collapsed, down ▼ when expanded. Drawn as small filled triangles since
        // the Quest/icon set ships dots/Q-marks but not directional chevrons at this image path.
        DrawChevron(sb, white, new Vector2(x, y + 5), expanded: !row.Collapsed);
        var label = $"{row.GroupLabel} ({row.ItemCount})";
        _font?.Draw(sb, label, new Vector2(x + 14, y + 4), new Color(35, 30, 25));
    }

    private static void DrawChevron(SpriteBatch sb, Texture2D white, Vector2 p, bool expanded)
    {
        // Simple 8x8 filled triangle of 1px rects — robust + lightweight.
        if (expanded)
            for (var i = 0; i < 5; i++) sb.Draw(white, new Rectangle((int)p.X + i, (int)p.Y + i, 9 - 2 * i, 1), new Color(60, 60, 70));
        else
            for (var i = 0; i < 5; i++) sb.Draw(white, new Rectangle((int)p.X + i, (int)p.Y + i, 1, 9 - 2 * i), new Color(60, 60, 70));
    }

    private void DrawItem(SpriteBatch sb, Texture2D white, QuestEntry q, int px, int y)
    {
        var rowW = ListRight - RowIndent - 4;
        var rowR = new Rectangle(px + RowIndent, y, rowW, RowH - 2);
        if (q.Id == _selected) sb.Draw(white, rowR, new Color(110, 150, 220, 70));

        // Bullet: yellow (icon0/1) for available, green (icon2-ish) for complete, gold otherwise.
        var bullet = q.Complete ? _bulletIcons[7] : q.Available ? _bulletIcons[1] : _bulletIcons[0];
        if (bullet?.Texture is { } tex)
            sb.Draw(tex, new Vector2(rowR.X + 2, rowR.Y + (RowH - bullet.Height) / 2f - 1), Color.White);

        // Quest name: single-line, ellipsis-truncated.
        var name = q.Name;
        if (_font != null)
        {
            var textX = rowR.X + 20;
            var maxW = rowR.Right - textX - 2;
            var shown = _font.TruncateToWidth(name, maxW);
            _font.Draw(sb, shown, new Vector2(textX, rowR.Y + 4), new Color(35, 30, 25));
        }
    }

    private void DrawScrollbar(SpriteBatch sb, Texture2D white, int totalRows, int px, int py)
    {
        if (totalRows <= VisibleRows) return;
        var trackTop = py + ListTop;
        var trackBot = py + ListBottom;
        var trackX = px + ScrollGutterX;
        // Track + 1-px border.
        sb.Draw(white, new Rectangle(trackX, trackTop, ScrollW, trackBot - trackTop), new Color(180, 170, 145, 120));
        // Thumb proportional to viewport size.
        var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleRows / totalRows);
        var span = trackBot - trackTop - thumbH;
        var frac = _scroll / Math.Max(1, totalRows - VisibleRows);
        var ty = trackTop + (int)(span * Math.Clamp(frac, 0f, 1f));
        sb.Draw(white, new Rectangle(trackX + 1, ty, ScrollW - 2, thumbH), new Color(95, 110, 130));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (_btMyLevel?.HandleMouseButton(x, y, down) == true) return true;
        if (_btAllLevel?.HandleMouseButton(x, y, down) == true) return true;
        if (_btIconInfo?.HandleMouseButton(x, y, down) == true) return true;
        if (!down) return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);

        // Tab switch.
        for (var i = 0; i < 4; i++)
            if (new Rectangle((int)Position.X + TabX[i], (int)Position.Y + 25, TabW[i], 22).Contains(x, y))
            { _tab = i; _scroll = 0; _selected = -1; return true; }

        // Scrollbar thumb / track.
        if (HandleScrollbarPress(x, y)) return true;

        // List row hit-test (header toggles collapse; item selects).
        var rows = Flatten();
        var start = (int)Math.Floor(_scroll);
        for (var i = 0; i < VisibleRows; i++)
        {
            var idx = start + i;
            if (idx >= rows.Count) break;
            var rowR = new Rectangle((int)Position.X + ListLeft,
                                     (int)Position.Y + ListTop + i * RowH,
                                     ListRight - ListLeft, RowH - 2);
            if (!rowR.Contains(x, y)) continue;
            var row = rows[idx];
            if (row.Kind == RowKind.Header)
            {
                var set = _collapsedByTab[_tab];
                if (!set.Add(row.GroupLabel)) set.Remove(row.GroupLabel);
                ClampScroll();
                return true;
            }
            _selected = row.Item!.Id;
            OnSelectQuest?.Invoke(_selected);
            return true;
        }

        // Title-strip drag.
        if (new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22).Contains(x, y))
        { _dragging = true; _dragOff = new Vector2(x - Position.X, y - Position.Y); return true; }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    private bool HandleScrollbarPress(int x, int y)
    {
        var rows = Flatten().Count;
        if (rows <= VisibleRows) return false;
        var trackTop = (int)Position.Y + ListTop;
        var trackBot = (int)Position.Y + ListBottom;
        var trackX = (int)Position.X + ScrollGutterX;
        var trackR = new Rectangle(trackX, trackTop, ScrollW, trackBot - trackTop);
        if (!trackR.Contains(x, y)) return false;
        var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleRows / rows);
        var span = trackBot - trackTop - thumbH;
        var frac = _scroll / Math.Max(1, rows - VisibleRows);
        var ty = trackTop + (int)(span * Math.Clamp(frac, 0f, 1f));
        if (y >= ty && y < ty + thumbH)
        {
            _draggingThumb = true;
            _thumbGrabDy = y - ty;
        }
        else
        {
            // Click above/below the thumb pages by a viewport.
            _scroll = y < ty ? _scroll - VisibleRows : _scroll + VisibleRows;
            ClampScroll();
        }
        return true;
    }

    private void DragThumbTo(int my)
    {
        var rows = Flatten().Count;
        if (rows <= VisibleRows) return;
        var trackTop = (int)Position.Y + ListTop;
        var trackBot = (int)Position.Y + ListBottom;
        var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleRows / rows);
        var span = trackBot - trackTop - thumbH;
        if (span <= 0) return;
        var rel = my - _thumbGrabDy - trackTop;
        var frac = Math.Clamp(rel / span, 0f, 1f);
        _scroll = frac * (rows - VisibleRows);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageUp)   { _scroll = Math.Max(0, _scroll - VisibleRows); return true; }
        if (key == Keys.PageDown) { _scroll += VisibleRows; ClampScroll(); return true; }
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
