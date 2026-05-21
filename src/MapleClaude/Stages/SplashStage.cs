using MapleClaude.App;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Stages;

/// <summary>
/// Splash sequence shown before <see cref="LoginStage"/>. Plays
/// <c>UI.wz/Logo.img/Wizet</c> frames at ~24 fps, then
/// <c>UI.wz/Logo.img/Nexon</c> frames. Any mouse click or key press
/// skips to the next stage immediately.
/// </summary>
public sealed class SplashStage : Stage
{
    private const float FrameDurationSeconds = 1f / 24f;

    private readonly ILogger<SplashStage> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WzPackage? _ui;
    private readonly WzPackage? _map;
    private readonly WzPackage? _sound;

    private WzTextureLoader? _loader;
    private List<WzSprite> _wizetFrames = new();
    private List<WzSprite> _nexonFrames = new();
    private int _currentSequenceIndex; // 0 = Wizet, 1 = Nexon
    private int _frameIndex;
    private float _frameTimer;

    public SplashStage(
        ILogger<SplashStage> logger,
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
        _wizetFrames = LoadFrames("Logo.img/Wizet");
        _nexonFrames = LoadFrames("Logo.img/Nexon");
        _logger.LogInformation(
            "SplashStage loaded Wizet={WizetCount} frames, Nexon={NexonCount} frames",
            _wizetFrames.Count, _nexonFrames.Count);
        if (_wizetFrames.Count == 0 && _nexonFrames.Count == 0)
        {
            // Nothing to splash — go straight to login.
            AdvanceToLogin();
            return;
        }
        // Start Wizet logo BGM (Sound.wz/BgmUI.img/WzLogo).
        PlaySplashBgm("WzLogo");
    }

    public override void OnExit()
    {
        _loader?.Dispose();
        _loader = null;
        base.OnExit();
    }

    public override void Update(GameTime gameTime)
    {
        var frames = CurrentFrames();
        if (frames.Count == 0)
        {
            AdvanceSequence();
            return;
        }

        _frameTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        while (_frameTimer >= FrameDurationSeconds)
        {
            _frameTimer -= FrameDurationSeconds;
            _frameIndex++;
            if (_frameIndex >= frames.Count)
            {
                AdvanceSequence();
                return;
            }
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var pp = GraphicsDevice.PresentationParameters;
        spriteBatch.Draw(Game.WhitePixel, pp.Bounds, Color.White);

        var frames = CurrentFrames();
        if (frames.Count == 0)
        {
            return;
        }
        var frame = frames[Math.Min(_frameIndex, frames.Count - 1)];

        // Every splash canvas carries its own `origin` property. WzSprite.Draw
        // anchors the frame so that the canvas pixel at (origin.X, origin.Y)
        // lands at the requested screen position — the splash animation expects
        // this so successive frames stay aligned around a stable anchor.
        //
        // For Wizet the WZ origin puts the visible logo to the bottom-right of
        // screen centre (because the canvas pads white space top-left of the
        // logo). Nudge the anchor up-and-left so the logo sits closer to true
        // visual centre, leaving the canvas's bottom-right padding as white
        // screen space, per user feedback.
        var anchor = _currentSequenceIndex == 0
            ? new Vector2(pp.BackBufferWidth * 0.35f, pp.BackBufferHeight * 0.35f)
            : new Vector2(pp.BackBufferWidth * 0.5f, pp.BackBufferHeight * 0.5f);
        frame.Draw(spriteBatch, anchor);
    }

    public override void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        // Skip only by clicking INSIDE the game window while it has focus.
        // Don't react to keyboard input (the user may be typing in another app).
        if (!down)
        {
            return;
        }
        if (!Game.IsActive)
        {
            return;
        }
        var pp = GraphicsDevice.PresentationParameters;
        if (x < 0 || x >= pp.BackBufferWidth || y < 0 || y >= pp.BackBufferHeight)
        {
            return;
        }
        AdvanceSequence();
    }

    private List<WzSprite> CurrentFrames() => _currentSequenceIndex switch
    {
        0 => _wizetFrames,
        1 => _nexonFrames,
        _ => new List<WzSprite>(),
    };

    private void AdvanceSequence()
    {
        _currentSequenceIndex++;
        _frameIndex = 0;
        _frameTimer = 0;
        if (_currentSequenceIndex == 1)
        {
            // Switch to Nexon BGM (Sound.wz/BgmUI.img/NxLogoMS).
            PlaySplashBgm("NxLogoMS");
        }
        else if (_currentSequenceIndex > 1)
        {
            AdvanceToLogin();
        }
    }

    private void PlaySplashBgm(string soundName)
    {
        if (_sound is null)
        {
            return;
        }
        if (_sound.GetItem($"BgmUI.img/{soundName}") is WzSound sound)
        {
            Game.AudioPlayer.PlayLoop(sound);
            _logger.LogInformation("Splash BGM: {Name}", soundName);
        }
        else
        {
            _logger.LogWarning("Splash BGM not found at Sound.wz/BgmUI.img/{Name}", soundName);
        }
    }

    private void AdvanceToLogin()
    {
        _logger.LogInformation("Splash complete — pushing LoginStage");
        Director.Replace(new LoginStage(
            _loggerFactory.CreateLogger<LoginStage>(),
            _loggerFactory,
            _ui, _map, _sound));
    }

    private List<WzSprite> LoadFrames(string propertyPath)
    {
        var result = new List<WzSprite>();
        if (_ui is null)
        {
            return result;
        }
        if (_ui.GetItem(propertyPath) is not WzProperty prop)
        {
            return result;
        }
        // Frames are keyed "0", "1", "2", ... — iterate in numeric order.
        var ordered = prop.Items
            .Where(kv => int.TryParse(kv.Key, out _))
            .OrderBy(kv => int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (_, value) in ordered)
        {
            if (value is WzCanvas canvas && _loader!.Load(canvas) is { } sprite)
            {
                result.Add(sprite);
            }
        }
        return result;
    }
}
