using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Render;

/// <summary>
/// A renderable WZ asset: a GPU texture plus the sprite's origin offset.
/// Origin is subtracted from the draw position so layered sprites align
/// the way the original game data expects.
/// </summary>
public sealed class WzSprite
{
    public Texture2D Texture { get; }
    public Vector2 Origin { get; }
    public int Width => Texture.Width;
    public int Height => Texture.Height;

    internal WzSprite(Texture2D texture, Vector2 origin)
    {
        Texture = texture;
        Origin = origin;
    }

    /// <summary>Draws the sprite with its origin subtracted from <paramref name="position"/>.</summary>
    public void Draw(SpriteBatch batch, Vector2 position, Color? color = null)
    {
        batch.Draw(Texture, position - Origin, color ?? Color.White);
    }
}
