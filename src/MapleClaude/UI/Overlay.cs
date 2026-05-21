using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

public abstract class Overlay
{
    public bool IsVisible { get; set; }

    public virtual void Update(GameTime gameTime) { }
    public virtual void Draw(SpriteBatch spriteBatch, Texture2D white) { }
    public virtual bool HandleMouseButton(int x, int y, bool down) => false;
    public virtual bool OnKeyPress(Keys key) => false;
    public virtual void OnTextInput(char character) { }
}
