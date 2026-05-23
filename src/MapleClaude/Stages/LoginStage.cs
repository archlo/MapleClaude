using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Map;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
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
/// Real v95 login stage. Renders <c>UI.wz/MapLogin1.img</c> as the backdrop
/// (positioned to show the title section at the BOTTOM of the map) with the
/// signboard panel overlaid in the centre. Uses the correct English v95
/// button set under <c>Login.img/Title/*</c>. Plays the login BGM
/// (<c>Sound.wz/BgmUI.img/Title</c>) on loop. ID and PW fields accept typed
/// input; buttons are clickable but don't yet send packets.
/// </summary>
public sealed class LoginStage : Stage
{
    private readonly ILogger<LoginStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;

    private WzTextureLoader? _loader;
    private MapScene? _scene;

    private WzSprite? _signboard;
    private WzSprite? _idBg;
    private WzSprite? _pwBg;
    private WzSprite? _commonFrame;

    private Button? _btLogin;
    private Button? _btLoginIdSave;
    private Button? _btLoginIdLost;
    private Button? _btPasswdLost;
    private Button? _btNew;
    private Button? _btHomePage;
    private Button? _btQuit;
    private Checkbox? _saveCheck;
    private TextField? _idField;
    private TextField? _pwField;

    private LoginWaitOverlay? _loginWait;
    private LoginNoticeOverlay? _notice;
    private QuitConfirmOverlay? _quitConfirm;

    private readonly List<Button> _allButtons = new();

    // Network status text shown above the panel while waiting for handshake / CheckPasswordResult.
    private string _statusLabel = string.Empty;
    private bool _connecting;

    // Mutable layout state exposed to the debug window for live tuning.
    // Values were nailed down by hand using drag-mode in the debug window
    // and then squared up (matching rows share Y; top/bottom rows space evenly).
    private Vector2 _cameraOffset = new(27, -8);
    private Vector2 _signboardCenter = new(371, 316);

    // Per-widget offsets FROM the signboard top-left. ApplyLayout (called every
    // frame in Update) re-applies these so debug-window edits take effect live.
    private Vector2 _idOffset = new(15, 16);
    private Vector2 _pwOffset = new(15, 41);
    private Vector2 _loginBtnOffset = new(181, 15);
    private Vector2 _checkOffset = new(17, 69);
    // Top row buttons (save / find-ID / find-PW): shared Y, 70 px X spacing.
    private Vector2 _saveTextOffset = new(33, 69);
    private Vector2 _lostIdOffset = new(103, 69);
    private Vector2 _lostPwOffset = new(173, 69);
    // Bottom row buttons (new / home / quit): shared Y, 75 px X spacing.
    private Vector2 _newOffset = new(13, 93);
    private Vector2 _homeOffset = new(88, 93);
    private Vector2 _quitOffset = new(163, 93);

    public LoginStage(
        ILogger<LoginStage> logger,
        ILoggerFactory loggerFactory,
        WzPackage? ui,
        WzPackage? map,
        WzPackage? sound)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _ui = ui;
        _map = map;
        _sound = sound;
    }

    public override void OnEnter(MapleClaudeGame game)
    {
        base.OnEnter(game);
        _loader = new WzTextureLoader(GraphicsDevice);

        if (_ui is null || _map is null)
        {
            _logger.LogError("LoginStage: WZ packages unavailable (ui={UiOk}, map={MapOk}).",
                _ui is not null, _map is not null);
            return;
        }

        // Map scene — load MapLogin1.img and place camera at the bottom (title section).
        if (_ui.GetItem("MapLogin1.img") is WzImage loginMap)
        {
            _scene = new MapScene(_loggerFactory.CreateLogger<MapScene>(), _map, _loader);
            _scene.Load(loginMap.Root);

            ApplyCamera();

            if (_sound is not null && _scene.BgmPath is { Length: > 0 } bgmPath)
            {
                StartBgm(bgmPath);
            }
        }

        // UI assets — the correct v95 GMS English set under Login.img/Title.
        _signboard = LoadCanvas("Login.img/Title/signboard");
        _idBg = LoadCanvas("Login.img/Title/ID");
        _pwBg = LoadCanvas("Login.img/Title/PW");
        _commonFrame = LoadCanvas("Login.img/Common/frame");
        var checkUnchecked = LoadCanvas("Login.img/Title/check/0");
        var checkChecked = LoadCanvas("Login.img/Title/check/1");

        // Signboard centre default comes from the field initializer (the
        // user-nailed value); ApplyLayout in Update re-applies every frame
        // so live debug-window edits take effect immediately.
        var signCenter = _signboardCenter;
        var signTopLeft = _signboard is null
            ? signCenter
            : signCenter - _signboard.Origin;

        // Initial positions — ApplyLayout (every frame in Update) re-derives these
        // from the live offset fields so debug edits take effect immediately.
        var idPos = signTopLeft + _idOffset;
        var pwPos = signTopLeft + _pwOffset;
        var loginBtnPos = signTopLeft + _loginBtnOffset;
        var checkPos = signTopLeft + _checkOffset;
        var saveTextPos = signTopLeft + _saveTextOffset;
        var lostIdPos = signTopLeft + _lostIdOffset;
        var lostPwPos = signTopLeft + _lostPwOffset;
        var newPos = signTopLeft + _newOffset;
        var homePos = signTopLeft + _homeOffset;
        var quitPos = signTopLeft + _quitOffset;

        _btLogin = MakeButton("BtLogin", loginBtnPos, BeginLogin);
        _btLoginIdSave = MakeButton("BtLoginIDSave", saveTextPos, () =>
        {
            // The "Save Login ID" text-button acts as a click-target for the
            // checkbox to its left — clicking it toggles the same state.
            if (_saveCheck != null)
            {
                _saveCheck.IsChecked = !_saveCheck.IsChecked;
            }
        });
        // These open Nexon web flows on the original client; against a Kinoko
        // server there is no such site, so they give the user clear feedback
        // instead of silently doing nothing. Account creation is server-side
        // (Kinoko auto-registers an unknown ID/PW on first login).
        _btLoginIdLost = MakeButton("BtLoginIDLost", lostIdPos,
            () => _notice?.Show("ID recovery isn't available on this server.", LoginNoticeOverlay.NoticeType.Ok));
        _btPasswdLost = MakeButton("BtPasswdLost", lostPwPos,
            () => _notice?.Show("Password recovery isn't available on this server.", LoginNoticeOverlay.NoticeType.Ok));
        _btNew = MakeButton("BtNew", newPos,
            () => _notice?.Show("New accounts are created automatically on first login.", LoginNoticeOverlay.NoticeType.Ok));
        _btHomePage = MakeButton("BtHomePage", homePos,
            () => _notice?.Show("No homepage is configured for this server.", LoginNoticeOverlay.NoticeType.Ok));
        _btQuit = MakeButton("BtQuit", quitPos, () => _quitConfirm!.IsVisible = true);

        _saveCheck = new Checkbox
        {
            Position = checkPos,
            Unchecked = checkUnchecked,
            Checked = checkChecked,
            HitSize = 12,
            IsChecked = false,
        };

        _idField = new TextField
        {
            Position = idPos,
            Width = _idBg?.Width ?? 160,
            Height = _idBg?.Height ?? 24,
            Background = _idBg,
            MaxLength = 16,
            Font = Game.Font,
        };
        _pwField = new TextField
        {
            Position = pwPos,
            Width = _pwBg?.Width ?? 160,
            Height = _pwBg?.Height ?? 23,
            Background = _pwBg,
            IsPassword = true,
            MaxLength = 16,
            Font = Game.Font,
        };

        // Default focus on the ID field.
        _idField.IsFocused = true;

        var center = new Vector2(GraphicsDevice.PresentationParameters.BackBufferWidth / 2f,
                                 GraphicsDevice.PresentationParameters.BackBufferHeight / 2f);
        _loginWait = new LoginWaitOverlay(_loader!, _ui, Game.Font, center)
        {
            OnCancel = () =>
            {
                _loginWait!.IsVisible = false;
                _ = Game.Session.DisconnectAsync();
            },
        };
        _notice = new LoginNoticeOverlay(_loader!, _ui, Game.Font, center);
        _quitConfirm = new QuitConfirmOverlay(_loader!, _ui, Game.Font, center)
        {
            OnYes = () => Game.Exit(),
            OnNo = () => _quitConfirm!.IsVisible = false,
        };

        RegisterDebugItems();
        ApplyLayout();

        Game.LoginHandlers.OnCheckPasswordResult += OnCheckPasswordResult;
        Game.Session.HandshakeReceived += OnHandshake;
        Game.Session.Disconnected += OnDisconnected;

        _logger.LogInformation(
            "LoginStage: scene={SceneOk} signboard={Sign} idField={Id} pwField={Pw} buttons={ButtonCount}",
            _scene != null, _signboard != null, _idBg != null, _pwBg != null, _allButtons.Count);
    }

    public override void OnExit()
    {
        UnregisterDebugItems();
        Game.LoginHandlers.OnCheckPasswordResult -= OnCheckPasswordResult;
        Game.Session.HandshakeReceived -= OnHandshake;
        Game.Session.Disconnected -= OnDisconnected;
        Game.AudioPlayer.Stop();
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    private void BeginLogin()
    {
        if (_connecting)
        {
            return;
        }
        var id = _idField?.Text ?? string.Empty;
        var pw = _pwField?.Text ?? string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            _statusLabel = "Please enter your ID.";
            return;
        }
        _connecting = true;
        _statusLabel = $"Connecting to {Game.LoginHost}:{Game.LoginPort}...";
        _logger.LogInformation("Login clicked: id='{Id}' connecting to {Host}:{Port}", id, Game.LoginHost, Game.LoginPort);

        // Show the modal LoginWait overlay; cancel disconnects + clears status.
        if (_loginWait != null)
        {
            _loginWait.IsVisible = true;
            _loginWait.OnCancel = () =>
            {
                _loginWait.IsVisible = false;
                _ = Game.Session.DisconnectAsync();
                _statusLabel = "Login cancelled.";
                _connecting = false;
            };
        }

        // ScrollUp SFX while we open the socket.
        if (_sound?.GetItem("UI.img/ScrollUp") is WzSound scrollUp)
        {
            Game.AudioPlayer.PlayEffect(scrollUp);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Game.Session.ConnectAsync(Game.LoginHost, Game.LoginPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectAsync failed");
                _statusLabel = "Connect failed.";
                _connecting = false;
                if (_loginWait != null)
                {
                    _loginWait.IsVisible = false;
                }
            }
        });
    }

    private void OnHandshake(HandshakeInfo info)
    {
        var id = _idField?.Text ?? string.Empty;
        var pw = _pwField?.Text ?? string.Empty;
        _logger.LogInformation("Handshake received; sending CheckPassword");

        // CheckPassword(1) wire (Kinoko LoginHandler.handleCheckPassword):
        //   string id, string pwd, byte[16] machineId, int gameRoomClient=0,
        //   byte gameStartMode=2, byte worldId=0, byte channelId=0, byte[4] partnerCode
        var p = OutPacket.Of(InHeader.CheckPassword);
        p.WriteString(id);
        p.WriteString(pw);
        p.WriteBytes(Game.Session.MachineId);
        p.WriteInt(0);          // gameRoomClient
        p.WriteByte(2);         // gameStartMode (regular login)
        p.WriteByte(0);         // worldId
        p.WriteByte(0);         // channelId
        p.WriteBytes(new byte[4]);
        Game.Session.Send(p);
        _statusLabel = "Authenticating...";
    }

    private void OnCheckPasswordResult(CheckPasswordResultArgs args)
    {
        if (_loginWait != null)
        {
            _loginWait.IsVisible = false;
        }

        if (!args.Success)
        {
            _statusLabel = args.ResultCode switch
            {
                4 => "Incorrect password.",
                5 => "Account not registered.",
                7 => "Already connected.",
                9 => "Login error.",
                _ => $"Login failed (code {args.ResultCode}).",
            };
            _connecting = false;
            _ = Game.Session.DisconnectAsync();
            return;
        }
        _statusLabel = "Login OK — fetching worlds...";
        var startCam = _scene?.Camera ?? Vector2.Zero;
        Game.StageDirector.Replace(new WorldSelectStage(
            _loggerFactory.CreateLogger<WorldSelectStage>(),
            _loggerFactory, _ui, _map, _sound, startCam, _cameraOffset));
    }

    private void OnDisconnected(Exception? ex)
    {
        if (!_connecting)
        {
            return;
        }
        if (_loginWait != null)
        {
            _loginWait.IsVisible = false;
        }
        _statusLabel = ex != null ? "Disconnected: " + ex.Message : "Disconnected.";
        _connecting = false;
    }

    public override void Update(GameTime gameTime)
    {
        // Re-apply tunables every frame so live edits via the debug window
        // take effect immediately.
        ApplyCamera();
        ApplyLayout();

        // Inbound packets are drained once per tick by MapleClaudeGame.Update.
        _notice?.Update(gameTime);
        _loginWait?.Update(gameTime);
        _quitConfirm?.Update(gameTime);

        _idField?.Update(gameTime);
        _pwField?.Update(gameTime);

        // Drive cursor hover state: any button under the mouse counts as clickable.
        var mouse = Mouse.GetState();
        var anyHover = false;
        foreach (var b in _allButtons)
        {
            if (b.Bounds.Contains(mouse.X, mouse.Y))
            {
                anyHover = true;
                break;
            }
        }
        if (!anyHover && _saveCheck != null && _saveCheck.Bounds.Contains(mouse.X, mouse.Y))
        {
            anyHover = true;
        }
        Game.Cursor?.SetHover(anyHover);
    }

    private void ApplyCamera()
    {
        if (_scene is null)
        {
            return;
        }
        var sp = _scene.StartPoint ?? new Vector2(0, -8);
        _scene.Camera = sp + _cameraOffset;
    }

    private void ApplyLayout()
    {
        if (_signboard is null)
        {
            return;
        }
        var signTopLeft = _signboardCenter - _signboard.Origin;
        if (_idField != null)
        {
            _idField.Position = signTopLeft + _idOffset;
        }
        if (_pwField != null)
        {
            _pwField.Position = signTopLeft + _pwOffset;
        }
        if (_btLogin != null)
        {
            _btLogin.Position = signTopLeft + _loginBtnOffset;
        }
        if (_saveCheck != null)
        {
            _saveCheck.Position = signTopLeft + _checkOffset;
        }
        if (_btLoginIdSave != null)
        {
            _btLoginIdSave.Position = signTopLeft + _saveTextOffset;
        }
        if (_btLoginIdLost != null)
        {
            _btLoginIdLost.Position = signTopLeft + _lostIdOffset;
        }
        if (_btPasswdLost != null)
        {
            _btPasswdLost.Position = signTopLeft + _lostPwOffset;
        }
        if (_btNew != null)
        {
            _btNew.Position = signTopLeft + _newOffset;
        }
        if (_btHomePage != null)
        {
            _btHomePage.Position = signTopLeft + _homeOffset;
        }
        if (_btQuit != null)
        {
            _btQuit.Position = signTopLeft + _quitOffset;
        }
    }

    private const string DebugCategory = "Login";

    private Vector2 SignTopLeft() => _signboard is null
        ? _signboardCenter
        : _signboardCenter - _signboard.Origin;

    private void RegisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        // Camera offset is in map space, not screen space — not draggable.
        reg.Register(new DebugItem(DebugCategory, "Camera offset (SP +)",
            () => _cameraOffset, v => _cameraOffset = v)
        { Draggable = false });
        // Signboard centre IS a screen-space position — default mapping is fine.
        reg.Register(new DebugItem(DebugCategory, "Signboard centre",
            () => _signboardCenter, v => _signboardCenter = v));
        // Inner widgets store offsets from signboard top-left; map them to/from
        // screen space so the markers render where the widget actually is and
        // dragging moves the widget under the cursor (the offset is updated as
        // mouse minus signboard top-left).
        reg.Register(new DebugItem(DebugCategory, "ID field offset",
            () => _idOffset, v => _idOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _idOffset,
            SetFromScreen = v => _idOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "PW field offset",
            () => _pwOffset, v => _pwOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _pwOffset,
            SetFromScreen = v => _pwOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtLogin offset",
            () => _loginBtnOffset, v => _loginBtnOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _loginBtnOffset,
            SetFromScreen = v => _loginBtnOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "Save check offset",
            () => _checkOffset, v => _checkOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _checkOffset,
            SetFromScreen = v => _checkOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtLoginIDSave (text) offset",
            () => _saveTextOffset, v => _saveTextOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _saveTextOffset,
            SetFromScreen = v => _saveTextOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtLoginIDLost offset",
            () => _lostIdOffset, v => _lostIdOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _lostIdOffset,
            SetFromScreen = v => _lostIdOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtPasswdLost offset",
            () => _lostPwOffset, v => _lostPwOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _lostPwOffset,
            SetFromScreen = v => _lostPwOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtNew offset",
            () => _newOffset, v => _newOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _newOffset,
            SetFromScreen = v => _newOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtHomePage offset",
            () => _homeOffset, v => _homeOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _homeOffset,
            SetFromScreen = v => _homeOffset = v - SignTopLeft(),
        });
        reg.Register(new DebugItem(DebugCategory, "BtQuit offset",
            () => _quitOffset, v => _quitOffset = v)
        {
            GetScreenPos = () => SignTopLeft() + _quitOffset,
            SetFromScreen = v => _quitOffset = v - SignTopLeft(),
        });
        if (_scene?.StartPoint is { } sp)
        {
            // Read-only display of the SP value for reference.
            reg.Register(new DebugItem(DebugCategory, "Map SP (read-only)",
                () => sp, _ => { })
            { Draggable = false });
        }
    }

    private void UnregisterDebugItems()
    {
        var reg = Game.DebugRegistry;
        reg.Unregister(DebugCategory, "Camera offset (SP +)");
        reg.Unregister(DebugCategory, "Signboard centre");
        reg.Unregister(DebugCategory, "ID field offset");
        reg.Unregister(DebugCategory, "PW field offset");
        reg.Unregister(DebugCategory, "BtLogin offset");
        reg.Unregister(DebugCategory, "Save check offset");
        reg.Unregister(DebugCategory, "BtLoginIDSave (text) offset");
        reg.Unregister(DebugCategory, "BtLoginIDLost offset");
        reg.Unregister(DebugCategory, "BtPasswdLost offset");
        reg.Unregister(DebugCategory, "BtNew offset");
        reg.Unregister(DebugCategory, "BtHomePage offset");
        reg.Unregister(DebugCategory, "BtQuit offset");
        reg.Unregister(DebugCategory, "Map SP (read-only)");
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

        // Login.img/Common/frame — the v95 client border/chrome that frames the
        // playable area. Drawn on top of the map but under the UI panels.
        _commonFrame?.Draw(spriteBatch, new Vector2(w / 2f, h / 2f));

        // Signboard panel — _signboardCenter is the tunable from RegisterDebugItems.
        _signboard?.Draw(spriteBatch, _signboardCenter);

        // Fields + checkbox.
        _idField?.Draw(spriteBatch, Game.WhitePixel);
        _pwField?.Draw(spriteBatch, Game.WhitePixel);
        _saveCheck?.Draw(spriteBatch);

        // Buttons.
        foreach (var b in _allButtons)
        {
            b.Draw(spriteBatch);
        }

        // Connection / error status — drawn below the signboard. Dark pad +
        // yellow text so it's readable against the bright train backdrop.
        if (!string.IsNullOrEmpty(_statusLabel) && Game.Font is not null)
        {
            var font = Game.Font;
            var size = font.Measure(_statusLabel);
            var padX = 6;
            var padY = 2;
            var baseY = _signboardCenter.Y + (_signboard?.Height ?? 0) / 2f + 6;
            var rect = new Rectangle(
                (int)(w / 2f - size.X / 2f) - padX,
                (int)baseY - padY,
                (int)size.X + padX * 2,
                font.LineHeight + padY * 2);
            spriteBatch.Draw(Game.WhitePixel, rect, new Color(0, 0, 0, 180));
            font.Draw(spriteBatch, _statusLabel,
                new Vector2(w / 2f - size.X / 2f, baseY),
                new Color(255, 230, 100));
        }

        // Modal notice (archlo's overlay) — sits above the status label but below the wait/quit overlays.
        _notice?.Draw(spriteBatch, Game.WhitePixel);

        // Overlays — drawn last so they sit on top of everything else.
        _loginWait?.Draw(spriteBatch, Game.WhitePixel);
        _quitConfirm?.Draw(spriteBatch, Game.WhitePixel);
    }

    public override void OnTextInput(char character)
    {
        _idField?.OnTextInput(character);
        _pwField?.OnTextInput(character);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (_notice?.IsVisible == true)    { _notice.HandleMouseButton(x, y, down);    return; }
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.HandleMouseButton(x, y, down); return; }
        if (_loginWait?.IsVisible == true)   { _loginWait.HandleMouseButton(x, y, down);   return; }

        // Buttons first so they can claim the click.
        foreach (var b in _allButtons)
        {
            if (b.HandleMouseButton(x, y, down))
            {
                return;
            }
        }
        if (_saveCheck?.HandleMouseButton(x, y, down) == true)
        {
            return;
        }
        if (_idField?.HandleMouseButton(x, y, down) == true)
        {
            if (_pwField != null)
            {
                _pwField.IsFocused = false;
            }
            return;
        }
        if (_pwField?.HandleMouseButton(x, y, down) == true)
        {
            if (_idField != null)
            {
                _idField.IsFocused = false;
            }
            return;
        }
    }

    public override void OnKeyPress(Keys key)
    {
        if (_notice?.IsVisible == true)      { _notice.OnKeyPress(key);      return; }
        if (_quitConfirm?.IsVisible == true) { _quitConfirm.OnKeyPress(key); return; }
        if (_loginWait?.IsVisible == true)   { _loginWait.OnKeyPress(key);   return; }

        // Give the focused text field first crack at the key (Ctrl-A/C/V/X,
        // arrows, Home/End, Delete). It returns true if it consumed the key.
        var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (_idField?.IsFocused == true && _idField.OnKeyPress(key, kb))
        {
            return;
        }
        if (_pwField?.IsFocused == true && _pwField.OnKeyPress(key, kb))
        {
            return;
        }

        switch (key)
        {
            case Keys.Tab:
                if (_idField != null && _pwField != null)
                {
                    var idHad = _idField.IsFocused;
                    _idField.IsFocused = !idHad;
                    _pwField.IsFocused = idHad;
                }
                break;
            case Keys.Enter:
                _btLogin?.OnClick?.Invoke();
                break;
            // Backspace is intentionally NOT handled here — MonoGame's
            // Window.TextInput already fires '\b' which routes through
            // OnTextInput → TextField.OnTextInput. Handling it again here
            // would delete two characters per press.
        }
    }

    private WzSprite? LoadCanvas(string path)
    {
        try
        {
            return _ui!.GetItem(path) is WzCanvas canvas ? _loader!.Load(canvas) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load WZ canvas at {Path}", path);
            return null;
        }
    }

    private Button MakeButton(string name, Vector2 pos, Action onClick)
    {
        var root = _ui!.GetItem($"Login.img/Title/{name}") as WzProperty;
        var b = new Button(_loader!, root) { Position = pos, OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private void StartBgm(string bgmPath)
    {
        var slash = bgmPath.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return;
        }
        var imgName = bgmPath[..slash] + ".img";
        var soundName = bgmPath[(slash + 1)..];
        if (_sound!.GetItem($"{imgName}/{soundName}") is WzSound sound)
        {
            Game.AudioPlayer.PlayLoop(sound);
        }
        else
        {
            _logger.LogWarning("BGM not found at Sound.wz/{Img}/{Name}", imgName, soundName);
        }
    }
}
