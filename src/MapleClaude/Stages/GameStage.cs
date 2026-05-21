using MapleClaude.App;
using MapleClaude.Character;
using System.Linq;
using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.UI.Game;
using MapleClaude.Wz;
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

    private WzTextureLoader? _loader;

    // World
    private GameCamera _camera = null!;
    private CharLook? _player;
    private readonly List<NpcLook> _npcs = new();

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

    // Input state
    private bool _moveLeft;
    private bool _moveRight;
    private bool _jumpPressed;

    public GameStage(
        ILogger<GameStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        WzPackage? charWz,
        WzPackage? npcWz)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _charWz = charWz;
        _npcWz = npcWz;
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

        _statusBar.OnCommunity = () => _userList!.IsVisible = !_userList.IsVisible;

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

        Game.AudioPlayer.Stop();
        _logger.LogInformation("GameStage entered — player skin=0, {NpcCount} NPCs", _npcs.Count);
    }

    public override void OnExit()
    {
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Read held keys each frame (movement is frame-continuous)
        var kb = Keyboard.GetState();
        _moveLeft = kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left);
        _moveRight = kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right);
        _jumpPressed = kb.IsKeyDown(Keys.Space) || kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up);

        // Player + camera
        if (_player != null)
        {
            _player.Update(dt, _moveLeft, _moveRight, _jumpPressed);
            _camera.Target = _player.Position;
        }
        _camera.Update(dt);

        // NPCs
        foreach (var npc in _npcs) npc.Update(dt);

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

        // Map background (placeholder tiled green)
        sb.Draw(Game.WhitePixel, new Rectangle(0, 0, w, h), new Color(34, 85, 34));
        DrawGroundPlane(sb, w, h);

        // NPCs (world-space → screen)
        foreach (var npc in _npcs)
        {
            var sp = _camera.WorldToScreen(npc.Position);
            if (sp.X > -100 && sp.X < w + 100)
                npc.Draw(sb, Game.WhitePixel, sp);
        }

        // Player character
        if (_player != null)
        {
            var playerScreen = _camera.WorldToScreen(_player.Position);
            _player.Draw(sb, Game.WhitePixel, playerScreen);
        }

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
    }

    public override void OnTextInput(char ch) => _chatBar?.OnTextInput(ch);

    public override void OnKeyPress(Keys key)
    {
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.OnKeyPress(key); return; }

        foreach (var p in _panels)
            if (p.IsVisible && p.OnKeyPress(key)) return;

        switch (key)
        {
            case Keys.E: _equip!.IsVisible = !_equip.IsVisible; break;
            case Keys.I: _item!.IsVisible = !_item.IsVisible; break;
            case Keys.K: _skill!.IsVisible      = !_skill.IsVisible; break;
            case Keys.OemTilde:
            case Keys.F11: _keyConfig!.IsVisible = !_keyConfig.IsVisible; break;
            case Keys.S: _stats!.IsVisible = !_stats.IsVisible; break;
            case Keys.Q: _quest!.IsVisible = !_quest.IsVisible; break;
            case Keys.M: _miniMap!.IsVisible    = !_miniMap.IsVisible; break;
            case Keys.W: _worldMap!.IsVisible   = !_worldMap.IsVisible; break;
            case Keys.U: _userList!.IsVisible   = !_userList.IsVisible; break;
            case Keys.OemQuestion: // ? = channel select
            case Keys.F9: _channelSelect!.IsVisible = !_channelSelect.IsVisible; break;
        }
    }

    private void SpawnNpc(int id, Vector2 worldPos, string name)
    {
        var npc = new NpcLook(id, worldPos, Game.Font) { Name = name };
        npc.Load(_loader!, _npcWz);
        _npcs.Add(npc);
    }
}
