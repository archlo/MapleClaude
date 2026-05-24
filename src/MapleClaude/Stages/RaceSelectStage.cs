using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Character-creation race/class screen — the v95 <c>CUINewCharRaceSelect</c>
/// ("step 3"). Reached directly from CharSelect's "Create a character"; gender is
/// chosen later during appearance customisation (there is no separate gender
/// screen). The backdrop is <b>black</b> (the login map is NOT shown here) behind
/// the login <c>Common/frame</c>; the six job panels — Dual Blade, Explorer
/// (<c>BtNormal</c>), Cygnus (<c>BtKnight</c>), Aran, Evan, Resistance — sit at
/// their authentic dialog coordinates. Clicking a job opens the
/// <c>RaceSelect/confirm</c> dialog (race banner + OK/Cancel); OK proceeds to
/// creation.
///
/// Two race numberings: the <b>UI race id</b> (button order, used for
/// <c>confirm/race/&lt;id&gt;</c>) is Dual=0, Explorer=1, Cygnus=2, Aran=3,
/// Evan=4, Resistance=5; the <b>server race</b> (Kinoko <c>RaceSelect</c> enum,
/// sent in CreateNewCharacter) is Resistance=0, Explorer=1, Cygnus=2, Aran=3,
/// Evan=4 (see <see cref="ServerRace"/>).
/// </summary>
public sealed class RaceSelectStage : Stage
{
    private const string DebugCat = "RaceSelect";

    // UI race ids = CUINewCharRaceSelect button order (also confirm/race/<id> index).
    public const int RaceDualBlade = 0;
    public const int RaceExplorer = 1;
    public const int RaceCygnus = 2;
    public const int RaceAran = 3;
    public const int RaceEvan = 4;
    public const int RaceResistance = 5;

    private static readonly string[] RaceNames =
        ["Dual Blade", "Explorer", "Cygnus Knight", "Aran", "Evan", "Resistance"];

    // The confirm dialog banners (RaceSelect/confirm/race/<n>) use a different order than
    // the buttons: n = 0 Explorer, 1 Cygnus, 2 Aran, 3 Evan, 4 Dual Blade, 5 Resistance.
    // Map our UI race id (button order) -> that banner index.
    private static readonly int[] ConfirmBannerIndex = [4, 0, 1, 2, 3, 5];

    private readonly ILogger<RaceSelectStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;

    private WzSprite? _commonFrame;
    private WzSprite? _background;
    private WzSprite? _stepHeader;   // step/3 "Create a character"
    private WzSprite? _newBadge;     // RaceSelect/new (drawn on the newer classes)

    private Button? _btNormal;
    private Button? _btKnight;
    private Button? _btAran;
    private Button? _btEvan;
    private Button? _btResistance;
    private Button? _btDual;
    private Button? _btBack;
    private readonly List<Button> _allButtons = new();

    // Confirm dialog (RaceSelect/confirm): shown after a job is clicked.
    private WzSprite? _confirmBg;
    private readonly WzSprite?[] _confirmRace = new WzSprite?[6];
    private Button? _confirmOk;
    private Button? _confirmCancel;
    private int _pendingRace = -1;   // UI race id awaiting confirm, or -1

    // Authentic CUINewCharRaceSelect::OnCreate button positions (dialog/screen space).
    private Vector2 _btResistancePos = new(45, 43);
    private Vector2 _btDualPos = new(405, 43);
    private Vector2 _btNormalPos = new(580, 43);
    private Vector2 _btKnightPos = new(45, 295);
    private Vector2 _btAranPos = new(284, 295);
    private Vector2 _btEvanPos = new(524, 295);
    private Vector2 _stepHeaderPos = new(0, 0);
    private Vector2 _btBackPos = new(0, 546);
    private Vector2 _confirmPos = new(293, 244);   // (800-214)/2, ~centred

    public RaceSelectStage(
        ILogger<RaceSelectStage> logger,
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
        if (_ui is null) return;

        // No login map here — the race screen is a black backdrop behind the frame.
        _commonFrame = LoadCanvas("Login.img/Common/frame");
        _background = LoadCanvas("Login.img/RaceSelect/backgrnd");
        _stepHeader = LoadCanvas("Login.img/Common/step/3");
        _newBadge = LoadCanvas("Login.img/RaceSelect/new/0");

        _btNormal = MakeButton("Login.img/RaceSelect/BtNormal", () => Choose(RaceExplorer));
        _btKnight = MakeButton("Login.img/RaceSelect/BtKnight", () => Choose(RaceCygnus));
        _btAran = MakeButton("Login.img/RaceSelect/BtAran", () => Choose(RaceAran));
        _btEvan = MakeButton("Login.img/RaceSelect/BtEvan", () => Choose(RaceEvan));
        _btResistance = MakeButton("Login.img/RaceSelect/BtResistance", () => Choose(RaceResistance));
        _btDual = MakeButton("Login.img/RaceSelect/BtDual", () => Choose(RaceDualBlade));
        // Back button is NOT in _allButtons — it draws on top of the frame.
        _btBack = MakeStandaloneButton("Login.img/Common/BtStart", GoBack);

        // Confirm dialog assets.
        _confirmBg = LoadCanvas("Login.img/RaceSelect/confirm/backgrnd");
        for (var i = 0; i < _confirmRace.Length; i++)
            _confirmRace[i] = LoadCanvas($"Login.img/RaceSelect/confirm/race/{i}");
        _confirmOk = MakeStandaloneButton("Login.img/RaceSelect/confirm/BtOK", ConfirmOk);
        _confirmCancel = MakeStandaloneButton("Login.img/RaceSelect/confirm/BtCancel", ConfirmCancel);

        ApplyLayout();
        RegisterDebugItems();
        _logger.LogInformation("RaceSelectStage entered (6-job race select)");
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

        // Black backdrop (no login map), then the race art + panels, then the frame.
        sb.Draw(Game.WhitePixel, pp.Bounds, Color.Black);
        _background?.Draw(sb, Vector2.Zero);
        foreach (var b in _allButtons) b.Draw(sb);

        if (_newBadge != null)
        {
            _newBadge.Draw(sb, _btResistancePos + new Vector2(4, -6));
            _newBadge.Draw(sb, _btDualPos + new Vector2(4, -6));
            _newBadge.Draw(sb, _btEvanPos + new Vector2(4, -6));
        }

        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));
        _btBack?.Draw(sb);   // on top of the frame, not behind it
        _stepHeader?.Draw(sb, _stepHeaderPos);

        if (_pendingRace >= 0) DrawConfirm(sb);
    }

    private void DrawConfirm(SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        sb.Draw(Game.WhitePixel, pp.Bounds, new Color(0, 0, 0, 140));
        _confirmBg?.Draw(sb, _confirmPos);
        // Race-name banner, centred near the top of the dialog (above the baked
        // "Would you like to select this character type?" text). Uses the confirm
        // banner ordering, not the button order.
        var bannerIdx = ConfirmBannerIndex[_pendingRace];
        if (bannerIdx < _confirmRace.Length && _confirmRace[bannerIdx] is { } banner)
        {
            var bx = _confirmPos.X + (214 - banner.Width) / 2f + banner.Origin.X;
            banner.Draw(sb, new Vector2(bx, _confirmPos.Y + 10 + banner.Origin.Y));
        }
        _confirmOk?.Draw(sb);
        _confirmCancel?.Draw(sb);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        if (_pendingRace >= 0)
        {
            _confirmOk?.HandleMouseButton(x, y, down);
            _confirmCancel?.HandleMouseButton(x, y, down);
            return; // modal
        }
        if (_btBack?.HandleMouseButton(x, y, down) == true) return;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return;
    }

    public override void OnKeyPress(Keys key)
    {
        if (_pendingRace >= 0)
        {
            if (key == Keys.Enter) ConfirmOk();
            else if (key is Keys.Escape or Keys.Back) ConfirmCancel();
            return;
        }
        if (key is Keys.Back or Keys.Escape) GoBack();
    }

    private void Choose(int uiRace)
    {
        PlayClick();
        if (_sound?.GetItem("UI.img/RaceSelect") is WzSound s) Game.AudioPlayer.PlayEffect(s);
        _pendingRace = uiRace;
        _logger.LogInformation("RaceSelect: {Race} clicked — confirm", RaceNames[uiRace]);
    }

    private void ConfirmCancel()
    {
        PlayClick();
        _pendingRace = -1;
    }

    private void ConfirmOk()
    {
        var uiRace = _pendingRace;
        _pendingRace = -1;
        PlayClick();
        _logger.LogInformation("RaceSelect: {Race} confirmed (server race {Sr})",
            RaceNames[uiRace], ServerRace(uiRace));
        // Gender + appearance + name are chosen on the creation screen.
        Game.StageDirector.Replace(new CharCreationStage(
            _loggerFactory.CreateLogger<CharCreationStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, isMale: true, raceIndex: uiRace,
            _cameraStart, _loginCameraOffset));
    }

    private void GoBack()
    {
        PlayClick();
        Game.StageDirector.Replace(new CharSelectStage(
            _loggerFactory.CreateLogger<CharSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _cameraStart, _loginCameraOffset));
    }

    // UI race id -> Kinoko server race code (RaceSelect enum). Dual Blade has no
    // distinct server race; it is created as an Explorer (thief branch).
    public static int ServerRace(int uiRace) => uiRace switch
    {
        RaceResistance => 0,   // RESISTANCE (Citizen)
        RaceCygnus => 2,       // CYGNUS (Noblesse)
        RaceAran => 3,         // ARAN
        RaceEvan => 4,         // EVAN
        _ => 1,                // NORMAL (Explorer / Dual Blade)
    };

    private void ApplyLayout()
    {
        if (_btResistance != null) _btResistance.Position = _btResistancePos;
        if (_btDual != null) _btDual.Position = _btDualPos;
        if (_btNormal != null) _btNormal.Position = _btNormalPos;
        if (_btKnight != null) _btKnight.Position = _btKnightPos;
        if (_btAran != null) _btAran.Position = _btAranPos;
        if (_btEvan != null) _btEvan.Position = _btEvanPos;
        if (_btBack != null) _btBack.Position = _btBackPos;
        // Authentic CConfirmRaceDlg::OnCreate button positions (relative to the dialog).
        if (_confirmOk != null) _confirmOk.Position = _confirmPos + new Vector2(42, 77);
        if (_confirmCancel != null) _confirmCancel.Position = _confirmPos + new Vector2(114, 77);
    }

    private void PlayClick()
    {
        if (_sound?.GetItem("UI.img/BtMouseClick") is WzSound click)
            Game.AudioPlayer.PlayEffect(click);
    }

    private WzSprite? LoadCanvas(string path)
    {
        try { return _ui?.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null; }
        catch (Exception ex) { _logger.LogWarning(ex, "RaceSelect: missing canvas {Path}", path); return null; }
    }

    private Button? MakeButton(string path, Action onClick)
    {
        var b = MakeStandaloneButton(path, onClick);
        if (b != null) _allButtons.Add(b);
        return b;
    }

    private Button? MakeStandaloneButton(string path, Action onClick)
    {
        try
        {
            if (_ui!.GetItem(path) is not WzProperty root) return null;
            return new Button(_loader!, root) { OnClick = onClick };
        }
        catch (Exception ex) { _logger.LogWarning(ex, "RaceSelect: missing button {Path}", path); return null; }
    }

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "BtNormal", () => _btNormalPos, v => _btNormalPos = v));
        reg.Register(new DebugItem(DebugCat, "BtKnight", () => _btKnightPos, v => _btKnightPos = v));
        reg.Register(new DebugItem(DebugCat, "BtAran", () => _btAranPos, v => _btAranPos = v));
        reg.Register(new DebugItem(DebugCat, "BtEvan", () => _btEvanPos, v => _btEvanPos = v));
        reg.Register(new DebugItem(DebugCat, "BtResistance", () => _btResistancePos, v => _btResistancePos = v));
        reg.Register(new DebugItem(DebugCat, "BtDual", () => _btDualPos, v => _btDualPos = v));
        reg.Register(new DebugItem(DebugCat, "Step header", () => _stepHeaderPos, v => _stepHeaderPos = v));
        reg.Register(new DebugItem(DebugCat, "Back button", () => _btBackPos, v => _btBackPos = v));
        reg.Register(new DebugItem(DebugCat, "Confirm dialog", () => _confirmPos, v => _confirmPos = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var n in new[] { "BtNormal", "BtKnight", "BtAran", "BtEvan", "BtResistance", "BtDual", "Step header", "Back button", "Confirm dialog" })
            reg.Unregister(DebugCat, n);
    }
}
