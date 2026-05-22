using MapleClaude.Map;
using MapleClaude.Net.Packet;
using Microsoft.Xna.Framework;

namespace MapleClaude.Character;

/// <summary>
/// Platformer physics + foothold ground-snap. Produces correctly-typed
/// move-path elements for outgoing <c>UserMove(44)</c>:
///   NORMAL       (attr 0)  — walking / falling
///   JUMP         (attr 1)  — jump start (just Vx/Vy)
///   START_FALL_DOWN (attr 11) — left foothold mid-air
///
/// Velocity units match v95: pixels/second, Y positive = down.
/// </summary>
public sealed class PlayerController
{
    // ── Constants matching v95 client physics ─────────────────────────────────
    private const float WalkSpeed       = 140f;   // px/s
    private const float JumpSpeed       = 555f;   // px/s  (upward = negative Y)
    private const float Gravity         = 2000f;  // px/s²
    private const float MaxFallSpeed    = 670f;   // px/s (terminal velocity)
    private const float FlushSeconds    = 0.10f;  // send move-path every 100 ms
    private const int   MaxElements     = 12;

    private readonly FieldScene _field;

    // ── Physics state ─────────────────────────────────────────────────────────
    private Vector2 _velocity;
    private bool    _grounded;
    private bool    _wasGrounded;
    private int     _currentFoothold;

    // ── Animation ─────────────────────────────────────────────────────────────
    private float _animTimer;
    private float _flushTimer;

    // ── Move-path ─────────────────────────────────────────────────────────────
    private readonly List<MoveElement> _pending = new();
    private Vector2 _lastSyncPos;
    private Vector2 _lastSyncVel;

    // ── Input edge-detect ─────────────────────────────────────────────────────
    private bool _prevJump;

    // ── Transient swing pose (set by TriggerAttack, decays each tick) ──────────
    private float _swingTimer;
    private const float SwingDuration = 0.4f;

    public Vector2 Position   { get; set; }
    public Stance  Stance     { get; private set; } = Stance.Stand1;
    public int     Frame      { get; private set; }
    public bool    FacingLeft { get; private set; }

    /// <summary>Force the player into the swing pose for <see cref="SwingDuration"/> seconds.</summary>
    public void TriggerAttack() => _swingTimer = SwingDuration;

    public PlayerController(FieldScene field)
    {
        _field = field;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(PlayerInput input, float dt)
    {
        _wasGrounded = _grounded;

        // Horizontal
        var dir = (input.Left ? -1 : 0) + (input.Right ? 1 : 0);
        if (dir != 0)
        {
            _velocity    = new Vector2(WalkSpeed * dir, _velocity.Y);
            FacingLeft   = dir < 0;
        }
        else
        {
            _velocity = new Vector2(_velocity.X * MathF.Pow(0.05f, dt), _velocity.Y);
            if (MathF.Abs(_velocity.X) < 1f) _velocity = new Vector2(0f, _velocity.Y);
        }

        // Jump — edge-detect: only trigger on the frame the key is first pressed
        var jumpEdge = input.JumpPressed && !_prevJump;
        _prevJump = input.JumpPressed;

        if (jumpEdge && _grounded)
        {
            // Emit JUMP element BEFORE applying the new velocity so the move-path
            // shows the velocities that explain the subsequent positions.
            _pending.Add(new MoveElement
            {
                Attr       = 1,                    // JUMP
                Vx         = (short)(_velocity.X),
                Vy         = (short)(-JumpSpeed),
                MoveAction = StanceMoveAction(Stance.Jump),
                Elapse     = 0,
            });
            _velocity  = new Vector2(_velocity.X, -JumpSpeed);
            _grounded  = false;
        }

        // Gravity
        if (!_grounded)
        {
            var newVy = Math.Min(_velocity.Y + Gravity * dt, MaxFallSpeed);
            _velocity = new Vector2(_velocity.X, newVy);
        }

        var prevPos = Position;
        Position += _velocity * dt;
        SnapToFoothold();

        // START_FALL_DOWN: was on a foothold, now in the air, didn't jump
        if (_wasGrounded && !_grounded && !jumpEdge)
        {
            _pending.Add(new MoveElement
            {
                Attr          = 11,                // START_FALL_DOWN
                Vx            = (short)_velocity.X,
                Vy            = (short)_velocity.Y,
                FhFallStart   = (short)_currentFoothold,
                MoveAction    = StanceMoveAction(Stance.Jump),
                Elapse        = 0,
            });
        }

        // Update stance — a live swing pose overrides the movement-derived one.
        if (_swingTimer > 0f)
        {
            _swingTimer -= dt;
            Stance = Stance.Swing;
        }
        else
        {
            Stance = !_grounded          ? Stance.Jump
                   : MathF.Abs(_velocity.X) > 1f ? Stance.Walk1
                   : Stance.Stand1;
        }

        _animTimer += dt;
        if (_animTimer >= 0.18f) { _animTimer -= 0.18f; Frame = (Frame + 1) % 4; }

        // Accumulate move-path
        _flushTimer += dt;
        if (_flushTimer >= FlushSeconds || _pending.Count >= MaxElements)
            AppendNormal();
    }

    // ── Foothold snap ─────────────────────────────────────────────────────────

    private void SnapToFoothold()
    {
        if (_field.Footholds.Count == 0)
        {
            _grounded = true;
            return;
        }

        Foothold? best      = null;
        var       bestDy    = float.PositiveInfinity;
        foreach (var (_, fh) in _field.Footholds)
        {
            var y = fh.YAt(Position.X);
            if (y is null) continue;
            var dy = y.Value - Position.Y;
            if (dy >= -2 && dy < bestDy) { bestDy = dy; best = fh; }
        }

        if (best is null) { _grounded = false; return; }

        var fhY = best.YAt(Position.X) ?? Position.Y;
        if (Position.Y >= fhY - 2)
        {
            Position           = new Vector2(Position.X, fhY);
            _velocity          = new Vector2(_velocity.X, 0f);
            _grounded          = true;
            _currentFoothold   = best.Id;
        }
        else
        {
            _grounded = false;
        }
    }

    // ── Move-path accumulation ────────────────────────────────────────────────

    private void AppendNormal()
    {
        var elapsedMs = (short)Math.Clamp(_flushTimer * 1000f, 1, 1000);
        _pending.Add(new MoveElement
        {
            Attr       = 0,                   // NORMAL
            X          = (short)Position.X,
            Y          = (short)Position.Y,
            Vx         = (short)_velocity.X,
            Vy         = (short)_velocity.Y,
            Fh         = (short)_currentFoothold,
            MoveAction = StanceMoveAction(Stance),
            Elapse     = elapsedMs,
        });
        _flushTimer   = 0f;
        _lastSyncPos  = Position;
        _lastSyncVel  = _velocity;
    }

    /// <summary>
    /// Flush the accumulated move path into a wire blob.
    /// Returns true + blob when there are elements; false otherwise.
    /// </summary>
    public bool TryFlushMovePath(out byte[] blob)
    {
        if (_pending.Count == 0)
        {
            // Ensure at least one NORMAL sample was queued
            AppendNormal();
        }
        if (_pending.Count == 0) { blob = Array.Empty<byte>(); return false; }

        blob = MovePathEncoder.Encode(
            (short)_lastSyncPos.X, (short)_lastSyncPos.Y,
            (short)_lastSyncVel.X, (short)_lastSyncVel.Y,
            _pending);
        _pending.Clear();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // v95 move-action byte: encodes stance + facing direction
    // High nibble = direction (0=right, 1=left), low nibble = stance index
    private byte StanceMoveAction(Stance s)
    {
        var stIdx = s switch
        {
            Stance.Stand1 => 0,
            Stance.Stand2 => 1,
            Stance.Walk1  => 2,
            Stance.Walk2  => 3,
            Stance.Jump   => 5,
            Stance.Alert  => 8,
            Stance.Prone  => 12,
            Stance.Sit    => 15,
            _             => 0,
        };
        return (byte)((FacingLeft ? 1 : 0) << 4 | (stIdx & 0x0F));
    }
}
