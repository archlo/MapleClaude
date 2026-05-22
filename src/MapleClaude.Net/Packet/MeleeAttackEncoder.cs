namespace MapleClaude.Net.Packet;

/// <summary>
/// One mob targeted by a melee swing. <see cref="Damage"/> holds the per-hit
/// damage values; its length must equal the encoder's <c>damagePerMob</c>.
/// </summary>
public sealed class MeleeTarget
{
    public required int MobId { get; init; }
    public short HitX { get; init; }
    public short HitY { get; init; }
    public short Delay { get; init; }
    public required int[] Damage { get; init; }
}

/// <summary>
/// Builds the <c>UserMeleeAttack(47)</c> client→server packet body.
/// Field order mirrors upstream Kinoko's
/// <c>kinoko/handler/user/AttackHandler.handlerUserMeleeAttack</c> +
/// <c>decodeMobAttackInfo</c> byte-for-byte.
///
/// The server hard-validates only <c>fieldKey</c> (it disposes the user on a
/// mismatch); the <c>dr*</c> scramble ints and the skill CRCs are read and
/// ignored (the skill CRC is soft-validated — a zero just logs a warning),
/// and the per-mob <c>damage[]</c> array is trusted and applied. So a basic
/// swing encodes every CRC/scramble field as 0 and supplies client-chosen
/// damage.
/// </summary>
public static class MeleeAttackEncoder
{
    /// <summary>
    /// Encode a basic (skillId 0) melee attack.
    /// </summary>
    /// <param name="fieldKey">The field key from SetField; must match server-side or we get disposed.</param>
    /// <param name="actionAndDir"><c>(attackAction &amp; 0x7FFF) | (bLeft &lt;&lt; 15)</c>.</param>
    /// <param name="attackSpeed">Swing speed (6 is a sensible default for a one-handed weapon).</param>
    /// <param name="userX">Player world X.</param>
    /// <param name="userY">Player world Y.</param>
    /// <param name="targets">Mobs hit by this swing (≤ 6). May be empty (a whiff).</param>
    /// <param name="damagePerMob">Number of damage entries per mob (1 for a basic swing).</param>
    public static byte[] Encode(
        byte fieldKey,
        short actionAndDir,
        byte attackSpeed,
        short userX,
        short userY,
        IReadOnlyList<MeleeTarget> targets,
        int damagePerMob = 1)
    {
        if (damagePerMob is < 1 or > 0xF)
        {
            throw new ArgumentOutOfRangeException(nameof(damagePerMob), "damagePerMob must be 1..15");
        }
        if (targets.Count > 0xF)
        {
            throw new ArgumentException("a single melee attack can target at most 15 mobs", nameof(targets));
        }

        var p = OutPacket.Of(InHeader.UserMeleeAttack);
        p.WriteByte(fieldKey);
        p.WriteInt(0);                                   // ~dr0
        p.WriteInt(0);                                   // ~dr1
        p.WriteByte((byte)((damagePerMob & 0xF) | ((targets.Count & 0xF) << 4))); // nDamagePerMob | (16 * nMobCount)
        p.WriteInt(0);                                   // ~dr2
        p.WriteInt(0);                                   // ~dr3
        p.WriteInt(0);                                   // nSkillID (0 = basic attack)
        p.WriteByte(0);                                  // nCombatOrders
        p.WriteInt(0);                                   // dwKey
        p.WriteInt(0);                                   // Crc32
        p.WriteInt(0);                                   // SKILLLEVELDATA::GetCrC (soft-validated)
        p.WriteInt(0);                                   // SKILLLEVELDATA::GetCrC (ignored)
        // (no keyDown int — basic attack is not a keydown skill)
        p.WriteByte(0);                                  // flag
        p.WriteShort(actionAndDir);                      // nAttackAction & 0x7FFF | bLeft << 15
        p.WriteInt(0);                                   // GETCRC32Svr
        p.WriteByte(0);                                  // nAttackActionType
        p.WriteByte(attackSpeed);                        // nAttackSpeed
        p.WriteInt(0);                                   // tAttackTime
        p.WriteInt(0);                                   // dwID

        foreach (var t in targets)
        {
            if (t.Damage.Length != damagePerMob)
            {
                throw new ArgumentException(
                    $"MeleeTarget {t.MobId} has {t.Damage.Length} damage values but damagePerMob is {damagePerMob}",
                    nameof(targets));
            }
            p.WriteInt(t.MobId);
            p.WriteByte(0);                              // nHitAction
            p.WriteByte(0);                              // nForeAction & 0x7F | (bLeft << 7)
            p.WriteByte(0);                              // nFrameIdx
            p.WriteByte(0);                              // CalcDamageStatIndex
            p.WriteShort(t.HitX);                        // ptHit.x
            p.WriteShort(t.HitY);                        // ptHit.y
            p.WriteShort(0);                             // padding
            p.WriteShort(0);                             // padding
            p.WriteShort(t.Delay);                       // tDelay
            foreach (var dmg in t.Damage)
            {
                p.WriteInt(dmg);
            }
            p.WriteInt(0);                               // CMob::GetCrc
        }

        p.WriteShort(userX);                             // GetPos()->x
        p.WriteShort(userY);                             // GetPos()->y

        var bytes = p.ToArray();
        p.Release();
        return bytes;
    }
}
