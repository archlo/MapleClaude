using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Community window — the authentic v95 <c>CUIUserList</c> "Main" panel, drawn from
/// <c>UIWindow2.img/UserList/Main</c> (layered frame 264×382, baked "MAPLE USER LIST &amp; GUILD").
/// Six baked tabs: <b>Friend</b>, <b>Party</b>, <b>Guild</b>, <b>Alliance</b> (Union), Expedition,
/// <b>Blacklist</b> — tab sprites <c>Main/Tab/enabled|disabled/0..5</c> at x={9,40,71,112,143,194} y=25.
///
/// Friend / Party / Guild are wired to the real protocol (add/delete buddy, party create/invite/kick/
/// leave, guild withdraw) via callbacks the stage maps to senders; Alliance chat is via <c>/a</c>;
/// the Blacklist is client-local (the Kinoko build has no block opcode). A shared name <see cref="TextField"/>
/// at the bottom feeds the add/invite/block actions.
/// </summary>
public sealed class UserList : GamePanel
{
    public enum Tab { Friend = 0, Party = 1, Guild = 2, Alliance = 3, Expedition = 4, Blacklist = 5 }

    private static readonly int[] TabX = { 9, 40, 71, 112, 143, 194 };
    private static readonly int[] TabW = { 30, 30, 40, 30, 50, 59 };
    private const int TabY = 25, TabH = 19;

    // ── Entry types ─────────────────────────────────────────────────────────────
    public sealed class FriendEntry
    {
        public int    FriendId;
        public string Name     = string.Empty;
        public string Location = "Online";
        public int    Level    = 1;
        public string Job      = "Beginner";
        public bool   Online   = true;
    }
    public sealed class PartyEntry
    {
        public int    CharId;
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

    // ── Data ────────────────────────────────────────────────────────────────────
    private readonly List<FriendEntry> _friends   = new();
    private readonly List<PartyEntry>  _party     = new();
    private readonly List<GuildEntry>  _guild      = new();
    private readonly List<GuildEntry>  _alliance   = new();
    private readonly List<string>      _blacklist  = new();
    private string _guildName    = string.Empty;
    private string _allianceName = string.Empty;

    private int _selFriend = -1;   // index into _friends
    private int _selParty  = -1;
    private int _selBlack  = -1;

    // ── WZ assets ───────────────────────────────────────────────────────────────
    private readonly WzSprite? _bg, _bg2;
    private readonly WzSprite?[] _tabOn  = new WzSprite?[6];
    private readonly WzSprite?[] _tabOff = new WzSprite?[6];
    private readonly Button? _btClose;
    private readonly TextField _nameField = new() { Width = 150, Height = 18, MaxLength = 13 };
    private readonly BuiltInFont? _font;

    private Tab  _activeTab = Tab.Friend;
    private int  _scroll;
    private bool _dragging;
    private Vector2 _dragOff;

    // ── Callbacks (wired by GameStage → senders) ──────────────────────────────────
    public Action<string>? OnAddFriend;       // name
    public Action<int>?    OnDeleteFriend;    // friendId
    public Action<string>? OnPartyInvite;     // name (also used by Friend → invite-to-party)
    public Action<int>?    OnPartyKick;       // charId
    public Action?         OnPartyCreate;
    public Action?         OnPartyLeave;
    public Action?         OnGuildLeave;
    public Action<int>?    OnGroupChatHint;   // group type 0=buddy 1=party 2=guild 3=alliance

    private const int ListX = 12, ListW = 240, EntryH = 30;
    private int ListTop => 58;
    private int ListBottom => PanelH - 56;
    private int VisibleRows => Math.Max(1, (ListBottom - ListTop) / EntryH);

    private int PanelW => _bg?.Width ?? 264;
    private int PanelH => _bg?.Height ?? 382;

    public UserList(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(560, 70);
        _nameField.Font = font;
        _nameField.TextColor = new Color(40, 36, 30);
        _nameField.CaretColor = new Color(40, 36, 30);

        var main = ui?.GetItem("UIWindow2.img/UserList/Main") as WzProperty;
        _bg  = Canvas(loader, main, "backgrnd");
        _bg2 = Canvas(loader, main, "backgrnd2");
        var on  = (main?.Get("Tab") as WzProperty)?.Get("enabled")  as WzProperty;
        var off = (main?.Get("Tab") as WzProperty)?.Get("disabled") as WzProperty;
        for (var i = 0; i < 6; i++) { _tabOn[i] = Canvas(loader, on, i.ToString()); _tabOff[i] = Canvas(loader, off, i.ToString()); }

        if (ui?.GetItem("Basic.img/BtClose3") is WzProperty close)
            _btClose = new Button(loader, close) { OnClick = () => IsVisible = false };
    }

    // ── Data API (called by GameStage) ────────────────────────────────────────────
    public void AddFriend(FriendEntry e) => _friends.Add(e);
    public void ClearFriends() { _friends.Clear(); _scroll = 0; _selFriend = -1; }
    public void SetParty(IEnumerable<PartyEntry> p) { _party.Clear(); _party.AddRange(p); _selParty = -1; }
    public void SetGuild(string name, IEnumerable<GuildEntry> g) { _guildName = name; _guild.Clear(); _guild.AddRange(g); }
    public void SetAlliance(string name, IEnumerable<GuildEntry> a) { _allianceName = name; _alliance.Clear(); _alliance.AddRange(a); }

    /// <summary>Whether the inline name field currently owns keyboard focus (so the stage routes
    /// text input here instead of to chat/hotkeys).</summary>
    public bool WantsTextInput => _nameField.IsFocused;
    public override void OnTextInput(char ch) => _nameField.OnTextInput(ch);

    // ── Update ────────────────────────────────────────────────────────────────────
    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        if (_btClose != null) _btClose.Position = Position + new Vector2(PanelW - 18, 4);
        _nameField.Position = Position + new Vector2(ListX, PanelH - 26);
        _nameField.Update(gt);
        var m = Mouse.GetState();
        if (_dragging)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _dragOff;
            else _dragging = false;
        }
        _btClose?.Update(m.X, m.Y, m.LeftButton == ButtonState.Pressed);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_bg != null) { _bg.Draw(sb, Position); _bg2?.Draw(sb, Position); }
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(12, 14, 24, 240));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(60, 65, 100));
            _font?.Draw(sb, "MAPLE USER LIST", new Vector2(px + 70, py + 5), new Color(220, 200, 150));
        }

        for (var i = 0; i < 6; i++)
        {
            var spr = i == (int)_activeTab ? _tabOn[i] : _tabOff[i];
            if (spr != null) spr.Draw(sb, Position);
        }

        switch (_activeTab)
        {
            case Tab.Friend:    DrawFriends(sb, white, px, py); break;
            case Tab.Party:     DrawParty(sb, white, px, py); break;
            case Tab.Guild:     DrawGuildList(sb, white, px, py, _guildName, _guild, "Not in a guild."); break;
            case Tab.Alliance:  DrawGuildList(sb, white, px, py, _allianceName, _alliance, "Not in an alliance.  Chat: /a"); break;
            case Tab.Expedition: _font?.Draw(sb, "Expedition is not available", new Vector2(px + 40, py + 150), new Color(140, 140, 150)); break;
            case Tab.Blacklist: DrawBlacklist(sb, white, px, py); break;
        }

        DrawActionBar(sb, white);
        if (NeedsNameField()) _nameField.Draw(sb, white);
        _btClose?.Draw(sb);
    }

    private void DrawFriends(SpriteBatch sb, Texture2D white, int px, int py)
    {
        var online = _friends.Count(f => f.Online);
        _font?.Draw(sb, $"Buddies  {online}/{_friends.Count}", new Vector2(px + ListX, py + 44), new Color(90, 150, 90));
        DrawRows(sb, white, _friends.Count, _selFriend, px, py, (i, ry, sel) =>
        {
            var f = _friends[i];
            sb.Draw(white, new Rectangle(px + ListX + 2, ry + 8, 8, 8), f.Online ? new Color(80, 200, 80) : new Color(120, 120, 120));
            _font?.Draw(sb, f.Name, new Vector2(px + ListX + 16, ry + 3), f.Online ? new Color(40, 36, 30) : new Color(120, 120, 120));
            _font?.Draw(sb, f.Online ? f.Location : "Offline", new Vector2(px + ListX + 16, ry + 16), new Color(120, 130, 160));
            if (f.Level > 0) _font?.Draw(sb, $"Lv.{f.Level} {f.Job}", new Vector2(px + ListW - 84, ry + 16), new Color(150, 140, 110));
        });
    }

    private void DrawParty(SpriteBatch sb, Texture2D white, int px, int py)
    {
        if (_party.Count == 0) { _font?.Draw(sb, "Not in a party.", new Vector2(px + 60, py + 150), new Color(120, 120, 130)); return; }
        DrawRows(sb, white, _party.Count, _selParty, px, py, (i, ry, sel) =>
        {
            var p = _party[i];
            _font?.Draw(sb, $"Lv.{p.Level} {p.Name}", new Vector2(px + ListX + 4, ry + 2), new Color(40, 36, 30));
            _font?.Draw(sb, p.Job, new Vector2(px + ListX + 4, ry + 15), new Color(120, 130, 160));
            var hp = new Rectangle(px + ListW - 90, ry + 16, 84, 7);
            sb.Draw(white, hp, new Color(0, 0, 0, 120));
            sb.Draw(white, new Rectangle(hp.X, hp.Y, hp.Width * Math.Clamp(p.HpPct, 0, 100) / 100, hp.Height), new Color(200, 60, 60));
        });
    }

    private void DrawGuildList(SpriteBatch sb, Texture2D white, int px, int py, string name, List<GuildEntry> list, string empty)
    {
        if (!string.IsNullOrEmpty(name)) _font?.Draw(sb, name, new Vector2(px + ListX, py + 44), new Color(200, 170, 90));
        if (list.Count == 0) { _font?.Draw(sb, empty, new Vector2(px + 36, py + 150), new Color(120, 120, 130)); return; }
        DrawRows(sb, white, list.Count, -1, px, py, (i, ry, sel) =>
        {
            var g = list[i];
            sb.Draw(white, new Rectangle(px + ListX + 2, ry + 8, 8, 8), g.Online ? new Color(80, 200, 80) : new Color(120, 120, 120));
            _font?.Draw(sb, g.Name, new Vector2(px + ListX + 16, ry + 3), g.Online ? new Color(40, 36, 30) : new Color(120, 120, 120));
            _font?.Draw(sb, g.Rank, new Vector2(px + ListX + 16, ry + 16), new Color(160, 140, 90));
        });
    }

    private void DrawBlacklist(SpriteBatch sb, Texture2D white, int px, int py)
    {
        if (_blacklist.Count == 0) { _font?.Draw(sb, "Block list is empty.", new Vector2(px + 50, py + 150), new Color(120, 120, 130)); return; }
        DrawRows(sb, white, _blacklist.Count, _selBlack, px, py, (i, ry, sel) =>
            _font?.Draw(sb, _blacklist[i], new Vector2(px + ListX + 8, ry + 8), new Color(190, 120, 120)));
    }

    private void DrawRows(SpriteBatch sb, Texture2D white, int count, int selected, int px, int py, Action<int, int, bool> drawRow)
    {
        var maxSc = Math.Max(0, count - VisibleRows);
        _scroll = Math.Clamp(_scroll, 0, maxSc);
        for (var r = 0; r < VisibleRows; r++)
        {
            var i = _scroll + r;
            if (i >= count) break;
            var ry = py + ListTop + r * EntryH;
            var rowR = new Rectangle(px + ListX, ry, ListW, EntryH - 2);
            if (i == selected) sb.Draw(white, rowR, new Color(255, 245, 200, 90));
            else if (r % 2 == 1) sb.Draw(white, rowR, new Color(0, 0, 0, 18));
            drawRow(i, ry, i == selected);
        }
        if (_scroll > 0)        _font?.Draw(sb, "▲", new Vector2(px + PanelW / 2f - 4, py + ListTop - 12), new Color(180, 180, 200));
        if (_scroll < maxSc)    _font?.Draw(sb, "▼", new Vector2(px + PanelW / 2f - 4, py + ListBottom), new Color(180, 180, 200));
    }

    // ── Action bar (clean text buttons) ───────────────────────────────────────────
    private (string label, Action act)[] TabButtons() => _activeTab switch
    {
        Tab.Friend => new (string, Action)[]
        {
            ("Add",    () => { var n = TakeName(); if (n.Length > 0) OnAddFriend?.Invoke(n); }),
            ("Invite", () => { if (_selFriend >= 0 && _selFriend < _friends.Count) OnPartyInvite?.Invoke(_friends[_selFriend].Name); }),
            ("Delete", () => { if (_selFriend >= 0 && _selFriend < _friends.Count) OnDeleteFriend?.Invoke(_friends[_selFriend].FriendId); }),
            ("Chat",   () => OnGroupChatHint?.Invoke(0)),
        },
        Tab.Party => new (string, Action)[]
        {
            ("Create", () => OnPartyCreate?.Invoke()),
            ("Invite", () => { var n = TakeName(); if (n.Length > 0) OnPartyInvite?.Invoke(n); }),
            ("Kick",   () => { if (_selParty >= 0 && _selParty < _party.Count) OnPartyKick?.Invoke(_party[_selParty].CharId); }),
            ("Leave",  () => OnPartyLeave?.Invoke()),
            ("Chat",   () => OnGroupChatHint?.Invoke(1)),
        },
        Tab.Guild => new (string, Action)[]
        {
            ("Leave", () => OnGuildLeave?.Invoke()),
            ("Chat",  () => OnGroupChatHint?.Invoke(2)),
        },
        Tab.Alliance => new (string, Action)[]
        {
            ("Chat", () => OnGroupChatHint?.Invoke(3)),
        },
        Tab.Blacklist => new (string, Action)[]
        {
            ("Block",   () => { var n = TakeName(); if (n.Length > 0 && !_blacklist.Contains(n)) _blacklist.Add(n); }),
            ("Unblock", () => { if (_selBlack >= 0 && _selBlack < _blacklist.Count) { _blacklist.RemoveAt(_selBlack); _selBlack = -1; } }),
        },
        _ => Array.Empty<(string, Action)>(),
    };

    private bool NeedsNameField() => _activeTab is Tab.Friend or Tab.Party or Tab.Blacklist;

    private string TakeName()
    {
        var n = _nameField.Text.Trim();
        _nameField.Clear();
        return n;
    }

    private const int BtW = 52, BtH = 18, BtGap = 4;

    private Rectangle ActionBtnRect(int i)
    {
        var x = (int)Position.X + ListX + i * (BtW + BtGap);
        var y = (int)Position.Y + PanelH - (NeedsNameField() ? 48 : 26);
        return new Rectangle(x, y, BtW, BtH);
    }

    private void DrawActionBar(SpriteBatch sb, Texture2D white)
    {
        var btns = TabButtons();
        var m = Mouse.GetState();
        for (var i = 0; i < btns.Length; i++)
        {
            var r = ActionBtnRect(i);
            var hover = r.Contains(m.X, m.Y);
            sb.Draw(white, r, hover ? new Color(95, 110, 150) : new Color(60, 70, 100));
            DrawBorder(sb, white, r, new Color(120, 130, 165));
            if (_font != null)
            {
                var sz = _font.Measure(btns[i].label);
                _font.Draw(sb, btns[i].label, new Vector2(r.X + (BtW - sz.X) / 2f, r.Y + (BtH - _font.LineHeight) / 2f + 1), Color.White);
            }
        }
    }

    // ── Input ──────────────────────────────────────────────────────────────────────
    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (NeedsNameField() && _nameField.HandleMouseButton(x, y, down)) return true;
        if (!down) return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);

        // Tab switch.
        for (var i = 0; i < 6; i++)
            if (new Rectangle((int)Position.X + TabX[i], (int)Position.Y + TabY, TabW[i], TabH).Contains(x, y))
            { _activeTab = (Tab)i; _scroll = 0; return true; }

        // Action buttons.
        var btns = TabButtons();
        for (var i = 0; i < btns.Length; i++)
            if (ActionBtnRect(i).Contains(x, y)) { btns[i].act(); return true; }

        // Row selection.
        var count = RowCount();
        for (var r = 0; r < VisibleRows; r++)
        {
            var idx = _scroll + r;
            if (idx >= count) break;
            var ry = (int)Position.Y + ListTop + r * EntryH;
            if (new Rectangle((int)Position.X + ListX, ry, ListW, EntryH - 2).Contains(x, y)) { Select(idx); return true; }
        }

        // Title-strip drag.
        if (new Rectangle((int)Position.X, (int)Position.Y, PanelW, 22).Contains(x, y))
        { _dragging = true; _dragOff = new Vector2(x - Position.X, y - Position.Y); return true; }

        return new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
    }

    private int RowCount() => _activeTab switch
    {
        Tab.Friend => _friends.Count,
        Tab.Party => _party.Count,
        Tab.Guild => _guild.Count,
        Tab.Alliance => _alliance.Count,
        Tab.Blacklist => _blacklist.Count,
        _ => 0,
    };

    private void Select(int idx)
    {
        switch (_activeTab)
        {
            case Tab.Friend: _selFriend = idx; break;
            case Tab.Party:  _selParty = idx; break;
            case Tab.Blacklist: _selBlack = idx; break;
        }
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (_nameField.IsFocused)
        {
            if (key == Keys.Enter)
            {
                var btns = TabButtons();
                if (btns.Length > 0) btns[0].act();   // primary action (Add / Create / Block)
                return true;
            }
            if (key == Keys.Escape) { _nameField.IsFocused = false; return true; }
            _nameField.OnKeyPress(key, Keyboard.GetState());
            return true;
        }
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (key == Keys.PageUp)   { _scroll = Math.Max(0, _scroll - VisibleRows); return true; }
        if (key == Keys.PageDown) { _scroll = Math.Min(Math.Max(0, RowCount() - VisibleRows), _scroll + VisibleRows); return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────
    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? root, string name) =>
        root?.Get(name) is WzCanvas c ? loader.Load(c) : null;

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
