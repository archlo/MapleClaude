using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Skill book panel. Toggle with K.
/// Shows up to 12 skill rows per tab (5 tabs for job levels).
/// Each row: icon, skill name, current/max level, SP-up button.
/// SP counter shown at top. Scroll to see more than 12 skills.
///
/// WZ: UIWindow.img/Skill/
///   backgrnd — panel background
///   BtClose / BtHyper / BtGuildSkill / BtRide / BtMacro
///   tab/enabled/N — tab buttons
///   BtSpUp0..11   — per-row SP buttons
/// </summary>
public sealed class SkillBook : GamePanel
{
    // ── Data model ──────────────────────────────────────────────────────────
    public sealed class SkillEntry
    {
        public int   Id;
        public string Name    = string.Empty;
        public int   Level;
        public int   MaxLevel = 20;
        public bool  Passive;
        public int   MpCost;
        /// <summary>Skill.wz icon canvas (loaded into <see cref="Icon"/> by the panel).</summary>
        public WzCanvas? IconCanvas;
        internal WzSprite? Icon;
        internal float Cooldown;       // seconds remaining
        internal float CooldownTotal;  // seconds (for the sweep overlay)
    }

    private readonly List<SkillEntry>   _skills     = new();
    private readonly List<List<SkillEntry>> _tabs   = new(5);

    // ── UI ──────────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly WzSprite? _spBackgrnd;
    private readonly Button?   _btClose;
    private readonly Button?[] _tabBtns  = new Button?[5];
    private readonly Button?[] _spUpBtns = new Button?[12];
    private readonly List<Button> _allButtons = new();

    // ── State ────────────────────────────────────────────────────────────────
    private int _activeTab;
    private int _scrollOffset;
    private const int Rows = 12;
    private const int RowH = 32;

    // Stats (wired by GameStage)
    public int SP { get; set; } = 0;
    public int JobId { get; set; } = 0; // 0 = Beginner

    /// <summary>Fired when the player clicks a row's SP-up button (server-authoritative
    /// when set — the panel does not increment locally). Arg = skill id.</summary>
    public Action<int>? OnSkillUp { get; set; }

    /// <summary>Fired on double-click of a learned, active skill row. Arg = (skillId, level).</summary>
    public Action<int, int>? OnSkillCast { get; set; }

    private double _lastClickTime;
    private int _lastClickRow = -1;

    private readonly BuiltInFont? _font;
    private readonly WzTextureLoader _loader;

    // Panel geometry
    private const int PanelW = 172;
    private const int PanelH = 480;
    private const int ListX  = 6;
    private const int ListY  = 58;

    public SkillBook(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        _loader = loader;
        IsVisible = false;
        Position = new Vector2(190, 40);

        var skill = ui?.GetItem("UIWindow.img/Skill") as WzProperty;
        _background = skill?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;
        _spBackgrnd = skill?.Get("sp_backgrnd") is WzCanvas sc ? loader.Load(sc) : null;

        _btClose = MakeBtn(loader, skill, "BtClose", () => IsVisible = false);

        var tabEnabled = (skill?.Get("Tab") as WzProperty)?.Get("enabled") as WzProperty;
        for (var i = 0; i < 5; i++)
        {
            var idx = i;
            var tabRoot = tabEnabled?.Get($"{i}") as WzProperty;
            if (tabRoot != null)
            {
                _tabBtns[i] = new Button(loader, tabRoot)
                {
                    OnClick = () => { _activeTab = idx; _scrollOffset = 0; },
                };
                _allButtons.Add(_tabBtns[i]!);
            }
        }

        for (var i = 0; i < 12; i++)
        {
            var row = i;
            var spRoot = skill?.Get($"BtSpUp{i}") as WzProperty
                      ?? skill?.Get("BtSpUp") as WzProperty;
            if (spRoot != null)
            {
                _spUpBtns[i] = new Button(loader, spRoot) { OnClick = () => LevelUpRow(row) };
                _allButtons.Add(_spUpBtns[i]!);
            }
        }

        LoadDefaultSkills();
        RebuildTabs();
        LayoutButtons();
    }

    // ── Skill data ───────────────────────────────────────────────────────────

    public void SetSkills(IEnumerable<SkillEntry> skills)
    {
        _skills.Clear();
        _skills.AddRange(skills);
        foreach (var s in _skills)
        {
            if (s.Icon is null && s.IconCanvas is not null) s.Icon = _loader.Load(s.IconCanvas);
        }
        RebuildTabs();
    }

    /// <summary>Start a cooldown on a skill (seconds). Blocks re-cast + shows a sweep
    /// overlay until it elapses. A zero/negative duration is a no-op.</summary>
    public void StartCooldown(int skillId, float seconds)
    {
        if (seconds <= 0) return;
        var sk = _skills.Find(s => s.Id == skillId);
        if (sk is null) return;
        sk.Cooldown = seconds;
        sk.CooldownTotal = seconds;
    }

    /// <summary>True if the skill is currently cooling down.</summary>
    public bool IsOnCooldown(int skillId) => _skills.Find(s => s.Id == skillId)?.Cooldown > 0;

    private void LoadDefaultSkills()
    {
        // Beginner default skills
        _skills.AddRange(new[]
        {
            new SkillEntry { Id = 1000000, Name = "Three Snails",  MaxLevel = 3 },
            new SkillEntry { Id = 1000001, Name = "Recovery",      MaxLevel = 3 },
            new SkillEntry { Id = 1000002, Name = "Nimble Feet",   MaxLevel = 3 },
        });
    }

    private void RebuildTabs()
    {
        for (var i = 0; i < 5; i++)
        {
            if (i < _tabs.Count) _tabs[i] = new List<SkillEntry>();
            else _tabs.Add(new List<SkillEntry>());
        }
        // Tab 0 = beginner / all for now
        foreach (var s in _skills) _tabs[0].Add(s);
    }

    private void LevelUpRow(int rowIndex)
    {
        if (SP <= 0) return;
        var tab = _activeTab < _tabs.Count ? _tabs[_activeTab] : _tabs[0];
        var abs = _scrollOffset + rowIndex;
        if (abs >= tab.Count) return;
        var sk = tab[abs];
        if (sk.Level >= sk.MaxLevel) return;
        if (OnSkillUp != null)
        {
            // Server-authoritative: request the up; ChangeSkillRecordResult applies it.
            OnSkillUp(sk.Id);
        }
        else
        {
            // Offline / demo fallback.
            sk.Level++;
            SP--;
        }
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        LayoutButtons();
        // Tick skill cooldowns.
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach (var s in _skills)
        {
            if (s.Cooldown > 0) s.Cooldown = Math.Max(0, s.Cooldown - dt);
        }
        // Enable SP-up buttons only when SP > 0 and skill not maxed
        var tab   = ActiveTab();
        for (var i = 0; i < 12; i++)
        {
            if (_spUpBtns[i] is null) continue;
            var abs = _scrollOffset + i;
            var canUp = SP > 0 && abs < tab.Count && tab[abs].Level < tab[abs].MaxLevel;
            _spUpBtns[i]!.Enabled = canUp;
        }
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        // Background
        if (_background != null)
            _background.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 12, 22, 235));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH));
        }

        // Title
        _font?.Draw(sb, "Skills", new Vector2(px + 66, py + 5), new Color(220, 200, 150));

        // SP counter
        var spStr = $"SP: {SP}";
        var spSz  = _font?.Measure(spStr) ?? Vector2.Zero;
        _font?.Draw(sb, spStr, new Vector2(px + PanelW - (int)spSz.X - 4, py + 5),
            SP > 0 ? new Color(100, 220, 100) : new Color(160, 160, 160));

        // Tab buttons row
        for (var i = 0; i < 5; i++)
        {
            var tx = px + 4 + i * 32;
            var tabRect = new Rectangle(tx, py + 20, 30, 14);
            sb.Draw(white, tabRect, i == _activeTab ? new Color(50, 50, 80) : new Color(25, 25, 40));
            _font?.Draw(sb, $"T{i + 1}", new Vector2(tx + 5, py + 22),
                i == _activeTab ? Color.White : new Color(140, 140, 140));
            _tabBtns[i]?.Draw(sb);
        }

        // Skill rows
        var tab = ActiveTab();
        var clip = new Rectangle(px, py + ListY - 2, PanelW, Rows * RowH + 4);
        for (var i = 0; i < Rows; i++)
        {
            var abs = _scrollOffset + i;
            if (abs >= tab.Count) break;
            DrawSkillRow(sb, white, tab[abs], px, py + ListY + i * RowH, i);
        }

        // Scroll indicators
        if (_scrollOffset > 0)
            DrawScrollArrow(sb, white, new Rectangle(px + PanelW / 2 - 5, py + ListY - 10, 10, 8), up: true);
        if (_scrollOffset + Rows < tab.Count)
            DrawScrollArrow(sb, white, new Rectangle(px + PanelW / 2 - 5, py + ListY + Rows * RowH + 2, 10, 8), up: false);

        // Close + SP-up buttons
        _btClose?.Draw(sb);
        for (var i = 0; i < 12; i++) _spUpBtns[i]?.Draw(sb);
    }

    private void DrawSkillRow(SpriteBatch sb, Texture2D white,
        SkillEntry sk, int px, int rowY, int rowIdx)
    {
        // Icon (real Skill.wz icon when available, else a placeholder box)
        var iconRect = new Rectangle(px + 4, rowY + 4, 24, 24);
        if (sk.Icon is not null)
            sb.Draw(sk.Icon.Texture, new Rectangle(iconRect.X, iconRect.Y, 24, 24), Color.White);
        else
            sb.Draw(white, iconRect, new Color(40, 40, 60));
        DrawBorder(sb, white, iconRect, new Color(80, 80, 100));

        // Cooldown overlay (darken the icon + show remaining seconds).
        if (sk.Cooldown > 0)
        {
            sb.Draw(white, iconRect, new Color(0, 0, 0, 150));
            _font?.Draw(sb, ((int)Math.Ceiling(sk.Cooldown)).ToString(),
                new Vector2(iconRect.X + 6, iconRect.Y + 6), new Color(255, 220, 120));
        }

        // Skill name (dimmed for passive skills, which can't be cast)
        _font?.Draw(sb, sk.Name, new Vector2(px + 32, rowY + 4),
            sk.Passive ? new Color(150, 150, 170) : Color.White);

        // Level (+ MP cost for active skills)
        var lvStr = sk.MpCost > 0 && !sk.Passive
            ? $"{sk.Level}/{sk.MaxLevel}  {sk.MpCost} MP"
            : $"{sk.Level}/{sk.MaxLevel}";
        _font?.Draw(sb, lvStr, new Vector2(px + 32, rowY + 16),
            sk.Level >= sk.MaxLevel ? new Color(200, 180, 80) : new Color(160, 200, 160));

        // SP-up button position
        if (_spUpBtns[rowIdx] != null)
            _spUpBtns[rowIdx]!.Position = new Vector2(px + PanelW - 22, rowY + 8);
    }

    private void DrawScrollArrow(SpriteBatch sb, Texture2D white, Rectangle r, bool up)
    {
        sb.Draw(white, r, new Color(80, 80, 100, 180));
        var tip  = up ? new Vector2(r.X + r.Width / 2f, r.Y)
                       : new Vector2(r.X + r.Width / 2f, r.Bottom);
        _font?.Draw(sb, up ? "^" : "v", tip + new Vector2(-4, -2), new Color(180, 180, 200));
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b?.HandleMouseButton(x, y, down) == true) return true;
        _btClose?.HandleMouseButton(x, y, down);

        // Double-click a learned, active skill row → cast it.
        if (down)
        {
            var tab = ActiveTab();
            for (var i = 0; i < Rows; i++)
            {
                var abs = _scrollOffset + i;
                if (abs >= tab.Count) break;
                var rowRect = new Rectangle((int)Position.X + ListX, (int)Position.Y + ListY + i * RowH, PanelW - ListX * 2, RowH);
                if (!rowRect.Contains(x, y)) continue;
                var now = Environment.TickCount64 / 1000.0;
                if (_lastClickRow == abs && now - _lastClickTime < 0.4)
                {
                    var sk = tab[abs];
                    if (!sk.Passive && sk.Level > 0 && sk.Cooldown <= 0)
                    {
                        OnSkillCast?.Invoke(sk.Id, sk.Level);
                    }
                    _lastClickRow = -1;
                }
                else
                {
                    _lastClickRow = abs;
                    _lastClickTime = now;
                }
                return true;
            }
        }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageDown) { _scrollOffset = Math.Min(_scrollOffset + Rows, Math.Max(0, ActiveTab().Count - Rows)); return true; }
        if (key == Keys.PageUp)   { _scrollOffset = Math.Max(0, _scrollOffset - Rows); return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private List<SkillEntry> ActiveTab() =>
        _activeTab < _tabs.Count ? _tabs[_activeTab] : _tabs[0];

    private void LayoutButtons()
    {
        if (_btClose != null)
            _btClose.Position = Position + new Vector2(PanelW - 18, 4);
    }

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r)
        => DrawBorder(sb, white, r, new Color(70, 70, 90));

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
