using MapleClaude.App;
using MapleClaude.Debug;
using MapleClaude.Platform;
using MapleClaude.Render;
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
    private WzTextureLoader? _cursorLoader;
    private MapleCursor? _cursor;
    private UI.BuiltInFont? _font;

    /// <summary>Shared bitmap font for text-input fields, debug overlays, etc.
    /// Built lazily in <see cref="LoadContent"/> from a system font.</summary>
    public UI.BuiltInFont? Font => _font;

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

    /// <summary>The cursor overlay. Public so stages can call <see cref="MapleCursor.SetHover"/>.</summary>
    public MapleCursor? Cursor => _cursor;

    public StageDirector StageDirector { get; }

    public Texture2D WhitePixel => _white;
    public WzAudioPlayer AudioPlayer { get; }
    public WzPackage? CharWz => _charWz;
    public WzPackage? NpcWz => _npcWz;

    /// <summary>Shared registry of tunable items exposed to the debug window.</summary>
    public DebugRegistry DebugRegistry { get; }

    public void ResizeWindow(int width, int height)
    {
        _graphics.PreferredBackBufferWidth  = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.ApplyChanges();
    }

    public MapleClaudeGame(
        ILogger<MapleClaudeGame> logger,
        ILoggerFactory loggerFactory,
        IConfiguration config,
        DebugRegistry debugRegistry)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _wzDir = ResolveWzDir(config);
        AudioPlayer = new WzAudioPlayer(loggerFactory.CreateLogger<WzAudioPlayer>());
        DebugRegistry = debugRegistry;

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

        TryOpenWzPackages();

        // Cursor overlay. Owns its own loader so its texture lifetime doesn't
        // tie to a stage's loader.
        _cursorLoader = new WzTextureLoader(GraphicsDevice);
        _cursor = new MapleCursor(_loggerFactory.CreateLogger<MapleCursor>(), _cursorLoader);
        _cursor.Load(_uiWz);

        // Window/taskbar icon — set after the window exists (LoadContent is safe).
        TrySetWindowIcon();

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
                // Don't lock the IME to a specific mode. ImeMode.On is documented
                // as Japanese/Chinese-only and can prevent the Korean IME from
                // toggling Han↔Yeong via Right Alt. NoControl lets the user's
                // system hotkeys work normally (Right Alt for Korean toggle,
                // Win+Space for language switch).
                form.ImeMode = System.Windows.Forms.ImeMode.NoControl;
                EnsureImeContext(Window.Handle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set window icon from Icon.png");
        }
    }

    protected override void UnloadContent()
    {
        AudioPlayer.Dispose();
        _cursorLoader?.Dispose();
        _uiWz?.Dispose();
        _mapWz?.Dispose();
        _soundWz?.Dispose();
        _charWz?.Dispose();
        _npcWz?.Dispose();
        _spriteBatch?.Dispose();
        _white?.Dispose();
        _windowIcon?.Dispose();
        _iconBitmap?.Dispose();
        _font?.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
        {
            _logger.LogInformation("ESC pressed — exiting");
            Exit();
        }

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
        _npcWz = TryOpen(Path.Combine(_wzDir, "Npc.wz"));
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
