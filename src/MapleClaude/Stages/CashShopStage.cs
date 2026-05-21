using MapleClaude.App;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Cash Shop stage. 1:1 recreation of v95 GMS UICashShop.
///
/// Enters at 1024×768; restores the previous resolution on exit.
///
/// WZ: UI.wz/CashShop.img/
///   Base/backgrnd               — main background
///   Base/BtExit                 — exit button
///   CSTab/0..8                  — 9 category tabs (left rail)
///   CSStatus/backgrnd           — top status strip
///   CSStatus/BtChargeNX         — charge NX button  (5, 554)
///   CSStatus/BtWish             — wish-list button
///   CSStatus/BtMileage          — mileage button
///   CSStatus/BtCoupon           — coupon button
///   CSStatus/BtHelp             — help button
///   CSPromotionBanner/0         — promotion banner   (138, 40)
///   CSList/backgrnd             — item-list area background
///   CSList/item/base            — per-item slot frame
///   CSList/item/none            — empty slot frame
///   CSList/BtNext               — next page
///   CSList/BtPrev               — prev page
///   CSList/BtBuy                — buy button
///   CSChar/backgrnd             — character preview panel
///   CSChar/BtPreview1..3        — outfit preview tabs  (957/974/991, 46)
///   CSChar/BtSaveAvatar         — save avatar
///   CSChar/BtTakeoffAvatar      — remove outfit
///   CSItemSearch/backgrnd       — search bar background
///
/// Item grid: 7 columns × 2 rows = 15 items (MAX_ITEMS = 7*2+1)
///   base origin : (137, 372) + col*(124, 0) + row*(0, 205)
///   item icon   : origin + (27, 101)
///   item name   : origin + (55, 108)
///   item price  : origin + (58, 127)
///   buy button  : origin + (9, 151)
///
/// Coordinate system: 1024-wide canvas, origin top-left.
/// </summary>
public sealed class CashShopStage : Stage
{
    // ── Sizes ─────────────────────────────────────────────────────────────────
    private const int CsW = 1024;
    private const int CsH = 768;

    // Item grid constants (from reference source)
    private const int ItemCols   = 7;
    private const int ItemRows   = 2;
    private const int MaxItems   = ItemCols * ItemRows + 1; // 15
    private const int ItemStepX  = 124;
    private const int ItemStepY  = 205;
    private static readonly Point ItemGridOrigin = new(137, 372);
    private static readonly Point ItemIconOffset = new(27, 101);
    private static readonly Point ItemNameOffset = new(55, 108);
    private static readonly Point ItemPriceOffset = new(58, 127);
    private static readonly Point ItemBuyOffset  = new(9, 151);

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ILogger<CashShopStage> _logger;
    private readonly WzPackage? _ui;
    private readonly int _prevW, _prevH;

    private WzTextureLoader? _loader;

    // ── WZ sprites ────────────────────────────────────────────────────────────
    private WzSprite? _bgMain;
    private WzSprite? _bgStatus;
    private WzSprite? _bgList;
    private WzSprite? _bgChar;
    private WzSprite? _bgSearch;
    private WzSprite? _banner;
    private WzSprite? _itemSlotBase;
    private WzSprite? _itemSlotNone;
    private WzSprite? _itemSlotLine;

    private readonly WzSprite?[] _tabSprites = new WzSprite?[9];

    // ── Buttons ───────────────────────────────────────────────────────────────
    private Button? _btExit;
    private Button? _btChargeNX;
    private Button? _btWish;
    private Button? _btMileage;
    private Button? _btCoupon;
    private Button? _btHelp;
    private Button? _btNext;
    private Button? _btPrev;
    private Button? _btSaveAvatar;
    private Button? _btTakeoff;
    private readonly Button?[] _tabBtns     = new Button?[9];
    private readonly Button?[] _previewBtns = new Button?[3];
    private readonly Button?[] _buyBtns     = new Button?[MaxItems];
    private readonly List<Button> _allButtons = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private int _activeTab;
    private int _page;
    private int _previewSlot;

    // ── Item data (placeholder — wired from server packets later) ─────────────
    public sealed class CashItem
    {
        public int    Id;
        public string Name     = string.Empty;
        public int    Price    = 1000;
        public int    MesoPrc  = 0;
        public bool   Discount;
        public int    DiscPrc  = 0;
        public string Label    = string.Empty;  // "NEW","SALE","HOT","LIMITED","BONUS",…
        public bool   InCart;
    }

    private readonly List<CashItem>[] _tabItems = new List<CashItem>[9];

    // ── Balances ──────────────────────────────────────────────────────────────
    public int NxCredit    { get; set; } = 0;
    public int NxPrepaid   { get; set; } = 0;
    public int MaplePoints { get; set; } = 0;

    private static readonly string[] TabNames =
    [
        "New/Best", "Character", "Equip", "Hair/Face",
        "Pet", "Others", "Event", "Package", "Popular",
    ];

    // ── Label colours ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Color> LabelColors = new()
    {
        ["NEW"]        = new Color(100, 200, 255),
        ["SALE"]       = new Color(255, 80,  80 ),
        ["HOT"]        = new Color(255, 160,  40),
        ["LIMITED"]    = new Color(180, 100, 255),
        ["BONUS"]      = new Color(80,  220,  80),
        ["WORLD_SALE"] = new Color(255, 220,  40),
    };

    private readonly BuiltInFont? _font;

    public CashShopStage(
        ILogger<CashShopStage> logger,
        WzPackage? ui,
        BuiltInFont? font,
        int prevW = 800, int prevH = 600)
    {
        _logger = logger;
        _ui     = ui;
        _font   = font;
        _prevW  = prevW;
        _prevH  = prevH;

        for (var i = 0; i < 9; i++)
        {
            _tabItems[i] = new List<CashItem>();
        }

        SeedPlaceholderItems();
    }

    // ── Stage lifecycle ───────────────────────────────────────────────────────

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        Game.ResizeWindow(CsW, CsH);
        _loader = new WzTextureLoader(GraphicsDevice);

        LoadAssets();
        LayoutButtons();
        _logger.LogInformation("CashShopStage entered — 1024×768 canvas");
    }

    public override void OnExit()
    {
        Game.ResizeWindow(_prevW, _prevH);
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    // ── Asset loading ─────────────────────────────────────────────────────────

    private void LoadAssets()
    {
        var cs = _ui?.GetItem("CashShop.img") is WzImage img ? img.Root : null;
        if (cs is null) { _logger.LogWarning("CashShop.img not found in UI.wz"); return; }

        var csBase   = cs.Get("Base")            as WzProperty;
        var csStatus = cs.Get("CSStatus")        as WzProperty;
        var csBanner = cs.Get("CSPromotionBanner") as WzProperty;
        var csList   = cs.Get("CSList")          as WzProperty;
        var csChar   = cs.Get("CSChar")          as WzProperty;
        var csSearch = cs.Get("CSItemSearch")    as WzProperty;
        var csTab    = cs.Get("CSTab")           as WzProperty;

        _bgMain    = LoadC(csBase,   "backgrnd");
        _bgStatus  = LoadC(csStatus, "backgrnd");
        _bgList    = LoadC(csList,   "backgrnd");
        _bgChar    = LoadC(csChar,   "backgrnd");
        _bgSearch  = LoadC(csSearch, "backgrnd");
        _banner    = LoadC(csBanner, "0");

        var csListItem = csList?.Get("item") as WzProperty;
        _itemSlotBase = LoadC(csListItem, "base");
        _itemSlotNone = LoadC(csListItem, "none");
        _itemSlotLine = LoadC(csListItem, "line");

        // Tabs
        for (var i = 0; i < 9; i++)
        {
            var tabNode = (csTab?.Get($"{i}") as WzProperty)?.Get("normal") as WzProperty;
            _tabSprites[i] = LoadC(tabNode, "0");

            var idx = i;
            _tabBtns[i] = MakeBtn(csTab, $"{i}", () => { _activeTab = idx; _page = 0; });
        }

        // Preview buttons  (957/974/991, 46)
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            _previewBtns[i] = MakeBtn(csChar, $"BtPreview{i + 1}", () => _previewSlot = idx);
        }

        // Buy buttons (one per item slot)
        for (var i = 0; i < MaxItems; i++)
        {
            var idx = i;
            _buyBtns[i] = MakeBtn(csList, "BtBuy", () => OnBuyClicked(idx));
        }

        _btExit        = MakeBtn(csBase,   "BtExit",          () => Exit());
        _btChargeNX    = MakeBtn(csStatus, "BtChargeNX",      () => { });
        _btWish        = MakeBtn(csStatus, "BtWish",          () => { });
        _btMileage     = MakeBtn(csStatus, "BtMileage",       () => { });
        _btCoupon      = MakeBtn(csStatus, "BtCoupon",        () => { });
        _btHelp        = MakeBtn(csStatus, "BtHelp",          () => { });
        _btNext        = MakeBtn(csList,   "BtNext",          () => _page++);
        _btPrev        = MakeBtn(csList,   "BtPrev",          () => { if (_page > 0) _page--; });
        _btSaveAvatar  = MakeBtn(csChar,   "BtSaveAvatar",    () => { });
        _btTakeoff     = MakeBtn(csChar,   "BtTakeoffAvatar", () => { });
    }

    private void LayoutButtons()
    {
        // Tab rail: stacked vertically on the left (reference has them in CSTab)
        for (var i = 0; i < 9; i++)
        {
            if (_tabBtns[i] is null) continue;
            _tabBtns[i]!.Position = new Vector2(16, 160 + i * 56);
        }

        // Preview buttons (957, 974, 991 x; y=46)
        for (var i = 0; i < 3; i++)
        {
            if (_previewBtns[i] is null) continue;
            _previewBtns[i]!.Position = new Vector2(957 + i * 17, 46);
        }

        // Status-strip buttons (top-right cluster)
        if (_btChargeNX != null) _btChargeNX.Position = new Vector2(5,   554);
        if (_btWish     != null) _btWish.Position     = new Vector2(930,  4);
        if (_btMileage  != null) _btMileage.Position  = new Vector2(956,  4);
        if (_btCoupon   != null) _btCoupon.Position   = new Vector2(982,  4);
        if (_btHelp     != null) _btHelp.Position     = new Vector2(1008, 4);

        // Exit button (top-right corner)
        if (_btExit != null) _btExit.Position = new Vector2(CsW - 38, 6);

        // Next/Prev pagination
        if (_btNext != null) _btNext.Position = new Vector2(820, 580);
        if (_btPrev != null) _btPrev.Position = new Vector2(140, 580);

        // Avatar panel buttons
        if (_btSaveAvatar != null) _btSaveAvatar.Position = new Vector2(840, 700);
        if (_btTakeoff    != null) _btTakeoff.Position    = new Vector2(920, 700);

        // Buy buttons: one per item slot in the grid
        for (var r = 0; r < ItemRows; r++)
        for (var c = 0; c < ItemCols; c++)
        {
            var idx = r * ItemCols + c;
            if (idx >= MaxItems || _buyBtns[idx] is null) continue;
            _buyBtns[idx]!.Position = new Vector2(
                ItemGridOrigin.X + c * ItemStepX + ItemBuyOffset.X,
                ItemGridOrigin.Y + r * ItemStepY + ItemBuyOffset.Y);
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        // Disable Prev on first page
        if (_btPrev != null) _btPrev.Enabled = _page > 0;
        var tab = _tabItems[_activeTab];
        var pages = (tab.Count + MaxItems - 1) / MaxItems;
        if (_btNext != null) _btNext.Enabled = _page < pages - 1;

        // Enable buy buttons only for non-empty slots
        var offset = _page * MaxItems;
        for (var i = 0; i < MaxItems; i++)
        {
            if (_buyBtns[i] != null)
                _buyBtns[i]!.Enabled = (offset + i) < tab.Count;
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(GameTime gameTime, SpriteBatch sb)
    {
        var white = Game.WhitePixel;

        // ── Background ───────────────────────────────────────────────────────
        if (_bgMain != null)
            _bgMain.Draw(sb, new Vector2(CsW / 2f, CsH / 2f));
        else
            DrawFallbackBg(sb, white);

        // ── Promotion banner  (138, 40) ──────────────────────────────────────
        _banner?.Draw(sb, new Vector2(138, 40));

        // ── Status strip (NX balance) ────────────────────────────────────────
        DrawStatusStrip(sb, white);

        // ── Left tab rail ────────────────────────────────────────────────────
        DrawTabRail(sb, white);

        // ── Item list area ───────────────────────────────────────────────────
        DrawItemList(sb, white);

        // ── Character preview panel ──────────────────────────────────────────
        DrawCharPreview(sb, white);

        // ── All buttons ──────────────────────────────────────────────────────
        foreach (var b in _allButtons) b.Draw(sb);
    }

    private void DrawFallbackBg(SpriteBatch sb, Texture2D white)
    {
        sb.Draw(white, new Rectangle(0, 0, CsW, CsH), new Color(14, 18, 30));
        // Header strip
        sb.Draw(white, new Rectangle(0, 0, CsW, 40), new Color(20, 22, 40));
        sb.Draw(white, new Rectangle(0, 39, CsW, 1), new Color(60, 60, 100));
        // Footer strip
        sb.Draw(white, new Rectangle(0, CsH - 50, CsW, 50), new Color(20, 22, 40));
        sb.Draw(white, new Rectangle(0, CsH - 50, CsW, 1), new Color(60, 60, 100));
    }

    private void DrawStatusStrip(SpriteBatch sb, Texture2D white)
    {
        if (_bgStatus != null)
            _bgStatus.Draw(sb, new Vector2(CsW / 2f, 20));
        else
            sb.Draw(white, new Rectangle(0, 0, CsW, 38), new Color(16, 18, 35, 220));

        // Title
        _font?.Draw(sb, "MapleStory Cash Shop", new Vector2(130, 10), new Color(255, 215, 0));

        // NX/MaplePoints balance
        _font?.Draw(sb, $"NX Credit: {NxCredit:N0}",       new Vector2(600, 8),  new Color(120, 200, 255));
        _font?.Draw(sb, $"NX Prepaid: {NxPrepaid:N0}",     new Vector2(600, 20), new Color(160, 230, 255));
        _font?.Draw(sb, $"MaplePoints: {MaplePoints:N0}",  new Vector2(780, 8),  new Color(255, 200, 80));
    }

    private void DrawTabRail(SpriteBatch sb, Texture2D white)
    {
        // Left panel background
        sb.Draw(white, new Rectangle(0, 40, 130, CsH - 90), new Color(16, 20, 36));
        DrawBorder(sb, white, new Rectangle(0, 40, 130, CsH - 90), new Color(50, 55, 90));

        for (var i = 0; i < 9; i++)
        {
            var ty = 160 + i * 56;
            var tabRect = new Rectangle(4, ty, 122, 50);
            var isActive = i == _activeTab;

            if (_tabSprites[i] != null)
                _tabSprites[i]!.Draw(sb, new Vector2(tabRect.X + 61, tabRect.Y + 25));
            else
            {
                sb.Draw(white, tabRect, isActive ? new Color(40, 50, 80) : new Color(22, 26, 44));
                DrawBorder(sb, white, tabRect, isActive ? new Color(100, 130, 200) : new Color(45, 50, 80));
                _font?.Draw(sb, TabNames[i], new Vector2(tabRect.X + 6, tabRect.Y + 18),
                    isActive ? Color.White : new Color(160, 165, 190));
            }

            _tabBtns[i]?.Draw(sb);
        }
    }

    private void DrawItemList(SpriteBatch sb, Texture2D white)
    {
        // List panel background
        if (_bgList != null)
            _bgList.Draw(sb, new Vector2(580, 490));
        else
        {
            sb.Draw(white, new Rectangle(130, 40, 820, CsH - 90), new Color(18, 22, 38));
            DrawBorder(sb, white, new Rectangle(130, 40, 820, CsH - 90), new Color(50, 55, 90));
        }

        // Search bar
        _bgSearch?.Draw(sb, new Vector2(580, 55));

        var tab    = _tabItems[_activeTab];
        var offset = _page * MaxItems;

        for (var r = 0; r < ItemRows; r++)
        for (var c = 0; c < ItemCols; c++)
        {
            var idx = r * ItemCols + c;
            var absIdx = offset + idx;
            var ox = ItemGridOrigin.X + c * ItemStepX;
            var oy = ItemGridOrigin.Y + r * ItemStepY;

            if (absIdx >= tab.Count)
            {
                // Empty slot
                if (_itemSlotNone != null)
                    _itemSlotNone.Draw(sb, new Vector2(ox + 62, oy + 102));
                else
                {
                    sb.Draw(white, new Rectangle(ox, oy, 120, 200), new Color(14, 17, 30, 180));
                    DrawBorder(sb, white, new Rectangle(ox, oy, 120, 200), new Color(35, 40, 65));
                }
                continue;
            }

            var item = tab[absIdx];
            DrawItemSlot(sb, white, item, ox, oy, idx);
        }

        // Pagination label
        var pages = Math.Max(1, (tab.Count + MaxItems - 1) / MaxItems);
        if (_font != null)
        {
            var pg = $"Page {_page + 1} / {pages}";
            var sz = _font.Measure(pg);
            _font.Draw(sb, pg, new Vector2((130 + 950 - (int)sz.X) / 2f, CsH - 38), new Color(160, 165, 190));
        }
    }

    private void DrawItemSlot(SpriteBatch sb, Texture2D white,
        CashItem item, int ox, int oy, int idx)
    {
        // Slot frame
        if (_itemSlotBase != null)
            _itemSlotBase.Draw(sb, new Vector2(ox + 62, oy + 102));
        else
        {
            sb.Draw(white, new Rectangle(ox, oy, 120, 200), new Color(20, 24, 42));
            DrawBorder(sb, white, new Rectangle(ox, oy, 120, 200), new Color(55, 60, 95));
        }

        // Icon placeholder  (ox+27, oy+101) = top-left of icon; 90×90
        var iconRect = new Rectangle(ox + ItemIconOffset.X, oy + ItemIconOffset.Y, 90, 90);
        sb.Draw(white, iconRect, new Color(28, 32, 55));
        DrawBorder(sb, white, iconRect, new Color(65, 70, 110));
        // Icon letter (first char of name as placeholder)
        if (_font != null && item.Name.Length > 0)
        {
            _font.Draw(sb, item.Name[0].ToString(),
                new Vector2(iconRect.X + 38, iconRect.Y + 38), new Color(120, 130, 160));
        }

        // Label badge (NEW / SALE / HOT / …)
        if (!string.IsNullOrEmpty(item.Label))
        {
            var lc = LabelColors.TryGetValue(item.Label, out var col) ? col : Color.White;
            _font?.Draw(sb, item.Label, new Vector2(ox + ItemIconOffset.X, oy + ItemIconOffset.Y - 12), lc);
        }

        // Item name  (ox+55, oy+108)
        var namePos = new Vector2(ox + ItemNameOffset.X, oy + ItemNameOffset.Y);
        _font?.Draw(sb, TruncateName(item.Name, 11), namePos, Color.White);

        // Price  (ox+58, oy+127) — NX price, show discount if applicable
        var priceY = oy + ItemPriceOffset.Y;
        if (item.Discount && item.DiscPrc > 0)
        {
            // Strikethrough original price in grey, discount in red
            _font?.Draw(sb, $"{item.Price:N0} NX", new Vector2(ox + ItemPriceOffset.X, priceY),
                new Color(100, 100, 100));
            _font?.Draw(sb, $"{item.DiscPrc:N0} NX", new Vector2(ox + ItemPriceOffset.X, priceY + 12),
                new Color(255, 80, 80));
        }
        else
        {
            var priceStr = item.MesoPrc > 0 ? $"{item.MesoPrc:N0} Meso" : $"{item.Price:N0} NX";
            _font?.Draw(sb, priceStr, new Vector2(ox + ItemPriceOffset.X, priceY), new Color(220, 200, 100));
        }

        // Buy button (positioned in LayoutButtons)
        _buyBtns[idx]?.Draw(sb);
    }

    private void DrawCharPreview(SpriteBatch sb, Texture2D white)
    {
        // Character preview panel: reference has it on the right side
        const int pvX = 860;
        const int pvY = 46;
        const int pvW = 160;
        const int pvH = 650;

        if (_bgChar != null)
            _bgChar.Draw(sb, new Vector2(pvX + pvW / 2f, pvY + pvH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(pvX, pvY, pvW, pvH), new Color(16, 20, 40));
            DrawBorder(sb, white, new Rectangle(pvX, pvY, pvW, pvH), new Color(55, 60, 100));
        }

        _font?.Draw(sb, "Preview", new Vector2(pvX + 48, pvY + 6), new Color(180, 185, 220));

        // Preview slot indicators (957/974/991, 46)
        for (var i = 0; i < 3; i++)
        {
            var bx = 957 + i * 17;
            var isActive = i == _previewSlot;
            var dotRect = new Rectangle(bx - 5, 48, 14, 10);
            sb.Draw(white, dotRect, isActive ? new Color(80, 120, 200) : new Color(40, 45, 75));
            _previewBtns[i]?.Draw(sb);
        }

        // Placeholder avatar area
        var avatarRect = new Rectangle(pvX + 20, pvY + 60, pvW - 40, pvH - 120);
        sb.Draw(white, avatarRect, new Color(22, 28, 50));
        _font?.Draw(sb, "[Avatar]", new Vector2(pvX + 42, pvY + pvH / 2 - 8), new Color(80, 90, 120));

        _btSaveAvatar?.Draw(sb);
        _btTakeoff?.Draw(sb);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return;
    }

    public override void OnKeyPress(Keys key)
    {
        if (key == Keys.Escape) Exit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Exit()
    {
        _logger.LogInformation("CashShopStage: exiting → pop to previous stage");
        Game.StageDirector.Pop();
    }

    private void OnBuyClicked(int slotIdx)
    {
        var tab    = _tabItems[_activeTab];
        var absIdx = _page * MaxItems + slotIdx;
        if (absIdx >= tab.Count) return;
        var item = tab[absIdx];
        _logger.LogInformation(
            "CashShop buy: slot={Slot} id={Id} name='{Name}' price={Price}NX — no packet yet",
            slotIdx, item.Id, item.Name, item.Price);
    }

    private Button? MakeBtn(WzProperty? root, string name, Action onClick)
    {
        try
        {
            var pr = root?.Get(name) as WzProperty;
            if (pr is null) return null;
            var b = new Button(_loader!, pr) { OnClick = onClick };
            _allButtons.Add(b);
            return b;
        }
        catch { return null; }
    }

    private WzSprite? LoadC(WzProperty? root, string key)
    {
        try
        {
            return root?.Get(key) is WzCanvas c ? _loader!.Load(c) : null;
        }
        catch { return null; }
    }

    private static string TruncateName(string name, int max) =>
        name.Length <= max ? name : name[..max] + "…";

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    // ── Placeholder data ──────────────────────────────────────────────────────

    private void SeedPlaceholderItems()
    {
        // Tab 0: New/Best
        var t0 = _tabItems[0];
        t0.Add(new CashItem { Id = 5000054, Name = "White Bunny",     Price = 3300,  Label = "NEW" });
        t0.Add(new CashItem { Id = 5000055, Name = "Black Cat",       Price = 3300,  Label = "HOT" });
        t0.Add(new CashItem { Id = 5010046, Name = "Maple Mount",     Price = 4300,  Label = "NEW" });
        t0.Add(new CashItem { Id = 5200000, Name = "Safety Charm",    Price = 1200 });
        t0.Add(new CashItem { Id = 5200001, Name = "Pet Food",        Price = 500 });
        t0.Add(new CashItem { Id = 5062000, Name = "Slot Coupon",     Price = 3300,  Label = "SALE", Discount = true, DiscPrc = 2500 });
        t0.Add(new CashItem { Id = 5040000, Name = "Name Change",     Price = 9900 });
        t0.Add(new CashItem { Id = 5040001, Name = "World Transfer",  Price = 9900,  Label = "LIMITED" });

        // Tab 1: Character (outfits)
        var t1 = _tabItems[1];
        t1.Add(new CashItem { Id = 1002357, Name = "Pink Bunny Hat",   Price = 1200 });
        t1.Add(new CashItem { Id = 1040090, Name = "Maple Suit",       Price = 3500 });
        t1.Add(new CashItem { Id = 1041090, Name = "Maple Dress",      Price = 3500 });
        t1.Add(new CashItem { Id = 1062080, Name = "Maple Pants",      Price = 2200,  Label = "SALE" });
        t1.Add(new CashItem { Id = 1072095, Name = "Maple Shoes",      Price = 1800 });
        t1.Add(new CashItem { Id = 1102085, Name = "Angel Wings",      Price = 3300,  Label = "HOT" });
        t1.Add(new CashItem { Id = 1012133, Name = "Maple Specs",      Price = 1100 });

        // Tab 2: Equip
        var t2 = _tabItems[2];
        t2.Add(new CashItem { Id = 1302086, Name = "Maple Sword",      Price = 5500,  Label = "NEW" });
        t2.Add(new CashItem { Id = 1402040, Name = "Maple Staff",      Price = 5500 });
        t2.Add(new CashItem { Id = 1482020, Name = "Maple Claw",       Price = 5500 });
        t2.Add(new CashItem { Id = 1452020, Name = "Maple Bow",        Price = 5500,  Label = "SALE", Discount = true, DiscPrc = 4400 });

        // Tab 4: Pets
        var t4 = _tabItems[4];
        t4.Add(new CashItem { Id = 5000006, Name = "Husky",            Price = 3300,  Label = "HOT" });
        t4.Add(new CashItem { Id = 5000016, Name = "Pink Bean",        Price = 3300,  Label = "NEW" });
        t4.Add(new CashItem { Id = 5000028, Name = "Panda",            Price = 3300 });
        t4.Add(new CashItem { Id = 5001005, Name = "White Tiger",      Price = 4300,  Label = "LIMITED" });

        // Tab 5: Others
        var t5 = _tabItems[5];
        t5.Add(new CashItem { Id = 5510000, Name = "Teleport Rock",    Price = 1100 });
        t5.Add(new CashItem { Id = 5040010, Name = "Gender Change",    Price = 9900 });
        t5.Add(new CashItem { Id = 5680000, Name = "Maple Points x1K", Price = 0, MesoPrc = 100000, Label = "BONUS" });
        t5.Add(new CashItem { Id = 5030000, Name = "Character Slot",   Price = 3300 });
        t5.Add(new CashItem { Id = 5180000, Name = "Storage Slot+4",   Price = 3300,  Label = "SALE" });

        // Tab 8: Popular
        var t8 = _tabItems[8];
        foreach (var item in t0) t8.Add(item);
        foreach (var item in t1.Take(4)) t8.Add(item);
    }
}
