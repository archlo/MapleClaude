using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Render;

/// <summary>
/// Lazy-loads <see cref="WzCanvas"/> pixel data into MonoGame
/// <see cref="Texture2D"/> instances. Caches by canvas identity so that
/// re-requesting the same WZ node returns the same texture (saving GPU
/// memory). Caller must keep the loader alive for the lifetime of any
/// textures it returns; disposing the loader disposes all cached textures.
/// </summary>
public sealed class WzTextureLoader : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<WzCanvas, WzSprite> _cache = new();
    private bool _disposed;

    public WzTextureLoader(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Loads (or returns the cached) sprite for a WZ canvas. Decodes pixels
    /// and uploads to GPU on first request. Returns <c>null</c> if the canvas
    /// is zero-sized or unsupported format (so a Stage can degrade gracefully).
    /// </summary>
    public WzSprite? Load(WzCanvas? canvas)
    {
        if (canvas is null)
        {
            return null;
        }

        if (_cache.TryGetValue(canvas, out var cached))
        {
            return cached;
        }

        if (canvas.Width <= 0 || canvas.Height <= 0)
        {
            return null;
        }

        ReadOnlySpan<byte> pixels;
        try
        {
            pixels = canvas.DecodeBgra();
        }
        catch (NotSupportedException)
        {
            // Unsupported pixel format (DXT3/DXT5/etc.) — surface as null so the
            // calling Stage can substitute a placeholder.
            return null;
        }

        var texture = new Texture2D(_device, canvas.Width, canvas.Height, mipmap: false, SurfaceFormat.Color);
        texture.SetData(pixels.ToArray());

        // Read origin from canvas property tree if present.
        var origin = Vector2.Zero;
        if (canvas.Property.Get("origin") is WzVector v)
        {
            origin = new Vector2(v.X, v.Y);
        }

        var sprite = new WzSprite(texture, origin);
        _cache[canvas] = sprite;
        return sprite;
    }

    /// <summary>
    /// Builds an <see cref="AnimatedSprite"/> from a WZ node. A bare
    /// <see cref="WzCanvas"/> becomes a single static frame; a
    /// <see cref="WzProperty"/> with numbered canvas children (<c>0,1,2…</c>)
    /// becomes a multi-frame animation, reading each frame's <c>delay</c>
    /// (default 100 ms). Returns <c>null</c> when no usable frame is found.
    /// </summary>
    public AnimatedSprite? LoadAnimation(object? node)
    {
        switch (node)
        {
            case WzCanvas canvas:
            {
                var s = Load(canvas);
                return s is null ? null : new AnimatedSprite([s], [100]);
            }
            case WzProperty prop:
            {
                var frames = new List<WzSprite>();
                var delays = new List<int>();
                for (var i = 0; ; i++)
                {
                    if (prop.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not WzCanvas frame)
                    {
                        break;
                    }
                    var sprite = Load(frame);
                    if (sprite is null)
                    {
                        break;
                    }
                    frames.Add(sprite);
                    var delay = ReadInt(frame.Property.Get("delay"), 100);
                    delays.Add(delay <= 0 ? 100 : delay);
                }
                return frames.Count == 0 ? null : new AnimatedSprite([.. frames], [.. delays]);
            }
            default:
                return null;
        }
    }

    private static int ReadInt(object? v, int fallback) => v switch
    {
        int i => i,
        short s => s,
        long l => (int)l,
        _ => fallback,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var sprite in _cache.Values)
        {
            sprite.Texture.Dispose();
        }
        _cache.Clear();
    }
}
