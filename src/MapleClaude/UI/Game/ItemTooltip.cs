using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Item tooltip card — a pragmatic port of the v95 <c>CUIToolTip::ShowItemToolTip</c> composition:
/// the item icon + name (coloured by potential grade) at the top, then the equip requirements
/// (level / STR-DEX-INT-LUK / job) and the stat bonuses (incPAD/incMAD/… ) read from the WZ
/// <c>info</c> node via <see cref="ItemIconLoader.LoadAttr"/>. Consumables/etc show name + quantity.
///
/// Rendered as a dark semi-transparent card near the cursor (the v95 tooltip frame), clamped to the
/// viewport. Requirement lines are drawn neutrally (the panel doesn't have the player's live stats to
/// red-flag unmet requirements — that refinement is a follow-up).
/// </summary>
public sealed class ItemTooltip
{
    private readonly BuiltInFont _font;
    private readonly ItemIconLoader _icons;

    private static readonly Color[] GradeColor =
    {
        Color.White,                 // 0 none
        new(0x99, 0xCC, 0xFF),       // 1 rare   — light blue
        new(0xCC, 0x99, 0xFF),       // 2 epic   — purple
        new(0xFF, 0xCC, 0x33),       // 3 unique — gold
        new(0x33, 0xFF, 0x66),       // 4 legendary — green
        new(0xFF, 0xAA, 0x55),       // 5 — orange (rare high tier)
    };
    private static readonly Color BodyColor = new(225, 225, 225);
    private static readonly Color StatColor = new(150, 205, 255);
    private static readonly Color ReqColor  = new(210, 210, 210);

    public ItemTooltip(BuiltInFont font, ItemIconLoader icons)
    {
        _font = font;
        _icons = icons;
    }

    public void Draw(SpriteBatch sb, Texture2D white, int itemId, string name, int grade, int quantity,
        int mouseX, int mouseY, int viewW, int viewH)
    {
        var lines = new List<(string text, Color color)>();
        var attr = _icons.LoadAttr(itemId);

        if (attr is { IsEquip: true })
        {
            if (attr.ReqLevel > 0 || attr.ReqStr > 0 || attr.ReqDex > 0 || attr.ReqInt > 0 ||
                attr.ReqLuk > 0 || attr.ReqJob > 0)
            {
                if (attr.ReqLevel > 0) lines.Add(($"REQ LEV : {attr.ReqLevel}", ReqColor));
                if (attr.ReqStr > 0)   lines.Add(($"REQ STR : {attr.ReqStr}", ReqColor));
                if (attr.ReqDex > 0)   lines.Add(($"REQ DEX : {attr.ReqDex}", ReqColor));
                if (attr.ReqInt > 0)   lines.Add(($"REQ INT : {attr.ReqInt}", ReqColor));
                if (attr.ReqLuk > 0)   lines.Add(($"REQ LUK : {attr.ReqLuk}", ReqColor));
                if (attr.ReqFame > 0)  lines.Add(($"REQ FAME : {attr.ReqFame}", ReqColor));
                lines.Add(($"REQ JOB : {JobReq(attr.ReqJob)}", ReqColor));
                lines.Add(("\x01divider", BodyColor));
            }
            AddStat(lines, "STR", attr.IncStr);
            AddStat(lines, "DEX", attr.IncDex);
            AddStat(lines, "INT", attr.IncInt);
            AddStat(lines, "LUK", attr.IncLuk);
            AddStat(lines, "Weapon Atk.", attr.IncPad);
            AddStat(lines, "Magic Atk.", attr.IncMad);
            AddStat(lines, "Weapon Def.", attr.IncPdd);
            AddStat(lines, "Magic Def.", attr.IncMdd);
            AddStat(lines, "MaxHP", attr.IncMhp);
            AddStat(lines, "MaxMP", attr.IncMmp);
            AddStat(lines, "Accuracy", attr.IncAcc);
            AddStat(lines, "Avoidability", attr.IncEva);
            AddStat(lines, "Speed", attr.IncSpeed);
            AddStat(lines, "Jump", attr.IncJump);
            if (attr.Upgrades > 0) lines.Add(($"Upgrades available : {attr.Upgrades}", new Color(255, 215, 120)));
        }
        else
        {
            if (quantity > 1) lines.Add(($"Quantity : {quantity}", BodyColor));
            if (attr is { Only: true }) lines.Add(("One-of-a-kind item", new Color(255, 180, 120)));
        }

        // ── Measure ──────────────────────────────────────────────────────────────
        const int pad = 6, iconBox = 34, lineGap = 2;
        var nameColor = GradeColor[Math.Clamp(grade, 0, GradeColor.Length - 1)];
        var icon = _icons.LoadIcon(itemId);
        var headerH = Math.Max(iconBox, _font.LineHeight + 4);
        var nameW = (int)_font.Measure(name).X;
        var contentW = nameW + (icon != null ? iconBox + 4 : 0);
        foreach (var (text, _) in lines)
            if (text != "\x01divider") contentW = Math.Max(contentW, (int)_font.Measure(text).X);

        var w = contentW + pad * 2;
        var h = pad + headerH + 2;
        foreach (var (text, _) in lines)
            h += text == "\x01divider" ? 5 : _font.LineHeight + lineGap;
        h += pad;

        var x = mouseX + 16;
        var y = mouseY + 16;
        if (x + w > viewW) x = Math.Max(0, mouseX - w - 4);
        if (y + h > viewH) y = Math.Max(0, viewH - h);

        // ── Frame ────────────────────────────────────────────────────────────────
        var rect = new Rectangle(x, y, w, h);
        sb.Draw(white, rect, new Color(8, 8, 16, 235));
        DrawBorder(sb, white, rect, new Color(120, 120, 150));
        DrawBorder(sb, white, new Rectangle(x + 1, y + 1, w - 2, h - 2), new Color(40, 40, 60));

        // Header: icon + name.
        var hx = x + pad;
        if (icon != null)
        {
            var ib = new Rectangle(hx, y + pad, iconBox, iconBox);
            sb.Draw(white, ib, new Color(0, 0, 0, 120));
            var dp = new Vector2(ib.X + (iconBox - icon.Width) / 2f, ib.Y + (iconBox - icon.Height) / 2f) + icon.Origin;
            icon.Draw(sb, dp);
            hx += iconBox + 4;
        }
        _font.Draw(sb, name, new Vector2(hx, y + pad + (headerH - _font.LineHeight) / 2f), nameColor);

        // Body lines.
        var ly = y + pad + headerH + 2;
        foreach (var (text, color) in lines)
        {
            if (text == "\x01divider")
            {
                sb.Draw(white, new Rectangle(x + pad, ly + 2, w - pad * 2, 1), new Color(70, 70, 90));
                ly += 5;
                continue;
            }
            _font.Draw(sb, text, new Vector2(x + pad, ly), color);
            ly += _font.LineHeight + lineGap;
        }
    }

    private static void AddStat(List<(string, Color)> lines, string label, int value)
    {
        if (value != 0) lines.Add(($"{label} : {value:+0;-0}", StatColor));
    }

    // reqJob bitmask: 0/-1 = any; 1 Warrior, 2 Magician, 4 Bowman, 8 Thief, 16 Pirate.
    private static string JobReq(int mask)
    {
        if (mask <= 0) return "Common";
        var parts = new List<string>(5);
        if ((mask & 1) != 0)  parts.Add("Warrior");
        if ((mask & 2) != 0)  parts.Add("Magician");
        if ((mask & 4) != 0)  parts.Add("Bowman");
        if ((mask & 8) != 0)  parts.Add("Thief");
        if ((mask & 16) != 0) parts.Add("Pirate");
        return parts.Count == 0 ? "Common" : string.Join(", ", parts);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
