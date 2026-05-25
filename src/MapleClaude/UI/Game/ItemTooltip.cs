using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace MapleClaude.UI.Game;

/// <summary>
/// Item tooltip card, styled after the requested reference (the WzComparerR-style preview): a blue
/// gradient frame with a bulleted name, the full requirement block (REQ LEV/STR/DEX/INT/LUK/FAM +
/// ITEM LEV/EXP), a row of job-requirement pills (BEGINNER … PIRATE), bulleted category / stat /
/// upgrade / hammer lines, the item description, the sell price (with a gold coin) and the item id.
/// Cash items get a gold coin overlaid on the icon (here and in the inventory/equip slots via
/// <see cref="DrawCashCoin"/>).
///
/// Requirement values turn red when the player can't meet them; the player's stats arrive via
/// <see cref="SetPlayer"/> and the description text via a String.wz lookup delegate.
/// </summary>
public sealed class ItemTooltip
{
    private readonly BuiltInFont _font;
    private readonly ItemIconLoader _icons;
    private readonly Func<int, string?>? _descOf;

    private static readonly Color FrameTop = new(60, 92, 156, 245);
    private static readonly Color FrameBot = new(20, 34, 70, 245);
    private static readonly Color Border   = new(150, 180, 225);
    private static readonly Color NameColor = Color.White;
    private static readonly Color ReqLabel = new(180, 192, 214);
    private static readonly Color ReqVal   = new(235, 240, 250);
    private static readonly Color ReqBad   = new(235, 80, 80);
    private static readonly Color Dash     = new(120, 130, 150);
    private static readonly Color StatColor = new(205, 216, 236);
    private static readonly Color InfoColor = new(170, 184, 210);
    private static readonly Color IdColor   = new(150, 165, 195);
    private static readonly Color DescColor = new(195, 205, 225);
    private static readonly Color DividerC  = new(90, 110, 150, 160);
    private static readonly Color PillOn    = new(200, 52, 52);
    private static readonly Color PillOff   = new(64, 46, 52);
    private static readonly Color PillTxtOn = Color.White;
    private static readonly Color PillTxtOff = new(135, 120, 122);

    private static readonly string[] JobPills = { "BEGINNER", "WARRIOR", "MAGICIAN", "BOWMAN", "THIEF", "PIRATE" };

    private int _pLevel, _pStr, _pDex, _pInt, _pLuk, _pJob;

    public ItemTooltip(BuiltInFont font, ItemIconLoader icons, Func<int, string?>? descOf = null)
    {
        _font = font;
        _icons = icons;
        _descOf = descOf;
    }

    public void SetPlayer(int level, int str, int dex, int intt, int luk, int jobId)
    {
        _pLevel = level; _pStr = str; _pDex = dex; _pInt = intt; _pLuk = luk; _pJob = jobId;
    }

    public void Draw(SpriteBatch sb, Texture2D white, int itemId, string name, int grade, int quantity,
        int mouseX, int mouseY, int viewW, int viewH)
    {
        var lh = _font.LineHeight;
        var attr = _icons.LoadAttr(itemId);
        var isEquip = attr is { IsEquip: true } || itemId / 1_000_000 == 1;
        const int pad = 8, iconBox = 34, iconGap = 8, pillGap = 3, pillPad = 4;

        // ── Requirement rows (equips): label + value text + unmet flag ─────────────
        var reqs = new List<(string label, string val, bool bad)>();
        if (isEquip)
        {
            var a = attr ?? new ItemAttr();
            reqs.Add(("REQ LEV", a.ReqLevel.ToString(), _pLevel > 0 && _pLevel < a.ReqLevel));
            reqs.Add(("REQ STR", a.ReqStr.ToString(), Unmet(_pStr, a.ReqStr)));
            reqs.Add(("REQ DEX", a.ReqDex.ToString(), Unmet(_pDex, a.ReqDex)));
            reqs.Add(("REQ INT", a.ReqInt.ToString(), Unmet(_pInt, a.ReqInt)));
            reqs.Add(("REQ LUK", a.ReqLuk.ToString(), Unmet(_pLuk, a.ReqLuk)));
            reqs.Add(("REQ FAM", a.ReqFame > 0 ? a.ReqFame.ToString() : "-", false));
            reqs.Add(("ITEM LEV", "-", false));
            reqs.Add(("ITEM EXP", "-", false));
        }

        // ── Job pills (equips) ─────────────────────────────────────────────────────
        var mask = attr?.ReqJob ?? 0;
        var jobOk = new bool[6];
        if (mask <= 0) { for (var i = 0; i < 6; i++) jobOk[i] = true; }
        else { jobOk[1] = (mask & 1) != 0; jobOk[2] = (mask & 2) != 0; jobOk[3] = (mask & 4) != 0; jobOk[4] = (mask & 8) != 0; jobOk[5] = (mask & 16) != 0; }

        // ── Bulleted info lines ────────────────────────────────────────────────────
        var info = new List<(string text, Color c)>();
        if (isEquip)
        {
            var cat = Category(itemId);
            if (cat.Length > 0) info.Add(($"· CATEGORY : {cat}", InfoColor));
            if (attr != null)
            {
                AddStat(info, "STR", attr.IncStr); AddStat(info, "DEX", attr.IncDex);
                AddStat(info, "INT", attr.IncInt); AddStat(info, "LUK", attr.IncLuk);
                AddStat(info, "Weapon Atk.", attr.IncPad); AddStat(info, "Magic Atk.", attr.IncMad);
                AddStat(info, "Weapon Def.", attr.IncPdd); AddStat(info, "Magic Def.", attr.IncMdd);
                AddStat(info, "MaxHP", attr.IncMhp); AddStat(info, "MaxMP", attr.IncMmp);
                AddStat(info, "Accuracy", attr.IncAcc); AddStat(info, "Avoidability", attr.IncEva);
                AddStat(info, "Speed", attr.IncSpeed); AddStat(info, "Jump", attr.IncJump);
                if (attr.AttackSpeed > 0) info.Add(($"· ATTACK SPEED : {SpeedLabel(attr.AttackSpeed)}", StatColor));
                info.Add(($"· NUMBER OF UPGRADES AVAILABLE : {attr.Upgrades}", InfoColor));
                info.Add(("· NUMBER OF VICIOUS' HAMMER APPLIED : 0", InfoColor));
            }
        }

        // ── Description ─────────────────────────────────────────────────────────────
        const int wrapW = 250;
        var desc = _descOf?.Invoke(itemId);
        var descLines = string.IsNullOrEmpty(desc) ? new List<string>() : Wrap(desc, wrapW);

        var price = attr?.Price ?? 0;
        var idLine = $"Item ID : {itemId}";

        // ── Measure ─────────────────────────────────────────────────────────────────
        var dot = 5;
        var nameW = dot + 3 + (int)_font.Measure(name).X;
        var reqW  = reqs.Count == 0 ? 0 : reqs.Max(r => (int)_font.Measure($"{r.label} : {r.val}").X);
        var blockW = reqs.Count == 0 ? iconBox : iconBox + iconGap + reqW;
        var pillsW = isEquip ? JobPills.Sum(p => (int)_font.Measure(p).X + pillPad * 2 + pillGap) - pillGap : 0;
        var infoW = info.Count == 0 ? 0 : info.Max(t => (int)_font.Measure(t.text).X);
        var descW = descLines.Count == 0 ? 0 : descLines.Max(s => (int)_font.Measure(s).X);
        var idW   = (int)_font.Measure(idLine).X;
        var contentW = new[] { nameW, blockW, pillsW, infoW, descW, idW, 130 }.Max();
        var w = contentW + pad * 2;

        // Height.
        var priceH = price > 0 ? lh + 2 : 0;
        var leftColH = iconBox + priceH;
        var blockH = Math.Max(leftColH, reqs.Count * lh);
        var h = pad + lh + 3 + Div() + blockH;         // name + divider + (icon | reqs)
        if (isEquip) h += lh + 6;                      // job pills
        h += Div() + info.Count * lh;                  // info section
        if (descLines.Count > 0) h += Div() + descLines.Count * lh;
        h += Div() + lh + pad;                         // item id

        var x = mouseX + 16;
        var y = mouseY + 16;
        if (x + w > viewW) x = Math.Max(0, mouseX - w - 4);
        if (y + h > viewH) y = Math.Max(0, viewH - h);

        // ── Frame (blue vertical gradient + rounded border) ─────────────────────────
        for (var i = 0; i < h; i++)
            sb.Draw(white, new Rectangle(x, y + i, w, 1), Color.Lerp(FrameTop, FrameBot, i / (float)Math.Max(1, h - 1)));
        DrawRoundBorder(sb, white, new Rectangle(x, y, w, h), Border);

        var cy = y + pad;

        // Name with a bullet dot.
        sb.Draw(white, new Rectangle(x + pad, cy + lh / 2 - 2, 3, 3), new Color(220, 230, 250));
        _font.Draw(sb, name, new Vector2(x + pad + dot + 3, cy), grade > 0 ? GradeColor(grade) : NameColor);
        cy += lh + 3;
        cy = Divline(sb, white, x, cy, w, pad);

        // Icon + requirement block.
        var blockTop = cy;
        var iconRect = new Rectangle(x + pad, blockTop, iconBox, iconBox);
        sb.Draw(white, iconRect, new Color(0, 0, 0, 90));
        DrawBorder(sb, white, iconRect, new Color(110, 130, 165));
        var icon = _icons.LoadIcon(itemId);
        if (icon != null)
            icon.Draw(sb, new Vector2(iconRect.X + (iconBox - icon.Width) / 2f, iconRect.Y + (iconBox - icon.Height) / 2f) + icon.Origin);
        if (attr is { Cash: true }) DrawCashCoin(sb, white, iconRect.X + 1, iconRect.Bottom - 11);

        if (price > 0)
        {
            DrawCashCoin(sb, white, x + pad, blockTop + iconBox + 1);
            _font.Draw(sb, price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                new Vector2(x + pad + 13, blockTop + iconBox + 2), new Color(235, 215, 120));
        }

        var rx = x + pad + iconBox + iconGap;
        var ry = blockTop;
        foreach (var (label, val, bad) in reqs)
        {
            var lp = $"{label} : ";
            _font.Draw(sb, lp, new Vector2(rx, ry), ReqLabel);
            _font.Draw(sb, val, new Vector2(rx + _font.Measure(lp).X, ry), val == "-" ? Dash : (bad ? ReqBad : ReqVal));
            ry += lh;
        }
        cy = blockTop + blockH;

        // Job pills.
        if (isEquip)
        {
            var px = x + pad;
            for (var i = 0; i < 6; i++)
            {
                var pw = (int)_font.Measure(JobPills[i]).X + pillPad * 2;
                var pr = new Rectangle(px, cy, pw, lh + 2);
                sb.Draw(white, pr, jobOk[i] ? PillOn : PillOff);
                _font.Draw(sb, JobPills[i], new Vector2(px + pillPad, cy + 1), jobOk[i] ? PillTxtOn : PillTxtOff);
                px += pw + pillGap;
            }
            cy += lh + 6;
        }

        // Info (category / stats / upgrades / hammer).
        cy = Divline(sb, white, x, cy, w, pad);
        foreach (var (text, c) in info) { _font.Draw(sb, text, new Vector2(x + pad, cy), c); cy += lh; }

        // Description.
        if (descLines.Count > 0)
        {
            cy = Divline(sb, white, x, cy, w, pad);
            foreach (var s in descLines) { _font.Draw(sb, s, new Vector2(x + pad, cy), DescColor); cy += lh; }
        }

        // Item id.
        cy = Divline(sb, white, x, cy, w, pad);
        _font.Draw(sb, idLine, new Vector2(x + pad, cy), IdColor);
    }

    // ── Cash coin (also used by the inventory/equip slot renderers) ──────────────────
    public static void DrawCashCoin(SpriteBatch sb, Texture2D white, int x, int y)
    {
        const int r = 5;
        int cx = x + r, cy = y + r;
        FillDisc(sb, white, cx, cy, r, new Color(168, 116, 16));       // rim
        FillDisc(sb, white, cx, cy, r - 1, new Color(255, 205, 50));   // gold
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

    // ── Helpers ──────────────────────────────────────────────────────────────────────
    private static bool Unmet(int have, int req) => have > 0 && have < req;
    private static int Div() => 5;

    private int Divline(SpriteBatch sb, Texture2D white, int x, int y, int w, int pad)
    {
        sb.Draw(white, new Rectangle(x + pad, y + 2, w - pad * 2, 1), DividerC);
        return y + 5;
    }

    private static void AddStat(List<(string, Color)> lines, string label, int v)
    {
        if (v != 0) lines.Add(($"· {label} : +{v}", StatColor));
    }

    private List<string> Wrap(string text, int maxW)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\\r", "\n").Replace("\\n", "\n").Replace("\r", "").Split('\n'))
        {
            var cur = "";
            foreach (var word in paragraph.Split(' '))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (_font.Measure(trial).X > maxW && cur.Length > 0) { lines.Add(cur); cur = word; }
                else cur = trial;
            }
            lines.Add(cur);
        }
        return lines;
    }

    private static Color GradeColor(int g) => g switch
    {
        1 => new Color(0x77, 0xCC, 0xFF), 2 => new Color(0xCC, 0x88, 0xFF),
        3 => new Color(0xFF, 0xCC, 0x33), 4 => new Color(0x55, 0xEE, 0x77),
        _ => Color.White,
    };

    private static string SpeedLabel(int s) => s switch
    {
        <= 2 => $"Faster ({s})", <= 4 => $"Fast ({s})", 5 => $"Normal ({s})", 6 => $"Slow ({s})", _ => $"Slower ({s})",
    };

    private static string Category(int itemId) => (itemId / 10000) switch
    {
        100 => "Hat", 101 => "Face Accessory", 102 => "Eye Accessory", 103 => "Earring",
        104 => "Top", 105 => "Overall", 106 => "Bottom", 107 => "Shoes", 108 => "Glove",
        109 => "Shield", 110 => "Cape", 111 => "Ring", 112 => "Pendant", 113 => "Belt",
        114 => "Medal", 115 => "Shoulder",
        130 => "One-Handed Sword", 131 => "One-Handed Axe", 132 => "One-Handed Blunt Weapon",
        133 => "Dagger", 137 => "Wand", 138 => "Staff",
        140 => "Two-Handed Sword", 141 => "Two-Handed Axe", 142 => "Two-Handed Blunt Weapon",
        143 => "Spear", 144 => "Pole Arm", 145 => "Bow", 146 => "Crossbow", 147 => "Claw",
        148 => "Knuckle", 149 => "Gun",
        _ => string.Empty,
    };

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    // Border with the 4 corner pixels skipped (a subtle rounded look).
    private static void DrawRoundBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X + 1, r.Y, r.Width - 2, 1), c);
        sb.Draw(white, new Rectangle(r.X + 1, r.Bottom - 1, r.Width - 2, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y + 1, 1, r.Height - 2), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y + 1, 1, r.Height - 2), c);
    }
}
