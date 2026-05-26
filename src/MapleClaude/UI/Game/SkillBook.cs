using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Skill window — the authentic v95 <c>CUISkill</c>, drawn from <c>UIWindow2.img/Skill/main</c>
/// (layered backgrounds 174×281, "SKILL INVENTORY"). Skills are a scrolling list of 140×35 rows
/// (the <c>skill0</c>/<c>skill1</c> row sprites) with the icon, name, level and an SP-up button;
/// 5 job-tier tabs sit at the top. Row geometry is a port of
/// <c>CUISkill::GetSkillIndexFromPoint</c>: row <c>i</c> spans x∈[10,149], top=93+40·i, 34 tall,
/// icon at x=13 (32×32). Double-clicking a learned active skill casts it.
/// </summary>
public sealed class SkillBook : GamePanel
{
    public sealed class SkillEntry
    {
        public int   Id;
        public string Name    = string.Empty;
        public int   Level;
        public int   MaxLevel = 20;
        public bool  Passive;
        public int   MpCost;
        public WzCanvas? IconCanvas;
        internal WzSprite? Icon;
        internal float Cooldown;
        internal float CooldownTotal;
    }

    private readonly List<SkillEntry>       _skills = new();
    private readonly List<List<SkillEntry>> _tabs   = new(5);

    // ── WZ assets (UIWindow2.img/Skill/main) ──────────────────────────────────
    private readonly WzSprite? _bg, _bg2, _bg3;
    private readonly WzSprite? _row0, _row1;
    private readonly WzSprite?[] _tabOn  = new WzSprite?[5];
    private readonly WzSprite?[] _tabOff = new WzSprite?[5];
    private readonly WzSprite?[] _tabIcon = new WzSprite?[5];   // per-tab job "book" icon overlay
    private readonly Button?   _btClose;
    private readonly Button?[] _spUp = new Button?[VisibleRows];
    private readonly List<Button> _allButtons = new();

    private int _activeTab;
    private int _scroll;
    private readonly bool[] _tabEnabled = new bool[5];
    private int[] _roots = [];
    private int _lastTabbedJob = int.MinValue;
    private const int VisibleRows = 4;
    private const int RowTop0 = 93, RowPitch = 40, RowX = 10, RowW = 139, RowH = 34;
    private const int TabX0 = 10, TabY = 27, TabStride = 31, TabW = 30, TabH = 20;
    private const int BookX = 15, BookY = 52;   // top-left mastery "book" icon + name header

    public int SP { get; set; }
    public int JobId { get; set; }

    public Action<int>? OnSkillUp { get; set; }
    public Action<int, int>? OnSkillCast { get; set; }

    /// <summary>True while a learned skill is picked up onto the cursor for binding.</summary>
    public bool IsDraggingSkill => _dragActive && IsVisible;
    /// <summary>The skill id currently held for binding (0 when none).</summary>
    public int DragSkillId => _dragActive ? _dragSkillId : 0;
    /// <summary>The held skill's icon, for the cursor ghost (drawn by the stage on top).</summary>
    public WzSprite? DragIcon => _dragIcon;
    /// <summary>Drop the held skill (after a successful bind, or to cancel).</summary>
    public void CancelSkillDrag() { _dragActive = false; _dragSkillId = 0; _dragIcon = null; _dragFromAbs = -1; }

    /// <summary>Resolves a job root's "book" icon (<c>Skill.wz/{root}.img/info/icon</c>) for the top-left header.</summary>
    public Func<int, WzCanvas?>? BookIconResolver { get; set; }

    /// <summary>Resolves a job root's display name for the top-left book header.</summary>
    public Func<int, string?>? BookNameResolver { get; set; }

    // Sticky drag (mirrors the item inventory): a click picks a learned skill onto the
    // cursor; the next click binds it to a key/quickslot (routed by GameStage) or — a
    // quick re-click on the same row — casts it.
    private bool _dragActive;
    private int _dragSkillId;
    private WzSprite? _dragIcon;
    private int _dragFromAbs = -1;
    private double _pickupTime;

    private readonly BuiltInFont? _font;
    private readonly WzTextureLoader _loader;

    private int PanelW => _bg?.Width ?? 174;
    private int PanelH => _bg?.Height ?? 281;

    public SkillBook(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        _loader = loader;
        IsVisible = false;
        Position = new Vector2(190, 40);

        var main = ui?.GetItem("UIWindow2.img/Skill/main") as WzProperty;
        _bg   = Canvas(main, "backgrnd");
        _bg2  = Canvas(main, "backgrnd2");
        _bg3  = Canvas(main, "backgrnd3");
        _row0 = Canvas(main, "skill0");
        _row1 = Canvas(main, "skill1");

        var tabOn  = (main?.Get("Tab") as WzProperty)?.Get("enabled")  as WzProperty;
        var tabOff = (main?.Get("Tab") as WzProperty)?.Get("disabled") as WzProperty;
        for (var i = 0; i < 5; i++) { _tabOn[i] = Canvas(tabOn, i.ToString()); _tabOff[i] = Canvas(tabOff, i.ToString()); }

        // SP-up buttons (one per visible row) — reuse the legacy +button sprite.
        var legacy = ui?.GetItem("UIWindow.img/Skill") as WzProperty;
        for (var i = 0; i < VisibleRows; i++)
        {
            var row = i;
            if ((legacy?.Get($"BtSpUp{i}") ?? legacy?.Get("BtSpUp")) is WzProperty sp)
            {
                _spUp[i] = new Button(loader, sp) { OnClick = () => LevelUpRow(row) };
                _allButtons.Add(_spUp[i]!);
            }
        }

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
        {
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
            _allButtons.Add(_btClose);
        }

        LoadDefaultSkills();
        RebuildTabs();
    }

    // ── Data ──────────────────────────────────────────────────────────────────
    public void SetSkills(IEnumerable<SkillEntry> skills)
    {
        _skills.Clear();
        _skills.AddRange(skills);
        foreach (var s in _skills)
            if (s.Icon is null && s.IconCanvas is not null) s.Icon = _loader.Load(s.IconCanvas);
        RebuildTabs();
    }

    public void StartCooldown(int skillId, float seconds)
    {
        if (seconds <= 0) return;
        var sk = _skills.Find(s => s.Id == skillId);
        if (sk is null) return;
        sk.Cooldown = seconds;
        sk.CooldownTotal = seconds;
    }

    public bool IsOnCooldown(int skillId) => _skills.Find(s => s.Id == skillId)?.Cooldown > 0;
    public int LevelOf(int skillId) => _skills.Find(s => s.Id == skillId)?.Level ?? 0;

    private void LoadDefaultSkills() => _skills.AddRange(new[]
    {
        new SkillEntry { Id = 1000, Name = "Three Snails", MaxLevel = 3 },
        new SkillEntry { Id = 1001, Name = "Recovery",     MaxLevel = 3 },
        new SkillEntry { Id = 1002, Name = "Nimble Feet",  MaxLevel = 3 },
    });

    // Group skills onto the 5 job-tier tabs by their root (skillId / 10000),
    // mapped through the job's advancement chain. Tab i shows the skills under
    // root GetSkillRoots(JobId)[i]; tabs past the player's advancement are locked.
    private void RebuildTabs()
    {
        for (var i = 0; i < 5; i++) { if (i < _tabs.Count) _tabs[i] = new(); else _tabs.Add(new()); }

        var roots = JobConstants.GetSkillRoots(JobId);
        _roots = roots;
        for (var i = 0; i < 5; i++)
        {
            _tabEnabled[i] = i < roots.Length;
            var canvas = _tabEnabled[i] ? BookIconResolver?.Invoke(roots[i]) : null;
            _tabIcon[i] = canvas != null ? _loader.Load(canvas) : null;
        }

        foreach (var s in _skills)
        {
            var tab = Array.IndexOf(roots, s.Id / 10000);
            if (tab >= 0 && tab < 5) _tabs[tab].Add(s);
        }

        // On a job change, jump to the highest unlocked tab (the just-reached
        // advancement, like the original CUISkill). On same-job rebuilds (skill
        // deltas) keep the player's current tab; only fix it if it became locked.
        var highest = Math.Max(0, roots.Length - 1);
        if (JobId != _lastTabbedJob)
        {
            _lastTabbedJob = JobId;
            _activeTab = highest;
            _scroll = 0;
        }
        else if (_activeTab >= roots.Length || !_tabEnabled[_activeTab])
        {
            _activeTab = highest;
            _scroll = 0;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, ActiveTab().Count - VisibleRows));
    }

    private List<SkillEntry> ActiveTab() => _activeTab < _tabs.Count ? _tabs[_activeTab] : _tabs[0];

    private void LevelUpRow(int rowIndex)
    {
        var tab = ActiveTab();
        var abs = _scroll + rowIndex;
        if (SP <= 0 || abs >= tab.Count) return;
        var sk = tab[abs];
        if (sk.Level >= sk.MaxLevel) return;
        if (OnSkillUp != null) OnSkillUp(sk.Id);
        else { sk.Level++; SP--; }
    }

    // ── Update ──────────────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        LayoutButtons();
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach (var s in _skills) if (s.Cooldown > 0) s.Cooldown = Math.Max(0, s.Cooldown - dt);

        var tab = ActiveTab();
        var m = Mouse.GetState();
        for (var i = 0; i < VisibleRows; i++)
        {
            if (_spUp[i] is null) continue;
            var abs = _scroll + i;
            _spUp[i]!.Enabled = SP > 0 && abs < tab.Count && tab[abs].Level < tab[abs].MaxLevel;
            _spUp[i]!.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
        }
        _btClose?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    // ── Draw ────────────────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_bg != null) { _bg.Draw(sb, Position); _bg2?.Draw(sb, Position); _bg3?.Draw(sb, Position); }
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 12, 22, 235));
            _font?.Draw(sb, "SKILL INVENTORY", new Vector2(px + 40, py + 5), new Color(220, 200, 150));
        }

        // SP counter (bottom of the window, like the authentic CUISkill).
        var spStr = $"SP: {SP}";
        var spW = _font?.Measure(spStr).X ?? 0f;
        _font?.Draw(sb, spStr, new Vector2(px + (PanelW - spW) / 2f, py + PanelH - 21),
            SP > 0 ? new Color(90, 200, 90) : new Color(150, 150, 150));

        // Job tabs: the active unlocked tab uses the "on" (selected) sprite; others
        // use the "off" sprite. Locked tiers (not yet advanced to) aren't drawn.
        for (var i = 0; i < 5; i++)
        {
            if (!_tabEnabled[i]) continue;
            var spr = i == _activeTab ? _tabOn[i] : _tabOff[i];
            spr?.Draw(sb, Position);
        }

        // Mastery "book" header (top-left): the active tab's job book icon + name.
        var book = _activeTab < _tabIcon.Length ? _tabIcon[_activeTab] : null;
        if (book?.Texture != null)
            sb.Draw(book.Texture, new Rectangle(px + BookX, py + BookY, Math.Min(book.Width, 32), Math.Min(book.Height, 32)), Color.White);
        var activeRoot = _activeTab < _roots.Length ? _roots[_activeTab] : 0;
        var bookName = BookNameResolver?.Invoke(activeRoot);
        if (!string.IsNullOrEmpty(bookName))
            _font?.Draw(sb, bookName, new Vector2(px + BookX + 36, py + BookY + 8), new Color(230, 220, 180));

        // Skill rows.
        var tab = ActiveTab();
        for (var i = 0; i < VisibleRows; i++)
        {
            var abs = _scroll + i;
            if (abs >= tab.Count) break;
            DrawRow(sb, white, tab[abs], px, py + RowTop0 + i * RowPitch, i, abs);
        }

        // Scroll hints.
        if (_scroll > 0)
            _font?.Draw(sb, "▲", new Vector2(px + PanelW / 2f - 4, py + RowTop0 - 12), new Color(200, 200, 220));
        if (_scroll + VisibleRows < tab.Count)
            _font?.Draw(sb, "▼", new Vector2(px + PanelW / 2f - 4, py + RowTop0 + VisibleRows * RowPitch), new Color(200, 200, 220));

        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawRow(SpriteBatch sb, Texture2D white, SkillEntry sk, int px, int rowY, int rowIdx, int abs)
    {
        // Zebra row background (skill0 / skill1).
        var bg = (abs % 2 == 0 ? _row0 : _row1);
        bg?.Draw(sb, new Vector2(px + RowX, rowY));

        // Icon (real Skill.wz icon, else placeholder).
        var iconRect = new Rectangle(px + 13, rowY + 3, 32, 32);
        if (sk.Icon != null)
            sb.Draw(sk.Icon.Texture, new Rectangle(iconRect.X, iconRect.Y, 32, 32), Color.White);
        else
        { sb.Draw(white, iconRect, new Color(40, 40, 60)); DrawBorder(sb, white, iconRect, new Color(80, 80, 100)); }
        if (sk.Cooldown > 0)
        {
            sb.Draw(white, iconRect, new Color(0, 0, 0, 150));
            _font?.Draw(sb, ((int)Math.Ceiling(sk.Cooldown)).ToString(), new Vector2(iconRect.X + 8, iconRect.Y + 8), new Color(255, 220, 120));
        }

        _font?.Draw(sb, sk.Name, new Vector2(px + 50, rowY + 2), sk.Passive ? new Color(120, 110, 90) : new Color(40, 36, 30));
        var lv = sk.MpCost > 0 && !sk.Passive ? $"{sk.Level}/{sk.MaxLevel}   {sk.MpCost} MP" : $"{sk.Level}/{sk.MaxLevel}";
        _font?.Draw(sb, lv, new Vector2(px + 50, rowY + 18), sk.Level >= sk.MaxLevel ? new Color(170, 130, 40) : new Color(70, 110, 70));

        if (_spUp[rowIdx] != null) _spUp[rowIdx]!.Position = new Vector2(px + PanelW - 26, rowY + 8);
    }

    // ── Input ────────────────────────────────────────────────────────────────
    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons) if (b?.HandleMouseButton(x, y, down) == true) return true;
        if (!down) return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);

        // Tab switch (locked tiers ignore clicks).
        for (var i = 0; i < 5; i++)
            if (_tabEnabled[i] && new Rectangle((int)Position.X + TabX0 + i * TabStride, (int)Position.Y + TabY, TabW, TabH).Contains(x, y))
            { _activeTab = i; _scroll = 0; return true; }

        // Resolve a held skill: a quick re-click on the same row casts it; anything else
        // inside the window just puts it back. (Binds onto a key/quickslot are intercepted
        // by GameStage before the click reaches here.)
        if (_dragActive)
        {
            var hitAbs = RowAt(x, y);
            var dtab = ActiveTab();
            if (hitAbs == _dragFromAbs && hitAbs >= 0 && hitAbs < dtab.Count
                && Environment.TickCount64 / 1000.0 - _pickupTime < 0.4)
            {
                var sk = dtab[hitAbs];
                if (!sk.Passive && sk.Level > 0 && sk.Cooldown <= 0) OnSkillCast?.Invoke(sk.Id, sk.Level);
            }
            CancelSkillDrag();
            return true;
        }

        // Pick a learned active skill up onto the cursor (ghost follows; drop on a key or
        // quickslot to bind, or re-click here to cast). Passive/unlearned rows aren't grabbable.
        var pickAbs = RowAt(x, y);
        if (pickAbs >= 0)
        {
            var ptab = ActiveTab();
            if (pickAbs < ptab.Count)
            {
                var sk = ptab[pickAbs];
                if (!sk.Passive && sk.Level > 0)
                {
                    _dragActive = true; _dragSkillId = sk.Id; _dragIcon = sk.Icon;
                    _dragFromAbs = pickAbs; _pickupTime = Environment.TickCount64 / 1000.0;
                }
            }
            return true;
        }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageDown) { _scroll = Math.Min(_scroll + VisibleRows, Math.Max(0, ActiveTab().Count - VisibleRows)); return true; }
        if (key == Keys.PageUp)   { _scroll = Math.Max(0, _scroll - VisibleRows); return true; }
        return false;
    }

    // The absolute skill index under a screen point, or -1.
    private int RowAt(int x, int y)
    {
        var t = ActiveTab();
        for (var i = 0; i < VisibleRows; i++)
        {
            var abs = _scroll + i;
            if (abs >= t.Count) break;
            if (new Rectangle((int)Position.X + RowX, (int)Position.Y + RowTop0 + i * RowPitch, RowW, RowH).Contains(x, y)) return abs;
        }
        return -1;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void LayoutButtons()
    {
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 6);
    }

    private WzSprite? Canvas(WzProperty? root, string name) =>
        root?.Get(name) is WzCanvas c ? _loader.Load(c) : null;

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
