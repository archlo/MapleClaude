using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Composes a player avatar from the WZ asset tree and draws it via
/// <see cref="SpriteBatch"/>. The render order matches the v83 reference
/// client (body bottom → cape → pants → top → mail → hair → face → hand
/// → cap → weapon) with v95-specific path corrections.
///
/// For Phase 2 we only support the <c>stand1</c> stance + frame 0 (and the
/// walk preview for CharSelect). Phase 3 adds walk1/jump cycles.
/// </summary>
public sealed class CharacterRenderer
{
    private readonly ILogger<CharacterRenderer> _logger;
    private readonly WzPackage? _characterWz;
    private readonly WzPackage? _itemWz;
    private readonly WzTextureLoader _loader;
    private readonly Dictionary<string, WzSprite?> _spriteCache = new();

    public CharacterRenderer(
        ILogger<CharacterRenderer> logger,
        WzPackage? characterWz,
        WzPackage? itemWz,
        WzTextureLoader loader)
    {
        _logger = logger;
        _characterWz = characterWz;
        _itemWz = itemWz;
        _loader = loader;
    }

    /// <summary>Render an avatar at <paramref name="footPos"/> (the bottom-centre of the body).</summary>
    public void Draw(
        SpriteBatch sb,
        AvatarLook look,
        CharacterStat? stat,
        Stance stance,
        int frame,
        Vector2 footPos,
        bool facingLeft)
    {
        if (_characterWz is null)
        {
            return;
        }
        var stanceKey = stance.ToWzKey();
        var skin = look.Skin;
        // Body / arm / hand: Character/0000200<skin>.img/<stance>/<frame>/<part>
        var bodyId = 2000 + skin;
        var body = ResolveCharacter($"0000{bodyId}.img/{stanceKey}/{frame}/body");
        var arm  = ResolveCharacter($"0000{bodyId}.img/{stanceKey}/{frame}/arm");
        var hand = ResolveCharacter($"0000{bodyId}.img/{stanceKey}/{frame}/hand");
        // Head: Character/00012<skin:D3>.img/<stance>/<frame>/head
        var head = ResolveCharacter($"00012{skin:D3}.img/{stanceKey}/{frame}/head");
        // Face: Character/Face/<face>.img/<expression=default>/0/face
        var face = ResolveFace(look.Face);
        // Hair sprites: split between hair, hairBelowBody, hairOverHead.
        var hairBelow = ResolveHair(look.Hair, "hairBelowBody");
        var hair = ResolveHair(look.Hair, "hair");
        var hairOver = ResolveHair(look.Hair, "hairOverHead");
        // Equip layer order (simplified for Phase 2): cape, pants, shoes, top/coat, gloves, cap, weapon.
        WzSprite? cape = null;
        WzSprite? pants = null;
        WzSprite? shoes = null;
        WzSprite? top = null;
        WzSprite? gloves = null;
        WzSprite? cap = null;
        WzSprite? weapon = null;
        foreach (var kv in look.HairEquip)
        {
            switch (kv.Key)
            {
                case BodyPartSlot.Cape:
                    cape = ResolveEquip(kv.Value, "Cape", stanceKey, frame, "cape");
                    break;
                case BodyPartSlot.Pants:
                    pants = ResolveEquip(kv.Value, "Pants", stanceKey, frame, "pants");
                    break;
                case BodyPartSlot.Shoes:
                    shoes = ResolveEquip(kv.Value, "Shoes", stanceKey, frame, "shoes");
                    break;
                case BodyPartSlot.Clothes:
                    top = ResolveEquip(kv.Value, "Coat", stanceKey, frame, "mail") ??
                          ResolveEquip(kv.Value, "Coat", stanceKey, frame, "coat") ??
                          ResolveEquip(kv.Value, "Longcoat", stanceKey, frame, "mail");
                    break;
                case BodyPartSlot.Gloves:
                    gloves = ResolveEquip(kv.Value, "Glove", stanceKey, frame, "glove");
                    break;
                case BodyPartSlot.Cap:
                    cap = ResolveEquip(kv.Value, "Cap", stanceKey, frame, "cap");
                    break;
                case BodyPartSlot.Weapon:
                    weapon = ResolveEquip(kv.Value, "Weapon", stanceKey, frame, "weapon");
                    break;
            }
        }

        _ = stat;
        var color = Color.White;
        var flip = facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        // Render roughly back-to-front. Each part's WZ origin anchors it relative
        // to the foot position. (Pixel-precise navel/neck/brow anchor-map alignment
        // is a follow-up; this origin-based order matches the character-create
        // preview.) Order: hair-below → cape → body → lower equips → arm/sleeve →
        // gloves → head → face → hair → cap → hand → weapon.
        hairBelow?.Draw(sb, footPos, flip, color);
        cape?.Draw(sb, footPos, flip, color);
        body?.Draw(sb, footPos, flip, color);
        pants?.Draw(sb, footPos, flip, color);
        shoes?.Draw(sb, footPos, flip, color);
        top?.Draw(sb, footPos, flip, color);
        arm?.Draw(sb, footPos, flip, color);
        gloves?.Draw(sb, footPos, flip, color);
        head?.Draw(sb, footPos, flip, color);
        face?.Draw(sb, footPos, flip, color);
        hair?.Draw(sb, footPos, flip, color);
        cap?.Draw(sb, footPos, flip, color);
        hairOver?.Draw(sb, footPos, flip, color);
        hand?.Draw(sb, footPos, flip, color);
        weapon?.Draw(sb, footPos, flip, color);
    }

    private WzSprite? ResolveCharacter(string path)
    {
        if (_spriteCache.TryGetValue("char/" + path, out var cached))
        {
            return cached;
        }
        WzSprite? sprite = null;
        try
        {
            if (_characterWz?.GetItem(path) is WzCanvas c)
            {
                sprite = _loader.Load(c);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "char asset {Path} missing", path);
        }
        _spriteCache["char/" + path] = sprite;
        return sprite;
    }

    private WzSprite? ResolveFace(int faceId)
    {
        var padded = faceId.ToString("D8");
        return ResolveCharacter($"Face/{padded}.img/default/0/face");
    }

    private WzSprite? ResolveHair(int hairId, string subkey)
    {
        var padded = hairId.ToString("D8");
        return ResolveCharacter($"Hair/{padded}.img/default/0/{subkey}")
            ?? ResolveCharacter($"Hair/{padded}.img/stand1/0/{subkey}");
    }

    private WzSprite? ResolveEquip(int itemId, string category, string stanceKey, int frame, string vslot)
    {
        var padded = itemId.ToString("D8");
        return ResolveCharacter($"{category}/{padded}.img/{stanceKey}/{frame}/{vslot}");
    }
}
