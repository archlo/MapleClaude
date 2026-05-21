using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.App;

/// <summary>
/// Owns the currently active <see cref="Stage"/> and routes per-frame
/// <see cref="Update"/>, <see cref="Draw"/>, and input events to it.
/// </summary>
public sealed class StageDirector
{
    private readonly MapleClaudeGame _game;
    private Stage? _current;

    public StageDirector(MapleClaudeGame game)
    {
        _game = game;
    }

    public Stage? Current => _current;

    public void Replace(Stage next)
    {
        _current?.OnExit();
        _current = next;
        _current.OnEnter(_game);
    }

    public void Update(GameTime gameTime)
    {
        _current?.Update(gameTime);
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _current?.Draw(gameTime, spriteBatch);
    }

    public void OnTextInput(char character)
    {
        _current?.OnTextInput(character);
    }

    public void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
        _current?.OnMouseButton(x, y, down, button);
    }

    public void OnKeyPress(Keys key)
    {
        _current?.OnKeyPress(key);
    }
}
