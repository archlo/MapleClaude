using MapleClaude.App;
using MapleClaude.Character;
using MapleClaude.Debug;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Session;
using MapleClaude.Localization;
using MapleClaude.Platform;
using MapleClaude.Render;
using MapleClaude.Settings;
using MapleClaude.Stages;
using MapleClaude.Wz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude;

/// <summary>
/// MonoGame <see cref="Game"/> subclass. Owns the graphics device, sprite batch,
/// white-pixel texture, the <see cref="App.StageDirector"/>, the WZ packages
/// (UI/Map/Sound), the global audio player, and the custom Maple cursor.
/// Boots into <see cref="SplashStage"/> which transitions to
/// <see cref="LoginStage"/> when the splash sequence completes.
/// </summary>
public sealed class MapleClaudeGame : Game
{
    private readonly ILogger<MapleClaudeGame> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GraphicsDeviceManager _graphics;
    private readonly string? _wzDir;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _white = null!;
    private WzPackage? _uiWz;
    private WzPackage? _mapWz;
    private WzPackage? _soundWz;
    private WzPackage? _charWz;
    private WzPackage? _npcWz;
    private WzPackage? _itemWz;
    private WzPackage? _mobWz;
    private WzPackage? _stringWz;
    private WzPackage? _skillWz;
    private WzPackage? _questWz;
    private WzPackage? _etcWz;
    private WzPackage? _baseWz;
    private WzTextureLoader? _cursorLoader;
    private MapleCursor? _cursor;
    private UI.BuiltInFont? _font;
    private UI.BuiltInFont? _basicFont;

    /// <summary>Shared bitmap font for text-input fields, debug overlays, etc.
    /// Built lazily in <see cref="LoadContent"/> from a system font.</summary>
    public UI.BuiltInFont? Font => _font;

    /// <summary>Authentic small UI label font — Arial 12px, the v95 FONT_BASIC face/size, grid-fit
    /// hinted (CharSelect stats, char-create row names). Crisper than <see cref="Font"/> when small.</summary>
    public UI.BuiltInFont? BasicFont => _basicFont;

    // Mouse-state tracking for OnMouseButton events.
    private bool _prevLeftDown;
    private bool _prevRightDown;
    private bool _prevMiddleDown;
    private KeyboardState _prevKeyboard;

    // Drag-mode picker state (only used when DebugRegistry.DragMode is on).
    private DebugItem? _dragTarget;
    private Vector2 _dragOffset;

    // Window/taskbar icon — kept alive on the instance so the icon handle and
    // bitmap pixels aren't GC'd out from under the form.
    private System.Drawing.Bitmap? _iconBitmap;
    private System.Drawing.Icon? _windowIcon;

    // Subclasses the game window's WndProc to keep the IME context attached and
    // drive the Han↔Yeong toggle from raw key messages. See InitializeImeSupport.
    private ImeWindowHook? _imeHook;

    /// <summary>The cursor overlay. Public so stages can call <see cref="MapleCursor.SetHover"/>.</summary>
    public MapleCursor? Cursor => _cursor;

    public StageDirector StageDirector { get; }

    public Texture2D WhitePixel => _white;
    public WzAudioPlayer AudioPlayer { get; }

    /// <summary>Persisted user preferences (keybinds + volumes) at
    /// <c>%APPDATA%/MapleClaude/settings.json</c>.</summary>
    public SettingsStore Settings { get; }

    /// <summary>Bundled UI/system string table (the v95 client StringPool), used
    /// for job names, system messages, and UI labels.</summary>
    public StringPool StringPool { get; }
    public WzPackage? UiWz => _uiWz;
    public WzPackage? MapWz => _mapWz;
    public WzPackage? SoundWz => _soundWz;
    public WzPackage? CharWz => _charWz;
    public WzPackage? NpcWz => _npcWz;
    public WzPackage? ItemWz => _itemWz;
    public WzPackage? MobWz  => _mobWz;
    public WzPackage? StringWz => _stringWz;
    public WzPackage? SkillWz => _skillWz;
    /// <summary>Etc.wz — holds MakeCharInfo.img (character-creation appearance options).</summary>
    public WzPackage? EtcWz => _etcWz;
    /// <summary>Base.wz — holds zmap.img (the global avatar layer draw order).</summary>
    public WzPackage? BaseWz => _baseWz;
    // Back-compat alias for stages that referenced the longer name.
    public WzPackage? CharacterWz => _charWz;

    /// <summary>Display-name lookup over String.wz (items/skills/maps/mobs/npcs).</summary>
    public NameService Names { get; }

    /// <summary>Skill data (max level, passive, MP/cooldown/duration, icon) from Skill.wz.</summary>
    public SkillInfoService Skills { get; }

    /// <summary>Shared registry of tunable items exposed to the debug window.</summary>
    public DebugRegistry DebugRegistry { get; }

    /// <summary>Network session used by every stage that wants to send/receive packets.</summary>
    public ClientSession Session { get; }

    /// <summary>Wired login-server S→C handlers (events).</summary>
    public LoginHandlers LoginHandlers { get; }

    /// <summary>Wired channel-server S→C handlers (events).</summary>
    public FieldHandlers FieldHandlers { get; }

    /// <summary>Migration coordinator (login → channel handoff).</summary>
    public MigrationCoordinator Migration { get; }

    /// <summary>The selected character's id, cached at character select and
    /// confirmed on each SetField — reused for in-game channel transfers.</summary>
    public int CharacterId { get; set; }

    /// <summary>Resolved login server host (env <c>MAPLECLAUDE_LOGIN_HOST</c>, default 127.0.0.1).</summary>
    public string LoginHost { get; }

    /// <summary>Resolved login server port (env <c>MAPLECLAUDE_LOGIN_PORT</c>, default 8484).</summary>
    public int LoginPort { get; }

    public void ResizeWindow(int width, int height)
    {
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.ApplyChanges();
    }

    public MapleClaudeGame(
        ILogger<MapleClaudeGame> logger,
        ILoggerFactory loggerFactory,
        IConfiguration config,
        DebugRegistry debugRegistry,
        ClientSession session,
        LoginHandlers loginHandlers,
        FieldHandlers fieldHandlers,
        MigrationCoordinator migration)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _wzDir = ResolveWzDir(config);
        AudioPlayer = new WzAudioPlayer(loggerFactory.CreateLogger<WzAudioPlayer>());
        Settings = new SettingsStore(loggerFactory.CreateLogger<SettingsStore>());
        StringPool = new StringPool(loggerFactory.CreateLogger<StringPool>(), Settings.Load().Language);
        Names = new NameService(() => _stringWz, loggerFactory.CreateLogger<NameService>(), () => _questWz);
        Skills = new SkillInfoService(() => _skillWz, loggerFactory.CreateLogger<SkillInfoService>());
        DebugRegistry = debugRegistry;
        Session = session;
        LoginHandlers = loginHandlers;
        FieldHandlers = fieldHandlers;
        Migration = migration;
        LoginHost = config["LOGIN_HOST"] ?? "127.0.0.1";
        LoginPort = int.TryParse(config["LOGIN_PORT"], out var port) ? port : 8484;

        // v95 native resolution is 800×600; larger windows leave black borders
        // around the title map (which is laid out for 800×600).
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
            PreferredBackBufferFormat = SurfaceFormat.Color,
            SynchronizeWithVerticalRetrace = true,
            IsFullScreen = false,
            HardwareModeSwitch = false,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = false; // Maple cursor overlay replaces the OS cursor.
        Window.Title = "MapleClaude";
        Window.AllowUserResizing = false;
        StageDirector = new StageDirector(this);

        // Window focus → pause/resume audio.
        Activated += (_, _) => AudioPlayer.Resume();
        Deactivated += (_, _) => AudioPlayer.Pause();

        // Unicode text input → active stage.
        Window.TextInput += (_, e) => StageDirector.OnTextInput(e.Character);
    }

    protected override void Initialize()
    {
        _logger.LogInformation(
            "MapleClaudeGame initializing — backbuffer {Width}x{Height}, wzDir={WzDir}",
            _graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight, _wzDir ?? "(none)");
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _white = new Texture2D(GraphicsDevice, 1, 1, mipmap: false, SurfaceFormat.Color);
        _white.SetData([Color.White]);

        // Runtime bitmap font. Malgun Gothic ships with Windows 7+ and has
        // full Hangul + Latin coverage so typed Korean (IME-composed) renders
        // alongside English. Non-ASCII glyphs are rasterised lazily on first
        // use, so startup stays fast even though Hangul has 11 172 syllables.
        try
        {
            _font = new UI.BuiltInFont(GraphicsDevice, "Malgun Gothic", 11f);
        }
        catch
        {
            try
            {
                _font = new UI.BuiltInFont(GraphicsDevice, "Microsoft Sans Serif", 11f);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build runtime font — TextField will fall back to dot placeholders");
            }
        }

        // Authentic small UI label font: Arial 12px (the v95 FONT_BASIC face/size), grid-fit hinted
        // for sharpness at small sizes. Falls back to the main font if Arial is unavailable.
        try
        {
            _basicFont = new UI.BuiltInFont(GraphicsDevice, "Arial", 12f,
                System.Drawing.GraphicsUnit.Pixel, System.Drawing.Text.TextRenderingHint.AntiAliasGridFit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Arial basic font — falling back to main font");
            _basicFont = _font;
        }

        TryOpenWzPackages();

        // Cursor overlay. Owns its own loader so its texture lifetime doesn't
        // tie to a stage's loader.
        _cursorLoader = new WzTextureLoader(GraphicsDevice);
        _cursor = new MapleCursor(_loggerFactory.CreateLogger<MapleCursor>(), _cursorLoader);
        _cursor.Load(_uiWz);

        // Window/taskbar icon — set after the window exists (LoadContent is safe).
        TrySetWindowIcon();

        // Make the window participate in the Windows IME (Korean Han↔Yeong, etc.).
        // Must run independently of the icon code above (which is gated on an embedded
        // resource + a managed Form), so it always executes.
        InitializeImeSupport();

        // Boot into the splash sequence.
        StageDirector.Replace(new SplashStage(
            _loggerFactory.CreateLogger<SplashStage>(),
            _loggerFactory,
            _uiWz, _mapWz, _soundWz));
        _logger.LogInformation("LoadContent complete — splash stage active");
    }

    // Ensure the game window has an Input Method Editor context attached.
    // Some hosting configurations (e.g. MonoGame's underlying window setup)
    // can leave a window with no IME context, which makes IME composition
    // — and the Korean Right-Alt toggle — silently do nothing.
    private void EnsureImeContext(IntPtr hwnd)
    {
        try
        {
            var existing = ImmGetContext(hwnd);
            if (existing == IntPtr.Zero)
            {
                var created = ImmCreateContext();
                if (created != IntPtr.Zero)
                {
                    ImmAssociateContext(hwnd, created);
                    _logger.LogInformation("Attached fresh IME context to game window");
                }
            }
            else
            {
                ImmReleaseContext(hwnd, existing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureImeContext failed");
        }
    }

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmCreateContext();

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int lpfdwConversion, out int lpfdwSentence);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(IntPtr hIMC, int fdwConversion, int fdwSentence);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

    [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    private const int ImeCmodeNative = 0x0001;        // Korean Hangul / Japanese Hiragana / etc.
    private const int GcsCompStr = 0x0008;            // the in-progress (un-committed) composition string
    private const int GcsResultStr = 0x0800;          // the committed result string

    /// <summary>Reads a UTF-16 composition string (e.g. <c>GCS_COMPSTR</c>) from an input context.</summary>
    private static string GetCompositionString(IntPtr himc, int gcsFlag)
    {
        var byteLen = ImmGetCompositionStringW(himc, gcsFlag, null, 0);
        if (byteLen <= 0)
        {
            return string.Empty;
        }
        var buf = new byte[byteLen];
        ImmGetCompositionStringW(himc, gcsFlag, buf, byteLen);
        return System.Text.Encoding.Unicode.GetString(buf, 0, byteLen);
    }

    /// <summary>
    /// Toggle the IME between Hangul and English (Yeong) on the game window's own input
    /// context. We act directly on the window's HIMC — <c>ImmSetOpenStatus</c> (Korean is
    /// "open" for Hangul, "closed" for English) plus the <c>IME_CMODE_NATIVE</c> conversion
    /// bit — rather than the <c>WM_IME_CONTROL</c>/default-IME-window path, which did not
    /// persist on our window. Reads the state back so the result is observable in the log
    /// and the taskbar 한 ↔ A indicator follows.
    /// </summary>
    private void ToggleNativeIme()
    {
        try
        {
            var hwnd = Window.Handle;
            var himc = ImmGetContext(hwnd);
            if (himc == IntPtr.Zero)
            {
                // No input context on the window yet — create + associate one, then refetch.
                EnsureImeContext(hwnd);
                himc = ImmGetContext(hwnd);
            }
            if (himc == IntPtr.Zero)
            {
                _logger.LogWarning("ToggleNativeIme: window has no IME context (ImmGetContext == 0)");
                return;
            }
            try
            {
                ImmGetConversionStatus(himc, out var conv, out var sentence);
                var open = ImmGetOpenStatus(himc);
                var goNative = !(open && (conv & ImeCmodeNative) != 0);
                if (goNative)
                {
                    // -> Hangul: open the IME and set the native-conversion bit.
                    ImmSetOpenStatus(himc, true);
                    ImmSetConversionStatus(himc, conv | ImeCmodeNative, sentence);
                }
                else
                {
                    // -> English: clear the native bit and close the IME.
                    ImmSetConversionStatus(himc, conv & ~ImeCmodeNative, sentence);
                    ImmSetOpenStatus(himc, false);
                }
                // Temporary diagnostic (drop to Debug once verified): read the state back.
                ImmGetConversionStatus(himc, out var conv2, out _);
                var open2 = ImmGetOpenStatus(himc);
                _logger.LogInformation(
                    "IME toggle: himc=0x{H:X} open {O0}->{O1} conv 0x{C0:X}->0x{C1:X} native={N}",
                    himc.ToInt64(), open, open2, conv, conv2, (conv2 & ImeCmodeNative) != 0);
            }
            finally
            {
                ImmReleaseContext(hwnd, himc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ToggleNativeIme failed");
        }
    }

    // Window-message + virtual-key constants for the IME WndProc hook.
    private const int WmSetFocus = 0x0007;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmInputLangChange = 0x0051;
    private const int WmImeStartComposition = 0x010D;
    private const int WmImeComposition = 0x010F;
    private const int WmImeEndComposition = 0x010E;
    private const int VkHangul = 0x15;   // Korean Han/Yeong key (== VK_KANA)
    private const int VkMenu = 0x12;     // generic Alt (Right Alt arrives here + extended bit)
    private const int VkRMenu = 0xA5;    // Right Alt (some configs deliver this directly)

    /// <summary>
    /// Make the game window participate in the Windows IME so Korean (and other) input
    /// methods are live for it. Without an associated IME context the OS-native Right-Alt
    /// Han↔Yeong toggle and Hangul composition do nothing for our window (the taskbar
    /// 한/A indicator never changes). MonoGame.WindowsDX makes no imm32 calls, so we own
    /// this. Decoupled from <see cref="TrySetWindowIcon"/> (which is gated on the embedded
    /// icon + a managed Form) so it always runs; the <see cref="ImeWindowHook"/> subclasses
    /// the raw HWND, working even when Control.FromHandle returns null.
    /// </summary>
    private void InitializeImeSupport()
    {
        try
        {
            var hwnd = Window.Handle;

            // Best-effort: if the window resolves to a managed Form, don't let WinForms
            // force an IME mode (NoControl = honour the user's system hotkeys).
            if (System.Windows.Forms.Control.FromHandle(hwnd) is System.Windows.Forms.Form form)
            {
                form.ImeMode = System.Windows.Forms.ImeMode.NoControl;
                _logger.LogInformation("IME: window resolved to a managed Form; ImeMode=NoControl");
            }
            else
            {
                _logger.LogInformation("IME: Control.FromHandle returned null (unmanaged HWND) — relying on the WndProc hook");
            }

            EnsureImeContext(hwnd);

            _imeHook = new ImeWindowHook(this);
            _imeHook.AssignHandle(hwnd);
            _logger.LogInformation("IME: WndProc hook attached to window 0x{Hwnd:X}", hwnd.ToInt64());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InitializeImeSupport failed");
        }
    }

    /// <summary>
    /// Subclasses the game window's WndProc to (a) keep its IME context associated across
    /// focus / input-language changes and (b) drive the Han↔Yeong toggle from the raw key
    /// message — more reliable than MonoGame's polled keyboard state, which doesn't surface
    /// Alt/IME keys consistently. Messages are never consumed: <c>base.WndProc</c> always
    /// runs, so MonoGame's WM_CHAR → TextInput keeps delivering composed Hangul.
    /// </summary>
    private sealed class ImeWindowHook : System.Windows.Forms.NativeWindow
    {
        private readonly MapleClaudeGame _owner;
        public ImeWindowHook(MapleClaudeGame owner) => _owner = owner;

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            switch (m.Msg)
            {
                case WmSetFocus:
                case WmInputLangChange:
                    _owner.EnsureImeContext(m.HWnd);
                    break;
                case WmImeStartComposition:
                    UI.TextField.SetActiveComposition(string.Empty);
                    break;
                case WmImeEndComposition:
                    UI.TextField.SetActiveComposition(string.Empty);
                    break;
                case WmImeComposition:
                {
                    // Surface the live (un-committed) composition string so the focused field
                    // can show the syllable being typed. The committed result still arrives as
                    // WM_CHAR via base.WndProc → MonoGame TextInput, so we don't insert it here.
                    if ((m.LParam.ToInt64() & GcsCompStr) != 0)
                    {
                        var himc = ImmGetContext(m.HWnd);
                        if (himc != IntPtr.Zero)
                        {
                            try
                            {
                                UI.TextField.SetActiveComposition(GetCompositionString(himc, GcsCompStr));
                            }
                            finally
                            {
                                ImmReleaseContext(m.HWnd, himc);
                            }
                        }
                    }
                    break;
                }
                case WmKeyDown:
                case WmSysKeyDown:
                {
                    var vk = m.WParam.ToInt32();
                    // Right Alt reports as generic VK_MENU with the extended-key bit
                    // (lParam bit 24) set; left Alt does not. Also accept the dedicated
                    // Korean key (VK_HANGUL) and a direct VK_RMENU if a config sends it.
                    var extended = (m.LParam.ToInt64() & 0x0100_0000) != 0;
                    if (vk == VkHangul || vk == VkRMenu || (vk == VkMenu && extended))
                    {
                        _owner._logger.LogInformation("IME hook: toggle key down vk=0x{Vk:X} ext={Ext}", vk, extended);
                        _owner.ToggleNativeIme();
                    }
                    break;
                }
            }
            base.WndProc(ref m);
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            using var stream = typeof(MapleClaudeGame).Assembly
                .GetManifestResourceStream("MapleClaude.Icon.png");
            if (stream is null)
            {
                _logger.LogWarning("Embedded resource MapleClaude.Icon.png not found — window icon unchanged");
                return;
            }
            _iconBitmap = new System.Drawing.Bitmap(stream);
            _windowIcon = System.Drawing.Icon.FromHandle(_iconBitmap.GetHicon());
            if (System.Windows.Forms.Control.FromHandle(Window.Handle) is System.Windows.Forms.Form form)
            {
                form.Icon = _windowIcon;
                form.ShowIcon = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set window icon from Icon.png");
        }
    }

    protected override void UnloadContent()
    {
        _imeHook?.ReleaseHandle();
        Session.Dispose();
        AudioPlayer.Dispose();
        _cursorLoader?.Dispose();
        _uiWz?.Dispose();
        _mapWz?.Dispose();
        _soundWz?.Dispose();
        _charWz?.Dispose();
        _npcWz?.Dispose();
        _itemWz?.Dispose();
        _etcWz?.Dispose();
        _baseWz?.Dispose();
        _spriteBatch?.Dispose();
        _white?.Dispose();
        _windowIcon?.Dispose();
        _iconBitmap?.Dispose();
        _font?.Dispose();
        if (!ReferenceEquals(_basicFont, _font)) _basicFont?.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        // Drain inbound network packets first so handlers see live state in this tick.
        Session.DrainInbound();

        var keyboard = Keyboard.GetState();
        // ESC is routed to the active stage via OnKeyPress below (e.g. quit-confirm
        // on the login/game screens, back-navigation on world/char select) — it must
        // NOT hard-exit the process here.

        var mouse = Mouse.GetState();
        var leftDown = mouse.LeftButton == ButtonState.Pressed;

        if (DebugRegistry.DragMode)
        {
            // Drag-mode hijacks left-click: pick the nearest registered tunable
            // and follow the cursor while held. Right/middle still fire normally.
            HandleDragMode(mouse.X, mouse.Y, leftDown);
            _prevLeftDown = leftDown;
            DispatchMouse(mouse.RightButton == ButtonState.Pressed, ref _prevRightDown, mouse.X, mouse.Y, MouseButton.Right);
            DispatchMouse(mouse.MiddleButton == ButtonState.Pressed, ref _prevMiddleDown, mouse.X, mouse.Y, MouseButton.Middle);
        }
        else
        {
            // Mouse button edge detection → OnMouseButton.
            DispatchMouse(leftDown, ref _prevLeftDown, mouse.X, mouse.Y, MouseButton.Left);
            DispatchMouse(mouse.RightButton == ButtonState.Pressed, ref _prevRightDown, mouse.X, mouse.Y, MouseButton.Right);
            DispatchMouse(mouse.MiddleButton == ButtonState.Pressed, ref _prevMiddleDown, mouse.X, mouse.Y, MouseButton.Middle);
        }

        // Keyboard edge detection → OnKeyPress (for keys that aren't covered by TextInput).
        var pressedKeys = keyboard.GetPressedKeys();
        foreach (var k in pressedKeys)
        {
            if (!_prevKeyboard.IsKeyDown(k))
            {
                // The Korean IME Han↔Yeong toggle (Right Alt / VK_HANGUL) is handled at the
                // window-message level in ImeWindowHook.WndProc, which is reliable for Alt/IME
                // keys that MonoGame's polled keyboard state doesn't surface consistently.
                StageDirector.OnKeyPress(k);
            }
        }
        _prevKeyboard = keyboard;

        StageDirector.Update(gameTime);
        _cursor?.Update(gameTime);
        base.Update(gameTime);
    }

    private void DispatchMouse(bool down, ref bool prevDown, int x, int y, MouseButton button)
    {
        if (down != prevDown)
        {
            StageDirector.OnMouseButton(x, y, down, button);
            prevDown = down;
            if (button == MouseButton.Left)
            {
                _cursor?.SetClick(down);
            }
        }
    }

    private void HandleDragMode(int mouseX, int mouseY, bool leftDown)
    {
        var mousePos = new Vector2(mouseX, mouseY);
        if (leftDown && !_prevLeftDown)
        {
            // Pick the nearest draggable item within 60 pixels (screen space).
            DebugItem? nearest = null;
            var bestDist = 60f;
            foreach (var item in DebugRegistry.Snapshot())
            {
                if (!item.Draggable)
                {
                    continue;
                }
                var d = Vector2.Distance(item.EffectiveScreenPos(), mousePos);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = item;
                }
            }
            if (nearest != null)
            {
                _dragTarget = nearest;
                _dragOffset = nearest.EffectiveScreenPos() - mousePos;
            }
        }
        else if (leftDown && _dragTarget != null)
        {
            _dragTarget.ApplyScreenPos(mousePos + _dragOffset);
        }
        else if (!leftDown && _dragTarget != null)
        {
            _dragTarget = null;
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp);
        StageDirector.Draw(gameTime, _spriteBatch);
        DrawDebugMarkers();
        _cursor?.Draw(_spriteBatch);
        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawDebugMarkers()
    {
        if (!DebugRegistry.DragMode)
        {
            return;
        }
        const int size = 8;
        var active = new Color(255, 220, 40);
        var idle = new Color(255, 70, 70);
        foreach (var item in DebugRegistry.Snapshot())
        {
            if (!item.Draggable)
            {
                continue;
            }
            var pos = item.EffectiveScreenPos();
            var rect = new Rectangle((int)pos.X - size / 2, (int)pos.Y - size / 2, size, size);
            _spriteBatch.Draw(_white, rect, item == _dragTarget ? active : idle);
        }
    }

    private string? ResolveWzDir(IConfiguration config)
    {
        var fromEnv = config["WZ_DIR"];
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }
        var beside = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(beside, "UI.wz")))
        {
            return beside;
        }
        return fromEnv;
    }

    private void TryOpenWzPackages()
    {
        if (string.IsNullOrWhiteSpace(_wzDir))
        {
            _logger.LogError("No WZ directory configured. Set MAPLECLAUDE_WZ_DIR or place UI.wz beside the exe.");
            return;
        }
        _uiWz = TryOpen(Path.Combine(_wzDir, "UI.wz"));
        _mapWz = TryOpen(Path.Combine(_wzDir, "Map.wz"));
        _soundWz = TryOpen(Path.Combine(_wzDir, "Sound.wz"));
        _charWz = TryOpen(Path.Combine(_wzDir, "Character.wz"));
        _npcWz  = TryOpen(Path.Combine(_wzDir, "Npc.wz"));
        _itemWz = TryOpen(Path.Combine(_wzDir, "Item.wz"));
        _mobWz  = TryOpen(Path.Combine(_wzDir, "Mob.wz"));
        _stringWz = TryOpen(Path.Combine(_wzDir, "String.wz"));
        _skillWz = TryOpen(Path.Combine(_wzDir, "Skill.wz"));
        _questWz = TryOpen(Path.Combine(_wzDir, "Quest.wz"));
        _etcWz = TryOpen(Path.Combine(_wzDir, "Etc.wz"));
        _baseWz = TryOpen(Path.Combine(_wzDir, "Base.wz"));
    }

    private WzPackage? TryOpen(string path)
    {
        try
        {
            var pkg = WzPackage.Open(path);
            _logger.LogInformation("Opened WZ package {Path}", path);
            return pkg;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open WZ package {Path}", path);
            return null;
        }
    }
}
