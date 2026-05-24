using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Domain;
using MapleClaude.Map;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Stages;

/// <summary>
/// World/server-select screen. Two sub-screens:
/// <list type="bullet">
///   <item><b>WorldList</b> — the v95 world-button grid. The server's worlds are
///     laid out in a 6-column grid (cell step 96×26) as the original v95 client
///     does, each slot drawing <c>WorldSelect/BtWorld/&lt;worldId&gt;</c> with an
///     optional world-state flag overlaid at (+78, +6).</item>
///   <item><b>ChannelGrid</b> — the channel selector panel (<c>chBackgrn</c>,
///     371×222). Channels are a 5-column grid; cell rect is
///     <c>left = 66·(i%5)+23, top = 29·(i/5)+93</c> (61×21). The go-world button
///     sits at (230, 43) inside the panel.</item>
/// </list>
/// All layout offsets below are the authentic v95 client values; only the two
/// panel anchors (screen-space) are tunable via the debug window.
/// </summary>
public sealed class WorldSelectStage : Stage
{
    private enum SubScreen { WorldList, ChannelGrid }

    // ---- Authentic v95 layout constants (world select / channel select) ----
    private const int WorldGridCols = 6;                       // 96·(i%6), 26·(i/6)
    private static readonly Vector2 WorldGridStep = new(96, 26);
    // World-state flag offset from a world button. The flag draws on the world-info
    // layer (itself at dialog + RelMove(-10,-10)) at (+78,+6) per slot, i.e. (+68,-4)
    // relative to the button. Per CUIWorldSelect::DrawWorldItems (IDA-verified).
    private static readonly Vector2 WorldFlagOffset = new(68, -4);
    // World-button 0 in login-MAP coordinates. The buttons are direct children of the
    // CUIWorldSelect dialog (CreateDlg(-249,-862), bScreenCoord=0): CCtrlWnd::CreateCtrl
    // places each at (96·col, 26·row) relative to the dialog's own layer, so button 0 is
    // the dialog's top-left. (The RelMove(-10,-10) in OnCreate offsets the separate
    // world-state-icon layer, NOT the buttons.) The signboard sits at map (31,-808) in
    // the same space, so the buttons land on the board via the camera.
    private static readonly Vector2 WorldGridOriginMap = new(-249, -862);
    private const int ChannelGridCols = 5;                     // 66·(i%5)+23, 29·(i/5)+93
    private static readonly Vector2 ChannelGridBase = new(23, 93);
    private static readonly Vector2 ChannelGridStep = new(66, 29);
    private static readonly Point ChannelCellSize = new(61, 21);
    private static readonly Vector2 GoWorldOffset = new(230, 43);

    private readonly ILogger<WorldSelectStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly Vector2 _cameraStart;

    private WzTextureLoader? _loader;
    private MapScene? _scene;
    private SubScreen _subScreen = SubScreen.WorldList;
    private bool _channelAssetsLoaded;
    private int _selectedWorldId;
    private int _selectedChannelId;

    // ---- Network state ----
    private string _statusLabel = "Loading worlds...";
    private readonly List<WorldInfo> _worlds = new();
    private bool _worldsDirty;

    // ---- Camera + scroll ----
    private Vector2 _cameraOffset;
    private float _scrollT;
    private const float ScrollDuration = 0.55f;

    // ---- Shared chrome ----
    private WzSprite? _frame;
    private WzSprite? _stepIndicator;
    private WzSprite? _selectWorldTitle;

    // ---- WorldList layer assets ----
    // The board the world list sits on (Map/Obj/login.img/WorldSelect/signboard) and
    // the world decorations are rendered natively by MapScene from the login map's
    // object layers — the world buttons below are placed in the same map space.
    private readonly List<Button> _worldButtons = new();
    private readonly List<WzSprite?> _worldFlags = new();
    private Button? _btExit;   // back to title/login (Common/BtExit; client's GotoTitle)

    // ---- ChannelGrid layer assets ----
    private WzSprite? _chBackgrn;
    private AnimatedSprite? _chSelect;
    private WzSprite? _worldBanner;
    private WzSprite? _chGauge;
    private Button? _btGoWorld;
    private readonly WzSprite?[] _channelNormal = new WzSprite?[20];
    private readonly WzSprite?[] _channelDisabled = new WzSprite?[20];

    // ---- Tunable knobs (debug-window editable) ----
    // World buttons are placed at WorldGridOriginMap in login-map space; this nudge
    // (map units) is only for fine adjustment — default 0 (authentic).
    private Vector2 _worldGridNudge = Vector2.Zero;
    private Vector2 _channelPanelAnchor = new(214, 189); // (800-371)/2, (600-222)/2
    private Vector2 _worldBannerOffset = new(12, 6);      // world banner inside panel
    private Vector2 _chSelectOffset = new(-2, -3);        // highlight centred over cell
    private Vector2 _chGaugeOffset = new(2, 15);          // gauge inside cell
    private Vector2 _btExitPos = new(0, 546);             // BtExit screen-space, CUILoginStart (0,546)

    public WorldSelectStage(
        ILogger<WorldSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        Vector2 cameraStart,
        Vector2 loginCameraOffset)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _cameraStart = cameraStart;
        _cameraOffset = new Vector2(loginCameraOffset.X, -608);
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        if (_ui is null || _map is null)
        {
            _logger.LogError("WorldSelectStage: WZ packages unavailable (ui={UiOk}, map={MapOk})",
                _ui is not null, _map is not null);
            return;
        }

        if (_ui.GetItem("MapLogin1.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);
            _scene.Camera = _cameraStart;
        }

        _frame = LoadCanvas("Login.img/Common/frame");
        _stepIndicator = LoadCanvas("Login.img/Common/step/1");
        _selectWorldTitle = LoadCanvas("Login.img/Common/selectWorld");
        _btExit = MakeButton("Login.img/Common/BtExit", GoBackToLogin);

        RegisterDebugItems();

        Game.LoginHandlers.OnWorldListComplete += OnWorldListComplete;
        Game.LoginHandlers.OnSelectWorldResult += OnSelectWorldResult;
        Game.Session.Disconnected += OnDisconnected;

        // WorldInfoRequest(4) — server replies with one WorldInformation per world.
        Game.Session.Send(OutPacket.Of(InHeader.WorldInfoRequest));

        _logger.LogInformation(
            "WorldSelectStage entered — scrolling from {Start} to SP + {Offset}",
            _cameraStart, _cameraOffset);
    }

    public override void OnExit()
    {
        UnregisterDebugItems();
        Game.LoginHandlers.OnWorldListComplete -= OnWorldListComplete;
        Game.LoginHandlers.OnSelectWorldResult -= OnSelectWorldResult;
        Game.Session.Disconnected -= OnDisconnected;
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    private void OnWorldListComplete(List<WorldInfo> worlds)
    {
        _worlds.Clear();
        _worlds.AddRange(worlds);
        _worldsDirty = true; // rebuild buttons on the game thread in Update
        _statusLabel = worlds.Count == 0 ? "No worlds available." : string.Empty;
        _logger.LogInformation("World list ready: {N} worlds", worlds.Count);
    }

    private void OnSelectWorldResult(SelectWorldResultArgs args)
    {
        if (!args.Success)
        {
            _statusLabel = $"World select failed (code {args.ResultCode}).";
            return;
        }
        _statusLabel = $"Character list: {args.Characters.Count} characters.";
        Game.Session.Characters.Clear();
        Game.Session.Characters.AddRange(args.Characters);
        Game.StageDirector.Replace(new CharSelectStage(
            _loggerFactory.CreateLogger<CharSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _selectedWorldId, _selectedChannelId,
            _scene?.Camera ?? Vector2.Zero, _cameraOffset));
    }

    private void OnDisconnected(Exception? cause)
    {
        _ = cause;
        _statusLabel = "Disconnected.";
    }

    public override void Update(GameTime gameTime)
    {
        if (_worldsDirty)
        {
            BuildWorldButtons();
            _worldsDirty = false;
        }

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _chSelect?.Update(gameTime.ElapsedGameTime.TotalMilliseconds);

        if (_scene is not null)
        {
            _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
            var sp = _scene.StartPoint ?? Vector2.Zero;
            var target = sp + _cameraOffset;
            _scene.Camera = Vector2.Lerp(_cameraStart, target, SmoothStep(_scrollT));
        }

        ApplyLayout();
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;

        if (_scene != null)
        {
            _scene.Draw(spriteBatch, Game.WhitePixel, w, h);
        }
        else
        {
            spriteBatch.Draw(Game.WhitePixel, pp.Bounds, new Color(10, 16, 36));
        }

        _frame?.Draw(spriteBatch, new Vector2(w / 2f, h / 2f));
        _stepIndicator?.Draw(spriteBatch, new Vector2(72, 50));
        _selectWorldTitle?.Draw(spriteBatch, new Vector2(w / 2f - 46, 26));

        if (_subScreen == SubScreen.WorldList)
        {
            DrawWorldList(spriteBatch);
        }
        else
        {
            DrawChannelGrid(spriteBatch);
        }

        if (!string.IsNullOrEmpty(_statusLabel) && Game.Font is not null)
        {
            var size = Game.Font.Measure(_statusLabel);
            Game.Font.Draw(spriteBatch, _statusLabel,
                new Vector2(w / 2f - size.X / 2f, h - 30), Color.White);
        }
    }

    private void DrawWorldList(SpriteBatch sb)
    {
        // The signboard + world decorations are drawn by MapScene (login-map obj layers).
        for (var i = 0; i < _worldButtons.Count; i++)
        {
            _worldButtons[i].Draw(sb);
            var flag = i < _worldFlags.Count ? _worldFlags[i] : null;
            flag?.Draw(sb, _worldButtons[i].Position + WorldFlagOffset);
        }
        _btExit?.Draw(sb);
    }

    private void DrawChannelGrid(SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        sb.Draw(Game.WhitePixel, pp.Bounds, new Color(0, 0, 0, 96));

        _chBackgrn?.Draw(sb, _channelPanelAnchor);
        _worldBanner?.Draw(sb, _channelPanelAnchor + _worldBannerOffset);

        var channelCount = SelectedWorld?.Channels.Count ?? 0;
        var maxUsers = MaxChannelUsers();

        for (var i = 0; i < _channelNormal.Length; i++)
        {
            var enabled = i < channelCount;
            var sprite = enabled ? _channelNormal[i] : _channelDisabled[i];
            if (sprite is null)
            {
                continue;
            }
            var cell = ChannelCellTopLeft(i);
            sprite.Draw(sb, cell);

            if (enabled)
            {
                DrawGauge(sb, cell + _chGaugeOffset, ChannelUsers(i), maxUsers);
                if (i == _selectedChannelId)
                {
                    _chSelect?.Draw(sb, cell + _chSelectOffset);
                }
            }
        }

        _btGoWorld?.Draw(sb);
    }

    private void DrawGauge(SpriteBatch sb, Vector2 pos, int users, int maxUsers)
    {
        if (_chGauge is null)
        {
            return;
        }
        var frac = maxUsers <= 0 ? 0f : Math.Clamp(users / (float)maxUsers, 0f, 1f);
        var fillW = (int)(_chGauge.Width * frac);
        if (fillW <= 0)
        {
            return;
        }
        var src = new Rectangle(0, 0, fillW, _chGauge.Height);
        sb.Draw(_chGauge.Texture, pos - _chGauge.Origin, src, Color.White);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_subScreen == SubScreen.WorldList)
        {
            if (_btExit?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
            foreach (var bt in _worldButtons)
            {
                if (bt.HandleMouseButton(x, y, down))
                {
                    return;
                }
            }
            return;
        }

        if (_btGoWorld?.HandleMouseButton(x, y, down) == true)
        {
            return;
        }
        if (down)
        {
            var channelCount = SelectedWorld?.Channels.Count ?? 0;
            for (var i = 0; i < channelCount; i++)
            {
                var cell = ChannelCellTopLeft(i);
                var rect = new Rectangle((int)cell.X, (int)cell.Y, ChannelCellSize.X, ChannelCellSize.Y);
                if (rect.Contains(x, y))
                {
                    OnChannelClicked(i);
                    return;
                }
            }
        }
    }

    public override void OnKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        if (key != Microsoft.Xna.Framework.Input.Keys.Back)
        {
            return;
        }
        if (_subScreen == SubScreen.ChannelGrid)
        {
            _subScreen = SubScreen.WorldList;
            _logger.LogInformation("Backspace — leaving channel grid, back to world list");
            return;
        }
        GoBackToLogin();
    }

    // Back from the world list returns to the title/login screen — the client's
    // CLogin::GotoTitle path (BtExit click or ESC/Backspace at the base step).
    private void GoBackToLogin()
    {
        _logger.LogInformation("Back — returning to LoginStage (GotoTitle)");
        Game.StageDirector.Replace(new LoginStage(
            _loggerFactory.CreateLogger<LoginStage>(),
            _loggerFactory, _ui, _map, _sound));
    }

    // ---- World list ----

    private void BuildWorldButtons()
    {
        if (_loader is null || _ui is null)
        {
            return;
        }
        _worldButtons.Clear();
        _worldFlags.Clear();

        foreach (var world in _worlds)
        {
            var id = world.WorldId;
            var worldId = id; // capture for closure
            var bt = MakeButton($"Login.img/WorldSelect/BtWorld/{id}", () => OnWorldClicked(worldId));
            if (bt is null)
            {
                continue;
            }
            _worldButtons.Add(bt);
            // World-state flag overlay (1=new, 2=hot, 3=event, …); first frame is enough.
            _worldFlags.Add(world.State > 0
                ? LoadCanvas($"Login.img/WorldNotice/{world.State}/0")
                : null);
        }

        _logger.LogInformation("Built {N} world buttons", _worldButtons.Count);
    }

    private void OnWorldClicked(int worldId)
    {
        _selectedWorldId = worldId;
        _selectedChannelId = 0;
        _logger.LogInformation("World {WorldId} clicked — opening channel grid", worldId);
        PlayClick();
        if (!_channelAssetsLoaded)
        {
            LoadChannelAssets();
            _channelAssetsLoaded = true;
        }
        _worldBanner = LoadCanvas($"Login.img/WorldSelect/world/{worldId}");
        _subScreen = SubScreen.ChannelGrid;
    }

    // ---- Channel grid ----

    private void OnChannelClicked(int channelId)
    {
        if (channelId == _selectedChannelId)
        {
            EnterWorld(); // second click on the selected channel enters
            return;
        }
        _selectedChannelId = channelId;
        PlayClick();
    }

    private void EnterWorld()
    {
        _statusLabel = $"Joining world {_selectedWorldId} ch {_selectedChannelId}...";
        // SelectWorld(5): byte gameStartMode=2, byte worldId, byte channelId, int unk=0.
        var p = OutPacket.Of(InHeader.SelectWorld);
        p.WriteByte(2);
        p.WriteByte((byte)_selectedWorldId);
        p.WriteByte((byte)_selectedChannelId);
        p.WriteInt(0);
        Game.Session.Send(p);
        Game.Session.Account.SelectedWorldId = (byte)_selectedWorldId;
        Game.Session.Account.SelectedChannelId = (byte)_selectedChannelId;
    }

    private void LoadChannelAssets()
    {
        _chBackgrn = LoadCanvas("Login.img/WorldSelect/chBackgrn");
        _chGauge = LoadCanvas("Login.img/WorldSelect/channel/chgauge");
        _chSelect = _loader?.LoadAnimation(_ui?.GetItem("Login.img/WorldSelect/channel/chSelect"));
        for (var i = 0; i < _channelNormal.Length; i++)
        {
            _channelNormal[i] = LoadCanvas($"Login.img/WorldSelect/channel/{i}/normal");
            _channelDisabled[i] = LoadCanvas($"Login.img/WorldSelect/channel/{i}/disabled");
        }
        _btGoWorld = MakeButton("Login.img/WorldSelect/BtGoworld", EnterWorld);
        _logger.LogInformation("ChannelGrid assets loaded");
    }

    private Vector2 ChannelCellTopLeft(int idx)
    {
        var col = idx % ChannelGridCols;
        var row = idx / ChannelGridCols;
        return _channelPanelAnchor + ChannelGridBase
            + new Vector2(col * ChannelGridStep.X, row * ChannelGridStep.Y);
    }

    private WorldInfo? SelectedWorld =>
        _worlds.FirstOrDefault(w => w.WorldId == _selectedWorldId);

    private int ChannelUsers(int idx)
    {
        var w = SelectedWorld;
        return w is not null && idx < w.Channels.Count ? w.Channels[idx].UserCount : 0;
    }

    private int MaxChannelUsers()
    {
        var w = SelectedWorld;
        if (w is null || w.Channels.Count == 0)
        {
            return 0;
        }
        var max = 0;
        foreach (var ch in w.Channels)
        {
            if (ch.UserCount > max)
            {
                max = ch.UserCount;
            }
        }
        return max;
    }

    // ---- Layout ----

    private void ApplyLayout()
    {
        // World buttons live in login-map space and ride the camera, so they sit on
        // the map-rendered signboard regardless of scroll.
        if (_scene is not null && _worldButtons.Count > 0)
        {
            var pp = GraphicsDevice.PresentationParameters;
            var origin = WorldGridOriginMap + _worldGridNudge;
            for (var i = 0; i < _worldButtons.Count; i++)
            {
                var col = i % WorldGridCols;
                var row = i / WorldGridCols;
                var mapPos = origin + new Vector2(col * WorldGridStep.X, row * WorldGridStep.Y);
                _worldButtons[i].Position = _scene.WorldToScreen(mapPos, pp.BackBufferWidth, pp.BackBufferHeight);
            }
        }
        if (_btExit != null)
        {
            _btExit.Position = _btExitPos;
        }
        if (_btGoWorld != null)
        {
            _btGoWorld.Position = _channelPanelAnchor + GoWorldOffset;
        }
    }

    // ---- Debug registry ----

    private const string WlCat = "WorldSelect";
    private const string ChCat = "ChannelGrid";

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(WlCat, "Camera offset (SP +)",
            () => _cameraOffset, v => _cameraOffset = v) { Draggable = false });
        reg.Register(new DebugItem(WlCat, "World grid nudge (map)",
            () => _worldGridNudge, v => _worldGridNudge = v) { Draggable = false });
        reg.Register(new DebugItem(WlCat, "BtExit (back)",
            () => _btExitPos, v => _btExitPos = v));

        reg.Register(new DebugItem(ChCat, "Panel anchor (chBackgrn TL)",
            () => _channelPanelAnchor, v => _channelPanelAnchor = v));
        reg.Register(new DebugItem(ChCat, "World banner offset",
            () => _worldBannerOffset, v => _worldBannerOffset = v));
        reg.Register(new DebugItem(ChCat, "chSelect offset",
            () => _chSelectOffset, v => _chSelectOffset = v));
        reg.Register(new DebugItem(ChCat, "Gauge offset",
            () => _chGaugeOffset, v => _chGaugeOffset = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Unregister(WlCat, "Camera offset (SP +)");
        reg.Unregister(WlCat, "World grid nudge (map)");
        reg.Unregister(WlCat, "BtExit (back)");
        reg.Unregister(ChCat, "Panel anchor (chBackgrn TL)");
        reg.Unregister(ChCat, "World banner offset");
        reg.Unregister(ChCat, "chSelect offset");
        reg.Unregister(ChCat, "Gauge offset");
    }

    // ---- Helpers ----

    private void PlayClick()
    {
        if (_sound?.GetItem("UI.img/BtMouseClick") is WzSound click)
        {
            Game.AudioPlayer.PlayEffect(click);
        }
    }

    private WzSprite? LoadCanvas(string path)
    {
        try
        {
            return _ui!.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorldSelectStage: missing UI canvas {Path}", path);
            return null;
        }
    }

    private Button? MakeButton(string path, Action onClick)
    {
        try
        {
            if (_ui!.GetItem(path) is not WzProperty root)
            {
                _logger.LogWarning("WorldSelectStage: missing button property {Path}", path);
                return null;
            }
            return new Button(_loader!, root) { OnClick = onClick };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorldSelectStage: failed to construct button {Path}", path);
            return null;
        }
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
