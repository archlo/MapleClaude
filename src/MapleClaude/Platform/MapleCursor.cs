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
/// these states: default (variant 0), hover-clickable (variant 1, animated
/// across multiple frames with WZ-supplied <c>delay</c>), ready-to-grab
/// (variant 5, the open-hand cursor shown over inventory item icons BEFORE
/// the click that picks them up), and click / closed-hand grab (variant 12).
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

    /// <summary>Frames of the vertical-resize cursor animation (variant 7), shown while
    /// the cursor is hovering over a resizable edge (e.g. the chat log's top grip)
    /// before a drag has begun.</summary>
    private readonly List<(WzSprite Sprite, int DelayMs)> _resizeFrames = new();
    private int _resizeFrameIndex;
    private float _resizeFrameElapsedMs;

    /// <summary>Static vertical-scroll cursor (variant 9), shown while a drag is
    /// actively in progress on a vertical grip. Authentic v95 behavior: hovering
    /// shows the animated indicator; the moment the user clicks-and-drags it
    /// freezes onto the static glyph.</summary>
    private WzSprite? _resizeStatic;

    /// <summary>Frames of the ready-to-grab cursor (variant 5, open hand). Shown while
    /// <see cref="GrabReady"/> is set but nothing is on the cursor yet — the user is hovering
    /// over a grabbable inventory item. Some WZ builds expose this as a single frame, others
    /// as a short animation; we handle both via the frame list.</summary>
    private readonly List<(WzSprite Sprite, int DelayMs)> _grabReadyFrames = new();
    private int _grabReadyFrameIndex;
    private float _grabReadyFrameElapsedMs;

    public CursorState State { get; private set; } = CursorState.Default;

    /// <summary>When true the cursor shows the closed-hand "grab" sprite (variant 12) regardless of
    /// hover/click state — set while an inventory item is picked up onto the cursor (ghost-drag).</summary>
    public bool ItemGrabbed { get; set; }

    /// <summary>When true (and <see cref="ItemGrabbed"/> is false) the cursor shows the open-hand
    /// "ready to grab" sprite (variant 5) — set while the mouse is over a grabbable inventory item
    /// icon but the user hasn't clicked yet. Overrides regular Hover so generic UI hover anim
    /// doesn't replace the grab indicator inside the inventory.</summary>
    public bool GrabReady { get; set; }

    /// <summary>When true the cursor shows the vertical-resize sprite (variant 7) — set by GameStage
    /// while the mouse is over a resizable edge (e.g. the chat log's drag grip).</summary>
    public bool Resize { get; set; }

    /// <summary>When true the cursor shows the static vertical-scroll sprite (variant 9) — set
    /// by GameStage while a vertical-grip drag is actively in progress. Takes priority over
    /// the animated <see cref="Resize"/> hover cursor.</summary>
    public bool ResizeDragging { get; set; }

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
        _resizeStatic = LoadVariantFrame(ui, "9", "0");  // static vertical-scroll (active drag)
        LoadVariantAnimation(ui, "1", _hoverFrames);
        LoadVariantAnimation(ui, "7", _resizeFrames);   // vertical-resize cursor (animated, hover)
        LoadVariantAnimation(ui, "5", _grabReadyFrames); // open-hand "ready to grab"
        _logger.LogInformation(
            "MapleCursor loaded: default={D} click={C} hoverFrames={H} resizeFrames={R} resizeStatic={RS} grabReadyFrames={G}",
            _default != null, _click != null, _hoverFrames.Count, _resizeFrames.Count, _resizeStatic != null, _grabReadyFrames.Count);
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
        var dtMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Advance the resize-cursor animation independently of State (it's an override flag).
        if (Resize && _resizeFrames.Count > 0)
        {
            _resizeFrameElapsedMs += dtMs;
            var safety = 0;
            while (safety++ < 8)
            {
                var d = _resizeFrames[_resizeFrameIndex].DelayMs;
                if (d <= 0 || _resizeFrameElapsedMs < d) break;
                _resizeFrameElapsedMs -= d;
                _resizeFrameIndex = (_resizeFrameIndex + 1) % _resizeFrames.Count;
            }
        }

        // Advance the grab-ready animation while it's the active sprite (override flag).
        if (GrabReady && !ItemGrabbed && _grabReadyFrames.Count > 0)
        {
            _grabReadyFrameElapsedMs += dtMs;
            var safety = 0;
            while (safety++ < 8)
            {
                var d = _grabReadyFrames[_grabReadyFrameIndex].DelayMs;
                if (d <= 0 || _grabReadyFrameElapsedMs < d) break;
                _grabReadyFrameElapsedMs -= d;
                _grabReadyFrameIndex = (_grabReadyFrameIndex + 1) % _grabReadyFrames.Count;
            }
        }

        if (State != CursorState.Hover || _hoverFrames.Count == 0)
        {
            return;
        }
        _hoverFrameElapsedMs += dtMs;
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
        // Priority order: held-item closed-hand → open-hand grab-ready → active-drag static
        // vertical-scroll → resize hover (animated) → hover → default. The static variant
        // 9 wins over the animated variant 7 the moment a drag begins, matching v95.
        WzSprite? sprite;
        if (ItemGrabbed)
        {
            sprite = _click ?? _default;
        }
        else if (GrabReady && _grabReadyFrames.Count > 0)
        {
            sprite = _grabReadyFrames[_grabReadyFrameIndex].Sprite;
        }
        else if (ResizeDragging && _resizeStatic is not null)
        {
            sprite = _resizeStatic;
        }
        else if (Resize && _resizeFrames.Count > 0)
        {
            sprite = _resizeFrames[_resizeFrameIndex].Sprite;
        }
        else
        {
            sprite = State switch
            {
                CursorState.Click => _click ?? _default,
                CursorState.Hover => _hoverFrames.Count > 0
                    ? _hoverFrames[_hoverFrameIndex].Sprite
                    : _default,
                _ => _default,
            };
        }
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
