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
