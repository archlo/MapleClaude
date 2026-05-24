using MapleClaude.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.App;

/// <summary>
/// Base class for a single UI/game screen. A stage owns its own state, runs
/// per-frame <see cref="Update"/> and <see cref="Draw"/>, and can request a
/// transition via the <see cref="StageDirector"/>.
/// </summary>
public abstract class Stage
{
    protected MapleClaudeGame Game { get; private set; } = null!;
    protected StageDirector Director => Game.StageDirector;
    protected GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

    public virtual void OnEnter(MapleClaudeGame game)
    {
        Game = game;
    }

    public virtual void OnExit()
    {
    }

    public virtual void Update(GameTime gameTime)
    {
    }

    public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);

    private MuteButton? _frameMuteButton;

    /// <summary>
    /// Draws — and services input for — the BGM mute toggle at the login
    /// <c>Common/frame</c>'s top-right corner. Call once per frame from a framed login
    /// stage's <see cref="Draw"/>, right after the frame sprite is drawn. The toggle is
    /// backed by the persistent audio player, so its state carries across stages.
    /// </summary>
    protected void DrawFrameMuteButton(SpriteBatch spriteBatch)
    {
        _frameMuteButton ??= new MuteButton(
            () => Game.AudioPlayer.Muted,
            () => Game.AudioPlayer.ToggleMute(),
            Game.Font);
        var pp = GraphicsDevice.PresentationParameters;
        _frameMuteButton.ServiceAndDraw(spriteBatch, Game.WhitePixel,
            pp.BackBufferWidth, pp.BackBufferHeight);
    }

    /// <summary>Called when the user types a character (Unicode TextInput event).</summary>
    public virtual void OnTextInput(char character)
    {
    }

    /// <summary>Called when a mouse button transitions down or up at the given pixel.</summary>
    public virtual void OnMouseButton(int x, int y, bool down, MouseButton button)
    {
    }

    /// <summary>Called when a non-character key is pressed (Enter, Backspace, Tab, etc.).</summary>
    public virtual void OnKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
    }
}

/// <summary>Which mouse button generated the event.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
}
