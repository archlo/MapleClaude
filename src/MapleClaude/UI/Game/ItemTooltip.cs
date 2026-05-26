using MapleClaude.Character;
using MapleClaude.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Authentic v95 item tooltip. Ports the body layout of <c>CUIToolTip</c> — the requirement
/// rows (<c>REQ LEV/STR/DEX/INT/LUK/POP</c>) drawn from <c>UIWindow2.img/ToolTip/Equip/{Can,Cannot}</c>
/// bitmap labels, the digit composition (<c>draw_number_by_image</c>) from the same canvases,
/// and the 6-class strip drawn from <c>Can|Cannot/{beginner,warrior,magician,bowman,thief,pirate}</c>.
/// The background box is the code-drawn <c>InitCanvas</c> recipe: dark blue fill
/// (<c>0xCC0E395A</c> = R14/G57/B90/A204, the IDB-verified <c>uColor</c> from
/// <c>SetToolTip_Equip</c>'s <c>MakeLayer</c> call), four white corner pixels, and a 1-px
/// inner outline (<c>bDoubleOutline=1</c>).
///
/// Non-equip path follows the reference screenshot (e.g. White Scroll 2340000): bullet + name
/// top-left, faint divider, two-column body (icon left, description wrapping right), faint
/// divider, "Item ID : NNNN" footer in the yellow-green tag colour.
///
/// Variable text (name, description, footer) still flows through <see cref="BuiltInFont"/>,
/// mirroring the v95 client which uses GDI for the same fields. Fixed labels and digits come
/// from <see cref="TooltipAssets"/>.
/// </summary>
public sealed class ItemTooltip
{
    private readonly BuiltInFont _font;
    private readonly ItemIconLoader _icons;
    private readonly TooltipAssets _assets;
    private readonly Func<int, string?>? _descOf;

    // Equip-tooltip uColor from CUIToolTip::SetToolTip_Equip MakeLayer call:
    //   neg eax / sbb eax,eax / and eax,0xD431C6A6 / add eax,0xCC0E395A
    //   non-epic → 0xCC0E395A → ARGB(A=204, R=14, G=57, B=90), the deep blue-grey behind every equip tooltip.
    private static readonly Color BgColor      = new(14, 57, 90, 204);
    private static readonly Color CornerWhite  = Color.White;
    private static readonly Color InnerOutline = new(255, 255, 255, 90);
    private static readonly Color DividerThin  = new(255, 255, 255, 28);
    private static readonly Color NameColor    = Color.White;
    private static readonly Color DescColor    = new(238, 238, 224);
    private static readonly Color StatColor    = new(214, 220, 230);
    private static readonly Color InfoColor    = new(190, 200, 220);
    // Item-ID footer colour matches the yellow-green tag in the reference screenshot.
    private static readonly Color IdColor      = new(230, 230, 110);

    private static readonly string[] JobKlassNames =
        { "beginner", "warrior", "magician", "bowman", "thief", "pirate" };
    // Fixed x columns from CUIToolTip::DrawItemReqJob (beginner@10, warrior@52, ...).
    private static readonly int[] JobX = { 10, 52, 92, 132, 171, 197 };

    // Tooltip width matches the v95 layout (job-strip pirate@197 + class-label width ~29 + right pad ~18).
    private const int EquipWidth = 244;

    // Equip layout constants from DrawItemIcon / DrawTextEquip_Req / DrawItemReqJob:
    private const int IconX        = 10;
    private const int IconSize     = 68;
    private const int ReqLabelX    = 94;
    private const int ReqValueRight = 144;   // value column right edge — digits right-align here
    private const int ReqRowStep   = 12;
    private const int ReqRowBaseDY = 32;     // first req row is icon_y + 32
    private const int JobStripDY   = 141;    // job strip is icon_y + 141

    // Player-stat baseline used to flag req rows red when unmet (Cannot/* canvases).
    private int _pLevel, _pStr, _pDex, _pInt, _pLuk;

    public ItemTooltip(BuiltInFont font, ItemIconLoader icons, TooltipAssets assets,
        Func<int, string?>? descOf = null, BuiltInFont? tabFont = null)
    {
        _font = font;
        _icons = icons;
        _assets = assets;
        _descOf = descOf;
        _ = tabFont; // legacy param — bitmap class labels no longer need a separate small font
    }

    /// <summary>Push the player's stats so the tooltip can flag requirement rows red on items
    /// the character can't actually wear (mirrors the v95 <c>m_pNumberCannot</c> branch in
    /// <c>DrawTextEquip_Req_Level</c>). <c>jobId</c> is kept for call-site symmetry; the job
    /// strip keys off the item's own <c>reqJob</c> mask, not the player's class.</summary>
    public void SetPlayer(int level, int str, int dex, int intt, int luk, int jobId)
    {
        _pLevel = level; _pStr = str; _pDex = dex; _pInt = intt; _pLuk = luk;
        _ = jobId;
    }

    public void Draw(SpriteBatch sb, Texture2D white, int itemId, string name, int grade, int quantity,
        int mouseX, int mouseY, int viewW, int viewH)
    {
        var attr = _icons.LoadAttr(itemId);
        var isEquip = attr is { IsEquip: true } || itemId / 1_000_000 == 1;
        if (isEquip)
            DrawEquip(sb, white, itemId, name, grade, attr, mouseX, mouseY, viewW, viewH);
        else
            DrawConsumable(sb, white, itemId, name, attr, mouseX, mouseY, viewW, viewH);
    }

    // ───────────────────────── Equip path ────────────────────────────────────────

    private void DrawEquip(SpriteBatch sb, Texture2D white, int itemId, string name, int grade,
        ItemAttr? attr, int mouseX, int mouseY, int viewW, int viewH)
    {
        var lh = _font.LineHeight;
        var w = EquipWidth;

        // Walk the layout top→bottom once to compute heights, then again to draw at the same y's.
        // Sections: name → dot → block(icon|req|job) → dot → info → desc → dot → id.
        var info = BuildInfoLines(itemId, attr);
        var desc = _descOf?.Invoke(itemId);
        var descLines = string.IsNullOrEmpty(desc)
            ? new List<string>()
            : WrapText(desc!, w - 14);

        var yName  = 6;
        var yDot1  = yName + lh + 3;
        var yBlock = yDot1 + 5;
        var yBlockBottom = yBlock + JobStripDY + 13;   // job strip is 13 px tall at icon_y + 141
        var yDot2  = yBlockBottom + 6;
        var yInfo  = yDot2 + 6;
        var yInfoBottom = yInfo + info.Count * (lh - 2);
        var yDot3  = info.Count > 0 ? yInfoBottom + 6 : yDot2;
        var yDesc  = (descLines.Count > 0 ? yDot3 + (info.Count > 0 ? 6 : 0) : yDot3);
        var yDescBottom = descLines.Count > 0 ? yDesc + descLines.Count * (lh - 1) : yDesc;
        var yDot4  = yDescBottom + (descLines.Count > 0 ? 6 : 0);
        var yId    = yDot4 + 6;
        var yIdBottom = yId + lh;
        var h = yIdBottom + 6;

        // Position the tooltip against the cursor; flip to the left if it would clip the right edge.
        var x = mouseX + 16;
        var y = mouseY + 16;
        if (x + w > viewW) x = Math.Max(0, mouseX - w - 4);
        if (y + h > viewH) y = Math.Max(0, viewH - h);

        DrawBackground(sb, white, x, y, w, h);

        // Name — centred, grade-coloured (matches DrawItemTitle's font=3 grade-palette logic).
        var nameColor = grade > 0 ? GradeColor(grade) : NameColor;
        var nameWidth = (int)_font.Measure(name).X;
        _font.Draw(sb, name, new Vector2(x + (w - nameWidth) / 2, y + yName), nameColor);

        DrawDotLine(sb, white, x + 7, y + yDot1, w - 14);

        // Icon well (68×68) — the v95 well is filled with 0xA0F0F000 (semi-transparent grey-black).
        var iconRect = new Rectangle(x + IconX, y + yBlock, IconSize, IconSize);
        sb.Draw(white, iconRect, new Color(15, 25, 40, 160));
        DrawThinBorder(sb, white, iconRect, new Color(255, 255, 255, 40));
        var icon = _icons.LoadIcon(itemId);
        if (icon is not null)
        {
            // Scale the inventory-sized icon up so it actually reads at hover distance.
            var scale = Math.Min(2.4f, (IconSize - 6f) / Math.Max(icon.Width, icon.Height));
            var dw = (int)(icon.Width * scale);
            var dh = (int)(icon.Height * scale);
            sb.Draw(icon.Texture,
                new Rectangle(iconRect.X + (IconSize - dw) / 2, iconRect.Y + (IconSize - dh) / 2, dw, dh),
                Color.White);
        }
        // Cash-item gold-coin overlay → WZ `cash` canvas (replaces the old code-drawn disc).
        if (attr is { Cash: true })
        {
            TooltipAssets.BlitAt(sb, _assets.Cash, iconRect.X + 1, iconRect.Bottom - (_assets.Cash?.Height ?? 5) - 1);
        }

        // Requirement rows — 6 bitmap labels + bitmap digit values right-aligned to x+144.
        DrawReqRow(sb, x, y + yBlock, 0, "reqLEV", attr?.ReqLevel ?? 0, Unmet(_pLevel, attr?.ReqLevel ?? 0), bNone: false);
        DrawReqRow(sb, x, y + yBlock, 1, "reqSTR", attr?.ReqStr   ?? 0, Unmet(_pStr,   attr?.ReqStr   ?? 0), bNone: false);
        DrawReqRow(sb, x, y + yBlock, 2, "reqDEX", attr?.ReqDex   ?? 0, Unmet(_pDex,   attr?.ReqDex   ?? 0), bNone: false);
        DrawReqRow(sb, x, y + yBlock, 3, "reqINT", attr?.ReqInt   ?? 0, Unmet(_pInt,   attr?.ReqInt   ?? 0), bNone: false);
        DrawReqRow(sb, x, y + yBlock, 4, "reqLUK", attr?.ReqLuk   ?? 0, Unmet(_pLuk,   attr?.ReqLuk   ?? 0), bNone: false);
        // POP/FAME uses the bNone branch when zero (renders the "—" dash from Can/none, not "0").
        var reqPop = attr?.ReqFame ?? 0;
        DrawReqRow(sb, x, y + yBlock, 5, "reqPOP", reqPop, bad: false, bNone: reqPop == 0);

        // Job-class strip — 6 bitmap labels at fixed x columns, grey-Cannot when the item's
        // job-mask excludes that class. Mask bits: 1=Warrior 2=Mag 4=Bow 8=Thf 16=Pir; 0=any.
        var mask = attr?.ReqJob ?? 0;
        var jobStripY = y + yBlock + JobStripDY;
        // Beginner: greyed unless any-class item (mask == 0).
        DrawJob(sb, x, jobStripY, 0, mask != 0 && mask != -1 && mask >= 0);
        DrawJob(sb, x, jobStripY, 1, mask != 0 && (mask & 0x01) == 0 || mask == -1);
        DrawJob(sb, x, jobStripY, 2, mask != 0 && (mask & 0x02) == 0 || mask == -1);
        DrawJob(sb, x, jobStripY, 3, mask != 0 && (mask & 0x04) == 0 || mask == -1);
        DrawJob(sb, x, jobStripY, 4, mask != 0 && (mask & 0x08) == 0 || mask == -1);
        DrawJob(sb, x, jobStripY, 5, mask != 0 && (mask & 0x10) == 0 || mask == -1);

        DrawDotLine(sb, white, x + 7, y + yDot2, w - 14);

        // Info — category/attack speed via WZ bitmaps where the index is known, otherwise GDI.
        var cy = y + yInfo;
        foreach (var line in info)
        {
            DrawInfoLine(sb, x, cy, line);
            cy += lh - 2;
        }

        if (descLines.Count > 0)
        {
            DrawDotLine(sb, white, x + 7, y + yDot3, w - 14);
            cy = y + yDesc;
            foreach (var line in descLines)
            {
                _font.Draw(sb, line, new Vector2(x + 7, cy), DescColor);
                cy += lh - 1;
            }
        }

        DrawDotLine(sb, white, x + 7, y + yDot4, w - 14);
        _font.Draw(sb, $"Item ID : {itemId}", new Vector2(x + 7, y + yId), IdColor);
    }

    private void DrawReqRow(SpriteBatch sb, int x, int blockY, int nNo, string key, int reqValue,
        bool bad, bool bNone)
    {
        var rowY = blockY + ReqRowBaseDY + ReqRowStep * nNo;
        var label = _assets.Req(key, met: !bad);
        TooltipAssets.BlitAt(sb, label, x + ReqLabelX, rowY);

        if (bNone)
        {
            // "none" dash from Can/none — same column right-edge alignment as digits.
            var none = _assets.Digit(-1, met: true);
            TooltipAssets.BlitAt(sb, none, x + ReqValueRight - (none?.Width ?? 0), rowY);
            return;
        }

        var met = !bad;
        var valueW = _assets.MeasureNumber(reqValue, met);
        _assets.DrawNumber(sb, x + ReqValueRight - valueW, rowY, reqValue, met);
    }

    private void DrawJob(SpriteBatch sb, int x, int y, int slot, bool greyed)
    {
        var label = _assets.JobLabel(JobKlassNames[slot], greyed);
        TooltipAssets.BlitAt(sb, label, x + JobX[slot], y);
    }

    // ───────────────────────── Non-equip path ─────────────────────────────────────

    private void DrawConsumable(SpriteBatch sb, Texture2D white, int itemId, string name,
        ItemAttr? attr, int mouseX, int mouseY, int viewW, int viewH)
    {
        const int IconWell = 36;     // tighter than the equip 68×68 — non-equip icons are usually 32×32
        const int IconGap  = 8;
        const int Pad      = 7;

        var lh = _font.LineHeight;
        // Width: pick something that gives the description ~180 px of wrap width.
        const int w = 232;
        var iconCol = Pad + IconWell;
        var descX   = iconCol + IconGap;
        var descW   = w - descX - Pad;

        var desc = _descOf?.Invoke(itemId) ?? string.Empty;
        var descLines = string.IsNullOrEmpty(desc) ? new List<string>() : WrapText(desc, descW);

        // Layout y's.
        var yName = 5;
        var yDiv1 = yName + lh + 3;
        var yBody = yDiv1 + 4;
        var bodyH = Math.Max(IconWell, descLines.Count * (lh - 1));
        var yDiv2 = yBody + bodyH + 4;
        var yId   = yDiv2 + 4;
        var h     = yId + lh + 5;

        var x = mouseX + 16;
        var y = mouseY + 16;
        if (x + w > viewW) x = Math.Max(0, mouseX - w - 4);
        if (y + h > viewH) y = Math.Max(0, viewH - h);

        DrawBackground(sb, white, x, y, w, h);

        // Header — bullet dot + name, left-aligned (matches the screenshot, NOT centred).
        sb.Draw(white, new Rectangle(x + Pad, y + yName + (lh / 2) - 1, 3, 3), Color.White);
        _font.Draw(sb, name, new Vector2(x + Pad + 6, y + yName), NameColor);

        // Subtle separator under the header.
        sb.Draw(white, new Rectangle(x + Pad, y + yDiv1, w - Pad * 2, 1), DividerThin);

        // Icon well — slightly darker square; small inner border.
        var iconRect = new Rectangle(x + Pad, y + yBody, IconWell, IconWell);
        sb.Draw(white, iconRect, new Color(0, 0, 0, 60));
        var icon = _icons.LoadIcon(itemId);
        if (icon is not null)
        {
            // Don't upscale non-equip icons; centre at native size inside the well.
            var dw = Math.Min(icon.Width, IconWell - 2);
            var dh = Math.Min(icon.Height, IconWell - 2);
            sb.Draw(icon.Texture,
                new Rectangle(iconRect.X + (IconWell - dw) / 2, iconRect.Y + (IconWell - dh) / 2, dw, dh),
                Color.White);
        }
        if (attr is { Cash: true })
        {
            TooltipAssets.BlitAt(sb, _assets.Cash, iconRect.X + 1, iconRect.Bottom - (_assets.Cash?.Height ?? 5) - 1);
        }

        // Description — wraps in the right column.
        var cy = y + yBody;
        foreach (var line in descLines)
        {
            _font.Draw(sb, line, new Vector2(x + descX, cy), DescColor);
            cy += lh - 1;
        }

        sb.Draw(white, new Rectangle(x + Pad, y + yDiv2, w - Pad * 2, 1), DividerThin);

        _font.Draw(sb, $"Item ID : {itemId}", new Vector2(x + Pad, y + yId), IdColor);
    }

    // ───────────────────────── Info-line building ─────────────────────────────────

    private List<InfoLine> BuildInfoLines(int itemId, ItemAttr? attr)
    {
        var lines = new List<InfoLine>();
        if (attr is null) return lines;

        // Category — weapon (130→47) → WeaponCategory/<i>, armour (100→121) → ItemCategory/<i>.
        var cat = itemId / 10000;
        if (cat is >= 130 and <= 147)
        {
            var wc = _assets.WeaponCategory(cat - 100);
            if (wc is not null) lines.Add(new InfoLine(InfoKind.Bitmap, wc, null));
            else lines.Add(new InfoLine(InfoKind.Text, null, $"CATEGORY : {WeaponCategoryText(cat)}"));
        }
        else if (cat is >= 100 and <= 121)
        {
            var ic = _assets.ItemCategory(cat - 99);
            if (ic is not null) lines.Add(new InfoLine(InfoKind.Bitmap, ic, null));
            else lines.Add(new InfoLine(InfoKind.Text, null, $"CATEGORY : {ItemCategoryText(cat)}"));
        }

        // Attack speed — Speed/<attackSpeed-2>: 2→Faster ... 8→Slower.
        if (attr.AttackSpeed > 0)
        {
            var sp = _assets.Speed(attr.AttackSpeed - 2);
            if (sp is not null) lines.Add(new InfoLine(InfoKind.Bitmap, sp, null));
            else lines.Add(new InfoLine(InfoKind.Text, null, $"ATK SPEED : {attr.AttackSpeed}"));
        }

        // Stat increments — GDI for now; the WZ Property/<i> index→stat mapping is
        // pending a runtime visual confirmation (see plan file).
        AddStat(lines, "STR", attr.IncStr);
        AddStat(lines, "DEX", attr.IncDex);
        AddStat(lines, "INT", attr.IncInt);
        AddStat(lines, "LUK", attr.IncLuk);
        AddStat(lines, "Weapon Atk.", attr.IncPad);
        AddStat(lines, "Magic Atk.",  attr.IncMad);
        AddStat(lines, "Weapon Def.", attr.IncPdd);
        AddStat(lines, "Magic Def.",  attr.IncMdd);
        AddStat(lines, "MaxHP",       attr.IncMhp);
        AddStat(lines, "MaxMP",       attr.IncMmp);
        AddStat(lines, "Accuracy",    attr.IncAcc);
        AddStat(lines, "Avoidability", attr.IncEva);
        AddStat(lines, "Speed",       attr.IncSpeed);
        AddStat(lines, "Jump",        attr.IncJump);

        if (attr.Upgrades > 0)
            lines.Add(new InfoLine(InfoKind.Text, null, $"Number Of Upgrades Available : {attr.Upgrades}"));

        return lines;
    }

    private static void AddStat(List<InfoLine> lines, string label, int value)
    {
        if (value == 0) return;
        lines.Add(new InfoLine(InfoKind.Text, null, $"{label} : +{value}"));
    }

    private void DrawInfoLine(SpriteBatch sb, int x, int y, InfoLine line)
    {
        switch (line.Kind)
        {
            case InfoKind.Bitmap when line.Sprite is not null:
                TooltipAssets.BlitAt(sb, line.Sprite, x + 10, y);
                break;
            case InfoKind.Text when line.Text is not null:
                _font.Draw(sb, line.Text, new Vector2(x + 10, y), StatColor);
                break;
        }
    }

    private enum InfoKind { Text, Bitmap }
    private readonly record struct InfoLine(InfoKind Kind, WzSprite? Sprite, string? Text);

    // ───────────────────────── Shared drawing helpers ─────────────────────────────

    private static void DrawBackground(SpriteBatch sb, Texture2D white, int x, int y, int w, int h)
    {
        // CUIToolTip::InitCanvas — solid fill, 4 white corner pixels, then the bDoubleOutline
        // inner outline (equip tooltips pass bDoubleOutline=1).
        sb.Draw(white, new Rectangle(x, y, w, h), BgColor);
        sb.Draw(white, new Rectangle(x, y, 1, 1), CornerWhite);
        sb.Draw(white, new Rectangle(x + w - 1, y, 1, 1), CornerWhite);
        sb.Draw(white, new Rectangle(x, y + h - 1, 1, 1), CornerWhite);
        sb.Draw(white, new Rectangle(x + w - 1, y + h - 1, 1, 1), CornerWhite);
        sb.Draw(white, new Rectangle(x + 1, y + 2, 1, h - 4), InnerOutline);
        sb.Draw(white, new Rectangle(x + w - 2, y + 2, 1, h - 4), InnerOutline);
        sb.Draw(white, new Rectangle(x + 2, y + 1, w - 4, 1), InnerOutline);
        sb.Draw(white, new Rectangle(x + 2, y + h - 2, w - 4, 1), InnerOutline);
    }

    private void DrawDotLine(SpriteBatch sb, Texture2D white, int x, int y, int w)
    {
        var dot = _assets.Dot(1);
        if (dot is null)
        {
            sb.Draw(white, new Rectangle(x, y, w, 1), DividerThin);
            return;
        }
        var step = dot.Width + 2;
        for (var px = x; px <= x + w - dot.Width; px += step)
        {
            sb.Draw(dot.Texture, new Vector2(px - dot.Origin.X, y - dot.Origin.Y), Color.White);
        }
    }

    private static void DrawThinBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private static bool Unmet(int have, int req) => have > 0 && have < req;

    private List<string> WrapText(string text, int maxW)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\\r", "\n").Replace("\\n", "\n").Replace("\r", "").Split('\n'))
        {
            var cur = "";
            foreach (var word in paragraph.Split(' '))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (_font.Measure(trial).X > maxW && cur.Length > 0)
                {
                    lines.Add(cur);
                    cur = word;
                }
                else
                {
                    cur = trial;
                }
            }
            lines.Add(cur);
        }
        return lines;
    }

    private static Color GradeColor(int g) => g switch
    {
        1 => new Color(0x77, 0xCC, 0xFF),
        2 => new Color(0xCC, 0x88, 0xFF),
        3 => new Color(0xFF, 0xCC, 0x33),
        4 => new Color(0x55, 0xEE, 0x77),
        _ => Color.White,
    };

    private static string WeaponCategoryText(int cat) => cat switch
    {
        130 => "One-Handed Sword",
        131 => "One-Handed Axe",
        132 => "One-Handed Blunt Weapon",
        133 => "Dagger",
        137 => "Wand",
        138 => "Staff",
        140 => "Two-Handed Sword",
        141 => "Two-Handed Axe",
        142 => "Two-Handed Blunt Weapon",
        143 => "Spear",
        144 => "Pole Arm",
        145 => "Bow",
        146 => "Crossbow",
        147 => "Claw",
        _ => string.Empty,
    };

    private static string ItemCategoryText(int cat) => cat switch
    {
        100 => "Hat",
        101 => "Face Accessory",
        102 => "Eye Accessory",
        103 => "Earring",
        104 => "Top",
        105 => "Overall",
        106 => "Bottom",
        107 => "Shoes",
        108 => "Glove",
        109 => "Shield",
        110 => "Cape",
        111 => "Ring",
        112 => "Pendant",
        113 => "Belt",
        114 => "Medal",
        115 => "Shoulder",
        _ => string.Empty,
    };

    // ───────────────────────── Public helpers ──────────────────────────────────────

    /// <summary>Small gold-coin overlay used to mark a cash item in an inventory grid cell.
    /// Kept as a public static so EquipInventory and ItemInventory can stamp the indicator
    /// directly onto their slot icons without needing the full tooltip context.</summary>
    public static void DrawCashCoin(SpriteBatch sb, Texture2D white, int x, int y)
    {
        const int r = 5;
        var cx = x + r;
        var cy = y + r;
        FillDisc(sb, white, cx, cy, r, new Color(168, 116, 16));        // rim
        FillDisc(sb, white, cx, cy, r - 1, new Color(255, 205, 50));    // gold
        sb.Draw(white, new Rectangle(cx - 2, cy - 3, 2, 2), new Color(255, 242, 175)); // highlight
    }

    private static void FillDisc(SpriteBatch sb, Texture2D white, int cx, int cy, int r, Color c)
    {
        for (var dy = -r; dy <= r; dy++)
        {
            var dx = (int)Math.Sqrt(Math.Max(0, r * r - dy * dy));
            sb.Draw(white, new Rectangle(cx - dx, cy + dy, 2 * dx + 1, 1), c);
        }
    }
}
