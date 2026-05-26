using MapleClaude.Map;
using MapleClaude.Net.Packet;
using Microsoft.Xna.Framework;

namespace MapleClaude.Character;

/// <summary>
/// Per-controlled-mob AI simulation. The <em>controller</em> (= the nearest player, i.e.
/// us) owns each mob's movement + aggro and emits <c>CP_MobMove(227)</c> packets through
/// <see cref="MapleClaude.Net.Senders.GameSender.MobMove"/>. The Kinoko server runs no
/// mob AI of its own — it just validates the controller and rebroadcasts the MovePath —
/// so this class is the entire authority on how each mob moves locally and how others
/// see it move.
///
/// <para>Behaviour by <see cref="MobInfo.MoveAbility"/>:</para>
/// <list type="bullet">
///  <item><b>STAY (0)</b> — never moves; never emits MobMove.</item>
///  <item><b>WALK (1)</b> — paces along the current foothold, turns at edges, idle pauses.</item>
///  <item><b>JUMP (2)</b> — treated like WALK for now (occasional vertical jumps TODO).</item>
///  <item><b>FLY (3/4)</b> — free 2D drift inside <see cref="FieldScene.Bounds"/>.</item>
/// </list>
///
/// <para>Aggro:</para>
/// <list type="bullet">
///  <item><see cref="MobInfo.FirstAttack"/> mobs lock onto the nearest player on sight.</item>
///  <item>Passive mobs flip to aggro after <see cref="OnDamagedByPlayer"/> for
///        <c>AggroDurationSec</c>.</item>
/// </list>
///
/// Modelled on <see cref="PlayerController.AppendNormal"/> / <c>TryFlushMovePath</c>:
/// accumulates <see cref="MoveElement"/>s and flushes via <see cref="MovePathEncoder.Encode"/>.
/// The MovePath wire format is shared with <c>UserMove</c>.
/// </summary>
public sealed class MobController
{
    // ── Tuning (best-effort match to v95 mob feel; tweak as needed) ──────────────
    private const float BaseWalkSpeed    = 60f;    // px/s before info.Speed % modifier
    private const float BaseFlySpeed     = 90f;    // px/s before info.FlySpeed % modifier
    private const float MinSpeed         = 20f;    // floor so −100% mobs still creep
    private const float IdlePauseMin     = 1.5f;
    private const float IdlePauseMax     = 4.0f;
    private const float WalkBurstMin     = 1.0f;
    private const float WalkBurstMax     = 3.0f;
    private const float EdgeMargin       = 16f;    // turn around within Npx of a foothold edge
    private const float AttackRangeX     = 50f;
    private const float AttackRangeY     = 60f;
    private const float AttackCooldown   = 0.7f;   // GMS i-frame ≈ 700 ms
    private const float AggroDurationSec     = 8f;
    private const float HitPlayerCooldownSec = 2f;     // IDB: 2000 ms normal mobs vs the same player
    private const float FlushIntervalSec = 0.25f;  // emit MobMove ~4 ×/s while moving

    // ── Inputs ───────────────────────────────────────────────────────────────────
    private readonly MobLook    _mob;
    private readonly FieldScene _field;
    private readonly MobInfo    _info;
    private readonly Random     _rng;

    // ── State ────────────────────────────────────────────────────────────────────
    private enum State { Idle, Walking, Chasing, Attacking }
    private State   _state = State.Idle;
    private float   _stateTimer;
    private float   _attackCooldown;
    private float   _hitPlayerCooldown;   // per-mob: 2 s between body-hits on the same player (IDB)
    private float   _knockedTimer;        // > 0 = mob is recoiling from a melee hit this frame
    private float   _knockedVx;           // horizontal impulse (px/s) applied during the knock window
    private float   _aggroTimer;          // > 0 = aggressive (FirstAttack uses +∞)
    private bool    _facingLeft;
    private Vector2 _velocity;
    private int     _currentFh;           // current foothold id (0 if fly / unknown)
    private Vector2 _flyTarget;
    private float   _flyTargetTimer;

    // ── Move-path accumulation (mirrors PlayerController) ────────────────────────
    private readonly List<MoveElement> _pending = new();
    private Vector2 _lastSyncPos;
    private Vector2 _lastSyncVel;
    private float   _flushTimer;
    private short   _mobCtrlSn;           // monotonic per controller, echoed in MobCtrlAck

    public MobController(MobLook mob, FieldScene field, MobInfo info, Random rng)
    {
        _mob   = mob;
        _field = field;
        _info  = info;
        _rng   = rng;

        // Resolve the foothold the mob is standing on (the server placed it there at spawn;
        // we just need the id so the per-element Fh in the MovePath is meaningful).
        var below = _field.GetFootholdBelow(_mob.Position.X, _mob.Position.Y - 2);
        _currentFh = below?.Id ?? 0;

        // FirstAttack mobs are perpetually aggressive (chase nearest player on sight).
        if (_info.FirstAttack) _aggroTimer = float.PositiveInfinity;

        _lastSyncPos = _mob.Position;
        _lastSyncVel = Vector2.Zero;
        EnterIdle();
    }

    // ── Public surface ───────────────────────────────────────────────────────────

    /// <summary>STAY mobs (moveAbility == 0) never tick and never emit MobMove. The
    /// caller can skip them entirely.</summary>
    public bool ShouldTick => !_info.IsStay;

    /// <summary>Header action byte for the next MobMove (chases / attacks visible to others).</summary>
    public MobActionType CurrentAction => _state switch
    {
        State.Attacking => MobActionType.Attack1,
        State.Chasing   => MobActionType.Chase,
        State.Walking   => _info.IsFly ? MobActionType.Fly : MobActionType.Move,
        _               => _info.IsFly ? MobActionType.Fly : MobActionType.Stand,
    };

    public bool IsChasing => _state == State.Chasing || _state == State.Attacking;

    /// <summary>The mob's parsed Mob.wz info (level, BodyAttack, MoveAbility, …) — read by
    /// GameStage to decide whether this mob deals touch damage to the player.</summary>
    public MobInfo Info => _info;

    /// <summary>True while the mob is in active aggro (firstAttack always, or aggro-on-hit
    /// for AggroDurationSec). Used by GameStage's touch-damage trigger so only aggressive
    /// mobs hit the player on contact.</summary>
    public bool IsAggressive => _aggroTimer > 0;

    /// <summary>Called when our melee attack damages this mob. Passive mobs flip aggro;
    /// firstAttack mobs are already aggressive, so this is a no-op for them.</summary>
    public void OnDamagedByPlayer()
    {
        if (_aggroTimer != float.PositiveInfinity) _aggroTimer = AggroDurationSec;
    }

    /// <summary>True when this mob is allowed to body-hit the player again. Per the
    /// v95 IDB (CMob::ProcessAttack), normal mobs cool down 2000 ms between hits on
    /// the same player. Gates the GameStage touch-damage trigger so the same snail
    /// can't damage-spam every frame within the player's i-frame.</summary>
    public bool CanHitPlayer => _hitPlayerCooldown <= 0f;

    /// <summary>Called by GameStage right after a UserHit is sent for this mob.
    /// Resets the per-mob cooldown to <c>HitPlayerCooldownSec</c>.</summary>
    public void NotePlayerHit() => _hitPlayerCooldown = HitPlayerCooldownSec;

    /// <summary>Apply a small horizontal push when this mob takes a melee hit. The mob is
    /// shoved in the impulse direction (caller passes signed px) for a brief
    /// <see cref="HitKnockbackSec"/> window, during which the AI is suspended and the mob
    /// holds its Hit anim. After the window, the regular AI (wander / chase / attack)
    /// resumes — and OnDamagedByPlayer flipped passive mobs to aggro already, so they
    /// turn around and chase the attacker.</summary>
    public void ApplyHitKnockback(float pushPx)
    {
        _knockedTimer = HitKnockbackSec;
        _knockedVx    = pushPx / HitKnockbackSec;
    }

    private const float HitKnockbackSec = 0.20f;

    /// <summary>Advance the AI by <paramref name="dt"/>. <paramref name="playerPos"/> is the
    /// local player's world position (the chase target). The owning <see cref="MobLook"/>
    /// gets its position / facing / animation state updated as a side effect.</summary>
    public void Update(float dt, Vector2 playerPos)
    {
        if (_info.IsStay) return;

        if (_aggroTimer > 0 && _aggroTimer != float.PositiveInfinity)
            _aggroTimer = Math.Max(0f, _aggroTimer - dt);
        if (_attackCooldown > 0)    _attackCooldown    -= dt;
        if (_hitPlayerCooldown > 0) _hitPlayerCooldown -= dt;

        // Knockback override: a melee hit just pushed the mob. Drive position from the
        // impulse along the current foothold (or freely for fly mobs) for HitKnockbackSec,
        // holding the Hit anim, then resume the regular AI tick. Skips state selection /
        // chase / attack until the timer elapses.
        if (_knockedTimer > 0f)
        {
            _knockedTimer -= dt;
            var nx = _mob.Position.X + _knockedVx * dt;
            if (!_info.IsFly && _currentFh != 0 && _field.GetFoothold(_currentFh) is Foothold knockFh)
            {
                nx = Math.Clamp(nx, knockFh.LeftEdgeX + 4f, knockFh.RightEdgeX - 4f);
                var y = knockFh.YAt(nx) ?? _mob.Position.Y;
                _mob.Position = new Vector2(nx, y);
            }
            else
            {
                _mob.Position = new Vector2(nx, _mob.Position.Y);
            }
            _mob.SetState(MobLook.MobState.Hit);
            _velocity = new Vector2(_knockedVx, 0f);
            return;
        }

        var aggressive = _aggroTimer > 0;
        var dx          = playerPos.X - _mob.Position.X;
        var dy          = playerPos.Y - _mob.Position.Y;
        var inAttackBox = Math.Abs(dx) <= AttackRangeX && Math.Abs(dy) <= AttackRangeY;
        var prev        = _state;

        // ── State selection ──────────────────────────────────────────────────────
        if (aggressive)
        {
            _facingLeft = dx < 0;
            if (inAttackBox)
            {
                if (_attackCooldown <= 0f) _attackCooldown = AttackCooldown;
                _state    = State.Attacking;
                _velocity = Vector2.Zero;
            }
            else
            {
                _state = State.Chasing;
            }
        }
        else
        {
            _stateTimer -= dt;
            if (_stateTimer <= 0f)
            {
                if (_state == State.Walking) EnterIdle();
                else                          EnterWalk();
            }
        }

        // ── Movement step ────────────────────────────────────────────────────────
        var moving = _state == State.Walking || _state == State.Chasing;
        if (moving)
        {
            if (_info.IsFly) StepFly(dt, playerPos);
            else             StepWalk(dt);
        }
        else
        {
            SeatOnFoothold();
            _velocity = Vector2.Zero;
        }

        // ── Renderer sync ────────────────────────────────────────────────────────
        if (!_info.NoFlip) _mob.SetFacing(_facingLeft);
        _mob.SetState(_state switch
        {
            State.Walking or State.Chasing => MobLook.MobState.Move,
            State.Attacking                => MobLook.MobState.Attack,
            _                              => MobLook.MobState.Stand,
        });

        // ── Accumulate a path sample ─────────────────────────────────────────────
        // Emit while moving (every flush interval or after meaningful change), and force
        // an emit on a state transition so other clients see the change promptly.
        _flushTimer += dt;
        if ((moving && (_flushTimer >= FlushIntervalSec || HasChanged()))
            || _state != prev)
        {
            AppendElement();
        }
    }

    /// <summary>If a MovePath has accumulated, build the wire blob and bump
    /// <c>mobCtrlSn</c>. The caller pairs <paramref name="blob"/> + <paramref name="sn"/>
    /// with <see cref="CurrentAction"/> / <see cref="FacingLeft"/> / <see cref="IsChasing"/>
    /// to drive <c>GameSender.MobMove</c>.</summary>
    public bool TryFlush(out byte[] blob, out short sn)
    {
        if (_pending.Count == 0)
        {
            blob = Array.Empty<byte>();
            sn   = _mobCtrlSn;
            return false;
        }
        blob = MovePathEncoder.Encode(
            (short)_lastSyncPos.X, (short)_lastSyncPos.Y,
            (short)_lastSyncVel.X, (short)_lastSyncVel.Y,
            _pending);
        _pending.Clear();
        sn = ++_mobCtrlSn;
        return true;
    }

    public bool FacingLeft => _facingLeft;

    // ── State transitions ────────────────────────────────────────────────────────

    private void EnterIdle()
    {
        _state      = State.Idle;
        _stateTimer = Lerp(IdlePauseMin, IdlePauseMax, (float)_rng.NextDouble());
        _velocity   = Vector2.Zero;
    }

    private void EnterWalk()
    {
        _state       = State.Walking;
        _stateTimer  = Lerp(WalkBurstMin, WalkBurstMax, (float)_rng.NextDouble());
        _facingLeft  = _rng.NextDouble() < 0.5;
        if (_info.IsFly)
        {
            PickFlyTarget();
        }
        else
        {
            _velocity = new Vector2((_facingLeft ? -1f : 1f) * WalkSpeed, 0f);
        }
    }

    // ── Movement primitives ──────────────────────────────────────────────────────

    private void StepWalk(float dt)
    {
        var dir = _facingLeft ? -1f : 1f;
        _velocity = new Vector2(dir * WalkSpeed, 0f);
        var nextX = _mob.Position.X + _velocity.X * dt;

        var fh = _currentFh != 0 ? _field.GetFoothold(_currentFh) : null;
        if (fh != null)
        {
            // Turn at the foothold edges (only when wandering — chase keeps trying to
            // close on the player even if it has to wall-walk for a beat).
            if (nextX < fh.LeftEdgeX + EdgeMargin)
            {
                nextX = fh.LeftEdgeX + EdgeMargin;
                if (_state != State.Chasing) _facingLeft = false;
            }
            else if (nextX > fh.RightEdgeX - EdgeMargin)
            {
                nextX = fh.RightEdgeX - EdgeMargin;
                if (_state != State.Chasing) _facingLeft = true;
            }
            var y = fh.YAt(nextX) ?? _mob.Position.Y;
            _mob.Position = new Vector2(nextX, y);
        }
        else
        {
            // No foothold (mob spawned mid-air?) — let it drift horizontally; FieldScene's
            // bounds clamp would be applied for fly, but a walking mob without a foothold
            // shouldn't occur on a well-formed map.
            _mob.Position = new Vector2(nextX, _mob.Position.Y);
        }
    }

    private void StepFly(float dt, Vector2 playerPos)
    {
        if (_state == State.Chasing)
        {
            _flyTarget = playerPos;
        }
        else
        {
            _flyTargetTimer -= dt;
            var toT = _flyTarget - _mob.Position;
            if (toT.LengthSquared() < 64f || _flyTargetTimer <= 0f) PickFlyTarget();
        }
        var d = _flyTarget - _mob.Position;
        if (d.LengthSquared() > 1f)
        {
            d.Normalize();
            _velocity = d * FlySpeed;
            if (!_info.NoFlip) _facingLeft = d.X < 0;
        }
        else
        {
            _velocity = Vector2.Zero;
        }
        _mob.Position += _velocity * dt;

        var b = _field.Bounds;
        _mob.Position = new Vector2(
            Math.Clamp(_mob.Position.X, b.Left + 8, b.Right - 8),
            Math.Clamp(_mob.Position.Y, b.Top  + 8, b.Bottom - 8));
    }

    private void SeatOnFoothold()
    {
        if (_info.IsFly || _currentFh == 0) return;
        var fh = _field.GetFoothold(_currentFh);
        var y  = fh?.YAt(_mob.Position.X);
        if (y.HasValue) _mob.Position = new Vector2(_mob.Position.X, y.Value);
    }

    private void PickFlyTarget()
    {
        var b = _field.Bounds;
        _flyTarget = new Vector2(
            b.Left + (float)(_rng.NextDouble() * b.Width),
            b.Top  + (float)(_rng.NextDouble() * b.Height));
        _flyTargetTimer = 2f + (float)(_rng.NextDouble() * 4f);
    }

    private float WalkSpeed => Math.Max(MinSpeed, BaseWalkSpeed * (1f + _info.Speed    / 100f));
    private float FlySpeed  => Math.Max(MinSpeed, BaseFlySpeed  * (1f + _info.FlySpeed / 100f));

    // ── Move-path sampling ───────────────────────────────────────────────────────

    private bool HasChanged()
        => Vector2.DistanceSquared(_mob.Position, _lastSyncPos) > 0.25f
        || Vector2.DistanceSquared(_velocity,     _lastSyncVel) > 0.25f;

    private void AppendElement()
    {
        var elapsedMs = (short)Math.Clamp(_flushTimer * 1000f, 1, 1000);
        _pending.Add(new MoveElement
        {
            // Attr 17 (FLYING_BLOCK) writes x/y/vx/vy — appropriate for fly mobs that
            // don't ride a foothold. Attr 0 (NORMAL) writes the full position+vel+foothold
            // tuple and is what walking mobs use (matches PlayerController.AppendNormal).
            Attr       = (byte)(_info.IsFly ? 17 : 0),
            X          = (short)_mob.Position.X,
            Y          = (short)_mob.Position.Y,
            Vx         = (short)_velocity.X,
            Vy         = (short)_velocity.Y,
            Fh         = (short)_currentFh,
            MoveAction = (byte)CurrentAction,
            Elapse     = elapsedMs,
        });
        _flushTimer  = 0f;
        _lastSyncPos = _mob.Position;
        _lastSyncVel = _velocity;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
