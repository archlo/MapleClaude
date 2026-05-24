using MapleClaude.App;
using MapleClaude.Character;
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
    private CharacterRenderer? _charRenderer;
    private readonly List<NpcLook> _npcs = new();

    // Mobs
    private readonly Dictionary<int, MobLook> _mobs = new();
    // Mob IDs we control (MobChangeController ctrl=1)
    private readonly HashSet<int> _controlledMobs = new();

    // Other players
    private readonly Dictionary<int, OtherCharLook> _otherChars = new();

    // Drops
    private readonly Dictionary<int, DropSprite> _drops = new();

    // Damage numbers
    private DamageNumber? _dmgNumbers;

    // Always-visible panels
    private StatusBar? _statusBar;
    private ChatBar? _chatBar;
    private MiniMap? _miniMap;
    private BuffList? _buffList;

    // Toggle panels
    private EquipInventory? _equip;
    private ItemInventory? _item;
    private SkillBook? _skill;
    private StatsInfo? _stats;
    private QuestLog? _quest;
    private KeyConfig? _keyConfig;
    private OptionMenu? _optionMenu;
    private CharInfo? _charInfo;

    // New high-priority panels
    private WorldMap?       _worldMap;
    private UserList?       _userList;
    private ChannelSelect?  _channelSelect;
    private StatusMessenger? _messenger;

    // Modal panels
    private NpcTalk? _npcTalk;
    private Shop? _shop;
    private Trunk? _trunk;
    private Messenger? _messengerWin;
    private Notice? _notice;
    private QuitConfirmOverlay? _quitConfirm;

    private readonly List<GamePanel> _panels = new();
    private GamePacketHandler? _netHandler;

    // Accumulated stat snapshot from server packets
    private CharStats _charStats = new();

    // Input state
    private bool _moveLeft;
    private bool _moveRight;
    private bool _jumpPressed;

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
    private bool _loggedMobMoveTodo;

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
    private int _pendingInviterId;
    private bool _hasPendingInvite;
    private string _myName = "Hero";

    // Messenger invite state: the dwSN of a pending messenger invite, joined via /maccept.
    private int _pendingMessengerId;
    private bool _hasMessengerInvite;

    // Melee attack pacing.
    private float _attackCooldown;
    private const float AttackCooldownSeconds = 0.6f;
    private const int MeleeReachX = 120;   // px in front of the player
    private const int MeleeReachY = 40;    // px above/below the player's foot point
    private const int MaxMeleeTargets = 6; // v95 caps a melee swing at 6 mobs
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
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        // Entering the game enlarges the window from the 800×600 login canvas to
        // the in-game resolution; remember the login size to restore on exit.
        var pp0 = GraphicsDevice.PresentationParameters;
        _prevW = pp0.BackBufferWidth;
        _prevH = pp0.BackBufferHeight;
        Game.ResizeWindow(InGameWidth, InGameHeight);

        var pp = GraphicsDevice.PresentationParameters;
        var font = Game.Font;

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
        _statusBar = new StatusBar(_loader, _ui, font) { IsVisible = true };
        _chatBar = new ChatBar(_loader, _ui, font) { IsVisible = true };
        _miniMap = new MiniMap(_loader, _ui, font) { IsVisible = true };
        _buffList = new BuffList(_loader, _ui, font) { IsVisible = true };
        _equip = new EquipInventory(_loader, _ui, font);
        _item = new ItemInventory(_loader, _ui, font);
        _item.OnItemActivate = OnInventoryItemActivate;
        _skill = new SkillBook(_loader, _ui, font);
        _skill.OnSkillUp = id => SendIfConnected(GameSender.SkillUp(id));
        _skill.OnSkillCast = CastSkill;
        _stats = new StatsInfo(_loader, _ui, font);
        _quest = new QuestLog(_loader, _ui, font);
        _quest.OnResign = id =>
        {
            if (Game.Session.IsConnected) Game.Session.Send(GameSender.QuestResign((short)id));
        };
        _keyConfig = new KeyConfig(_loader, _ui, font);
        _optionMenu = new OptionMenu(_loader, _ui, font);
        _charInfo = new CharInfo(_loader, _ui, font);
        _npcTalk = new NpcTalk(_loader, _ui, font);
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
        _statusBar.OnInfo    = () => _charInfo!.IsVisible  = !_charInfo.IsVisible;
        _statusBar.OnEquip   = () => _equip!.IsVisible     = !_equip.IsVisible;
        _statusBar.OnItems   = () => _item!.IsVisible      = !_item.IsVisible;
        _statusBar.OnSkills  = () => _skill!.IsVisible     = !_skill.IsVisible;
        _statusBar.OnStats   = () => _stats!.IsVisible     = !_stats.IsVisible;
        _statusBar.OnOptions = () => _optionMenu!.IsVisible = !_optionMenu.IsVisible;
        _statusBar.OnKeys    = () => _keyConfig!.IsVisible  = !_keyConfig.IsVisible;
        _statusBar.OnQuit     = () => _quitConfirm!.IsVisible = true;
        _statusBar.OnCashShop = () => Game.StageDirector.Push(new CashShopStage(
            _loggerFactory.CreateLogger<CashShopStage>(), _ui, Game.Font,
            Game.GraphicsDevice.PresentationParameters.BackBufferWidth,
            Game.GraphicsDevice.PresentationParameters.BackBufferHeight));
        _statusBar.OnCharacter = () => _stats!.IsVisible   = !_stats.IsVisible;
        _statusBar.OnMenu    = () => _optionMenu!.IsVisible = !_optionMenu.IsVisible;

        // MiniMap: set map info and initial bounds
        _miniMap.SetMapInfo(string.Empty, string.Empty, new Rectangle(-3000, -2000, 6000, 4000));

        _panels.Add(_statusBar);
        _panels.Add(_chatBar);
        _panels.Add(_miniMap);
        _panels.Add(_buffList);
        _panels.Add(_equip);
        _panels.Add(_item);
        _panels.Add(_skill);
        _panels.Add(_stats);
        _panels.Add(_quest);
        _panels.Add(_keyConfig);
        _panels.Add(_optionMenu);

        // ── Persisted settings (keybinds + volumes) ───────────────────────────
        // Disk bindings are applied now; a server-sent keymap (FuncKeyMappedInit)
        // arrives later and overrides them via ApplyServerKeymap — server wins.
        var saved = Game.Settings.Load();
        _keyConfig.LoadBindings(ParseBindings(saved.KeyBindings));
        _keyConfig.LoadFuncBinds(ParseFuncBinds(saved.FuncBindings));
        _optionMenu.LoadVolumes(saved.BgmVolume, saved.SfxVolume);
        ApplyAudioVolumes();
        _keyConfig.OnBindingsChanged += SaveSettings;
        _optionMenu.OnSettingsChanged += () => { SaveSettings(); ApplyAudioVolumes(); };
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
        _dmgNumbers    = new DamageNumber(font);

        _statusBar.OnCommunity = ToggleCommunityPanel;
        _chatBar!.OnSendChat = OnChatSubmit;

        _channelSelect.OnChannelChange = ch =>
            _logger.LogInformation("Channel change requested: CH{Ch} — no packet yet", ch);

        _panels.Add(_worldMap);
        _panels.Add(_userList);
        _panels.Add(_channelSelect);
        _panels.Add(_messenger);

        // ── Network packet handler ────────────────────────────────────────────
        _netHandler = new GamePacketHandler(_loggerFactory.CreateLogger<GamePacketHandler>());

        _netHandler.OnStatChanged = stats =>
        {
            _charStats = stats;
            if (!string.IsNullOrEmpty(stats.Name)) _myName = stats.Name;
            // Feed to UI panels
            if (_stats != null)
            {
                _stats.Level  = stats.Level;
                _stats.Job    = JobName(stats.JobId);
                _stats.Str    = stats.Str;
                _stats.Dex    = stats.Dex;
                _stats.Int    = stats.Int;
                _stats.Luk    = stats.Luk;
                _stats.Hp     = stats.Hp;   _stats.MaxHp = stats.MaxHp;
                _stats.Mp     = stats.Mp;   _stats.MaxMp = stats.MaxMp;
                _stats.AP     = stats.AP;   _stats.SP    = stats.SP;
            }
            if (_statusBar != null)
            {
                _statusBar.Level    = stats.Level;
                _statusBar.CharName = stats.Name;
                _statusBar.Hp       = stats.Hp;   _statusBar.MaxHp = stats.MaxHp;
                _statusBar.Mp       = stats.Mp;   _statusBar.MaxMp = stats.MaxMp;
                _statusBar.Exp      = stats.Exp;
            }
            if (_charInfo != null)
            {
                _charInfo.CharName = stats.Name;
                _charInfo.Level    = stats.Level;
                _charInfo.Job      = JobName(stats.JobId);
            }
            if (_skill != null) _skill.SP = stats.SP;
            if (stats.Level > 0)
                _messenger?.ShowLevelUp(stats.Level);
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

        // Wire AP distribution to server packets
        if (_stats != null)
        {
            _stats.OnStrUp = () => { if (Game.Session.IsConnected) Game.Session.Send(GameSender.UserAbilityUp(GameSender.MapleStat.Str)); };
            _stats.OnDexUp = () => { if (Game.Session.IsConnected) Game.Session.Send(GameSender.UserAbilityUp(GameSender.MapleStat.Dex)); };
            _stats.OnIntUp = () => { if (Game.Session.IsConnected) Game.Session.Send(GameSender.UserAbilityUp(GameSender.MapleStat.Int)); };
            _stats.OnLukUp = () => { if (Game.Session.IsConnected) Game.Session.Send(GameSender.UserAbilityUp(GameSender.MapleStat.Luk)); };
        }

        // Wire SkillBook SP-up to server
        // (SkillBook.LevelUpRow calls the action per skill row — wired when we have skill data)

        _netHandler.RegisterAll(Game.Session);

        // ── FieldHandlers events (wired to rendering + UI) ────────────────────
        var fh = Game.FieldHandlers;

        fh.OnSkillRecordResult += records => ApplySkills(records);
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
            if ((a.Mask & 0x4000)!= 0) { if (_stats != null) _stats.AP = a.Ap; }
            if ((a.Mask & 0x8000)!= 0) { if (_stats != null && _skill != null) _skill.SP = a.Sp; }
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
            if (a.State == 2) _messenger?.ShowQuest(Game.Names.QuestName(a.QuestId) ?? $"Quest {a.QuestId}");
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
                                  new Vector2(args.X, args.Y));
            mob.Load(_loader!, _mobWz);
            _mobs[args.MobId] = mob;
            _logger.LogDebug("Mob enter: id={Id} tmpl={T} pos=({X},{Y})", args.MobId, args.TemplateId, args.X, args.Y);
        };

        fh.OnMobLeave += mobId =>
        {
            _mobs.Remove(mobId);
            _controlledMobs.Remove(mobId);
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
        };

        fh.OnMobChangeController += (mobId, isCtrl) =>
        {
            if (isCtrl) _controlledMobs.Add(mobId);
            else        _controlledMobs.Remove(mobId);
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

        fh.OnUserMove   += args =>
        {
            if (_otherChars.TryGetValue(args.CharId, out var other))
                other.SetPosition(args.X, args.Y);
        };

        fh.OnDropEnter += args =>
            _drops[args.DropId] = new DropSprite(args.DropId, args.IsMoney,
                                                  args.ItemIdOrAmount,
                                                  new Vector2(args.X, args.Y), Game.Font);

        fh.OnDropLeave  += args => _drops.Remove(args.DropId);

        fh.OnInventoryOperation += ops =>
        {
            foreach (var op in ops)
            {
                var tab = InvTypeToTab(op.InvType);
                switch (op.OpType)
                {
                    case 0: // NewItem — full item slot
                        if (op.Item is null) break;
                        if (op.InvType == InventoryType.Equipped)
                        {
                            _equip?.SetEquipped(op.Pos, ItemDisplayName(op.Item));
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
                    case 2: // Position — move (NewPos 0 = removed from this tab)
                        if (op.InvType == InventoryType.Equipped || op.Pos < 0 || op.NewPos < 0)
                        {
                            // Equip/unequip move — equipped panel + avatar refresh come via
                            // separate packets; the stat change arrives via StatChanged.
                            break;
                        }
                        if (tab >= 0) _item?.MoveSlot(tab, op.Pos, op.NewPos);
                        break;
                    case 3: // DelItem
                        if (op.InvType == InventoryType.Equipped) _equip?.RemoveEquipped(op.Pos);
                        else if (tab >= 0)                        _item?.RemoveSlot(tab, op.Pos);
                        break;
                }
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
        };

        // ── Social: group chat / whisper / party / friends ────────────────────
        fh.OnGroupMessage += (type, from, text) =>
            _chatBar?.AddLine($"[{GroupPrefix(type)}] {from} : {text}", GroupColor(type));

        fh.OnWhisper += (from, ch, text) =>
            _chatBar?.AddLine($"[whisper] {from} : {text}", new Color(220, 150, 220));

        fh.OnFriendList += list =>
        {
            _userList!.ClearFriends();
            foreach (var f in list)
            {
                _userList.AddFriend(new UserList.FriendEntry
                {
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
                Name  = m.Name,
                Level = m.Level,
                Job   = JobName(m.Job),
                HpPct = 100,
            }));
            _logger.LogInformation("Party loaded: {Count} member(s) boss={Boss}", members.Count, bossId);
        };

        fh.OnGuildLoad += args =>
        {
            if (_userList is null) return;
            if (args is null) { _userList.SetGuild(string.Empty, Array.Empty<UserList.GuildEntry>()); return; }
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
            _npcTalk.LoadPortrait(_npcWz, args.SpeakerId);
            // The answer we send back must echo the type the server sent.
            var type = args.MsgType;
            _dialogMsgType = type;
            switch (type)
            {
                case ScriptMessageType.AskMenu:
                    var choices = ParseMenuChoices(args.Text);
                    _npcTalk.ShowMenu(args.Text, choices);
                    _npcTalk.OnMenuChoice = choice => SendIfConnected(GameSender.ScriptAnswerNumber(type, choice));
                    break;

                case ScriptMessageType.AskYesNo:
                case ScriptMessageType.AskAccept:
                    _npcTalk.Show(args.Text, NpcTalk.DialogType.YesNo);
                    _npcTalk.OnYes = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 1));
                    _npcTalk.OnNo  = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 0));
                    break;

                case ScriptMessageType.AskText:
                case ScriptMessageType.AskBoxText:
                    _npcTalk.ShowAskText(args.Text, args.DefaultText, args.MinLength, args.MaxLength);
                    _npcTalk.OnTextConfirm = text => SendIfConnected(GameSender.ScriptAnswerText(type, text));
                    _npcTalk.OnNo          = ()   => SendIfConnected(GameSender.ScriptAnswerCancel(type));
                    break;

                case ScriptMessageType.AskNumber:
                    _npcTalk.ShowAskNumber(args.Text, args.DefaultNum, args.MinNum, args.MaxNum);
                    _npcTalk.OnNumberConfirm = num => SendIfConnected(GameSender.ScriptAnswerNumber(type, num));
                    _npcTalk.OnNo            = ()  => SendIfConnected(GameSender.ScriptAnswerCancel(type));
                    break;

                default: // SAY (0) and rare types — a plain message with prev/next.
                    _npcTalk.Show(args.Text,
                        args.HasPrev && args.HasNext ? NpcTalk.DialogType.PrevNext
                      : args.HasNext                ? NpcTalk.DialogType.Next
                      : NpcTalk.DialogType.Ok);
                    // SAY action: 1 = next/ok, -1 = prev, 0 = end.
                    _npcTalk.OnOk = _npcTalk.OnNext = () => SendIfConnected(GameSender.ScriptAnswerSay(type, 1));
                    _npcTalk.OnPrev = () => SendIfConnected(GameSender.ScriptAnswerSay(type, -1));
                    break;
            }
        };

        fh.OnFuncKeyMappedInit += entries =>
        {
            _keyConfig?.ApplyServerKeymap(
                entries.Select(e => (e.KeyIndex, (int)e.Type, e.ActionId)));
        };

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
        base.OnExit();
    }

    private void OnSetField(SetFieldArgs args)
    {
        _fieldKey = args.FieldKey;
        if (args.Stat is null || _map is null)
        {
            return;
        }
        Game.CharacterId = args.Stat.CharacterId;
        if (args.Look is not null && _player is not null && _charRenderer is not null)
        {
            _player.SetAvatar(_charRenderer, args.Look);
        }
        // Ask the server for our guild roster once (it isn't pushed on login).
        if (!_guildLoadSent && Game.Session.IsConnected)
        {
            Game.Session.Send(GameSender.GuildLoad());
            _guildLoadSent = true;
        }
        try
        {
            // A SetField (re)initializes the field — drop every entity from the
            // previous map so a portal/channel transfer doesn't leak stale
            // mobs/npcs/drops/players. The server re-sends them after SetField.
            _mobs.Clear();
            _controlledMobs.Clear();
            _npcs.Clear();
            _otherChars.Clear();
            _drops.Clear();

            _field = new FieldScene(_loggerFactory.CreateLogger<FieldScene>(), _map, _loader!);
            _field.Load(args.Stat.PosMap);
            _physics = new PlayerController(_field);
            _field.PlacePlayerAtPortal(_physics, args.Stat.Portal);
            // Re-position archlo's CharLook to the spawn point.
            if (_player != null)
            {
                _player.Position = _physics.Position;
            }
            _camera.Target = _physics.Position;
            _camera.MapBounds = _field.Info.HasVR
                ? new Rectangle(_field.Info.VRLeft, _field.Info.VRTop,
                                _field.Info.VRRight - _field.Info.VRLeft,
                                _field.Info.VRBottom - _field.Info.VRTop)
                : _camera.MapBounds;
            PopulateInventory(args);
            PopulateQuests(args);
            UpdateMapName(args.Stat.PosMap);
            PlayMapBgm(_field.Info.Bgm);
            _logger.LogInformation("SetField processed — mapId={Map} portal={Portal} money={Money}",
                args.Stat.PosMap, args.Stat.Portal, args.Money);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load field {Id}", args.Stat.PosMap);
        }
    }

    // Walk-into-portal travel: if the player stands on a warp portal (one with a
    // real target map), send UserTransferFieldRequest. The server replies with a
    // fresh SetField for the destination map.
    private const float PortalEnterRange = 40f;

    private bool TryEnterPortal()
    {
        if (_field is null || _physics is null || !Game.Session.IsConnected) return false;
        var pos = _physics.Position;
        foreach (var portal in _field.Portals.Values)
        {
            if (portal.TargetMap <= 0 || portal.TargetMap == 999_999_999) continue;
            if (Vector2.Distance(pos, portal.Position) > PortalEnterRange) continue;
            Game.Session.Send(GameSender.TransferField(
                _fieldKey, portal.TargetMap, portal.TargetPortal, (short)pos.X, (short)pos.Y));
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
        _skill.SetSkills(records.Select(r =>
        {
            var info = Game.Skills.Get(r.SkillId);
            return new SkillBook.SkillEntry
            {
                Id = r.SkillId,
                Name = Game.Names.SkillName(r.SkillId) ?? $"Skill {r.SkillId}",
                Level = r.Level,
                MaxLevel = info?.MaxLevel ?? (r.MasterLevel > 0 ? r.MasterLevel : 20),
                Passive = info?.Passive ?? false,
                MpCost = info?.MpConAt(Math.Max(1, r.Level)) ?? 0,
                IconCanvas = info?.Icon,
            };
        }));
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

    // Persisted bindings are "<Keys>" → "<KeyAction>" name pairs; parse back to
    // the live enum map, skipping any entry that no longer resolves.
    private static Dictionary<Keys, KeyConfig.KeyAction> ParseBindings(Dictionary<string, string> raw)
    {
        var map = new Dictionary<Keys, KeyConfig.KeyAction>();
        foreach (var (ks, vs) in raw)
        {
            if (Enum.TryParse<Keys>(ks, out var key) &&
                Enum.TryParse<KeyConfig.KeyAction>(vs, out var action))
            {
                map[key] = action;
            }
        }
        return map;
    }

    // Func bindings persist as "<Keys>" → "<typeInt>:<id>".
    private static Dictionary<Keys, KeyConfig.FuncBind> ParseFuncBinds(Dictionary<string, string> raw)
    {
        var map = new Dictionary<Keys, KeyConfig.FuncBind>();
        foreach (var (ks, vs) in raw)
        {
            var colon = vs.IndexOf(':');
            if (colon <= 0) continue;
            if (Enum.TryParse<Keys>(ks, out var key) &&
                int.TryParse(vs.AsSpan(0, colon), out var typeInt) &&
                int.TryParse(vs.AsSpan(colon + 1), out var id) &&
                Enum.IsDefined(typeof(KeyConfig.FuncBindType), typeInt))
            {
                map[key] = new KeyConfig.FuncBind((KeyConfig.FuncBindType)typeInt, id);
            }
        }
        return map;
    }

    private void SaveSettings()
    {
        var s = Game.Settings.Load();
        if (_keyConfig != null)
        {
            s.KeyBindings = _keyConfig.Bindings.ToDictionary(
                kv => kv.Key.ToString(), kv => kv.Value.ToString());
            s.FuncBindings = _keyConfig.FuncBinds.ToDictionary(
                kv => kv.Key.ToString(), kv => $"{(int)kv.Value.Type}:{kv.Value.Id}");
        }
        if (_optionMenu != null)
        {
            s.BgmVolume = _optionMenu.BgmVolume;
            s.SfxVolume = _optionMenu.SfxVolume;
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
    };

    /// <summary>Load the initial inventory + skills delivered in SetField's CharacterData.</summary>
    private void PopulateQuests(SetFieldArgs args)
    {
        if (_quest is null || args.Quests is null) return;
        _quest.SetQuests(args.Quests.Select(q => new QuestLog.QuestEntry
        {
            Id = q.QuestId,
            Name = Game.Names.QuestName(q.QuestId) ?? $"Quest {q.QuestId}",
            Progress = q.Value,
            Complete = false,
        }));
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
        _equip?.ClearEquipped();
        foreach (var (invType, items) in args.Inventory)
        {
            var tab = InvTypeToTab(invType);
            foreach (var (pos, item) in items)
            {
                if (invType == InventoryType.Equipped)
                {
                    _equip?.SetEquipped(pos, ItemDisplayName(item));
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
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteBytes(movePathBlob);
        Game.Session.Send(p);
    }

    public override void Update(GameTime gameTime)
    {
        Game.Session.DrainQueue();   // dispatch all queued server packets on game thread
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Read held keys each frame (movement is frame-continuous).
        // Movement is driven by the live KeyConfig bindings so user rebinds
        // (rebound MoveLeft/MoveRight/Jump in the in-game F12 dialog) take
        // effect without needing a restart.
        var kb = Keyboard.GetState();
        _moveLeft = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.MoveLeft);
        _moveRight = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.MoveRight);
        _jumpPressed = _keyConfig!.IsActionDown(kb, KeyConfig.KeyAction.Jump);

        // Foothold physics — only when the channel server has sent SetField and
        // a map is loaded. Drives both the CharLook visual position and the
        // outbound UserMove(44) packets.
        if (_attackCooldown > 0f)
        {
            _attackCooldown -= dt;
        }

        if (_physics != null)
        {
            var input = new PlayerInput
            {
                Left = _moveLeft,
                Right = _moveRight,
                JumpPressed = _jumpPressed,
            };
            _physics.Update(input, dt);
            if (_player != null)
            {
                _player.Position = _physics.Position;
                _player.UpdateFromPhysics(dt, _physics.Stance, _physics.FacingLeft);
            }
            _camera.Target = _physics.Position;

            if (_physics.TryFlushMovePath(out var blob))
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

        // NPCs
        foreach (var npc in _npcs) npc.Update(dt);

        // Mobs
        var deadMobs = new List<int>();
        foreach (var (id, mob) in _mobs)
        {
            mob.Update(dt);
            if (mob.IsDead) deadMobs.Add(id);
        }
        foreach (var id in deadMobs) _mobs.Remove(id);

        // TODO(wire-correctness): outgoing MobMove(227) is intentionally NOT
        // sent yet. The upstream shape (per kinoko/handler/field/MobHandler.handleMobMove
        // lines 48-98) is:
        //   int dwMobID, short mobCtrlSn, byte actionMask, byte actionAndDir,
        //   int targetInfo, int multiTargetForBall count + (int,int)*count,
        //   int randTimeForAreaAttack count + int*count,
        //   byte (bActive | 16*!cheatRand), int HackedCode,
        //   int ptTarget.x, int ptTarget.y, int dwHackedCodeCRC,
        //   <MovePath blob>,
        //   byte bChasing, byte (pTarget!=0), byte pvcActive.bChasing,
        //   byte pvcActive.bChasingHack, int pvcActive.tChaseDuration
        // We don't currently run a mob-side physics simulation so we can't
        // synthesise a legitimate MovePath. Surface a one-shot debug log
        // rather than ship a malformed packet that would desync the IV chain.
        if (Game.Session.IsConnected && _controlledMobs.Count > 0 && !_loggedMobMoveTodo)
        {
            _loggedMobMoveTodo = true;
            _logger.LogDebug(
                "MobMove(227) outbound is suppressed — wire shape (mobCtrlSn + HackedCode + MovePath + tail) " +
                "is not yet implemented; controlled mobs will appear frozen to other clients until this is fixed.");
        }

        // Map animation + background autoscroll
        _field?.Update(dt * 1000.0);

        // Drops
        foreach (var drop in _drops.Values) drop.Update(dt);

        // Other players
        foreach (var other in _otherChars.Values) other.Update(dt);

        // Damage numbers
        _dmgNumbers?.Update(dt);

        // Feed MiniMap map bounds so the coordinate projection matches the camera
        if (_miniMap != null)
            _miniMap.SetMapInfo(_mapStreet, _mapNameText, _camera.MapBounds);

        // Sync stats to panels
        if (_statusBar != null)
        {
            _statusBar.Hp      = _stats?.Hp      ?? 50;
            _statusBar.MaxHp   = _stats?.MaxHp   ?? 50;
            _statusBar.Mp      = _stats?.Mp       ?? 30;
            _statusBar.MaxMp   = _stats?.MaxMp    ?? 30;
            _statusBar.Level   = _stats?.Level    ?? 1;
            _statusBar.CharName = _charInfo?.CharName ?? "Hero";
        }

        // Feed player position + NPC dots to minimap
        if (_miniMap != null && _player != null)
        {
            _miniMap.PlayerWorldPos = _player.Position;
            _miniMap.SetDots(_npcs.Select(n => (n.Position, new Color(255, 220, 80))));
        }

        // Panels
        foreach (var p in _panels) p.Update(gameTime);
        _quitConfirm?.Update(gameTime);
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

        // NPCs (world-space → screen)
        foreach (var npc in _npcs)
        {
            var sp = _camera.WorldToScreen(npc.Position);
            if (sp.X > -100 && sp.X < w + 100)
                npc.Draw(sb, Game.WhitePixel, sp);
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
                mob.Draw(sb, Game.WhitePixel, ms);
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
        }

        // Damage numbers (drawn on top of everything)
        _dmgNumbers?.Draw(sb, Game.WhitePixel, _camera.WorldToScreen);

        // UI panels (screen-space)
        foreach (var p in _panels)
            p.Draw(sb, Game.WhitePixel);

        _quitConfirm?.Draw(sb, Game.WhitePixel);
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
        for (var i = _panels.Count - 1; i >= 0; i--)
        {
            var p = _panels[i];
            if (p.IsVisible && p.HandleMouseButton(x, y, down)) return;
        }
        // NPC click-to-talk — only when no dialog is already open
        if (down && _npcTalk?.IsVisible != true)
        {
            foreach (var npc in _npcs)
            {
                var sp = _camera.WorldToScreen(npc.Position);
                if (npc.GetScreenBounds(sp).Contains(x, y))
                {
                    SelectNpc(npc);
                    break;
                }
            }
        }
    }

    /// <summary>Send UserSelectNpc with the player's current position (the
    /// server reads it). Shared by click-to-talk and the Interact key.</summary>
    private void SelectNpc(NpcLook npc)
    {
        if (!Game.Session.IsConnected)
        {
            return;
        }
        var pos = _physics?.Position ?? _player?.Position ?? Vector2.Zero;
        Game.Session.Send(GameSender.UserSelectNpc(npc.ObjId, (short)pos.X, (short)pos.Y));
    }

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
            _chatBar?.AddLine($"[Party] {_myName} : {partyRest}", GroupColor((int)GameSender.ChatGroupType.Party));
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

        // Plain map chat.
        SendIfConnected(GameSender.UserChat(line));
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

    // ChatGroupType → display prefix / colour. 0=Buddy 1=Party 2=Guild 3=Alliance.
    private static string GroupPrefix(int groupType) => groupType switch
    {
        0 => "Buddy",
        1 => "Party",
        2 => "Guild",
        3 => "Alliance",
        _ => "Group",
    };

    private static Color GroupColor(int groupType) => groupType switch
    {
        0 => new Color(150, 200, 255),   // buddy — blue
        1 => new Color(120, 220, 160),   // party — green
        2 => new Color(220, 200, 120),   // guild — gold
        3 => new Color(200, 160, 220),   // alliance — purple
        _ => Color.White,
    };

    public override void OnTextInput(char ch)
    {
        if (_npcTalk?.IsVisible == true) { _npcTalk.OnTextInput(ch); return; }
        _chatBar?.OnTextInput(ch);
    }

    public override void OnKeyPress(Keys key)
    {
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.OnKeyPress(key); return; }

        foreach (var p in _panels)
            if (p.IsVisible && p.OnKeyPress(key)) return;

        // F12 always opens KeyConfig (meta-key — not itself bindable)
        if (key == Keys.F12) { _keyConfig!.IsVisible = !_keyConfig.IsVisible; return; }

        // Up arrow at a warp portal → travel. Jump is polled separately as a
        // held key, so this discrete press doesn't interfere with jumping.
        if (key == Keys.Up && TryEnterPortal()) return;

        // Skill / item / face keys (the player's func-key bindings) take precedence.
        if (_keyConfig!.GetFuncBind(key) is { } funcBind) { DispatchFunc(funcBind); return; }

        // All other keys routed through KeyConfig action bindings
        DispatchAction(_keyConfig!.GetAction(key));
    }

    // A skill/item/face/macro key binding (from the server keymap or the editor).
    private void DispatchFunc(KeyConfig.FuncBind bind)
    {
        switch (bind.Type)
        {
            case KeyConfig.FuncBindType.Skill:
                var level = _skill?.LevelOf(bind.Id) ?? 0;
                if (level > 0) CastSkill(bind.Id, level);
                break;
            case KeyConfig.FuncBindType.Item:
                UseItemById(bind.Id);
                break;
            // Face (emote) and Macro have no sender yet — bindings are stored/shown
            // but firing them is a follow-up (UserEmotion / macro execution).
        }
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
            case KeyConfig.KeyAction.MiniMap:        _miniMap!.IsVisible       = !_miniMap.IsVisible;        break;
            case KeyConfig.KeyAction.WorldMap:       _worldMap!.IsVisible      = !_worldMap.IsVisible;       break;
            case KeyConfig.KeyAction.KeyBindings:    _keyConfig!.IsVisible     = !_keyConfig.IsVisible;      break;
            case KeyConfig.KeyAction.CharInfo:       _charInfo!.IsVisible      = !_charInfo.IsVisible;       break;
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
            case KeyConfig.KeyAction.MapleChat:      _chatBar!.IsVisible       = !_chatBar.IsVisible;        break;
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
        _attackCooldown = AttackCooldownSeconds;

        // Visible swing immediately (server echoes MobDamaged for the numbers).
        _physics.TriggerAttack();
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
        _npcs.Add(npc);
    }

    // Parses #L0#choice#l menu anchors from a script text, or falls back to newline-split.
    private static IReadOnlyList<string> ParseMenuChoices(string text)
    {
        var results = new List<string>();
        // Try #L<n>#...#l pattern first
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"#L\d+#(.+?)#l");
        if (matches.Count > 0)
        {
            foreach (System.Text.RegularExpressions.Match m in matches)
                results.Add(m.Groups[1].Value.Trim());
            return results;
        }
        // Fallback: newline-separated lines, stripping common format codes
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(line, @"#[A-Za-z0-9]+#?", "").Trim();
            if (!string.IsNullOrEmpty(clean))
                results.Add(clean);
        }
        return results;
    }
}
