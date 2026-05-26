using MapleClaude.Domain;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Character;

/// <summary>
/// An animated Maple avatar. Animation timing is driven by the WZ <c>delay</c>
/// nodes (<c>Character/0000200X.img/&lt;action&gt;/&lt;frame&gt;/delay</c>), not a
/// fixed cadence, so every action plays at its authored speed — looping movement
/// <see cref="Stance"/>s (stand1/walk1/ladder/rope/…) and one-shot attack
/// <see cref="AttackAction"/>s (swingO1/stabO1/…) alike.
///
/// The drawn action is the one-shot attack while one is playing, otherwise the
/// movement stance: a basic attack (<see cref="Attack"/>) runs once through its
/// frames and reverts to whatever movement stance is current when it ends; a
/// ladder/rope climb freezes on its frame while idle and advances only while
/// moving (authentic hand-over-hand that stops when you stop).
///
/// With a <see cref="CharacterRenderer"/> + <see cref="AvatarLook"/> attached
/// (<see cref="SetAvatar"/>) Draw composes the full avatar; otherwise it falls
/// back to a body-only render or a placeholder silhouette.
/// </summary>
public sealed class CharLook
{
    private readonly WzTextureLoader _loader;
    private WzPackage? _charWz;

    // Per-action frame data (delays + body-only sprites), loaded lazily on first
    // use and cached by WZ action key.
    private readonly Dictionary<string, ActionFrames> _actionCache = new(StringComparer.Ordinal);

    // ── Visual state ──────────────────────────────────────────────────────────
    private string _moveAction = "stand1";   // current looping movement stance key
    private string? _attackAction;            // one-shot attack action key (null = none)
    private int _frame;
    private float _elapsedMs;
    private bool _facingLeft;
    private bool _climbing;                    // current movement stance is ladder/rope
    private bool _climbMoving;                 // climbing AND actually moving (else freeze)

    // Face emotion (the F1..F7 expressions). 0 = open-eye / blink default; 1..23 =
    // an active emotion playing its own per-frame WZ delays. Driven by SetEmotion
    // (local trigger or server broadcast) and advanced inside AdvanceEmotion each
    // tick; reverts to 0 when the duration elapses (matches CAvatar::Update).
    private int   _emotion;
    private int   _emotionFrame;
    private float _emotionFrameTimer;
    private float _emotionEndsInMs;

    public bool FacingLeft => _facingLeft;
    public bool IsAttacking => _attackAction is not null;

    /// <summary>Current emotion id (0 = none/default). Read by the renderer at draw time.</summary>
    public int EmotionId => _emotion;
    /// <summary>Current frame index inside the active emotion's WZ frame list.</summary>
    public int EmotionFrame => _emotionFrame;

    // The action currently drawn: a live attack overrides the movement stance.
    private string CurrentAction => _attackAction ?? _moveAction;

    // Physics (used only for the pre-SetField demo movement on the title path).
    public Vector2 Position { get; set; }
    private Vector2 _velocity;
    private bool _onGround = true;
    private const float Gravity = 980f;
    private const float JumpSpeed = -480f;
    private const float WalkSpeed = 120f;

    private const int PlaceholderW = 30;
    private const int PlaceholderH = 60;

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
        _actionCache.Clear();   // skin/look may differ → reload frame data on demand
    }

    /// <summary>
    /// Bind the Character package for frame loading. Safe with a null package —
    /// Draw then falls back to a placeholder (or the renderer, if one was attached).
    /// </summary>
    public void Load(WzPackage? charWz)
    {
        _charWz = charWz;
        _loaded = charWz is not null;
    }

    // ── Driving the animation ───────────────────────────────────────────────────

    /// <summary>Animation-only update when an external controller owns movement.
    /// <paramref name="climbMoving"/> is honored only on ladder/rope: when false the
    /// climb pose freezes on its current frame.</summary>
    public void UpdateFromPhysics(float dt, Stance stance, bool facingLeft, bool climbMoving = true)
    {
        _facingLeft = facingLeft;
        SetMoveAction(stance.ToWzKey());
        _climbing = stance is Stance.Ladder or Stance.Rope;
        _climbMoving = climbMoving;
        AdvanceAnim(dt);
        AdvanceEmotion(dt);
    }

    /// <summary>Self-driven demo physics for the pre-SetField title path.</summary>
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

        SetMoveAction(!_onGround ? "jump" : vx != 0 ? "walk1" : "stand1");
        _climbing = false;
        AdvanceAnim(dt);
        AdvanceEmotion(dt);
    }

    /// <summary>Start (or revert) a face emotion. <paramref name="emotion"/>=0 cancels
    /// the current emotion and goes back to default/blink. <paramref name="durationMs"/>=-1
    /// means "use the WZ face's own per-frame total" — matches the v95 client's
    /// <c>CAvatar::PrepareFaceLayer</c> when called from the FuncKey path. Out-of-range
    /// emotions are clamped to 0..23.</summary>
    public void SetEmotion(int emotion, int durationMs)
    {
        _emotion = Math.Clamp(emotion, 0, 23);
        _emotionFrame = 0;
        _emotionFrameTimer = 0f;
        if (_emotion == 0) { _emotionEndsInMs = 0f; return; }
        if (durationMs < 0)
        {
            durationMs = _renderer is not null && _look is not null
                ? _renderer.EmotionDurationMs(_look.Face, _emotion)
                : 0;
        }
        _emotionEndsInMs = Math.Max(0, durationMs);
    }

    // Advance the active emotion's frame clock. Frames use their own per-frame
    // WZ delays (independent of the body action); the emotion reverts to default
    // when the total duration elapses.
    private void AdvanceEmotion(float dt)
    {
        if (_emotion == 0) return;
        var ms = dt * 1000f;
        _emotionEndsInMs -= ms;
        if (_emotionEndsInMs <= 0)
        {
            SetEmotion(0, 0);
            return;
        }
        if (_renderer is null || _look is null) return;
        var delays = _renderer.EmotionFrameDelays(_look.Face, _emotion);
        if (delays.Length == 0) return;
        _emotionFrameTimer += ms;
        for (var guard = 0; guard < 16; guard++)
        {
            var d = delays[Math.Min(_emotionFrame, delays.Length - 1)];
            if (d <= 0) d = 100;
            if (_emotionFrameTimer < d) break;
            _emotionFrameTimer -= d;
            _emotionFrame = (_emotionFrame + 1) % delays.Length;
            if (delays.Length == 1) break;
        }
    }

    /// <summary>Start a one-shot basic-attack animation for the equipped weapon
    /// (or <c>proneStab</c> when prone). Returns its total duration in seconds — for
    /// pacing the next swing; 0 when no avatar is attached, the action is empty, or
    /// the avatar is climbing (you can't swing on a ladder).</summary>
    public float Attack(bool prone)
    {
        if (_renderer is null || _look is null || _climbing) return 0f;
        var action = _renderer.PickAttackAction(_look, prone);
        var delays = GetFrames(action).Delays;
        if (delays.Length == 0) return 0f;

        _attackAction = action;
        _frame = 0;
        _elapsedMs = 0f;
        var total = 0;
        foreach (var d in delays) total += d;
        return total / 1000f;
    }

    /// <summary>Play a specific one-shot action (e.g. a skill's body action) once,
    /// reverting to the movement stance when it ends. Returns the duration in seconds,
    /// or 0 when the action has no frames (caller may fall back to a normal attack).</summary>
    public float PlayAction(string action)
    {
        if (_renderer is null || _look is null || _climbing || string.IsNullOrEmpty(action)) return 0f;
        var delays = GetFrames(action).Delays;
        if (delays.Length == 0) return 0f;

        _attackAction = action;
        _frame = 0;
        _elapsedMs = 0f;
        var total = 0;
        foreach (var d in delays) total += d;
        return total / 1000f;
    }

    private void SetMoveAction(string action)
    {
        if (_moveAction == action) return;
        _moveAction = action;
        // Don't disturb a live attack — it reverts to the new movement stance when
        // it ends. Otherwise restart the (looping) movement animation cleanly.
        if (_attackAction is null) { _frame = 0; _elapsedMs = 0f; }
    }

    // Advance the current action's frame index by elapsed time, consuming each
    // frame's authored delay. Movement stances loop; an attack reverts to the
    // movement stance when it reaches its end; a ladder/rope freezes while idle.
    private void AdvanceAnim(float dt)
    {
        var delays = GetFrames(CurrentAction).Delays;
        if (delays.Length == 0) return;
        if (_attackAction is null && _climbing && !_climbMoving) return;   // idle on a ladder → hold the frame

        _elapsedMs += dt * 1000f;
        for (var guard = 0; guard < 16; guard++)
        {
            var d = delays[Math.Min(_frame, delays.Length - 1)];
            if (d <= 0) d = 100;
            if (_elapsedMs < d) break;
            _elapsedMs -= d;

            if (_frame + 1 < delays.Length)
            {
                _frame++;
            }
            else if (_attackAction is not null)
            {
                _attackAction = null;           // attack done → revert to the movement stance
                _frame = 0;
                delays = GetFrames(CurrentAction).Delays;
                if (delays.Length == 0) break;
            }
            else
            {
                _frame = 0;                      // loop the movement stance
            }
        }
    }

    // ── Drawing ─────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, Texture2D white, Vector2 screenPos)
    {
        var action = CurrentAction;

        // Full composed avatar when a look + renderer are attached.
        if (_renderer is not null && _look is not null)
        {
            _renderer.Draw(sb, _look, stat: null, action, _frame, screenPos, _facingLeft,
                emotionId: _emotion, emotionFrame: _emotionFrame);
            return;
        }

        var frames = GetFrames(action);
        if (!_loaded || frames.Bodies.Length == 0)
        {
            DrawPlaceholder(sb, white, screenPos);
            return;
        }

        var f = frames.Bodies[Math.Min(_frame, frames.Bodies.Length - 1)];
        var flip = _facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        f.Body?.Draw(sb, screenPos, flip);
        f.Arm?.Draw(sb, screenPos, flip);
        f.Hand?.Draw(sb, screenPos, flip);
        f.Head?.Draw(sb, screenPos, flip);
    }

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

    // ── Lazy per-action frame data ───────────────────────────────────────────────

    private ActionFrames GetFrames(string action)
    {
        if (_actionCache.TryGetValue(action, out var cached)) return cached;
        var af = LoadAction(action);
        _actionCache[action] = af;
        return af;
    }

    private ActionFrames LoadAction(string action)
    {
        if (_charWz is null) return ActionFrames.Empty;
        var skin = _look?.Skin ?? SkinId;
        var bodyId = $"000{2000 + skin:D5}.img";
        var headId = $"000{12000 + skin:D5}.img";
        // The renderer composes parts itself; only the body-only fallback needs sprites.
        var loadSprites = _renderer is null;

        var delays = new List<int>();
        var bodies = new List<LayerFrame>();
        for (var i = 0; ; i++)
        {
            var bodyNode = _charWz.GetItem($"{bodyId}/{action}/{i}") as WzProperty;
            var headNode = _charWz.GetItem($"{headId}/{action}/{i}") as WzProperty;
            if (bodyNode is null && headNode is null) break;

            delays.Add(ReadDelay(bodyNode));
            bodies.Add(loadSprites
                ? new LayerFrame
                {
                    Body = LoadPart(bodyNode, "body"),
                    Arm  = LoadPart(bodyNode, "arm") ?? LoadPart(bodyNode, "armBelowHead"),
                    Hand = LoadPart(bodyNode, "hand"),
                    Head = LoadPart(headNode, "head"),
                }
                : LayerFrame.Empty);
        }
        return delays.Count == 0
            ? ActionFrames.Empty
            : new ActionFrames(delays.ToArray(), bodies.ToArray());
    }

    private WzSprite? LoadPart(WzProperty? frameNode, string partName)
    {
        var canvas = frameNode?.Get(partName) as WzCanvas;
        return canvas is null ? null : _loader.Load(canvas);
    }

    private static int ReadDelay(WzProperty? node)
    {
        if (node is null) return 100;
        var d = node.Get("delay") switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            _ => 100,
        };
        return d <= 0 ? 100 : d;
    }

    private sealed class LayerFrame
    {
        public static readonly LayerFrame Empty = new();
        public WzSprite? Body;
        public WzSprite? Head;
        public WzSprite? Arm;
        public WzSprite? Hand;
    }

    private sealed class ActionFrames
    {
        public static readonly ActionFrames Empty = new([], []);
        public readonly int[] Delays;
        public readonly LayerFrame[] Bodies;

        public ActionFrames(int[] delays, LayerFrame[] bodies)
        {
            Delays = delays;
            Bodies = bodies;
        }
    }
}
