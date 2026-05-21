using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Active-buff icon strip shown at the top-right. Icons are placeholders until
/// buff packets are wired. WZ: <c>UIWindow2.img/BuffList/</c>
/// </summary>
public sealed class BuffList : GamePanel
{
    private readonly WzSprite? _buffSlot;
    private readonly BuiltInFont? _font;

    private readonly List<(string name, int secondsLeft)> _buffs = new();

    public BuffList(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;
        Position = new Vector2(690, 5);

        var bl = ui?.GetItem("UIWindow2.img/BuffList") as WzProperty;
        _buffSlot = bl?.Get("slot") is WzCanvas sc ? loader.Load(sc) : null;

        // Placeholder buffs visible on start
        _buffs.Add(("Magic Guard",    1800));
        _buffs.Add(("Speed Infusion",  300));
        _buffs.Add(("Warrior Elixir",  900));
    }

    public void AddBuff(string name, int seconds) => _buffs.Add((name, seconds));
    public void ClearBuffs() => _buffs.Clear();

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        for (var i = _buffs.Count - 1; i >= 0; i--)
        {
            var (n, s) = _buffs[i];
            _buffs[i] = (n, (int)(s - dt));
            if (_buffs[i].secondsLeft <= 0) _buffs.RemoveAt(i);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        for (var i = 0; i < Math.Min(_buffs.Count, 10); i++)
        {
            var pos = Position + new Vector2(-(i * 22), 0);
            if (_buffSlot != null) _buffSlot.Draw(sb, pos);
            else sb.Draw(white, new Rectangle((int)pos.X, (int)pos.Y, 20, 20), new Color(60, 60, 100));
        }
    }
}
