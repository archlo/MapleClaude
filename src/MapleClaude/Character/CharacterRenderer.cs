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

    // ── Face blink (shared clock; all drawn avatars blink together) ──────────────
    private bool _blinking;
    private int _blinkFrame;               // current blink frame index
    private float _blinkFrameTimer;        // ms elapsed in the current frame
    private int _blinksRemaining;          // consecutive blinks left in this trigger
    private float _idleTimer;              // ms since the last blink ended
    private float _nextBlinkIn = 3000f;    // ms until the next blink starts
    private int[] _activeBlinkDelays = System.Array.Empty<int>();   // delays of the face the clock animates
    private readonly Dictionary<int, int[]> _blinkDelaysByFace = new();
    private static readonly Random _blinkRng = new();

    /// <summary>Advance the face-blink clock. Call once per frame from the stage. Mirrors the v95
    /// CAvatar blink: wait 2-5s, then blink 1-3 times back-to-back, each playing the WZ blink frames
    /// at their per-frame delays.</summary>
    public void Update(float dt)
    {
        var ms = dt * 1000f;
        if (!_blinking)
        {
            _idleTimer += ms;
            if (_idleTimer >= _nextBlinkIn && _activeBlinkDelays.Length > 0)
            {
                _blinking = true;
                _blinkFrame = 0;
                _blinkFrameTimer = 0f;
                _blinksRemaining = _blinkRng.Next(1, 4);   // 1-3 consecutive blinks
            }
            return;
        }
        _blinkFrameTimer += ms;
        var delay = _blinkFrame < _activeBlinkDelays.Length ? _activeBlinkDelays[_blinkFrame] : 60;
        if (_blinkFrameTimer < delay) return;
        _blinkFrameTimer -= delay;
        if (++_blinkFrame < _activeBlinkDelays.Length) return;   // next frame of this blink
        _blinkFrame = 0;
        if (--_blinksRemaining > 0) return;                      // next consecutive blink
        _blinking = false;
        _idleTimer = 0f;
        _nextBlinkIn = 2000 + _blinkRng.Next(3000);              // IDB: 2000 + rand()%3000
    }

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
        // Ladder/rope are back-facing stances: the body/head/cap already use their back frames; hide the
        // (front-only) face and pull the hair's back frames so we don't draw a face on the back of the head.
        var isClimbing = stance is Stance.Ladder or Stance.Rope;
        var skin = look.Skin;
        var bodyId = 2000 + skin;
        var bodyImg = $"000{bodyId:D5}.img";   // 0000200<skin>.img
        var headImg = $"000{12000 + skin:D5}.img"; // 0001200<skin>.img

        var body = LoadPart($"{bodyImg}/{st}/{frame}/body");
        var arm = LoadPart($"{bodyImg}/{st}/{frame}/arm");
        var hand = LoadPart($"{bodyImg}/{st}/{frame}/hand");
        var head = LoadPart($"{headImg}/{st}/{frame}/head");
        var face = LoadFace(look.Face);
        // Hair: front layers normally; on a ladder/rope (back-facing) use the dedicated back-of-head
        // frames backHairBelowCap (behind the head) + backHair (over it), keyed by the climb stance+frame.
        AvatarPart? hairBelow, hairShade, hair, hairOver;
        if (isClimbing)
        {
            AvatarPart? BackHair(string part) =>
                LoadPart($"Hair/{look.Hair:D8}.img/{st}/{frame}/{part}")
                ?? LoadPart($"Hair/{look.Hair:D8}.img/{st}/0/{part}")
                ?? LoadPart($"Hair/{look.Hair:D8}.img/backDefault/{part}");
            hairBelow = BackHair("backHairBelowCap");
            hair      = BackHair("backHair");
            hairShade = null;
            hairOver  = null;
        }
        else
        {
            hairBelow = LoadHair(look.Hair, "hairBelowBody");
            hairShade = LoadHair(look.Hair, "hairShade");
            hair      = LoadHair(look.Hair, "hair");
            hairOver  = LoadHair(look.Hair, "hairOverHead");
        }

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
        var handPen = Align(hand, body, bodyPen, "navel");
        Emit(hand, handPen);
        Emit(head, headPen);
        if (!isClimbing) Emit(face, Align(face, head, headPen, "brow"));   // no front face on a back-facing climb
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
        // A weapon frame's parent anchor key varies by stance (the first vector in its map):
        // stand/walk use "hand", prone/jump use "handMove", some use "navel". Align to the matching
        // reference part so the weapon sits in the hand in every pose.
        var wKey = FirstMapKey(weapon);
        var (wRef, wPen) = wKey switch
        {
            "handMove" => (hand, handPen),
            "navel"    => (body, bodyPen),
            _          => (arm, armPen),
        };
        Emit(weapon, Align(weapon, wRef, wPen, wKey ?? "hand"));

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

    // The first vector key in a part's anchor map. For a weapon this is its parent anchor
    // (hand / handMove / navel), which differs per stance.
    private static string? FirstMapKey(AvatarPart? part)
    {
        if (part is null) return null;
        foreach (var k in part.Map.Keys) return k;
        return null;
    }

    private AvatarPart? LoadFace(int faceId)
    {
        _activeBlinkDelays = EnsureBlinkDelays(faceId);
        if (_blinking && _activeBlinkDelays.Length > 0)
        {
            var bf = Math.Min(_blinkFrame, _activeBlinkDelays.Length - 1);
            var blink = LoadPart($"Face/{faceId:D8}.img/blink/{bf}/face");
            if (blink is not null) return blink;
        }
        // Open-eyed default the rest of the time.
        return LoadPart($"Face/{faceId:D8}.img/default/face");
    }

    // Per-face blink frame delays (ms), read once from the WZ; the blink plays through these.
    private int[] EnsureBlinkDelays(int faceId)
    {
        if (_blinkDelaysByFace.TryGetValue(faceId, out var cached)) return cached;
        var delays = new List<int>();
        for (var i = 0; i < 16; i++)
        {
            if (_characterWz?.GetItem($"Face/{faceId:D8}.img/blink/{i}") is not WzProperty frame) break;
            var d = frame.Get("delay") switch { int v => v, short s => s, long l => (int)l, _ => 60 };
            delays.Add(d <= 0 ? 60 : d);
        }
        var arr = delays.ToArray();
        _blinkDelaysByFace[faceId] = arr;
        return arr;
    }

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
