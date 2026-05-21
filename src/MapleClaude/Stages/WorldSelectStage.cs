using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Stages;

/// <summary>
/// World/server-select screen (UI placeholder, no packet exchange yet).
/// Two sub-screens:
/// <list type="bullet">
///   <item><b>WorldList</b> — the v95 world button row + start / VAC /
///     view-choice / notice banner. Clicking a world plays a click SFX
///     and switches to ChannelGrid.</item>
///   <item><b>ChannelGrid</b> — the channel selector overlay (5×4 grid
///     of channel buttons, world icon, population gauge, scroll bar,
///     go-world button). Backspace returns to WorldList.</item>
/// </list>
/// Backspace from WorldList returns to <see cref="LoginStage"/>.
/// </summary>
public sealed class WorldSelectStage : Stage
{
    private enum SubScreen { WorldList, ChannelGrid }

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

    // ---- Camera + scroll ----
    private Vector2 _cameraOffset;
    private float _scrollT;
    // 0.55 s — fast enough to feel like a real scroll, slow enough that the
    // motion registers visually. Tuned by feel.
    private const float ScrollDuration = 0.55f;

    // ---- WorldList layer assets ----
    private WzSprite? _commonFrame;
    private WzSprite? _stepIndicator;
    private WzSprite? _worldNotice;
    private Button? _btWorld0;
    private Button? _btWorldE;
    private Button? _btStart;
    private Button? _btVAC;
    private Button? _btViewChoice;

    // ---- ChannelGrid layer assets ----
    private WzSprite? _chBackgrn;
    private WzSprite? _chSelect;
    private WzSprite? _worldIcon;
    private WzSprite? _chGauge;
    private WzSprite? _chScroll;
    private Button? _btGoWorld;
    private readonly WzSprite?[] _channelSprites = new WzSprite?[20];

    // ---- WorldList layout tunables (screen-space; debug-window editable) ----
    private Vector2 _worldNoticePos = new(400, 50);
    private Vector2 _btWorld0Pos = new(320, 280);
    private Vector2 _btWorldEPos = new(440, 280);
    private Vector2 _btStartPos = new(400, 510);
    private Vector2 _btVACPos = new(700, 80);
    private Vector2 _btViewChoicePos = new(620, 80);

    // ---- ChannelGrid layout tunables ----
    // The chBackgrn panel is 448 × 233 in v95; centred on the 800 × 600
    // backbuffer that places its top-left at (176, 184). Element positions
    // below derive from the v95 reference layout where known:
    //   - Channel grid:  base (23, 93) inside the panel; step (66, 29);
    //                    5 columns × 4 rows for 20 channels.
    //   - BtGoworld:     (230, 43) inside the panel.
    //   - chgauge / world name draw at (260, 10) inside the panel.
    // Anything not pulled from the reference is a sensible guess and can
    // be tuned live via the debug window (drag-mode in screen space).
    private Vector2 _chBackgrnPos = new(400, 300);     // panel centre on 800×600
    private Vector2 _chSelectPos = new(400, 200);
    private Vector2 _worldIconPos = new(400, 220);
    private Vector2 _chGaugePos = new(436, 194);       // chBackgrn TL + (260, 10)
    private Vector2 _chScrollPos = new(580, 300);
    private Vector2 _btGoWorldPos = new(406, 227);     // chBackgrn TL + (230, 43)
    private Vector2 _channelGridBase = new(199, 277);  // chBackgrn TL + (23, 93)
    private Vector2 _channelGridStep = new(66, 29);    // 66 px column, 29 px row
    private const int GridCols = 5;                    // 5 cols × 4 rows = 20 channels

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
        // Inherit X from the user-tuned login camera; default Y scrolls up
        // by roughly one screen height. User can re-tune via debug.
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

        _commonFrame = LoadCanvas("Login.img/Common/frame");
        _stepIndicator = LoadCanvas("Login.img/Common/step/1");
        _worldNotice = LoadCanvas("Login.img/WorldNotice");

        _btWorld0 = MakeButton("Login.img/WorldSelect/BtWorld/0",
            () => OnWorldClicked(0));
        _btWorldE = MakeButton("Login.img/WorldSelect/BtWorld/e", () => { });
        if (_btWorldE != null)
        {
            _btWorldE.Enabled = false; // user manifest lists only e/disabled — render as disabled
        }
        _btStart = MakeButton("Login.img/Common/BtStart",
            () => _logger.LogInformation("BtStart clicked (no-op placeholder)"));
        if (_btStart != null)
        {
            _btStart.Enabled = false; // no world picked yet
        }
        _btVAC = MakeButton("Login.img/ViewAllChar/BtVAC",
            () => _logger.LogInformation("BtVAC (View All Char) clicked (no-op placeholder)"));
        _btViewChoice = MakeButton("Login.img/WorldSelect/BtViewChoice",
            () => _logger.LogInformation("BtViewChoice clicked (no-op placeholder)"));

        ApplyLayout();
        RegisterDebugItems();
        _logger.LogInformation(
            "WorldSelectStage entered — scrolling from {Start} to SP + {Offset}",
            _cameraStart, _cameraOffset);
    }

    public override void OnExit()
    {
        UnregisterDebugItems();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    public override void Update(GameTime gameTime)
    {
        if (_scene is null)
        {
            return;
        }
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
        var sp = _scene.StartPoint ?? Vector2.Zero;
        var target = sp + _cameraOffset;
        _scene.Camera = Vector2.Lerp(_cameraStart, target, SmoothStep(_scrollT));

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
        _commonFrame?.Draw(spriteBatch, new Vector2(w / 2f, h / 2f));
        _stepIndicator?.Draw(spriteBatch, new Vector2(72, 50));

        if (_subScreen == SubScreen.WorldList)
        {
            DrawWorldList(spriteBatch);
        }
        else
        {
            DrawChannelGrid(spriteBatch);
        }
    }

    private void DrawWorldList(SpriteBatch sb)
    {
        _worldNotice?.Draw(sb, _worldNoticePos);
        _btWorld0?.Draw(sb);
        _btWorldE?.Draw(sb);
        _btStart?.Draw(sb);
        _btVAC?.Draw(sb);
        _btViewChoice?.Draw(sb);
    }

    private void DrawChannelGrid(SpriteBatch sb)
    {
        // Dim the world list behind the panel for focus.
        var pp = GraphicsDevice.PresentationParameters;
        sb.Draw(Game.WhitePixel, pp.Bounds, new Color(0, 0, 0, 96));

        _chBackgrn?.Draw(sb, _chBackgrnPos);
        _chSelect?.Draw(sb, _chSelectPos);
        _worldIcon?.Draw(sb, _worldIconPos);
        _chGauge?.Draw(sb, _chGaugePos);
        _chScroll?.Draw(sb, _chScrollPos);

        for (var i = 0; i < _channelSprites.Length; i++)
        {
            var sprite = _channelSprites[i];
            if (sprite is null)
            {
                continue;
            }
            var col = i % GridCols;
            var row = i / GridCols;
            var pos = _channelGridBase + new Vector2(col * _channelGridStep.X, row * _channelGridStep.Y);
            sprite.Draw(sb, pos);
        }

        _btGoWorld?.Draw(sb);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }
        if (_subScreen == SubScreen.WorldList)
        {
            if (_btWorld0?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
            if (_btWorldE?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
            if (_btStart?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
            if (_btVAC?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
            if (_btViewChoice?.HandleMouseButton(x, y, down) == true)
            {
                return;
            }
        }
        else
        {
            _btGoWorld?.HandleMouseButton(x, y, down);
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
            _logger.LogInformation("Backspace pressed — leaving channel grid, back to world list");
            return;
        }
        _logger.LogInformation("Backspace pressed — returning to LoginStage");
        Game.StageDirector.Replace(new LoginStage(
            _loggerFactory.CreateLogger<LoginStage>(),
            _loggerFactory, _ui, _map, _sound));
    }

    private void OnWorldClicked(int worldId)
    {
        _selectedWorldId = worldId;
        _logger.LogInformation("World {WorldId} clicked — opening channel grid", worldId);
        if (_sound?.GetItem("UI.img/BtMouseClick") is WzSound click)
        {
            Game.AudioPlayer.PlayEffect(click);
        }
        if (!_channelAssetsLoaded)
        {
            LoadChannelAssets();
            _channelAssetsLoaded = true;
        }
        _subScreen = SubScreen.ChannelGrid;
    }

    private void LoadChannelAssets()
    {
        _chBackgrn = LoadCanvas("Login.img/WorldSelect/chBackgrn");
        _chSelect = LoadCanvas("Login.img/WorldSelect/channel/chSelect");
        _worldIcon = LoadCanvas("Login.img/WorldSelect/world/0");
        _chGauge = LoadCanvas("Login.img/WorldSelect/channel/chgauge");
        _chScroll = LoadCanvas("Login.img/WorldSelect/scroll/1");
        for (var i = 0; i < _channelSprites.Length; i++)
        {
            // 0–4 enabled, 5–19 disabled per the v95 manifest the user pasted.
            var state = i < 5 ? "normal" : "disabled";
            _channelSprites[i] = LoadCanvas($"Login.img/WorldSelect/channel/{i}/{state}");
        }
        _btGoWorld = MakeButton("Login.img/WorldSelect/BtGoworld", () =>
        {
            _logger.LogInformation("BtGoworld clicked — entering CharSelectStage world={W} ch={C}",
                _selectedWorldId, _selectedChannelId);
            Game.StageDirector.Replace(new CharSelectStage(
                _loggerFactory.CreateLogger<CharSelectStage>(),
                _loggerFactory, _ui, _map, _sound,
                _selectedWorldId, _selectedChannelId,
                _scene?.Camera ?? Vector2.Zero, _cameraOffset));
        });
        ApplyChannelLayout();
        _logger.LogInformation("ChannelGrid assets loaded");
    }

    private void ApplyLayout()
    {
        if (_btWorld0 != null)
        {
            _btWorld0.Position = _btWorld0Pos;
        }
        if (_btWorldE != null)
        {
            _btWorldE.Position = _btWorldEPos;
        }
        if (_btStart != null)
        {
            _btStart.Position = _btStartPos;
        }
        if (_btVAC != null)
        {
            _btVAC.Position = _btVACPos;
        }
        if (_btViewChoice != null)
        {
            _btViewChoice.Position = _btViewChoicePos;
        }
        ApplyChannelLayout();
    }

    private void ApplyChannelLayout()
    {
        if (_btGoWorld != null)
        {
            _btGoWorld.Position = _btGoWorldPos;
        }
    }

    // ---- Debug registry ----

    private const string WlCat = "WorldSelect";
    private const string ChCat = "ChannelGrid";

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(WlCat, "Camera offset (SP +)",
            () => _cameraOffset, v => _cameraOffset = v)
        { Draggable = false });

        reg.Register(new DebugItem(WlCat, "WorldNotice",
            () => _worldNoticePos, v => _worldNoticePos = v));
        reg.Register(new DebugItem(WlCat, "BtWorld 0",
            () => _btWorld0Pos, v => _btWorld0Pos = v));
        reg.Register(new DebugItem(WlCat, "BtWorld e",
            () => _btWorldEPos, v => _btWorldEPos = v));
        reg.Register(new DebugItem(WlCat, "BtStart",
            () => _btStartPos, v => _btStartPos = v));
        reg.Register(new DebugItem(WlCat, "BtVAC",
            () => _btVACPos, v => _btVACPos = v));
        reg.Register(new DebugItem(WlCat, "BtViewChoice",
            () => _btViewChoicePos, v => _btViewChoicePos = v));

        reg.Register(new DebugItem(ChCat, "chBackgrn",
            () => _chBackgrnPos, v => _chBackgrnPos = v));
        reg.Register(new DebugItem(ChCat, "chSelect (title)",
            () => _chSelectPos, v => _chSelectPos = v));
        reg.Register(new DebugItem(ChCat, "world icon",
            () => _worldIconPos, v => _worldIconPos = v));
        reg.Register(new DebugItem(ChCat, "chgauge",
            () => _chGaugePos, v => _chGaugePos = v));
        reg.Register(new DebugItem(ChCat, "scroll/1",
            () => _chScrollPos, v => _chScrollPos = v));
        reg.Register(new DebugItem(ChCat, "BtGoworld",
            () => _btGoWorldPos, v => _btGoWorldPos = v));
        reg.Register(new DebugItem(ChCat, "Grid base (ch/0)",
            () => _channelGridBase, v => _channelGridBase = v));
        reg.Register(new DebugItem(ChCat, "Grid step (col,row)",
            () => _channelGridStep, v => _channelGridStep = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Unregister(WlCat, "Camera offset (SP +)");
        reg.Unregister(WlCat, "WorldNotice");
        reg.Unregister(WlCat, "BtWorld 0");
        reg.Unregister(WlCat, "BtWorld e");
        reg.Unregister(WlCat, "BtStart");
        reg.Unregister(WlCat, "BtVAC");
        reg.Unregister(WlCat, "BtViewChoice");
        reg.Unregister(ChCat, "chBackgrn");
        reg.Unregister(ChCat, "chSelect (title)");
        reg.Unregister(ChCat, "world icon");
        reg.Unregister(ChCat, "chgauge");
        reg.Unregister(ChCat, "scroll/1");
        reg.Unregister(ChCat, "BtGoworld");
        reg.Unregister(ChCat, "Grid base (ch/0)");
        reg.Unregister(ChCat, "Grid step (col,row)");
    }

    // ---- Helpers ----

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
            var root = _ui!.GetItem(path) as WzProperty;
            if (root is null)
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
