using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Map;
using MapleClaude.Net;
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
/// Character-select screen (step 2 of login). Shows up to 6 character slots per
/// visible page from MapLogin1.img's CharSelect section.  No server data yet —
/// all slots render as empty. BtSelect is enabled only when a slot is clicked.
/// </summary>
public sealed class CharSelectStage : Stage
{
    /// <summary>Character data sent by <c>SelectWorldResult</c> packet.</summary>
    public sealed class CharInfo
    {
        public int    Id;
        public string Name   = string.Empty;
        public int    Level  = 1;
        public int    JobId  = 0;
        public int    MapId  = 0;
        public int    Face   = 20000;
        public int    Hair   = 30000;
    }

    private const int SlotCount = 6;
    private const string DebugCat = "CharSelect";

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

    // Shared UI
    private WzSprite? _commonFrame;
    private WzSprite? _stepIndicator;

    // CharSelect-specific sprites
    private WzSprite? _charSlot;
    private WzSprite? _charEmpty;
    private WzSprite? _charInfo;
    private WzSprite? _nameTag;
    private WzSprite? _selectedWorld;
    private WzSprite? _effectNormal;
    private WzSprite? _effectSelected;

    // Buttons
    private Button? _btSelect;
    private Button? _btNew;
    private Button? _btDelete;
    private Button? _btPageL;
    private Button? _btPageR;
    private readonly List<Button> _allButtons = new();

    // Overlays
    private LoginNoticeOverlay? _notice;

    // State
    private int _selectedSlot = -1;
    private readonly List<CharInfo> _chars = new();

    /// <summary>Set by WorldSelectStage after receiving SelectWorldResult packet.</summary>
    public void LoadChars(IEnumerable<CharInfo> chars)
    {
        _chars.Clear();
        _chars.AddRange(chars);
        _selectedSlot = -1;
        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;
        _logger.LogInformation("CharSelectStage: loaded {N} chars from server", _chars.Count);
    }

    public int SelectedCharId => _selectedSlot >= 0 && _selectedSlot < _chars.Count
        ? _chars[_selectedSlot].Id : -1;
    private int _page;

    // Camera scroll
    private float _scrollT;
    private const float ScrollDuration = 0.55f;

    // Layout tunables (debug-window editable)
    private Vector2 _cameraOffset = new(27, -1216);
    private Vector2 _selectedWorldPos = new(400, 155);
    private Vector2 _stepIndicatorPos = new(72, 50);
    private Vector2 _charInfoPos = new(590, 320);
    private Vector2 _slotBase = new(92, 320);
    private float _slotSpacing = 113f;
    private Vector2 _btSelectPos = new(590, 430);
    private Vector2 _btNewPos = new(400, 490);
    private Vector2 _btDeletePos = new(570, 490);
    private Vector2 _btPageLPos = new(50, 320);
    private Vector2 _btPageRPos = new(755, 320);

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

        _charSlot = LoadCanvas("Login.img/CharSelect/charSlot");
        _charEmpty = LoadCanvas("Login.img/CharSelect/character/0");
        _charInfo = LoadCanvas("Login.img/CharSelect/charInfo1");
        _nameTag = LoadCanvas("Login.img/CharSelect/nameTag");
        _selectedWorld = LoadCanvas("Login.img/CharSelect/selectedWorld");
        _effectNormal = LoadCanvas("Login.img/CharSelect/effect/0");
        _effectSelected = LoadCanvas("Login.img/CharSelect/effect/1");

        _btSelect = MakeButton("Login.img/CharSelect/BtSelect", () => OnSelectClicked());
        _btNew = MakeButton("Login.img/CharSelect/BtNew", () => OnNewClicked());
        _btDelete = MakeButton("Login.img/CharSelect/BtDelete", () => OnDeleteClicked());
        _btPageL = MakeButton("Login.img/CharSelect/pageL", () => OnPageL());
        _btPageR = MakeButton("Login.img/CharSelect/pageR", () => OnPageR());

        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;

        _notice = new LoginNoticeOverlay(_loader, _ui, Game.Font, new Vector2(400, 300));

        ApplyLayout();
        RegisterDebugItems();

        _logger.LogInformation(
            "CharSelectStage: world={World} channel={Channel} scene={Scene}",
            _worldId, _channelId, _scene != null);
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
        Game.Session.DrainQueue();
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
        _stepIndicator?.Draw(sb, _stepIndicatorPos);
        _selectedWorld?.Draw(sb, _selectedWorldPos);

        DrawSlots(sb);

        _charInfo?.Draw(sb, _charInfoPos);

        foreach (var b in _allButtons)
            b.Draw(sb);

        _notice?.Draw(sb, Game.WhitePixel);
    }

    private void DrawSlots(SpriteBatch sb)
    {
        var offset = _page * SlotCount;
        for (var i = 0; i < SlotCount; i++)
        {
            var pos    = SlotPos(i);
            var absIdx = offset + i;
            var hasChar = absIdx < _chars.Count;
            var ch = hasChar ? _chars[absIdx] : null;

            _charSlot?.Draw(sb, pos);
            if (hasChar)
            {
                // Character exists — show name and level
                if (Game.Font != null)
                {
                    var namePos = pos + new Vector2(-30, 72);
                    Game.Font.Draw(sb, ch!.Name, namePos, Color.White);
                    Game.Font.Draw(sb, $"Lv.{ch.Level}", namePos + new Vector2(0, 13),
                        new Color(220, 200, 100));
                }
            }
            else
            {
                _charEmpty?.Draw(sb, pos);
            }

            if (i == _selectedSlot - offset)
                _effectSelected?.Draw(sb, pos);
            else
                _effectNormal?.Draw(sb, pos + new Vector2(0, 10));

            _nameTag?.Draw(sb, pos + new Vector2(0, 70));
        }
    }

    private Vector2 SlotPos(int i) => _slotBase + new Vector2(i * _slotSpacing, 0);

    private void ApplyLayout()
    {
        if (_btSelect != null) _btSelect.Position = _btSelectPos;
        if (_btNew != null) _btNew.Position = _btNewPos;
        if (_btDelete != null) _btDelete.Position = _btDeletePos;
        if (_btPageL != null) _btPageL.Position = _btPageLPos;
        if (_btPageR != null) _btPageR.Position = _btPageRPos;
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (_notice?.IsVisible == true)
        {
            _notice.HandleMouseButton(x, y, down);
            return;
        }

        // Slot hit test (on mouse-down to give visual feedback)
        if (down)
        {
            for (var i = 0; i < SlotCount; i++)
            {
                var pos = SlotPos(i);
                var slotW = _charSlot?.Width ?? 80;
                var slotH = _charSlot?.Height ?? 150;
                var ox = _charSlot?.Origin.X ?? slotW / 2f;
                var oy = _charSlot?.Origin.Y ?? slotH / 2f;
                var rect = new Rectangle((int)(pos.X - ox), (int)(pos.Y - oy), slotW, slotH);
                if (rect.Contains(x, y))
                {
                    SelectSlot(i);
                    return;
                }
            }
        }

        foreach (var b in _allButtons)
        {
            if (b.HandleMouseButton(x, y, down)) return;
        }
    }

    public override void OnKeyPress(Keys key)
    {
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
                if (_selectedSlot < SlotCount - 1) SelectSlot(_selectedSlot + 1);
                break;
        }
    }

    private void SelectSlot(int index)
    {
        _selectedSlot = index;
        if (_btSelect != null) _btSelect.Enabled = true;
        if (_btDelete != null) _btDelete.Enabled = true;
        _logger.LogInformation("CharSelect: slot {Slot} selected (no char data yet)", index);
    }

    private void OnSelectClicked()
    {
        if (_selectedSlot < 0) return;
        var charId = SelectedCharId;
        _logger.LogInformation("CharSelect: BtSelect slot={Slot} charId={Id}", _selectedSlot, charId);

        if (Game.Session.IsConnected && charId > 0)
        {
            // Send SelectCharacter — server replies with SelectCharacterResult (host:port)
            // then we reconnect to channel server and send MigrateIn
            Game.Session.RegisterHandler(InHeader.SelectCharacterResult, pkt =>
            {
                var result = pkt.DecodeByte();
                pkt.DecodeByte(); // 0
                if (result != 0)
                {
                    _notice?.Show($"Select character failed (code {result}).");
                    return;
                }
                var ipBytes   = pkt.DecodeArray(4);
                var host      = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
                var port      = pkt.DecodeShort();
                var receivedId = pkt.DecodeInt();
                _logger.LogInformation("SelectCharacterResult: migrate to {Host}:{Port}", host, port);
                MigrateToChannel(host, port, charId);
            });
            Game.Session.Send(LoginSender.SelectCharacter(charId));
        }
        else
        {
            // UI-only mode
            EnterGameStage();
        }
    }

    private async void MigrateToChannel(string host, int port, int charId)
    {
        try
        {
            await Game.Session.ConnectChannelAsync(host, port).ConfigureAwait(false);
            Game.Session.Send(LoginSender.MigrateIn(charId, new byte[8]));
            EnterGameStage();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate to channel {Host}:{Port}", host, port);
            _notice?.Show("Connection to game server failed.");
        }
    }

    private void EnterGameStage()
    {
        Game.StageDirector.Replace(new GameStage(
            _loggerFactory.CreateLogger<GameStage>(),
            _loggerFactory, _ui, _map, _sound, Game.CharWz, Game.NpcWz));
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
        if (_selectedSlot < 0) return;
        _notice?.Show("No character to delete.", LoginNoticeOverlay.NoticeType.Ok);
    }

    private void OnPageL()
    {
        if (_page <= 0) return;
        _page--;
        _selectedSlot = -1;
        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;
    }

    private void OnPageR()
    {
        _page++;
        _selectedSlot = -1;
        if (_btSelect != null) _btSelect.Enabled = false;
        if (_btDelete != null) _btDelete.Enabled = false;
    }

    private void GoBack()
    {
        Game.StageDirector.Replace(new WorldSelectStage(
            _loggerFactory.CreateLogger<WorldSelectStage>(),
            _loggerFactory, _ui, _map, _sound,
            _scene?.Camera ?? Vector2.Zero, _loginCameraOffset));
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
            var root = _ui!.GetItem(path) as WzProperty;
            if (root is null) return null;
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
        reg.Register(new DebugItem(DebugCat, "SelectedWorld", () => _selectedWorldPos, v => _selectedWorldPos = v));
        reg.Register(new DebugItem(DebugCat, "StepIndicator", () => _stepIndicatorPos, v => _stepIndicatorPos = v));
        reg.Register(new DebugItem(DebugCat, "CharInfo panel", () => _charInfoPos, v => _charInfoPos = v));
        reg.Register(new DebugItem(DebugCat, "Slot base (slot 0)", () => _slotBase, v => _slotBase = v));
        reg.Register(new DebugItem(DebugCat, "BtSelect", () => _btSelectPos, v => _btSelectPos = v));
        reg.Register(new DebugItem(DebugCat, "BtNew", () => _btNewPos, v => _btNewPos = v));
        reg.Register(new DebugItem(DebugCat, "BtDelete", () => _btDeletePos, v => _btDeletePos = v));
        reg.Register(new DebugItem(DebugCat, "BtPageL", () => _btPageLPos, v => _btPageLPos = v));
        reg.Register(new DebugItem(DebugCat, "BtPageR", () => _btPageRPos, v => _btPageRPos = v));
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        foreach (var name in new[]
        {
            "Camera offset (SP +)", "SelectedWorld", "StepIndicator", "CharInfo panel",
            "Slot base (slot 0)", "BtSelect", "BtNew", "BtDelete", "BtPageL", "BtPageR",
        })
            reg.Unregister(DebugCat, name);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
