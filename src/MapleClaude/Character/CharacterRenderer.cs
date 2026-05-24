using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Composes a player avatar from the WZ asset tree using the authentic MapleStory
/// model: each part canvas carries an <c>origin</c>, a <c>map</c> of named anchor
/// points (neck / navel / hand / brow), and a <c>z</c> layer string. Parts are
/// stitched by aligning shared anchors — the body is the root; the arm and clothing
/// attach by <c>navel</c>, the head by <c>neck</c>, the face/hair/cap by <c>brow</c>,
/// the gloves/weapon by <c>hand</c> — and drawn back-to-front per
/// <see cref="AvatarZMap"/> (<c>Base.wz/zmap.img</c>). The head canvas is a UOL
/// (<c>../../front/head</c>), resolved by the reader.
///
/// A named point's world position is <c>pen + map[name]</c>; a sprite draws at
/// <c>pen − origin</c> (see <see cref="WzSprite"/>). So aligning anchor <c>k</c> of a
/// part to a reference gives <c>partPen = refPen + ref.map[k] − part.map[k]</c> (or
/// just <c>refPen + ref.map[k]</c> when the part has no <c>k</c> and its origin is the
/// anchor, e.g. the face).
/// </summary>
public sealed class CharacterRenderer
{
    private readonly ILogger<CharacterRenderer> _logger;
    private readonly WzPackage? _characterWz;
    private readonly WzPackage? _itemWz;
    private readonly WzTextureLoader _loader;
    private readonly AvatarZMap _zmap;
    private readonly Dictionary<string, AvatarPart?> _partCache = new();

    public CharacterRenderer(
        ILogger<CharacterRenderer> logger,
        WzPackage? characterWz,
        WzPackage? itemWz,
        WzPackage? baseWz,
        WzTextureLoader loader)
    {
        _logger = logger;
        _characterWz = characterWz;
        _itemWz = itemWz;
        _loader = loader;
        _zmap = new AvatarZMap(baseWz, logger);
    }

    /// <summary>Whether the look's weapon uses the two-handed (stand2/walk2) stances. The v95
    /// client doesn't derive this from the weapon TYPE — it reads the weapon's WZ <c>info/walk</c>
    /// int (CActionMan::GetCharacterImgEntry / CAvatar::MoveAction2RawAction): <c>walk != 1</c> ⇒
    /// walk2. Bare hand (no weapon) ⇒ walk1.</summary>
    public bool IsTwoHanded(AvatarLook look)
    {
        var w = 0;
        if (look.HairEquip.TryGetValue(BodyPartSlot.Weapon, out var wid) && wid != 0) w = wid;
        else if (look.HairEquip.TryGetValue(BodyPartSlot.CashWeapon, out var cw) && cw != 0) w = cw;
        if (w == 0) return false;
        var info = _characterWz?.GetItem($"Weapon/{w:D8}.img/info") as WzProperty;
        return info?.Get("walk") switch { int i => i != 1, short s => s != 1, _ => false };
    }

    /// <summary>Number of body animation frames for a stance (e.g. walk1 = 4), for looping.</summary>
    public int FrameCount(AvatarLook look, Stance stance)
    {
        if (_characterWz is null) return 1;
        var bodyImg = $"000{2000 + look.Skin:D5}.img";
        var n = 0;
        while (_characterWz.GetItem($"{bodyImg}/{stance.ToWzKey()}/{n}") != null) n++;
        return Math.Max(1, n);
    }

    private sealed record AvatarPart(WzSprite Sprite, IReadOnlyDictionary<string, Vector2> Map, string Z);

    /// <summary>
    /// Draw the avatar with its body pen at <paramref name="position"/> (the body's
    /// origin point). Parts are stitched relative to it via their anchor maps.
    /// </summary>
    public void Draw(
        SpriteBatch sb,
        AvatarLook look,
        CharacterStat? stat,
        Stance stance,
        int frame,
        Vector2 position,
        bool facingLeft)
    {
        _ = stat;
        if (_characterWz is null) return;

        var st = stance.ToWzKey();
        var skin = look.Skin;
        var bodyId = 2000 + skin;
        var bodyImg = $"000{bodyId:D5}.img";   // 0000200<skin>.img
        var headImg = $"000{12000 + skin:D5}.img"; // 0001200<skin>.img

        var body = LoadPart($"{bodyImg}/{st}/{frame}/body");
        var arm = LoadPart($"{bodyImg}/{st}/{frame}/arm");
        var hand = LoadPart($"{bodyImg}/{st}/{frame}/hand");
        var head = LoadPart($"{headImg}/{st}/{frame}/head");
        var face = LoadFace(look.Face);
        var hairBelow = LoadHair(look.Hair, "hairBelowBody");
        var hairShade = LoadHair(look.Hair, "hairShade");
        var hair = LoadHair(look.Hair, "hair");
        var hairOver = LoadHair(look.Hair, "hairOverHead");

        // Equips (read from the visible-equip map).
        AvatarPart? cape = null, coat = null, coatArm = null, pants = null, shoes = null,
            gloves = null, cap = null, weapon = null;
        foreach (var (slot, itemId) in look.HairEquip)
        {
            switch (slot)
            {
                case BodyPartSlot.Cape:
                    cape = LoadEquip("Cape", itemId, st, frame, "cape");
                    break;
                case BodyPartSlot.Clothes:
                    var coatCat = itemId / 10000 == 105 ? "Longcoat" : "Coat";
                    coat = LoadEquip(coatCat, itemId, st, frame, "mail");
                    coatArm = LoadEquip(coatCat, itemId, st, frame, "mailArm");
                    break;
                case BodyPartSlot.Pants:
                    pants = LoadEquip("Pants", itemId, st, frame, "pants");
                    break;
                case BodyPartSlot.Shoes:
                    shoes = LoadEquip("Shoes", itemId, st, frame, "shoes");
                    break;
                case BodyPartSlot.Gloves:
                    gloves = LoadEquip("Glove", itemId, st, frame, "glove");
                    break;
                case BodyPartSlot.Cap:
                    cap = LoadEquip("Cap", itemId, st, frame, "cap");
                    break;
                case BodyPartSlot.Weapon:
                    weapon = LoadWeapon(itemId, st, frame);
                    break;
            }
        }

        // ---- anchor chaining (body is the root pen) ----
        var bodyPen = position;
        var armPen = Align(arm, body, bodyPen, "navel");
        var headPen = Align(head, body, bodyPen, "neck");

        var draws = new List<(WzSprite spr, Vector2 pen, int z)>(20);
        void Emit(AvatarPart? p, Vector2 pen)
        {
            if (p is not null) draws.Add((p.Sprite, pen, _zmap.FrontIndex(p.Z)));
        }

        Emit(body, bodyPen);
        Emit(arm, armPen);
        Emit(hand, Align(hand, body, bodyPen, "navel"));
        Emit(head, headPen);
        Emit(face, Align(face, head, headPen, "brow"));
        Emit(hairBelow, Align(hairBelow, head, headPen, "brow"));
        Emit(hairShade, Align(hairShade, head, headPen, "brow"));
        Emit(hair, Align(hair, head, headPen, "brow"));
        Emit(hairOver, Align(hairOver, head, headPen, "brow"));
        Emit(cape, Align(cape, body, bodyPen, "navel"));
        Emit(coat, Align(coat, body, bodyPen, "navel"));
        Emit(coatArm, Align(coatArm, body, bodyPen, "navel"));
        Emit(pants, Align(pants, body, bodyPen, "navel"));
        Emit(shoes, Align(shoes, body, bodyPen, "navel"));
        Emit(gloves, Align(gloves, arm, armPen, "hand"));
        Emit(cap, Align(cap, head, headPen, "brow"));
        Emit(weapon, Align(weapon, arm, armPen, "hand"));

        // Back-to-front: zmap is front→back, so draw highest index first.
        draws.Sort((a, b) => b.z.CompareTo(a.z));

        // WZ body sprites are authored facing LEFT, so facing right is a horizontal flip
        // plus a mirror of each part's pen about the body anchor. facingLeft = WZ default.
        var flip = facingLeft ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
        foreach (var (spr, pen, _) in draws)
        {
            var p = facingLeft ? pen : new Vector2(2f * position.X - pen.X, pen.Y);
            spr.Draw(sb, p, flip, Color.White);
        }
    }

    // partPen = refPen + ref.map[key] − part.map[key]; if the part lacks the key its
    // origin is the anchor (partPen = refPen + ref.map[key]); if the ref lacks it, no shift.
    private static Vector2 Align(AvatarPart? part, AvatarPart? refPart, Vector2 refPen, string key)
    {
        if (part is null || refPart is null || !refPart.Map.TryGetValue(key, out var refAnchor))
        {
            return refPen;
        }
        var pen = refPen + refAnchor;
        if (part.Map.TryGetValue(key, out var partAnchor))
        {
            pen -= partAnchor;
        }
        return pen;
    }

    private AvatarPart? LoadFace(int faceId) =>
        // The still face is default/face (no frame index); blink/etc. are the animated ones.
        LoadPart($"Face/{faceId:D8}.img/default/face");

    private AvatarPart? LoadHair(int hairId, string layer) =>
        // The still hair layer is default/<layer>; stand1/0/<layer> is a UOL back to it.
        LoadPart($"Hair/{hairId:D8}.img/default/{layer}")
        ?? LoadPart($"Hair/{hairId:D8}.img/stand1/0/{layer}");

    private AvatarPart? LoadEquip(string category, int itemId, string st, int frame, string vslot) =>
        LoadPart($"{category}/{itemId:D8}.img/{st}/{frame}/{vslot}");

    private AvatarPart? LoadWeapon(int itemId, string st, int frame)
    {
        // v95 weapons nest their frames under a weapon-type folder, e.g.
        // Weapon/01322092.img/32/stand1/0/weapon (wt = (id/10000)%100); older
        // weapons keep them at the image root.
        var wt = itemId / 10000 % 100;
        return LoadPart($"Weapon/{itemId:D8}.img/{wt}/{st}/{frame}/weapon")
            ?? LoadPart($"Weapon/{itemId:D8}.img/{st}/{frame}/weapon");
    }

    private AvatarPart? LoadPart(string path)
    {
        if (_partCache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        AvatarPart? part = null;
        try
        {
            var node = _characterWz?.GetItem(path);
            if (node is WzUol uol)
            {
                node = uol.Resolve();
            }
            if (node is WzCanvas canvas)
            {
                var sprite = _loader.Load(canvas);
                if (sprite is not null)
                {
                    part = new AvatarPart(sprite, ReadMap(canvas), ReadZ(canvas));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "avatar part {Path} missing", path);
        }
        _partCache[path] = part;
        return part;
    }

    private static IReadOnlyDictionary<string, Vector2> ReadMap(WzCanvas canvas)
    {
        var map = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        if (canvas.Property.Get("map") is WzProperty mp)
        {
            foreach (var (key, value) in mp.Items)
            {
                if (value is WzVector v)
                {
                    map[key] = new Vector2(v.X, v.Y);
                }
            }
        }
        return map;
    }

    private static string ReadZ(WzCanvas canvas) =>
        canvas.Property.Get("z") as string ?? string.Empty;
}
