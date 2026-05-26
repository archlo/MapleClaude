using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Quest detail panel — authentic v95 <c>CUIQuestInfoDetail</c> rebuilt from
/// <c>UIWindow2.img/Quest/quest_info</c>. 296×396, anchored to the right edge of the companion
/// <see cref="QuestLog"/>. Header (blue <c>backgrnd3</c>) shows the quest name, "Quest ID : {id}",
/// "Over Level {min} Under Level {max}" and an animated NPC speaker (the quest's start NPC's
/// <c>stand</c> action from Npc.wz). The body draws the WZ <c>summary</c> canvas with the
/// <c>QuestData.Summary</c> text wrapped to width and manually scrollable. Action buttons:
/// <list type="bullet">
///   <item><c>BtArlim</c> — REMOTELY ACCEPT QUEST (visible on Available tab).</item>
///   <item><c>BtGiveup</c> — RESIGN (visible on In Progress tab).</item>
///   <item><c>BtNPC</c> — FIND NPC (always visible if a start NPC exists).</item>
///   <item><c>BtClose</c> — top-right close.</item>
/// </list>
/// The panel always sits to the right of the list; the list owns "is the window open at all".
/// </summary>
public sealed class QuestDetail : GamePanel
{
    private const int PanelW0 = 296, PanelH0 = 396;
    // backgrnd3 (276×95) sits at panel-local (10, 27) thanks to its origin (-10, -27).
    private const int HeaderX = 10, HeaderY = 27, HeaderW = 276, HeaderH = 95;
    // summary canvas (263×112) sits at (10, 252). The body extends below it through the bottom buttons.
    private const int BodyX = 16, BodyY = 130, BodyW = 263, BodyH = 196;
    private const int ScrollW = 12, ScrollGutterX = PanelW0 - 18;

    private readonly WzTextureLoader _loader;
    private readonly WzPackage? _npcWz;
    private readonly BuiltInFont? _font;

    private readonly WzSprite? _bg, _bg2, _bg3, _summaryBg;
    private readonly Button? _btClose;
    private readonly Button? _btAccept;     // BtArlim (REMOTELY ACCEPT)
    private readonly Button? _btResign;     // BtGiveup
    private readonly Button? _btFindNpc;    // BtNPC

    private NpcLook? _speaker;
    private int _speakerNpcId;

    private QuestData? _quest;
    private byte _state;     // 0 = available, 1 = in-progress, 2 = completed
    private float _scroll;
    private bool _draggingThumb;
    private float _thumbGrabDy;
    private List<string> _wrappedSummary = new();
    private int _prevWheel;

    /// <summary>Anchor — the list panel's position + (list.PanelW, 0). Set by the parent stage every
    /// frame so dragging the list moves the detail with it.</summary>
    public Vector2 AnchorTopLeft { get; set; }

    public Action<int>? OnRemoteAccept { get; set; }
    public Action<int>? OnResign       { get; set; }
    public Action<int>? OnFindNpc      { get; set; }

    public QuestDetail(WzTextureLoader loader, WzPackage? ui, WzPackage? npcWz, BuiltInFont? font)
    {
        _loader = loader;
        _npcWz = npcWz;
        _font = font;
        IsVisible = false;

        var qi = (ui?.GetItem("UIWindow2.img/Quest/quest_info")) as WzProperty;
        _bg = Canvas(loader, qi, "backgrnd");
        _bg2 = Canvas(loader, qi, "backgrnd2");
        _bg3 = Canvas(loader, qi, "backgrnd3");
        _summaryBg = Canvas(loader, qi, "summary");

        _btClose    = ButtonFrom(loader, qi, "BtClose",  () => IsVisible = false);
        _btAccept   = ButtonFrom(loader, qi, "BtArlim",  () => { if (_quest != null) OnRemoteAccept?.Invoke(_quest.Id); });
        _btResign   = ButtonFrom(loader, qi, "BtGiveup", () => { if (_quest != null) OnResign?.Invoke(_quest.Id); });
        _btFindNpc  = ButtonFrom(loader, qi, "BtNPC",    () => { if (_quest != null) OnFindNpc?.Invoke(_quest.Id); });
    }

    private static Button? ButtonFrom(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
        => root?.Get(name) is WzProperty p ? new Button(loader, p) { OnClick = onClick } : null;

    /// <summary>Set the displayed quest and current server state. <paramref name="state"/>: 0=available,
    /// 1=in-progress, 2=completed (matches QuestRecordArgs.State).</summary>
    public void SetQuest(QuestData? data, byte state)
    {
        _quest = data;
        _state = state;
        _scroll = 0;
        _wrappedSummary = new();
        if (data is null) { IsVisible = false; return; }

        IsVisible = true;
        if (_font != null)
            _wrappedSummary = _font.WrapToWidth(data.Summary, BodyW - 6).ToList();

        // Speaker = start NPC for available/in-progress; complete NPC for completed.
        var npcId = state == 2 && data.Complete.Npc != 0 ? data.Complete.Npc : data.Start.Npc;
        if (npcId != 0 && npcId != _speakerNpcId)
        {
            _speakerNpcId = npcId;
            _speaker = new NpcLook(npcId, Vector2.Zero, _font);
            _speaker.Load(_loader, _npcWz);
            _speaker.SetState("stand");
        }
        else if (npcId == 0)
        {
            _speaker = null;
            _speakerNpcId = 0;
        }
    }

    public override void Relayout(int viewWidth, int viewHeight) { }

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        Position = AnchorTopLeft;

        // Position the buttons every frame (the WZ origin places each on its baked offset).
        var p = Position;
        if (_btClose   != null) _btClose.Position   = p + new Vector2(PanelW0 - 18, 6);
        if (_btAccept  != null) _btAccept.Position  = p + new Vector2(HeaderX + 6, HeaderY + 60);
        if (_btResign  != null) _btResign.Position  = p + new Vector2(HeaderX + 6, HeaderY + 60);
        if (_btFindNpc != null) _btFindNpc.Position = p + new Vector2(PanelW0 - 92, PanelH0 - 30);

        var m = Mouse.GetState();
        var down = m.LeftButton == ButtonState.Pressed;
        if (_draggingThumb)
        {
            if (down) DragThumbTo(m.Y);
            else _draggingThumb = false;
        }

        // Wheel inside body area scrolls one line per detent.
        var bodyRect = new Rectangle((int)p.X + BodyX, (int)p.Y + BodyY, BodyW, BodyH);
        if (bodyRect.Contains(m.X, m.Y))
        {
            var d = m.ScrollWheelValue - _prevWheel;
            if (d != 0) _scroll = Math.Max(0, _scroll - Math.Sign(d));
        }
        _prevWheel = m.ScrollWheelValue;

        ClampScroll();

        _speaker?.Update((float)gt.ElapsedGameTime.TotalSeconds);

        _btClose?.Update(m.X, m.Y, down);
        if (_state == 0) _btAccept?.Update(m.X, m.Y, down);
        else if (_state == 1) _btResign?.Update(m.X, m.Y, down);
        _btFindNpc?.Update(m.X, m.Y, down);
    }

    private int VisibleLines => _font is null ? 0 : Math.Max(1, BodyH / _font.LineHeight);

    private void ClampScroll()
    {
        var max = Math.Max(0, _wrappedSummary.Count - VisibleLines);
        if (_scroll > max) _scroll = max;
        if (_scroll < 0) _scroll = 0;
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible || _quest is null) return;
        var p = Position;

        if (_bg != null) { _bg.Draw(sb, p); _bg2?.Draw(sb, p); }
        else sb.Draw(white, new Rectangle((int)p.X, (int)p.Y, PanelW0, PanelH0), new Color(18, 22, 30, 235));

        // Header (blue strip): backgrnd3 fills HeaderX..HeaderX+HeaderW at HeaderY..HeaderY+HeaderH.
        _bg3?.Draw(sb, p);
        DrawHeader(sb, p);

        // Body: summary canvas + wrapped text + scrollbar.
        _summaryBg?.Draw(sb, p);
        DrawBody(sb, white, p);

        // Bottom-right FIND NPC + actions.
        if (_state == 0) _btAccept?.Draw(sb);
        else if (_state == 1) _btResign?.Draw(sb);
        _btFindNpc?.Draw(sb);
        _btClose?.Draw(sb);
    }

    private void DrawHeader(SpriteBatch sb, Vector2 p)
    {
        if (_quest is null || _font is null) return;
        var headerLeft = p.X + HeaderX + 10;
        var headerTop  = p.Y + HeaderY + 6;
        var nameMax = HeaderW - 90;   // leave room for the speaker on the right

        var name = _font.TruncateToWidth(_quest.Name, nameMax);
        _font.Draw(sb, name, new Vector2(headerLeft, headerTop), new Color(255, 255, 255));

        _font.Draw(sb, $"Quest ID : {_quest.Id}",
            new Vector2(headerLeft, headerTop + 18), new Color(230, 245, 255));

        var min = _quest.Start.LvMin;
        var max = _quest.Start.LvMax;
        var levelStr = min > 0 && max > 0 ? $"Over Level {min}  Under Level {max}"
                     : min > 0            ? $"Over Level {min}"
                     : max > 0            ? $"Under Level {max}"
                     :                       string.Empty;
        if (!string.IsNullOrEmpty(levelStr))
            _font.Draw(sb, levelStr, new Vector2(headerLeft, headerTop + 36), new Color(220, 240, 255));

        // Animated speaker (top-right of the header strip).
        if (_speaker != null)
        {
            var feet = new Vector2(p.X + HeaderX + HeaderW - 36, p.Y + HeaderY + HeaderH - 8);
            _speaker.DrawFrameOnly(sb, feet);
        }
    }

    private void DrawBody(SpriteBatch sb, Texture2D white, Vector2 p)
    {
        if (_font is null) return;
        var x = (int)p.X + BodyX;
        var y = (int)p.Y + BodyY;
        var start = (int)Math.Floor(_scroll);
        var lh = _font.LineHeight;
        for (var i = 0; i < VisibleLines; i++)
        {
            var idx = start + i;
            if (idx >= _wrappedSummary.Count) break;
            _font.Draw(sb, _wrappedSummary[idx], new Vector2(x, y + i * lh), new Color(60, 60, 70));
        }
        DrawScrollbar(sb, white, p);
    }

    private void DrawScrollbar(SpriteBatch sb, Texture2D white, Vector2 p)
    {
        if (_wrappedSummary.Count <= VisibleLines) return;
        var trackTop = (int)p.Y + BodyY;
        var trackBot = (int)p.Y + BodyY + BodyH;
        var trackX = (int)p.X + ScrollGutterX;
        sb.Draw(white, new Rectangle(trackX, trackTop, ScrollW, trackBot - trackTop), new Color(180, 170, 145, 120));
        var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleLines / _wrappedSummary.Count);
        var span = trackBot - trackTop - thumbH;
        var frac = _scroll / Math.Max(1, _wrappedSummary.Count - VisibleLines);
        var ty = trackTop + (int)(span * Math.Clamp(frac, 0f, 1f));
        sb.Draw(white, new Rectangle(trackX + 1, ty, ScrollW - 2, thumbH), new Color(95, 110, 130));
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btClose?.HandleMouseButton(x, y, down) == true) return true;
        if (_state == 0 && _btAccept?.HandleMouseButton(x, y, down) == true) return true;
        if (_state == 1 && _btResign?.HandleMouseButton(x, y, down) == true) return true;
        if (_btFindNpc?.HandleMouseButton(x, y, down) == true) return true;

        if (!down) return PanelRect.Contains(x, y);

        // Scrollbar.
        if (_wrappedSummary.Count > VisibleLines)
        {
            var trackTop = (int)Position.Y + BodyY;
            var trackBot = (int)Position.Y + BodyY + BodyH;
            var trackX = (int)Position.X + ScrollGutterX;
            var trackR = new Rectangle(trackX, trackTop, ScrollW, trackBot - trackTop);
            if (trackR.Contains(x, y))
            {
                var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleLines / _wrappedSummary.Count);
                var span = trackBot - trackTop - thumbH;
                var frac = _scroll / Math.Max(1, _wrappedSummary.Count - VisibleLines);
                var ty = trackTop + (int)(span * Math.Clamp(frac, 0f, 1f));
                if (y >= ty && y < ty + thumbH) { _draggingThumb = true; _thumbGrabDy = y - ty; }
                else { _scroll = y < ty ? _scroll - VisibleLines : _scroll + VisibleLines; ClampScroll(); }
                return true;
            }
        }

        // Eat clicks inside the panel so they don't reach the world.
        return PanelRect.Contains(x, y);
    }

    private Rectangle PanelRect =>
        new Rectangle((int)Position.X, (int)Position.Y, PanelW0, PanelH0);

    private void DragThumbTo(int my)
    {
        var max = Math.Max(0, _wrappedSummary.Count - VisibleLines);
        if (max == 0) return;
        var trackTop = (int)Position.Y + BodyY;
        var trackBot = (int)Position.Y + BodyY + BodyH;
        var thumbH = Math.Max(18, (trackBot - trackTop) * VisibleLines / _wrappedSummary.Count);
        var span = trackBot - trackTop - thumbH;
        if (span <= 0) return;
        var rel = my - _thumbGrabDy - trackTop;
        _scroll = Math.Clamp(rel / span, 0f, 1f) * max;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        return true;
    }

    private static WzSprite? Canvas(WzTextureLoader loader, WzProperty? root, string name) =>
        root?.Get(name) is WzCanvas c ? loader.Load(c) : null;
}
