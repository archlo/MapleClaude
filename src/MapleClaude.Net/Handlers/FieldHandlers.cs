using MapleClaude.Domain;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Net.Handlers;

/// <summary>
/// Decoders for the channel-server S→C opcodes we care about in Phase 2.
/// Raises typed events consumed by GameStage on the game thread.
/// </summary>
public sealed class FieldHandlers
{
    private readonly ILogger<FieldHandlers> _logger;

    // ── SetField / heartbeat ──────────────────────────────────────────────────
    public event Action<SetFieldArgs>?          OnSetField;

    // ── Stats ─────────────────────────────────────────────────────────────────
    public event Action<StatChangedArgs>?       OnStatChanged;

    // ── Mobs ─────────────────────────────────────────────────────────────────
    public event Action<MobEnterArgs>?          OnMobEnter;
    public event Action<int>?                   OnMobLeave;    // mobId
    public event Action<MobMoveArgs>?           OnMobMove;
    public event Action<MobDamagedArgs>?        OnMobDamaged;
    /// <summary>int mobId, bool isControl (true = client must send MobMove for this mob).</summary>
    public event Action<int, bool>?             OnMobChangeController;

    // ── NPCs ─────────────────────────────────────────────────────────────────
    public event Action<NpcEnterArgs>?          OnNpcEnter;
    public event Action<int>?                   OnNpcLeave;    // npcObjId

    // ── Other players ─────────────────────────────────────────────────────────
    public event Action<OtherCharEnterArgs>?    OnUserEnter;
    public event Action<int>?                   OnUserLeave;   // charId
    public event Action<OtherCharMoveArgs>?     OnUserMove;

    // ── Drops ─────────────────────────────────────────────────────────────────
    public event Action<DropEnterArgs>?         OnDropEnter;
    public event Action<DropLeaveArgs>?         OnDropLeave;

    // ── Inventory ─────────────────────────────────────────────────────────────
    public event Action<List<InventoryOpArg>>?  OnInventoryOperation;

    // ── Chat / broadcast ──────────────────────────────────────────────────────
    public event Action<UserChatArgs>?          OnUserChat;

    // ── NPC script ────────────────────────────────────────────────────────────
    public event Action<ScriptMessageArgs>?     OnScriptMessage;

    // ── Key bindings ──────────────────────────────────────────────────────────
    public event Action<List<FuncKeyEntry>>?    OnFuncKeyMappedInit;

    // ── Foothold ──────────────────────────────────────────────────────────────
    public event Action<List<FootholdEntry>>?   OnFootHoldInfo;

    public FieldHandlers(ILogger<FieldHandlers> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Clears all subscriber lambdas except <see cref="OnSetField"/> (which
    /// uses a named-method pattern and is managed by the caller).
    /// Call from <c>GameStage.OnExit</c> to prevent stale references.
    /// </summary>
    public void ClearAllExceptSetField()
    {
        OnStatChanged         = null;
        OnMobEnter            = null;
        OnMobLeave            = null;
        OnMobMove             = null;
        OnMobDamaged          = null;
        OnMobChangeController = null;
        OnNpcEnter            = null;
        OnNpcLeave            = null;
        OnUserEnter           = null;
        OnUserLeave           = null;
        OnUserMove            = null;
        OnDropEnter           = null;
        OnDropLeave           = null;
        OnInventoryOperation  = null;
        OnUserChat            = null;
        OnScriptMessage       = null;
        OnFuncKeyMappedInit   = null;
        OnFootHoldInfo        = null;
    }

    public void Register(PacketRouter router)
    {
        router.Register(OutHeader.SetField,            (p, s) => HandleSetField(p, s));
        router.Register(OutHeader.AliveReq,            (p, s) => HandleAliveReq(s));
        router.Register(OutHeader.StatChanged,         (p, s) => HandleStatChanged(p));
        router.Register(OutHeader.MobEnterField,        (p, s) => HandleMobEnter(p));
        router.Register(OutHeader.MobLeaveField,        (p, s) => HandleMobLeave(p));
        router.Register(OutHeader.MobChangeController,  (p, s) => HandleMobChangeController(p, s));
        router.Register(OutHeader.MobMove,              (p, s) => HandleMobMove(p));
        router.Register(OutHeader.MobDamaged,           (p, s) => HandleMobDamaged(p));
        router.Register(OutHeader.NpcEnterField,       (p, s) => HandleNpcEnter(p));
        router.Register(OutHeader.NpcLeaveField,       (p, s) => HandleNpcLeave(p));
        router.Register(OutHeader.UserEnterField,      (p, s) => HandleUserEnter(p));
        router.Register(OutHeader.UserLeaveField,      (p, s) => HandleUserLeave(p));
        router.Register(OutHeader.UserMove,            (p, s) => HandleUserMove(p));
        router.Register(OutHeader.DropEnterField,      (p, s) => HandleDropEnter(p));
        router.Register(OutHeader.DropLeaveField,      (p, s) => HandleDropLeave(p));
        router.Register(OutHeader.InventoryOperation,  (p, s) => HandleInventoryOp(p));
        router.Register(OutHeader.UserChat,            (p, s) => HandleUserChat(p));
        router.Register(OutHeader.ScriptMessage,       (p, s) => HandleScriptMessage(p));
        router.Register(OutHeader.FuncKeyMappedInit,   (p, s) => HandleFuncKeyMappedInit(p));
        router.Register(OutHeader.FootHoldInfo,        (p, s) => HandleFootHoldInfo(p));
    }

    // ── SetField ──────────────────────────────────────────────────────────────

    private void HandleSetField(InPacket p, ClientSession session)
    {
        _logger.LogInformation("SetField received");
        p.ReadShort();              // CClientOptMan::DecodeOpt
        var channelId    = p.ReadInt();
        var oldDriverId  = p.ReadInt();
        var fieldKey     = p.ReadByte();
        var isMigrate    = p.ReadByte() != 0;
        p.ReadShort();              // nNotifierCheck

        var args = new SetFieldArgs { ChannelId = channelId, FieldKey = fieldKey, IsMigrate = isMigrate };

        if (isMigrate)
        {
            args.CalcDamageSeed1 = p.ReadInt();
            args.CalcDamageSeed2 = p.ReadInt();
            args.CalcDamageSeed3 = p.ReadInt();
            try
            {
                args.DwFlag = p.ReadLong();
                p.ReadByte();   // nCombatOrders
                p.ReadByte();   // sLinkedCharacter false
                args.Stat = AvatarCodec.DecodeCharacterStat(p);
                args.Look = new AvatarLook
                {
                    Gender = args.Stat.Gender,
                    Skin   = args.Stat.Skin,
                    Face   = args.Stat.Face,
                    Hair   = args.Stat.Hair,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SetField partial decode");
            }
        }
        else
        {
            p.ReadByte();       // isRevive
            args.PosMap = p.ReadInt();
            args.Portal = p.ReadByte();
            p.ReadInt();        // hp
            p.ReadByte();       // bChaseEnable
        }
        OnSetField?.Invoke(args);
    }

    private void HandleAliveReq(ClientSession session)
    {
        var ack = OutPacket.Of(InHeader.AliveAck);
        session.Send(ack);
    }

    // ── StatChanged ───────────────────────────────────────────────────────────
    // Kinoko: WvsContext.statChanged — bool exclRequest + long flag + per-stat fields

    private void HandleStatChanged(InPacket p)
    {
        p.ReadByte();   // exclRequest
        var mask = p.ReadLong();
        var args = new StatChangedArgs { Mask = mask };

        if ((mask & 0x01)   != 0) args.Skin    = p.ReadByte();
        if ((mask & 0x02)   != 0) args.Face    = p.ReadInt();
        if ((mask & 0x04)   != 0) args.Hair    = p.ReadInt();
        if ((mask & 0x10)   != 0) args.Level   = p.ReadByte();
        if ((mask & 0x20)   != 0) args.Job     = p.ReadShort();
        if ((mask & 0x40)   != 0) args.Str     = p.ReadShort();
        if ((mask & 0x80)   != 0) args.Dex     = p.ReadShort();
        if ((mask & 0x100)  != 0) args.Int     = p.ReadShort();
        if ((mask & 0x200)  != 0) args.Luk     = p.ReadShort();
        if ((mask & 0x400)  != 0) args.Hp      = p.ReadInt();
        if ((mask & 0x800)  != 0) args.MaxHp   = p.ReadInt();
        if ((mask & 0x1000) != 0) args.Mp      = p.ReadInt();
        if ((mask & 0x2000) != 0) args.MaxMp   = p.ReadInt();
        if ((mask & 0x4000) != 0) args.Ap      = p.ReadShort();
        if ((mask & 0x8000) != 0) args.Sp      = p.ReadShort();
        if ((mask & 0x10000)!= 0) args.Exp     = p.ReadInt();
        if ((mask & 0x20000)!= 0) args.Pop     = p.ReadShort();
        if ((mask & 0x200000)!=0) args.Meso    = p.ReadInt();

        p.ReadByte();   // bExclRequest trailing
        OnStatChanged?.Invoke(args);
    }

    // ── Mobs ─────────────────────────────────────────────────────────────────
    // MobEnterField: int dwMobID, byte nCalcDamageIndex, int dwTemplateID,
    //   TemporaryStatSet (skip), Mob.encode (skip most) → short x, short y, byte action, short foothold

    private void HandleMobEnter(InPacket p)
    {
        // Mirrors upstream MobPacket.mobEnterField:
        //   int mobId, byte calcDamageIndex, int templateId,
        //   MobStat.encodeTemporary(...), Mob.encode(...)
        var mobId      = p.ReadInt();
        var calcDmgIdx = p.ReadByte();
        var templateId = p.ReadInt();
        _ = calcDmgIdx;
        try
        {
            // MobStat.encodeTemporary for a fresh (un-buffed) mob writes only the
            // temporary-stat BitFlag: MobTemporaryStat.FLAG_SIZE = 128 bits = 4
            // ints = 16 bytes, all zero. A buffed mob would append per-stat option
            // blocks after the flag — NOT handled here; those would shift the
            // position read. Acceptable for Phase 4 (fresh field spawns); revisit
            // when mob buffs land.
            p.Skip(16);     // temporary-stat flag (4 ints, zero for fresh spawn)

            // Mob.encode (CMob::Init):
            var x          = p.ReadShort();   // ptPosPrev.x
            var y          = p.ReadShort();   // ptPosPrev.y
            p.ReadByte();                     // nMoveAction
            var foothold   = p.ReadShort();   // current foothold
            p.ReadShort();                    // start foothold
            var summonType = p.ReadSByte();   // nAppearType
            if (summonType == -3 /* REVIVED */ || summonType >= 0)
            {
                p.ReadInt();                  // dwOption
            }
            p.ReadByte();                     // nTeamForMCarnival
            p.ReadInt();                      // nEffectItemID
            p.ReadInt();                      // nPhase
            OnMobEnter?.Invoke(new MobEnterArgs
            {
                MobId = mobId, TemplateId = templateId,
                X = x, Y = y, FhId = foothold,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MobEnterField partial decode mobId={Id} tmpl={T}", mobId, templateId);
            OnMobEnter?.Invoke(new MobEnterArgs { MobId = mobId, TemplateId = templateId });
        }
    }

    private void HandleMobLeave(InPacket p)
    {
        var mobId    = p.ReadInt();
        var leaveType = p.ReadByte();   // 0=fade,1=remove,2=die
        OnMobLeave?.Invoke(mobId);
    }

    private void HandleMobChangeController(InPacket p, ClientSession session)
    {
        // MobChangeController: byte nCalcDamageIndex, int dwMobID
        // nCalcDamageIndex 0 = release control, 1|2 = take control
        var calcDmgIndex = p.ReadByte();
        var mobId        = p.ReadInt();
        var isCtrl       = calcDmgIndex != 0;
        _logger.LogDebug("MobChangeController: mobId={Id} ctrl={Ctrl}", mobId, isCtrl);
        OnMobChangeController?.Invoke(mobId, isCtrl);
        // If we're given control, ack with MobApplyCtrl (opcode 228).
        // Per kinoko/handler/field/MobHandler.handleMobApplyCtrl: the server
        // reads `int dwMobID, int crc` — NOT a byte. Send 0 for crc; the
        // server only re-checks the controller distance against this packet,
        // not the crc value.
        if (isCtrl)
        {
            var ack = OutPacket.Of((short)InHeader.MobApplyCtrl);
            ack.WriteInt(mobId);
            ack.WriteInt(0);    // dwCliCrc (server reads but doesn't validate)
            session.Send(ack);
        }
    }

    private void HandleMobMove(InPacket p)
    {
        var mobId = p.ReadInt();
        p.ReadByte();   // bNotForceLandingWhenDiscard
        p.ReadByte();   // bNotChangeAction
        var actionMask   = p.ReadByte();
        var actionAndDir = p.ReadByte();
        p.ReadInt();    // targetInfo
        var multiCount   = p.ReadInt();
        for (var i = 0; i < multiCount; i++) { p.ReadInt(); p.ReadInt(); }
        var randCount    = p.ReadInt();
        for (var i = 0; i < randCount; i++) p.ReadInt();
        // MovePath: short x, short y follow as first two encoded values
        try
        {
            var x = p.ReadShort();
            var y = p.ReadShort();
            OnMobMove?.Invoke(new MobMoveArgs { MobId = mobId, X = x, Y = y });
        }
        catch { /* MovePath incomplete */ }
    }

    // ── Mob damaged ───────────────────────────────────────────────────────────
    // MobDamaged: int dwMobId, byte showDamage, int nDamage, [int hp, int maxHp]

    private void HandleMobDamaged(InPacket p)
    {
        var mobId  = p.ReadInt();
        var flag   = p.ReadByte();   // 0=normal, others=special
        var damage = p.ReadInt();
        int hp = -1, maxHp = -1;
        try { hp = p.ReadInt(); maxHp = p.ReadInt(); } catch { }
        OnMobDamaged?.Invoke(new MobDamagedArgs { MobId = mobId, Damage = damage, Hp = hp, MaxHp = maxHp });
    }

    // ── NPCs ─────────────────────────────────────────────────────────────────
    // NpcEnterField: int dwNpcID, int dwTemplateID, Npc.encode → short x, short y, bool bLeft, short foothold

    private void HandleNpcEnter(InPacket p)
    {
        var objId      = p.ReadInt();
        var templateId = p.ReadInt();
        try
        {
            var x        = p.ReadShort();
            var y        = p.ReadShort();
            var facingLeft = p.ReadBool();
            var foothold = p.ReadShort();
            p.ReadShort(); // rx0
            p.ReadShort(); // rx1
            OnNpcEnter?.Invoke(new NpcEnterArgs
            {
                ObjId = objId, TemplateId = templateId,
                X = x, Y = y, FacingLeft = facingLeft,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NpcEnterField partial decode npcId={Id}", objId);
            OnNpcEnter?.Invoke(new NpcEnterArgs { ObjId = objId, TemplateId = templateId });
        }
    }

    private void HandleNpcLeave(InPacket p)
    {
        OnNpcLeave?.Invoke(p.ReadInt());
    }

    // ── Other players ─────────────────────────────────────────────────────────
    // UserEnterField: int charId, byte level, string name, guild info, stat, look, position…

    private void HandleUserEnter(InPacket p)
    {
        var charId = p.ReadInt();
        var level  = p.ReadByte();
        var name   = p.ReadString(13);
        // Guild info (string guild, short markBg, byte bgColor, short mark, byte color)
        try
        {
            var guild = p.ReadString(12);
            p.ReadShort(); p.ReadByte(); p.ReadShort(); p.ReadByte(); // guild mark
            // Skip rest of remote char init (secondary stat, look, equips…)
            // Jump to position which is near the end of the fixed block
            // We read until x,y which are the last reliable shorts before movement data
            // This is approximate — full decode needs AvatarCodec
            var look = AvatarCodec.DecodeAvatarLook(p);
            p.ReadInt();   // dwDriverId
            p.ReadInt();   // dwPassengerId
            p.ReadInt();   // nChocoBuff
            p.ReadInt();   // nActiveEffectItemId
            p.ReadInt();   // nCompletedSetItemID
            p.ReadInt();   // nPortableChairId
            var x = p.ReadShort();
            var y = p.ReadShort();
            OnUserEnter?.Invoke(new OtherCharEnterArgs
            {
                CharId = charId, Level = level, Name = name,
                Look = look, X = x, Y = y,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UserEnterField partial decode charId={Id}", charId);
            OnUserEnter?.Invoke(new OtherCharEnterArgs { CharId = charId, Name = name, Level = level });
        }
    }

    private void HandleUserLeave(InPacket p) => OnUserLeave?.Invoke(p.ReadInt());

    private void HandleUserMove(InPacket p)
    {
        var charId = p.ReadInt();
        // MovePath starts immediately after
        try
        {
            var x = p.ReadShort();
            var y = p.ReadShort();
            OnUserMove?.Invoke(new OtherCharMoveArgs { CharId = charId, X = x, Y = y });
        }
        catch { }
    }

    // ── Drops ─────────────────────────────────────────────────────────────────
    // DropEnterField: byte enterType, int dropId, byte isMoney, int info(itemId/amount),
    //   int ownerId, byte ownType, short x, short y, …

    private void HandleDropEnter(InPacket p)
    {
        var enterType = p.ReadByte();
        var dropId    = p.ReadInt();
        var isMoney   = p.ReadBool();
        var info      = p.ReadInt();    // itemId or meso amount
        var ownerId   = p.ReadInt();
        p.ReadByte();    // ownType
        var x = p.ReadShort();
        var y = p.ReadShort();
        OnDropEnter?.Invoke(new DropEnterArgs
        {
            DropId = dropId, IsMoney = isMoney,
            ItemIdOrAmount = info, X = x, Y = y,
        });
    }

    private void HandleDropLeave(InPacket p)
    {
        var leaveType = p.ReadByte();
        var dropId    = p.ReadInt();
        OnDropLeave?.Invoke(new DropLeaveArgs { DropId = dropId, LeaveType = leaveType });
    }

    // ── Inventory ─────────────────────────────────────────────────────────────
    // WvsContext.inventoryOperation:
    //   byte exclRequest, byte opCount, [opType(byte) invType(byte) pos(short) + type-specific], byte 0

    private void HandleInventoryOp(InPacket p)
    {
        p.ReadByte();    // exclRequest
        var count = p.ReadByte();
        var ops   = new List<InventoryOpArg>(count);
        for (var i = 0; i < count; i++)
        {
            var opType  = p.ReadByte();   // 0=New 1=Qty 2=Move 3=Del 4=Exp
            var invType = p.ReadByte();   // 0=Equip 1=Consume 2=Install 3=Etc 4=Cash
            var pos     = p.ReadShort();
            var op      = new InventoryOpArg { OpType = opType, InvType = invType, Pos = pos };
            switch (opType)
            {
                case 0: // NewItem — read item (minimal: int itemId, byte isCash, long expiry, …)
                    op.ItemId = p.ReadInt();
                    // Skip item detail (too complex to decode fully without item schema)
                    // Just log and continue
                    break;
                case 1: // ItemNumber — short quantity
                    op.Quantity = p.ReadShort();
                    break;
                case 2: // Position — short newPos
                    op.NewPos = p.ReadShort();
                    break;
                case 3: // DelItem — nothing extra
                    break;
                case 4: // EXP — int exp
                    p.ReadInt();
                    break;
            }
            ops.Add(op);
        }
        p.ReadByte();   // bSN trailing zero
        OnInventoryOperation?.Invoke(ops);
    }

    // ── Chat ─────────────────────────────────────────────────────────────────
    // UserChat: int charId, bool isAdmin, string text, bool showBalloon

    private void HandleUserChat(InPacket p)
    {
        var charId  = p.ReadInt();
        p.ReadBool();   // isAdmin
        var text    = p.ReadString();
        p.ReadBool();   // showBalloon
        OnUserChat?.Invoke(new UserChatArgs { CharId = charId, Text = text });
    }

    // ── NPC Script ────────────────────────────────────────────────────────────
    // Mirrors upstream ScriptMessage.encode (kinoko/script/common/ScriptMessage.java):
    //   byte speakerType, int speakerId, byte msgType, byte messageParam,
    //   then a per-type body. For SAY the prev/next bytes come AFTER the text;
    //   ASK* types have no prev/next.

    private const int ScriptParamSpeakerOnRight = 0x4; // ScriptMessageParam.SPEAKER_ON_RIGHT

    private void HandleScriptMessage(InPacket p)
    {
        p.ReadByte();                                   // nSpeakerTypeID (unused)
        var speakerId    = p.ReadInt();
        var msgType      = ScriptMessageTypeExtensions.FromValue(p.ReadByte());
        var messageParam = p.ReadByte();                // bParam
        var args = new ScriptMessageArgs
        {
            SpeakerId    = speakerId,
            MsgType      = msgType,
            MessageParam = messageParam,
        };
        try
        {
            switch (msgType)
            {
                case ScriptMessageType.Say:
                    if ((messageParam & ScriptParamSpeakerOnRight) != 0)
                    {
                        p.ReadInt();                    // nSpeakerTemplateID (repeated)
                    }
                    args.Text    = p.ReadString();
                    args.HasPrev = p.ReadBool();
                    args.HasNext = p.ReadBool();
                    break;

                case ScriptMessageType.AskYesNo:
                case ScriptMessageType.AskAccept:
                case ScriptMessageType.AskMenu:
                    args.Text = p.ReadString();
                    break;

                case ScriptMessageType.AskText:
                    args.Text        = p.ReadString();
                    args.DefaultText = p.ReadString();
                    args.MinLength   = p.ReadShort();
                    args.MaxLength   = p.ReadShort();
                    break;

                case ScriptMessageType.AskBoxText:
                    args.Text        = p.ReadString();
                    args.DefaultText = p.ReadString();
                    args.MinLength   = p.ReadShort();   // columns
                    args.MaxLength   = p.ReadShort();   // lines
                    break;

                case ScriptMessageType.AskNumber:
                    args.Text       = p.ReadString();
                    args.DefaultNum = p.ReadInt();
                    args.MinNum     = p.ReadInt();
                    args.MaxNum     = p.ReadInt();
                    break;

                default:
                    // SAYIMAGE / ASKAVATAR / ASKSLIDEMENU etc. — rare in low-level
                    // content; decode the leading text best-effort so the dialog
                    // still shows something.
                    args.Text = p.ReadString();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScriptMessage trailing decode msgType={T}", msgType);
        }
        OnScriptMessage?.Invoke(args);
    }

    // ── FuncKeyMappedInit ─────────────────────────────────────────────────────
    // 90 entries: byte type + int actionId

    private void HandleFuncKeyMappedInit(InPacket p)
    {
        var entries = new List<FuncKeyEntry>(90);
        for (var i = 0; i < 90; i++)
        {
            var type     = p.ReadByte();
            var actionId = p.ReadInt();
            entries.Add(new FuncKeyEntry { KeyIndex = i, Type = type, ActionId = actionId });
        }
        OnFuncKeyMappedInit?.Invoke(entries);
    }

    // ── FootHoldInfo ──────────────────────────────────────────────────────────

    private void HandleFootHoldInfo(InPacket p)
    {
        var count = p.ReadInt();
        var footholds = new List<FootholdEntry>(count);
        for (var i = 0; i < count; i++)
        {
            footholds.Add(new FootholdEntry
            {
                Id   = p.ReadShort(),
                X1   = p.ReadShort(),
                Y1   = p.ReadShort(),
                X2   = p.ReadShort(),
                Y2   = p.ReadShort(),
                Prev = p.ReadShort(),
                Next = p.ReadShort(),
            });
        }
        OnFootHoldInfo?.Invoke(footholds);
    }
}

// ── Argument types ────────────────────────────────────────────────────────────

public sealed class SetFieldArgs
{
    public int ChannelId { get; init; }
    public byte FieldKey { get; init; }
    public bool IsMigrate { get; init; }
    public int CalcDamageSeed1 { get; set; }
    public int CalcDamageSeed2 { get; set; }
    public int CalcDamageSeed3 { get; set; }
    public long DwFlag { get; set; }
    public CharacterStat? Stat { get; set; }
    public AvatarLook? Look { get; set; }
    public int PosMap { get; set; }
    public byte Portal { get; set; }
}

public sealed class StatChangedArgs
{
    public long Mask { get; set; }
    public byte   Skin;
    public int    Face, Hair;
    public byte   Level;
    public short  Job, Str, Dex, Int, Luk;
    public int    Hp, MaxHp, Mp, MaxMp;
    public short  Ap, Sp;
    public int    Exp, Meso;
    public short  Pop;
}

public sealed class MobEnterArgs
{
    public int   MobId, TemplateId;
    public short X, Y, FhId;
}

public sealed class MobMoveArgs  { public int MobId; public short X, Y; }
public sealed class MobDamagedArgs { public int MobId, Damage, Hp, MaxHp; }

public sealed class NpcEnterArgs
{
    public int   ObjId, TemplateId;
    public short X, Y;
    public bool  FacingLeft;
}

public sealed class OtherCharEnterArgs
{
    public int       CharId;
    public byte      Level;
    public string    Name = string.Empty;
    public AvatarLook? Look;
    public short     X, Y;
}

public sealed class OtherCharMoveArgs { public int CharId; public short X, Y; }

public sealed class DropEnterArgs
{
    public int   DropId;
    public bool  IsMoney;
    public int   ItemIdOrAmount;
    public short X, Y;
}

public sealed class DropLeaveArgs { public int DropId; public byte LeaveType; }

public sealed class InventoryOpArg
{
    public byte  OpType;   // 0=New 1=Qty 2=Move 3=Del 4=Exp
    public byte  InvType;  // 0=Equip 1=Consume 2=Install 3=Etc 4=Cash
    public short Pos;
    public int   ItemId;
    public short Quantity;
    public short NewPos;
}

public sealed class UserChatArgs   { public int CharId; public string Text = string.Empty; }

public sealed class ScriptMessageArgs
{
    public int                SpeakerId;
    public ScriptMessageType  MsgType;
    public byte               MessageParam;
    public string             Text        = string.Empty;
    // SAY (0)
    public bool   HasPrev, HasNext;
    // ASKTEXT (3) / ASKBOXTEXT (14)
    public string DefaultText = string.Empty;
    public int    MinLength, MaxLength;
    // ASKNUMBER (4)
    public int    DefaultNum, MinNum, MaxNum;
}

public sealed class FuncKeyEntry   { public int KeyIndex, ActionId; public byte Type; }
public sealed class FootholdEntry  { public short Id, X1, Y1, X2, Y2, Prev, Next; }
