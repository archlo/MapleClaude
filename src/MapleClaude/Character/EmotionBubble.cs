using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// One-shot over-head emotion bubbles (the sweat-drop, the heart, the question mark,
/// …). Mirrors the v95 client's <c>CAvatar::SetEmotion → CAnimationDisplayer::Effect_General</c>
/// path: when the avatar pulls a face, a short animation from
/// <c>Effect.wz/EmotionEffect.img/&lt;name&gt;</c> plays at world layer z=3 above the
/// avatar's head, then disappears. The bubble is anchored to a fixed world position
/// at spawn (matching how <c>Effect_General</c> snapshots the spawn coordinates) —
/// it does not re-track the avatar.
///
/// Pattern: same shape as <see cref="DamageNumber"/> — a small pool of entries the
/// owning <c>GameStage</c> ticks each frame and draws after the avatar pass.
/// </summary>
public sealed class EmotionBubble
{
    private readonly WzPackage? _effectWz;
    private readonly WzTextureLoader _loader;

    // EmotionEffect.img doesn't reuse Character.wz's Face subnode names verbatim;
    // a few collapse onto shared graphics. This table is the v95 client's lookup —
    // for the FuncKey emotions (1..7) the bubble subnode is the same as the face
    // subnode, which is what the IDB summary shows for the default F1..F7 bindings.
    // Falling back to <see cref="CharacterRenderer.EmotionName"/> on a miss keeps
    // the bubble silently absent if no asset is authored.

    private sealed class FrameData
    {
        public required WzSprite Sprite;
        public int DelayMs;
    }

    private sealed class Anim
    {
        public FrameData[] Frames = Array.Empty<FrameData>();
        public int TotalDurationMs;
        public bool MissingAsset;            // true when the WZ subnode wasn't found
    }

    private sealed class Entry
    {
        public required Anim Animation;
        public Vector2 WorldPos;             // snapshot at spawn (avatar head, world space)
        public int FrameIndex;
        public float FrameTimerMs;
        public float TotalAgeMs;
    }

    private readonly Dictionary<string, Anim> _animCache = new(StringComparer.Ordinal);
    private readonly List<Entry> _entries = new();

    public EmotionBubble(WzPackage? effectWz, WzTextureLoader loader)
    {
        _effectWz = effectWz;
        _loader = loader;
    }

    /// <summary>Spawn an over-head bubble at <paramref name="headWorldPos"/> for the
    /// given <paramref name="emotion"/> (1..23). Emotion 0 (no expression) is a no-op,
    /// as is an emotion whose WZ subnode is absent.</summary>
    public void Add(int emotion, Vector2 headWorldPos)
    {
        if (_effectWz is null) return;
        if (emotion is <= 0 or > 23) return;
        var anim = GetOrLoad(CharacterRenderer.EmotionName(emotion));
        if (anim.MissingAsset || anim.Frames.Length == 0) return;
        _entries.Add(new Entry { Animation = anim, WorldPos = headWorldPos });
    }

    /// <summary>Drop every active bubble (e.g. on field exit / stage tear-down).</summary>
    public void Clear() => _entries.Clear();

    public void Update(float dt)
    {
        var ms = dt * 1000f;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            e.TotalAgeMs += ms;
            if (e.TotalAgeMs >= e.Animation.TotalDurationMs)
            {
                _entries.RemoveAt(i);
                continue;
            }
            e.FrameTimerMs += ms;
            // Advance frames at their per-frame WZ delays. One-shot: when the last
            // frame's delay elapses the whole entry is removed (handled by the age
            // check above), so no wrap-around here.
            while (e.FrameIndex < e.Animation.Frames.Length - 1)
            {
                var d = e.Animation.Frames[e.FrameIndex].DelayMs;
                if (e.FrameTimerMs < d) break;
                e.FrameTimerMs -= d;
                e.FrameIndex++;
            }
        }
    }

    public void Draw(SpriteBatch sb, Func<Vector2, Vector2> worldToScreen)
    {
        foreach (var e in _entries)
        {
            var frame = e.Animation.Frames[Math.Min(e.FrameIndex, e.Animation.Frames.Length - 1)];
            frame.Sprite.Draw(sb, worldToScreen(e.WorldPos), Color.White);
        }
    }

    // ── Lazy WZ animation loader ───────────────────────────────────────────────

    private Anim GetOrLoad(string emotionName)
    {
        if (_animCache.TryGetValue(emotionName, out var cached)) return cached;

        var anim = new Anim();
        if (_effectWz is null) { anim.MissingAsset = true; _animCache[emotionName] = anim; return anim; }

        var frames = new List<FrameData>();
        var total = 0;
        for (var i = 0; i < 32; i++)
        {
            // Frames live at EmotionEffect.img/<name>/<i>; the canvas itself carries
            // the visual, and a sibling `delay` int gives the per-frame duration.
            // Some emotions are a single frame, some animate (4-6 frames typical).
            var frameNode = _effectWz.GetItem($"EmotionEffect.img/{emotionName}/{i}");
            WzCanvas? canvas = frameNode switch
            {
                WzCanvas c   => c,
                WzProperty p => FindFirstCanvas(p),
                _            => null,
            };
            if (canvas is null) break;

            var sprite = _loader.Load(canvas);
            if (sprite is null) break;

            // The `delay` may live either on the frame's parent property (when the
            // node is a property containing the canvas + delay) or on the canvas
            // node itself. Default to 100 ms when neither is present.
            int delay = 100;
            if (frameNode is WzProperty propNode)
            {
                delay = propNode.Get("delay") switch { int v => v, short s => s, long l => (int)l, _ => 100 };
            }
            else if (canvas.Property.Get("delay") is { } d)
            {
                delay = d switch { int v => v, short s => s, long l => (int)l, _ => 100 };
            }
            if (delay <= 0) delay = 100;

            frames.Add(new FrameData { Sprite = sprite, DelayMs = delay });
            total += delay;
        }

        if (frames.Count == 0)
        {
            // Single-canvas fallback: EmotionEffect.img/{name} carries the canvas
            // directly (no numbered subnodes) for one-shot effects. Default to
            // 2500 ms when no `delay` child is present (matches the v95 client
            // emotion-layer behavior).
            if (_effectWz.GetItem($"EmotionEffect.img/{emotionName}") is WzProperty rootProp &&
                FindFirstCanvas(rootProp) is { } canvas)
            {
                var sprite = _loader.Load(canvas);
                if (sprite is not null)
                {
                    var d = rootProp.Get("delay") switch { int v => v, short s => s, long l => (int)l, _ => 2500 };
                    if (d <= 0) d = 2500;
                    frames.Add(new FrameData { Sprite = sprite, DelayMs = d });
                    total += d;
                }
            }
        }

        if (frames.Count == 0)
        {
            anim.MissingAsset = true;
        }
        else
        {
            anim.Frames = frames.ToArray();
            anim.TotalDurationMs = total;
        }
        _animCache[emotionName] = anim;
        return anim;
    }

    private static WzCanvas? FindFirstCanvas(WzProperty p)
    {
        // Some emotion frames are stored as a property carrying the canvas at "0" or
        // as the first canvas child; tolerate either layout.
        if (p.Get("0") is WzCanvas zero) return zero;
        foreach (var kv in p.Items)
        {
            if (kv.Value is WzCanvas c) return c;
        }
        return null;
    }
}
