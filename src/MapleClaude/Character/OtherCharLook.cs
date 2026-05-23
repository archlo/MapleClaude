using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Renders another player in the same map.
/// Position is server-authoritative — updated from <c>UserMove</c> packets.
/// Appearance comes from the decoded <see cref="AvatarLook"/> in
/// <c>UserEnterField</c>. Reuses <see cref="CharLook"/> for sprite loading;
/// no player physics — position set directly.
/// </summary>
public sealed class OtherCharLook
{
    public int       CharId   { get; }
    public string    Name     { get; }
    public byte      Level    { get; }
    public AvatarLook? Look   { get; }

    public Vector2   Position { get; set; }
    private bool     _facingLeft;

    private CharLook? _sprites;
    private readonly BuiltInFont? _font;

    private const int PlaceholderW = 30;
    private const int PlaceholderH = 60;

    public OtherCharLook(int charId, string name, byte level, AvatarLook? look,
                         Vector2 position, BuiltInFont? font)
    {
        CharId   = charId;
        Name     = name;
        Level    = level;
        Look     = look;
        Position = position;
        _font    = font;
    }

    /// <summary>Build the avatar from Character.wz. With a <paramref name="renderer"/>
    /// and a decoded <see cref="Look"/>, the full avatar (hair/face/equips) is drawn.</summary>
    public void LoadSprites(WzTextureLoader loader, WzPackage? charWz, CharacterRenderer? renderer)
    {
        if (charWz is null) return;
        _sprites = new CharLook(loader, Look?.Skin ?? 0);
        _sprites.Load(charWz);
        if (renderer is not null && Look is not null)
        {
            _sprites.SetAvatar(renderer, Look);
        }
    }

    public void SetPosition(short x, short y) => Position = new Vector2(x, y);

    public void SetFacing(bool facingLeft) => _facingLeft = facingLeft;

    public void Update(float dt)
    {
        // Remote players don't run physics; drive an idle stand animation and
        // keep the facing the last move packet set.
        _sprites?.UpdateFromPhysics(dt, Stance.Stand1, _facingLeft);
    }

    public void Draw(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        if (_sprites != null)
        {
            _sprites.Draw(sb, white, screenPos);
        }
        else
        {
            // Placeholder silhouette
            sb.Draw(white, new Rectangle(
                (int)(screenPos.X - PlaceholderW / 2f),
                (int)(screenPos.Y - PlaceholderH),
                PlaceholderW, PlaceholderH), new Color(60, 60, 100, 200));
            sb.Draw(white, new Rectangle(
                (int)(screenPos.X - 12), (int)(screenPos.Y - PlaceholderH - 18), 24, 18),
                new Color(220, 180, 140, 200));
        }

        // Name + level tag
        if (_font != null)
        {
            var tag  = $"[{Level}] {Name}";
            var sz   = _font.Measure(tag);
            var tx   = screenPos.X - sz.X / 2f;
            var ty   = screenPos.Y - PlaceholderH - 32;
            var bg   = new Rectangle((int)tx - 2, (int)ty - 1, (int)sz.X + 4, _font.LineHeight + 2);
            sb.Draw(white, bg, new Color(0, 0, 0, 160));
            _font.Draw(sb, tag, new Vector2(tx, ty), new Color(255, 230, 100));
        }
    }
}
