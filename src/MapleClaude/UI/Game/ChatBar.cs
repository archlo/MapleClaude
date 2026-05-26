using System.Text;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game chat — an authentic rebuild of the v95 <c>CUIStatusBar</c> chat box, drawn from
/// <c>UI.wz/StatusBar2.img</c>. It owns the chat backgrounds (the bar no longer draws them) plus
/// the interactive pieces the original client builds:
/// <list type="bullet">
/// <item>a <b>chat-target dropup</b> (the All/Buddy/Party/… selector at the left, <c>mainBar/chatTarget</c>),</item>
/// <item>the <b>6 filter tabs</b> shown when expanded (<c>chat/Tap</c>),</item>
/// <item><b>scroll</b> arrows (<c>mainBar/scrollUp|scrollDown</c> and the expanded <c>chat/scroll</c> bar),</item>
/// <item>three window modes — <b>Minimized</b> (peek, no input) / <b>Normal</b> (input + 1 line) /
/// <b>Expanded</b> (multi-line log + tabs), toggled by the open/close arrows.</item>
/// </list>
/// Every baked-origin sprite draws at the bar's window anchor (<see cref="StatusBar.ChatAnchor"/>)
/// and lands via <c>pos - origin</c> — the same convention the gauges/buttons use. Programmatic
/// geometry uses the window transform <c>screen = anchor + (localX-512, localY-577)</c>, matching
/// the IDB (<c>SetChatType</c>, <c>MakeCtrlEdit</c>, <c>_ResetChatBarPos</c>).
/// </summary>
public sealed class ChatBar : GamePanel
{
    /// <summary>Outgoing send target — drives GameStage's default (no-slash) routing.</summary>
    public enum ChatTargetKind { All, Buddy, Party, Guild, Alliance, Expedition }

    /// <summary>Per-line channel used for the filter tabs (mirrors <c>CChatLog::m_nType</c> bits).</summary>
    public enum ChatLineType { Normal, Buddy, Party, Guild, Alliance, Expedition, System, Whisper }

    private enum ChatMode { Minimized, Normal, Expanded }

    // Authentic window-local Y anchors (chatWnd.y) per mode, in the 578-tall bar window (IDB SetChatType).
    private const int LineH        = 13;    // CChatLog line height
    private const int ExpandedLogW = 552;   // log spans local x≈2..554 (scrollbar at the right)
    // Expanded log height is drag-resizable in 1-line (13px) steps (IDB ChangeChatWndSize):
    // height ∈ [26, 489] → 2..37 visible lines; default 70 (5 lines).
    private const int MinChatH = 26, MaxChatH = 489;

    // Filter bits (IDB OnButtonClicked 0x3F6–0x3FB / IsFiltered).
    private const uint FBuddy = 0x8, FParty = 0x4, FGuild = 0x10, FAlliance = 0x20, FExpedition = 0x4000000;

    private static readonly string[] IconNames =
        { "all", "friend", "party", "guild", "association", "expedition" };

    private readonly BuiltInFont? _font;
    private readonly TextField? _input;
    private readonly List<(string text, Color color, ChatLineType type)> _lines = new();

    // ── WZ assets ────────────────────────────────────────────────────────────────
    private readonly WzSprite? _chatSpace, _chatSpace2, _chatEnter, _chatCover;       // backgrounds
    private readonly Button? _dropBase;                                               // chatTarget/base (4-state)
    private readonly WzSprite?[] _icons = new WzSprite?[6];                           // all/friend/party/...
    private readonly Button? _btOpen, _btClose, _btScrollUp, _btScrollDown;           // mainBar arrows
    private readonly TabButton?[] _tabs = new TabButton?[6];                          // chat/Tap (5-state)
    private readonly WzSprite? _tapBar, _tapBarOver;                                  // tab strip / drag-grip hint
    private readonly WzSprite? _scrUp, _scrTrack, _scrDown, _scrThumb;                // chat/scroll/normal

    // ── State ────────────────────────────────────────────────────────────────────
    private ChatMode _mode = ChatMode.Normal;
    private ChatMode _restoreMode = ChatMode.Normal;   // mode to return to when un-minimizing
    private bool _dropOpen;
    private uint _filterFlag;       // 0 = "All" tab
    private int _scroll;            // lines scrolled up from the newest (0 = bottom)
    private int _expandedHeight = 70;   // drag-resizable expanded log height (px); 70 = 5 lines
    private bool _dragResize;           // dragging the top edge to resize the log
    private int _dragStartMouseY, _dragStartHeight;
    private int _lastWrappedCount;      // cached display-line count after the last DrawLog (for scroll clamp)
    private int _prevWheel;             // last frame's Mouse.ScrollWheelValue (for delta-based wheel scroll)
    private int _viewW = 1024, _viewH = 768;
    private int _mx, _my;

    /// <summary>The status bar owns the bar window anchor; chat sprites/positions reference it.</summary>
    public StatusBar? Bar { get; set; }

    /// <summary>Currently selected send target (set via the dropup). GameStage routes plain chat to it.</summary>
    public ChatTargetKind Target { get; private set; } = ChatTargetKind.All;

    /// <summary>Wire to GameStage's chat submit (slash parsing + target routing).</summary>
    public Action<string>? OnSendChat { get; set; }

    public ChatBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;

        var mainBar = ui?.GetItem("StatusBar2.img/mainBar") as WzProperty;
        var chat    = ui?.GetItem("StatusBar2.img/chat") as WzProperty;

        _chatSpace  = Canvas(loader, mainBar, "chatSpace");
        _chatSpace2 = Canvas(loader, mainBar, "chatSpace2");
        _chatEnter  = Canvas(loader, mainBar, "chatEnter");
        _chatCover  = Canvas(loader, mainBar, "chatCover");

        // Chat-target dropup: a 4-state button face + one label icon per target.
        if (mainBar?.Get("chatTarget") as WzProperty is { } ct)
        {
            if (ct.Get("base") as WzProperty is { } baseRoot)
                _dropBase = new Button(loader, baseRoot) { OnClick = () => _dropOpen = !_dropOpen };
            for (var i = 0; i < 6; i++) _icons[i] = Canvas(loader, ct, IconNames[i]);
        }

        // The single open/close arrow toggles Normal ⇄ Expanded (one click to the resizable log);
        // minimizing to the peek is the BtChat button (ToggleMode). chatOpen shows until expanded.
        _btOpen       = MakeArrow(loader, mainBar, "chatOpen",   () => SetMode(ChatMode.Expanded));
        _btClose      = MakeArrow(loader, mainBar, "chatClose",  () => SetMode(ChatMode.Normal));
        _btScrollUp   = MakeArrow(loader, mainBar, "scrollUp",   () => Scroll(+1));
        _btScrollDown = MakeArrow(loader, mainBar, "scrollDown", () => Scroll(-1));

        _tapBar     = Canvas(loader, chat, "tapBar");
        _tapBarOver = Canvas(loader, chat, "tapBarOver");
        if (chat?.Get("Tap") as WzProperty is { } tap)
            for (var i = 0; i < 6; i++) _tabs[i] = new TabButton(loader, tap.Get(IconNames[i]) as WzProperty);

        if ((chat?.Get("scroll") as WzProperty)?.Get("normal") as WzProperty is { } sn)
        {
            _scrUp    = Canvas(loader, sn, "0");
            _scrTrack = Canvas(loader, sn, "1");
            _scrDown  = Canvas(loader, sn, "2");
            _scrThumb = Canvas(loader, sn, "3");
        }

        if (font != null)
            _input = new TextField
            {
                Width = 405, Height = 16, Font = font, MaxLength = 70,
                DrawFallbackBox = false,                 // chatEnter art is the input box
                TextColor = new Color(40, 40, 40), CaretColor = new Color(40, 40, 40),
            };

        AddLine("Welcome to MapleClaude!", new Color(220, 200, 100));
        RefreshTabChecks();   // "All" tab starts checked (no filter)
    }

    // ── Public API used by GameStage ───────────────────────────────────────────────
    public void AddLine(string text, Color? color = null) => AddLine(text, color, ChatLineType.Normal);

    public void AddLine(string text, Color? color, ChatLineType type)
    {
        _lines.Add((text, color ?? Color.White, type));
        if (_lines.Count > 200) _lines.RemoveAt(0);
        _scroll = 0;   // auto-stick to the newest line
    }

    /// <summary>Toggle minimize: hides the chat to the peek strip and restores it to whatever mode it
    /// was in (Normal or Expanded). Wired to both the bottom-bar BtChat button and the MapleChat key (;).</summary>
    public void ToggleMode()
    {
        if (_mode == ChatMode.Minimized)
        {
            SetMode(_restoreMode);
        }
        else
        {
            _restoreMode = _mode;
            SetMode(ChatMode.Minimized);
        }
    }

    /// <summary>True while the mouse is over the expanded log's drag grip, or the grip is being
    /// dragged — GameStage reads this to swap in the vertical-resize cursor.</summary>
    public bool IsOverResizeGrip =>
        IsVisible && _mode == ChatMode.Expanded && (_dragResize || DragGripRect().Contains(_mx, _my));

    /// <summary>True while the user is actively dragging the chat log's vertical resize grip
    /// (i.e. mouse is held down after grabbing the top edge). Used by the cursor system to
    /// swap from the animated vertical-resize sprite to the static vertical-scroll sprite.</summary>
    public bool IsDragGripActive => IsVisible && _mode == ChatMode.Expanded && _dragResize;

    // ── Geometry (everything anchors to the bar's window reference point) ───────────
    private Vector2 Anchor => Bar?.ChatAnchor ?? new Vector2(512, _viewH - 1);
    private Vector2 Win(float lx, float ly) => new(Anchor.X - 512 + lx, Anchor.Y - 577 + ly);

    private Rectangle EnterRect => _chatEnter is { } s
        ? new Rectangle((int)(Anchor.X - s.Origin.X), (int)(Anchor.Y - s.Origin.Y), s.Width, s.Height)
        : new Rectangle((int)Win(45, 519).X, (int)Win(45, 519).Y, 457, 21);

    // Window-local Y of the expanded log's top edge (IDB ptChatWnd.y = 515 - height).
    private int ExpandedChatWndY => 515 - _expandedHeight;
    private int VisibleLines => _mode == ChatMode.Expanded ? Math.Max(1, _expandedHeight / LineH) : 1;

    // Log rect (where lines are drawn), above the input row. In Expanded, the top edge follows the
    // drag-resized height; in Normal it's a single line just above the input. The bottom sits a
    // few px above chatEnter so text isn't crammed against the input.
    private Rectangle LogRect
    {
        get
        {
            var e = EnterRect;
            var bottom = e.Y - 5;
            var left = (int)(Anchor.X - 510);
            if (_mode == ChatMode.Expanded)
            {
                var top = (int)Win(0, ExpandedChatWndY).Y;
                return new Rectangle(left, top, ExpandedLogW, bottom - top);
            }
            return new Rectangle(left, bottom - LineH, 505, LineH);
        }
    }

    // The draggable top edge of the expanded log (the tapBar line). Grab it to resize vertically.
    private Rectangle DragGripRect()
    {
        var r = LogRect;
        return new Rectangle(r.X, r.Y - 2, r.Width, 8);
    }

    // The collapsed chat strip (chatSpace) — clicking it starts a chat (reopens to Normal).
    private Rectangle MinimizedStripRect()
    {
        var cs = _chatSpace2 ?? _chatSpace;
        return cs != null
            ? new Rectangle((int)(Anchor.X - cs.Origin.X), (int)(Anchor.Y - cs.Origin.Y), cs.Width, cs.Height)
            : new Rectangle((int)(Anchor.X - 508), (int)(Anchor.Y - 60), 505, 25);
    }

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _viewW = viewWidth;
        _viewH = viewHeight;
    }

    private void SetMode(ChatMode mode)
    {
        _mode = mode;
        _scroll = 0;
        _dragResize = false;
        if (mode == ChatMode.Minimized)
        {
            _dropOpen = false;
            if (_input != null) _input.IsFocused = false;
        }
    }

    /// <summary>Press Enter to chat (CUIStatusBar::StartChat): reopen if minimized, then focus the input.</summary>
    private void StartChat()
    {
        if (_mode == ChatMode.Minimized) SetMode(ChatMode.Normal);
        if (_input != null) _input.IsFocused = true;
    }

    private void Scroll(int deltaLines)
    {
        var max = Math.Max(0, FilteredCount() - VisibleLines);
        _scroll = Math.Clamp(_scroll + deltaLines, 0, max);
    }

    // Drag the top edge: up grows the log, down shrinks it (IDB ChangeChatWndSize, 13px line steps).
    // Continuous here; VisibleLines = height/13 quantises the line count. Capped so it can't run off-screen.
    private void ApplyDragResize(int mouseY)
    {
        var hardMax = Math.Max(MinChatH, Math.Min(MaxChatH, (int)Anchor.Y - 70));
        _expandedHeight = Math.Clamp(_dragStartHeight + (_dragStartMouseY - mouseY), MinChatH, hardMax);
        var max = Math.Max(0, FilteredCount() - VisibleLines);
        if (_scroll > max) _scroll = max;
    }

    // Count of display lines (post-filter, post-wrap), cached after the last DrawLog. Drives the
    // scroll-clamp + the scrollbar thumb so they advance line-by-line in display space, not source.
    private int FilteredCount() => _lastWrappedCount;

    // Max text pixel-width per display line — log width minus side padding and (in Expanded) the
    // scrollbar gutter. Used to word-wrap chat lines so they don't run off the right edge.
    private int TextMaxWidth()
    {
        if (_mode == ChatMode.Expanded) return ExpandedLogW - 12 - 11;
        if (_mode == ChatMode.Minimized) return (_chatSpace2?.Width ?? _chatSpace?.Width ?? 505) - 12;
        return 505 - 12;
    }

    // Filter the source log + word-wrap each surviving line to the current text width.
    private List<(string text, Color color)> BuildWrappedView()
    {
        var result = new List<(string, Color)>(_lines.Count);
        var maxW = TextMaxWidth();
        foreach (var l in _lines)
        {
            if (!Passes(l.type)) continue;
            WrapInto(l.text, l.color, maxW, result);
        }
        return result;
    }

    // Word-wrap on spaces, hard-break a single word longer than maxWidth as a fallback.
    private void WrapInto(string text, Color color, int maxWidth, List<(string, Color)> dst)
    {
        if (_font == null || maxWidth <= 0 || _font.Measure(text).X <= maxWidth)
        {
            dst.Add((text, color));
            return;
        }
        var words = text.Split(' ');
        var line = new StringBuilder();
        foreach (var word in words)
        {
            var sep = line.Length == 0 ? "" : " ";
            if (_font.Measure(line + sep + word).X <= maxWidth)
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
                continue;
            }
            if (line.Length > 0) { dst.Add((line.ToString(), color)); line.Clear(); }
            // The single word still doesn't fit — char-break it greedily.
            var w = word;
            while (_font.Measure(w).X > maxWidth && w.Length > 1)
            {
                var cut = w.Length;
                while (cut > 1 && _font.Measure(w[..cut]).X > maxWidth) cut--;
                dst.Add((w[..cut], color));
                w = w[cut..];
            }
            line.Append(w);
        }
        if (line.Length > 0) dst.Add((line.ToString(), color));
    }

    private bool Passes(ChatLineType t)
    {
        if (_filterFlag == 0) return true;
        return t switch
        {
            ChatLineType.Buddy      => (_filterFlag & FBuddy) != 0,
            ChatLineType.Party      => (_filterFlag & FParty) != 0,
            ChatLineType.Guild      => (_filterFlag & FGuild) != 0,
            ChatLineType.Alliance   => (_filterFlag & FAlliance) != 0,
            ChatLineType.Expedition => (_filterFlag & FExpedition) != 0,
            _ => true,   // Normal / System / Whisper always shown
        };
    }

    // ── Update ─────────────────────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        if (!IsVisible) return;
        var ms = Mouse.GetState();
        _mx = ms.X; _my = ms.Y;
        // Sample wheel up-front so _prevWheel doesn't go stale across an early-return path.
        var wheelDelta = ms.ScrollWheelValue - _prevWheel;
        _prevWheel = ms.ScrollWheelValue;
        Layout();

        var down = ms.LeftButton == ButtonState.Pressed;
        ArrowFor(_mode)?.Update(_mx, _my, down);   // open-arrow (minimized) or close-arrow (open)
        if (_mode == ChatMode.Minimized) return;

        // Mouse wheel: wheel up = older (+_scroll), down = newer; only over the log area.
        if (wheelDelta != 0 && LogRect.Contains(_mx, _my)) Scroll(Math.Sign(wheelDelta));

        _dropBase?.Update(_mx, _my, down);
        if (_mode == ChatMode.Normal) { _btScrollUp?.Update(_mx, _my, down); _btScrollDown?.Update(_mx, _my, down); }
        if (_mode == ChatMode.Expanded)
        {
            if (_dragResize) { if (down) ApplyDragResize(_my); else _dragResize = false; }
            for (var i = 0; i < 6; i++) if (_tabs[i] is { } t) t.Hover = t.Bounds.Contains(_mx, _my);
        }

        if (_input != null)
        {
            var ir = InputRect();
            _input.Position = new Vector2(ir.X, ir.Y);
            _input.Width = ir.Width;
            _input.Update(gameTime);
        }
    }

    // Expanded shows the collapse arrow (→ Normal); Minimized/Normal show the expand arrow (→ Expanded).
    private Button? ArrowFor(ChatMode mode) => mode == ChatMode.Expanded ? _btClose : _btOpen;

    private Rectangle InputRect()
    {
        var e = EnterRect;
        var x = e.X + 30;                                   // just right of the dropup face
        var arrowX = (int)(Anchor.X - 28);                 // chatOpen/chatClose left edge (origin x=28)
        var w = Math.Max(80, arrowX - 6 - x);
        var h = 16;
        return new Rectangle(x, e.Y + (e.Height - h) / 2, w, h);
    }

    private void Layout()
    {
        var a = Anchor;
        if (_dropBase != null) _dropBase.Position = a;
        if (_btOpen != null) _btOpen.Position = a;
        if (_btClose != null) _btClose.Position = a;
        if (_btScrollUp != null) _btScrollUp.Position = a;
        if (_btScrollDown != null) _btScrollDown.Position = a;
        if (_mode == ChatMode.Expanded)
            for (var i = 0; i < 6; i++)
                if (_tabs[i] is { } t) t.Position = Win(1 + 46 * i, ExpandedChatWndY - 19);
    }

    // ── Draw ─────────────────────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        Layout();
        var a = Anchor;

        if (_mode == ChatMode.Minimized)
        {
            _chatSpace?.Draw(sb, a);
            _chatSpace2?.Draw(sb, a);
            DrawLog(sb, white, peek: true);
            _btOpen?.Draw(sb);
            return;
        }

        // Open (Normal / Expanded): the input-box art.
        _chatEnter?.Draw(sb, a);

        if (_mode == ChatMode.Expanded)
        {
            DrawLog(sb, white, peek: false);
            // tapBar marks the draggable top edge; show tapBarOver while hovering/dragging the grip.
            var gripActive = _dragResize || DragGripRect().Contains(_mx, _my);
            ((gripActive ? _tapBarOver : null) ?? _tapBar)?.Draw(sb, Win(0, ExpandedChatWndY));
            for (var i = 0; i < 6; i++) _tabs[i]?.Draw(sb);
            DrawScrollBar(sb, white);
        }
        else
        {
            DrawLog(sb, white, peek: false);
        }

        _chatCover?.Draw(sb, a);

        // Chat-target dropup face: the 4-state base + the current target's label icon.
        _dropBase?.Draw(sb);
        _icons[(int)Target]?.Draw(sb, a);

        _input?.Draw(sb, white);
        ArrowFor(_mode)?.Draw(sb);   // expand (Normal) or collapse (Expanded) arrow
        if (_mode == ChatMode.Normal) { _btScrollUp?.Draw(sb); _btScrollDown?.Draw(sb); }

        if (_dropOpen) DrawDropList(sb, white);
    }

    private void DrawLog(SpriteBatch sb, Texture2D white, bool peek)
    {
        int left, width, bottom, lines;
        Rectangle? backdrop = null;
        if (peek)
        {
            // One ghost line centred in the collapsed chatSpace strip.
            var cs = _chatSpace2 ?? _chatSpace;
            bottom = cs != null ? (int)(Anchor.Y - cs.Origin.Y + cs.Height) - 6 : (int)(Anchor.Y - 41);
            left = (int)(Anchor.X - 508);
            width = cs?.Width ?? 505;
            lines = 1;
        }
        else
        {
            var r = LogRect;
            left = r.X; width = r.Width; bottom = r.Bottom; lines = VisibleLines;
            backdrop = r;   // solid backdrop across the full log area (covers empty space too)
        }

        var vis = BuildWrappedView();
        _lastWrappedCount = vis.Count;

        if (backdrop is { } bd)
            sb.Draw(white, bd, new Color(0, 0, 0, 150));

        if (vis.Count == 0) return;

        var end = Math.Max(0, vis.Count - _scroll);
        var start = Math.Max(0, end - lines);
        var n = end - start;
        if (n <= 0) return;

        for (var i = 0; i < n; i++)
        {
            var ln = vis[start + i];
            var y = bottom - (n - i) * LineH;
            var color = peek ? new Color(ln.color, 170) : ln.color;
            _font?.Draw(sb, ln.text, new Vector2(left + 6, y), color);
        }
    }

    // The expanded-mode scrollbar (chat/scroll): up arrow, stretched track, down arrow, proportional thumb.
    private void DrawScrollBar(SpriteBatch sb, Texture2D white)
    {
        if (_scrUp == null || _scrDown == null) return;
        var r = LogRect;
        var x = r.Right - _scrUp.Width;
        var top = r.Y;
        var bottom = r.Bottom;
        _scrUp.Draw(sb, new Vector2(x, top));
        _scrDown.Draw(sb, new Vector2(x, bottom - _scrDown.Height));
        var trackTop = top + _scrUp.Height;
        var trackBot = bottom - _scrDown.Height;
        if (_scrTrack != null && trackBot > trackTop)
            sb.Draw(_scrTrack.Texture, new Rectangle(x, trackTop, _scrTrack.Width, trackBot - trackTop), Color.White);
        // Thumb position reflects _scroll within the filtered range.
        var total = Math.Max(VisibleLines, FilteredCount());
        if (_scrThumb != null && total > VisibleLines)
        {
            var span = trackBot - trackTop - _scrThumb.Height;
            var frac = 1f - _scroll / (float)Math.Max(1, total - VisibleLines);
            var ty = trackTop + (int)(span * Math.Clamp(frac, 0f, 1f));
            _scrThumb.Draw(sb, new Vector2(x + (_scrUp.Width - _scrThumb.Width) / 2f, ty));
        }
    }

    private Rectangle DropListRect()
    {
        var bb = _dropBase?.Bounds ?? new Rectangle((int)(Anchor.X - 510), (int)(Anchor.Y - 58), 68, 21);
        const int rowH = 18;
        var h = 6 * rowH;
        return new Rectangle(bb.X, bb.Y - h, Math.Max(bb.Width, 70), h);
    }

    private void DrawDropList(SpriteBatch sb, Texture2D white)
    {
        var r = DropListRect();
        const int rowH = 18;
        sb.Draw(white, r, new Color(28, 28, 36, 245));
        DrawBorder(sb, white, r, new Color(120, 110, 80));
        for (var i = 0; i < 6; i++)
        {
            var row = new Rectangle(r.X, r.Y + i * rowH, r.Width, rowH);
            if (row.Contains(_mx, _my)) sb.Draw(white, row, new Color(80, 100, 150, 200));
            var icon = _icons[i];
            if (icon != null)
                sb.Draw(icon.Texture, new Vector2(row.X + 6, row.Y + (rowH - icon.Height) / 2f), Color.White);
        }
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    // ── Input ──────────────────────────────────────────────────────────────────────
    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        Layout();

        if (_mode == ChatMode.Minimized)
        {
            if (_btOpen?.HandleMouseButton(x, y, down) == true) return true;
            if (down && MinimizedStripRect().Contains(x, y)) { StartChat(); return true; }   // click the strip to chat
            return false;
        }

        // Open dropup list gets first crack.
        if (_dropOpen)
        {
            var lr = DropListRect();
            const int rowH = 18;
            if (down)
            {
                if (lr.Contains(x, y))
                {
                    var i = Math.Clamp((y - lr.Y) / rowH, 0, 5);
                    Target = (ChatTargetKind)i;
                    _dropOpen = false;
                    return true;
                }
                if (!(_dropBase?.Bounds.Contains(x, y) ?? false)) _dropOpen = false;   // click-away closes
            }
        }

        if (_dropBase?.HandleMouseButton(x, y, down) == true) return true;
        if (ArrowFor(_mode)?.HandleMouseButton(x, y, down) == true) return true;

        if (_mode == ChatMode.Normal)
        {
            if (_btScrollUp?.HandleMouseButton(x, y, down) == true) return true;
            if (_btScrollDown?.HandleMouseButton(x, y, down) == true) return true;
        }

        if (_mode == ChatMode.Expanded)
        {
            for (var i = 0; i < 6; i++)
                if (_tabs[i] is { Enabled: true } t && t.Bounds.Contains(x, y))
                {
                    if (down) { ClickTab(i); }
                    return true;
                }
            if (HandleScrollBarClick(x, y, down)) return true;
            // Drag the top edge to resize the log vertically (checked after tabs/scroll so they win).
            if (down && DragGripRect().Contains(x, y))
            {
                _dragResize = true; _dragStartMouseY = y; _dragStartHeight = _expandedHeight;
                return true;
            }
            if (!down) _dragResize = false;
        }

        if (_mode != ChatMode.Minimized) _input?.HandleMouseButton(x, y, down);

        // Swallow clicks on our footprint so they don't reach the world.
        return InputRect().Contains(x, y) || LogRect.Contains(x, y) || (_dropBase?.Bounds.Contains(x, y) ?? false);
    }

    private bool HandleScrollBarClick(int x, int y, bool down)
    {
        if (!down || _scrUp == null || _scrDown == null) return false;
        var r = LogRect;
        var bx = r.Right - _scrUp.Width;
        if (new Rectangle(bx, r.Y, _scrUp.Width, _scrUp.Height).Contains(x, y)) { Scroll(+1); return true; }
        if (new Rectangle(bx, r.Bottom - _scrDown.Height, _scrDown.Width, _scrDown.Height).Contains(x, y)) { Scroll(-1); return true; }
        return false;
    }

    private void ClickTab(int i)
    {
        _filterFlag = i switch
        {
            0 => 0,
            1 => _filterFlag ^ FBuddy,
            2 => _filterFlag ^ FParty,
            3 => _filterFlag ^ FGuild,
            4 => _filterFlag ^ FAlliance,
            5 => _filterFlag ^ FExpedition,
            _ => _filterFlag,
        };
        _scroll = 0;
        RefreshTabChecks();
    }

    private void RefreshTabChecks()
    {
        if (_tabs[0] is { } t0) t0.Checked = _filterFlag == 0;
        if (_tabs[1] is { } t1) t1.Checked = (_filterFlag & FBuddy) != 0;
        if (_tabs[2] is { } t2) t2.Checked = (_filterFlag & FParty) != 0;
        if (_tabs[3] is { } t3) t3.Checked = (_filterFlag & FGuild) != 0;
        if (_tabs[4] is { } t4) t4.Checked = (_filterFlag & FAlliance) != 0;
        if (_tabs[5] is { } t5) t5.Checked = (_filterFlag & FExpedition) != 0;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (_input?.IsFocused == true)
        {
            if (key == Keys.Enter)
            {
                var text = _input.Text.Trim();
                if (text.Length > 0) OnSendChat?.Invoke(text);   // no local echo; server broadcasts it back
                _input.Clear();
                _input.IsFocused = false;
                return true;
            }
            if (key == Keys.Escape) { _input.Clear(); _input.IsFocused = false; return true; }
            if (key == Keys.Back) { _input.OnTextInput('\b'); return true; }
            _input.OnKeyPress(key, Keyboard.GetState());
            return true;                                          // swallow so it doesn't fire game hotkeys
        }
        if (key == Keys.Enter && _input != null) { StartChat(); return true; }
        return false;
    }

    public override void OnTextInput(char ch)
    {
        if (_input?.IsFocused == true) _input.OnTextInput(ch);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────
    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? parent, string name)
        => parent?.Get(name) is WzCanvas c ? loader.Load(c) : null;

    private static Button? MakeArrow(WzTextureLoader loader, WzProperty? mainBar, string name, Action onClick)
        => mainBar?.Get(name) as WzProperty is { } root ? new Button(loader, root) { OnClick = onClick } : null;

    /// <summary>A 5-state chat filter tab (<c>chat/Tap/&lt;name&gt;</c>): normal/pressed/disabled/mouseOver/checked.
    /// Origin is (0,0) so it draws at its top-left <see cref="Position"/>.</summary>
    private sealed class TabButton
    {
        private readonly WzSprite? _normal, _pressed, _disabled, _over, _checked;
        public Vector2 Position;
        public bool Checked;
        public bool Hover;
        public bool Enabled { get; set; } = true;

        public TabButton(WzTextureLoader loader, WzProperty? root)
        {
            _normal   = St(loader, root, "normal");
            _pressed  = St(loader, root, "pressed");
            _disabled = St(loader, root, "disabled");
            _over     = St(loader, root, "mouseOver");
            _checked  = St(loader, root, "checked");
        }

        private int W => _normal?.Width ?? 38;
        private int H => _normal?.Height ?? 19;
        public Rectangle Bounds => new((int)Position.X, (int)Position.Y, W, H);

        public void Draw(SpriteBatch sb)
        {
            var s = !Enabled ? _disabled ?? _normal
                  : Checked ? _checked ?? _pressed ?? _normal
                  : Hover ? _over ?? _normal
                  : _normal;
            s?.Draw(sb, Position);
        }

        private static WzSprite? St(WzTextureLoader loader, WzProperty? root, string state)
            => (root?.Get(state) as WzProperty)?.Get("0") is WzCanvas c ? loader.Load(c) : null;
    }
}
