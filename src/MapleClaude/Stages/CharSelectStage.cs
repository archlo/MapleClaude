using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Domain;
using MapleClaude.Map;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Stages;

/// <summary>
/// Character-select screen (step 2 of login). Mirrors the v95 client's
/// <c>CUIAvatar</c> + <c>CUICharSelect</c>: three character slots per page laid
/// out in the scrolling login-map space, the BtSelect/BtNew/BtDelete buttons in
/// a vertical stack, and left/right page arrows. The wooden stage signboard and
/// the login frame are rendered by <see cref="MapScene"/> from the login map.
///
/// All slot/button/arrow coordinates below are the authentic login-map values
/// (from <c>CUIAvatar::ResetCharacter</c> / <c>CUICharSelect::OnCreate</c>) and
/// are placed on screen via <see cref="MapScene.WorldToScreen"/>, so they ride
/// the camera exactly like the stage they stand on.
/// </summary>
public sealed class CharSelectStage : Stage
{
    private const int SlotCount = 3;   // 3 avatars per page (CUIAvatar), paged
    private const string DebugCat = "CharSelect";

    // ---- Authentic char-select map coordinates (login-map space) ----
    // CUIAvatar dialog (-290,-1218); avatar i at relative (170+125·i, 80) → these
    // absolutes. CUICharSelect dialog (255,-1359); buttons at relative (4,-2/36/85).
    private static readonly Vector2 SlotBaseMap = new(-120, -1138);  // slot 0 avatar anchor (feet)
    private static readonly Vector2 SlotStepMap = new(125, 0);
    private static readonly Vector2 PageLMap = new(-260, -1215);
    private static readonly Vector2 PageRMap = new(188, -1213);
    private static readonly Vector2 BtSelectMap = new(259, -1361);
    private static readonly Vector2 BtNewMap = new(259, -1323);
    private static readonly Vector2 BtDeleteMap = new(259, -1274);

    private readonly ILogger<CharSelectStage> _logger;
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

    // Shared chrome (overlay)
    private WzSprite? _commonFrame;
    private WzSprite? _stepIndicator;

    // CharSelect sprites
    private WzSprite? _charEmpty;       // character/1 — empty-slot figure
    private WzSprite? _charInfo;        // charInfo1 stat card (selected char)
    private WzSprite? _effectSelected;  // effect/0 — selection glow
    private WzSprite? _pageL;           // pageL/0 arrow
    private WzSprite? _pageR;           // pageR/0 arrow

    // Buttons (vertical stack, right side)
    private Button? _btSelect;
    private Button? _btNew;
    private Button? _btDelete;
    private readonly List<Button> _allButtons = new();

    // Overlays
    private LoginNoticeOverlay? _notice;
    private DeleteConfirmOverlay? _deleteConfirm;

    // State
    private int _selectedSlot = -1;     // absolute index into Session.Characters
    private int _page;

    // Camera scroll
    private float _scrollT;
    private const float ScrollDuration = 0.55f;

    // Tunables (debug-window editable)
    private Vector2 _cameraOffset = new(28, -1208);   // authentic step-2 camera (GetStepHeight)
    private Vector2 _stepIndicatorPos = new(72, 50);
    private Vector2 _charInfoPos = new(620, 300);     // stat card (no IDA coord — tunable)
    private Vector2 _slotNudge = Vector2.Zero;         // map-space fine nudge for the avatar row

    private List<CharacterEntry> Characters => Game.Session.Characters;
    private int MaxPage => Characters.Count == 0 ? 0 : (Characters.Count - 1) / SlotCount;

    public CharSelectStage(
        ILogger<CharSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        int worldId,
        int channelId,
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

    /// <summary>
    /// Re-entry constructor used by <see cref="CharCreateStage"/> after a
    /// successful <c>CreateNewCharacterResult</c>. World / channel / camera
    /// state are restored from the previous selection (held on the session).
    /// The character list lives on <c>Game.Session.Characters</c>; the
    /// <paramref name="existingCharacters"/> parameter is retained for call-site
    /// API stability.
    /// </summary>
    public CharSelectStage(
        ILogger<CharSelectStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound,
        IReadOnlyList<CharacterEntry> existingCharacters)
        : this(logger, loggerFactory, ui, map, sound,
               worldId: 0, channelId: 0,
               cameraStart: Vector2.Zero, loginCameraOffset: new Vector2(28, -1208))
    {
        _ = existingCharacters;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        if (_ui is null || _map is null)
        {
            _logger.LogError("CharSelectStage: WZ packages unavailable");
            return;
        }

        if (_ui.GetItem("MapLogin1.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);
            _scene.Camera = _cameraStart;
        }

        _commonFrame = LoadCanvas("Login.img/Common/frame");
        _stepIndicator = LoadCanvas("Login.img/Common/step/2");

        _charEmpty = LoadCanvas("Login.img/CharSelect/character/1/0");
        _charInfo = LoadCanvas("Login.img/CharSelect/charInfo1");
        _effectSelected = LoadCanvas("Login.img/CharSelect/effect/0/0");
        _pageL = LoadCanvas("Login.img/CharSelect/pageL/0/0");
        _pageR = LoadCanvas("Login.img/CharSelect/pageR/0/0");

        _btSelect = MakeButton("Login.img/CharSelect/BtSelect", OnSelectClicked);
        _btNew = MakeButton("Login.img/CharSelect/BtNew", OnNewClicked);
        _btDelete = MakeButton("Login.img/CharSelect/BtDelete", OnDeleteClicked);

        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;

        _notice = new LoginNoticeOverlay(_loader, _ui, Game.Font, new Vector2(400, 300));
        _deleteConfirm = new DeleteConfirmOverlay(_loader, _ui, Game.Font, new Vector2(400, 300))
        {
            OnConfirm = (charId, spw) =>
            {
                _logger.LogInformation("CharSelect: BtDelete confirm — charId={Cid}", charId);
                Game.Session.Send(LoginSender.DeleteCharacter(charId, spw));
            },
        };

        ApplyLayout();
        RegisterDebugItems();

        Game.LoginHandlers.OnSelectCharacterResult += OnSelectCharacterResult;
        Game.LoginHandlers.OnDeleteCharacterResult += OnDeleteCharacterResult;

        _logger.LogInformation(
            "CharSelectStage: world={World} channel={Channel} chars={N}",
            _worldId, _channelId, Characters.Count);
    }

    public override void OnExit()
    {
        Game.LoginHandlers.OnSelectCharacterResult -= OnSelectCharacterResult;
        Game.LoginHandlers.OnDeleteCharacterResult -= OnDeleteCharacterResult;
        UnregisterDebugItems();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    private void OnSelectCharacterResult(SelectCharacterResultArgs args)
    {
        if (!args.Success || args.ChannelHost is null)
        {
            _notice?.Show($"Could not enter character (code {args.ResultCode}).", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        _logger.LogInformation("SelectCharacterResult ok charId={Cid} — starting migration", args.CharacterId);
        Game.CharacterId = args.CharacterId;
        var fieldStage = new GameStage(
            _loggerFactory.CreateLogger<GameStage>(),
            _loggerFactory, _ui, _map, _sound,
            Game.CharWz, Game.NpcWz, Game.MobWz);
        Game.StageDirector.Replace(fieldStage);
        _ = Task.Run(async () =>
        {
            try
            {
                await Game.Migration.BeginMigrateAsync(args.ChannelHost, args.ChannelPort, args.CharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed");
            }
        });
    }

    private void OnDeleteCharacterResult(DeleteCharacterArgs args)
    {
        if (args.Success)
        {
            _selectedSlot = -1;
            if (_btSelect != null) _btSelect.Enabled = false;
            if (_btDelete != null) _btDelete.Enabled = false;
            _page = Math.Min(_page, MaxPage);
            _notice?.Show("Character deleted.", LoginNoticeOverlay.NoticeType.Ok);
        }
        else
        {
            _notice?.Show(DeleteFailMessage(args.ResultCode), LoginNoticeOverlay.NoticeType.Ok);
        }
    }

    // LoginResultType codes the server returns for a failed delete (mirrors
    // upstream Kinoko handleDeleteCharacter / LoginResultType).
    private static string DeleteFailMessage(byte code) => code switch
    {
        20 => "Incorrect 2nd password.",                        // IncorrectSPW
        6  => "Could not delete the character (server error).", // DBFail
        22 => "A guild master's character cannot be deleted.",
        24 => "An engaged character cannot be deleted.",
        29 => "A character in a family cannot be deleted.",
        _  => $"Could not delete the character (code {code}).",
    };

    public override void Update(GameTime gameTime)
    {
        if (_scene != null)
        {
            var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _scrollT = Math.Min(1f, _scrollT + dt / ScrollDuration);
            var sp = _scene.StartPoint ?? Vector2.Zero;
            var target = sp + _cameraOffset;
            _scene.Camera = Vector2.Lerp(_cameraStart, target, SmoothStep(_scrollT));
        }

        ApplyLayout();
        _notice?.Update(gameTime);
        _deleteConfirm?.Update(gameTime);
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

        // The wooden stage signboard is drawn by MapScene from the login map.
        _commonFrame?.Draw(sb, new Vector2(w / 2f, h / 2f));
        _stepIndicator?.Draw(sb, _stepIndicatorPos);

        DrawSlots(sb);

        if (_selectedSlot >= 0 && _selectedSlot < Characters.Count)
            _charInfo?.Draw(sb, _charInfoPos);

        if (MaxPage > 0)
        {
            if (_page > 0) _pageL?.Draw(sb, MapToScreen(PageLMap));
            if (_page < MaxPage) _pageR?.Draw(sb, MapToScreen(PageRMap));
        }

        foreach (var b in _allButtons)
            b.Draw(sb);

        _notice?.Draw(sb, Game.WhitePixel);
        _deleteConfirm?.Draw(sb, Game.WhitePixel);
    }

    private void DrawSlots(SpriteBatch sb)
    {
        for (var i = 0; i < SlotCount; i++)
        {
            var absIdx = _page * SlotCount + i;
            var pos = SlotScreen(i);
            var hasChar = absIdx < Characters.Count;

            if (absIdx == _selectedSlot)
                _effectSelected?.Draw(sb, pos);

            if (!hasChar)
            {
                _charEmpty?.Draw(sb, pos);
                continue;
            }

            // Avatar rendering is deferred; show the name + level beneath the slot.
            if (Game.Font != null)
            {
                var stat = Characters[absIdx].Stat;
                DrawCentered(sb, stat.Name, pos + new Vector2(0, 6), Color.White);
                DrawCentered(sb, $"Lv.{stat.Level}", pos + new Vector2(0, 20), new Color(220, 200, 100));
            }
        }
    }

    private void DrawCentered(SpriteBatch sb, string text, Vector2 center, Color color)
    {
        if (Game.Font is null) return;
        var size = Game.Font.Measure(text);
        Game.Font.Draw(sb, text, new Vector2(center.X - size.X / 2f, center.Y), color);
    }

    private Vector2 MapToScreen(Vector2 map)
    {
        var pp = GraphicsDevice.PresentationParameters;
        return _scene?.WorldToScreen(map, pp.BackBufferWidth, pp.BackBufferHeight)
            ?? new Vector2(pp.BackBufferWidth / 2f, pp.BackBufferHeight / 2f);
    }

    private Vector2 SlotScreen(int i) => MapToScreen(SlotBaseMap + SlotStepMap * i + _slotNudge);

    private void ApplyLayout()
    {
        if (_btSelect != null) _btSelect.Position = MapToScreen(BtSelectMap);
        if (_btNew != null) _btNew.Position = MapToScreen(BtNewMap);
        if (_btDelete != null) _btDelete.Position = MapToScreen(BtDeleteMap);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (_deleteConfirm?.IsVisible == true) { _deleteConfirm.HandleMouseButton(x, y, down); return; }
        if (_notice?.IsVisible == true) { _notice.HandleMouseButton(x, y, down); return; }

        if (down)
        {
            // Page arrows.
            if (MaxPage > 0)
            {
                if (_page > 0 && HitSprite(_pageL, PageLMap, x, y)) { OnPageL(); return; }
                if (_page < MaxPage && HitSprite(_pageR, PageRMap, x, y)) { OnPageR(); return; }
            }

            // Character slots — hit a box around each avatar standing on the stage.
            for (var i = 0; i < SlotCount; i++)
            {
                var pos = SlotScreen(i);
                var rect = new Rectangle((int)pos.X - 45, (int)pos.Y - 110, 90, 130);
                if (rect.Contains(x, y))
                {
                    SelectSlot(_page * SlotCount + i);
                    return;
                }
            }
        }

        foreach (var b in _allButtons)
        {
            if (b.HandleMouseButton(x, y, down)) return;
        }
    }

    private bool HitSprite(WzSprite? sprite, Vector2 map, int x, int y)
    {
        if (sprite is null) return false;
        var pos = MapToScreen(map);
        var rect = new Rectangle(
            (int)(pos.X - sprite.Origin.X), (int)(pos.Y - sprite.Origin.Y),
            sprite.Width, sprite.Height);
        return rect.Contains(x, y);
    }

    public override void OnKeyPress(Keys key)
    {
        if (_deleteConfirm?.IsVisible == true) { _deleteConfirm.OnKeyPress(key); return; }
        if (_notice?.OnKeyPress(key) == true) return;

        switch (key)
        {
            case Keys.Back:
                GoBack();
                break;
            case Keys.Enter:
                if (_selectedSlot >= 0) OnSelectClicked();
                break;
            case Keys.Left:
                if (_selectedSlot > 0) SelectSlot(_selectedSlot - 1);
                break;
            case Keys.Right:
                if (_selectedSlot < Characters.Count - 1) SelectSlot(_selectedSlot + 1);
                break;
        }
    }

    public override void OnTextInput(char character)
    {
        if (_deleteConfirm?.IsVisible == true) _deleteConfirm.OnTextInput(character);
    }

    private void SelectSlot(int absIdx)
    {
        if (absIdx < 0 || absIdx >= Characters.Count)
        {
            return;
        }
        _selectedSlot = absIdx;
        _page = absIdx / SlotCount;
        if (_btSelect != null) _btSelect.Enabled = true;
        if (_btDelete != null) _btDelete.Enabled = true;
        PlayClick();
        _logger.LogInformation("CharSelect: slot {Slot} selected", absIdx);
    }

    private void OnSelectClicked()
    {
        if (_selectedSlot < 0 || _selectedSlot >= Characters.Count)
        {
            _notice?.Show("That slot has no character yet.", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        var entry = Characters[_selectedSlot];
        _logger.LogInformation("CharSelect: BtSelect — slot {Slot} charId={Cid} name={Name}",
            _selectedSlot, entry.Stat.CharacterId, entry.Stat.Name);

        // SelectCharacter(19): int charId, string macAddress, string macAddressWithHddSerial.
        var p = OutPacket.Of(InHeader.SelectCharacter);
        p.WriteInt(entry.Stat.CharacterId);
        p.WriteString(MachineIdProvider.GetFakeMacAddress());
        p.WriteString(MachineIdProvider.GetFakeMacAddressWithHddSerial());
        Game.Session.Send(p);
    }

    private void OnNewClicked()
    {
        _logger.LogInformation("CharSelect: BtNew — entering gender select");
        Game.StageDirector.Replace(new GenderSelectStage(
            _loggerFactory.CreateLogger<GenderSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _worldId, _channelId, _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

    private void OnDeleteClicked()
    {
        if (_selectedSlot < 0 || _selectedSlot >= Characters.Count)
        {
            _notice?.Show("That slot has no character yet.", LoginNoticeOverlay.NoticeType.Ok);
            return;
        }
        var entry = Characters[_selectedSlot];
        _deleteConfirm?.Show(entry.Stat.Name, entry.Stat.CharacterId);
    }

    private void OnPageL()
    {
        if (_page <= 0) return;
        _page--;
        ClearSelection();
    }

    private void OnPageR()
    {
        if (_page >= MaxPage) return;
        _page++;
        ClearSelection();
    }

    private void ClearSelection()
    {
        _selectedSlot = -1;
        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;
        PlayClick();
    }

    private void GoBack()
    {
        Game.StageDirector.Replace(new WorldSelectStage(
            _loggerFactory.CreateLogger<WorldSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
    }

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
            _logger.LogWarning(ex, "CharSelectStage: missing canvas {Path}", path);
            return null;
        }
    }

    private Button? MakeButton(string path, Action onClick)
    {
        try
        {
            if (_ui!.GetItem(path) is not WzProperty root) return null;
            var b = new Button(_loader!, root) { OnClick = onClick };
            _allButtons.Add(b);
            return b;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CharSelectStage: missing button {Path}", path);
            return null;
        }
    }

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Register(new DebugItem(DebugCat, "Camera offset (SP +)", () => _cameraOffset, v => _cameraOffset = v) { Draggable = false });
        reg.Register(new DebugItem(DebugCat, "Slot nudge (map)", () => _slotNudge, v => _slotNudge = v) { Draggable = false });
        reg.Register(new DebugItem(DebugCat, "StepIndicator", () => _stepIndicatorPos, v => _stepIndicatorPos = v));
        reg.Register(new DebugItem(DebugCat, "CharInfo card", () => _charInfoPos, v => _charInfoPos = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var name in new[] { "Camera offset (SP +)", "Slot nudge (map)", "StepIndicator", "CharInfo card" })
            reg.Unregister(DebugCat, name);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
