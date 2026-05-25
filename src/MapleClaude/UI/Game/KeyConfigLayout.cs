using MapleClaude.Domain;
using Microsoft.Xna.Framework;

namespace MapleClaude.UI.Game;

/// <summary>
/// Authentic v95 key-config geometry — a faithful port of
/// <c>CUIKeyConfig::CalcKeyIconPosInfo</c> (which fills <c>s_ptShortKeyPos_0[145]</c>
/// at runtime) plus <c>GetShortCutPos</c> / <c>GetShortCutIndexByPos</c>.
///
/// The keyboard is 6 rows at <c>aLineY = {28,66,99,132,165,198}</c>; each row starts
/// at x=11 and advances per key by a custom width (default 34, from the original
/// <c>aDefrentOffset</c> table). Wide keys then get a fixed nudge so the 32×32 icon
/// centres on them. Indices are DInput scancodes (the same space the server's
/// <c>FuncKeyMappedInit</c> uses); cells are 32×32, hit-tested by point-in-rect.
///
/// The 42-icon palette below the keyboard is <c>s_ptShortKeyPos_0[91..132]</c>:
/// <c>x = 34*col + 6</c>, rows of 18/18/6 at <c>y = 267/301/335</c>.
///
/// All coordinates are panel-relative (the panel is the 629×373 <c>backgrnd</c>).
/// </summary>
internal static class KeyConfigLayout
{
    public const int CellSize = 32;
    public const int PaletteCount = 42;

    // Cell top-left per bindable scancode (nudges already applied). Right-side
    // modifiers (54/89/90) are present so their icon draws on both keys.
    private static readonly Dictionary<int, Point> Cells = new();
    private static readonly Point[] Palette = new Point[PaletteCount];

    // Right modifier -> canonical left modifier (CUIKeyConfig::GetShortCutIndexByPos).
    public const int ScRShift = 54, ScLShift = 42;
    public const int ScRCtrl = 89, ScLCtrl = 29;
    public const int ScRAlt = 90, ScLAlt = 56;

    public static IReadOnlyCollection<int> BindableScancodes => Cells.Keys;

    static KeyConfigLayout()
    {
        BuildKeyboard();
        BuildPalette();
        ApplyWideKeyNudges();
    }

    // ── Row table ──────────────────────────────────────────────────────────────
    // Each entry: (scancode, advanceAfter). scancode -1 = a physical key that
    // occupies a column (advances x) but holds no binding slot (ESC/Tab/PrtSc are
    // baked system keys; Win/arrows aren't in the func-key space). The advance is
    // the width consumed by THIS key before the next; the last key's value is the
    // row terminator and ignored.
    private static readonly int[] LineY = { 28, 66, 99, 132, 165, 198 };

    private static readonly (int sc, int adv)[][] Rows =
    [
        // Row 0 — Esc F1..F12 + Pause/PrtSc/Break (Esc/PrtSc are baked system keys).
        [ (-1,68),(59,34),(60,34),(61,34),(62,42),(63,34),(64,34),(65,34),(66,42),
          (67,34),(68,34),(87,34),(88,42),(-1,34),(-1,34),(-1,0) ],
        // Row 1 — ` 1..0 - = Backspace + Ins Home PgUp.
        [ (41,34),(2,34),(3,34),(4,34),(5,34),(6,34),(7,34),(8,34),(9,34),(10,34),
          (11,34),(12,34),(13,34),(14,58),(82,34),(71,34),(73,0) ],
        // Row 2 — Tab Q..] \ + Del End PgDn (Tab is a baked system key).
        [ (-1,50),(16,34),(17,34),(18,34),(19,34),(20,34),(21,34),(22,34),(23,34),
          (24,34),(25,34),(26,34),(27,34),(43,42),(83,34),(79,34),(81,0) ],
        // Row 3 — Caps A..L ; ' Enter.
        [ (58,67),(30,34),(31,34),(32,34),(33,34),(34,34),(35,34),(36,34),(37,34),
          (38,34),(39,34),(40,34),(28,0) ],
        // Row 4 — LShift Z..M , . / RShift.
        [ (42,84),(44,34),(45,34),(46,34),(47,34),(48,34),(49,34),(50,34),(51,34),
          (52,34),(53,34),(54,0) ],
        // Row 5 — LCtrl Win LAlt Space RAlt [menu] RCtrl. Advances from CalcKeyIconPosInfo
        // (aDefrentOffset 49/50/53/170/56/56); the context-menu key (no binding) sits between
        // RAlt and RCtrl, so RCtrl lands at x=445 — without it RCtrl falls on the menu key.
        [ (29,49),(-1,50),(56,53),(57,170),(90,56),(-1,56),(89,0) ],
    ];

    private static void BuildKeyboard()
    {
        for (var row = 0; row < Rows.Length; row++)
        {
            var x = 11;
            var y = LineY[row];
            foreach (var (sc, adv) in Rows[row])
            {
                if (sc >= 0)
                    Cells[sc] = new Point(x, y);
                x += adv;
            }
        }
    }

    private static void BuildPalette()
    {
        for (var i = 0; i < PaletteCount; i++)
        {
            var col = i % 18;
            var rowY = 267 + 34 * (i / 18);
            Palette[i] = new Point(34 * col + 6, rowY);
        }
    }

    // Centre the icon on the wide modifier/space keys (the trailing adjustments in
    // CalcKeyIconPosInfo).
    private static void ApplyWideKeyNudges()
    {
        Nudge(42, 24);  // LShift
        Nudge(54, 16);  // RShift
        Nudge(29, 8);   // LCtrl
        Nudge(89, 8);   // RCtrl
        Nudge(56, 9);   // LAlt
        Nudge(90, 9);   // RAlt
        Nudge(57, 60);  // Space
    }

    private static void Nudge(int sc, int dx)
    {
        if (Cells.TryGetValue(sc, out var p))
            Cells[sc] = new Point(p.X + dx, p.Y);
    }

    // ── Queries ─────────────────────────────────────────────────────────────────

    public static bool TryGetCell(int scancode, out Point topLeft) =>
        Cells.TryGetValue(scancode, out topLeft);

    public static Point PaletteCell(int slot) => Palette[slot];

    /// <summary>Hit-test a panel-relative point against keyboard cells, returning the
    /// canonical scancode (right modifiers fold to the left), or -1.</summary>
    public static int HitTestKey(int x, int y)
    {
        foreach (var (sc, p) in Cells)
        {
            if (x >= p.X && x < p.X + CellSize && y >= p.Y && y < p.Y + CellSize)
                return sc switch
                {
                    ScRShift => ScLShift,
                    ScRCtrl => ScLCtrl,
                    ScRAlt => ScLAlt,
                    _ => sc,
                };
        }
        return -1;
    }

    /// <summary>Hit-test the icon palette, returning the slot index (0..41) or -1.</summary>
    public static int HitTestPalette(int x, int y)
    {
        for (var i = 0; i < PaletteCount; i++)
        {
            var p = Palette[i];
            if (x >= p.X && x < p.X + CellSize && y >= p.Y && y < p.Y + CellSize)
                return i;
        }
        return -1;
    }

    // ── Palette slot ⇄ action mapping (ResetPaletteItems / GetIdxFromPaletteSlot) ──
    // Slots 0..29  -> MENU        id 0..29
    // Slots 30..34 -> BASICACTION id 50..54
    // Slots 35..41 -> BASICMOTION id 100..106
    public static FuncKeyMapped PaletteBinding(int slot) => slot switch
    {
        < 30 => new FuncKeyMapped(FuncKeyType.Menu, slot),
        < 35 => new FuncKeyMapped(FuncKeyType.BasicAction, 50 + (slot - 30)),
        _ => new FuncKeyMapped(FuncKeyType.BasicMotion, 100 + (slot - 35)),
    };

    /// <summary>The palette slot a fixed (built-in) binding came from, or -1 if the
    /// binding is a skill/item/macro (which never live in the palette).</summary>
    public static int PaletteSlotOf(FuncKeyMapped fk) => fk.Type switch
    {
        FuncKeyType.Menu when fk.Id is >= 0 and < 30 => fk.Id,
        FuncKeyType.BasicAction when fk.Id is >= 50 and <= 54 => 30 + (fk.Id - 50),
        FuncKeyType.BasicMotion or FuncKeyType.Emotion when fk.Id is >= 100 and <= 106 => 35 + (fk.Id - 100),
        _ => -1,
    };
}
