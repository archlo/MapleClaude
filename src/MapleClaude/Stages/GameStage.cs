using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Net;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Map;
using MapleClaude.Render;
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

    // One-shot debug-log latch for the not-yet-implemented MobMove(227)
    // outgoing path; see comment block in Update.
    private bool _loggedMobMoveTodo;

    // Active NPC-dialog message type — answers must echo the type the server sent.
    private ScriptMessageType _dialogMsgType;

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
        _skill = new SkillBook(_loader, _ui, font);
        _stats = new StatsInfo(_loader, _ui, font);
        _quest = new QuestLog(_loader, _ui, font);
        _keyConfig = new KeyConfig(_loader, _ui, font);
        _optionMenu = new OptionMenu(_loader, _ui, font);
        _charInfo = new CharInfo(_loader, _ui, font);
        _npcTalk = new NpcTalk(_loader, _ui, font);
        _shop = new Shop(_loader, _ui, font);
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
        _miniMap.SetMapInfo("Maple Road", "Henesys", new Rectangle(-3000, -2000, 6000, 4000));

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
        _panels.Add(_charInfo);
        _panels.Add(_npcTalk);
        _panels.Add(_shop);
        _panels.Add(_notice);

        // ── New high-priority panels ─────────────────────────────────────────
        _worldMap      = new WorldMap     (_loader, _ui, font);
        _userList      = new UserList     (_loader, _ui, font);
        _channelSelect = new ChannelSelect(_loader, _ui, font);
        _messenger     = new StatusMessenger(font) { Position = new Vector2(10, 340) };
        _dmgNumbers    = new DamageNumber(font);

        _statusBar.OnCommunity = () => _userList!.IsVisible = !_userList.IsVisible;
        _chatBar!.OnSendChat = msg =>
        {
            if (Game.Session.IsConnected)
                Game.Session.Send(GameSender.UserChat(msg));
        };

        _channelSelect.OnChannelChange = ch =>
            _logger.LogInformation("Channel change requested: CH{Ch} — no packet yet", ch);

        _panels.Add(_worldMap);
        _panels.Add(_userList);
        _panels.Add(_channelSelect);
        _panels.Add(_messenger);

        // Demo messenger messages
        _messenger.ShowLoot("Blue Snail Shell");
        _messenger.ShowEXP(12);
        _messenger.ShowBuff("Magic Guard");

        // ── Network packet handler ────────────────────────────────────────────
        _netHandler = new GamePacketHandler(_loggerFactory.CreateLogger<GamePacketHandler>());

        _netHandler.OnStatChanged = stats =>
        {
            _charStats = stats;
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

        _netHandler.OnUserChat = (name, text, type) =>
        {
            var prefix = string.IsNullOrEmpty(name) ? string.Empty : $"[{name}] ";
            _chatBar?.AddLine(prefix + text);
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
                var npc = new NpcLook(args.TemplateId, new Vector2(args.X, args.Y), Game.Font);
                npc.ObjId = args.ObjId;
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
            other.LoadSprites(_loader!, _charWz);
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
                // 0=Equip 1=Consume 2=Install 3=Etc 4=Cash → panel tab 0-4
                switch (op.OpType)
                {
                    case 0: // NewItem
                        _item?.AddItem(new ItemInventory.InvItem
                        {
                            Id = op.ItemId, Name = $"Item {op.ItemId:D7}",
                            Quantity = 1, Tab = op.InvType,
                        });
                        _messenger?.ShowLoot($"Item {op.ItemId}");
                        break;
                    case 3: // DelItem — remove from correct slot/tab
                        break;
                    case 1: // Qty update
                        break;
                }
            }
        };

        fh.OnUserChat += args =>
            _chatBar?.AddLine($"[{args.CharId}] {args.Text}");

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

        // Subscribe to the channel-server SetField so we can load the real map
        // + spawn position when the migration handoff completes.
        Game.FieldHandlers.OnSetField += OnSetField;

        _logger.LogInformation("GameStage entered — awaiting SetField from channel");
    }

    public override void OnExit()
    {
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
        try
        {
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
            _logger.LogInformation("SetField processed — mapId={Map} portal={Portal}",
                args.Stat.PosMap, args.Stat.Portal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load field {Id}", args.Stat.PosMap);
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
                var charLookStance = _physics.Stance switch
                {
                    Stance.Jump   => CharLook.Stance.Jump,
                    Stance.Walk1  => CharLook.Stance.Walk1,
                    Stance.Walk2  => CharLook.Stance.Walk1,
                    // CharLook has no swing frames; Alert is the closest
                    // "weapon-ready" pose so the player visibly reacts.
                    Stance.Swing  => CharLook.Stance.Alert,
                    _             => CharLook.Stance.Stand1,
                };
                _player.UpdateFromPhysics(dt, charLookStance, _physics.FacingLeft);
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

        // Drops
        foreach (var drop in _drops.Values) drop.Update(dt);

        // Other players
        foreach (var other in _otherChars.Values) other.Update(dt);

        // Damage numbers
        _dmgNumbers?.Update(dt);

        // Feed MiniMap map bounds so the coordinate projection matches the camera
        if (_miniMap != null)
            _miniMap.SetMapInfo("Maple Road", "Henesys", _camera.MapBounds);

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

        // All other keys routed through KeyConfig bindings
        DispatchAction(_keyConfig!.GetAction(key));
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
            // Cash Shop
            case KeyConfig.KeyAction.CashShop:
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

    private static string JobName(int jobId) => jobId switch
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
            // Phase 4: client-chosen damage in a killable range. The v95 Kinoko
            // server trusts the client damage value (no server-side recompute),
            // so this is a placeholder until weapon + stat formulas land
            // (Phase 6/7). A 1-hit swing → one damage int per mob.
            var dmg = _attackRng.Next(30, 81);
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

        // Wire shape: byte fieldKey, int tickCount, short x, short y,
        // int dwDropID, int dwCliCrc. Per upstream
        // kinoko/handler/field/FieldHandler.handleDropPickUpRequest.
        var p = OutPacket.Of(InHeader.DropPickUpRequest);
        p.WriteByte(_fieldKey);     // bFieldKey — server compares to user.getFieldKey()
        p.WriteInt(0);              // update_time / tickCount
        p.WriteShort((short)playerPos.X);
        p.WriteShort((short)playerPos.Y);
        p.WriteInt(nearest.DropId);
        p.WriteInt(0);              // dwCliCrc — server reads but does not validate
        Game.Session.Send(p);
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
