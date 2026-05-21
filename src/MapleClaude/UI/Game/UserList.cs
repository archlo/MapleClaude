using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Friends / Party / Guild list panel.
/// 8 tabs: Friend | Friend(All) | Friend(Online) | Party(Mine) | Party(Search) | Boss | Blacklist | Guild
/// Toggle with key binding or social button in StatusBar.
/// WZ: UIWindow2.img/UserList/
/// </summary>
public sealed class UserList : GamePanel
{
    // ── Tab enum ──────────────────────────────────────────────────────────────
    public enum Tab { Friend, FriendAll, FriendOnline, PartyMine, PartySearch, Boss, Blacklist, Guild }

    private static readonly string[] TabLabels =
        ["Friends", "All", "Online", "My Party", "Find", "Boss", "Block", "Guild"];

    // ── Entry types ───────────────────────────────────────────────────────────
    public sealed class FriendEntry
    {
        public string Name     = string.Empty;
        public string Location = "Online";
        public int    Level    = 1;
        public string Job      = "Beginner";
        public bool   Online   = true;
    }

    public sealed class PartyEntry
    {
        public string Name  = string.Empty;
        public int    Level = 1;
        public string Job   = string.Empty;
        public int    HpPct = 100;
    }

    public sealed class GuildEntry
    {
        public string Name = string.Empty;
        public string Rank = "Member";
        public bool   Online;
    }

    // ── Data ──────────────────────────────────────────────────────────────────
    private readonly List<FriendEntry> _friends    = new();
    private readonly List<PartyEntry>  _party      = new();
    private readonly List<GuildEntry>  _guild      = new();
    private readonly List<string>      _blacklist  = new();

    // ── UI ────────────────────────────────────────────────────────────────────
    private readonly WzSprite? _background;
    private readonly Button?   _btClose;
    private readonly Button?   _btAddFriend;
    private readonly Button?   _btCreateParty;
    private readonly Button?   _btLeaveParty;
    private readonly Button?[] _tabBtns = new Button?[8];
    private readonly List<Button> _allButtons = new();

    private Tab  _activeTab   = Tab.Friend;
    private int  _scroll;
    private bool _dragging;
    private Vector2 _dragOff;

    private const int PanelW   = 200;
    private const int PanelH   = 400;
    private const int TabH     = 20;
    private const int EntryH   = 28;
    private const int ListY    = 30 + TabH;
    private const int ListRows = 11;

    private readonly BuiltInFont? _font;

    public UserList(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(600, 80);

        var ul = ui?.GetItem("UIWindow2.img/UserList") as WzProperty;
        _background   = ul?.Get("backgrnd")      is WzCanvas bc ? loader.Load(bc) : null;

        _btClose       = MakeBtn(loader, ul, "BtClose",       () => IsVisible = false);
        _btAddFriend   = MakeBtn(loader, ul, "BtAddFriend",   () => { });
        _btCreateParty = MakeBtn(loader, ul, "BtPartyCreate", () => { });
        _btLeaveParty  = MakeBtn(loader, ul, "BtPartyLeave",  () => { });

        var tabNode = ul?.Get("tab") as WzProperty;
        for (var i = 0; i < 8; i++)
        {
            var idx = i;
            var pr = (tabNode?.Get($"{i}") as WzProperty);
            if (pr != null)
            {
                _tabBtns[i] = new Button(loader, pr) { OnClick = () => SetTab((Tab)idx) };
                _allButtons.Add(_tabBtns[i]!);
            }
        }

        SeedPlaceholder();
        LayoutButtons();
    }

    // ── Data API ──────────────────────────────────────────────────────────────

    public void AddFriend(FriendEntry e) => _friends.Add(e);
    public void SetParty(IEnumerable<PartyEntry> p) { _party.Clear(); _party.AddRange(p); }
    public void SetGuild(IEnumerable<GuildEntry> g) { _guild.Clear(); _guild.AddRange(g); }

    private void SetTab(Tab t) { _activeTab = t; _scroll = 0; }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        LayoutButtons();
        var mouse = Mouse.GetState();
        var mp = new Vector2(mouse.X, mouse.Y);
        if (_dragging)
        {
            if (mouse.LeftButton == ButtonState.Pressed) Position = mp - _dragOff;
            else _dragging = false;
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

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
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 14, 24, 240));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(60, 65, 100));
        }

        // Title
        sb.Draw(white, new Rectangle(px, py, PanelW, 28), new Color(16, 18, 36));
        _font?.Draw(sb, TabLabels[(int)_activeTab], new Vector2(px + 70, py + 8), new Color(220, 200, 150));

        // Tab strip
        var tabW = PanelW / 8;
        for (var i = 0; i < 8; i++)
        {
            var tx = px + i * tabW;
            var tabR = new Rectangle(tx, py + 28, tabW, TabH);
            var isActive = i == (int)_activeTab;
            sb.Draw(white, tabR, isActive ? new Color(40, 50, 80) : new Color(20, 22, 38));
            DrawBorder(sb, white, tabR, isActive ? new Color(100, 120, 200) : new Color(40, 45, 70));
            _font?.Draw(sb, TabLabels[i][0].ToString(), new Vector2(tx + tabW / 2 - 3, py + 31),
                isActive ? Color.White : new Color(130, 135, 160));
        }

        // Entry list
        DrawList(sb, white, px, py + ListY);

        // Action buttons
        if (_activeTab == Tab.Friend || _activeTab == Tab.FriendAll || _activeTab == Tab.FriendOnline)
            _btAddFriend?.Draw(sb);
        else if (_activeTab == Tab.PartyMine)
        {
            _btCreateParty?.Draw(sb);
            _btLeaveParty?.Draw(sb);
        }

        foreach (var b in _tabBtns) b?.Draw(sb);
        _btClose?.Draw(sb);
    }

    private void DrawList(SpriteBatch sb, Texture2D white, int px, int py)
    {
        switch (_activeTab)
        {
            case Tab.Friend:
            case Tab.FriendAll:
            case Tab.FriendOnline:
                var showOnline = _activeTab == Tab.FriendOnline;
                var friends = showOnline ? _friends.Where(f => f.Online).ToList() : _friends;
                var onlineCount = _friends.Count(f => f.Online);
                _font?.Draw(sb, $"Online: {onlineCount}/{_friends.Count}",
                    new Vector2(px + 4, py), new Color(140, 200, 140));
                DrawFriends(sb, white, friends, px, py + 16);
                break;

            case Tab.PartyMine:
                DrawParty(sb, white, px, py);
                break;

            case Tab.Guild:
                DrawGuild(sb, white, px, py);
                break;

            case Tab.Blacklist:
                DrawSimpleList(sb, white, _blacklist, px, py);
                break;

            default:
                _font?.Draw(sb, "(No data)", new Vector2(px + 60, py + 80), new Color(90, 90, 100));
                break;
        }
    }

    private void DrawFriends(SpriteBatch sb, Texture2D white,
        IList<FriendEntry> list, int px, int py)
    {
        var start = Math.Max(0, Math.Min(_scroll, list.Count - ListRows));
        for (var i = 0; i < ListRows && start + i < list.Count; i++)
        {
            var f = list[start + i];
            var ry = py + i * EntryH;
            var row = new Rectangle(px + 2, ry, PanelW - 4, EntryH - 2);
            sb.Draw(white, row, i % 2 == 0 ? new Color(18, 22, 36) : new Color(22, 26, 42));

            var dotColor = f.Online ? new Color(80, 220, 80) : new Color(120, 120, 120);
            sb.Draw(white, new Rectangle(px + 5, ry + 8, 8, 8), dotColor);

            _font?.Draw(sb, f.Name, new Vector2(px + 18, ry + 4), f.Online ? Color.White : new Color(140, 140, 140));
            _font?.Draw(sb, f.Location, new Vector2(px + 18, ry + 14),  new Color(140, 160, 200));
        }
    }

    private void DrawParty(SpriteBatch sb, Texture2D white, int px, int py)
    {
        if (_party.Count == 0)
        {
            _font?.Draw(sb, "Not in a party.", new Vector2(px + 40, py + 60), new Color(100, 100, 110));
            return;
        }
        for (var i = 0; i < _party.Count && i < 6; i++)
        {
            var p   = _party[i];
            var ry  = py + i * 50;
            var row = new Rectangle(px + 2, ry, PanelW - 4, 46);
            sb.Draw(white, row, i % 2 == 0 ? new Color(18, 22, 36) : new Color(22, 26, 42));
            _font?.Draw(sb, $"Lv.{p.Level} {p.Name}", new Vector2(px + 6, ry + 4), Color.White);
            _font?.Draw(sb, p.Job, new Vector2(px + 6, ry + 16), new Color(160, 180, 220));
            // Mini HP bar
            var hpR = new Rectangle(px + 6, ry + 32, PanelW - 16, 8);
            sb.Draw(white, hpR, new Color(0, 0, 0, 140));
            var fill = new Rectangle(hpR.X, hpR.Y, (int)(hpR.Width * p.HpPct / 100f), hpR.Height);
            sb.Draw(white, fill, new Color(200, 50, 50));
        }
    }

    private void DrawGuild(SpriteBatch sb, Texture2D white, int px, int py)
    {
        if (_guild.Count == 0)
        {
            _font?.Draw(sb, "Not in a guild.", new Vector2(px + 40, py + 60), new Color(100, 100, 110));
            return;
        }
        for (var i = 0; i < Math.Min(_guild.Count, ListRows); i++)
        {
            var g   = _guild[i];
            var ry  = py + i * EntryH;
            sb.Draw(white, new Rectangle(px + 2, ry, PanelW - 4, EntryH - 2),
                i % 2 == 0 ? new Color(18, 22, 36) : new Color(22, 26, 42));
            var dot = g.Online ? new Color(80, 220, 80) : new Color(100, 100, 100);
            sb.Draw(white, new Rectangle(px + 5, ry + 8, 8, 8), dot);
            _font?.Draw(sb, g.Name, new Vector2(px + 18, ry + 4), Color.White);
            _font?.Draw(sb, g.Rank, new Vector2(px + 18, ry + 14), new Color(180, 160, 100));
        }
    }

    private void DrawSimpleList(SpriteBatch sb, Texture2D white, IList<string> list, int px, int py)
    {
        for (var i = 0; i < Math.Min(list.Count, ListRows); i++)
            _font?.Draw(sb, list[i], new Vector2(px + 6, py + i * EntryH + 6), new Color(200, 150, 150));
        if (list.Count == 0)
            _font?.Draw(sb, "(Empty)", new Vector2(px + 60, py + 60), new Color(90, 90, 100));
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        foreach (var b in _allButtons)
            if (b?.HandleMouseButton(x, y, down) == true) return true;
        _btAddFriend?.HandleMouseButton(x, y, down);
        _btCreateParty?.HandleMouseButton(x, y, down);
        _btLeaveParty?.HandleMouseButton(x, y, down);

        var titleBar = new Rectangle((int)Position.X, (int)Position.Y, PanelW, 28);
        if (down && titleBar.Contains(x, y))
        {
            _dragging = true;
            _dragOff = new Vector2(x - Position.X, y - Position.Y);
        }
        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void LayoutButtons()
    {
        if (_btClose       != null) _btClose.Position       = Position + new Vector2(PanelW - 18, 4);
        if (_btAddFriend   != null) _btAddFriend.Position   = Position + new Vector2(4, PanelH - 24);
        if (_btCreateParty != null) _btCreateParty.Position = Position + new Vector2(4, PanelH - 24);
        if (_btLeaveParty  != null) _btLeaveParty.Position  = Position + new Vector2(80, PanelH - 24);
    }

    private void SeedPlaceholder()
    {
        _friends.Add(new FriendEntry { Name = "Artale",  Level = 55, Job = "Wizard",   Location = "Ludus Lake",     Online = true  });
        _friends.Add(new FriendEntry { Name = "Broa",    Level = 30, Job = "Swordman", Location = "Perion",         Online = true  });
        _friends.Add(new FriendEntry { Name = "Scania",  Level = 72, Job = "Bowman",   Location = "Aqua Road",      Online = false });
        _friends.Add(new FriendEntry { Name = "Windia",  Level = 14, Job = "Beginner", Location = "Maple Island",   Online = false });
        _party.Add  (new PartyEntry  { Name = "Hero",    Level = 1,  Job = "Beginner", HpPct = 100 });
        _guild.Add  (new GuildEntry  { Name = "MapleGuild", Rank = "Jr. Master", Online = true });
    }

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
