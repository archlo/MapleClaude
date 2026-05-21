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
/// Character creation stage. Covers UICommonCreation + UIExplorerCreation (and variants).
/// Flow: Gender → Race → THIS STAGE (appearance customisation → name entry → confirm)
///
/// Sub-screens:
///   Appearance  — left/right cycle face, hair, hairColour, skin; preview avatar
///   NameEntry   — text field for character name + validate
///   Confirm     — yes/no review screen
///
/// WZ paths:
///   Login.img/NewChar/        — main creation background and avatar board
///   Login.img/NewChar/BtYes   — confirm button
///   Login.img/NewChar/BtNo    — back button
///   Login.img/NewChar/BtPrev, BtNext — appearance cycling
///   Login.img/NewChar/nameboard — name input panel
///
/// Backspace returns to RaceSelectStage.
/// </summary>
public sealed class CharCreationStage : Stage
{
    private const string DebugCat = "CharCreate";

    // ── Appearance options (placeholder counts — real data comes from server) ──
    private static readonly int FaceCount     = 8;
    private static readonly int HairCount     = 8;
    private static readonly int HairColorCount = 8;
    private static readonly int SkinCount     = 4;

    private enum SubScreen { Appearance, NameEntry, Confirm }

    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly ILogger<CharCreationStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly bool _isMale;
    private readonly int _raceIndex;
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;
    private MapScene? _scene;

    // ── WZ sprites ────────────────────────────────────────────────────────────
    private WzSprite? _commonFrame;
    private WzSprite? _avatarBoard;
    private WzSprite? _nameBoard;
    private WzSprite? _confirmBoard;

    // ── Buttons ───────────────────────────────────────────────────────────────
    private Button? _btYes;
    private Button? _btNo;
    private Button? _btFaceL,   _btFaceR;
    private Button? _btHairL,   _btHairR;
    private Button? _btColorL,  _btColorR;
    private Button? _btSkinL,   _btSkinR;
    private Button? _btRandom;
    private readonly Button?[] _hairColorBtns = new Button?[8];
    private readonly List<Button> _allButtons = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private SubScreen _subScreen = SubScreen.Appearance;
    private int _faceIdx;
    private int _hairIdx;
    private int _hairColorIdx;
    private int _skinIdx;
    private TextField? _nameField;
    private string _nameError = string.Empty;

    // ── Layout tunables ───────────────────────────────────────────────────────
    private Vector2 _avatarBoardPos = new(250, 300);
    private Vector2 _nameBoardPos   = new(400, 350);
    private Vector2 _confirmPos     = new(400, 350);
    private Vector2 _cameraOffset   = new(27, -1216);

    private float _scrollT;
    private const float ScrollDuration = 0.55f;

    public CharCreationStage(
        ILogger<CharCreationStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int worldId, int channelId,
        bool isMale, int raceIndex,
        Vector2 cameraStart,
        Vector2 loginCameraOffset)
    {
        _logger             = logger;
        _loggerFactory      = loggerFactory;
        _ui                 = ui;
        _map                = map;
        _sound              = sound;
        _worldId            = worldId;
        _channelId          = channelId;
        _isMale             = isMale;
        _raceIndex          = raceIndex;
        _cameraStart        = cameraStart;
        _loginCameraOffset  = loginCameraOffset;
    }

    // ── Stage lifecycle ───────────────────────────────────────────────────────

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

        _commonFrame = LoadC("Login.img/Common/frame");
        _avatarBoard = LoadC("Login.img/NewChar/board") ?? LoadC("Login.img/CharSelect/charInfo1");
        _nameBoard   = LoadC("Login.img/NewChar/nameboard");
        _confirmBoard = LoadC("Login.img/NewChar/board");

        var nc = _ui.GetItem("Login.img/NewChar") as WzProperty;

        _btYes    = MakeBtn(nc, "BtYes",     () => OnYes());
        _btNo     = MakeBtn(nc, "BtNo",      () => OnNo());
        _btFaceL  = MakeBtn(nc, "BtLeft0",   () => Cycle(ref _faceIdx,     -1, FaceCount));
        _btFaceR  = MakeBtn(nc, "BtRight0",  () => Cycle(ref _faceIdx,     +1, FaceCount));
        _btHairL  = MakeBtn(nc, "BtLeft1",   () => Cycle(ref _hairIdx,     -1, HairCount));
        _btHairR  = MakeBtn(nc, "BtRight1",  () => Cycle(ref _hairIdx,     +1, HairCount));
        _btColorL = MakeBtn(nc, "BtLeft2",   () => Cycle(ref _hairColorIdx,-1, HairColorCount));
        _btColorR = MakeBtn(nc, "BtRight2",  () => Cycle(ref _hairColorIdx,+1, HairColorCount));
        _btSkinL  = MakeBtn(nc, "BtLeft3",   () => Cycle(ref _skinIdx,     -1, SkinCount));
        _btSkinR  = MakeBtn(nc, "BtRight3",  () => Cycle(ref _skinIdx,     +1, SkinCount));
        _btRandom = MakeBtn(nc, "BtRandom",  () => Randomize());

        // Hair colour swatches
        for (var i = 0; i < 8; i++)
        {
            var idx = i;
            var pr  = (nc?.Get($"BtHairColor{i}") as WzProperty);
            if (pr != null)
            {
                _hairColorBtns[i] = new Button(_loader!, pr) { OnClick = () => _hairColorIdx = idx };
                _allButtons.Add(_hairColorBtns[i]!);
            }
        }

        _nameField = new TextField
        {
            Position  = _nameBoardPos + new Vector2(-70, 0),
            Width     = 140,
            Height    = 20,
            Font      = Game.Font,
            MaxLength = 12,
        };
        _nameField.IsFocused = false;

        ApplyLayout();
        RegisterDebug();
        _logger.LogInformation("CharCreationStage: world={W} ch={C} male={M} race={R}",
            _worldId, _channelId, _isMale, _raceIndex);
    }

    public override void OnExit()
    {
        UnregisterDebug();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        if (_scene != null)
        {
            var dt = (float)gt.ElapsedGameTime.TotalSeconds;
            _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
            var sp = _scene.StartPoint ?? Vector2.Zero;
            _scene.Camera = Vector2.Lerp(_cameraStart, sp + _cameraOffset, SmoothStep(_scrollT));
        }
        _nameField?.Update(gt);
        ApplyLayout();
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(GameTime gt, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w  = pp.BackBufferWidth;
        var h  = pp.BackBufferHeight;

        if (_scene != null)
            _scene.Draw(sb, Game.WhitePixel, w, h);
        else
            sb.Draw(Game.WhitePixel, pp.Bounds, new Color(10, 16, 36));

        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));

        switch (_subScreen)
        {
            case SubScreen.Appearance: DrawAppearance(sb, w, h); break;
            case SubScreen.NameEntry:  DrawNameEntry (sb, w, h); break;
            case SubScreen.Confirm:    DrawConfirm   (sb, w, h); break;
        }
    }

    private void DrawAppearance(SpriteBatch sb, int w, int h)
    {
        // Avatar board
        _avatarBoard?.Draw(sb, _avatarBoardPos);
        if (_avatarBoard is null)
        {
            sb.Draw(Game.WhitePixel, new Rectangle((int)_avatarBoardPos.X - 80, (int)_avatarBoardPos.Y - 120, 160, 240), new Color(16, 20, 35));
        }

        // Placeholder avatar silhouette
        var av = _avatarBoardPos;
        sb.Draw(Game.WhitePixel, new Rectangle((int)av.X - 20, (int)av.Y - 90, 40, 80), new Color(60, 40, 80, 180));
        sb.Draw(Game.WhitePixel, new Rectangle((int)av.X - 14, (int)av.Y - 110, 28, 24), new Color(220, 180, 140, 200));

        // Appearance stats
        if (Game.Font != null)
        {
            var lx = _avatarBoardPos.X + 100;
            var ly = _avatarBoardPos.Y - 100f;
            DrawAppRow(sb, lx, ref ly, "Face",       _faceIdx + 1);
            DrawAppRow(sb, lx, ref ly, "Hair",       _hairIdx + 1);
            DrawAppRow(sb, lx, ref ly, "Hair Color", _hairColorIdx + 1);
            DrawAppRow(sb, lx, ref ly, "Skin",       _skinIdx + 1);
        }

        foreach (var b in _allButtons) b.Draw(sb);
        _btYes?.Draw(sb);
        _btNo?.Draw(sb);
        _btRandom?.Draw(sb);
    }

    private void DrawNameEntry(SpriteBatch sb, int w, int h)
    {
        _nameBoard?.Draw(sb, _nameBoardPos);
        if (_nameBoard is null)
        {
            sb.Draw(Game.WhitePixel, new Rectangle((int)_nameBoardPos.X - 140, (int)_nameBoardPos.Y - 50, 280, 100), new Color(16, 20, 35));
        }
        Game.Font?.Draw(sb, "Character Name:", _nameBoardPos + new Vector2(-70, -30), new Color(220, 200, 150));
        _nameField?.Draw(sb, Game.WhitePixel);
        if (!string.IsNullOrEmpty(_nameError))
            Game.Font?.Draw(sb, _nameError, _nameBoardPos + new Vector2(-70, 28), new Color(255, 80, 80));
        _btYes?.Draw(sb);
        _btNo?.Draw(sb);
    }

    private void DrawConfirm(SpriteBatch sb, int w, int h)
    {
        _confirmBoard?.Draw(sb, _confirmPos);
        if (Game.Font != null)
        {
            var cx = _confirmPos.X - 80;
            var cy = _confirmPos.Y - 40f;
            Game.Font.Draw(sb, "Create character?",  new Vector2(cx, cy),       new Color(220, 200, 150));
            Game.Font.Draw(sb, $"Name: {_nameField?.Text ?? ""}",
                                                     new Vector2(cx, cy + 18),  Color.White);
            Game.Font.Draw(sb, $"Gender: {(_isMale ? "Male" : "Female")}",
                                                     new Vector2(cx, cy + 32),  Color.White);
        }
        _btYes?.Draw(sb);
        _btNo?.Draw(sb);
    }

    private void DrawAppRow(SpriteBatch sb, float lx, ref float ly, string label, int val)
    {
        Game.Font!.Draw(sb, $"{label}: {val}", new Vector2(lx, ly), new Color(200, 200, 220));
        ly += 16;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        if (_subScreen == SubScreen.NameEntry)
        {
            _nameField?.HandleMouseButton(x, y, down);
        }
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return;
        _btYes?.HandleMouseButton(x, y, down);
        _btNo?.HandleMouseButton(x, y, down);
        _btRandom?.HandleMouseButton(x, y, down);
    }

    public override void OnKeyPress(Keys key)
    {
        if (key == Keys.Back)
        {
            if (_subScreen == SubScreen.NameEntry)  { _subScreen = SubScreen.Appearance; return; }
            if (_subScreen == SubScreen.Confirm)    { _subScreen = SubScreen.NameEntry;  return; }
            GoBackToRaceSelect();
            return;
        }
        if (_subScreen == SubScreen.NameEntry)
        {
            if (key == Keys.Back)  _nameField?.OnTextInput('\b');
            if (key == Keys.Enter) OnYes();
        }
        if (_subScreen == SubScreen.Confirm && key == Keys.Enter) OnYes();
    }

    public override void OnTextInput(char ch)
    {
        if (_subScreen == SubScreen.NameEntry) _nameField?.OnTextInput(ch);
    }

    // ── Transitions ───────────────────────────────────────────────────────────

    private void OnYes()
    {
        switch (_subScreen)
        {
            case SubScreen.Appearance:
                _subScreen = SubScreen.NameEntry;
                if (_nameField != null) _nameField.IsFocused = true;
                break;

            case SubScreen.NameEntry:
                _nameError = ValidateName(_nameField?.Text ?? "");
                if (_nameError.Length == 0) _subScreen = SubScreen.Confirm;
                break;

            case SubScreen.Confirm:
                _logger.LogInformation(
                    "CharCreation: create name='{Name}' male={Male} face={F} hair={H} skin={S} — no packet yet",
                    _nameField?.Text, _isMale, _faceIdx, _hairIdx, _skinIdx);
                // Transition to CharSelect (server will create and respond)
                Game.StageDirector.Replace(new CharSelectStage(
                    _loggerFactory.CreateLogger<CharSelectStage>(),
                    _loggerFactory, _ui, _map, _sound,
                    _worldId, _channelId,
                    _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
                break;
        }
    }

    private void OnNo()
    {
        switch (_subScreen)
        {
            case SubScreen.Appearance: GoBackToRaceSelect(); break;
            case SubScreen.NameEntry:  _subScreen = SubScreen.Appearance; break;
            case SubScreen.Confirm:    _subScreen = SubScreen.NameEntry; break;
        }
    }

    private void GoBackToRaceSelect()
    {
        Game.StageDirector.Replace(new RaceSelectStage(
            _loggerFactory.CreateLogger<RaceSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _isMale,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Cycle(ref int idx, int dir, int count) =>
        idx = (idx + dir + count) % count;

    private void Randomize()
    {
        var rng = System.Random.Shared;
        _faceIdx      = rng.Next(FaceCount);
        _hairIdx      = rng.Next(HairCount);
        _hairColorIdx = rng.Next(HairColorCount);
        _skinIdx      = rng.Next(SkinCount);
    }

    private static string ValidateName(string name)
    {
        if (name.Length < 4) return "Name must be at least 4 characters.";
        if (name.Length > 12) return "Name must be at most 12 characters.";
        foreach (var c in name)
            if (!char.IsLetterOrDigit(c)) return "Only letters and digits allowed.";
        return string.Empty;
    }

    private WzSprite? LoadC(string path)
    {
        try { return _ui?.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null; }
        catch { return null; }
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

    private void ApplyLayout()
    {
        if (_btYes != null) _btYes.Position    = _subScreen switch
        {
            SubScreen.Appearance => _avatarBoardPos + new Vector2(40, 140),
            SubScreen.NameEntry  => _nameBoardPos   + new Vector2(20, 60),
            _                    => _confirmPos      + new Vector2(20, 60),
        };
        if (_btNo  != null) _btNo.Position     = _btYes?.Position - new Vector2(60, 0) ?? Vector2.Zero;
        if (_btFaceL  != null) _btFaceL.Position  = _avatarBoardPos + new Vector2(-100, -80);
        if (_btFaceR  != null) _btFaceR.Position  = _avatarBoardPos + new Vector2(-20,  -80);
        if (_btHairL  != null) _btHairL.Position  = _avatarBoardPos + new Vector2(-100, -60);
        if (_btHairR  != null) _btHairR.Position  = _avatarBoardPos + new Vector2(-20,  -60);
        if (_btColorL != null) _btColorL.Position = _avatarBoardPos + new Vector2(-100, -40);
        if (_btColorR != null) _btColorR.Position = _avatarBoardPos + new Vector2(-20,  -40);
        if (_btSkinL  != null) _btSkinL.Position  = _avatarBoardPos + new Vector2(-100, -20);
        if (_btSkinR  != null) _btSkinR.Position  = _avatarBoardPos + new Vector2(-20,  -20);
        if (_btRandom != null) _btRandom.Position = _avatarBoardPos + new Vector2(-60, 120);
    }

    private void RegisterDebug()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "AvatarBoard", () => _avatarBoardPos, v => _avatarBoardPos = v));
        reg.Register(new DebugItem(DebugCat, "NameBoard",   () => _nameBoardPos,   v => _nameBoardPos   = v));
        reg.Register(new DebugItem(DebugCat, "Camera offset", () => _cameraOffset, v => _cameraOffset   = v) { Draggable = false });
    }

    private void UnregisterDebug()
    {
        var reg = Game.DebugRegistry;
        reg.Unregister(DebugCat, "AvatarBoard");
        reg.Unregister(DebugCat, "NameBoard");
        reg.Unregister(DebugCat, "Camera offset");
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
