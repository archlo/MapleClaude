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
    private const float BodyHeight      = 60f;    // px, for airborne wall collision
    private const float ClimbSpeed      = 120f;   // px/s, ladder/rope climb speed
    private const float FlushSeconds    = 0.10f;  // send move-path every 100 ms
    private const int   MaxElements     = 12;

    private readonly FieldScene _field;

    // ── Physics state ─────────────────────────────────────────────────────────
    private Vector2 _velocity;
    private bool    _grounded;
    private bool    _wasGrounded;
    private int     _currentFoothold;
    private LadderRope? _climb;   // non-null while attached to a ladder/rope

    // ── Animation ─────────────────────────────────────────────────────────────
    private float _animTimer;
    private float _flushTimer;

    // ── Move-path ─────────────────────────────────────────────────────────────
    private readonly List<MoveElement> _pending = new();
    private Vector2 _lastSyncPos;
    private Vector2 _lastSyncVel;
    private Stance _lastSyncStance = Stance.Stand1;

    // ── Input edge-detect ─────────────────────────────────────────────────────
    private bool _prevJump;

    // ── Knockback / hit-stun ──────────────────────────────────────────────────
    // While _staggerTimer > 0 we drop all movement input so the impulse from
    // ApplyKnockback actually carries the player; gravity + the existing
    // FallFreely/ClampToBounds path handle the landing the same way a normal
    // fall does. Mirrors CUserLocal::SetDamaged's input-lock window.
    private float _staggerTimer;

    public Vector2 Position   { get; set; }
    public Stance  Stance     { get; private set; } = Stance.Stand1;
    public int     Frame      { get; private set; }
    public bool    FacingLeft { get; private set; }

    /// <summary>True while standing on a foothold (not airborne / climbing). Used to
    /// root a grounded melee swing so the avatar doesn't slide while attacking.</summary>
    public bool    Grounded   => _grounded;

    /// <summary>True while a knockback impulse is still playing out — movement input is
    /// locked so the player visibly flies back instead of immediately being overridden by
    /// WASD. Cleared automatically when the stagger window expires.</summary>
    public bool    IsStaggered => _staggerTimer > 0f;

    /// <summary>Apply a knockback impulse: overrides the current velocity with (vx, vy),
    /// goes airborne, then physics carries it. Locks movement input for staggerSec. The
    /// real v95 client passes raw velocity (no fixed strength constant) via
    /// CVecCtrl::SetImpactNext — caller picks vx (sign = push direction away from the
    /// attacker) and vy (negative = upward arc; gravity at <c>2000 px/s²</c> brings the
    /// player back down).</summary>
    public void ApplyKnockback(float vx, float vy, float staggerSec = 0.40f)
    {
        _velocity     = new Vector2(vx, vy);
        _grounded     = false;
        _staggerTimer = staggerSec;
    }

    /// <summary>Zero horizontal velocity (grounded only) so a melee swing stops the walk
    /// instantly instead of decelerating into a visible slide. No-op airborne / climbing
    /// to keep jump-attack drift and ladder movement intact.</summary>
    public void StopWalking()
    {
        if (_grounded && _climb is null) _velocity = new Vector2(0f, _velocity.Y);
    }

    /// <summary>True while attached to a ladder/rope AND actively climbing (Up or Down
    /// held). Drives the climb-frame freeze in <see cref="CharLook"/> so the pose holds
    /// when you stop.</summary>
    public bool    ClimbMoving { get; private set; }

    public PlayerController(FieldScene field)
    {
        _field = field;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(PlayerInput input, float dt)
    {
        _wasGrounded = _grounded;

        // Stagger countdown (knockback recovery): while > 0 we ignore ALL movement input
        // so the impulse from ApplyKnockback carries the player away from the attacker
        // instead of being overridden by held keys / jump. Ladder/rope ticks below check
        // their own gates, so this just zeroes the input mask uniformly.
        if (_staggerTimer > 0f)
        {
            _staggerTimer = Math.Max(0f, _staggerTimer - dt);
            input = default;
        }

        // Ladder/rope climbing owns movement while attached; grabbing one also skips normal movement.
        if (_climb is not null)
        {
            TickAnimAndFlush(dt, advanceFrame: UpdateClimb(input, dt));
            return;
        }
        if (TryGrabLadder(input))
        {
            TickAnimAndFlush(dt);
            return;
        }

        // Horizontal
        var dir = (input.Left ? -1 : 0) + (input.Right ? 1 : 0);
        if (dir != 0)
        {
            _velocity    = new Vector2(WalkSpeed * dir, _velocity.Y);
            FacingLeft   = dir < 0;
        }
        else if (_grounded)
        {
            // No input on the ground: stop briskly (~0.1s) so releasing a key returns to standing
            // instead of gliding. (Airborne keeps its horizontal momentum until landing.)
            var vx = _velocity.X;
            var dec = WalkSpeed * 10f * dt;
            _velocity = new Vector2(MathF.Abs(vx) <= dec ? 0f : vx - MathF.Sign(vx) * dec, _velocity.Y);
        }

        // Jump trigger: fire on the rising edge, but also whenever grounded while the key is held — so
        // holding jump re-jumps on every landing (you jump, become airborne, land, jump again) instead of
        // requiring a fresh tap each time. The `&& _grounded` gate below already limits this to one jump
        // per ground contact (jumping immediately leaves the ground).
        var jumpEdge = input.JumpPressed && (!_prevJump || _grounded);
        _prevJump = input.JumpPressed;

        var downJumped = false;
        if (jumpEdge && _grounded)
        {
            if (input.Down)
            {
                // Down-jump: drop through the current foothold to the platform below — only when it
                // isn't solid (ForbidFallDown) and there actually is a foothold below to land on.
                var cur = _field.GetFoothold(_currentFoothold);
                if ((cur is null || !cur.ForbidFallDown)
                    && _field.GetFootholdBelow(Position.X, Position.Y + 6f) is not null)
                {
                    Position   = new Vector2(Position.X, Position.Y + 6f);   // nudge just below the ground
                    _velocity  = new Vector2(_velocity.X, 1f);              // start descending
                    _grounded  = false;
                    downJumped = true;
                }
                // else: nothing below / solid ground — Down+Jump does NOT jump up.
            }
            else
            {
                // Normal jump. Emit JUMP BEFORE applying the velocity so the move-path explains the
                // subsequent positions.
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
        }

        // Integrate with proper foothold collision: walk the floor when grounded, fall otherwise.
        if (_grounded)
        {
            WalkOnFoothold(_velocity.X * dt);
        }
        else
        {
            FallFreely(dt);
        }
        ClampToBounds();   // keep the player inside the map's VR (or foothold AABB) after either path

        // START_FALL_DOWN: was on a foothold, now in the air, didn't jump
        if (_wasGrounded && !_grounded && (!jumpEdge || downJumped))
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

        // Movement-derived stance. A basic-attack swing is a one-shot animation owned
        // by CharLook (it overrides the drawn pose and reverts on completion), so the
        // controller's stance stays movement-driven — you can walk/jump while swinging.
        Stance = !_grounded             ? Stance.Jump
               : input.Down && dir == 0 ? Stance.Prone
               : dir != 0               ? Stance.Walk1   // walk anim even when blocked against a wall
               : Stance.Stand1;

        TickAnimAndFlush(dt);
    }

    /// <summary>Advance the 4-frame animation cycle (skipped when <paramref name="advanceFrame"/> is false,
    /// e.g. idle on a ladder so the climb pose freezes) and accumulate/flush the move-path. Shared by the
    /// walk/jump/fall path and the ladder/rope climb path.</summary>
    private void TickAnimAndFlush(float dt, bool advanceFrame = true)
    {
        if (advanceFrame)
        {
            _animTimer += dt;
            if (_animTimer >= 0.18f) { _animTimer -= 0.18f; Frame = (Frame + 1) % 4; }
        }

        // Accumulate move-path — but only when the player is actually moving or changing state.
        // While idle (grounded, velocity ~0, stance unchanged) we queue nothing, so the per-frame
        // flush in GameStage sends nothing. The v95 client likewise stops emitting UserMove while
        // standing still instead of spamming it ~60×/s.
        _flushTimer += dt;
        if ((_flushTimer >= FlushSeconds || _pending.Count >= MaxElements) && HasChangedSinceSync())
            AppendNormal();
    }

    // ── Spawn + foothold collision ────────────────────────────────────────────

    /// <summary>Place the player on the ground beneath <paramref name="pos"/> (portal arrival). Snaps
    /// directly onto a foothold when the point already sits on one; otherwise leaves the player airborne
    /// to drop onto the floor below via gravity (the authentic spawn "drop-in").</summary>
    public void Spawn(Vector2 pos)
    {
        Position  = pos;
        _velocity = Vector2.Zero;
        var fh = _field.GetFootholdBelow(pos.X, pos.Y);
        if (fh is not null && fh.YAt(pos.X) is { } gy && gy - pos.Y <= 4f)
        {
            Position         = new Vector2(pos.X, gy);
            _currentFoothold = fh.Id;
            _grounded        = true;
        }
        else
        {
            _grounded = false;
        }
        _wasGrounded = _grounded;
        _prevJump    = false;
        // Treat the spawn pose as the baseline so we don't emit a spurious idle UserMove on arrival;
        // the drop-in fall (if airborne) is real movement and re-triggers sends naturally.
        _pending.Clear();
        _flushTimer     = 0f;
        _lastSyncPos    = Position;
        _lastSyncVel    = _velocity;
        _lastSyncStance = Stance;
    }

    /// <summary>Grounded: move along the current foothold's slope by <paramref name="dx"/>, crossing
    /// onto connected footholds (prev/next) at the ends. Walking off an open edge drops into a fall.</summary>
    private void WalkOnFoothold(float dx)
    {
        var fh = _field.GetFoothold(_currentFoothold)
               ?? _field.GetFootholdBelow(Position.X, Position.Y - 4f);
        if (fh is null) { _grounded = false; return; }

        var newX = Position.X + dx;
        for (var guard = 0; guard < 64; guard++)
        {
            float lo = Math.Min(fh.X1, fh.X2), hi = Math.Max(fh.X1, fh.X2);
            if (newX < lo)
            {
                var edgeY = fh.YAt(lo) ?? Position.Y;
                var nId   = fh.X2 >= fh.X1 ? fh.Prev : fh.Next;   // neighbour at the left end
                var next  = nId != 0 ? _field.GetFoothold(nId) : null;
                if (next is null) { Position = new Vector2(newX, edgeY); _grounded = false; return; }   // open end → walk off → fall
                if (next.IsWall)   // vertical neighbour
                {
                    if (Math.Min(next.Y1, next.Y2) < edgeY - 4f)   // extends UP from the edge → a real wall → stop
                    {
                        Position = new Vector2(lo, edgeY); _velocity = new Vector2(0f, _velocity.Y); _grounded = true; return;
                    }
                    Position = new Vector2(newX, edgeY); _grounded = false; return;   // drops DOWN → a ledge → walk off → fall
                }
                fh = next; continue;   // continuous neighbour → keep walking
            }
            if (newX > hi)
            {
                var edgeY = fh.YAt(hi) ?? Position.Y;
                var nId   = fh.X2 >= fh.X1 ? fh.Next : fh.Prev;   // neighbour at the right end
                var next  = nId != 0 ? _field.GetFoothold(nId) : null;
                if (next is null) { Position = new Vector2(newX, edgeY); _grounded = false; return; }   // open end → walk off → fall
                if (next.IsWall)   // vertical neighbour
                {
                    if (Math.Min(next.Y1, next.Y2) < edgeY - 4f)   // extends UP from the edge → a real wall → stop
                    {
                        Position = new Vector2(hi, edgeY); _velocity = new Vector2(0f, _velocity.Y); _grounded = true; return;
                    }
                    Position = new Vector2(newX, edgeY); _grounded = false; return;   // drops DOWN → a ledge → walk off → fall
                }
                fh = next; continue;   // continuous neighbour → keep walking
            }
            Position         = new Vector2(newX, fh.YAt(newX) ?? Position.Y);
            _currentFoothold = fh.Id;
            _velocity        = new Vector2(_velocity.X, 0f);
            _grounded        = true;
            return;
        }
        _grounded = false;   // pathological chain — fall rather than loop forever
    }

    /// <summary>Airborne: apply gravity (clamped to terminal speed) and integrate, landing via a
    /// continuous ground-crossing test so a fast fall can't tunnel through a foothold.</summary>
    private void FallFreely(float dt)
    {
        var vy = Math.Min(_velocity.Y + Gravity * dt, MaxFallSpeed);
        _velocity = new Vector2(_velocity.X, vy);

        var newX = Position.X + _velocity.X * dt;
        // Airborne wall collision (authentic ZMass gate, CWvsPhysicalSpace2D): only walls in the player's
        // own connected foothold group block, so a tall wall on another platform never pins the jump. Keep
        // Vx so a same-group wall is cleared the instant the feet rise above its top.
        if (_velocity.X != 0f
            && _field.GetFoothold(_currentFoothold) is { ZMass: var zmass and not 0 }
            && _field.GetZMassWallX(zmass, Position.X, newX, Position.Y - BodyHeight, Position.Y) is { } wallX)
        {
            newX = wallX;
        }
        var newY = Position.Y + vy * dt;

        if (vy > 0f)   // only landings happen while descending
        {
            var fh = _field.GetFootholdBelow(newX, Position.Y);
            if (fh is not null && fh.YAt(newX) is { } groundY
                && Position.Y <= groundY && newY >= groundY)
            {
                Position         = new Vector2(newX, groundY);
                _velocity        = new Vector2(_velocity.X, 0f);
                _grounded        = true;
                _currentFoothold = fh.Id;
                return;
            }
        }

        Position = new Vector2(newX, newY);
    }

    /// <summary>Keep the player inside the map's movement bounds (the VR rectangle, or the foothold AABB
    /// when the map has no VR) so they can't walk or jump past the visual range. Velocity is preserved so
    /// the walk animation / jump arc keep playing while pinned at an edge.</summary>
    private void ClampToBounds()
    {
        var b = _field.Bounds;
        var x = Math.Clamp(Position.X, b.Left, b.Right);
        var y = Math.Clamp(Position.Y, b.Top, b.Bottom);
        if (x == Position.X && y == Position.Y) return;
        // Hit a VR edge: zero the perpendicular velocity (matches CVecCtrl::BoundPosMapRange). The walk
        // stance is driven by input direction, so the walk animation still plays while pinned at the edge.
        _velocity = new Vector2(x != Position.X ? 0f : _velocity.X, y != Position.Y ? 0f : _velocity.Y);
        Position  = new Vector2(x, y);
    }

    // ── Ladder / rope climbing ─────────────────────────────────────────────────

    /// <summary>Grab a ladder/rope when Up/Down is pressed and one is in reach (works grounded or
    /// mid-air). Returns true once attached. Mirrors the v95 grab: x within ±10 of the ladder, y inside
    /// its span; refuses to grab "up" when already at the top or "down" when already at the bottom.</summary>
    private bool TryGrabLadder(PlayerInput input)
    {
        if (!input.Up && !input.Down) return false;
        if (_field.GetLadderOrRope(Position.X, Position.Y) is not { } lr) return false;
        if (input.Up && !input.Down && Position.Y <= lr.Top + 2f) return false;
        if (input.Down && !input.Up && Position.Y >= lr.Bottom - 2f) return false;

        _climb           = lr;
        _grounded        = false;
        _currentFoothold = 0;
        _velocity        = Vector2.Zero;
        ClimbMoving      = false;
        // Snap onto the ladder centre AND into its [Top, Bottom] span. Grabbing from
        // the ground at the base leaves Position.Y at/below Bottom; clamping in means
        // the first climb tick moves up the ladder instead of immediately stepping
        // back off the near end (the "puts me in position but won't climb" bug).
        Position         = new Vector2(lr.X, Math.Clamp(Position.Y, lr.Top, lr.Bottom));
        Stance           = lr.IsLadder ? Stance.Ladder : Stance.Rope;
        return true;
    }

    /// <summary>Climb the attached ladder/rope. Returns true while actually moving (so the climb pose
    /// animates; it freezes when idle). Up = toward Top, Down = toward Bottom; no gravity; x stays on the
    /// ladder. Reaching an end steps onto the platform there; the Jump key hops off.</summary>
    private bool UpdateClimb(PlayerInput input, float dt)
    {
        var lr = _climb!;

        // Hop off with Jump (edge-detected), carrying any held left/right.
        var jumpEdge = input.JumpPressed && !_prevJump;
        _prevJump = input.JumpPressed;
        if (jumpEdge)
        {
            var hopDir = (input.Left ? -1 : 0) + (input.Right ? 1 : 0);
            _climb       = null;
            ClimbMoving  = false;
            _grounded    = false;
            if (hopDir != 0) FacingLeft = hopDir < 0;
            _velocity = new Vector2(hopDir * WalkSpeed, -JumpSpeed * 0.7f);
            Stance    = Stance.Jump;
            return true;
        }

        var iy = (input.Up ? -1 : 0) + (input.Down ? 1 : 0);
        _velocity   = new Vector2(0f, iy * ClimbSpeed);
        ClimbMoving = iy != 0;
        var newY = Position.Y + iy * ClimbSpeed * dt;

        // Direction-aware exits: only step off the TOP while climbing up, and off the
        // BOTTOM while climbing down. (A single boundary test fires the instant you grab
        // at the near end and refuses to climb — and bounces you off when starting from
        // the platform above.)
        if (iy < 0 && newY <= lr.Top)        // climbing up, reached the top → onto the platform above
        {
            LeaveLadderOntoGround(lr.X, lr.Top - 6f);
            return true;
        }
        if (iy > 0 && newY >= lr.Bottom)     // climbing down, reached the bottom → onto the floor below
        {
            LeaveLadderOntoGround(lr.X, lr.Bottom + 2f);
            return true;
        }

        Position = new Vector2(lr.X, Math.Clamp(newY, lr.Top, lr.Bottom));
        Stance   = lr.IsLadder ? Stance.Ladder : Stance.Rope;
        return iy != 0;            // animate only while moving
    }

    /// <summary>Detach from a ladder/rope at (<paramref name="x"/>, <paramref name="y"/>) and land on the
    /// foothold below if any, otherwise fall.</summary>
    private void LeaveLadderOntoGround(float x, float y)
    {
        _climb       = null;
        ClimbMoving  = false;
        _velocity    = Vector2.Zero;
        if (_field.GetFootholdBelow(x, y) is { } fh && fh.YAt(x) is { } gy)
        {
            Position         = new Vector2(x, gy);
            _currentFoothold = fh.Id;
            _grounded        = true;
        }
        else
        {
            Position  = new Vector2(x, y);
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
        _flushTimer     = 0f;
        _lastSyncPos    = Position;
        _lastSyncVel    = _velocity;
        _lastSyncStance = Stance;
    }

    // True when position, velocity, or stance has meaningfully changed since the last queued sample.
    // Gates move-path accumulation so an idle, grounded player emits no UserMove traffic.
    private bool HasChangedSinceSync()
        => Stance != _lastSyncStance
        || Vector2.DistanceSquared(Position, _lastSyncPos) > 0.25f
        || Vector2.DistanceSquared(_velocity, _lastSyncVel) > 0.25f;

    /// <summary>
    /// Flush the accumulated move path into a wire blob.
    /// Returns true + blob when there are elements; false otherwise.
    /// </summary>
    public bool TryFlushMovePath(out byte[] blob)
    {
        // Nothing queued → the player hasn't moved since the last send; don't emit an idle UserMove.
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
            Stance.Ladder => 6,
            Stance.Rope   => 7,
            Stance.Alert  => 8,
            Stance.Prone  => 12,
            Stance.Sit    => 15,
            _             => 0,
        };
        return (byte)((FacingLeft ? 1 : 0) << 4 | (stIdx & 0x0F));
    }
}
