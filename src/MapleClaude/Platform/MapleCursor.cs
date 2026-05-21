using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.Platform;

/// <summary>
/// Replaces the OS cursor with the MapleStory cursor sprite loaded from
/// <c>UI.wz/Basic.img/Cursor/&lt;variant&gt;/&lt;frame&gt;</c>. Supports
/// three states: default (variant 0), hover-clickable (variant 1, animated
/// across multiple frames with WZ-supplied <c>delay</c>), click (variant 12).
/// </summary>
public sealed class MapleCursor
{
    public enum CursorState
    {
        Default,
        Hover,
        Click,
    }

    private readonly ILogger _logger;
    private readonly WzTextureLoader _loader;
    private WzSprite? _default;
    private WzSprite? _click;

    /// <summary>Frames of the hover-cursor animation: (sprite, delayMs).</summary>
    private readonly List<(WzSprite Sprite, int DelayMs)> _hoverFrames = new();
    private int _hoverFrameIndex;
    private float _hoverFrameElapsedMs;

    public CursorState State { get; private set; } = CursorState.Default;

    public MapleCursor(ILogger logger, WzTextureLoader loader)
    {
        _logger = logger;
        _loader = loader;
    }

    public void Load(WzPackage? ui)
    {
        if (ui is null)
        {
            return;
        }
        _default = LoadVariantFrame(ui, "0", "0");
        _click = LoadVariantFrame(ui, "12", "0");
        LoadVariantAnimation(ui, "1", _hoverFrames);
        _logger.LogInformation(
            "MapleCursor loaded: default={D} click={C} hoverFrames={H}",
            _default != null, _click != null, _hoverFrames.Count);
    }

    public void SetHover(bool isOverClickable)
    {
        if (State == CursorState.Click)
        {
            return;
        }
        if (isOverClickable)
        {
            if (State != CursorState.Hover)
            {
                State = CursorState.Hover;
                _hoverFrameIndex = 0;
                _hoverFrameElapsedMs = 0;
            }
        }
        else
        {
            State = CursorState.Default;
        }
    }

    public void SetClick(bool isMouseDown)
    {
        if (isMouseDown)
        {
            State = CursorState.Click;
        }
        else
        {
            State = CursorState.Default;
        }
    }

    public void Update(GameTime gameTime)
    {
        if (State != CursorState.Hover || _hoverFrames.Count == 0)
        {
            return;
        }
        _hoverFrameElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
        var safetyIterations = 0;
        while (safetyIterations++ < 8)
        {
            var currentDelay = _hoverFrames[_hoverFrameIndex].DelayMs;
            if (currentDelay <= 0 || _hoverFrameElapsedMs < currentDelay)
            {
                break;
            }
            _hoverFrameElapsedMs -= currentDelay;
            _hoverFrameIndex = (_hoverFrameIndex + 1) % _hoverFrames.Count;
        }
    }

    public void Draw(SpriteBatch sb)
    {
        WzSprite? sprite = State switch
        {
            CursorState.Click => _click ?? _default,
            CursorState.Hover => _hoverFrames.Count > 0
                ? _hoverFrames[_hoverFrameIndex].Sprite
                : _default,
            _ => _default,
        };
        if (sprite is null)
        {
            return;
        }
        var mouse = Mouse.GetState();
        sb.Draw(sprite.Texture, new Vector2(mouse.X, mouse.Y) - sprite.Origin, Color.White);
    }

    private WzSprite? LoadVariantFrame(WzPackage ui, string variant, string frame)
    {
        var canvas = ui.GetItem($"Basic.img/Cursor/{variant}/{frame}") as WzCanvas;
        return _loader.Load(canvas);
    }

    private void LoadVariantAnimation(WzPackage ui, string variant, List<(WzSprite, int)> dst)
    {
        if (ui.GetItem($"Basic.img/Cursor/{variant}") is not WzProperty root)
        {
            return;
        }
        var ordered = root.Items
            .Where(kv => int.TryParse(kv.Key, out _))
            .OrderBy(kv => int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (_, value) in ordered)
        {
            if (value is not WzCanvas canvas)
            {
                continue;
            }
            var sprite = _loader.Load(canvas);
            if (sprite is null)
            {
                continue;
            }
            // delay is a WzIntProperty on the canvas's nested properties.
            var delay = canvas.Property.Get("delay") switch
            {
                int i => i,
                short s => s,
                long l => (int)l,
                _ => 100, // sensible default if the asset lacks delay
            };
            dst.Add((sprite, delay));
        }
    }
}
