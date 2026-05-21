using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Race/class-selection screen shown after gender select.
/// WZ: <c>Login.img/RaceSelect/</c> (BtAdventure, BtKnight, BtAran, BtCygnus, BtYes, BtNo).
/// Backspace returns to GenderSelectStage.
/// </summary>
public sealed class RaceSelectStage : Stage
{
    private const string DebugCat = "RaceSelect";

    private readonly ILogger<RaceSelectStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly bool _isMale;
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;
    private MapScene? _scene;

    private WzSprite? _commonFrame;
    private WzSprite? _background;
    private WzSprite? _signpost;

    private Button? _btAdventure;
    private Button? _btKnight;
    private Button? _btAran;
    private Button? _btCygnus;
    private Button? _btYes;
    private Button? _btNo;
    private readonly List<Button> _allButtons = new();

    private int _selectedRace = 0; // 0=Adventure,1=Knight,2=Aran,3=Cygnus

    private Vector2 _bgPos = new(400, 300);
    private Vector2 _signpostPos = new(400, 200);
    private Vector2 _btAdvPos = new(200, 380);
    private Vector2 _btKnightPos = new(320, 380);
    private Vector2 _btAranPos = new(440, 380);
    private Vector2 _btCygnusPos = new(560, 380);
    private Vector2 _btYesPos = new(370, 470);
    private Vector2 _btNoPos = new(460, 470);

    private static readonly string[] RaceNames = ["Explorer", "Cygnus Knight", "Aran", "Cygnus"];

    public RaceSelectStage(
        ILogger<RaceSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int worldId, int channelId, bool isMale,
        Vector2 cameraStart,
        Vector2 loginCameraOffset)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _worldId = worldId;
        _channelId = channelId;
        _isMale = isMale;
        _cameraStart = cameraStart;
        _loginCameraOffset = loginCameraOffset;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        if (_ui is null || _map is null) return;

        if (_ui.GetItem("MapLogin1.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);
            _scene.Camera = _cameraStart;
        }

        _commonFrame = LoadCanvas("Login.img/Common/frame");
        _background = LoadCanvas("Login.img/RaceSelect/backgrnd");

        // Signpost sprite shows which race is selected
        _signpost = LoadCanvas("Login.img/CharSelect/adventure/0");

        _btAdventure = MakeButton("Login.img/RaceSelect/BtAdventure", () => SelectRace(0));
        _btKnight = MakeButton("Login.img/RaceSelect/BtKnight", () => SelectRace(1));
        _btAran = MakeButton("Login.img/RaceSelect/BtAran", () => SelectRace(2));
        _btCygnus = MakeButton("Login.img/RaceSelect/BtCygnus", () => SelectRace(3));
        _btYes = MakeButton("Login.img/RaceSelect/BtYes", () => OnConfirm());
        _btNo = MakeButton("Login.img/RaceSelect/BtNo", () => GoBack());

        ApplyLayout();
        RegisterDebugItems();
        _logger.LogInformation("RaceSelectStage entered gender={Gender}", _isMale ? "M" : "F");
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
        // Update signpost based on selected race
        _signpost = _selectedRace switch
        {
            1 => LoadCanvas("Login.img/CharSelect/knight/0") ?? _signpost,
            2 => LoadCanvas("Login.img/CharSelect/aran/0") ?? _signpost,
            _ => LoadCanvas("Login.img/CharSelect/adventure/0") ?? _signpost,
        };
        ApplyLayout();
    }

    public override void Draw(GameTime gameTime, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;

        if (_scene != null)
            _scene.Draw(sb, Game.WhitePixel, w, h);
        else
            sb.Draw(Game.WhitePixel, pp.Bounds, new Color(10, 16, 36));

        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));
        _background?.Draw(sb, _bgPos);
        _signpost?.Draw(sb, _signpostPos);

        foreach (var b in _allButtons) b.Draw(sb);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return;
    }

    public override void OnKeyPress(Keys key)
    {
        if (key == Keys.Back) GoBack();
        if (key == Keys.Enter) OnConfirm();
    }

    private void SelectRace(int index)
    {
        _selectedRace = index;
        _logger.LogInformation("RaceSelect: {Race} selected", RaceNames[index]);
    }

    private void OnConfirm()
    {
        _logger.LogInformation("RaceSelect confirmed: {Race} gender={Gender}",
            RaceNames[_selectedRace], _isMale ? "M" : "F");
        Game.StageDirector.Replace(new CharCreationStage(
            _loggerFactory.CreateLogger<CharCreationStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _isMale, _selectedRace,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private void GoBack()
    {
        Game.StageDirector.Replace(new GenderSelectStage(
            _loggerFactory.CreateLogger<GenderSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private WzSprite? LoadCanvas(string path)
    {
        try { return _ui?.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null; }
        catch { return null; }
    }

    private Button? MakeButton(string path, Action onClick)
    {
        try
        {
            var root = _ui!.GetItem(path) as WzProperty;
            if (root is null) return null;
            var b = new Button(_loader!, root) { OnClick = onClick };
            _allButtons.Add(b);
            return b;
        }
        catch { return null; }
    }

    private void ApplyLayout()
    {
        if (_btAdventure != null) _btAdventure.Position = _btAdvPos;
        if (_btKnight != null) _btKnight.Position = _btKnightPos;
        if (_btAran != null) _btAran.Position = _btAranPos;
        if (_btCygnus != null) _btCygnus.Position = _btCygnusPos;
        if (_btYes != null) _btYes.Position = _btYesPos;
        if (_btNo != null) _btNo.Position = _btNoPos;
    }

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "Background", () => _bgPos, v => _bgPos = v));
        reg.Register(new DebugItem(DebugCat, "Signpost", () => _signpostPos, v => _signpostPos = v));
        reg.Register(new DebugItem(DebugCat, "BtAdventure", () => _btAdvPos, v => _btAdvPos = v));
        reg.Register(new DebugItem(DebugCat, "BtKnight", () => _btKnightPos, v => _btKnightPos = v));
        reg.Register(new DebugItem(DebugCat, "BtAran", () => _btAranPos, v => _btAranPos = v));
        reg.Register(new DebugItem(DebugCat, "BtCygnus", () => _btCygnusPos, v => _btCygnusPos = v));
        reg.Register(new DebugItem(DebugCat, "BtYes", () => _btYesPos, v => _btYesPos = v));
        reg.Register(new DebugItem(DebugCat, "BtNo", () => _btNoPos, v => _btNoPos = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var n in new[] { "Background", "Signpost", "BtAdventure", "BtKnight", "BtAran", "BtCygnus", "BtYes", "BtNo" })
            reg.Unregister(DebugCat, n);
    }
}
