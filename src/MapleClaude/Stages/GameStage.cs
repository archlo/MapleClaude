using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Debug;
using MapleClaude.Domain;
using MapleClaude.Net;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Localization;
using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.Settings;
using MapleClaude.UI;
using MapleClaude.UI.Game;
using MapleClaude.Wz;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// In-game stage. Renders a game map, a player character, and NPC sprites.
/// Camera follows the player with smooth lerp and map bounds clamping.
/// Key bindings:
///   Move: A/D or Left/Right arrows
///   Jump: Space or W/Up arrow
///   Panels: E=Equip  I=Items  K=Skills  S=Stats  Q=Quest  M=MiniMap
/// </summary>
public sealed class GameStage : Stage
{
    private readonly ILogger<GameStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly WzPackage? _charWz;
    private readonly WzPackage? _npcWz;
    private readonly WzPackage? _mobWz;

    private WzTextureLoader? _loader;

    // World
    private GameCamera _camera = null!;
    private CharLook? _player;
    private AvatarLook? _myLook;   // the player's current look; mutated on equip/unequip to re-render the avatar
    private CharacterRenderer? _charRenderer;
    private readonly List<NpcLook> _npcs = new();

    // Mobs
    private readonly Dictionary<int, MobLook> _mobs = new();
    // Mob IDs we control (MobChangeController ctrl=1)
    private readonly HashSet<int> _controlledMobs = new();
    // Mob AI controllers - one per controlled mob, driven by Mob.wz info nodes via MobInfoService.
    // Created on MobChangeController(isCtrl=true); deferred via _pendingCtrl when _field isn't loaded
    // yet (the new map's MobChangeController packets arrive during the post-SetField fade-out
    // window, before ApplyFieldChange swaps _field at full black). The mob-tick in Update drains
    // _pendingCtrl every frame once _field is non-null. Without these the mobs we're nearest to
    // (= ours to control) appear frozen to ourselves AND to every other client.
    private readonly Dictionary<int, MobController> _mobCtl = new();
    private readonly HashSet<int> _pendingCtrl = new();
    private readonly Random _mobRng = new();
    // Per-player i-frame after taking damage from any mob (matches the GMS ~0.7s window).
    // While > 0 the touch-damage trigger below skips all collisions so mobs can't combo us.
    private float _userHitCooldown;
    private MobInfoService? _mobInfoSvc;

    // Other players
    private readonly Dictionary<int, OtherCharLook> _otherChars = new();

    // Drops
    private readonly Dictionary<int, DropSprite> _drops = new();
    private ItemIconLoader? _iconLoader;   // shared item-icon loader (inventories, tooltips, ground drops)

    // Damage numbers
    private DamageNumber? _dmgNumbers;

    // Over-head emotion bubbles (Effect.wz/EmotionEffect.img). Spawned when the local
    // player or a nearby player triggers a face expression; one-shot, world-space.
    private EmotionBubble? _emotionBubble;

    // Always-visible panels
    private StatusBar? _statusBar;
    private ChatBar? _chatBar;
    private ChatBalloonLayer? _balloons;
    private ChatBalloonLayer? _npcBalloons;   // ambient NPC chatter (info/speak), keyed by NPC objId
    private MiniMap? _miniMap;
    private BuffList? _buffList;

    // Toggle panels
    private EquipInventory? _equip;
    private ItemInventory? _item;
    private SkillBook? _skill;
    private StatsInfo? _stats;
    private StatDetailInfo? _statDetail;
    private QuestLog? _quest;
    private QuestDetail? _questDetail;
    // Client-side quest state for marker/availability computation: questId → (state, progress value).
    // State mirrors the server: 1 = PERFORM (in progress, value = mob-kill string), 2 = COMPLETE.
    private readonly Dictionary<int, (byte State, string Value)> _questStates = new();
    // NPC template id → which quest marker floats above its head (recomputed by RefreshQuestAvailability).
    private enum QuestMarkerKind { None, New, InProgress }
    private readonly Dictionary<int, QuestMarkerKind> _npcMarkers = new();
    // Animated quest-marker icons (UI.wz/UIWindow2.img/QuestIcon): group 0 = "!" lightbulb (new quest),
    // group 5 = "?" (in-progress turn-in). Shared frame clock advanced in Update.
    private WzSprite[] _questMarkNew = [];
    private WzSprite[] _questMarkProgress = [];
    private int _markerFrame;
    private float _markerTimer;
    private KeyConfig? _keyConfig;
    private QuickSlotConfig? _quickSlotConfig;
    private QuickSlotBar? _quickSlots;
    private OptionMenu? _optionMenu;
    private CharInfo? _charInfo;
    private int _lastClickCharId = -1;     // last field-avatar clicked (for double-click → profile)
    private double _lastClickTime;          // seconds (Environment.TickCount64 / 1000.0)

    // New high-priority panels
    private WorldMap?       _worldMap;
    private UserList?       _userList;
    private FamilyWindow?   _familyWindow;
    private ChannelSelect?  _channelSelect;
    private StatusMessenger? _messenger;

    // Modal panels
    private NpcTalk? _npcTalk;
    private ScriptSubst? _npcSubst;
    private BuiltInFont? _dlgBody, _dlgBold, _dlgName;
    private BuiltInFont? _tipFont;     // small (9px) font for item tooltips — Game.Font (~14.7px) is too big
    private BuiltInFont? _tipTabFont;  // tiny (8px) font rendered natively for the job-requirement tabs
    private Shop? _shop;
    private Trunk? _trunk;
    private Messenger? _messengerWin;
    private Notice? _notice;
    private QuitConfirmOverlay? _quitConfirm;
    private GameMenu? _gameMenu;   // authentic CUIGameMenu (ESC / status-bar System button)

    private readonly List<GamePanel> _panels = new();
    private GamePacketHandler? _netHandler;

    // Accumulated stat snapshot from server packets
    private CharStats _charStats = new();

    // Accumulated skill records (id → record). Single-skill ChangeSkillRecord
    // deltas merge into this set rather than replacing it; the Skill window is
    // rebuilt from it + the job's full Skill.wz tree. _lastSkillJob gates the
    // (WZ-walking) rebuild to actual job changes, not every StatChanged.
    private readonly Dictionary<int, SkillRecord> _skillRecords = new();
    private int _lastSkillJob = int.MinValue;
    private readonly Dictionary<int, WzSprite?> _skillIconCache = new();
    private readonly List<SkillEffect> _skillEffects = new();

    // Input state
    private bool _moveLeft;
    private bool _moveRight;
    private bool _jumpPressed;
    private bool _downPressed;
    private bool _upPressed;

    // Network state populated by SetField.
    private FieldScene? _field;
    private PlayerController? _physics;
    private byte _fieldKey;
    private string _currentBgm = string.Empty;
    private string _mapStreet = string.Empty;
    private string _mapNameText = string.Empty;
    private bool _guildLoadSent;

    // One-shot debug-log latch for the not-yet-implemented MobMove(227)
    // outgoing path; see comment block in Update.

    // ── In-game resolution ───────────────────────────────────────────────────
    // The login flow runs at 800×600; entering a map switches to the larger
    // in-game canvas and restores the previous size on exit (the same pattern
    // CashShopStage uses). _prevW/_prevH capture the login size at enter time.
    private const int InGameWidth  = 1024;
    private const int InGameHeight = 768;
    private int _prevW = 800;
    private int _prevH = 600;

    // Active NPC-dialog message type — answers must echo the type the server sent.
    private ScriptMessageType _dialogMsgType;

    // Social state. Party member ids are cached from the last PartyResult so /p
    // (party chat) can address them; a pending invite lets /accept join.
    private readonly List<int> _partyMemberIds = new();
    private readonly List<int> _buddyIds = new();        // online friends' char ids (for /b)
    private readonly List<int> _guildMemberIds = new();  // online guild members' char ids (for /g)
    private int _pendingInviterId;
    private bool _hasPendingInvite;
    private string _myName = "Hero";

    // Messenger invite state: the dwSN of a pending messenger invite, joined via /maccept.
    private int _pendingMessengerId;
    private bool _hasMessengerInvite;

    // Melee attack pacing.
    private float _attackCooldown;
    private const float AttackCooldownSeconds = 0.6f;

    // Emotion send pacing — matches the v95 client's CWvsContext::SendEmotionChange
    // 2000 ms guard between successive UserEmotion packets.
    private float _emotionCooldown;
    private const float EmotionCooldownSeconds = 2.0f;
    private const int MeleeReachX = 120;   // px in front of the player
    private const int MeleeReachY = 40;    // px above/below the player's foot point
    private const int MaxMeleeTargets = 15; // v95 caps a melee swing at 15 mobs (IDB: CMobPool::FindHitMobInRect apMob[15])
    private static readonly Random _attackRng = new();

    public GameStage(
        ILogger<GameStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        WzPackage? charWz,
        WzPackage? npcWz,
        WzPackage? mobWz = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _charWz = charWz;
        _npcWz  = npcWz;
        _mobWz  = mobWz;
        _mobInfoSvc = new MobInfoService(mobWz);
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        game.ConsumeImeJump();   // discard any stale 한/영 jump request from a prior stage
        _loader = new WzTextureLoader(GraphicsDevice);

        // Entering the game enlarges the window from the 800×600 login canvas to
        // the in-game resolution; remember the login size to restore on exit.
        var pp0 = GraphicsDevice.PresentationParameters;
        _prevW = pp0.BackBufferWidth;
        _prevH = pp0.BackBufferHeight;
        // Apply the saved in-game resolution (defaults to 1024×768); the login flow runs at 800×600.
        var savedRes = Game.Settings.Load();
        Game.ResizeWindow(savedRes.ResW > 0 ? savedRes.ResW : InGameWidth,
                          savedRes.ResH > 0 ? savedRes.ResH : InGameHeight);

        var pp = GraphicsDevice.PresentationParameters;
        var font = Game.Font;
        var basicFont = Game.BasicFont;

        // Authentic NPC-dialog fonts: A12M body (12px) + bold (#e), A11M name (11px).
        // BuiltInFont rasterises a system TrueType face, which is what the client's IWzFont does.
        _dlgBody = new BuiltInFont(GraphicsDevice, "Arial", 12f, System.Drawing.GraphicsUnit.Pixel,
            System.Drawing.Text.TextRenderingHint.AntiAlias, "Malgun Gothic");
        _dlgBold = new BuiltInFont(GraphicsDevice, "Arial", 12f, System.Drawing.GraphicsUnit.Pixel,
            System.Drawing.Text.TextRenderingHint.AntiAlias, "Malgun Gothic", System.Drawing.FontStyle.Bold);
        _dlgName = new BuiltInFont(GraphicsDevice, "Arial", 11f, System.Drawing.GraphicsUnit.Pixel,
            System.Drawing.Text.TextRenderingHint.AntiAlias, "Malgun Gothic");
        // Item tooltips render at a small 9px — the default UI font is Tahoma 11 *point* (~14.7px),
        // which makes the whole card oversized/too wide.
        _tipFont = new BuiltInFont(GraphicsDevice, "Tahoma", 9f, System.Drawing.GraphicsUnit.Pixel,
            System.Drawing.Text.TextRenderingHint.AntiAlias, "Malgun Gothic");
        // The job-requirement tabs are drawn with this rendered natively at 8px (crisp) rather than by
        // scaling the 9px font down, which blurred glyphs and stretched the letter spacing.
        _tipTabFont = new BuiltInFont(GraphicsDevice, "Tahoma", 8f, System.Drawing.GraphicsUnit.Pixel,
            System.Drawing.Text.TextRenderingHint.AntiAlias, "Malgun Gothic");
        _npcSubst = new ScriptSubst
        {
            ItemName  = Game.Names.ItemName,
            NpcName   = Game.Names.NpcName,
            MobName   = Game.Names.MobName,
            MapName   = Game.Names.MapName,
            SkillName = Game.Names.SkillName,
        };

        // Camera — starts at screen centre, follows player
        _camera = new GameCamera(Vector2.Zero)
        {
            ViewWidth = pp.BackBufferWidth,
            ViewHeight = pp.BackBufferHeight,
            MapBounds = new Rectangle(-3000, -2000, 6000, 4000),
            FollowSpeed = 5f,
        };

        // Full-avatar renderer (body + equips + hair + face), shared by the player
        // and other players. The real look/skin arrive via SetField / UserEnterField.
        _charRenderer = new CharacterRenderer(
            _loggerFactory.CreateLogger<CharacterRenderer>(), _charWz, Game.ItemWz, Game.BaseWz, _loader);

        // Player character — spawn at map origin
        _player = new CharLook(_loader, skinId: 0) { Position = Vector2.Zero };
        _player.Load(_charWz);

        // Sample NPCs — placed at fixed world positions (replaced by server data later)
        SpawnNpc(9000001, new Vector2(-200, 0), "Henesys Merchant");
        SpawnNpc(1012000, new Vector2(200, 0), "Maple Administrator");
        SpawnNpc(1052002, new Vector2(400, 0), "Henesys Potion Seller");

        // UI panels
        _statusBar = new StatusBar(_loader, _ui, font, basicFont) { IsVisible = true };
        _chatBar = new ChatBar(_loader, _ui, font) { IsVisible = true };
        _balloons = new ChatBalloonLayer(_loader, _ui, font);
        _npcBalloons = new ChatBalloonLayer(_loader, _ui, font);
        _questMarkNew      = LoadQuestIcon(0);   // "!" lightbulb (new quest available)
        _questMarkProgress = LoadQuestIcon(5);   // "?" (in-progress turn-in)
        _chatBar.Bar = _statusBar;   // chat aligns to the status bar's authentic chat-input rect
        _miniMap = new MiniMap(_loader, _ui, font, _logger) { IsVisible = true };
        _buffList = new BuffList(_loader, _ui, font) { IsVisible = true };
        var iconLoader = _iconLoader = new ItemIconLoader(_loader, _charWz, Game.ItemWz);
        _equip = new EquipInventory(_loader, _ui, font, iconLoader, Game.Names.ItemDesc, _tipFont, _tipTabFont);
        _item = new ItemInventory(_loader, _ui, font, iconLoader, Game.Names.ItemDesc, _tipFont, _tipTabFont);
        _item.OnItemActivate = OnInventoryItemActivate;
        // Drag an item to another slot in the same tab → rearrange (server-authoritative).
        _item.OnMoveItem = (tab, from, to) =>
        {
            if (Game.Session.IsConnected)
                Game.Session.Send(GameSender.ChangeSlotPosition(TabToInvType(tab), (short)from, (short)to, 1));
        };
        // Drag an item outside the window → drop it on the ground (newPos = 0; server spawns the drop).
        _item.OnDropToGround = (tab, slot) =>
        {
            if (!Game.Session.IsConnected) return;
            var qty = (short)Math.Max(1, _item?.ItemAt(tab, slot)?.Quantity ?? 1);
            Game.Session.Send(GameSender.ChangeSlotPosition(TabToInvType(tab), (short)slot, 0, qty));
        };
        // Drag a worn item outside both windows → drop it on the ground (from its negative body part).
        _equip.OnDropToGround = bodyPart =>
        {
            if (Game.Session.IsConnected)
                Game.Session.Send(GameSender.ChangeSlotPosition(InventoryType.Equip, (short)-bodyPart, 0, 1));
        };
        // Double-click a worn slot in the Equip window → unequip to the first free Equip-tab slot.
        _equip.OnUnequip = bodyPart =>
        {
            if (!Game.Session.IsConnected) return;
            var free = _item?.FirstFreeSlot(0) ?? -1;
            if (free <= 0) return;
            Game.Session.Send(GameSender.ChangeSlotPosition(
                InventoryType.Equip, (short)-bodyPart, (short)free, 1));
        };
        // Live-tunable slot-grid origin (drag in MAPLECLAUDE_DEBUG mode) — the one
        // inventory coordinate the WZ tree doesn't carry (the client hardcodes it).
        Game.DebugRegistry.Register(new DebugItem("Inventory", "Slot base",
            () => _item!.SlotBase, v => _item!.SlotBase = v)
        {
            GetScreenPos  = () => _item!.Position + _item.SlotBase,
            SetFromScreen = s => _item!.SlotBase = s - _item!.Position,
        });
        _skill = new SkillBook(_loader, _ui, font);
        _skill.OnSkillUp = id => SendIfConnected(GameSender.SkillUp(id));
        _skill.OnSkillCast = CastSkill;
        _skill.BookIconResolver = Game.Skills.GetBookIcon;
        _skill.BookNameResolver = JobName;
        // Stat window uses the small crisp 12px font: Game.Font is Tahoma 11*point* (~14.7px), too
        // large for the dense WZ stat rows (18px pitch) — it made the values oversized and sit low.
        // basicFont (Tahoma 12px) matches the authentic CUIStat value font height.
        _stats = new StatsInfo(_loader, _ui, basicFont);
        _statDetail = new StatDetailInfo(_loader, _ui, basicFont);
        _stats.OnDetailToggled = open =>
        {
            if (_statDetail == null) return;
            _statDetail.Position = _stats!.Position + new Vector2(172, 90);
            _statDetail.IsVisible = open;
        };
        _quest = new QuestLog(_loader, _ui, font);
        _quest.OnResign = id =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.QuestResign((short)id));
        };
        _questDetail = new QuestDetail(_loader, _ui, _npcWz, font);
        _quest.OnSelectQuest = id =>
        {
            var data = Game.Quests.Get(id);
            var state = _questStates.TryGetValue(id, out var st) ? st.State : (byte)0;
            _questDetail.SetQuest(data, state);
        };
        _quest.OnLevelFilterChanged = RefreshQuestAvailability;
        _questDetail.OnRemoteAccept = id =>
        {
            var q = Game.Quests.Get(id);
            if (q is null || !Game.Session.IsConnected) return;
            Game.Session.Send(GameSender.QuestStartScript((short)id, q.Start.Npc, 0, 0));
        };
        _questDetail.OnResign = id =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.QuestResign((short)id));
            _questDetail.IsVisible = false;
        };
        _questDetail.OnFindNpc = id =>
        {
            var q = Game.Quests.Get(id);
            if (q is null) return;
            FocusCameraOnNpc(q.Start.Npc != 0 ? q.Start.Npc : q.Complete.Npc);
        };
        _keyConfig = new KeyConfig(_loader, _ui, font);
        _quickSlotConfig = new QuickSlotConfig(_loader, _ui, font);
        _keyConfig.OnOpenQuickSlot = () => _quickSlotConfig!.Open();
        _quickSlots = new QuickSlotBar(_loader, _ui, font, _keyConfig.BindingAt, _keyConfig.BindSkillToKey, SkillIcon);
        _optionMenu = new OptionMenu(_loader, _ui, font);
        _charInfo = new CharInfo(_loader, _ui, basicFont, _iconLoader);
        _npcTalk = new NpcTalk(_loader, _ui, _npcWz, _dlgBody, _dlgBold, _dlgName, _npcSubst);
        _shop = new Shop(_loader, _ui, font);
        _shop.OnBuy = (slot, itemId, price, count) =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.ShopBuy((short)slot, itemId, count, price));
        };
        _shop.OnSell = (pos, itemId, count) =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.ShopSell(pos, itemId, count));
        };
        _shop.OnClosed = () =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.ShopClose());
        };

        _trunk = new Trunk(_loader, _ui, font);
        _trunk.OnWithdraw = (invType, pos) => SendIfConnected(GameSender.TrunkWithdraw(invType, pos));
        _trunk.OnDeposit  = (pos, itemId, count) => SendIfConnected(GameSender.TrunkDeposit(pos, itemId, count));
        _trunk.OnSort     = () => SendIfConnected(GameSender.TrunkSort());
        _trunk.OnClosed   = () => SendIfConnected(GameSender.TrunkClose());

        _messengerWin = new Messenger(_loader, _ui, font);
        _messengerWin.OnClosed = () => SendIfConnected(GameSender.MessengerLeave());

        _notice = new Notice(_loader, _ui, font);

        _quitConfirm = new QuitConfirmOverlay(_loader, _ui, font, new Vector2(400, 300))
        {
            OnYes = () => Game.Exit(),
            OnNo = () => _quitConfirm!.IsVisible = false,
        };

        // StatusBar full submenu callbacks
        _statusBar.OnInfo    = ToggleOwnProfile;
        _statusBar.OnEquip   = () => _equip!.IsVisible     = !_equip.IsVisible;
        _statusBar.OnItems   = () => _item!.IsVisible      = !_item.IsVisible;
        _statusBar.OnSkills  = () => _skill!.IsVisible     = !_skill.IsVisible;
        _statusBar.OnStats   = () => _stats!.IsVisible     = !_stats.IsVisible;
        _statusBar.OnOptions = () => _optionMenu!.IsVisible = !_optionMenu.IsVisible;
        _statusBar.OnKeys    = ToggleKeyConfig;
        _statusBar.OnQuit     = () => _quitConfirm!.IsVisible = true;
        _statusBar.OnCashShop = () => Game.StageDirector.Push(new CashShopStage(
            _loggerFactory.CreateLogger<CashShopStage>(), _ui, Game.Font,
            Game.GraphicsDevice.PresentationParameters.BackBufferWidth,
            Game.GraphicsDevice.PresentationParameters.BackBufferHeight));
        _statusBar.OnCharacter = ToggleOwnProfile;

        _panels.Add(_statusBar);
        _panels.Add(_chatBar);
        _panels.Add(_miniMap);
        _panels.Add(_buffList);
        _panels.Add(_equip);
        _panels.Add(_item);
        _panels.Add(_skill);
        _panels.Add(_stats);
        _panels.Add(_statDetail);
        _panels.Add(_quest);
        _panels.Add(_questDetail);
        _panels.Add(_keyConfig);
        _panels.Add(_quickSlotConfig);
        _panels.Add(_quickSlots);
        _panels.Add(_optionMenu);

        // ── Persisted settings (keybinds + volumes) ───────────────────────────
        // Disk bindings are applied now; a server-sent keymap (FuncKeyMappedInit)
        // arrives later and overrides them via ApplyServerKeymap — server wins.
        var saved = Game.Settings.Load();
        _keyConfig.ImportMap(ParseMap(saved.FuncKeyMap));
        _optionMenu.LoadVolumes(saved.BgmVolume, saved.SfxVolume);
        _optionMenu.LoadResolution(saved.ResW, saved.ResH);
        ApplyAudioVolumes();
        _keyConfig.OnBindingsChanged += SaveSettings;
        _keyConfig.OnSaveToServer += SendFuncKeyMapModified;
        _keyConfig.SkillIconResolver = SkillIcon;
        _optionMenu.OnSettingsChanged += () => { SaveSettings(); ApplyAudioVolumes(); ApplyResolution(_optionMenu.ResW, _optionMenu.ResH); };
        _panels.Add(_charInfo);
        _panels.Add(_npcTalk);
        _panels.Add(_shop);
        _panels.Add(_trunk);
        _panels.Add(_messengerWin);
        _panels.Add(_notice);

        // ── New high-priority panels ─────────────────────────────────────────
        _worldMap      = new WorldMap     (_loader, _ui, font);
        _userList      = new UserList     (_loader, _ui, font);
        _channelSelect = new ChannelSelect(_loader, _ui, font);
        _messenger     = new StatusMessenger(font) { Position = new Vector2(10, 340) };
        // v95 floating damage numbers use the WZ damage-skin sprites from
        // Effect.wz/BasicEff.img (NoRed1 for white digits, NoCri1 for crit, NoMiss for
        // misses). DamageDigits loads them once; DamageNumber draws them with a text
        // fallback if any glyph is missing.
        _dmgNumbers    = new DamageNumber(font, new DamageDigits(Game.EffectWz, _loader));
        _emotionBubble = new EmotionBubble(Game.EffectWz, _loader);
        _familyWindow  = new FamilyWindow (_loader, _ui, font);

        // ── Community window actions → protocol senders ───────────────────────────
        _userList.OnAddFriend    = name => { if (name.Length > 0) { SendIfConnected(GameSender.FriendAdd(name)); _chatBar?.AddLine($"Buddy request sent to {name}.", new Color(150, 220, 150)); } };
        _userList.OnDeleteFriend = id   => SendIfConnected(GameSender.FriendDelete(id));
        _userList.OnPartyInvite  = name => { if (name.Length > 0) SendIfConnected(GameSender.PartyInvite(name)); };
        _userList.OnPartyKick    = id   => SendIfConnected(GameSender.PartyKick(id));
        _userList.OnPartyCreate  = ()   => SendIfConnected(GameSender.PartyCreate());
        _userList.OnPartyLeave   = ()   => SendIfConnected(GameSender.PartyLeave());
        _userList.OnGuildLeave   = ()   => { if (Game.CharacterId != 0) SendIfConnected(GameSender.GuildLeave(Game.CharacterId, _myName)); };
        _userList.OnGroupChatHint = type => _chatBar?.AddLine(
            $"To chat, type {type switch { 0 => "/b", 1 => "/p", 2 => "/g", 3 => "/a", _ => "/b" }} <message>",
            new Color(150, 200, 150));

        // Bottom-bar buttons. The Menu / System buttons open authentic pop-up submenus (handled inside
        // StatusBar); their items route to the panels below.
        _statusBar.OnQuest   = () => _quest!.IsVisible         = !_quest.IsVisible;
        _statusBar.OnChannel = () => _channelSelect!.IsVisible = !_channelSelect.IsVisible;
        _statusBar.OnChat    = () => _chatBar?.ToggleMode();   // BtChat shows/hides the chat input row
        _statusBar.OnMTS     = () => { };
        _statusBar.OnClaim   = () => { };
        // Menu pop-up items (Item/Equip/Stat/Skill/Quest reuse the callbacks wired above).
        _statusBar.OnCommunity = () => _userList!.IsVisible     = !_userList.IsVisible;
        _statusBar.OnMessenger = () => _messengerWin!.IsVisible = !_messengerWin.IsVisible;
        _statusBar.OnRanking   = () => _chatBar?.AddLine("Rankings are not available on this server.", new Color(200, 200, 120));
        // ── Authentic in-game system menu (CUIGameMenu) ─────────────────────────────────────────
        // ESC and the status-bar System button both open it; activating an item closes the menu and
        // routes to the matching panel. Change Skin / KeySetting / JoyPad aren't in the v95 menu.
        _gameMenu = new GameMenu(_loader, _ui, font)
        {
            OnChannel      = () => _channelSelect!.IsVisible = true,
            OnSkin         = () => _chatBar?.AddLine("UI skin change is not supported.", new Color(200, 200, 120)),
            OnGameOption   = () => _optionMenu!.IsVisible = true,
            OnSystemOption = () => _optionMenu!.IsVisible = true,
            OnQuit         = () => _quitConfirm!.IsVisible = true,
        };
        var gmPp = Game.GraphicsDevice.PresentationParameters;
        _gameMenu.Relayout(gmPp.BackBufferWidth, gmPp.BackBufferHeight);
        // ESC opens the legacy CUIGameMenu directly (OnKeyPress). The bottom-bar System button now
        // opens the MODERN StatusBar2 System pop-up; route its Game/System Option + JoyPad items.
        _statusBar.OnSystem       = () => _gameMenu!.Open();   // retained (unused by the bar pop-up)
        _statusBar.OnGameOption   = () => _optionMenu!.IsVisible = true;
        _statusBar.OnSystemOption = () => _optionMenu!.IsVisible = true;
        _statusBar.OnJoyPad       = () => _chatBar?.AddLine("JoyPad configuration is not supported.", new Color(200, 200, 120));
        _chatBar!.OnSendChat = OnChatSubmit;

        _channelSelect.OnChannelChange = ch =>
            _logger.LogInformation("Channel change requested: CH{Ch} — no packet yet", ch);

        _panels.Add(_worldMap);
        _panels.Add(_userList);
        _panels.Add(_familyWindow);
        _panels.Add(_channelSelect);
        _panels.Add(_messenger);

        // ── Network packet handler ────────────────────────────────────────────
        _netHandler = new GamePacketHandler(_loggerFactory.CreateLogger<GamePacketHandler>());

        _netHandler.OnStatChanged = (stats, mask) =>
        {
            _charStats = stats;
            if (!string.IsNullOrEmpty(stats.Name)) _myName = stats.Name;
            PushCharStats(stats);
            // Only celebrate a level-up when this burst actually carried LEVEL —
            // the snapshot is persistent, so stats.Level is set on every packet.
            if ((mask & 0x10) != 0 && stats.Level > 0)
                _messenger?.ShowLevelUp(stats.Level);
            if ((mask & 0x10) != 0 || (mask & 0x20) != 0)
                RefreshQuestAvailability();   // a level-up / job-advance can unlock new quests
        };

        _netHandler.OnFuncKeyInit = _ =>
        {
            // Apply server-sent keybindings to KeyConfig
            if (_netHandler.FuncKeyEntries != null && _keyConfig != null)
                _keyConfig.ApplyServerKeymap(_netHandler.FuncKeyEntries);
        };

        _netHandler.OnAliveReq = () =>
            Game.Session.Send(GameSender.AliveAck());

        _netHandler.OnSystemMsg = (type, _) =>
        {
            // Logged at debug level; visible messages arrive via UserChat
        };

        _channelSelect!.OnChannelChange = ch =>
        {
            _logger.LogInformation("Channel change → CH{Ch}", ch);
            Game.Session.Send(GameSender.TransferChannel(ch - 1));
        };

        // Wire AP distribution to server packets. The server replies with
        // StatChanged (AP + the raised stat) — we never mutate stats locally.
        if (_stats != null)
        {
            _stats.OnHpUp  = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.MaxHp));
            _stats.OnMpUp  = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.MaxMp));
            _stats.OnStrUp = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.Str));
            _stats.OnDexUp = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.Dex));
            _stats.OnIntUp = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.Int));
            _stats.OnLukUp = () => SendIfConnected(GameSender.UserAbilityUp(GameSender.MapleStat.Luk));
            _stats.OnAutoAssign = flag =>
            {
                var ap = _charStats.AP;
                if (ap > 0) SendIfConnected(GameSender.UserAbilityMassUp(new[] { (flag, ap) }));
            };
        }

        // Wire SkillBook SP-up to server
        // (SkillBook.LevelUpRow calls the action per skill row — wired when we have skill data)

        _netHandler.RegisterAll(Game.Session);

        // ── FieldHandlers events (wired to rendering + UI) ────────────────────
        var fh = Game.FieldHandlers;

        fh.OnSkillRecordResult += records => ApplySkills(records);
        fh.OnQuickslotInit += keys => _quickSlots?.SetKeys(keys);
        // Full per-stat buff decode is deferred; the buff icon is added
        // optimistically on cast (CastSkill). A reset clears the HUD.
        fh.OnTemporaryStatReset += () => _buffList?.ClearBuffs();

        fh.OnStatChanged += a =>
        {
            if ((a.Mask & 0x400) != 0) { if (_stats != null) _stats.Hp = a.Hp; if (_statusBar != null) _statusBar.Hp = a.Hp; }
            if ((a.Mask & 0x800) != 0) { if (_stats != null) _stats.MaxHp = a.MaxHp; if (_statusBar != null) _statusBar.MaxHp = a.MaxHp; }
            if ((a.Mask & 0x1000)!= 0) { if (_stats != null) _stats.Mp = a.Mp; if (_statusBar != null) _statusBar.Mp = a.Mp; }
            if ((a.Mask & 0x2000)!= 0) { if (_stats != null) _stats.MaxMp = a.MaxMp; if (_statusBar != null) _statusBar.MaxMp = a.MaxMp; }
            if ((a.Mask & 0x10)  != 0 && a.Level > 0) { if (_stats != null) _stats.Level = a.Level; if (_statusBar != null) _statusBar.Level = a.Level; }
            if ((a.Mask & 0x20)  != 0)   // JOB — a live advancement: relabel + re-tab the Skill window
            {
                _charStats.JobId = a.Job;
                var jobName = JobName(a.Job);
                if (_stats != null) { _stats.JobId = a.Job; _stats.Job = jobName; }
                if (_statusBar != null) _statusBar.JobName = jobName;
                SetSkillJob(a.Job);   // CharInfo is repopulated from _charStats each time it opens
            }
            if ((a.Mask & 0x4000)!= 0) { if (_stats != null) _stats.AP = a.Ap; }
            if ((a.Mask & 0x8000)!= 0) { if (_stats != null && _skill != null) _skill.SP = a.Sp; }
            if ((a.Mask & 0x40000)!=0) { if (_item != null) _item.Meso = a.Meso; }   // MONEY
        };

        // ── Loot / EXP / meso popups (Message 38) ─────────────────────────────
        // EXP gain and meso pickup float over the player via the StatusMessenger.
        // Item loot popups arrive via OnInventoryOperation (which carries the full
        // item slot with its name), so OnLootMessage only surfaces meso + warnings.
        fh.OnIncExp     += exp   => _messenger?.ShowEXP(exp);
        fh.OnIncMoney   += money => _messenger?.ShowLoot($"+{money:N0} mesos");
        fh.OnLootMessage += a =>
        {
            if (a.Warning < 0)
                _messenger?.Show(LootWarningText(a.Warning), StatusMessenger.MsgColor.Orange);
            else if (a.IsMoney)
                _messenger?.ShowLoot($"+{a.Money:N0} mesos");
        };

        // ── NPC shop ──────────────────────────────────────────────────────────
        fh.OnShopOpen += args =>
        {
            if (_shop is null) return;
            var buy = args.Items.Select((it, idx) => new Shop.ShopItem(
                Game.Names.ItemName(it.ItemId) ?? $"Item {it.ItemId:D7}",
                it.ItemId, it.Price, it.Quantity, idx)).ToList();
            var inv = _item?.AllItems ?? (IReadOnlyList<ItemInventory.InvItem>)Array.Empty<ItemInventory.InvItem>();
            var sell = inv.Select(iv => new Shop.ShopItem(iv.Name, iv.Id, 0, (short)iv.Quantity, iv.Slot)).ToList();
            _shop.OpenShop(buy, sell);
        };
        fh.OnShopResult += args => _messenger?.Show(ShopResultText(args), StatusMessenger.MsgColor.White);

        // ── Player storage / trunk ────────────────────────────────────────────
        fh.OnTrunkResult += args =>
        {
            if (_trunk is null) return;
            switch (args.ResultType)
            {
                case 22:   // OpenTrunkDlg
                case 9:    // GetSuccess
                case 13:   // PutSuccess
                case 15:   // SortItem
                {
                    var trunkItems = args.Items.Select(it => new Trunk.TrunkItem(
                        Game.Names.ItemName(it.ItemId) ?? $"Item {it.ItemId:D7}",
                        it.ItemId, (short)it.Quantity, it.InvType, it.PositionInType)).ToList();
                    var inv = _item?.AllItems ?? (IReadOnlyList<ItemInventory.InvItem>)Array.Empty<ItemInventory.InvItem>();
                    var deposit = inv.Select(iv => new Trunk.TrunkItem(
                        iv.Name, iv.Id, (short)iv.Quantity, 0, iv.Slot)).ToList();
                    if (args.ResultType == 22) _trunk.Open(args.Money, trunkItems, deposit);
                    else                       _trunk.Refresh(args.Money, trunkItems, deposit);
                    break;
                }
                case 19:   // MoneySuccess
                    _trunk.SetMoney(args.Money);
                    break;
                case 24:   // ServerMsg
                    if (!string.IsNullOrEmpty(args.Message))
                        _messenger?.Show(args.Message, StatusMessenger.MsgColor.White);
                    break;
                default:   // failure subtypes (GetNoMoney, PutNoSpace, …)
                    _messenger?.Show(TrunkResultText(args.ResultType), StatusMessenger.MsgColor.White);
                    break;
            }
        };

        // ── Maple Messenger ───────────────────────────────────────────────────
        fh.OnMessengerResult += args =>
        {
            switch (args.Action)
            {
                case 1:  // SelfEnterResult — our slot; open the window
                    _messengerWin?.SetSelf(args.UserIndex);
                    _chatBar?.AddLine("Messenger opened.", new Color(150, 220, 150));
                    break;
                case 0:  // Enter — another participant joined
                    if (args.Name is { Length: > 0 })
                    {
                        _messengerWin?.SetParticipant(args.UserIndex, args.Name);
                        _chatBar?.AddLine($"{args.Name} joined the messenger.", new Color(150, 220, 150));
                    }
                    break;
                case 2:  // Leave
                    _messengerWin?.RemoveParticipant(args.UserIndex);
                    break;
                case 3:  // Invite
                    _pendingMessengerId = args.MessengerId;
                    _hasMessengerInvite = true;
                    _chatBar?.AddLine($"{args.Name} invited you to a messenger — type /maccept",
                        new Color(150, 220, 150));
                    break;
                case 4:  // InviteResult
                    _chatBar?.AddLine(args.Flag
                        ? $"Invited {args.Name} to the messenger."
                        : $"{args.Name} could not be invited.",
                        args.Flag ? new Color(150, 220, 150) : new Color(220, 120, 120));
                    break;
                case 5:  // Blocked
                    _chatBar?.AddLine($"{args.Name} declined the messenger.", new Color(220, 120, 120));
                    break;
                case 6:  // Chat
                    if (args.Chat is { Length: > 0 })
                        _chatBar?.AddLine($"[Messenger] {args.Chat}", new Color(120, 220, 220));
                    break;
            }
        };

        // ── Quests ────────────────────────────────────────────────────────────
        fh.OnQuestRecord += a =>
        {
            _quest?.UpdateQuest(a.QuestId, a.State, a.Value,
                Game.Names.QuestName(a.QuestId) ?? $"Quest {a.QuestId}");
            if (a.State == 0) _questStates.Remove(a.QuestId);
            else              _questStates[a.QuestId] = (a.State, a.Value ?? string.Empty);
            if (a.State == 2) _messenger?.ShowQuest(Game.Names.QuestName(a.QuestId) ?? $"Quest {a.QuestId}");
            RefreshQuestAvailability();   // a start/complete/forfeit changes markers + the Available list
        };

        // ── In-game migration (channel transfer / cash-shop return) ───────────
        // The server replies to TransferChannel with MigrateCommand(16); reconnect
        // to the new endpoint and re-send MigrateIn with the cached character id.
        fh.OnMigrateCommand += (host, port) =>
        {
            _logger.LogInformation("MigrateCommand → reconnecting to {Host}:{Port}",
                string.Join('.', host), port);
            _ = Game.Migration.BeginMigrateAsync(host, port, Game.CharacterId);
        };

        fh.OnMobEnter += args =>
        {
            var mob = new MobLook(args.MobId, args.TemplateId,
                                  new Vector2(args.X, args.Y))
            {
                Name  = Game.Names?.MobName(args.TemplateId) ?? string.Empty,
                Level = _mobInfoSvc?.Get(args.TemplateId).Level ?? 0,
            };
            mob.Load(_loader!, _mobWz);
            _mobs[args.MobId] = mob;
            _logger.LogDebug("Mob enter: id={Id} tmpl={T} pos=({X},{Y})", args.MobId, args.TemplateId, args.X, args.Y);
        };

        // MobHPIndicator(298) — server's "you just hit this mob" update. Pushes the mob's
        // current HP percentage to drive the HP bar above the mob (auto-hides after ~5s).
        fh.OnMobHpIndicator += (mobId, pct) =>
        {
            if (_mobs.TryGetValue(mobId, out var mob)) mob.SetHpPercent(pct);
        };

        fh.OnMobLeave += mobId =>
        {
            _mobs.Remove(mobId);
            _controlledMobs.Remove(mobId);
            _mobCtl.Remove(mobId);
            _pendingCtrl.Remove(mobId);
        };

        fh.OnMobMove += args =>
        {
            if (_mobs.TryGetValue(args.MobId, out var mob))
            {
                var newPos    = new Vector2(args.X, args.Y);
                var dx        = newPos.X - mob.Position.X;
                mob.SetFacing(dx < 0);
                mob.Position  = newPos;
                mob.SetState(MobLook.MobState.Move);
            }
        };

        fh.OnMobDamaged += args =>
        {
            if (_mobs.TryGetValue(args.MobId, out var mob))
            {
                if (args.Hp >= 0)  { mob.Hp    = args.Hp; }
                if (args.MaxHp > 0){ mob.MaxHp  = args.MaxHp; }
                if (args.Damage > 0)
                {
                    mob.OnHit(args.Damage);
                    _dmgNumbers?.Add(args.Damage, mob.Position, DamageNumber.Kind.MobDamage);
                }
                if (args.Hp == 0) mob.OnDie();
            }
            // Passive (non-firstAttack) mobs flip aggro when damaged. In a single-player
            // session every MobDamaged is us hitting it, so we just route the event to our
            // controller for the aggro timer to kick in. (Multi-client: this would
            // over-aggro on any nearby player's hit; revisit when party play lands.)
            if (_mobCtl.TryGetValue(args.MobId, out var ctl)) ctl.OnDamagedByPlayer();
        };

        fh.OnMobChangeController += (mobId, isCtrl) =>
        {
            if (isCtrl)
            {
                _controlledMobs.Add(mobId);
                TryCreateMobController(mobId);
            }
            else
            {
                _controlledMobs.Remove(mobId);
                _mobCtl.Remove(mobId);
                _pendingCtrl.Remove(mobId);
            }
        };

        fh.OnNpcEnter += args =>
        {
            var existing = _npcs.FirstOrDefault(n => n.ObjId == args.ObjId);
            if (existing is null)
            {
                var npc = new NpcLook(args.TemplateId, new Vector2(args.X, args.Y), Game.Font)
                {
                    ObjId = args.ObjId,
                    Name  = Game.Names.NpcName(args.TemplateId) ?? string.Empty,
                };
                npc.Load(_loader!, _npcWz);
                npc.FaceLeft(args.FacingLeft);
                AttachAmbientSpeak(npc, args.TemplateId);
                _npcs.Add(npc);
            }
        };

        fh.OnNpcLeave += objId =>
        {
            _npcs.RemoveAll(n => n.ObjId == objId);
        };

        fh.OnUserEnter += args =>
        {
            if (_otherChars.ContainsKey(args.CharId)) return;
            var other = new OtherCharLook(args.CharId, args.Name, args.Level, args.Look,
                                          new Vector2(args.X, args.Y), Game.Font);
            other.LoadSprites(_loader!, _charWz, _charRenderer);
            _otherChars[args.CharId] = other;
        };

        fh.OnUserLeave  += id => _otherChars.Remove(id);

        // Character-profile response: open the UserInfo window for the requested player. The packet
        // carries no name/look, so reuse the in-field OtherCharLook (keyed by char id) for those.
        fh.OnCharacterInfo += a =>
        {
            if (_charInfo == null) return;
            var oc = _otherChars.TryGetValue(a.CharId, out var v) ? v : null;
            _charInfo.ShowProfile(oc?.Name ?? string.Empty, a.Level, JobName(a.Job), a.Fame,
                a.Guild, a.Alliance, _charRenderer, oc?.Look, a.Pets);
        };

        fh.OnUserMove   += args =>
        {
            if (_otherChars.TryGetValue(args.CharId, out var other))
                other.SetPosition(args.X, args.Y);
        };

        // UserEmotion broadcast (charId>0) / UserEmotionLocal echo (charId==0).
        // Both apply the emotion + spawn the over-head bubble. The local trigger
        // already applied locally in TriggerEmotion before the packet was sent —
        // a 0-charId echo from the server (Kinoko vanilla doesn't emit one) just
        // refreshes the timer idempotently.
        fh.OnUserEmotion += a =>
        {
            if (a.CharId == 0 || a.CharId == Game.CharacterId)
            {
                _player?.SetEmotion(a.Emotion, a.DurationMs);
                if (a.Emotion > 0)
                {
                    var pos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
                    _emotionBubble?.Add(a.Emotion, pos - new Vector2(0, 60));
                }
            }
            else if (_otherChars.TryGetValue(a.CharId, out var oc))
            {
                oc.SetEmotion(a.Emotion, a.DurationMs);
                if (a.Emotion > 0)
                    _emotionBubble?.Add(a.Emotion, oc.Position - new Vector2(0, 60));
            }
        };

        fh.OnDropEnter += args =>
            _drops[args.DropId] = new DropSprite(args.DropId, args.IsMoney, args.ItemIdOrAmount,
                new Vector2(args.SourceX, args.SourceY), new Vector2(args.X, args.Y), args.Animated,
                args.IsMoney ? null : _iconLoader?.LoadIcon(args.ItemIdOrAmount), Game.Font);

        fh.OnDropLeave  += args =>
        {
            // Picked up (user/mob/pet) → the item flies into the picker and fades (CDropPool absorb),
            // then is removed when the animation finishes. Other leave types (expire/explode) remove now.
            if (args.LeaveType is 2 or 3 or 5 && _drops.TryGetValue(args.DropId, out var d))
                // Live target: re-read the player's chest each frame so the item homes to the moving body.
                d.StartAbsorb(() => (_physics?.Position ?? _player?.Position ?? Vector2.Zero) - new Vector2(0, 28));
            else
                _drops.Remove(args.DropId);
        };

        fh.OnInventoryOperation += ops =>
        {
            var lookDirty = false;   // an equipped slot changed → rebuild the rendered avatar once at the end
            foreach (var op in ops)
            {
                var tab = InvTypeToTab(op.InvType);
                switch (op.OpType)
                {
                    case 0: // NewItem — full item slot
                        if (op.Item is null) break;
                        if (op.InvType == InventoryType.Equipped)
                        {
                            _equip?.SetEquipped(op.Pos, op.Item.ItemId, ItemDisplayName(op.Item));
                            if (_myLook != null) { _myLook.HairEquip[op.Pos] = op.Item.ItemId; lookDirty = true; }
                        }
                        else if (tab >= 0)
                        {
                            _item?.SetSlot(tab, op.Pos, ToInvItem(op.Item, tab, op.Pos));
                        }
                        _messenger?.ShowLoot(ItemDisplayName(op.Item));
                        break;
                    case 1: // ItemNumber — quantity changed
                        if (tab >= 0) _item?.SetSlotQuantity(tab, op.Pos, op.Quantity);
                        break;
                    case 2: // Position — move. Equipped slots are negative body parts:
                            // NewPos<0 = equipping (inv -> worn), Pos<0 = unequipping (worn -> inv).
                        if (op.NewPos < 0)
                        {
                            // Equip: the item is still in the equip tab at op.Pos. Move it onto
                            // the worn body part and clear the inventory cell.
                            var moved = _item?.ItemAt(0, op.Pos);
                            if (moved != null)
                            {
                                _equip?.SetEquipped(-op.NewPos, moved.Id, moved.Name);
                                _item?.RemoveSlot(0, op.Pos);
                                if (_myLook != null) { _myLook.HairEquip[-op.NewPos] = moved.Id; lookDirty = true; }
                            }
                        }
                        else if (op.Pos < 0)
                        {
                            // Unequip: pull the worn item back into the equip tab at op.NewPos.
                            if (_equip != null && _equip.TryGetEquipped(-op.Pos, out var uid, out var uname))
                            {
                                _equip.RemoveEquipped(-op.Pos);
                                if (op.NewPos > 0)
                                {
                                    _item?.SetSlot(0, op.NewPos,
                                        new ItemInventory.InvItem { Id = uid, Name = uname, Quantity = 1 });
                                }
                            }
                            if (_myLook != null && _myLook.HairEquip.Remove(-op.Pos)) lookDirty = true;
                        }
                        else if (tab >= 0)
                        {
                            _item?.MoveSlot(tab, op.Pos, op.NewPos);
                        }
                        break;
                    case 3: // DelItem. A negative position is a worn slot (e.g. an equipped item dropped to
                            // the ground → the server sends delItem(Equip, -bodyPart)); fold it onto the equip
                            // window + avatar, not the item grid.
                        if (op.InvType == InventoryType.Equipped || op.Pos < 0)
                        {
                            var part = op.Pos < 0 ? -op.Pos : op.Pos;
                            _equip?.RemoveEquipped(part);
                            if (_myLook != null && _myLook.HairEquip.Remove(part)) lookDirty = true;
                        }
                        else if (tab >= 0) _item?.RemoveSlot(tab, op.Pos);
                        break;
                }
            }
            // Re-apply the mutated look so the worn avatar (and the profile) re-render with the change.
            if (lookDirty && _myLook != null && _player != null && _charRenderer != null)
            {
                _player.SetAvatar(_charRenderer, _myLook);
                _charInfo?.SetAvatar(_charRenderer, _myLook);
            }
        };

        fh.OnUserChat += args =>
        {
            // The wire packet carries only the speaker's char id; resolve a
            // display name from the other-char roster, falling back to our own
            // name (the server echoes our map chat back to us).
            var speaker = _otherChars.TryGetValue(args.CharId, out var oc) && !string.IsNullOrEmpty(oc.Name)
                ? oc.Name
                : _myName;
            _chatBar?.AddLine($"{speaker} : {args.Text}");
            _balloons?.Set(args.CharId, args.Text);
        };

        // ── Social: group chat / whisper / party / friends ────────────────────
        fh.OnGroupMessage += (type, from, text) =>
            _chatBar?.AddLine($"[{GroupPrefix(type)}] {from} : {text}", GroupColor(type), GroupLineType(type));

        fh.OnWhisper += (from, ch, text) =>
            _chatBar?.AddLine($"[whisper] {from} : {text}", new Color(220, 150, 220), ChatBar.ChatLineType.Whisper);

        fh.OnFriendList += list =>
        {
            _userList!.ClearFriends();
            _buddyIds.Clear();
            _buddyIds.AddRange(list.Where(f => f.Online).Select(f => f.FriendId));
            foreach (var f in list)
            {
                _userList.AddFriend(new UserList.FriendEntry
                {
                    FriendId = f.FriendId,
                    Name     = f.Name,
                    Level    = 0,
                    Job      = string.Empty,
                    Online   = f.Online,
                    Location = f.Online ? $"Ch {f.Channel + 1}" : "Offline",
                });
            }
            _logger.LogInformation("Friend list loaded: {Count} friend(s)", list.Count);
        };

        fh.OnPartyLoad += (members, bossId) =>
        {
            _partyMemberIds.Clear();
            _partyMemberIds.AddRange(members.Select(m => m.CharId));
            _userList!.SetParty(members.Select(m => new UserList.PartyEntry
            {
                CharId = m.CharId,
                Name   = m.Name,
                Level  = m.Level,
                Job    = JobName(m.Job),
                HpPct  = 100,
            }));
            _logger.LogInformation("Party loaded: {Count} member(s) boss={Boss}", members.Count, bossId);
        };

        fh.OnGuildLoad += args =>
        {
            if (_userList is null) return;
            if (args is null) { _userList.SetGuild(string.Empty, Array.Empty<UserList.GuildEntry>()); _guildMemberIds.Clear(); return; }
            _guildMemberIds.Clear();
            _guildMemberIds.AddRange(args.Members.Where(m => m.Online).Select(m => m.CharacterId));
            _userList.SetGuild(args.Name, args.Members.Select(m => new UserList.GuildEntry
            {
                Name   = m.Name,
                Rank   = GuildRankName(m.Rank),
                Online = m.Online,
            }));
            _logger.LogInformation("Guild loaded: {Name} ({Count} members)", args.Name, args.Members.Count);
        };

        fh.OnPartyInvite += (inviterId, name) =>
        {
            _pendingInviterId = inviterId;
            _hasPendingInvite = true;
            _chatBar?.AddLine($"{name} invited you to a party — type /accept", new Color(150, 220, 150));
        };

        fh.OnScriptMessage += args =>
        {
            if (_npcTalk is null) return;
            // The answer we send back must echo the type the server sent.
            var type = args.MsgType;
            var param = args.MessageParam;
            _dialogMsgType = type;
            if (_npcSubst != null) _npcSubst.PlayerName = _myName;

            switch (type)
            {
                case ScriptMessageType.AskMenu:
                    _npcTalk.ShowMenu(args.SpeakerId, param, args.Text);
                    _npcTalk.OnMenuChoice = choice => SendIfConnected(GameSender.ScriptAnswerNumber(type, choice));
                    _npcTalk.OnClose      = ()     => SendIfConnected(GameSender.ScriptAnswerCancel(type));
                    break;

                case ScriptMessageType.AskYesNo:
                case ScriptMessageType.AskAccept:
                    _npcTalk.ShowYesNo(args.SpeakerId, param, args.Text, type == ScriptMessageType.AskAccept);
                    _npcTalk.OnYes   = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 1));
                    _npcTalk.OnNo    = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 0));
                    _npcTalk.OnClose = () => SendIfConnected(GameSender.ScriptAnswerSay(type, -1));
                    break;

                case ScriptMessageType.AskText:
                case ScriptMessageType.AskBoxText:
                    _npcTalk.ShowAskText(args.SpeakerId, param, args.Text, args.DefaultText,
                        args.MinLength, args.MaxLength, type == ScriptMessageType.AskBoxText);
                    _npcTalk.OnTextConfirm = text => SendIfConnected(GameSender.ScriptAnswerText(type, text));
                    _npcTalk.OnClose       = ()   => SendIfConnected(GameSender.ScriptAnswerCancel(type));
                    break;

                case ScriptMessageType.AskNumber:
                    _npcTalk.ShowAskNumber(args.SpeakerId, param, args.Text, args.DefaultNum, args.MinNum, args.MaxNum);
                    _npcTalk.OnNumberConfirm = num => SendIfConnected(GameSender.ScriptAnswerNumber(type, num));
                    _npcTalk.OnClose         = ()  => SendIfConnected(GameSender.ScriptAnswerCancel(type));
                    break;

                default: // SAY (0), SayImage (1) and rare types — a message with optional prev/next.
                    _npcTalk.ShowSay(args.SpeakerId, param, args.Text, args.HasPrev, args.HasNext);
                    // SAY action: next/ok = 1, prev = 0, close = -1.
                    _npcTalk.OnOk = _npcTalk.OnNext = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 1));
                    _npcTalk.OnPrev  = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 0));
                    _npcTalk.OnClose = () => SendIfConnected(GameSender.ScriptAnswerSay(type, -1));
                    break;
            }
        };

        // Note: the server keymap is applied via _netHandler.OnFuncKeyInit (the
        // GamePacketHandler decoder, which reads bDefault + 89 correctly). We do not
        // also wire fh.OnFuncKeyMappedInit here to avoid a double-apply.

        Game.AudioPlayer.Stop();

        // Anchor the HUD to the (now enlarged) in-game window.
        RelayoutHud();

        // Subscribe to the channel-server SetField so we can load the real map
        // + spawn position when the migration handoff completes.
        Game.FieldHandlers.OnSetField += OnSetField;

        _logger.LogInformation("GameStage entered — awaiting SetField from channel");
    }

    /// <summary>
    /// Re-anchor every HUD panel to the active window size. Edge-anchored panels
    /// (status bar, chat, buff list, loot messages) override
    /// <see cref="GamePanel.Relayout"/>; the quit overlay is recentred
    /// separately. Toggle windows (inventory, skills, …) keep their authored
    /// positions — they stay fully on-screen at the larger in-game resolution.
    /// </summary>
    private void RelayoutHud()
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;
        foreach (var p in _panels) p.Relayout(w, h);
        _quitConfirm?.Relayout(w, h);
        _gameMenu?.Relayout(w, h);
    }

    /// <summary>Apply a new in-game window resolution live: resize, then re-anchor the HUD. The
    /// per-frame camera-dimension sync picks up the new viewport. Persisted via SaveSettings.</summary>
    private void ApplyResolution(int w, int h)
    {
        var pp = GraphicsDevice.PresentationParameters;
        if (pp.BackBufferWidth == w && pp.BackBufferHeight == h) return;
        Game.ResizeWindow(w, h);
        RelayoutHud();
    }

    public override void OnExit()
    {
        // Restore the login-flow window size we enlarged from in OnEnter.
        Game.ResizeWindow(_prevW, _prevH);
        // OnSetField uses a named method — safe to unsubscribe
        Game.FieldHandlers.OnSetField -= OnSetField;
        // Lambda subscriptions will be cleaned up when FieldHandlers is
        // cleared on next stage's OnEnter; GameStage lifetime is short.
        Game.FieldHandlers.ClearAllExceptSetField();
        _loader?.Dispose();
        _loader = null;
        _dlgBody?.Dispose(); _dlgBody = null;
        _dlgBold?.Dispose(); _dlgBold = null;
        _dlgName?.Dispose(); _dlgName = null;
        _tipFont?.Dispose(); _tipFont = null;
        _tipTabFont?.Dispose(); _tipTabFont = null;
        base.OnExit();
    }

    // Map-change fade transition. _fadeAlpha: 0 = clear .. 1 = opaque black. On a
    // field-change SetField we fade to black, swap the map at full black (pop-free,
    // via _pendingField), then fade back in (stage transitions are otherwise instant).
    private float _fadeAlpha;
    private int   _fadePhase;            // 0 idle, +1 fading to black, -1 fading in
    private SetFieldArgs? _pendingField; // SetField awaiting the full-black swap moment
    private const float FadeToBlackPerSec = 1f / 0.18f;  // ~180 ms to black
    private const float FadeInPerSec      = 1f / 0.30f;  // ~300 ms to clear

    private void OnSetField(SetFieldArgs args)
    {
        _fieldKey = args.FieldKey;
        if (_map is null)
        {
            return;
        }
        // Stat / look / inventory / quests only arrive with the migrate (CharacterData)
        // SetField. A field-transfer SetField (portal / channel / revive) carries only a
        // minimal { posMap, portal } block, so args.Stat is null there - seed nothing from it.
        if (args.Stat is not null)
        {
            Game.CharacterId = args.Stat.CharacterId;
            // SetField's CharacterStat is the authoritative name source (StatChanged may not carry it).
            if (!string.IsNullOrEmpty(args.Stat.Name))
            {
                _myName = args.Stat.Name;
                if (_statusBar != null) _statusBar.CharName = _myName;
            }
            // Seed the stat snapshot from SetField's CharacterStat so the Stat / Character-info
            // windows show the real name + stats immediately (StatChanged arrives later and never
            // carries the name). Guild comes from a separate packet, so preserve any known value.
            var seed = args.Stat;
            _charStats = new CharStats
            {
                Name  = seed.Name, Level = seed.Level, JobId = seed.Job,
                Str = seed.Str, Dex = seed.Dex, Int = seed.Int, Luk = seed.Luk,
                Hp = seed.Hp, MaxHp = seed.MaxHp, Mp = seed.Mp, MaxMp = seed.MaxMp,
                Exp = seed.Exp, AP = seed.Ap, Fame = seed.Pop, Guild = _charStats.Guild,
            };
            PushCharStats(_charStats);
            if (args.Look is not null && _player is not null && _charRenderer is not null)
            {
                _myLook = args.Look;
                _player.SetAvatar(_charRenderer, args.Look);
                _charInfo?.SetAvatar(_charRenderer, args.Look);   // profile shows the real avatar
            }
            PopulateInventory(args);
            PopulateQuests(args);
        }
        // Ask the server for our guild roster once (it isn't pushed on login).
        if (!_guildLoadSent && Game.Session.IsConnected)
        {
            Game.Session.Send(GameSender.GuildLoad());
            _guildLoadSent = true;
        }
        // Clear the old field's entities NOW at the SetField boundary - the deferred terrain
        // swap (see AdvanceFieldTransition) only runs ~180 ms later at full black, by which
        // point the new map's NpcEnter / MobEnter / DropEnter spawn packets have already
        // arrived. Clearing then would wipe the freshly-spawned new map entities. Spawn
        // handlers don't depend on _field, so populating during the fade-out window is fine.
        _mobs.Clear();
        _controlledMobs.Clear();
        _mobCtl.Clear();          // controllers reference the OLD FieldScene; they'd be orphaned after the swap
        _pendingCtrl.Clear();
        _npcs.Clear();
        _otherChars.Clear();
        _drops.Clear();
        _emotionBubble?.Clear();   // emotion bubbles from the old map shouldn't persist into the new one

        // Defer the actual terrain swap to the fully-black moment of the fade transition
        // (see AdvanceFieldTransition) so the old map never visibly pops to the new one.
        _pendingField = args;
        _fadePhase = 1;   // begin fade-to-black
    }

    // Performs the real map swap: tears down the previous field's entities and loads the
    // destination terrain. Destination map + spawn portal come from the full CharacterStat
    // on a migrate SetField, or the minimal top-level block on a transfer.
    private void ApplyFieldChange(SetFieldArgs args)
    {
        int  targetMap    = args.Stat?.PosMap ?? args.PosMap;
        byte targetPortal = args.Stat?.Portal ?? args.Portal;
        try
        {
            // Entity clears already happened immediately at the SetField boundary in OnSetField -
            // this swap is deferred to full black, and clearing here would wipe the new map's
            // spawn packets that arrived during the fade-out window.
            _field = new FieldScene(_loggerFactory.CreateLogger<FieldScene>(), _map!, _loader!);
            _field.Load(targetMap);
            _physics = new PlayerController(_field);
            _field.PlacePlayerAtPortal(_physics, targetPortal);
            // Re-position archlo's CharLook to the spawn point.
            if (_player != null)
            {
                _player.Position = _physics.Position;
            }
            _camera.Target = _physics.Position;
            _camera.MapBounds = _field.Bounds;   // VR rect when present, else the foothold AABB
            UpdateMapName(targetMap);
            _miniMap?.SetField(_field.MiniMap, _mapStreet, _mapNameText);
            PlayMapBgm(_field.Info.Bgm);
            _logger.LogInformation("SetField processed - mapId={Map} portal={Portal} money={Money}",
                targetMap, targetPortal, args.Money);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load field {Id}", targetMap);
        }
    }

    // Drives the map-change fade: fade to black, swap the map at full black (pop-free), then
    // fade back in. Runs on the Update thread alongside OnSetField, so the swap is race-free.
    private void AdvanceFieldTransition(float dt)
    {
        if (_fadePhase > 0)            // fading to black
        {
            _fadeAlpha += dt * FadeToBlackPerSec;
            if (_fadeAlpha >= 1f)
            {
                _fadeAlpha = 1f;
                if (_pendingField is { } pending)
                {
                    _pendingField = null;
                    ApplyFieldChange(pending);
                }
                _fadePhase = -1;       // begin fade-in
            }
        }
        else if (_fadePhase < 0)       // fading in from black
        {
            _fadeAlpha -= dt * FadeInPerSec;
            if (_fadeAlpha <= 0f)
            {
                _fadeAlpha = 0f;
                _fadePhase = 0;
            }
        }
    }

    // Build the AI controller for a mob the server told us to drive. Deferred via
    // _pendingCtrl when _field hasn't been swapped yet (new map's MobChangeController
    // packets land during the fade-out, before ApplyFieldChange creates _field). The
    // mob-tick block in Update drains _pendingCtrl every frame once _field is non-null.
    private void TryCreateMobController(int mobId)
    {
        if (_field is null || _mobInfoSvc is null) { _pendingCtrl.Add(mobId); return; }
        if (!_mobs.TryGetValue(mobId, out var look)) { _pendingCtrl.Add(mobId); return; }
        var info = _mobInfoSvc.Get(look.TemplateId);
        _mobCtl[mobId] = new MobController(look, _field, info, _mobRng);
    }

    // Walk-into-portal travel: if the player stands on a warp portal (one with a
    // real target map), send UserTransferFieldRequest. The server replies with a
    // fresh SetField for the destination map.
    private const float PortalEnterRange = 40f;

    private bool TryEnterPortal()
    {
        if (_field is null || _physics is null || !Game.Session.IsConnected) return false;
        if (_fadePhase != 0) return false;   // ignore portal input while a map transition is running
        var pos = _physics.Position;
        foreach (var portal in _field.Portals.Values)
        {
            if (portal.TargetMap <= 0 || portal.TargetMap == 999_999_999) continue;
            if (Vector2.Distance(pos, portal.Position) > PortalEnterRange) continue;
            // The server resolves the portal by name on the CURRENT field, so send
            // the source portal name (pn, e.g. "out00"), not the destination portal
            // name (tn). See MigrationHandler.handleUserTransferFieldRequest.
            Game.Session.Send(GameSender.TransferField(
                _fieldKey, portal.TargetMap, portal.Name, (short)pos.X, (short)pos.Y));
            _logger.LogInformation("Portal '{Name}' → map {Map} (portal {Tp})",
                portal.Name, portal.TargetMap, portal.TargetPortal);
            return true;
        }
        return false;
    }

    // ── Skills / buffs ─────────────────────────────────────────────────────────

    private void ApplySkills(IReadOnlyList<SkillRecord> records)
    {
        if (_skill is null)
        {
            return;
        }
        foreach (var r in records) _skillRecords[r.SkillId] = r; // delta-safe merge (never replace)
        RebuildSkillBook();
    }

    // Skill icon for KeyConfig / quickslot bound keys (cached WzSprite from Skill.wz).
    private WzSprite? SkillIcon(int skillId)
    {
        if (_skillIconCache.TryGetValue(skillId, out var s)) return s;
        var canvas = Game.Skills.Get(skillId)?.Icon;
        var sprite = canvas != null ? _loader?.Load(canvas) : null;
        _skillIconCache[skillId] = sprite;
        return sprite;
    }

    // Rebuild the Skill window's entries: every skill in the job's reachable
    // Skill.wz tree (so learnable-but-unleveled skills appear), unioned with any
    // server-granted records (covers learned skills outside the tree, and the
    // case where Skill.wz is unavailable). Current levels come from the
    // accumulated records; the per-job tab grouping happens in SkillBook.
    private void RebuildSkillBook()
    {
        if (_skill is null)
        {
            return;
        }
        var ids = new HashSet<int>();
        foreach (var root in JobConstants.GetSkillRoots(_skill.JobId))
        {
            foreach (var id in Game.Skills.EnumerateSkillIds(root))
            {
                ids.Add(id);
            }
        }
        foreach (var id in _skillRecords.Keys)
        {
            ids.Add(id);
        }

        _skill.SetSkills(ids.Order().Select(id =>
        {
            var info = Game.Skills.Get(id);
            _skillRecords.TryGetValue(id, out var rec);
            var level = rec?.Level ?? 0;
            return new SkillBook.SkillEntry
            {
                Id = id,
                Name = Game.Names.SkillName(id) ?? $"Skill {id}",
                Level = level,
                MaxLevel = info?.MaxLevel ?? (rec is { MasterLevel: > 0 } ? rec.MasterLevel : 20),
                Passive = info?.Passive ?? false,
                MpCost = info?.MpConAt(Math.Max(1, level)) ?? 0,
                IconCanvas = info?.Icon,
            };
        }));
    }

    // Point the Skill window at a job and re-tab it — but only when the job
    // actually changes (this is called on every StatChanged / stat push, and the
    // rebuild walks Skill.wz). Used by the SetField seed and a live JOB change.
    private void SetSkillJob(int jobId)
    {
        if (_skill is null || jobId == _lastSkillJob)
        {
            return;
        }
        _lastSkillJob = jobId;
        _skill.JobId = jobId;
        RebuildSkillBook();
    }

    // Double-click a learned active skill → cast it. Buff skills (those with a
    // Skill.wz duration) show an optimistic buff icon with the real duration; cast
    // starts the real cooldown. (Full TemporaryStatSet decode is still deferred.)
    private void CastSkill(int skillId, int level)
    {
        if (!Game.Session.IsConnected)
        {
            return;
        }
        Game.Session.Send(GameSender.UseSkill(skillId, (byte)level));

        // Play the skill's body action (random from its WZ action list) + effect animations.
        var cast = Game.Skills.GetCastInfo(skillId);
        if (cast is not null)
        {
            if (cast.Actions.Length > 0)
            {
                var act = cast.Actions[_attackRng.Next(cast.Actions.Length)];
                if ((_player?.PlayAction(act) ?? 0f) <= 0f) _player?.Attack(false);
            }
            SpawnSkillEffect(cast.Effect, screen: false);
            SpawnSkillEffect(cast.Effect0, screen: false);
            SpawnSkillEffect(cast.Screen, screen: true);
        }

        var info = Game.Skills.Get(skillId);
        var buffSeconds = info?.BuffTimeAt(level) ?? 0;
        if (buffSeconds > 0)
        {
            _buffList?.AddBuff(Game.Names.SkillName(skillId) ?? $"Skill {skillId}", buffSeconds);
        }
        var cooldown = info?.CooltimeAt(level) ?? 0;
        if (cooldown > 0)
        {
            _skill?.StartCooldown(skillId, cooldown);
        }
    }

    // Spawn a one-shot skill effect animation (anchored on the caster, or fullscreen).
    private void SpawnSkillEffect(object? node, bool screen)
    {
        if (node is null) return;
        var anim = _loader?.LoadAnimation(node);
        if (anim is null) return;
        _skillEffects.Add(new SkillEffect { Anim = anim, Flip = _player?.FacingLeft ?? false, Screen = screen });
    }

    private sealed class SkillEffect
    {
        public required AnimatedSprite Anim;
        public bool Flip;
        public bool Screen;
        public double Elapsed;
    }

    // Double-click in the item grid: use a consumable, or equip an equip.
    private void OnInventoryItemActivate(int tab, int slot, int itemId)
    {
        if (!Game.Session.IsConnected)
        {
            return;
        }
        switch (tab)
        {
            case 0: // Equip tab → equip to its body part (negative dest slot).
                var bodyPart = EquipBodyPart(itemId);
                if (bodyPart != 0)
                {
                    Game.Session.Send(GameSender.ChangeSlotPosition(
                        InventoryType.Equip, (short)slot, (short)-bodyPart, 1));
                }
                break;
            case 1: // Use tab → consume (potion etc.); the server echoes StatChanged.
                Game.Session.Send(GameSender.UseItem((short)slot, itemId));
                break;
        }
    }

    // v95 equip item id → equipped body part. 0 = not a known equip slot.
    private static int EquipBodyPart(int itemId)
    {
        var cat = itemId / 10000;
        return cat switch
        {
            100 => 1,   // Hat
            101 => 2,   // Face acc
            102 => 3,   // Eye acc
            103 => 4,   // Earring
            104 => 5,   // Top
            105 => 5,   // Overall
            106 => 6,   // Bottom
            107 => 7,   // Shoes
            108 => 8,   // Gloves
            109 => 10,  // Shield
            110 => 9,   // Cape
            111 => 12,  // Ring
            112 => 17,  // Pendant
            113 => 49,  // Belt
            114 => 50,  // Medal
            _   => (itemId / 1_000_000 == 1 && cat is >= 130 and <= 170) ? 11 : 0, // Weapon
        };
    }

    // ItemInventory tab index → wire InventoryType (inverse of InvTypeToTab).
    private static InventoryType TabToInvType(int tab) => tab switch
    {
        0 => InventoryType.Equip,
        1 => InventoryType.Consume,
        2 => InventoryType.Install,
        3 => InventoryType.Etc,
        4 => InventoryType.Cash,
        _ => InventoryType.Equip,
    };

    // 5 item tabs map to ItemInventory tab indices; Equipped(0) goes to the
    // equip panel, not the item grid.
    private static int InvTypeToTab(InventoryType t) => t switch
    {
        InventoryType.Equip   => 0,
        InventoryType.Consume => 1,
        InventoryType.Install => 2,
        InventoryType.Etc     => 3,
        InventoryType.Cash    => 4,
        _                     => -1, // Equipped
    };

    // Prefer the wire-provided title, then the String.wz name, then a formatted id.
    private string ItemDisplayName(InventoryItem it) =>
        !string.IsNullOrEmpty(it.Title) ? it.Title
            : Game.Names.ItemName(it.ItemId) ?? $"Item {it.ItemId:D7}";

    // ── Settings persistence ────────────────────────────────────────────────────

    // Persisted func-key map is "<scancode>" → "<typeInt>:<id>"; parse back to a
    // scancode-indexed FuncKeyMapped array, skipping any entry that won't resolve.
    private static FuncKeyMapped[] ParseMap(Dictionary<string, string> raw)
    {
        var map = new FuncKeyMapped[KeyConfig.MapSize];
        foreach (var (ks, vs) in raw)
        {
            var colon = vs.IndexOf(':');
            if (colon <= 0) continue;
            if (int.TryParse(ks, out var sc) && sc >= 0 && sc < KeyConfig.MapSize &&
                int.TryParse(vs.AsSpan(0, colon), out var typeInt) &&
                int.TryParse(vs.AsSpan(colon + 1), out var id) &&
                Enum.IsDefined(typeof(FuncKeyType), (byte)typeInt))
            {
                map[sc] = new FuncKeyMapped((FuncKeyType)typeInt, id);
            }
        }
        return map;
    }

    // Send the changed slots to the server (FuncKeyMappedModified / KeyModified).
    private void SendFuncKeyMapModified(IReadOnlyList<(int index, FuncKeyMapped fk)> changed)
    {
        if (!Game.Session.IsConnected || changed.Count == 0) return;
        var p = OutPacket.Of(InHeader.FuncKeyMappedModified);
        p.WriteInt(0);                 // FuncKeyMappedType.KeyModified
        p.WriteInt(changed.Count);     // size
        foreach (var (index, fk) in changed)
        {
            p.WriteInt(index);
            p.WriteByte((byte)fk.Type);
            p.WriteInt(fk.Id);
        }
        Game.Session.Send(p);
    }

    private void SaveSettings()
    {
        var s = Game.Settings.Load();
        if (_keyConfig != null)
        {
            var map = _keyConfig.ExportMap();
            var dict = new Dictionary<string, string>();
            for (var sc = 0; sc < map.Length; sc++)
                if (map[sc].IsBound)
                    dict[sc.ToString()] = $"{(int)map[sc].Type}:{map[sc].Id}";
            s.FuncKeyMap = dict;
        }
        if (_optionMenu != null)
        {
            s.BgmVolume = _optionMenu.BgmVolume;
            s.SfxVolume = _optionMenu.SfxVolume;
            s.ResW = _optionMenu.ResW;
            s.ResH = _optionMenu.ResH;
        }
        Game.Settings.Save(s);
    }

    private void ApplyAudioVolumes()
    {
        if (_optionMenu is null) return;
        Game.AudioPlayer.Volume    = _optionMenu.BgmVolume / 100f;
        Game.AudioPlayer.SfxVolume = _optionMenu.SfxVolume / 100f;
    }

    // Resolve the map's display name from String.wz ("street : name") for the
    // mini-map header. Falls back to empty (the mini-map then shows just bounds).
    private void UpdateMapName(int mapId)
    {
        var combined = Game.Names.MapName(mapId);
        if (string.IsNullOrEmpty(combined))
        {
            _mapStreet = string.Empty;
            _mapNameText = $"Map {mapId}";
            return;
        }
        var parts = combined.Split(" : ", 2, StringSplitOptions.None);
        _mapStreet   = parts.Length == 2 ? parts[0] : string.Empty;
        _mapNameText = parts.Length == 2 ? parts[1] : combined;
    }

    // Resolve a map's "info/bgm" value (e.g. "Bgm00/SleepyWood") to a Sound.wz
    // node ("Bgm00.img/SleepyWood") and loop it. No-op if the BGM is unchanged
    // (so a same-map SetField doesn't restart the track).
    private void PlayMapBgm(string bgm)
    {
        if (string.IsNullOrEmpty(bgm) || bgm == _currentBgm) return;
        var slash = bgm.IndexOf('/');
        if (slash <= 0) return;
        var dir  = bgm[..slash];
        var name = bgm[(slash + 1)..];
        if (Game.SoundWz?.GetItem($"{dir}.img/{name}") is WzSound snd && Game.AudioPlayer.PlayLoop(snd))
        {
            _currentBgm = bgm;
        }
    }

    // Guild rank (1=Master, 2=Jr.Master, 3-5=Member) → label.
    private static string GuildRankName(int rank) => rank switch
    {
        1 => "Master",
        2 => "Jr. Master",
        >= 3 and <= 5 => "Member",
        _ => string.Empty,
    };

    // ShopResultType (kinoko) → a player-facing line for the status messenger.
    private static string ShopResultText(ShopResultArgs a) => a.ResultType switch
    {
        0  => "Purchase complete.",
        1  => "That item is out of stock.",
        2  => "You don't have enough mesos.",
        4  => "Item sold.",
        8  => "Recharge complete.",
        10 => "You don't have enough mesos.",
        16 => "You can't buy any more of this item.",
        17 => "You can't trade this item.",
        19 => a.Message ?? string.Empty,
        _  => "The transaction could not be completed.",
    };

    // TrunkResultType (kinoko) failure subtypes → a player-facing line.
    private static string TrunkResultText(byte resultType) => resultType switch
    {
        10 => "You can't take that item out.",      // GetUnknown
        11 => "You don't have enough mesos.",        // GetNoMoney
        12 => "You can only take out mesos.",        // GetHavingOnlyItem
        14 => "That request can't be completed.",    // PutIncorrectRequest
        16 => "You don't have enough mesos.",        // PutNoMoney
        17 => "Your storage is full.",               // PutNoSpace
        18 => "You can't store that item.",          // PutUnknown
        23 => "Trading is blocked.",                 // TradeBlocked
        _  => "The storage request could not be completed.",
    };

    // DropPickUp warning subtypes (kinoko MessagePacket.DropPickUpMessageType):
    //   -3 CANNOT_ACQUIRE_ANY_ITEMS, -2 UNAVAILABLE_FOR_PICK_UP, -1 CANNOT_GET_ANYMORE_ITEMS.
    // -1/-3 have clean StringPool matches; -2 has no clean pool id (kept inline).
    private string LootWarningText(sbyte warning) => warning switch
    {
        -1 => Game.StringPool.Get(StringId.DropCannotGetAnymoreItems),
        -2 => "This item cannot be picked up.",
        -3 => Game.StringPool.Get(StringId.DropCannotAcquireItems),
        _  => "Unable to pick up.",
    };

    private ItemInventory.InvItem ToInvItem(InventoryItem it, int tab, int slot) => new()
    {
        Id = it.ItemId,
        Name = ItemDisplayName(it),
        Quantity = it.Type == InvItemType.Bundle ? it.Quantity : 1,
        Tab = tab,
        Slot = slot,
        Grade = it.Equip?.Grade ?? 0,   // potential rank → Quality dot
    };

    /// <summary>Load the started + completed quests delivered in SetField's CharacterData into the
    /// quest log and the client-side state map (used for NPC markers + Available-tab availability).</summary>
    private void PopulateQuests(SetFieldArgs args)
    {
        if (_quest is null || args.Quests is null) return;
        _questStates.Clear();
        foreach (var q in args.Quests)
            _questStates[q.QuestId] = (q.State, q.Value ?? string.Empty);
        _quest.SetQuests(args.Quests.Select(q =>
        {
            var qd = Game.Quests.Get(q.QuestId);
            return new QuestLog.QuestEntry
            {
                Id = q.QuestId,
                Name = qd?.Name is { Length: > 0 } n ? n : (Game.Names.QuestName(q.QuestId) ?? $"Quest {q.QuestId}"),
                Progress = q.Value,
                Parent = qd?.Parent ?? string.Empty,
                LvMin = qd?.Start.LvMin ?? 0,
                LvMax = qd?.Start.LvMax ?? 0,
                Complete = q.State == 2,
            };
        }));
        RefreshQuestAvailability();
    }

    /// <summary>Recompute, from Quest.wz + current quest state, which quests the player can start
    /// (the Quest-log "Available" tab) and which marker floats over each NPC (<see cref="_npcMarkers"/>).
    /// Mirrors the upstream Kinoko <c>QuestInfo.canStartQuest</c>/<c>canCompleteQuest</c> gates; the
    /// server re-validates on accept, so the client errs toward showing rather than hiding.</summary>
    private void RefreshQuestAvailability()
    {
        if (_quest is null) return;
        var level = _charStats.Level;
        var jobId = _charStats.JobId;
        var available = new List<QuestLog.QuestEntry>();
        _npcMarkers.Clear();

        foreach (var (id, q) in Game.Quests.All())
        {
            if (!CanStartQuest(q, level, jobId)) continue;
            available.Add(new QuestLog.QuestEntry
            {
                Id = id,
                Name = string.IsNullOrEmpty(q.Name) ? (Game.Names.QuestName(id) ?? $"Quest {id}") : q.Name,
                Progress = q.Blurb[0],
                Parent = q.Parent,
                LvMin = q.Start.LvMin,
                LvMax = q.Start.LvMax,
                Available = true,
            });
            if (q.Start.Npc != 0) _npcMarkers[q.Start.Npc] = QuestMarkerKind.New;
        }

        // In-progress quests put a "talk to me" ("?") marker on their complete NPC. Don't let it
        // override a "new quest" lightbulb already claimed for that NPC.
        foreach (var (qid, st) in _questStates)
        {
            if (st.State != 1) continue;
            var q = Game.Quests.Get(qid);
            if (q is null || q.Complete.Npc == 0) continue;
            if (!_npcMarkers.ContainsKey(q.Complete.Npc))
                _npcMarkers[q.Complete.Npc] = QuestMarkerKind.InProgress;
        }

        available.Sort((a, b) => a.Id.CompareTo(b.Id));
        _quest.SetAvailable(available);
    }

    private bool CanStartQuest(QuestData q, int level, int jobId)
    {
        if (q.Start.Npc == 0) return false;          // no start NPC ⇒ not a clickable/available quest
        if (_questStates.ContainsKey(q.Id)) return false;   // already started or completed
        return MeetsCheck(q.Start, level, jobId);
    }

    /// <summary>FIND NPC button — pan the camera to the start (or complete) NPC if it spawned on
    /// the current field. Otherwise flash a system-chat message so the user knows nothing happened.</summary>
    private void FocusCameraOnNpc(int templateId)
    {
        if (templateId == 0) return;
        var npc = _npcs.FirstOrDefault(n => n.NpcId == templateId);
        if (npc is null)
        {
            _chatBar?.AddLine("This NPC is not on the current map.", new Color(220, 200, 120));
            return;
        }
        _camera.Target = npc.Position;
    }

    private bool CanCompleteQuest(QuestData q, int level, int jobId)
    {
        if (!_questStates.TryGetValue(q.Id, out var st) || st.State != 1) return false;
        return MeetsCheck(q.Complete, level, jobId) && MobProgressMet(q.Complete, st.Value);
    }

    private bool MeetsCheck(QuestReq r, int level, int jobId)
    {
        // BtAllLevel on the QuestLog disables the level filter so the player can browse out-of-range
        // quests; the level fields still display on the detail panel.
        var enforceLevel = !(_quest?.ShowAllLevels ?? false);
        if (enforceLevel)
        {
            if (r.LvMin > 0 && level < r.LvMin) return false;
            if (r.LvMax > 0 && level > r.LvMax) return false;
        }
        if (!JobAllowed(r.Jobs, jobId)) return false;
        foreach (var (pqId, pqState) in r.Quests)
        {
            var have = _questStates.TryGetValue(pqId, out var s) ? s.State : (byte)0;
            if (have != pqState) return false;
        }
        // Date window / day-of-week (QuestDateCheck / QuestDayOfWeekCheck).
        var now = DateTime.UtcNow;
        if (r.StartDate is { } sd && now < sd) return false;
        if (r.EndDate   is { } ed && now > ed) return false;
        if (r.DayOfWeekMask != 0 && (r.DayOfWeekMask & (1 << (int)now.DayOfWeek)) == 0) return false;
        return true;
    }

    // Lenient job gate (the server enforces the exact rule on accept): allow when the quest lists no
    // job, lists "beginner" (0 — used by the many any-job quests), or shares the player's job branch.
    private static bool JobAllowed(List<int> jobs, int jobId)
    {
        if (jobs.Count == 0) return true;
        foreach (var j in jobs)
            if (j == 0 || j == jobId || j / 100 == jobId / 100) return true;
        return false;
    }

    // The quest-record value packs each complete-mob count as %03d (see Kinoko QuestInfo.progressQuest).
    private static bool MobProgressMet(QuestReq complete, string value)
    {
        for (var i = 0; i < complete.Mobs.Count; i++)
        {
            var have = 0;
            var off = i * 3;
            if (value.Length >= off + 3 && int.TryParse(value.Substring(off, 3), out var n)) have = n;
            if (have < complete.Mobs[i].Count) return false;
        }
        return true;
    }

    private void PopulateInventory(SetFieldArgs args)
    {
        if (args.Skills is { Count: > 0 })
        {
            ApplySkills(args.Skills);
        }
        if (args.Inventory is null)
        {
            return;
        }
        _item?.ClearAll();
        if (_item != null) _item.Meso = args.Money;
        _equip?.ClearEquipped();
        foreach (var (invType, items) in args.Inventory)
        {
            var tab = InvTypeToTab(invType);
            foreach (var (pos, item) in items)
            {
                if (invType == InventoryType.Equipped)
                {
                    _equip?.SetEquipped(pos, item.ItemId, ItemDisplayName(item));
                }
                else if (tab >= 0)
                {
                    _item?.SetSlot(tab, pos, ToInvItem(item, tab, pos));
                }
            }
        }
    }

    private void SendUserMove(byte[] movePathBlob)
    {
        // UserMove(44): int 0; int 0; byte fieldKey; int 0; int 0; int crc; int crc32; <MovePath blob>.
        var p = OutPacket.Of(InHeader.UserMove);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteByte(_fieldKey);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteInt(_field?.Crc ?? 0);   // dwCrc — must match field.getFieldCrc() or the server logs a CRC mismatch
        p.WriteInt(0);                  // 0
        p.WriteInt(0);                  // Crc32 (server reads it but does not validate)
        p.WriteBytes(movePathBlob);
        Game.Session.Send(p);
    }

    public override void Update(GameTime gameTime)
    {
        Game.Session.DrainQueue();   // dispatch all queued server packets on game thread
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        AdvanceFieldTransition(dt);   // map-change fade-to-black / swap-at-black / fade-in

        // Cursor sprite priority:
        //   ItemGrabbed (closed hand, variant 12) — something is on the cursor (mid-ghost-drag).
        //   GrabReady   (open  hand, variant 5)  — hovering a grabbable inventory item, no click yet.
        // Both ride on top of the regular Hover anim so the inventory grab indicator wins.
        if (Game.Cursor != null)
        {
            Game.Cursor.ItemGrabbed = _item?.IsDragging == true || _equip?.IsDragging == true
                || _skill?.IsDraggingSkill == true;
            Game.Cursor.GrabReady = !Game.Cursor.ItemGrabbed
                && ((_item?.HoverOverItem ?? false) || (_equip?.HoverOverItem ?? false));
        }

        // Read held keys each frame (movement is frame-continuous).
        // Movement is driven by the live KeyConfig bindings so user rebinds
        // (rebound MoveLeft/MoveRight/Jump in the in-game F12 dialog) take
        // effect without needing a restart.
        var kb = Keyboard.GetState();
        _moveLeft = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.MoveLeft);
        _moveRight = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.MoveRight);
        var imeJump = Game.ConsumeImeJump();   // the 한/영 / Right-Alt key acts as jump in gameplay
        _jumpPressed = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.Jump) || imeJump;
        _downPressed = kb.IsKeyDown(Keys.Down);   // prone when grounded + idle
        _upPressed = kb.IsKeyDown(Keys.Up);       // grab + climb ladders / ropes
        // Typing in chat → the keyboard drives text, not the avatar. Also suppress all input when the
        // window isn't focused: the right-modifier check uses Win32 GetAsyncKeyState (focus-agnostic),
        // so without this an alt-tabbed R-Ctrl/R-Alt would still move/attack.
        if (TextField.Active != null || !Game.IsActive)
        {
            _moveLeft = _moveRight = _jumpPressed = _downPressed = _upPressed = false;
        }

        // Foothold physics — only when the channel server has sent SetField and
        // a map is loaded. Drives both the CharLook visual position and the
        // outbound UserMove(44) packets.
        if (_attackCooldown > 0f)
        {
            _attackCooldown -= dt;
        }
        if (_emotionCooldown > 0f)
        {
            _emotionCooldown -= dt;
        }
        // Held-key auto-repeat: holding the attack key re-swings once the per-attack delay has elapsed.
        // Game.IsActive gates the GetAsyncKeyState right-modifier check (R-Ctrl attack) to the focused window.
        if (_physics != null && _attackCooldown <= 0f && TextField.Active == null && Game.IsActive
            && _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.Attack))
        {
            DoMeleeAttack();
        }

        if (_physics != null)
        {
            // Root horizontal input while a melee swing plays: grounded, this stops the avatar from
            // sliding; airborne, it stops you steering mid-jump (you can't change direction while
            // attacking). The existing jump momentum is preserved (no input ≠ deceleration in the air),
            // so a jump-attack still arcs along the trajectory it was launched on; climbing is unaffected.
            var rooted = _attackCooldown > 0f;
            var input = new PlayerInput
            {
                Left = _moveLeft && !rooted,
                Right = _moveRight && !rooted,
                Up = _upPressed,
                Down = _downPressed,
                JumpPressed = _jumpPressed,
            };
            _physics.Update(input, dt);
            _charRenderer?.Update(dt);   // advance the face-blink clock
            if (_player != null)
            {
                _player.Position = _physics.Position;
                _player.UpdateFromPhysics(dt, _physics.Stance, _physics.FacingLeft, _physics.ClimbMoving);
            }
            _camera.Target = _physics.Position;

            // Suppress UserMove while a field transition is pending: the server has already
            // moved us to the new field, so sending the old field's CRC would be rejected.
            if (_pendingField is null && _physics.TryFlushMovePath(out var blob))
            {
                SendUserMove(blob);
            }
        }
        else if (_player != null)
        {
            // Pre-SetField: archlo's CharLook handles its own demo physics.
            _player.Update(dt, _moveLeft, _moveRight, _jumpPressed);
            _camera.Target = _player.Position;
        }
        // Keep the camera's view size in step with the live backbuffer so the
        // world→screen transform stays correct after a resolution change (map
        // entry, or returning from the cash shop).
        var camPp = GraphicsDevice.PresentationParameters;
        _camera.ViewWidth = camPp.BackBufferWidth;
        _camera.ViewHeight = camPp.BackBufferHeight;
        _camera.Update(dt);

        // NPCs — advance animation + ambient-speak timer; float any due chatter line.
        foreach (var npc in _npcs)
        {
            npc.Update(dt);
            if (npc.TakePendingSpeak() is { } line) _npcBalloons?.Set(npc.ObjId, line);
        }
        _npcBalloons?.Update(dt);

        // Advance the shared quest-marker animation clock (~8 fps).
        _markerTimer += dt;
        if (_markerTimer >= 0.12f) { _markerTimer = 0f; _markerFrame++; }

        // Clickable-cursor feedback when the pointer is over an NPC (matches LoginStage's hover).
        var ms = Mouse.GetState();
        var overNpc = false;
        if (_npcTalk?.IsVisible != true)
        {
            foreach (var npc in _npcs)
            {
                if (npc.GetScreenBounds(_camera.WorldToScreen(npc.Position)).Contains(ms.X, ms.Y))
                {
                    overNpc = true;
                    break;
                }
            }
        }
        Game.Cursor?.SetHover(overNpc);

        // Mobs
        var deadMobs = new List<int>();
        foreach (var (id, mob) in _mobs)
        {
            mob.Update(dt);
            if (mob.IsDead) deadMobs.Add(id);
        }
        foreach (var id in deadMobs) { _mobs.Remove(id); _mobCtl.Remove(id); _pendingCtrl.Remove(id); }

        // Controlled-mob AI + MobMove(227). The Kinoko server runs no mob AI of its own;
        // the controller (= the nearest player, i.e. us) is the sole authority on each
        // mob's movement and aggro. We drain _pendingCtrl first so MobChangeController
        // packets that arrived during the fade-out window get their controllers built
        // now that _field is loaded. See MobController for the per-moveAbility AI.
        if (_field is not null && _physics is not null && Game.Session.IsConnected)
        {
            if (_pendingCtrl.Count > 0)
            {
                var pending = _pendingCtrl.ToArray();
                _pendingCtrl.Clear();
                foreach (var id in pending) TryCreateMobController(id);
            }
            var playerPos = _physics.Position;
            if (_userHitCooldown > 0) _userHitCooldown -= dt;
            foreach (var (mobId, ctl) in _mobCtl)
            {
                if (!ctl.ShouldTick) continue;   // STAY mobs (moveAbility 0) never emit
                ctl.Update(dt, playerPos);
                if (ctl.TryFlush(out var blob, out var sn))
                {
                    Game.Session.Send(GameSender.MobMove(
                        mobId, sn, (byte)ctl.CurrentAction, ctl.FacingLeft,
                        blob, chasing: ctl.IsChasing));
                }
                // Touch-damage ("mob attacks back"): when an aggressive mob with bodyAttack
                // overlaps the player AABB and our i-frame is open, send UserHit(52). The
                // server validates against the mob's MobAttack(0) template + broadcasts; we
                // apply HP locally for instant feedback (StatChanged corrects it shortly).
                // Full mob-skill attacks (MobSkill -> Disease) are deferred.
                // Touch-damage with real knockback. Gates:
                //   - global player i-frame (_userHitCooldown, 1 s for GMS feel)
                //   - per-mob hit cooldown (ctl.CanHitPlayer, 2 s per IDB)
                //   - mob must be aggressive AND have info/bodyAttack
                //   - AABB overlap (35 px × 60 px around player)
                // On hit we push the player AWAY from the mob via PlayerController.ApplyKnockback
                // (raw velocity per IDB SetImpactNext — no fixed-strength constant; just (vx,vy)),
                // send UserHit with knockback=2 so the server broadcasts the knockback to other
                // players, apply HP locally for instant feedback (server's StatChanged corrects
                // shortly), and start both cooldowns. Full mob-skill attacks (MobSkill → Disease)
                // remain deferred.
                if (_userHitCooldown <= 0f && ctl.CanHitPlayer && ctl.IsAggressive
                    && ctl.Info.BodyAttack && _mobs.TryGetValue(mobId, out var mobLook))
                {
                    var mp = mobLook.Position;
                    if (Math.Abs(mp.X - playerPos.X) < 35f && Math.Abs(mp.Y - playerPos.Y) < 60f)
                    {
                        var dmg     = Math.Max(1, ctl.Info.Level * 3);
                        var pushDir = playerPos.X >= mp.X ? +1f : -1f;
                        // Tuned to a small "snail-touch" hop; bigger mobs can scale vx by level later.
                        _physics.ApplyKnockback(vx: pushDir * 130f, vy: -260f);
                        var dir     = (byte)(mp.X < playerPos.X ? 0 : 1);
                        Game.Session.Send(GameSender.UserHit(
                            attackIndex: 0, magicElemAttr: 0,
                            damage: dmg, templateId: mobLook.TemplateId, mobId: mobId,
                            dir: dir, knockback: 2));
                        if (_charStats is not null)
                            _charStats.Hp = Math.Max(0, _charStats.Hp - dmg);
                        _userHitCooldown = 1.0f;      // player-global i-frame (GMS ≈ 1 s)
                        ctl.NotePlayerHit();          // per-mob cooldown (IDB: 2 s)
                    }
                }
            }
        }

        // Map animation + background autoscroll
        _field?.Update(dt * 1000.0);

        // Drops
        foreach (var drop in _drops.Values) drop.Update(dt);
        // Reap drops whose pick-up absorb animation finished.
        List<int>? doneDrops = null;
        foreach (var (id, d) in _drops) if (d.Finished) (doneDrops ??= new()).Add(id);
        if (doneDrops != null) foreach (var id in doneDrops) _drops.Remove(id);

        // Other players
        foreach (var other in _otherChars.Values) other.Update(dt);
        _balloons?.Update(dt);

        // Damage numbers
        _dmgNumbers?.Update(dt);

        // Over-head emotion bubbles
        _emotionBubble?.Update(dt);

        // Sync stats to panels
        if (_statusBar != null)
        {
            _statusBar.Hp      = _stats?.Hp      ?? 50;
            _statusBar.MaxHp   = _stats?.MaxHp   ?? 50;
            _statusBar.Mp      = _stats?.Mp       ?? 30;
            _statusBar.MaxMp   = _stats?.MaxMp    ?? 30;
            _statusBar.Level   = _stats?.Level    ?? 1;
            _statusBar.CharName = _myName;   // authoritative real name (set by SetField/StatChanged)
        }

        // Feed live entity positions to the minimap (icons are plotted via the map's
        // miniMap centerX/centerY/mag transform — see MiniMap.DrawMarker).
        if (_miniMap != null)
        {
            if (_player != null) _miniMap.PlayerWorldPos = _player.Position;
            _miniMap.SetNpcs(_npcs.Select(n => n.Position));
            _miniMap.SetOtherPlayers(_otherChars.Values.Select(o => o.Position));
            _miniMap.SetPortals(_field is { } f
                ? f.Portals.Values.Where(p => p.Type == 2).Select(p => p.Position)
                : Enumerable.Empty<Vector2>());
        }

        // Keep the detail window pinned to the right of the main Stat window
        // (the original opens it at main top-left + (172, 90)).
        if (_statDetail is { IsVisible: true } && _stats != null)
            _statDetail.Position = _stats.Position + new Vector2(172, 90);

        // QuestDetail rides off the right edge of the QuestLog; hide if the list closes.
        if (_questDetail != null && _quest != null)
        {
            _questDetail.AnchorTopLeft = _quest.Position + new Vector2(_quest.OuterWidth, 0);
            if (!_quest.IsVisible) _questDetail.IsVisible = false;
        }

        // Panels
        foreach (var p in _panels) p.Update(gameTime);

        // Swap in the vertical-resize cursor while the mouse is over the chat log's drag grip.
        // Use the animated variant 7 on hover, then the static variant 9 once a drag actually
        // begins — matches the v95 client's cursor swap.
        if (Game.Cursor != null)
        {
            Game.Cursor.Resize         = _chatBar?.IsOverResizeGrip == true;
            Game.Cursor.ResizeDragging = _chatBar?.IsDragGripActive == true;
        }

        // Advance + retire one-shot skill effect animations.
        var fxDt = gameTime.ElapsedGameTime.TotalMilliseconds;
        for (var i = _skillEffects.Count - 1; i >= 0; i--)
        {
            _skillEffects[i].Anim.Update(fxDt);
            _skillEffects[i].Elapsed += fxDt;
            if (_skillEffects[i].Elapsed >= _skillEffects[i].Anim.TotalDurationMs) _skillEffects.RemoveAt(i);
        }
        _quitConfirm?.Update(gameTime);
        _gameMenu?.Update(gameTime);
    }

    public override void Draw(GameTime gameTime, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;

        // Field backdrop — use the real Map.wz scene if SetField has been
        // processed; otherwise fall back to archlo's tinted green ground plane.
        if (_field != null)
        {
            _field.Camera.Position = _camera.Position;
            _field.Draw(sb, Game.WhitePixel, w, h);
        }
        else
        {
            sb.Draw(Game.WhitePixel, new Rectangle(0, 0, w, h), new Color(34, 85, 34));
            DrawGroundPlane(sb, w, h);
        }

        // NPCs (world-space → screen) + any quest marker floating above the head.
        foreach (var npc in _npcs)
        {
            var sp = _camera.WorldToScreen(npc.Position);
            if (sp.X <= -100 || sp.X >= w + 100) continue;
            npc.Draw(sb, Game.WhitePixel, sp);
            var marker = MarkerFramesFor(npc.NpcId);
            if (marker.Length > 0)
                marker[_markerFrame % marker.Length]
                    .Draw(sb, new Vector2(sp.X, sp.Y - npc.HeadOffset - 18));   // QuestIcon origin is centred
        }

        // Drops
        foreach (var drop in _drops.Values)
        {
            var ds = _camera.WorldToScreen(drop.Position);
            if (ds.X > -50 && ds.X < w + 50 && ds.Y > -50 && ds.Y < h + 50)
                drop.Draw(sb, Game.WhitePixel, ds);
        }

        // Mobs
        foreach (var mob in _mobs.Values)
        {
            var ms = _camera.WorldToScreen(mob.Position);
            if (ms.X > -80 && ms.X < w + 80)
            {
                mob.Draw(sb, Game.WhitePixel, ms);
                // "{Name} (Lv. N)" tag below the mob for ~5 s after each hit — driven by
                // MobLook's HitTagVisible timer (pulsed by OnHit + SetHpPercent).
                if (mob.HitTagVisible && Game.Font != null && mob.NameTagText.Length > 0)
                {
                    var tag = mob.NameTagText;
                    var sz  = Game.Font.Measure(tag);
                    var tx  = ms.X - sz.X / 2f;
                    var ty  = ms.Y + 6f;
                    Game.Font.Draw(sb, tag, new Vector2(tx + 1f, ty + 1f), new Color(0, 0, 0, 200));
                    Game.Font.Draw(sb, tag, new Vector2(tx,      ty),      Color.White);
                }
            }
        }

        // Other players
        foreach (var other in _otherChars.Values)
        {
            var os = _camera.WorldToScreen(other.Position);
            if (os.X > -80 && os.X < w + 80)
                other.Draw(sb, Game.WhitePixel, os);
        }

        // Player character
        if (_player != null)
        {
            var playerScreen = _camera.WorldToScreen(_player.Position);
            _player.Draw(sb, Game.WhitePixel, playerScreen);

            // Local-player name tag — drawn under the avatar's foot point
            // (authentic v95 placement). Style matches OtherCharLook.
            if (Game.Font is { } nameFont && !string.IsNullOrEmpty(_myName))
            {
                var tag = $"[{_charStats.Level}] {_myName}";
                var sz  = nameFont.Measure(tag);
                var tx  = playerScreen.X - sz.X / 2f;
                var ty  = playerScreen.Y + 4f;
                var bg  = new Rectangle((int)tx - 2, (int)ty - 1,
                                        (int)sz.X + 4, nameFont.LineHeight + 2);
                sb.Draw(Game.WhitePixel, bg, new Color(0, 0, 0, 160));
                nameFont.Draw(sb, tag, new Vector2(tx, ty), new Color(255, 230, 100));
            }

            foreach (var fx in _skillEffects)
                if (!fx.Screen)
                    fx.Anim.Draw(sb, playerScreen, fx.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
        }

        // Chat balloons above the speakers (arrow tip just above each head).
        _balloons?.Draw(sb, id =>
        {
            if (_otherChars.TryGetValue(id, out var oc))
                return _camera.WorldToScreen(oc.Position) - new Vector2(0, 64);
            if (_player != null)
                return _camera.WorldToScreen(_player.Position) - new Vector2(0, 74);
            return (Vector2?)null;
        });

        // Ambient NPC speech balloons (arrow tip just above each NPC's head).
        _npcBalloons?.Draw(sb, id =>
        {
            var npc = _npcs.FirstOrDefault(n => n.ObjId == id);
            if (npc is null) return null;
            return _camera.WorldToScreen(npc.Position) - new Vector2(0, npc.HeadOffset + 4);
        });

        // Over-head emotion bubbles (z=3 in the v95 client — above the avatar layer).
        _emotionBubble?.Draw(sb, _camera.WorldToScreen);

        // Damage numbers (drawn on top of everything)
        _dmgNumbers?.Draw(sb, Game.WhitePixel, _camera.WorldToScreen);

        // UI panels (screen-space)
        foreach (var p in _panels)
            p.Draw(sb, Game.WhitePixel);

        // System menu draws above the panels; the quit confirm sits above the menu.
        _gameMenu?.Draw(sb, Game.WhitePixel);
        _quitConfirm?.Draw(sb, Game.WhitePixel);

        // Fullscreen skill effects (e.g. screen flash), centered, above the world + panels.
        foreach (var fx in _skillEffects)
            if (fx.Screen)
                fx.Anim.Draw(sb, new Vector2(w / 2f, h / 2f));

        // Skill drag ghost (above all panels): a skill picked up to bind onto a key/quickslot.
        if (_skill?.IsDraggingSkill == true && _skill.DragIcon is { } skGhost)
        {
            var gm = Mouse.GetState();
            skGhost.Draw(sb, new Vector2(gm.X, gm.Y) + skGhost.Origin - new Vector2(16, 16));
        }

        // Map-change transition: full-screen black fade, above the world + all panels.
        if (_fadeAlpha > 0f)
            sb.Draw(Game.WhitePixel, new Rectangle(0, 0, w, h),
                new Color(0, 0, 0, (int)(_fadeAlpha * 255f)));
    }

    private void DrawGroundPlane(SpriteBatch sb, int w, int h)
    {
        // A simple ground line so the player has a visual reference
        var groundScreen = _camera.WorldToScreen(Vector2.Zero);
        var groundY = (int)groundScreen.Y;
        sb.Draw(Game.WhitePixel, new Rectangle(0, groundY, w, 4), new Color(80, 60, 40));
        // Dirt fill below
        if (groundY < h)
            sb.Draw(Game.WhitePixel, new Rectangle(0, groundY + 4, w, h - groundY), new Color(100, 70, 40));
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.HandleMouseButton(x, y, down); return; }
        if (_gameMenu?.IsVisible == true) { _gameMenu.HandleMouseButton(x, y, down); return; }
        // A held item dropped outside its own window → drop it to the ground (drops inside the window —
        // slot moves, equips, cancels — are resolved by the window's own HandleMouseButton below).
        if (down && _item?.IsDragging == true && !_item.ContainsPoint(x, y))
        {
            _item.DropHeldToGround();
            return;
        }
        // While a worn equip is held on the cursor, the next click resolves the drop: onto the item
        // inventory (or a fast click back on its slot) → unequip; outside both → drop to ground; else put back.
        if (down && _equip?.IsDragging == true)
        {
            _equip.ResolveDrop(x, y, _item?.ContainsPoint(x, y) == true);
            return;
        }
        // A skill held on the cursor (picked up from the Skill window): a click binds it to
        // the key/quickslot under the cursor. Otherwise it falls through to the Skill window,
        // which casts (quick re-click on the same row) or cancels.
        if (down && _skill?.IsDraggingSkill == true)
        {
            if (_keyConfig?.IsVisible == true && _keyConfig.TryBindSkillAt(_skill.DragSkillId, x, y))
            { _skill.CancelSkillDrag(); return; }
            if (_quickSlots?.TryBindSkillAt(_skill.DragSkillId, x, y) == true)
            { _skill.CancelSkillDrag(); return; }
        }
        for (var i = _panels.Count - 1; i >= 0; i--)
        {
            var p = _panels[i];
            if (p.IsVisible && p.HandleMouseButton(x, y, down)) return;
        }
        // Double-click a player (yourself or another) → open the character profile. Your own profile
        // uses local data; another player round-trips through the server (UserCharacterInfoRequest →
        // CharacterInfo). Consume the click so it can't fall through to an NPC behind the avatar.
        if (down)
        {
            var hitId = HitTestPlayer(x, y);
            if (hitId != int.MinValue)
            {
                var now = Environment.TickCount64 / 1000.0;
                if (hitId == _lastClickCharId && now - _lastClickTime < 0.4)
                {
                    if (hitId == Game.CharacterId) ShowOwnProfile();
                    else SendIfConnected(GameSender.UserCharacterInfoRequest(hitId));
                    _lastClickCharId = -1;
                }
                else { _lastClickCharId = hitId; _lastClickTime = now; }
                return;
            }
        }
        // NPC click — only when no dialog is already open. The floating quest marker is clickable too;
        // it always opens the quest flow (even for NPCs that run a general script on a body click).
        if (down && _npcTalk?.IsVisible != true)
        {
            var qpos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
            foreach (var npc in _npcs)
            {
                var sp = _camera.WorldToScreen(npc.Position);
                if (MarkerFramesFor(npc.NpcId).Length > 0)
                {
                    var mr = new Rectangle((int)sp.X - 22, (int)(sp.Y - npc.HeadOffset - 18) - 22, 44, 44);
                    if (mr.Contains(x, y))
                    {
                        if (Game.Session.IsConnected) TryStartQuestInteraction(npc, qpos);
                        break;
                    }
                }
                if (npc.GetScreenBounds(sp).Contains(x, y))
                {
                    SelectNpc(npc);
                    break;
                }
            }
        }
    }

    /// <summary>Hit-test the field avatars at a screen point: returns the char id of the player under
    /// the cursor (another player, or yourself keyed by <c>Game.CharacterId</c>), or
    /// <see cref="int.MinValue"/> if none. Drives the double-click → character-profile gesture.</summary>
    private int HitTestPlayer(int x, int y)
    {
        foreach (var (id, oc) in _otherChars)
        {
            if (oc.GetScreenBounds(_camera.WorldToScreen(oc.Position)).Contains(x, y)) return id;
        }
        var me = _camera.WorldToScreen(_physics?.Position ?? _player?.Position ?? Vector2.Zero);
        var selfBox = new Rectangle((int)me.X - 18, (int)me.Y - 84, 36, 90);
        if (selfBox.Contains(x, y)) return Game.CharacterId;
        return int.MinValue;
    }

    /// <summary>Open your own profile in the UserInfo window (status bar / CharInfo key / double-click
    /// yourself). Re-applies your current snapshot + avatar every time, so a prior other-player view
    /// never leaves a stale name/avatar behind.</summary>
    private void ShowOwnProfile()
    {
        if (_charInfo == null) return;
        _charInfo.ShowProfile(_myName, _charStats.Level, JobName(_charStats.JobId), _charStats.Fame,
            _charStats.Guild, string.Empty, _charRenderer, _myLook, null);
    }

    private void ToggleOwnProfile()
    {
        if (_charInfo == null) return;
        if (_charInfo.IsVisible) _charInfo.IsVisible = false;
        else ShowOwnProfile();
    }

    /// <summary>Click / Interact-key on an NPC. A scripted NPC goes through UserSelectNpc (the server
    /// runs its info/script). A pure quest NPC has no script, so UserSelectNpc would do nothing —
    /// route it through the quest packet instead. Shared by click-to-talk and the Interact key.</summary>
    private void SelectNpc(NpcLook npc)
    {
        if (!Game.Session.IsConnected) return;
        var pos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
        if (!npc.HasScript && TryStartQuestInteraction(npc, pos)) return;
        Game.Session.Send(GameSender.UserSelectNpc(npc.ObjId, (short)pos.X, (short)pos.Y));
    }

    /// <summary>If the NPC has quests we can act on, open them (act directly for one, show a menu for
    /// several) and return true. Used by clicking a pure-quest NPC and by clicking its floating marker.
    /// The selected quest's q{id}s/q{id}e script then drives the dialog through the normal ScriptMessage
    /// path; the server re-validates startability/completability.</summary>
    private bool TryStartQuestInteraction(NpcLook npc, Vector2 pos)
    {
        var actions = QuestActionsAtNpc(npc.NpcId);
        if (actions.Count == 0) return false;
        if (actions.Count == 1) SendQuestScript(actions[0].Id, actions[0].Complete, npc, pos);
        else                    ShowQuestMenu(npc, actions, pos);
        return true;
    }

    // Quests this NPC can start (Complete=false) or complete now (Complete=true), for the player's state.
    private List<(int Id, bool Complete)> QuestActionsAtNpc(int npcTemplateId)
    {
        var actions = new List<(int, bool)>();
        var level = _charStats.Level;
        var job = _charStats.JobId;
        foreach (var (qid, isStart) in Game.Quests.ForNpc(npcTemplateId))
        {
            var q = Game.Quests.Get(qid);
            if (q is null) continue;
            if (isStart) { if (CanStartQuest(q, level, job)) actions.Add((qid, false)); }
            else         { if (CanCompleteQuest(q, level, job)) actions.Add((qid, true)); }
        }
        actions.Sort((a, b) => b.Item2.CompareTo(a.Item2));   // ready turn-ins first
        return actions;
    }

    // A client-local quest picker reusing the authentic CUtilDlgEx menu (inline #L# links). Picking an
    // entry sends the quest script request; the server's reply re-drives _npcTalk via ScriptMessage.
    private void ShowQuestMenu(NpcLook npc, List<(int Id, bool Complete)> actions, Vector2 pos)
    {
        if (_npcTalk is null) return;
        var lines = new List<string> { "#e#bSelect a quest:#k#n", "" };
        for (var i = 0; i < actions.Count; i++)
            lines.Add($"#L{i}#{(actions[i].Complete ? "(Complete) " : "(Start) ")}{QuestDisplayName(actions[i].Id)}#l");
        _npcTalk.ShowMenu(npc.NpcId, 0, string.Join("\r\n", lines));
        _npcTalk.OnMenuChoice = choice =>
        {
            if (choice >= 0 && choice < actions.Count)
                SendQuestScript(actions[choice].Id, actions[choice].Complete, npc, pos);
        };
        _npcTalk.OnClose = () => { };   // client-local menu — nothing to tell the server
    }

    private void SendQuestScript(int questId, bool complete, NpcLook npc, Vector2 pos)
    {
        if (!Game.Session.IsConnected) return;
        Game.Session.Send(complete
            ? GameSender.QuestCompleteScript((short)questId, npc.NpcId, (short)pos.X, (short)pos.Y)
            : GameSender.QuestStartScript((short)questId, npc.NpcId, (short)pos.X, (short)pos.Y));
    }

    private string QuestDisplayName(int id)
    {
        var q = Game.Quests.Get(id);
        return q is { Name.Length: > 0 } ? q.Name : (Game.Names.QuestName(id) ?? $"Quest {id}");
    }

    /// <summary>Load the animation frames of a UI.wz/UIWindow2.img/QuestIcon group (the NPC head markers).</summary>
    private WzSprite[] LoadQuestIcon(int group)
    {
        if (_loader is null || _ui?.GetItem($"UIWindow2.img/QuestIcon/{group}") is not WzProperty node)
            return [];
        var frames = new List<WzSprite>();
        for (var i = 0; node.Get(i.ToString()) is WzCanvas c; i++)
            if (_loader.Load(c) is { } sprite) frames.Add(sprite);
        return frames.ToArray();
    }

    private WzSprite[] MarkerFramesFor(int npcTemplateId) =>
        _npcMarkers.TryGetValue(npcTemplateId, out var kind)
            ? kind switch
            {
                QuestMarkerKind.New        => _questMarkNew,
                QuestMarkerKind.InProgress => _questMarkProgress,
                _ => [],
            }
            : [];

    private const float NpcInteractRange = 80f;

    /// <summary>Interact key: talk to the closest NPC within range.</summary>
    private void TalkToNearestNpc()
    {
        if (_npcTalk?.IsVisible == true || _npcs.Count == 0)
        {
            return;
        }
        var pos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
        NpcLook? nearest = null;
        var best = NpcInteractRange;
        foreach (var npc in _npcs)
        {
            var d = Vector2.Distance(npc.Position, pos);
            if (d < best)
            {
                best = d;
                nearest = npc;
            }
        }
        if (nearest is not null)
        {
            SelectNpc(nearest);
        }
    }

    private void SendIfConnected(OutPacket p)
    {
        if (Game.Session.IsConnected)
        {
            Game.Session.Send(p);
        }
    }

    // ── Social helpers ──────────────────────────────────────────────────────────

    /// <summary>Open/close the community (friends/party/guild) panel. Loads the
    /// friend list from the server the moment the panel is opened.</summary>
    private void ToggleCommunityPanel()
    {
        if (_userList is null)
        {
            return;
        }
        _userList.IsVisible = !_userList.IsVisible;
        if (_userList.IsVisible)
        {
            SendIfConnected(GameSender.FriendLoad());
        }
    }

    /// <summary>Parse a chat-bar submission for slash commands; anything else is
    /// ordinary map chat. The ChatBar already echoed the raw line locally, so
    /// command lines are removed from the log and replaced with their result.</summary>
    private void OnChatSubmit(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (TryConsumeCommand(line, "/w ", out var whisperRest) ||
            TryConsumeCommand(line, "/whisper ", out whisperRest))
        {
            var (target, message) = SplitFirstToken(whisperRest);
            if (target.Length > 0 && message.Length > 0)
            {
                SendIfConnected(GameSender.Whisper(target, message));
                _chatBar?.AddLine($"[whisper to {target}] {message}", new Color(200, 140, 200));
            }
            return;
        }

        if (TryConsumeCommand(line, "/p ", out var partyRest))
        {
            if (_partyMemberIds.Count == 0)
            {
                _chatBar?.AddLine("You are not in a party.", new Color(220, 120, 120));
                return;
            }
            SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Party, _partyMemberIds, partyRest));
            _chatBar?.AddLine($"[Party] {_myName} : {partyRest}", GroupColor((int)GameSender.ChatGroupType.Party), ChatBar.ChatLineType.Party);
            return;
        }

        if (TryConsumeCommand(line, "/b ", out var buddyRest) && buddyRest.Length > 0)
        {
            if (_buddyIds.Count == 0) { _chatBar?.AddLine("No buddies are online.", new Color(220, 120, 120)); return; }
            SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Friend, _buddyIds, buddyRest));
            _chatBar?.AddLine($"[Buddy] {_myName} : {buddyRest}", GroupColor((int)GameSender.ChatGroupType.Friend), ChatBar.ChatLineType.Buddy);
            return;
        }

        if (TryConsumeCommand(line, "/g ", out var guildRest) && guildRest.Length > 0)
        {
            if (_guildMemberIds.Count == 0) { _chatBar?.AddLine("You are not in a guild.", new Color(220, 120, 120)); return; }
            SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Guild, _guildMemberIds, guildRest));
            _chatBar?.AddLine($"[Guild] {_myName} : {guildRest}", GroupColor((int)GameSender.ChatGroupType.Guild), ChatBar.ChatLineType.Guild);
            return;
        }

        if (TryConsumeCommand(line, "/a ", out var allyRest) && allyRest.Length > 0)
        {
            // Alliance relays via the guild roster we know (full alliance-member decode is a follow-up).
            SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Alliance, _guildMemberIds, allyRest));
            _chatBar?.AddLine($"[Alliance] {_myName} : {allyRest}", GroupColor((int)GameSender.ChatGroupType.Alliance), ChatBar.ChatLineType.Alliance);
            return;
        }

        if (line.StartsWith("/family", StringComparison.OrdinalIgnoreCase))
        {
            if (_familyWindow != null) _familyWindow.IsVisible = !_familyWindow.IsVisible;
            return;
        }

        if (TryConsumeCommand(line, "/invite ", out var inviteName))
        {
            var (name, _) = SplitFirstToken(inviteName);
            if (name.Length > 0)
            {
                SendIfConnected(GameSender.PartyInvite(name));
            }
            return;
        }

        if (line.StartsWith("/accept", StringComparison.OrdinalIgnoreCase))
        {
            if (_hasPendingInvite)
            {
                SendIfConnected(GameSender.PartyJoin(_pendingInviterId));
                _hasPendingInvite = false;
            }
            else
            {
                _chatBar?.AddLine("No pending party invite.", new Color(220, 120, 120));
            }
            return;
        }

        if (line.StartsWith("/create", StringComparison.OrdinalIgnoreCase))
        {
            SendIfConnected(GameSender.PartyCreate());
            return;
        }

        if (line.StartsWith("/leave", StringComparison.OrdinalIgnoreCase))
        {
            SendIfConnected(GameSender.PartyLeave());
            return;
        }

        // ── Messenger commands ────────────────────────────────────────────────
        if (line.StartsWith("/messenger", StringComparison.OrdinalIgnoreCase))
        {
            SendIfConnected(GameSender.MessengerEnter(0));   // 0 = create a new room
            _messengerWin?.Open();
            return;
        }

        if (TryConsumeCommand(line, "/minvite ", out var mInviteName))
        {
            var (name, _) = SplitFirstToken(mInviteName);
            if (name.Length > 0) SendIfConnected(GameSender.MessengerInvite(name));
            return;
        }

        if (line.StartsWith("/maccept", StringComparison.OrdinalIgnoreCase))
        {
            if (_hasMessengerInvite)
            {
                SendIfConnected(GameSender.MessengerEnter(_pendingMessengerId));
                _messengerWin?.Open();
                _hasMessengerInvite = false;
            }
            else
            {
                _chatBar?.AddLine("No pending messenger invite.", new Color(220, 120, 120));
            }
            return;
        }

        if (line.StartsWith("/mleave", StringComparison.OrdinalIgnoreCase))
        {
            SendIfConnected(GameSender.MessengerLeave());
            _messengerWin?.Reset();
            if (_messengerWin != null) _messengerWin.IsVisible = false;
            return;
        }

        if (TryConsumeCommand(line, "/m ", out var mChat) && mChat.Length > 0)
        {
            SendIfConnected(GameSender.MessengerChat(mChat));
            _chatBar?.AddLine($"[Messenger] {_myName} : {mChat}", new Color(120, 220, 220));
            return;
        }

        if (line.StartsWith("/help", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("/commands", StringComparison.OrdinalIgnoreCase))
        {
            var hdr = new Color(255, 220, 120);
            var c   = new Color(180, 200, 230);
            _chatBar?.AddLine("Available commands:", hdr);
            _chatBar?.AddLine("/w (/whisper) <name> <msg> — whisper a player", c);
            _chatBar?.AddLine("/p party · /b buddy · /g guild · /a alliance — group chat", c);
            _chatBar?.AddLine("/invite <name>, /accept, /create, /leave — party", c);
            _chatBar?.AddLine("/messenger, /minvite <name>, /maccept, /mleave, /m <msg> — messenger", c);
            _chatBar?.AddLine("/family — open the family window", c);
            _chatBar?.AddLine("/help, /commands — show this list", c);
            return;
        }

        // No slash command → route to the chat-target dropup's current selection.
        // All = ordinary map chat (the server echoes it back to us, so no local echo);
        // the group targets broadcast to the roster we know and echo locally.
        switch (_chatBar?.Target ?? ChatBar.ChatTargetKind.All)
        {
            case ChatBar.ChatTargetKind.Buddy:
                if (_buddyIds.Count == 0) { _chatBar?.AddLine("No buddies are online.", new Color(220, 120, 120)); return; }
                SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Friend, _buddyIds, line));
                _chatBar?.AddLine($"[Buddy] {_myName} : {line}", GroupColor(0), ChatBar.ChatLineType.Buddy);
                return;
            case ChatBar.ChatTargetKind.Party:
                if (_partyMemberIds.Count == 0) { _chatBar?.AddLine("You are not in a party.", new Color(220, 120, 120)); return; }
                SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Party, _partyMemberIds, line));
                _chatBar?.AddLine($"[Party] {_myName} : {line}", GroupColor(1), ChatBar.ChatLineType.Party);
                return;
            case ChatBar.ChatTargetKind.Guild:
                if (_guildMemberIds.Count == 0) { _chatBar?.AddLine("You are not in a guild.", new Color(220, 120, 120)); return; }
                SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Guild, _guildMemberIds, line));
                _chatBar?.AddLine($"[Guild] {_myName} : {line}", GroupColor(2), ChatBar.ChatLineType.Guild);
                return;
            case ChatBar.ChatTargetKind.Alliance:
                SendIfConnected(GameSender.GroupChat(GameSender.ChatGroupType.Alliance, _guildMemberIds, line));
                _chatBar?.AddLine($"[Alliance] {_myName} : {line}", GroupColor(3), ChatBar.ChatLineType.Alliance);
                return;
            case ChatBar.ChatTargetKind.Expedition:
                // We don't track an expedition roster yet, so we can't address the broadcast.
                _chatBar?.AddLine("Expedition chat is unavailable.", new Color(220, 120, 120));
                return;
            default:
                SendIfConnected(GameSender.UserChat(line));
                return;
        }
    }

    // Returns true and the remainder (trimmed of the prefix) when line starts
    // with prefix (case-insensitive). rest is "" when it doesn't match.
    private static bool TryConsumeCommand(string line, string prefix, out string rest)
    {
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = line[prefix.Length..].Trim();
            return true;
        }
        rest = string.Empty;
        return false;
    }

    private static (string token, string remainder) SplitFirstToken(string s)
    {
        s = s.Trim();
        var sp = s.IndexOf(' ');
        return sp < 0 ? (s, string.Empty) : (s[..sp], s[(sp + 1)..].Trim());
    }

    // ChatGroupType → display prefix / colour. 0=Buddy 1=Party 2=Guild 3=Alliance 6=Expedition.
    private static string GroupPrefix(int groupType) => groupType switch
    {
        0 => "Buddy",
        1 => "Party",
        2 => "Guild",
        3 => "Alliance",
        6 => "Expedition",
        _ => "Group",
    };

    private static Color GroupColor(int groupType) => groupType switch
    {
        0 => new Color(150, 200, 255),   // buddy — blue
        1 => new Color(120, 220, 160),   // party — green
        2 => new Color(220, 200, 120),   // guild — gold
        3 => new Color(200, 160, 220),   // alliance — purple
        6 => new Color(230, 170, 90),    // expedition — orange
        _ => Color.White,
    };

    // Incoming group-chat type → ChatBar filter channel (drives the tab filtering).
    private static ChatBar.ChatLineType GroupLineType(int groupType) => groupType switch
    {
        0 => ChatBar.ChatLineType.Buddy,
        1 => ChatBar.ChatLineType.Party,
        2 => ChatBar.ChatLineType.Guild,
        3 => ChatBar.ChatLineType.Alliance,
        6 => ChatBar.ChatLineType.Expedition,
        _ => ChatBar.ChatLineType.Normal,
    };

    public override void OnTextInput(char ch)
    {
        if (_npcTalk?.IsVisible == true) { _npcTalk.OnTextInput(ch); return; }
        if (_userList is { IsVisible: true, WantsTextInput: true }) { _userList.OnTextInput(ch); return; }
        _chatBar?.OnTextInput(ch);
    }

    public override void OnKeyPress(Keys key)
    {
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.OnKeyPress(key); return; }
        // While the system menu is open it's modal — arrows/Enter/ESC drive it.
        if (_gameMenu?.IsVisible == true) { _gameMenu.OnKeyPress(key); return; }

        foreach (var p in _panels)
            if (p.IsVisible && p.OnKeyPress(key)) return;

        // While typing in chat, the keyboard drives the text field — don't fire game hotkeys.
        if (TextField.Active != null) return;

        // ESC opens the authentic in-game system menu (CUIGameMenu: Change Channel, Change Skin,
        // Game Option, System Option, Quit Game). It's modal once open (handled above), so this only
        // fires after any panel that wanted ESC has already consumed it to close itself.
        if (key == Keys.Escape) { _gameMenu?.Open(); return; }

        // F12 always opens KeyConfig (meta-key — not itself bindable)
        if (key == Keys.F12) { ToggleKeyConfig(); return; }

        // Up arrow at a warp portal → travel. Jump is polled separately as a
        // held key, so this discrete press doesn't interfere with jumping.
        if (key == Keys.Up && TryEnterPortal()) return;

        // Route the key through its func-key binding (server keymap / editor).
        DispatchFuncKey(_keyConfig!.ForKey(key));
    }

    private void ToggleKeyConfig()
    {
        if (_keyConfig!.IsVisible) _keyConfig.IsVisible = false;
        else _keyConfig.Open();
    }

    // A pressed key's func-key binding (skill / item / face / menu / action).
    private void DispatchFuncKey(FuncKeyMapped fk)
    {
        switch (fk.Type)
        {
            case FuncKeyType.Skill:
                var level = _skill?.LevelOf(fk.Id) ?? 0;
                if (level > 0) CastSkill(fk.Id, level);
                break;
            case FuncKeyType.Item:
            case FuncKeyType.Effect:
                UseItemById(fk.Id);
                break;
            case FuncKeyType.Menu:
            case FuncKeyType.BasicAction:
                DispatchAction((KeyConfig.KeyAction)fk.Id);
                break;
            case FuncKeyType.Emotion:
            case FuncKeyType.BasicMotion:
                // The bound id is the face palette slot 100..106 (default F1..F7) or any other
                // emotion id; the wire value the server reads is (id - 99) → 1..23 per the v95
                // client's CWvsContext::SendEmotionChange (CUserLocal::UseFuncKeyMapped case 6).
                TriggerEmotion(fk.Id - 99);
                break;
            // Macros are stored/shown/draggable but firing them is a follow-up.
        }
    }

    // Send a face emotion (F1..F7 via the default keymap, or any drag-bound emotion icon).
    // Mirrors the v95 client's CWvsContext::SendEmotionChange ordering: apply locally first
    // (the avatar reacts even if the packet is dropped), then send. A 2 s cooldown matches
    // the v95 client's own guard between successive sends.
    private void TriggerEmotion(int emotion)
    {
        if (_player is null) return;
        if (emotion is < 0 or > 23) return;
        if (_emotionCooldown > 0f) return;
        // TODO(morph): block when morphed once we model that. v95 client's
        // CWvsContext::SendEmotionChange returns early when CAvatar::IsMorphed().

        _player.SetEmotion(emotion, durationMs: -1);
        if (emotion > 0)
        {
            var pos = _physics?.Position ?? _player.Position;
            _emotionBubble?.Add(emotion, pos - new Vector2(0, 60));
        }
        if (Game.Session.IsConnected)
            Game.Session.Send(GameSender.UserEmotion(emotion));
        _emotionCooldown = EmotionCooldownSeconds;
    }

    private void UseItemById(int itemId)
    {
        if (!Game.Session.IsConnected || _item is null) return;
        var slot = _item.FindUseSlot(itemId);
        if (slot <= 0) return;
        Game.Session.Send(GameSender.UseItem((short)slot, itemId));
    }

    private void DispatchAction(KeyConfig.KeyAction action)
    {
        switch (action)
        {
            // ── Panel toggles (matched to GMS v95 KeyAction IDs) ─────────────
            case KeyConfig.KeyAction.Equipment:      _equip!.IsVisible         = !_equip.IsVisible;         break;
            case KeyConfig.KeyAction.Items:          _item!.IsVisible          = !_item.IsVisible;           break;
            case KeyConfig.KeyAction.Skills:         _skill!.IsVisible         = !_skill.IsVisible;          break;
            case KeyConfig.KeyAction.Stats:          _stats!.IsVisible         = !_stats.IsVisible;          break;
            case KeyConfig.KeyAction.QuestLog:       _quest!.IsVisible         = !_quest.IsVisible;          break;
            case KeyConfig.KeyAction.MiniMap:        _miniMap!.CycleMode();                                  break;
            case KeyConfig.KeyAction.WorldMap:       _worldMap!.IsVisible      = !_worldMap.IsVisible;       break;
            case KeyConfig.KeyAction.KeyBindings:    _keyConfig!.IsVisible     = !_keyConfig.IsVisible;      break;
            case KeyConfig.KeyAction.CharInfo:       ToggleOwnProfile();       break;
            case KeyConfig.KeyAction.Family:         _familyWindow!.IsVisible  = !_familyWindow.IsVisible;   break;
            case KeyConfig.KeyAction.ChangeChannel:  _channelSelect!.IsVisible = !_channelSelect.IsVisible;  break;
            case KeyConfig.KeyAction.Menu:           _optionMenu!.IsVisible    = !_optionMenu.IsVisible;     break;
            case KeyConfig.KeyAction.MainMenu:       _optionMenu!.IsVisible    = !_optionMenu.IsVisible;     break;
            // Social panels — open UserList to the relevant tab
            case KeyConfig.KeyAction.Friends:
            case KeyConfig.KeyAction.BuddyChat:      _userList!.IsVisible      = !_userList.IsVisible;       break;
            case KeyConfig.KeyAction.Party:          _userList!.IsVisible      = !_userList.IsVisible;       break;
            case KeyConfig.KeyAction.Guild:          _userList!.IsVisible      = !_userList.IsVisible;       break;
            case KeyConfig.KeyAction.BossParty:      _userList!.IsVisible      = !_userList.IsVisible;       break;
            // Chat
            case KeyConfig.KeyAction.Say:
            case KeyConfig.KeyAction.ToggleChat:
            case KeyConfig.KeyAction.MapleChat:      _chatBar!.ToggleMode();                                  break;
            // Cash Shop — request migration (same connection: server replies with
            // SetCashShop on this socket), then open the local shell.
            case KeyConfig.KeyAction.CashShop:
                if (Game.Session.IsConnected) Game.Session.Send(GameSender.MigrateToCashShop());
                Game.StageDirector.Push(new CashShopStage(
                    _loggerFactory.CreateLogger<CashShopStage>(), _ui, Game.Font,
                    Game.GraphicsDevice.PresentationParameters.BackBufferWidth,
                    Game.GraphicsDevice.PresentationParameters.BackBufferHeight));
                break;
            case KeyConfig.KeyAction.Attack:
                DoMeleeAttack();
                break;
            case KeyConfig.KeyAction.PickUp:
                DoPickUp();
                break;
            case KeyConfig.KeyAction.Sit:
                if (Game.Session.IsConnected)
                {
                    var sit = OutPacket.Of(InHeader.UserSitRequest);
                    sit.WriteShort(-1); // fieldSeatId -1 = ground sit
                    Game.Session.Send(sit);
                }
                break;
            case KeyConfig.KeyAction.Interact:
                TalkToNearestNpc();
                break;
            case KeyConfig.KeyAction.None:
            case KeyConfig.KeyAction.MoveLeft:
            case KeyConfig.KeyAction.MoveRight:
            case KeyConfig.KeyAction.Jump:           break; // handled in Update
        }
    }

    // Push the merged stat snapshot into every panel that displays it. The
    // snapshot is persistent, so unmasked fields keep their prior values; the
    // character name lives in _myName (StatChanged carries no name).
    private void PushCharStats(CharStats stats)
    {
        if (_stats != null)
        {
            _stats.Name  = _myName;
            _stats.JobId = stats.JobId;
            _stats.Job   = JobName(stats.JobId);
            _stats.Level = stats.Level;
            _stats.Exp   = stats.Exp;
            _stats.Fame  = stats.Fame;
            _stats.Guild = stats.Guild;
            _stats.Str   = stats.Str;
            _stats.Dex   = stats.Dex;
            _stats.Int   = stats.Int;
            _stats.Luk   = stats.Luk;
            _stats.Hp    = stats.Hp;   _stats.MaxHp = stats.MaxHp;
            _stats.Mp    = stats.Mp;   _stats.MaxMp = stats.MaxMp;
            _stats.AP    = stats.AP;   _stats.SP    = stats.SP;
        }
        if (_statDetail != null)
        {
            _statDetail.Inputs = new StatInputs
            {
                JobId = stats.JobId,
                Str = stats.Str, Dex = stats.Dex, Int = stats.Int, Luk = stats.Luk,
                MaxHp = stats.MaxHp, MaxMp = stats.MaxMp,
                Speed = 100, Jump = 100,
            };
        }
        if (_statusBar != null)
        {
            _statusBar.Level    = stats.Level;
            _statusBar.CharName = _myName;
            _statusBar.JobName  = JobName(stats.JobId);
            _statusBar.Hp       = stats.Hp;   _statusBar.MaxHp = stats.MaxHp;
            _statusBar.Mp       = stats.Mp;   _statusBar.MaxMp = stats.MaxMp;
            _statusBar.Exp      = stats.Exp;
        }
        // CharInfo is not pushed here: it's repopulated from _charStats + _myLook each time you open
        // your own profile (ShowOwnProfile), so a stat tick can't corrupt another player's profile view.
        if (_skill != null)
        {
            _skill.SP = stats.SP;
            SetSkillJob(stats.JobId);
        }
        // Feed the player's requirement stats to the item tooltips (unmet reqs show red).
        _item?.SetPlayerStats(stats.Level, stats.Str, stats.Dex, stats.Int, stats.Luk, stats.JobId);
        _equip?.SetPlayerStats(stats.Level, stats.Str, stats.Dex, stats.Int, stats.Luk, stats.JobId);
    }

    // Prefer the StringPool job name; fall back to the built-in table for jobs
    // not yet mapped to a pool id (no regression).
    private string JobName(int jobId) =>
        StringId.JobNameId.TryGetValue(jobId, out var sp) ? Game.StringPool.Get(sp) : JobNameFallback(jobId);

    private static string JobNameFallback(int jobId) => jobId switch
    {
        0   => "Beginner",
        100 => "Swordman",  110 => "Fighter",  111 => "Crusader",  112 => "Hero",
        120 => "Page",      121 => "White Knight", 122 => "Paladin",
        130 => "Spearman",  131 => "Dragon Knight", 132 => "Dark Knight",
        200 => "Magician",  210 => "Wizard(F/P)", 211 => "Mage(F/P)", 212 => "Arch Mage(F/P)",
        220 => "Wizard(I/L)", 221 => "Mage(I/L)", 222 => "Arch Mage(I/L)",
        230 => "Cleric",    231 => "Priest",    232 => "Bishop",
        300 => "Bowman",    310 => "Hunter",    311 => "Ranger",    312 => "Bowmaster",
        320 => "Crossbowman", 321 => "Sniper",  322 => "Marksman",
        400 => "Thief",     410 => "Assassin",  411 => "Hermit",    412 => "Night Lord",
        420 => "Bandit",    421 => "Chief Bandit", 422 => "Shadower",
        500 => "Pirate",    510 => "Brawler",   511 => "Marauder",  512 => "Buccaneer",
        520 => "Gunslinger", 521 => "Outlaw",   522 => "Corsair",
        1000 => "Noblesse", 1100 => "Dawn Warrior", 1110 => "Soul Master",
        1200 => "Blaze Wizard", 1300 => "Wind Archer", 1400 => "Night Walker",
        1500 => "Thunder Breaker",
        2000 => "Aran",     2100 => "Aran",
        _ => $"Job {jobId}",
    };

    // ── Combat ────────────────────────────────────────────────────────────────

    private void DoMeleeAttack()
    {
        if (!Game.Session.IsConnected || _physics is null)
        {
            return;
        }
        if (_attackCooldown > 0f)
        {
            return;
        }
        // No swinging while on a ladder/rope.
        if (_physics.Stance is Stance.Ladder or Stance.Rope)
        {
            return;
        }

        // Visible swing immediately (server echoes MobDamaged for the numbers). CharLook
        // picks a weapon-appropriate action (swingO1/stabO1/…, or proneStab when prone)
        // and plays it once; its full length paces the next swing so the animation
        // completes before re-triggering on a held attack key.
        var swingDuration = _player?.Attack(prone: _physics.Stance == Stance.Prone) ?? 0f;
        _attackCooldown = swingDuration > 0f ? swingDuration : AttackCooldownSeconds;
        _physics.StopWalking();   // stop the walk on swing start so we don't slide while rooted
        if (_sound?.GetItem("Weapon.img/swordL/Attack") is WzSound swing)
        {
            Game.AudioPlayer.PlayEffect(swing);
        }

        var pos = _physics.Position;
        var facingLeft = _physics.FacingLeft;

        // Hit box: a strip extending MeleeReachX in front of the player, MeleeReachY tall.
        var minX = facingLeft ? pos.X - MeleeReachX : pos.X;
        var maxX = facingLeft ? pos.X : pos.X + MeleeReachX;
        var minY = pos.Y - MeleeReachY * 2; // mobs sit on the ground; bias the box upward
        var maxY = pos.Y + MeleeReachY;

        var targets = new List<MeleeTarget>(MaxMeleeTargets);
        foreach (var mob in _mobs.Values)
        {
            if (mob.IsDead)
            {
                continue;
            }
            var mp = mob.Position;
            if (mp.X < minX || mp.X > maxX || mp.Y < minY || mp.Y > maxY)
            {
                continue;
            }
            // Client-chosen damage; the v95 Kinoko server trusts it (no
            // server-side recompute). Scale it by the character's job/level/stats
            // via MeleeDamage.Estimate so a stronger character hits harder, then
            // roll within that window. A 1-hit swing → one damage int per mob.
            var (dmgMin, dmgMax) = MeleeDamage.Estimate(
                _charStats.JobId, Math.Max(1, _charStats.Level),
                _charStats.Str, _charStats.Dex, _charStats.Int, _charStats.Luk);
            var dmg = _attackRng.Next(dmgMin, dmgMax + 1);
            targets.Add(new MeleeTarget
            {
                MobId = mob.MobId,
                HitX = (short)mp.X,
                HitY = (short)mp.Y,
                Delay = 0,
                Damage = new[] { dmg },
            });

            // Local hit feedback - the server only echoes MobHPIndicator(298) back to the
            // attacker (no MobDamaged broadcast to the hitter), so the hit anim / damage
            // number / aggro flip / mob knockback are all driven locally from here.
            mob.OnHit(dmg);
            _dmgNumbers?.Add(dmg, mob.Position, DamageNumber.Kind.MobDamage);
            if (_mobCtl.TryGetValue(mob.MobId, out var hitCtl))
            {
                hitCtl.OnDamagedByPlayer();
                var pushDir = mob.Position.X >= pos.X ? +1f : -1f;
                hitCtl.ApplyHitKnockback(pushDir * 25f);
            }

            if (targets.Count >= MaxMeleeTargets)
            {
                break;
            }
        }

        // actionAndDir: basic swing action 0, bLeft bit in 0x8000.
        var actionAndDir = (short)(facingLeft ? 0x8000 : 0x0000);
        var blob = MeleeAttackEncoder.Encode(
            _fieldKey, actionAndDir, attackSpeed: 6,
            userX: (short)pos.X, userY: (short)pos.Y,
            targets, damagePerMob: 1);
        Game.Session.SendRaw(blob);

        _logger.LogDebug("UserMeleeAttack: {N} target(s) in range", targets.Count);
    }

    private void DoPickUp()
    {
        if (!Game.Session.IsConnected || _drops.Count == 0) return;
        var playerPos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
        DropSprite? nearest = null;
        var bestDist = 80f;
        foreach (var drop in _drops.Values)
        {
            var d = Vector2.Distance(playerPos, drop.Position);
            if (d < bestDist) { bestDist = d; nearest = drop; }
        }
        if (nearest is null) return;

        Game.Session.Send(GameSender.PickUpDrop(
            _fieldKey, (short)playerPos.X, (short)playerPos.Y, nearest.DropId));
    }

    private void SpawnNpc(int id, Vector2 worldPos, string name)
    {
        var npc = new NpcLook(id, worldPos, Game.Font) { Name = name };
        npc.Load(_loader!, _npcWz);
        AttachAmbientSpeak(npc, id);
        _npcs.Add(npc);
    }

    /// <summary>Resolve an NPC's <c>info/speak</c> keys to text (String.wz/Npc.img) and attach them so
    /// it floats random ambient lines in a chat balloon. NPCs without a speak node stay silent.</summary>
    private void AttachAmbientSpeak(NpcLook npc, int templateId)
    {
        if (npc.SpeakKeys.Count == 0) return;
        var name = npc.Name.Split(" : ")[0];
        var lines = new List<string>();
        foreach (var key in npc.SpeakKeys)
        {
            var line = Game.Names.NpcText(templateId, key);
            if (!string.IsNullOrEmpty(line))
                lines.Add(line.Replace("/name", name));
        }
        npc.SetAmbientSpeak(lines);
    }

}
