using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// The bottom-right quick-slot bar (authentic v95 <c>CUIStatusBar::CQuickSlot</c>).
///
/// The slot frame itself is <b>not</b> drawn by this class — it is supplied by the bar:
/// <list type="bullet">
///   <item>At <c>viewW &gt; 800</c> ("attached" mode) the 1024-wide bar art
///   <c>StatusBar2.img/mainBar/backgrnd</c> has the 4×2 slot grid baked into its right end,
///   so we only render the 8 bound icons and key labels on top.</item>
///   <item>At <c>viewW &lt;= 800</c> ("popup" mode) the 800-pixel bar overhangs the screen and the
///   attached frame is off-canvas; the composite <c>StatusBar2.img/mainBar/quickSlot/quickSlot</c>
///   panel (145×93) is rendered by <see cref="StatusBar"/> together with its
///   <c>BtOpen</c>/<c>BtClose</c> toggle, and we overlay icons/labels at the popup's slot
///   positions.</item>
/// </list>
///
/// Slot rects come from the IDB <c>s_ptShortKeyPos</c> array: slot i at
/// <c>(7 + 33·(i%4), 15 + 33·(i/4))</c>, 32×32. In attached mode the slot grid's layer origin
/// inside the 1024×85 bar is <c>(881, 2)</c> (CUIStatusBar::CQuickSlot::ChangeScreenResolution),
/// so slot 0 lands at bar-relative <c>(888, 17)</c>. In popup mode the composite canvas has
/// origin <c>(-143, 143)</c> and is anchored at the bar reference point
/// <c>R = (viewW/2, viewH-1)</c>; its top-left in screen space is therefore
/// <c>(viewW/2 + 143, viewH - 144)</c>, putting slot 0 at <c>(viewW/2 + 150, viewH - 129)</c>.
///
/// Each slot's key label is the canvas under
/// <c>UIWindow2.img/KeyConfig/quickslotConfig/key/{scancode}</c> drawn at slot + (2,2); the
/// bound action's icon fills the slot below it. The 8 slots are a view of 8 FuncKey scancodes
/// (default Shift/Ins/Home/PgUp/Ctrl/Del/End/PgDn from Kinoko <c>DEFAULT_QUICKSLOT_KEY_MAP</c>,
/// overridden by <c>QuickslotMappedInit</c> 175). Dragging a skill from the Skill window onto
/// a slot binds it through the shared FuncKey map.
/// </summary>
public sealed class QuickSlotBar : GamePanel
{
    public const int SlotCount = 8;
    private static readonly int[] DefaultKeys = { 0x2A, 0x52, 0x47, 0x49, 0x1D, 0x53, 0x4F, 0x51 };
    private readonly int[] _keys = (int[])DefaultKeys.Clone();

    private readonly WzTextureLoader _loader;
    private readonly WzProperty? _labelRoot;  // UIWindow2.img/KeyConfig/quickslotConfig[/key]
    private readonly Dictionary<int, WzSprite?> _labelCache = new();
    private readonly Func<int, FuncKeyMapped> _bindingAt;
    private readonly Action<int, int> _bindSkill;
    private readonly Func<int, WzSprite?> _skillIcon;

    private int _viewW = 800, _viewH = 600;

    // Slot layout (IDB s_ptShortKeyPos): 4 cols × 2 rows of 32×32 cells with 33px stride.
    private const int SlotX0 = 7, SlotY0 = 15, SlotStride = 33, SlotSize = 32;

    // Bar geometry: see StatusBar.cs. BarCenterX is max(512, viewW/2) so a ≤1024 viewport
    // is left-anchored (the right end overhangs at 800, exactly like the v95 client), and a
    // wider viewport centres the bar. The attached slot layer sits at bar-relative (881, 2)
    // (CUIStatusBar::CQuickSlot::ChangeScreenResolution / s_ptShortKeyPos).
    private const int BarH = 85;
    private const int AttachedLayerX = 881, AttachedLayerY = 2;

    // Popup composite (StatusBar2.img/mainBar/quickSlot/quickSlot) is 145×93 with origin
    // (-143, 143); see WZ. Drawn at R=(viewW/2, viewH-1), its top-left lands at
    // (viewW/2 + 143, viewH - 144).
    private const int PopupOriginX = 143, PopupOriginY = 143;

    public QuickSlotBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font,
        Func<int, FuncKeyMapped> bindingAt, Action<int, int> bindSkill, Func<int, WzSprite?> skillIcon)
    {
        _ = font;
        _loader = loader;
        _bindingAt = bindingAt;
        _bindSkill = bindSkill;
        _skillIcon = skillIcon;
        var qc = ui?.GetItem("UIWindow2.img/KeyConfig/quickslotConfig") as WzProperty;
        _labelRoot = (qc?.Get("key") as WzProperty) ?? qc; // labels are keyed by scancode
        IsVisible = true;
    }

    /// <summary>Set the 8 quick-slot key scancodes (from QuickslotMappedInit).</summary>
    public void SetKeys(int[]? keys)
    {
        if (keys is null) return;
        for (var i = 0; i < SlotCount && i < keys.Length; i++) _keys[i] = keys[i];
    }

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _viewW = viewWidth;
        _viewH = viewHeight;
    }

    // Slot-grid top-left in screen space.
    //   - viewW > 800  → attached to the bar's baked frame at bar-relative (881, 2).
    //                    Bar is left-anchored ≤1024 and centred above 1024.
    //   - viewW ≤ 800  → overlay onto StatusBar's popup composite, anchored at the bar
    //                    reference point R=(viewW/2, viewH-1) offset by the canvas origin.
    private Vector2 GridTopLeft
    {
        get
        {
            if (_viewW > 800)
            {
                var barCenterX = Math.Max(512f, _viewW / 2f);
                return new Vector2(barCenterX - 512 + AttachedLayerX, _viewH - BarH + AttachedLayerY);
            }
            return new Vector2(_viewW / 2f + PopupOriginX, _viewH - 1 - PopupOriginY);
        }
    }

    private Rectangle SlotRect(int i)
    {
        var g = GridTopLeft;
        return new Rectangle((int)g.X + SlotX0 + SlotStride * (i % 4),
                             (int)g.Y + SlotY0 + SlotStride * (i / 4), SlotSize, SlotSize);
    }

    /// <summary>Bind a skill (dragged from the Skill window) to the slot under the cursor.</summary>
    public bool TryBindSkillAt(int skillId, int x, int y)
    {
        if (!IsVisible) return false;
        for (var i = 0; i < SlotCount; i++)
            if (SlotRect(i).Contains(x, y)) { _bindSkill(_keys[i], skillId); return true; }
        return false;
    }

    private WzSprite? Label(int scancode)
    {
        if (_labelCache.TryGetValue(scancode, out var s)) return s;
        var sprite = _labelRoot?.Get(scancode.ToString(System.Globalization.CultureInfo.InvariantCulture)) is WzCanvas c
            ? _loader.Load(c) : null;
        _labelCache[scancode] = sprite;
        return sprite;
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        for (var i = 0; i < SlotCount; i++)
        {
            var r = SlotRect(i);
            var fk = _bindingAt(_keys[i]);
            if (fk.Type == FuncKeyType.Skill && _skillIcon(fk.Id) is { Texture: { } tex })
                sb.Draw(tex, r, Color.White); // bound skill icon fills the slot
            if (Label(_keys[i]) is { Texture: { } lt } label)
                sb.Draw(lt, new Rectangle(r.X + 2, r.Y + 2, label.Width, label.Height), Color.White);
        }
    }
}
