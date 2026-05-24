using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Debug;
using MapleClaude.Domain;
using MapleClaude.Map;
using MapleClaude.Net;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Character-creation stage — the authentic v95 per-class flow (CLogin-driven
/// <c>CUINewCharNameSelect&lt;Class&gt;</c> + <c>CUINewCharAvatarSelect</c>). Reached from
/// the race/job screen. Two sub-screens, both showing a live avatar preview on the
/// scrolling create frame of <c>MapLogin.img</c> (which — unlike MapLogin1 — has all five
/// race frames, so Resistance no longer falls off the map):
/// <list type="number">
///   <item><b>Name</b> — the <c>charName</c> panel + a text field; OK runs CheckDuplicatedID.</item>
///   <item><b>Look</b> — the <c>charSet</c> panel + <c>avatarSel</c> category rows cycled by
///     BtLeft/BtRight; OK sends CreateNewCharacter.</item>
/// </list>
/// Appearance options come from <see cref="MakeCharInfoProvider"/> (Etc.wz/MakeCharInfo.img),
/// so every selected item is a server-valid starting item.
/// </summary>
public sealed class CharCreationStage : Stage
{
    private const string DebugCat = "CharCreate";

    // UI appearance categories = avatarSel/0..8 (rows 0-7 map 1:1 to MakeCharInfo cats;
    // row 8 toggles gender). Matches CLogin's 9-row m_aMaleItem/m_aFemaleItem.
    private const int CatFace = 0;
    private const int CatHair = 1;
    private const int CatHairColor = 2;
    private const int CatSkin = 3;
    private const int CatCoat = 4;
    private const int CatPants = 5;
    private const int CatShoes = 6;
    private const int CatWeapon = 7;
    private const int CatGender = 8;
    private const int RowCount = 9;

    private enum SubScreen { Name, Look }

    private readonly ILogger<CharCreationStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;
    private readonly int _worldId;
    private readonly int _channelId;
    private readonly int _raceIndex;   // UI race id (RaceSelectStage button order)
    private readonly Vector2 _cameraStart;
    private readonly Vector2 _loginCameraOffset;

    private WzTextureLoader? _loader;
    private MapScene? _scene;
    private CharacterRenderer? _charRenderer;
    private MakeCharInfoProvider? _makeChar;
    private string _section = "Info";

    private WzSprite? _commonFrame;
    private WzSprite? _charNameBoard;  // charName panel (name screen)
    private WzSprite? _charSetBoard;   // charSet panel (look screen)
    private readonly WzSprite?[] _rowNormal = new WzSprite?[RowCount];
    private readonly WzSprite?[] _rowDisabled = new WzSprite?[RowCount];

    private readonly Button?[] _btLeftRow = new Button?[RowCount];   // one cycler pair per row (IDB)
    private readonly Button?[] _btRightRow = new Button?[RowCount];
    private Button? _btYes, _btNo;
    private readonly List<Button> _allButtons = new();

    private SubScreen _subScreen = SubScreen.Name;
    private bool _male = true;
    private readonly int[] _sel = new int[RowCount];   // option index per category
    private int _selectedRow;
    private TextField? _nameField;
    private float _nameFieldNudgeX;   // per-class X shift for the name recess (Resistance/Knight)
    private float _nameFieldNudgeY;   // per-class Y shift for the name recess (Aran)
    private Vector2 _nameOkOffset = new(27, 178);    // per-class OK / Cancel offsets on the charName board
    private Vector2 _nameNoOffset = new(101, 178);
    private string _nameError = string.Empty;

    private Vector2 _cameraOffset = new(27, -1216);
    private float _scrollT;
    private const float ScrollDuration = 0.55f;

    // Both dialogs (name + look) ride the scrolling login map, anchored at the SAME world point
    // (IDB CWnd::CreateWnd left=109, top=-2613-600*uiRace, bScreenCoord=0); the live avatar sits at
    // map (22, -2369-600*uiRace). Children are drawn at board-relative IDB offsets, so the whole
    // right-side UI is exact-from-IDB and stays aligned with the map regardless of camera.
    private Vector2 _boardWorld;
    private Vector2 _avatarWorld;
    private const int RowPitch = 18;          // avatarSel row spacing (IDB)
    private ForbiddenNameProvider? _forbidden;

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
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
        _worldId = worldId;
        _channelId = channelId;
        _male = isMale;
        _raceIndex = raceIndex;
        _cameraStart = cameraStart;
        _loginCameraOffset = loginCameraOffset;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);
        _makeChar = new MakeCharInfoProvider(Game.EtcWz, _loggerFactory.CreateLogger<MakeCharInfoProvider>());
        _forbidden = new ForbiddenNameProvider(Game.EtcWz, _loggerFactory.CreateLogger<ForbiddenNameProvider>());
        _section = MakeCharInfoProvider.SectionForRace(_raceIndex);
        RandomizeAppearance();   // start with a random valid look (CLogin::InitNewCharEquip)

        // Scroll to this class's creation frame. GetStepHeight(4) = -8 - 600*(4 + uiRace).
        var uiRace = ConvertRaceToUiRace(RaceSelectStage.ServerRace(_raceIndex));
        _cameraOffset = new Vector2(27, -8 - 600 * (4 + uiRace));
        // Both boards (name+look) anchor here in login-map space; the avatar rides the map too.
        _boardWorld = new Vector2(109, -2613 - 600 * uiRace);
        _avatarWorld = new Vector2(22, -2369 - 600 * uiRace);

        if (_ui is null || _map is null) return;

        // MapLogin.img has all nine frames (incl. Resistance -4808); MapLogin1 does not.
        if (_ui.GetItem("MapLogin.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);
            _scene.Camera = _cameraStart;
        }

        _charRenderer = new CharacterRenderer(
            _loggerFactory.CreateLogger<CharacterRenderer>(), Game.CharWz, Game.ItemWz, Game.BaseWz, _loader);

        _commonFrame = LoadCanvas("Login.img/Common/frame");

        // Per-class panel root (NewCharResistance/NewCharKnight/…) with a generic NewChar
        // fallback. The Cygnus board node is "NewCharKnight" (there is no NewCharCygnus).
        var suffix = _raceIndex switch { 5 => "Resistance", 4 => "Evan", 3 => "Aran", 2 => "Knight", _ => "" };
        var classRoot = _ui.GetItem($"Login.img/NewChar{suffix}") as WzProperty;
        var newChar = _ui.GetItem("Login.img/NewChar") as WzProperty;

        _charNameBoard = LoadCanvasFrom(classRoot, "charName") ?? LoadCanvasFrom(newChar, "charName");
        _charSetBoard = LoadCanvasFrom(classRoot, "charSet") ?? LoadCanvasFrom(newChar, "charSet");
        for (var i = 0; i < RowCount; i++)
        {
            _rowNormal[i] = LoadCanvasFrom(newChar, $"avatarSel/{i}/normal");
            _rowDisabled[i] = LoadCanvasFrom(newChar, $"avatarSel/{i}/disabled");
        }

        // One BtLeft/BtRight pair PER row (IDB: 9 pairs, ids 1002+i / 1011+i) — each cycles its own
        // category. No dice button on the avatar screen (the look auto-randomizes on entry).
        for (var i = 0; i < RowCount; i++)
        {
            var row = i;
            _btLeftRow[i] = MakeButtonFrom(newChar, "BtLeft", () => CycleRow(row, -1));
            _btRightRow[i] = MakeButtonFrom(newChar, "BtRight", () => CycleRow(row, +1));
        }
        // Prefer the class-specific YES/NO art (NewCharResistance/NewCharKnight/…),
        // falling back to the generic NewChar pair when a class has none.
        _btYes = MakeButtonFrom(classRoot, "BtYes", OnYes) ?? MakeButtonFrom(newChar, "BtYes", OnYes);
        _btNo = MakeButtonFrom(classRoot, "BtNo", OnNo) ?? MakeButtonFrom(newChar, "BtNo", OnNo);

        // Per-class name-field styling. The wider Resistance/Knight boards push the
        // wooden recess ~3px right of the generic Explorer board; Resistance's lighter
        // board needs black ink + caret, while Explorer/Knight read white with a cyan
        // caret on their darker recess. Other classes keep the Explorer defaults.
        var (nameTextColor, nameCaretColor) = suffix switch
        {
            "Resistance" => (Color.Black, Color.Black),
            _ => (Color.White, new Color(120, 220, 255)),
        };
        // Per-race charName board offsets (IDB charName/NewChar{suffix}): Aran's board is shorter
        // (OK/Cancel at y137); Resistance/Cygnus are wider (shifted right). Plus the tuned recess
        // nudges — Aran up 4px, Resistance +2px right (over its +3 wide-board shift).
        _nameFieldNudgeX = suffix switch { "Resistance" => 5f, "Knight" => 3f, _ => 0f };
        _nameFieldNudgeY = suffix == "Aran" ? -8f : 0f;
        (_nameOkOffset, _nameNoOffset) = suffix switch
        {
            "Resistance" => (new Vector2(47, 178), new Vector2(122, 178)),
            "Aran" => (new Vector2(34, 137), new Vector2(108, 137)),
            "Knight" => (new Vector2(31, 181), new Vector2(104, 181)),
            _ => (new Vector2(27, 178), new Vector2(101, 178)),   // Normal (Explorer / Dual / Evan)
        };

        _nameField = new TextField
        {
            Width = 120,            // IDB charName edit is 120 wide; ApplyLayout sets Position
            Height = 22,
            Font = Game.Font,
            MaxLength = 12,
            IsFocused = true,
            DrawFallbackBox = false,   // type directly over the panel's wooden recess
            TextColor = nameTextColor,
            CaretColor = nameCaretColor,
        };

        ApplyLayout();
        RegisterDebug();
        _logger.LogInformation(
            "CharCreationStage: race(ui)={R} section={S} faceOpts={F} hairOpts={H} camera={Cam}",
            _raceIndex, _section, _makeChar.Options(_section, _male, 0).Length,
            _makeChar.Options(_section, _male, 1).Length, _cameraOffset);
    }

    public override void OnExit()
    {
        UnregisterDebug();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    public override void Update(GameTime gt)
    {
        if (_scene != null)
        {
            var dt = (float)gt.ElapsedGameTime.TotalSeconds;
            _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
            var sp = _scene.StartPoint ?? Vector2.Zero;
            _scene.Camera = Vector2.Lerp(_cameraStart, sp + _cameraOffset, SmoothStep(_scrollT));
        }
        Game.Session.DrainQueue();
        if (_subScreen == SubScreen.Name) _nameField?.Update(gt);
        ApplyLayout();
    }

    public override void Draw(GameTime gt, SpriteBatch sb)
    {
        var pp = GraphicsDevice.PresentationParameters;
        var w = pp.BackBufferWidth;
        var h = pp.BackBufferHeight;

        if (_scene != null)
            _scene.Draw(sb, Game.WhitePixel, w, h);
        else
            sb.Draw(Game.WhitePixel, pp.Bounds, new Color(10, 16, 36));

        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));
        DrawFrameMuteButton(sb);

        DrawPreview(sb);

        if (_subScreen == SubScreen.Name) DrawNameScreen(sb);
        else DrawLookScreen(sb);
    }

    private void DrawPreview(SpriteBatch sb)
    {
        if (_charRenderer is null) return;
        _charRenderer.Draw(sb, BuildLook(), stat: null, Stance.Stand1, frame: 0,
            position: AvatarScreen(), facingLeft: false);
    }

    private void DrawNameScreen(SpriteBatch sb)
    {
        var board = BoardScreen();
        _charNameBoard?.Draw(sb, board);           // panel art carries the "Character Name" label
        _nameField?.Draw(sb, Game.WhitePixel);
        _btYes?.Draw(sb);
        _btNo?.Draw(sb);
        DrawNameError(sb, board);
    }

    // Validation/forbidden message, centered under the OK/Cancel row on a fitted dark backdrop
    // (plain text over the scrolling map is unreadable).
    private void DrawNameError(SpriteBatch sb, Vector2 board)
    {
        if (Game.Font == null || string.IsNullOrEmpty(_nameError)) return;
        var size = Game.Font.Measure(_nameError);
        var pos = new Vector2(board.X + 100 - size.X / 2f, board.Y + 236);   // board centre, gap below the OK/Cancel row (+178, ~41 tall)
        var box = new Rectangle((int)(pos.X - 7), (int)(pos.Y - 5),
                                (int)(size.X + 14), (int)(size.Y + 10));
        sb.Draw(Game.WhitePixel, box, new Color(0, 0, 0, 215));
        Game.Font.Draw(sb, _nameError, pos, new Color(255, 150, 150));
    }

    private void DrawLookScreen(SpriteBatch sb)
    {
        var board = BoardScreen();
        _charSetBoard?.Draw(sb, board);

        for (var i = 0; i < RowCount; i++)
        {
            // Row label (avatarSel/{i}); IDB blit x = Xmove(-7)+21 = 14, y = 102 + 18*i.
            var label = _rowNormal[i] ?? _rowDisabled[i];
            label?.Draw(sb, new Vector2(board.X + 14, board.Y + 102 + RowPitch * i));

            // Selected-item name, shrunk + fitted between the arrows (the full UI font overflows it).
            if (Game.Font != null)
            {
                var name = RowName(i);
                if (!string.IsNullOrEmpty(name)) DrawRowName(sb, board, i, name);
            }

            _btLeftRow[i]?.Draw(sb);
            _btRightRow[i]?.Draw(sb);
        }

        _btYes?.Draw(sb);
        _btNo?.Draw(sb);
    }

    // Selected-item name for a row: small UI font, fitted between the arrows ([+81,+198]),
    // centered in the gap and vertically centered in the 17px label band.
    private void DrawRowName(SpriteBatch sb, Vector2 board, int row, string name)
    {
        var font = Game.BasicFont ?? Game.Font;             // authentic Arial 12px, rendered crisp
        if (font is null) return;
        const float regionLeft = 81f, regionRight = 198f;   // inner edges between BtLeft/BtRight
        var maxW = regionRight - regionLeft - 6f;
        var fullW = font.Measure(name).X;
        var scale = fullW > maxW ? maxW / fullW : 1f;       // native size; shrink only to fit long names
        var x = board.X + (regionLeft + regionRight) / 2f - fullW * scale / 2f;
        var y = board.Y + 102 + RowPitch * row + (17f - font.LineHeight * scale) / 2f;
        font.Draw(sb, name, new Vector2(x, y), Color.Black, scale);
    }

    // ---- appearance model ----

    private int CurId(int mciCat, int sel)
    {
        var o = _makeChar?.Options(_section, _male, mciCat) ?? Array.Empty<int>();
        return o.Length == 0 ? 0 : o[((sel % o.Length) + o.Length) % o.Length];
    }

    private AvatarLook BuildLook()
    {
        var face = CurId(MakeCharInfoProvider.CatFace, _sel[CatFace]);
        var hairBase = CurId(MakeCharInfoProvider.CatHair, _sel[CatHair]);
        var hairColor = CurId(MakeCharInfoProvider.CatHairColor, _sel[CatHairColor]);
        var skin = CurId(MakeCharInfoProvider.CatSkin, _sel[CatSkin]);
        var coat = CurId(MakeCharInfoProvider.CatCoat, _sel[CatCoat]);
        var pants = CurId(MakeCharInfoProvider.CatPants, _sel[CatPants]);
        var shoes = CurId(MakeCharInfoProvider.CatShoes, _sel[CatShoes]);
        var weapon = CurId(MakeCharInfoProvider.CatWeapon, _sel[CatWeapon]);

        var look = new AvatarLook
        {
            Gender = (byte)(_male ? 0 : 1),
            Skin = (byte)skin,
            Face = face,
            Hair = hairBase + hairColor,
        };
        if (coat != 0) look.HairEquip[BodyPartSlot.Clothes] = coat;
        if (pants != 0) look.HairEquip[BodyPartSlot.Pants] = pants;
        if (shoes != 0) look.HairEquip[BodyPartSlot.Shoes] = shoes;
        if (weapon != 0) look.HairEquip[BodyPartSlot.Weapon] = weapon;
        return look;
    }

    private void CycleRow(int row, int dir)
    {
        PlayClick();
        if (row == CatGender) { _male = !_male; return; }
        _sel[row] += dir;
    }

    // Pick a random valid option per appearance category (not gender), mirroring
    // CLogin::InitNewCharEquip (rand()%count) so a full default look shows on enter.
    private void RandomizeAppearance()
    {
        var rng = System.Random.Shared;
        for (var i = 0; i < CatGender; i++) _sel[i] = rng.Next(0, 9999);
    }

    // ---- input ----

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (_subScreen == SubScreen.Name)
        {
            _nameField?.HandleMouseButton(x, y, down);
        }
        else
        {
            // Each row has its own BtLeft/BtRight; route press+release to all of them.
            for (var i = 0; i < RowCount; i++)
            {
                _btLeftRow[i]?.HandleMouseButton(x, y, down);
                _btRightRow[i]?.HandleMouseButton(x, y, down);
            }
        }

        _btYes?.HandleMouseButton(x, y, down);
        _btNo?.HandleMouseButton(x, y, down);
    }

    public override void OnKeyPress(Keys key)
    {
        if (_subScreen == SubScreen.Name)
        {
            if (key == Keys.Enter) { OnYes(); return; }
            if (key == Keys.Escape) { OnNo(); return; }
            return;
        }
        switch (key)
        {
            case Keys.Up: _selectedRow = (_selectedRow - 1 + RowCount) % RowCount; break;
            case Keys.Down: _selectedRow = (_selectedRow + 1) % RowCount; break;
            case Keys.Left: CycleRow(_selectedRow, -1); break;
            case Keys.Right: CycleRow(_selectedRow, +1); break;
            case Keys.Enter: OnYes(); break;
            case Keys.Escape: OnNo(); break;
        }
    }

    public override void OnTextInput(char ch)
    {
        if (_subScreen == SubScreen.Name) _nameField?.OnTextInput(ch);
    }

    // ---- transitions ----

    private void OnYes()
    {
        if (_subScreen == SubScreen.Name) { OnCheckName(); return; }
        SendCreate();
    }

    private void OnNo()
    {
        PlayClick();
        if (_subScreen == SubScreen.Look) { _subScreen = SubScreen.Name; if (_nameField != null) _nameField.IsFocused = true; return; }
        GoBackToRaceSelect();
    }

    private void OnCheckName()
    {
        var name = _nameField?.Text ?? string.Empty;
        _nameError = ValidateName(name);
        if (_nameError.Length != 0)
        {
            _logger.LogInformation("Name check '{Name}' rejected locally: {Err}", name, _nameError);
            return;
        }
        if (_forbidden?.IsForbidden(name) == true)
        {
            _nameError = "This name cannot be used.";
            _logger.LogInformation("Name check '{Name}' rejected: forbidden word", name);
            return;
        }
        if (!Game.Session.IsConnected)
        {
            _logger.LogWarning("Name check '{Name}': session NOT connected — advancing to Look WITHOUT a server check", name);
            _subScreen = SubScreen.Look;
            return;
        }

        _logger.LogInformation("Name check '{Name}': sending CheckDuplicatedID, awaiting server reply", name);
        Game.Session.RegisterHandler(OutHeader.CheckDuplicatedIDResult, pkt =>
        {
            pkt.ReadString();                 // echo name
            var code = pkt.ReadByte();
            _logger.LogInformation("Name check '{Name}' result: code={Code} ({Result})",
                name, code, code == 0 ? "available -> Look" : "rejected");
            if (code == 0) _subScreen = SubScreen.Look;
            else _nameError = code switch
            {
                1 => "Name already in use.",
                2 => "This name cannot be used.",
                _ => "Invalid name.",
            };
            Game.Session.UnregisterHandler(OutHeader.CheckDuplicatedIDResult);
        });
        Game.Session.Send(LoginSender.CheckDuplicatedId(name));
    }

    private void SendCreate()
    {
        var name = _nameField?.Text ?? string.Empty;
        var face = CurId(MakeCharInfoProvider.CatFace, _sel[CatFace]);
        var hairBase = CurId(MakeCharInfoProvider.CatHair, _sel[CatHair]);
        var hairColor = CurId(MakeCharInfoProvider.CatHairColor, _sel[CatHairColor]);
        var skin = CurId(MakeCharInfoProvider.CatSkin, _sel[CatSkin]);
        var coat = CurId(MakeCharInfoProvider.CatCoat, _sel[CatCoat]);
        var pants = CurId(MakeCharInfoProvider.CatPants, _sel[CatPants]);
        var shoes = CurId(MakeCharInfoProvider.CatShoes, _sel[CatShoes]);
        var weapon = CurId(MakeCharInfoProvider.CatWeapon, _sel[CatWeapon]);

        _logger.LogInformation(
            "CharCreation: create name='{Name}' race={R} face={F} hair={H}+{C} skin={S} coat={Co} pants={P} shoes={Sh} wep={W} male={M}",
            name, RaceSelectStage.ServerRace(_raceIndex), face, hairBase, hairColor, skin, coat, pants, shoes, weapon, _male);

        if (!Game.Session.IsConnected)
        {
            GoBackToCharSelect();
            return;
        }

        var handler = new LoginPacketHandler(
            _loggerFactory.CreateLogger<LoginPacketHandler>(), _loggerFactory);
        handler.OnCharCreated = charId =>
        {
            _logger.LogInformation("Char created id={Id} — back to CharSelect", charId);
            GoBackToCharSelect();
        };
        handler.OnCharCreateFail = msg =>
        {
            _subScreen = SubScreen.Name;
            _nameError = "Creation failed.";
            _logger.LogWarning("Create char failed: {Msg}", msg);
        };
        handler.AliveAckRequested = () => Game.Session.Send(LoginSender.AliveAck());
        handler.RegisterAll(Game.Session);

        Game.Session.Send(LoginSender.CreateNewCharacter(
            name,
            race: RaceSelectStage.ServerRace(_raceIndex),
            face: face,
            hair: hairBase,
            hairColor: hairColor,
            skin: skin,
            coat: coat,
            pants: pants,
            shoes: shoes,
            weapon: weapon,
            male: _male));
    }

    private void GoBackToRaceSelect()
    {
        Game.StageDirector.Replace(new RaceSelectStage(
            _loggerFactory.CreateLogger<RaceSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId,
            _cameraStart, _loginCameraOffset));
    }

    private void GoBackToCharSelect()
    {
        Game.StageDirector.Replace(new CharSelectStage(
            _loggerFactory.CreateLogger<CharSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    // ---- helpers ----

    // Matches the IDB is_valid_character_name local rules (before the forbidden-word scan).
    private static string ValidateName(string name)
    {
        if (name.Length < 4) return "Name must be at least 4 characters.";
        if (name.Length > 12) return "Name must be at most 12 characters.";
        if (name.Contains(' ')) return "Name cannot contain spaces.";
        foreach (var c in name)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
                return "Only letters and digits allowed.";
        // IDB anti-impersonation rule: at most 3 of the look-alike 'I'/'l' glyphs combined.
        var il = 0;
        foreach (var c in name) if (c is 'I' or 'l') il++;
        if (il >= 4) return "Too many look-alike 'I'/'l' characters.";
        return string.Empty;
    }

    private void ApplyLayout()
    {
        var board = BoardScreen();
        if (_subScreen == SubScreen.Name)
        {
            // IDB charName board, per-race (OK/Cancel + recess nudges set in OnEnter).
            if (_nameField != null) _nameField.Position = board + new Vector2(33 + _nameFieldNudgeX, 106 + _nameFieldNudgeY);
            if (_btYes != null) _btYes.Position = board + _nameOkOffset;
            if (_btNo != null) _btNo.Position = board + _nameNoOffset;
            return;
        }
        // IDB charSet board: per-row BtLeft (66,103+18i) / BtRight (198,103+18i); BtYes (37,330), BtNo (111,330).
        for (var i = 0; i < RowCount; i++)
        {
            var y = board.Y + 103 + RowPitch * i;
            if (_btLeftRow[i] != null) _btLeftRow[i]!.Position = new Vector2(board.X + 66, y);
            if (_btRightRow[i] != null) _btRightRow[i]!.Position = new Vector2(board.X + 198, y);
        }
        if (_btYes != null) _btYes.Position = board + new Vector2(37, 330);
        if (_btNo != null) _btNo.Position = board + new Vector2(111, 330);
    }

    // Board screen pos = the IDB world anchor run through the scene camera, so the panel + all its
    // children ride the scrolling login map and stay aligned with the background frame.
    private Vector2 BoardScreen()
    {
        if (_scene is null) return new Vector2(482, 95);
        var pp = GraphicsDevice.PresentationParameters;
        return _scene.WorldToScreen(_boardWorld, pp.BackBufferWidth, pp.BackBufferHeight);
    }

    private Vector2 AvatarScreen()
    {
        if (_scene is null) return new Vector2(395, 339);
        var pp = GraphicsDevice.PresentationParameters;
        return _scene.WorldToScreen(_avatarWorld, pp.BackBufferWidth, pp.BackBufferHeight);
    }

    // Display name for a look row (CLogin::GetNewCharEquipName): face + equips from String.wz
    // (NameService); hair/hairColor/skin from the MakeCharInfo Name table; gender literal.
    private string RowName(int row) => row switch
    {
        CatFace => ItemName(CatFace),
        CatHair => _makeChar?.Name(_section, _male, 1, CurId(CatHair, _sel[CatHair])) ?? string.Empty,
        CatHairColor => _makeChar?.Name(_section, _male, 2, CurId(CatHairColor, _sel[CatHairColor])) ?? string.Empty,
        CatSkin => _makeChar?.Name(_section, _male, 3, CurId(CatSkin, _sel[CatSkin])) ?? string.Empty,
        CatCoat or CatPants or CatShoes or CatWeapon => ItemName(row),
        CatGender => _male ? "Male" : "Female",
        _ => string.Empty,
    };

    private string ItemName(int cat) => Game.Names.ItemName(CurId(cat, _sel[cat])) ?? string.Empty;

    private void PlayClick()
    {
        if (_sound?.GetItem("UI.img/BtMouseClick") is WzSound click) Game.AudioPlayer.PlayEffect(click);
    }

    private WzSprite? LoadCanvas(string path)
    {
        try { return _ui?.GetItem(path) is WzCanvas c ? _loader!.Load(c) : null; }
        catch { return null; }
    }

    private WzSprite? LoadCanvasFrom(WzProperty? root, string relPath)
    {
        try
        {
            object? node = root;
            foreach (var part in relPath.Split('/'))
                node = (node as WzProperty)?.Get(part);
            return node is WzCanvas c ? _loader!.Load(c) : null;
        }
        catch { return null; }
    }

    private Button? MakeButtonFrom(WzProperty? root, string name, Action onClick)
    {
        try
        {
            if (root?.Get(name) is not WzProperty pr) return null;
            var b = new Button(_loader!, pr) { OnClick = onClick };
            _allButtons.Add(b);
            return b;
        }
        catch { return null; }
    }

    // CLogin::ConvertSelectedRaceToUIRace — server race -> login-map creation-frame index.
    private static int ConvertRaceToUiRace(int serverRace) => serverRace switch
    {
        0 => 4, 1 => 1, 2 => 0, 3 => 3, 4 => 2, _ => serverRace,
    };

    private void RegisterDebug()
    {
        // Positions are now IDB-derived (world-anchored); only the camera frame stays adjustable.
        Game.DebugRegistry.Register(
            new DebugItem(DebugCat, "Camera offset", () => _cameraOffset, v => _cameraOffset = v) { Draggable = false });
    }

    private void UnregisterDebug()
        => Game.DebugRegistry.Unregister(DebugCat, "Camera offset");

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
