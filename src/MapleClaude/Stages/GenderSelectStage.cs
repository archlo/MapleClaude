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
/// Gender-select screen shown after BtNew in CharSelect.
/// WZ: <c>Login.img/GenderSelect/</c> (BtMale, BtFemale, BtYes, BtNo, backgrnd).
/// Backspace or BtNo returns to CharSelectStage.
/// </summary>
public sealed class GenderSelectStage : Stage
{
    private const string DebugCat = "GenderSelect";

    private readonly ILogger<GenderSelectStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;
    private MapScene? _scene;

    private WzSprite? _commonFrame;
    private WzSprite? _background;
    private WzSprite? _maleSel;
    private WzSprite? _femaleSel;

    private Button? _btMale;
    private Button? _btFemale;
    private Button? _btYes;
    private Button? _btNo;
    private readonly List<Button> _allButtons = new();

    private bool _maleSelected = true;

    private Vector2 _bgPos = new(400, 300);
    private Vector2 _btMalePos = new(270, 300);
    private Vector2 _btFemalePos = new(530, 300);
    private Vector2 _btYesPos = new(370, 430);
    private Vector2 _btNoPos = new(460, 430);

    public GenderSelectStage(
        ILogger<GenderSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int worldId, int channelId,
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
        _background = LoadCanvas("Login.img/GenderSelect/backgrnd");
        _maleSel = LoadCanvas("Login.img/GenderSelect/male/0");
        _femaleSel = LoadCanvas("Login.img/GenderSelect/female/0");

        _btMale = MakeButton("Login.img/GenderSelect/BtMale",
            () => { _maleSelected = true; });
        _btFemale = MakeButton("Login.img/GenderSelect/BtFemale",
            () => { _maleSelected = false; });
        _btYes = MakeButton("Login.img/GenderSelect/BtYes", () => OnConfirm());
        _btNo = MakeButton("Login.img/GenderSelect/BtNo", () => GoBack());

        ApplyLayout();
        RegisterDebugItems();
        _logger.LogInformation("GenderSelectStage entered");
    }

    public override void OnExit()
    {
        UnregisterDebugItems();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    public override void Update(GameTime gameTime) => ApplyLayout();

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

        if (_maleSelected) _maleSel?.Draw(sb, _btMalePos);
        else _femaleSel?.Draw(sb, _btFemalePos);

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

    private void OnConfirm()
    {
        _logger.LogInformation("Gender confirmed: {Gender}", _maleSelected ? "Male" : "Female");
        Game.StageDirector.Replace(new RaceSelectStage(
            _loggerFactory.CreateLogger<RaceSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _maleSelected,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private void GoBack()
    {
        Game.StageDirector.Replace(new CharSelectStage(
            _loggerFactory.CreateLogger<CharSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private WzSprite? LoadCanvas(string path)
    {
        try { return _ui!.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null; }
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
        if (_btMale != null) _btMale.Position = _btMalePos;
        if (_btFemale != null) _btFemale.Position = _btFemalePos;
        if (_btYes != null) _btYes.Position = _btYesPos;
        if (_btNo != null) _btNo.Position = _btNoPos;
    }

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "Background", () => _bgPos, v => _bgPos = v));
        reg.Register(new DebugItem(DebugCat, "BtMale", () => _btMalePos, v => _btMalePos = v));
        reg.Register(new DebugItem(DebugCat, "BtFemale", () => _btFemalePos, v => _btFemalePos = v));
        reg.Register(new DebugItem(DebugCat, "BtYes", () => _btYesPos, v => _btYesPos = v));
        reg.Register(new DebugItem(DebugCat, "BtNo", () => _btNoPos, v => _btNoPos = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var n in new[] { "Background", "BtMale", "BtFemale", "BtYes", "BtNo" })
            reg.Unregister(DebugCat, n);
    }
}
