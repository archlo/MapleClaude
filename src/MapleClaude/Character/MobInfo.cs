namespace MapleClaude.Character;

/// <summary>
/// Client-side, AI-only template for one mob template id, sourced from
/// <c>Mob.wz/&lt;id:D7&gt;.img/info/*</c>. Only the fields the local mob controller actually
/// uses to drive movement / aggro live here — combat-relevant fields (HP, damage,
/// skills, exp, …) are kept on the server side and don't need a client mirror.
///
/// Kinoko's MobTemplate (upstream) intentionally does NOT parse moveAbility / speed /
/// flySpeed / fly / firstAttack / bodyAttack / noFlip / pushed — those are client-side
/// movement parameters. See plan: "the server does no mob AI; movement is entirely
/// controller-driven."
/// </summary>
public sealed class MobInfo
{
    public int  TemplateId  { get; init; }

    /// <summary>0 = STAY (mushroom-like, never moves), 1 = WALK, 2 = JUMP,
    /// 3 or 4 = FLY (the v95 client IDB shows <c>bFly = (v == 4)</c> in
    /// CMob::GenerateMovePath; both 3 and 4 are treated as fly to be safe).</summary>
    public int  MoveAbility { get; init; } = 1;

    /// <summary>Walk-speed % modifier (Mob.wz info/speed). Final walk speed =
    /// base × (1 + Speed/100), clamped to a sensible range.</summary>
    public int  Speed       { get; init; }

    /// <summary>Fly-speed % modifier (info/flySpeed, or info/fs as a fallback).</summary>
    public int  FlySpeed    { get; init; }

    /// <summary>Some flying mobs set info/fly even when moveAbility isn't 3/4.</summary>
    public bool Fly         { get; init; }

    /// <summary>info/firstAttack — aggressive mob: chases the nearest player on sight.</summary>
    public bool FirstAttack { get; init; }

    /// <summary>info/bodyAttack — touch-damage mob: contact with the player triggers
    /// the body-attack hit (see UserHit / Part 3).</summary>
    public bool BodyAttack  { get; init; }

    /// <summary>info/noFlip — mobs whose sprite isn't symmetric and shouldn't be flipped
    /// when facing left (e.g. directional bosses).</summary>
    public bool NoFlip      { get; init; }

    /// <summary>info/pushed — knockback resistance (higher = less knockback).</summary>
    public int  Pushed      { get; init; }

    public bool Boss        { get; init; }
    public int  Level       { get; init; } = 1;

    public bool IsStay => MoveAbility == 0;
    public bool IsFly  => MoveAbility >= 3 || Fly;
    public bool IsJump => MoveAbility == 2;
}
