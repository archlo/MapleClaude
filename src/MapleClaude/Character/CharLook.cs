using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// Renders a Maple character by compositing body, head, arm, and hand layers
/// from Character.wz. Supports stand1/walk1/jump stances, left/right flipping,
/// and WASD/arrow-key movement with basic gravity.
///
/// WZ paths:
///   body:  Character.wz/00002{skin:D3}.img/{stance}/{frame}/body
///   head:  Character.wz/00012{skin:D3}.img/{stance}/{frame}/head
///   arm:   .../arm  (same body img)
///   hand:  .../hand (same body img)
/// </summary>
public sealed class CharLook
{
    public enum Stance { Stand1, Walk1, Jump, Alert, Prone }

    private static readonly string[] StanceNames = ["stand1", "walk1", "jump", "alert", "prone"];

    // Sprites per stance per frame per layer
    private readonly Dictionary<Stance, List<LayerFrame>> _frames = new();

    private Stance _stance = Stance.Stand1;
    private int _frame;
    private float _frameTimer;
    private bool _facingLeft;

    // Physics
    public Vector2 Position { get; set; }
    private Vector2 _velocity;
    private bool _onGround = true;
    private const float Gravity = 980f;
    private const float JumpSpeed = -480f;
    private const float WalkSpeed = 120f;

    // Fallback placeholder dimensions (used when no sprites loaded)
    private const int PlaceholderW = 30;
    private const int PlaceholderH = 60;

    private readonly WzTextureLoader _loader;
    private bool _loaded;

    public int SkinId { get; }

    public CharLook(WzTextureLoader loader, int skinId = 0)
    {
        _loader = loader;
        SkinId = skinId;
    }

    /// <summary>
    /// Load sprites from Character.wz.  Safe to call with a null package — falls
    /// back to placeholder rectangle rendering.
    /// </summary>
    public void Load(WzPackage? charWz)
    {
        if (charWz is null) return;

        // GetItem navigates through .img files via full slash-delimited paths,
        // same pattern used in MapScene.LoadObjSprite.
        var bodyId = $"00002{SkinId:D3}.img";
        var headId = $"00012{SkinId:D3}.img";

        foreach (var stance in Enum.GetValues<Stance>())
        {
            var stName = StanceNames[(int)stance];
            var frameList = new List<LayerFrame>();
            var frameIdx = 0;
            while (true)
            {
                var frameNode = charWz.GetItem($"{bodyId}/{stName}/{frameIdx}") as WzProperty;
                var headFrame = charWz.GetItem($"{headId}/{stName}/{frameIdx}") as WzProperty;
                if (frameNode is null && headFrame is null) break;

                var lf = new LayerFrame
                {
                    Delay = ReadDelay(frameNode),
                    Body = LoadPart(frameNode, "body"),
                    Arm = LoadPart(frameNode, "arm") ?? LoadPart(frameNode, "armBelowHead"),
                    Hand = LoadPart(frameNode, "hand"),
                    Head = LoadPart(headFrame, "head"),
                };
                frameList.Add(lf);
                frameIdx++;
            }
            if (frameList.Count > 0)
                _frames[stance] = frameList;
        }

        _loaded = _frames.Count > 0;
    }

    public void Update(float dt, bool moveLeft, bool moveRight, bool jump)
    {
        // Horizontal movement
        var vx = 0f;
        if (moveLeft) { vx -= WalkSpeed; _facingLeft = true; }
        if (moveRight) { vx += WalkSpeed; _facingLeft = false; }

        // Jump
        if (jump && _onGround)
        {
            _velocity.Y = JumpSpeed;
            _onGround = false;
        }

        // Gravity
        if (!_onGround)
            _velocity.Y += Gravity * dt;

        Position += new Vector2(vx * dt, _velocity.Y * dt);

        // Simple ground plane at y=0
        if (Position.Y >= 0f)
        {
            Position = new Vector2(Position.X, 0f);
            _velocity.Y = 0f;
            _onGround = true;
        }

        // Pick stance
        var newStance = !_onGround ? Stance.Jump
            : (vx != 0) ? Stance.Walk1
            : Stance.Stand1;

        if (newStance != _stance)
        {
            _stance = newStance;
            _frame = 0;
            _frameTimer = 0;
        }

        // Advance animation frame
        if (_frames.TryGetValue(_stance, out var frames) && frames.Count > 0)
        {
            var delayMs = frames[_frame].Delay;
            if (delayMs <= 0) delayMs = 120;
            _frameTimer += dt * 1000f;
            if (_frameTimer >= delayMs)
            {
                _frameTimer -= delayMs;
                _frame = (_frame + 1) % frames.Count;
            }
        }
    }

    public void Draw(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        if (!_loaded)
        {
            DrawPlaceholder(sb, white, screenPos);
            return;
        }

        if (!_frames.TryGetValue(_stance, out var frames) || frames.Count == 0)
        {
            DrawPlaceholder(sb, white, screenPos);
            return;
        }

        var f = frames[Math.Min(_frame, frames.Count - 1)];
        var flip = _facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        // Draw order: body → arm → hand → head (approximate v95 order)
        DrawPart(sb, f.Body, screenPos, flip);
        DrawPart(sb, f.Arm, screenPos, flip);
        DrawPart(sb, f.Hand, screenPos, flip);
        DrawPart(sb, f.Head, screenPos, flip);
    }

    private static void DrawPart(SpriteBatch sb, WzSprite? sprite, Vector2 charPos, SpriteEffects flip)
    {
        if (sprite is null) return;
        // v95 character parts are drawn relative to a shared "navel" anchor.
        // Without the full anchor map system, draw each part at charPos offset
        // by its stored origin — this gives reasonable alignment for stand stance.
        sprite.Draw(sb, charPos, flip);
    }

    private void DrawPlaceholder(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        // Dark silhouette so the character is visible even without WZ assets
        var rect = new Rectangle(
            (int)(screenPos.X - PlaceholderW / 2f),
            (int)(screenPos.Y - PlaceholderH),
            PlaceholderW, PlaceholderH);
        sb.Draw(white, rect, new Color(60, 40, 80, 200));
        // Head circle (drawn as a smaller box)
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
