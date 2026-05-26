using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Render;

/// <summary>
/// A multi-frame WZ animation: a list of <see cref="WzSprite"/> frames with
/// per-frame delays (milliseconds). Advance with <see cref="Update"/> and draw
/// <see cref="Current"/>. A single-frame animation is static (Update is a no-op),
/// so callers can treat static and animated assets uniformly.
/// </summary>
public sealed class AnimatedSprite
{
    private readonly WzSprite[] _frames;
    private readonly int[] _delaysMs;
    private int _index;
    private double _accumMs;

    public AnimatedSprite(WzSprite[] frames, int[] delaysMs)
    {
        ArgumentOutOfRangeException.ThrowIfZero(frames.Length);
        _frames = frames;
        _delaysMs = delaysMs;
        var total = 0;
        foreach (var d in delaysMs) total += d > 0 ? d : 100;
        TotalDurationMs = total;
    }

    public int FrameCount => _frames.Length;
    /// <summary>Sum of all frame delays (one full cycle), in milliseconds.</summary>
    public int TotalDurationMs { get; }
    public WzSprite Current => _frames[_index];
    public Vector2 Origin => Current.Origin;
    public int Width => Current.Width;
    public int Height => Current.Height;

    public void Update(double dtMs)
    {
        if (_frames.Length < 2)
        {
            return;
        }
        _accumMs += dtMs;
        // Advance frames; the guard stops a runaway loop if a delay is 0/negative.
        var guard = 0;
        while (_accumMs >= _delaysMs[_index] && guard++ < 256)
        {
            _accumMs -= _delaysMs[_index];
            _index = (_index + 1) % _frames.Length;
        }
    }

    public void Draw(SpriteBatch sb, Vector2 position, Color? color = null) =>
        _frames[_index].Draw(sb, position, color);

    public void Draw(SpriteBatch sb, Vector2 position, SpriteEffects effects, Color? color = null) =>
        _frames[_index].Draw(sb, position, effects, color);
}
