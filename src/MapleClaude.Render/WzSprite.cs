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

    /// <summary>
    /// Draws with horizontal or vertical flip. The flip is applied around the sprite's
    /// natural anchor so layered character parts stay aligned when facing left.
    /// </summary>
    public void Draw(SpriteBatch batch, Vector2 position, SpriteEffects effects, Color? color = null)
    {
        if (effects == SpriteEffects.None)
        {
            Draw(batch, position, color);
            return;
        }
        // MonoGame flips the texture content around its rectangle, NOT around the origin,
        // so a non-centred origin would shift the sprite on flip. Mirror the origin on the
        // flipped axis (Width-X / Height-Y) so the sprite pivots on its logical anchor —
        // layered character parts then stay aligned when the avatar faces the other way.
        var pivot = new Vector2(
            (effects & SpriteEffects.FlipHorizontally) != 0 ? Width - Origin.X : Origin.X,
            (effects & SpriteEffects.FlipVertically) != 0 ? Height - Origin.Y : Origin.Y);
        batch.Draw(Texture,
            position,
            sourceRectangle: null,
            color ?? Color.White,
            rotation: 0f,
            origin: pivot,
            scale: Vector2.One,
            effects,
            layerDepth: 0f);
    }
}
