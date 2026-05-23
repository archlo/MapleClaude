using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// An animated Maple avatar. Owns the per-stance frame timing (loaded from the
/// body image) and, when given an <see cref="AvatarLook"/> + a
/// <see cref="CharacterRenderer"/>, draws the full composed avatar
/// (body + arm + hand + head + face + hair + equips). Without a look it falls
/// back to a body-only render or a placeholder silhouette.
///
/// Animation frames come from <c>Character.wz/00002&lt;skin:D3&gt;.img/&lt;stance&gt;</c>.
/// </summary>
public sealed class CharLook
{
    // Stances the player avatar actually uses; loaded for frame-timing.
    private static readonly Stance[] LoadedStances =
    [
        Stance.Stand1, Stance.Walk1, Stance.Walk2, Stance.Jump, Stance.Alert,
        Stance.Prone, Stance.Sit, Stance.Swing,
    ];

    private readonly Dictionary<Stance, List<LayerFrame>> _frames = new();

    private Stance _stance = Stance.Stand1;
    private int _frame;
    private float _frameTimer;
    private bool _facingLeft;
    public bool FacingLeft => _facingLeft;

    // Physics (used only for the pre-SetField demo movement on the title path).
    public Vector2 Position { get; set; }
    private Vector2 _velocity;
    private bool _onGround = true;
    private const float Gravity = 980f;
    private const float JumpSpeed = -480f;
    private const float WalkSpeed = 120f;

    private const int PlaceholderW = 30;
    private const int PlaceholderH = 60;

    private readonly WzTextureLoader _loader;
    private bool _loaded;

    // Full-avatar composition (optional — set via SetAvatar).
    private CharacterRenderer? _renderer;
    private AvatarLook? _look;

    public int SkinId { get; }

    public CharLook(WzTextureLoader loader, int skinId = 0)
    {
        _loader = loader;
        SkinId = skinId;
    }

    /// <summary>Attach the full-avatar renderer + look so Draw composes
    /// hair/face/equips rather than just the body.</summary>
    public void SetAvatar(CharacterRenderer renderer, AvatarLook look)
    {
        _renderer = renderer;
        _look = look;
    }

    /// <summary>
    /// Load body frames for animation timing. Safe with a null package — Draw
    /// then falls back to a placeholder (or the renderer, if one was attached).
    /// </summary>
    public void Load(WzPackage? charWz)
    {
        if (charWz is null) return;

        var bodyId = $"00002{SkinId:D3}.img";
        var headId = $"00012{SkinId:D3}.img";

        foreach (var stance in LoadedStances)
        {
            var stName = stance.ToWzKey();
            var frameList = new List<LayerFrame>();
            var frameIdx = 0;
            while (true)
            {
                var frameNode = charWz.GetItem($"{bodyId}/{stName}/{frameIdx}") as WzProperty;
                var headFrame = charWz.GetItem($"{headId}/{stName}/{frameIdx}") as WzProperty;
                if (frameNode is null && headFrame is null) break;

                frameList.Add(new LayerFrame
                {
                    Delay = ReadDelay(frameNode),
                    Body = LoadPart(frameNode, "body"),
                    Arm = LoadPart(frameNode, "arm") ?? LoadPart(frameNode, "armBelowHead"),
                    Hand = LoadPart(frameNode, "hand"),
                    Head = LoadPart(headFrame, "head"),
                });
                frameIdx++;
            }
            if (frameList.Count > 0) _frames[stance] = frameList;
        }

        _loaded = _frames.Count > 0;
    }

    /// <summary>Animation-only update when an external controller owns movement.</summary>
    public void UpdateFromPhysics(float dt, Stance stance, bool facingLeft)
    {
        _facingLeft = facingLeft;
        if (stance != _stance) { _stance = stance; _frame = 0; _frameTimer = 0; }
        AdvanceFrame(dt);
    }

    public void Update(float dt, bool moveLeft, bool moveRight, bool jump)
    {
        var vx = 0f;
        if (moveLeft) { vx -= WalkSpeed; _facingLeft = true; }
        if (moveRight) { vx += WalkSpeed; _facingLeft = false; }

        if (jump && _onGround) { _velocity.Y = JumpSpeed; _onGround = false; }
        if (!_onGround) _velocity.Y += Gravity * dt;

        Position += new Vector2(vx * dt, _velocity.Y * dt);

        if (Position.Y >= 0f)
        {
            Position = new Vector2(Position.X, 0f);
            _velocity.Y = 0f;
            _onGround = true;
        }

        var newStance = !_onGround ? Stance.Jump : (vx != 0) ? Stance.Walk1 : Stance.Stand1;
        if (newStance != _stance) { _stance = newStance; _frame = 0; _frameTimer = 0; }

        AdvanceFrame(dt);
    }

    private void AdvanceFrame(float dt)
    {
        if (!_frames.TryGetValue(_stance, out var frames) || frames.Count == 0) return;
        var delayMs = frames[Math.Min(_frame, frames.Count - 1)].Delay;
        if (delayMs <= 0) delayMs = 120;
        _frameTimer += dt * 1000f;
        if (_frameTimer >= delayMs)
        {
            _frameTimer -= delayMs;
            _frame = (_frame + 1) % frames.Count;
        }
    }

    public void Draw(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        // Full composed avatar when a look + renderer are attached.
        if (_renderer is not null && _look is not null)
        {
            _renderer.Draw(sb, _look, stat: null, _stance, _frame, screenPos, _facingLeft);
            return;
        }

        if (!_loaded || !_frames.TryGetValue(_stance, out var frames) || frames.Count == 0)
        {
            DrawPlaceholder(sb, white, screenPos);
            return;
        }

        var f = frames[Math.Min(_frame, frames.Count - 1)];
        var flip = _facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        DrawPart(sb, f.Body, screenPos, flip);
        DrawPart(sb, f.Arm, screenPos, flip);
        DrawPart(sb, f.Hand, screenPos, flip);
        DrawPart(sb, f.Head, screenPos, flip);
    }

    private static void DrawPart(SpriteBatch sb, WzSprite? sprite, Vector2 charPos, SpriteEffects flip) =>
        sprite?.Draw(sb, charPos, flip);

    private void DrawPlaceholder(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        var rect = new Rectangle(
            (int)(screenPos.X - PlaceholderW / 2f),
            (int)(screenPos.Y - PlaceholderH),
            PlaceholderW, PlaceholderH);
        sb.Draw(white, rect, new Color(60, 40, 80, 200));
        var head = new Rectangle(rect.X + 5, rect.Y - 18, 20, 18);
        sb.Draw(white, head, new Color(220, 180, 140, 200));
    }

    private WzSprite? LoadPart(WzProperty? frameNode, string partName)
    {
        var canvas = frameNode?.Get(partName) as WzCanvas;
        return canvas is null ? null : _loader.Load(canvas);
    }

    private static int ReadDelay(WzProperty? node)
    {
        if (node is null) return 120;
        return node.Get("delay") switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            _ => 120,
        };
    }

    private sealed class LayerFrame
    {
        public int Delay;
        public WzSprite? Body;
        public WzSprite? Head;
        public WzSprite? Arm;
        public WzSprite? Hand;
    }
}
