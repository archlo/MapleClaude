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

    // ── Migration (in-game channel transfer / cash-shop return) ────────────────
    /// <summary>Server directed us to a new channel endpoint (MigrateCommand 16):
    /// (channelHost[4], channelPort). The client reconnects and re-sends MigrateIn.</summary>
    public event Action<byte[], ushort>?        OnMigrateCommand;

    // ── Stats ─────────────────────────────────────────────────────────────────
    public event Action<StatChangedArgs>?       OnStatChanged;

    // ── Mobs ─────────────────────────────────────────────────────────────────
    public event Action<MobEnterArgs>?          OnMobEnter;
    public event Action<int>?                   OnMobLeave;    // mobId
    public event Action<MobMoveArgs>?           OnMobMove;
    public event Action<MobDamagedArgs>?        OnMobDamaged;
    /// <summary>int mobId, bool isControl (true = client must send MobMove for this mob).</summary>
    public event Action<int, bool>?             OnMobChangeController;
    /// <summary>Server reply to a MobMove the controller sent: echoes mobCtrlSn + the mob's
    /// current MP + a suggested next-skill. Per the v95 IDB the ack is informational —
    /// the client doesn't gate further moves on it.</summary>
    public event Action<MobCtrlAckArgs>?        OnMobCtrlAck;
    /// <summary>Server pushes the mob's current HP as a percentage (0-100) right after the
    /// controller's attack lands. Sent ONLY to the attacker (server takes a shortcut and
    /// skips the MobDamaged broadcast back to the hitter); drives the HP bar above hit
    /// mobs. Per kinoko/packet/field/MobPacket.mobHpIndicator.</summary>
    public event Action<int, byte>?             OnMobHpIndicator;

    // ── NPCs ─────────────────────────────────────────────────────────────────
    public event Action<NpcEnterArgs>?          OnNpcEnter;
    public event Action<int>?                   OnNpcLeave;    // npcObjId

    // ── Other players ─────────────────────────────────────────────────────────
    public event Action<OtherCharEnterArgs>?    OnUserEnter;
    public event Action<int>?                   OnUserLeave;   // charId
    public event Action<OtherCharMoveArgs>?     OnUserMove;
    /// <summary>A nearby player (CharId &gt; 0) or the local player (CharId == 0 sentinel for
    /// the UserEmotionLocal echo) started a face expression. Duration may be -1 (use the
    /// face's own WZ frame-delay total).</summary>
    public event Action<UserEmotionArgs>?       OnUserEmotion;

    // ── Character profile (double-click another player) ───────────────────────
    public event Action<CharacterInfoArgs>?     OnCharacterInfo;

    // ── Drops ─────────────────────────────────────────────────────────────────
    public event Action<DropEnterArgs>?         OnDropEnter;
    public event Action<DropLeaveArgs>?         OnDropLeave;

    // ── Loot / EXP / meso popups (Message 38) ──────────────────────────────────
    /// <summary>EXP gained (IncEXP message) — the raw exp delta.</summary>
    public event Action<int>?                   OnIncExp;
    /// <summary>Meso gained from a quest / script reward (IncMoney message).</summary>
    public event Action<int>?                   OnIncMoney;
    /// <summary>A drop pick-up message (meso, item bundle, or a warning).</summary>
    public event Action<LootMessageArgs>?       OnLootMessage;

    // ── Inventory ─────────────────────────────────────────────────────────────
    public event Action<List<InventoryOpArg>>?  OnInventoryOperation;

    // ── Chat / broadcast ──────────────────────────────────────────────────────
    public event Action<UserChatArgs>?          OnUserChat;
    /// <summary>Party / buddy / guild group chat: (groupType, fromName, text).
    /// groupType 0=Friend/Buddy 1=Party 2=Guild 3=Alliance.</summary>
    public event Action<int, string, string>?   OnGroupMessage;
    /// <summary>An incoming whisper was received: (fromName, channelId, text).</summary>
    public event Action<string, int, string>?   OnWhisper;

    // ── Social: party / friends ────────────────────────────────────────────────
    /// <summary>A party invite arrived: (inviterId, inviterName).</summary>
    public event Action<int, string>?           OnPartyInvite;
    /// <summary>Party roster (re)loaded: (members, partyBossId). Empty members
    /// list means the party disbanded / the player left.</summary>
    public event Action<List<PartyMember>, int>? OnPartyLoad;
    /// <summary>Friend list (re)loaded.</summary>
    public event Action<List<FriendInfo>>?      OnFriendList;
    /// <summary>Guild roster (re)loaded; null = not in a guild.</summary>
    public event Action<GuildLoadArgs?>?        OnGuildLoad;

    // ── NPC script ────────────────────────────────────────────────────────────
    public event Action<ScriptMessageArgs>?     OnScriptMessage;

    // ── NPC shop ────────────────────────────────────────────────────────────────
    public event Action<ShopOpenArgs>?          OnShopOpen;
    public event Action<ShopResultArgs>?        OnShopResult;

    // ── Player storage / trunk ──────────────────────────────────────────────────
    public event Action<TrunkResultArgs>?       OnTrunkResult;

    // ── Maple Messenger ─────────────────────────────────────────────────────────
    public event Action<MessengerResultArgs>?   OnMessengerResult;

    // ── Quests ────────────────────────────────────────────────────────────────────
    /// <summary>A quest record changed (Message QuestRecord / QuestRecordEx).</summary>
    public event Action<QuestRecordArgs>?       OnQuestRecord;

    // ── Skills / buffs ─────────────────────────────────────────────────────────
    public event Action<List<SkillRecord>>?     OnSkillRecordResult;
    /// <summary>A buff (temporary-stat) set occurred — full per-stat decode is
    /// deferred, so this just signals "something was applied".</summary>
    public event Action?                        OnTemporaryStatSet;
    /// <summary>One or more buffs expired (the reset flag is not yet mapped to
    /// individual skills).</summary>
    public event Action?                        OnTemporaryStatReset;

    // ── Key bindings ──────────────────────────────────────────────────────────
    public event Action<List<FuncKeyEntry>>?    OnFuncKeyMappedInit;
    public event Action<int[]>?                 OnQuickslotInit;

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
        OnMigrateCommand      = null;
        OnStatChanged         = null;
        OnMobEnter            = null;
        OnMobLeave            = null;
        OnMobMove             = null;
        OnMobDamaged          = null;
        OnMobChangeController = null;
        OnMobCtrlAck          = null;
        OnMobHpIndicator      = null;
        OnNpcEnter            = null;
        OnNpcLeave            = null;
        OnUserEnter           = null;
        OnUserLeave           = null;
        OnUserMove            = null;
        OnUserEmotion         = null;
        OnCharacterInfo       = null;
        OnDropEnter           = null;
        OnDropLeave           = null;
        OnIncExp              = null;
        OnIncMoney            = null;
        OnLootMessage         = null;
        OnInventoryOperation  = null;
        OnUserChat            = null;
        OnGroupMessage        = null;
        OnWhisper             = null;
        OnPartyInvite         = null;
        OnPartyLoad           = null;
        OnFriendList          = null;
        OnGuildLoad           = null;
        OnScriptMessage       = null;
        OnShopOpen            = null;
        OnShopResult          = null;
        OnTrunkResult         = null;
        OnMessengerResult     = null;
        OnQuestRecord         = null;
        OnSkillRecordResult   = null;
        OnTemporaryStatSet    = null;
        OnTemporaryStatReset  = null;
        OnFuncKeyMappedInit   = null;
        OnQuickslotInit       = null;
        OnFootHoldInfo        = null;
    }

    public void Register(PacketRouter router)
    {
        router.Register(OutHeader.SetField,            (p, s) => HandleSetField(p, s));
        router.Register(OutHeader.MigrateCommand,      (p, s) => HandleMigrateCommand(p));
        router.Register(OutHeader.AliveReq,            (p, s) => HandleAliveReq(s));
        router.Register(OutHeader.StatChanged,         (p, s) => HandleStatChanged(p));
        router.Register(OutHeader.MobEnterField,        (p, s) => HandleMobEnter(p));
        router.Register(OutHeader.MobLeaveField,        (p, s) => HandleMobLeave(p));
        router.Register(OutHeader.MobChangeController,  (p, _) => HandleMobChangeController(p));
        router.Register(OutHeader.MobMove,              (p, s) => HandleMobMove(p));
        router.Register(OutHeader.MobDamaged,           (p, s) => HandleMobDamaged(p));
        router.Register(OutHeader.MobCtrlAck,           (p, _) => HandleMobCtrlAck(p));
        router.Register(OutHeader.MobHPIndicator,       (p, _) => HandleMobHpIndicator(p));
        router.Register(OutHeader.NpcEnterField,       (p, s) => HandleNpcEnter(p));
        router.Register(OutHeader.NpcLeaveField,       (p, s) => HandleNpcLeave(p));
        router.Register(OutHeader.UserEnterField,      (p, s) => HandleUserEnter(p));
        router.Register(OutHeader.UserLeaveField,      (p, s) => HandleUserLeave(p));
        router.Register(OutHeader.UserMove,            (p, s) => HandleUserMove(p));
        router.Register(OutHeader.UserEmotion,         (p, s) => HandleUserEmotion(p));
        router.Register(OutHeader.UserEmotionLocal,    (p, s) => HandleUserEmotionLocal(p));
        router.Register(OutHeader.CharacterInfo,       (p, s) => HandleCharacterInfo(p));
        router.Register(OutHeader.DropEnterField,      (p, s) => HandleDropEnter(p));
        router.Register(OutHeader.DropLeaveField,      (p, s) => HandleDropLeave(p));
        router.Register(OutHeader.Message,             (p, s) => HandleMessage(p));
        router.Register(OutHeader.InventoryOperation,  (p, s) => HandleInventoryOp(p));
        router.Register(OutHeader.UserChat,            (p, s) => HandleUserChat(p));
        router.Register(OutHeader.GroupMessage,        (p, s) => HandleGroupMessage(p));
        router.Register(OutHeader.Whisper,             (p, s) => HandleWhisper(p));
        router.Register(OutHeader.PartyResult,         (p, s) => HandlePartyResult(p));
        router.Register(OutHeader.FriendResult,        (p, s) => HandleFriendResult(p));
        router.Register(OutHeader.GuildResult,         (p, s) => HandleGuildResult(p));
        router.Register(OutHeader.ScriptMessage,       (p, s) => HandleScriptMessage(p));
        router.Register(OutHeader.OpenShopDlg,         (p, s) => HandleOpenShopDlg(p));
        router.Register(OutHeader.ShopResult,          (p, s) => HandleShopResult(p));
        router.Register(OutHeader.TrunkResult,         (p, s) => HandleTrunkResult(p));
        router.Register(OutHeader.Messenger,           (p, s) => HandleMessenger(p));
        router.Register(OutHeader.FuncKeyMappedInit,   (p, s) => HandleFuncKeyMappedInit(p));
        router.Register(OutHeader.QuickslotMappedInit, (p, s) => HandleQuickslotMappedInit(p));
        router.Register(OutHeader.FootHoldInfo,        (p, s) => HandleFootHoldInfo(p));
        router.Register(OutHeader.ChangeSkillRecordResult, (p, s) => HandleChangeSkillRecord(p));
        router.Register(OutHeader.TemporaryStatSet,    (p, s) => OnTemporaryStatSet?.Invoke());
        router.Register(OutHeader.TemporaryStatReset,  (p, s) => OnTemporaryStatReset?.Invoke());
    }

    // ── Migration ───────────────────────────────────────────────────────────────
    // MigrateCommand(16): byte (1 = migrate), byte[4] channelHost, short channelPort.
    // Sent on an in-game channel transfer (and cash-shop return). Mirrors
    // kinoko/packet/ClientPacket.migrateCommand.
    private void HandleMigrateCommand(InPacket p)
    {
        p.ReadByte();                       // 1 (migrate flag)
        var host = p.ReadBytes(4);
        var port = (ushort)p.ReadShort();
        OnMigrateCommand?.Invoke(host, port);
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
                // CharacterData continues after the stat block (SetField uses
                // DBChar.ALL): friendMax, linkedChar, money, inventory sizes,
                // ext-slot expiry, then the equipped + 5 inventory tabs.
                p.ReadByte();                       // nFriendMax
                p.ReadBool();                       // sLinkedCharacter (bool → str=false)
                args.Money = p.ReadInt();           // nMoney
                p.ReadByte();                       // equip inv size
                p.ReadByte();                       // consume inv size
                p.ReadByte();                       // install inv size
                p.ReadByte();                       // etc inv size
                p.ReadByte();                       // cash inv size
                p.ReadLong();                       // aEquipExtExpire (FileTime)
                args.Inventory = DecodeInventory(p);
                // Fold worn equips into the avatar look so the local player renders dressed — SetField's
                // CharacterData has no compact AvatarLook block, only the full equipped inventory.
                if (args.Look is not null
                    && args.Inventory.TryGetValue(InventoryType.Equipped, out var worn))
                {
                    AvatarCodec.PopulateEquipsFromInventory(args.Look, worn);
                }
                args.Skills = DecodeSkillRecords(p); // DBChar.SKILLRECORD section
                DecodeSkillCooltime(p);              // DBChar.SKILLCOOLTIME (skipped)
                args.Quests = DecodeQuestRecords(p); // DBChar.QUESTRECORD (in-progress)
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

    // CharacterData inventory section (DBChar.ITEMSLOT*). The equip section has
    // 5 short-terminated sub-loops (normal-equipped, cash-equipped, equip-tab,
    // dragon, mechanic); the consume/install/etc/cash tabs are byte-terminated.
    private static Dictionary<InventoryType, List<(short, InventoryItem)>> DecodeInventory(InPacket p)
    {
        var inv = new Dictionary<InventoryType, List<(short, InventoryItem)>>();
        var equipped = new List<(short, InventoryItem)>();
        var equipTab = new List<(short, InventoryItem)>();

        // ITEMSLOTEQUIP — normal equipped (worn).
        short pos;
        while ((pos = p.ReadShort()) != 0)
        {
            equipped.Add((pos, ItemDecoder.Decode(p)));
        }
        // cash-equipped (body part - 1000).
        while ((pos = p.ReadShort()) != 0)
        {
            equipped.Add((pos, ItemDecoder.Decode(p)));
        }
        // equip inventory tab.
        while ((pos = p.ReadShort()) != 0)
        {
            equipTab.Add((pos, ItemDecoder.Decode(p)));
        }
        // dragon equips.
        while (p.ReadShort() != 0)
        {
            ItemDecoder.Decode(p);
        }
        // mechanic equips.
        while (p.ReadShort() != 0)
        {
            ItemDecoder.Decode(p);
        }

        inv[InventoryType.Equipped] = equipped;
        inv[InventoryType.Equip]    = equipTab;
        inv[InventoryType.Consume]  = DecodeByteTab(p);
        inv[InventoryType.Install]  = DecodeByteTab(p);
        inv[InventoryType.Etc]      = DecodeByteTab(p);
        inv[InventoryType.Cash]     = DecodeByteTab(p);
        return inv;
    }

    private static List<(short, InventoryItem)> DecodeByteTab(InPacket p)
    {
        var list = new List<(short, InventoryItem)>();
        byte slot;
        while ((slot = p.ReadByte()) != 0)
        {
            list.Add((slot, ItemDecoder.Decode(p)));
        }
        return list;
    }

    // CharacterData SKILLRECORD section: short count, per record
    // { int skillId, int level, FileTime expire, if needMasterLevel: int masterLevel }.
    private static List<SkillRecord> DecodeSkillRecords(InPacket p)
    {
        var records = new List<SkillRecord>();
        var count = p.ReadShort();
        for (var i = 0; i < count; i++)
        {
            var skillId = p.ReadInt();
            var level   = p.ReadInt();
            p.ReadLong();                       // expire (FileTime)
            var masterLevel = IsSkillNeedMasterLevel(skillId) ? p.ReadInt() : 0;
            records.Add(new SkillRecord { SkillId = skillId, Level = level, MasterLevel = masterLevel });
        }
        return records;
    }

    // CharacterData SKILLCOOLTIME section: short count, per { int skillId, short cooltime }.
    private static void DecodeSkillCooltime(InPacket p)
    {
        var count = p.ReadShort();
        for (var i = 0; i < count; i++)
        {
            p.ReadInt();    // skillId
            p.ReadShort();  // cooltime seconds
        }
    }

    // CharacterData QUESTRECORD then QUESTCOMPLETE sections (these are adjacent in the
    // DBChar.ALL stream the server sends at field entry — see CharacterData.encodeCharacterData):
    //   QUESTRECORD  (in-progress): short count, { short questId, string value }
    //   QUESTCOMPLETE (completed):  short count, { short questId, FileTime(8) }
    // Quest ids are unsigned shorts (some medal/event ids exceed 32767).
    private static List<QuestRecordArgs> DecodeQuestRecords(InPacket p)
    {
        var quests = new List<QuestRecordArgs>();
        var started = p.ReadShort();
        for (var i = 0; i < started; i++)
        {
            int questId = (ushort)p.ReadShort();
            var value = p.ReadString();
            quests.Add(new QuestRecordArgs { QuestId = questId, State = 1, Value = value });
        }
        var completed = p.ReadShort();
        for (var i = 0; i < completed; i++)
        {
            int questId = (ushort)p.ReadShort();
            p.ReadLong();   // completedTime (FileTime) — not displayed
            quests.Add(new QuestRecordArgs { QuestId = questId, State = 2, Value = string.Empty });
        }
        return quests;
    }

    // Mirrors upstream SkillConstants.isSkillNeedMasterLevel for common jobs:
    // a first-job-base id (e.g. 100/200/1000) never needs it; otherwise a
    // 4th-job skill (job id ending in 2) does. Evan/Dual edge cases are
    // approximated — a miss only corrupts the trailing skill list, which is
    // isolated (the field still loads).
    private static bool IsSkillNeedMasterLevel(int skillId)
    {
        var jobId = skillId / 10000;
        if (jobId == 100 * (jobId / 100))
        {
            return false;
        }
        return jobId % 10 == 2;
    }

    private void HandleAliveReq(ClientSession session)
    {
        var ack = OutPacket.Of(InHeader.AliveAck);
        session.Send(ack);
    }

    // ChangeSkillRecordResult: byte exclRequest, short count,
    // per { int skillId, int level, int masterLevel, FileTime expire }, byte bSN.
    private void HandleChangeSkillRecord(InPacket p)
    {
        p.ReadByte();                       // exclRequest
        var count = p.ReadShort();
        var records = new List<SkillRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var skillId     = p.ReadInt();
            var level       = p.ReadInt();
            var masterLevel = p.ReadInt();
            p.ReadLong();                   // expire (FileTime)
            records.Add(new SkillRecord { SkillId = skillId, Level = level, MasterLevel = masterLevel });
        }
        OnSkillRecordResult?.Invoke(records);
    }

    // ── StatChanged ───────────────────────────────────────────────────────────
    // Kinoko: WvsContext.statChanged — byte bExclRequestSent, int dwCharStat (4-byte
    // mask, NOT long), then per-stat fields in Stat.ENCODE_ORDER, trailing two bytes.
    // Stat bits (kinoko/world/user/stat/Stat.java):
    //   SKIN=0x1 FACE=0x2 HAIR=0x4 PETSN=0x8 LEVEL=0x10 JOB=0x20 STR=0x40 DEX=0x80
    //   INT=0x100 LUK=0x200 HP=0x400 MHP=0x800 MP=0x1000 MMP=0x2000 AP=0x4000
    //   SP=0x8000 EXP=0x10000 POP=0x20000 MONEY=0x40000 PETSN2=0x80000
    //   PETSN3=0x100000 TEMPEXP=0x200000
    // Widths: SKIN/LEVEL=byte; JOB/STR/DEX/INT/LUK/AP/SP/POP=short;
    //   FACE/HAIR/HP/MHP/MP/MMP/EXP/MONEY/TEMPEXP=int; PETSN/2/3=long.
    // SP is read as a short — extend-SP jobs (Evan, etc.) encode a map instead, but
    // those packets carry no EXP/MONEY for us so an isolated parse error is harmless
    // (the router catches it; the canonical HUD snapshot also decodes independently).
    private void HandleStatChanged(InPacket p)
    {
        p.ReadByte();             // bExclRequestSent
        var mask = p.ReadInt();   // dwCharStat
        var args = new StatChangedArgs { Mask = mask };

        if ((mask & 0x1)     != 0) args.Skin  = p.ReadByte();
        if ((mask & 0x2)     != 0) args.Face  = p.ReadInt();
        if ((mask & 0x4)     != 0) args.Hair  = p.ReadInt();
        if ((mask & 0x8)     != 0) p.ReadLong();                  // PETSN
        if ((mask & 0x10)    != 0) args.Level = p.ReadByte();
        if ((mask & 0x20)    != 0) args.Job   = p.ReadShort();
        if ((mask & 0x40)    != 0) args.Str   = p.ReadShort();
        if ((mask & 0x80)    != 0) args.Dex   = p.ReadShort();
        if ((mask & 0x100)   != 0) args.Int   = p.ReadShort();
        if ((mask & 0x200)   != 0) args.Luk   = p.ReadShort();
        if ((mask & 0x400)   != 0) args.Hp    = p.ReadInt();
        if ((mask & 0x800)   != 0) args.MaxHp = p.ReadInt();
        if ((mask & 0x1000)  != 0) args.Mp    = p.ReadInt();
        if ((mask & 0x2000)  != 0) args.MaxMp = p.ReadInt();
        if ((mask & 0x4000)  != 0) args.Ap    = p.ReadShort();
        if ((mask & 0x8000)  != 0) args.Sp    = p.ReadShort();
        if ((mask & 0x10000) != 0) args.Exp   = p.ReadInt();
        if ((mask & 0x20000) != 0) args.Pop   = p.ReadShort();
        if ((mask & 0x40000) != 0) args.Meso  = p.ReadInt();      // MONEY
        if ((mask & 0x80000) != 0) p.ReadLong();                  // PETSN2
        if ((mask & 0x100000)!= 0) p.ReadLong();                  // PETSN3
        if ((mask & 0x200000)!= 0) p.ReadInt();                   // TEMPEXP

        OnStatChanged?.Invoke(args);
    }

    // ── Message (loot / EXP / meso popups) ──────────────────────────────────────
    // kinoko/packet/world/MessagePacket — byte MessageType, then a per-type tail.
    //   IncEXP(3):    byte white, int exp, … (only exp is needed)
    //   IncMoney(6):  int money
    //   DropPickUp(0): sbyte subtype —
    //     ITEM_BUNDLE(0)/ITEM_SINGLE(2): int itemId, int quantity
    //     MONEY(1):                      byte portionNotFound, int money, short cafeBonus
    //     <0 (warning):                  no body
    // Other message types (QuestRecord, System, …) are surfaced elsewhere.
    private void HandleMessage(InPacket p)
    {
        var msgType = p.ReadByte();
        switch (msgType)
        {
            case 3:                                   // IncEXP
                p.ReadByte();                         // white
                OnIncExp?.Invoke(p.ReadInt());
                break;
            case 6:                                   // IncMoney
                OnIncMoney?.Invoke(p.ReadInt());
                break;
            case 0:                                   // DropPickUp
            {
                var subtype = p.ReadSByte();
                var args = new LootMessageArgs { Warning = subtype };
                switch (subtype)
                {
                    case 0:                           // ITEM_BUNDLE
                    case 2:                           // ITEM_SINGLE
                        args.ItemId   = p.ReadInt();
                        args.Quantity = p.ReadInt();
                        break;
                    case 1:                           // MONEY
                        args.IsMoney = true;
                        p.ReadByte();                 // portionNotFound
                        args.Money = p.ReadInt();
                        p.ReadShort();                // internet-cafe meso bonus
                        break;
                    // subtype < 0: a warning (inventory full, etc.) — no body.
                }
                OnLootMessage?.Invoke(args);
                break;
            }
            case 1:                                   // QuestRecord
            {
                var questId = p.ReadShort();
                var state = p.ReadByte();             // 0=None 1=Perform 2=Complete
                var value = string.Empty;
                if (state == 1) value = p.ReadString();       // PERFORM → progress string
                else if (state == 2) p.ReadLong();            // COMPLETE → FileTime
                else if (state == 0) p.ReadByte();            // NONE → delete flag
                OnQuestRecord?.Invoke(new QuestRecordArgs { QuestId = questId, State = state, Value = value });
                break;
            }
            case 11:                                  // QuestRecordEx
            {
                var questId = p.ReadShort();
                var value = p.ReadString();
                OnQuestRecord?.Invoke(new QuestRecordArgs { QuestId = questId, State = 1, Value = value, IsEx = true });
                break;
            }
        }
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

    private void HandleMobChangeController(InPacket p)
    {
        // byte nCalcDamageIndex (0 = release control, 1|2 = take control), int dwMobID.
        // (When taking control the server also re-sends the full spawn body; we ignore
        // it — the mob already exists from MobEnterField, and the packet is self-framed.)
        var isCtrl = p.ReadByte() != 0;
        var mobId  = p.ReadInt();
        _logger.LogDebug("MobChangeController: mobId={Id} ctrl={Ctrl}", mobId, isCtrl);
        OnMobChangeController?.Invoke(mobId, isCtrl);
        // NOTE: we deliberately do NOT send MobApplyCtrl(228) here. The server assigns the
        // controller itself; MobApplyCtrl is a client control-CLAIM, which Kinoko logs as
        // "invalid MobApplyCtrl request" for normal (non-firstAttack/pickUpDrop) mobs. The
        // controller's real job is MobMove(227) (see the GameStage TODO), not an ack.
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

    // ── Mob control ack ──────────────────────────────────────────────────────────

    // MobCtrlAck (288): int mobId, short mobCtrlSn, byte nextAttackPossible, short mp,
    // byte nSkillCommand, byte nSLV. Sent by the server after every MobMove the
    // controller emitted — echoes the mobCtrlSn (correlation), syncs the mob's MP, and
    // suggests the next skill it would like played. Per the v95 IDB the ack is
    // informational; the client doesn't gate further moves on it.
    private void HandleMobCtrlAck(InPacket p)
    {
        var mobId       = p.ReadInt();
        var mobCtrlSn   = p.ReadShort();
        var nextAttack  = p.ReadByte();
        var mp          = p.ReadShort();
        var skill       = p.ReadByte();
        var slv         = p.ReadByte();
        OnMobCtrlAck?.Invoke(new MobCtrlAckArgs
        {
            MobId              = mobId,
            MobCtrlSn          = mobCtrlSn,
            NextAttackPossible = nextAttack != 0,
            Mp                 = mp,
            NextSkillId        = skill,
            NextSkillLevel     = slv,
        });
    }

    // ── Mob HP indicator (server -> attacker, "I hit this mob") ──────────────────

    // MobHPIndicator (298): int mobId, byte percentage. Sent only to the attacker after
    // each melee/skill hit (the server skips the MobDamaged broadcast back to the hitter
    // to save bandwidth). Drives the HP bar above the hit mob locally — see GameStage's
    // OnMobHpIndicator subscription -> MobLook.SetHpPercent.
    private void HandleMobHpIndicator(InPacket p)
    {
        var mobId = p.ReadInt();
        var pct   = p.ReadByte();
        OnMobHpIndicator?.Invoke(mobId, pct);
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

    // UserEmotion (OutHeader 219): int charId, int nEmotion, int nDuration, byte bByItemOption.
    // Mirrors upstream kinoko/packet/user/UserRemote.emotion. Broadcast to every user in the
    // field except the sender; the listener routes it to the matching OtherCharLook.
    private void HandleUserEmotion(InPacket p)
    {
        var charId    = p.ReadInt();
        var emotion   = p.ReadInt();
        var duration  = p.ReadInt();
        var byItem    = p.ReadBool();
        OnUserEmotion?.Invoke(new UserEmotionArgs
        {
            CharId       = charId,
            Emotion      = emotion,
            DurationMs   = duration,
            ByItemOption = byItem,
        });
    }

    // UserEmotionLocal (OutHeader 232): int nEmotion, int nDuration, byte bByItemOption.
    // Vanilla Kinoko does not emit this in the FuncKey flow (handleUserEmotion only
    // broadcasts UserRemote.emotion=219, excluding the sender); kept for forked-server
    // and item-effect paths. CharId=0 is the sentinel for "the local player".
    private void HandleUserEmotionLocal(InPacket p)
    {
        var emotion  = p.ReadInt();
        var duration = p.ReadInt();
        var byItem   = p.ReadBool();
        OnUserEmotion?.Invoke(new UserEmotionArgs
        {
            CharId       = 0,
            Emotion      = emotion,
            DurationMs   = duration,
            ByItemOption = byItem,
        });
    }

    // ── Character profile ──────────────────────────────────────────────────────
    // CharacterInfo(61): WvsContext.characterInfo. Carries no avatar look or name —
    // GameStage reuses the in-field OtherCharLook (keyed by CharId) for those.
    private void HandleCharacterInfo(InPacket p) => OnCharacterInfo?.Invoke(DecodeCharacterInfo(p));

    /// <summary>Decodes the CharacterInfo(61) body. Static + public so the wire shape is unit-testable.
    /// Field order mirrors upstream Kinoko <c>WvsContext.characterInfo</c> byte-for-byte through the pet
    /// loop; the trailing taming-mob / wishlist / medal / chair blocks aren't shown and are left unread
    /// (each S→C packet is framed independently, so unread tail bytes are harmless).</summary>
    public static CharacterInfoArgs DecodeCharacterInfo(InPacket p)
    {
        var a = new CharacterInfoArgs
        {
            CharId   = p.ReadInt(),       // dwCharacterId
            Level    = p.ReadByte(),      // nLevel
            Job      = p.ReadShort(),     // nJob
            Fame     = p.ReadShort(),     // nPOP (signed)
            Married  = p.ReadByte() != 0, // bIsMarried
            Guild    = p.ReadString(),    // sCommunity
            Alliance = p.ReadString(),    // sAlliance
        };
        p.ReadByte();                     // bMedalInfo (always 0 in Kinoko)

        // CUIUserInfo::SetMultiPetInfo: one block per active pet, a 0 byte terminates.
        while (p.ReadByte() != 0)         // bPetActivated
        {
            a.Pets.Add(new CharInfoPet
            {
                TemplateId  = p.ReadInt(),
                Name        = p.ReadString(),
                Level       = p.ReadByte(),
                Tameness    = p.ReadShort(),
                Repleteness = p.ReadByte(),
                PetSkill    = p.ReadShort(),
                PetWear     = p.ReadInt(),
            });
        }
        return a;
    }

    // ── Drops ─────────────────────────────────────────────────────────────────
    // DropEnterField: byte enterType, int dropId, byte isMoney, int info(itemId/amount),
    //   int ownerId, byte ownType, short x, short y, …

    private void HandleDropEnter(InPacket p)
    {
        var enterType = p.ReadByte();   // DropEnterType: 0 JUST_SHOWING, 1 CREATE, 2 ON_THE_FOOTHOLD, 3 FADING_OUT
        var dropId    = p.ReadInt();
        var isMoney   = p.ReadBool();
        var info      = p.ReadInt();    // itemId or meso amount
        var ownerId   = p.ReadInt();
        p.ReadByte();    // ownType
        var x = p.ReadShort();          // landing (resting) position
        var y = p.ReadShort();
        p.ReadInt();     // sourceId (object id)
        // Animated enter types carry the source position the drop is tossed from (mob/player); the parabolic
        // toss runs from there to (x,y). ON_THE_FOOTHOLD (2) has no source — the drop just appears.
        var animated = enterType != 2;
        short sx = x, sy = y;
        if (animated)
        {
            sx = p.ReadShort();
            sy = p.ReadShort();
            p.ReadShort();   // tDelay
        }
        OnDropEnter?.Invoke(new DropEnterArgs
        {
            DropId = dropId, IsMoney = isMoney,
            ItemIdOrAmount = info, X = x, Y = y,
            SourceX = sx, SourceY = sy, Animated = animated,
        });
    }

    private void HandleDropLeave(InPacket p)
    {
        var leaveType = p.ReadByte();
        var dropId    = p.ReadInt();
        var pickUpId  = 0;
        if (leaveType is 2 or 3 or 5) pickUpId = p.ReadInt();   // picked up by user(2)/mob(3)/pet(5)
        OnDropLeave?.Invoke(new DropLeaveArgs { DropId = dropId, LeaveType = leaveType, PickUpId = pickUpId });
    }

    // ── Inventory ─────────────────────────────────────────────────────────────
    // WvsContext.inventoryOperation:
    //   byte exclRequest, byte opCount, [opType(byte) invType(byte) pos(short) + type-specific], byte 0

    // WvsContext.inventoryOperation: byte exclRequest, byte count,
    // per op { byte opType, byte invType, short pos, payload }, byte bSN.
    // opType: 0=NewItem 1=ItemNumber 2=Position 3=DelItem 4=EXP.
    // invType: 0=Equipped 1=Equip 2=Consume 3=Install 4=Etc 5=Cash.
    private void HandleInventoryOp(InPacket p)
    {
        p.ReadByte();    // bExclRequestSent
        var count = p.ReadByte();
        var ops   = new List<InventoryOpArg>(count);
        for (var i = 0; i < count; i++)
        {
            var opType  = p.ReadByte();
            var invType = (InventoryType)p.ReadByte();
            var pos     = p.ReadShort();
            var op      = new InventoryOpArg { OpType = opType, InvType = invType, Pos = pos };
            switch (opType)
            {
                case 0: // NewItem — full item slot
                    op.Item   = ItemDecoder.Decode(p);
                    op.ItemId = op.Item.ItemId;
                    break;
                case 1: // ItemNumber — new quantity
                    op.Quantity = p.ReadShort();
                    break;
                case 2: // Position — new slot
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

    // ── Group chat ──────────────────────────────────────────────────────────────
    // FieldPacket.groupMessage (OutHeader.GroupMessage = 150):
    //   byte groupType, string fromName, string text.
    private void HandleGroupMessage(InPacket p)
    {
        var groupType = p.ReadByte();
        var fromName  = p.ReadString();
        var text      = p.ReadString();
        OnGroupMessage?.Invoke(groupType, fromName, text);
    }

    // ── Whisper ─────────────────────────────────────────────────────────────────
    // WhisperPacket (OutHeader.Whisper = 151): byte flag, then a flag-specific
    // body. We only surface WhisperReceive (flag 0x12 = Whisper 0x2 | Receive
    // 0x10) as a chat line: { string fromName, byte channelId, byte fromAdmin,
    // string text }. WhisperResult / LocationResult replies are logged only.
    // Incoming parse errors are isolated (they never desync the cipher), so a
    // best-effort decode is safe.
    private const int WhisperReceiveBit = 0x10;

    private void HandleWhisper(InPacket p)
    {
        var flag = p.ReadByte();
        if ((flag & WhisperReceiveBit) != 0)
        {
            try
            {
                var fromName  = p.ReadString();
                var channelId = p.ReadByte();
                p.ReadByte();                   // bFromAdmin
                var text      = p.ReadString();
                OnWhisper?.Invoke(fromName, channelId, text);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Whisper receive partial decode flag={Flag}", flag);
            }
        }
        else
        {
            _logger.LogDebug("Whisper result/location reply flag={Flag} (not shown)", flag);
        }
    }

    // ── Party result ────────────────────────────────────────────────────────────
    // PartyPacket (OutHeader.PartyResult = 62): byte resultType then a per-type
    // body. resultType values mirror upstream kinoko/server/party/PartyResultType.
    private const int PartyResInviteParty        = 4;
    private const int PartyResLoadPartyDone      = 7;
    private const int PartyResWithdrawPartyDone  = 12;
    private const int PartyResJoinPartyDone      = 15;
    private const int PartyResUserMigration      = 38;

    private void HandlePartyResult(InPacket p)
    {
        var resultType = p.ReadByte();
        switch (resultType)
        {
            case PartyResInviteParty:
            {
                // int inviterId, string inviterName, int level, int job, byte 0
                var inviterId   = p.ReadInt();
                var inviterName = p.ReadString();
                p.ReadInt();                    // nLevel
                p.ReadInt();                    // nJobCode
                p.ReadByte();                   // party-search related
                OnPartyInvite?.Invoke(inviterId, inviterName);
                break;
            }
            case PartyResLoadPartyDone:
            {
                // int partyId, PARTYDATA
                p.ReadInt();                    // nPartyID
                EmitPartyData(p);
                break;
            }
            case PartyResJoinPartyDone:
            {
                // int partyId, string memberName, PARTYDATA
                p.ReadInt();                    // nPartyID
                p.ReadString();                 // joining member name
                EmitPartyData(p);
                break;
            }
            case PartyResWithdrawPartyDone:
            {
                // int partyId, int charId, byte notDisband; if notDisband:
                //   byte kick, string name, PARTYDATA. Else the party disbanded.
                p.ReadInt();                    // nPartyID
                p.ReadInt();                    // leaving charId
                var notDisband = p.ReadBool();
                if (notDisband)
                {
                    p.ReadByte();               // kick / expelled flag
                    p.ReadString();             // member name
                    EmitPartyData(p);
                }
                else
                {
                    OnPartyLoad?.Invoke(new List<PartyMember>(), 0);
                }
                break;
            }
            case PartyResUserMigration:
            {
                // PARTYDATA-bearing in some builds — decode defensively.
                try
                {
                    EmitPartyData(p);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PartyResult UserMigration decode (ignored)");
                }
                break;
            }
            default:
                _logger.LogDebug("PartyResult type {Type} not handled", resultType);
                break;
        }
    }

    // PARTYDATA::Decode — column-major, PARTY_MAX = 6. Mirrors upstream
    // kinoko/server/party/Party.encode byte-for-byte:
    //   int[6] charId, string(13)[6] name, int[6] job, int[6] level,
    //   int[6] channelId, int partyBossId, int[6] fieldId,
    //   TownPortal[6] (each = int townId, int fieldId, int skillId, int x, int y),
    //   int[6] pqReward, int[6] pqRewardType, int pqRewardMobTemplateId, int bPQReward.
    // A member slot is empty when charId == 0; empties are skipped.
    private const int PartyMax = 6;

    private void EmitPartyData(InPacket p)
    {
        var charIds   = new int[PartyMax];
        var names     = new string[PartyMax];
        var jobs      = new int[PartyMax];
        var levels    = new int[PartyMax];
        var channels  = new int[PartyMax];

        for (var i = 0; i < PartyMax; i++) charIds[i]  = p.ReadInt();
        for (var i = 0; i < PartyMax; i++) names[i]    = p.ReadString(13);
        for (var i = 0; i < PartyMax; i++) jobs[i]     = p.ReadInt();
        for (var i = 0; i < PartyMax; i++) levels[i]   = p.ReadInt();
        for (var i = 0; i < PartyMax; i++) channels[i] = p.ReadInt();
        var bossId = p.ReadInt();
        for (var i = 0; i < PartyMax; i++) p.ReadInt();             // fieldId
        for (var i = 0; i < PartyMax; i++) p.Skip(5 * 4);          // TownPortal (5 ints)
        for (var i = 0; i < PartyMax; i++) p.ReadInt();             // pqReward
        for (var i = 0; i < PartyMax; i++) p.ReadInt();             // pqRewardType
        p.ReadInt();                                                // pqRewardMobTemplateId
        p.ReadInt();                                                // bPQReward

        var members = new List<PartyMember>(PartyMax);
        for (var i = 0; i < PartyMax; i++)
        {
            if (charIds[i] == 0)
            {
                continue;
            }
            members.Add(new PartyMember
            {
                CharId  = charIds[i],
                Name    = names[i],
                Job     = jobs[i],
                Level   = levels[i],
                Channel = channels[i],
            });
        }
        OnPartyLoad?.Invoke(members, bossId);
    }

    // ── Friend result ───────────────────────────────────────────────────────────
    // FriendPacket (OutHeader.FriendResult = 65): byte resultType. For the list
    // (re)load shapes (LoadFriend_Done = 7, SetFriend_Done = 10,
    // DeleteFriend_Done = 18 — all use CWvsContext::CFriend::Reset):
    //   byte count, count × GW_Friend { int friendId, string name(13), byte flag,
    //   int channel, string group(17) }, then count × int inShop.
    // channel == CHANNEL_OFFLINE (-2) means offline (so online == channel >= 0).
    private const int FriendResLoadFriendDone = 7;
    private const int FriendResSetFriendDone  = 10;
    private const int FriendResDeleteFriendDone = 18;

    private void HandleFriendResult(InPacket p)
    {
        var resultType = p.ReadByte();
        if (resultType != FriendResLoadFriendDone &&
            resultType != FriendResSetFriendDone &&
            resultType != FriendResDeleteFriendDone)
        {
            _logger.LogDebug("FriendResult type {Type} not handled", resultType);
            return;
        }
        var count = p.ReadByte();
        var friends = new List<FriendInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var friendId = p.ReadInt();
            var name     = p.ReadString(13);
            var flag     = p.ReadByte();
            var channel  = p.ReadInt();
            p.ReadString(17);               // friend group
            friends.Add(new FriendInfo
            {
                FriendId = friendId,
                Name     = name,
                Flag     = flag,
                Channel  = channel,
            });
        }
        for (var i = 0; i < count; i++)
        {
            p.ReadInt();                    // aInShop
        }
        OnFriendList?.Invoke(friends);
    }

    // ── Guild ───────────────────────────────────────────────────────────────────
    // GuildResult: byte resultType. LoadGuild_Done(28) → byte hasGuild + Guild.encode:
    //   int guildId, string name, 5×string gradeName, byte memberCount,
    //   memberCount×int charId (column-major), memberCount×GuildMember(37):
    //     string(13) name, int job, int level, int rank, int online, int 0, int allianceRank;
    //   int memberMax, short markBg, byte markBgColor, short mark, byte markColor,
    //   string notice, int points, int allianceId, byte level, short 0 (skills).
    private const int GuildResLoadGuildDone = 28;

    private void HandleGuildResult(InPacket p)
    {
        var resultType = p.ReadByte();
        if (resultType != GuildResLoadGuildDone)
        {
            _logger.LogDebug("GuildResult type {Type} not handled", resultType);
            return;
        }
        if (!p.ReadBool())          // no guild
        {
            OnGuildLoad?.Invoke(null);
            return;
        }
        var args = new GuildLoadArgs
        {
            GuildId = p.ReadInt(),
            Name = p.ReadString(),
        };
        for (var i = 0; i < 5; i++) p.ReadString();   // grade names

        var memberCount = p.ReadByte();
        var ids = new int[memberCount];
        for (var i = 0; i < memberCount; i++) ids[i] = p.ReadInt();
        for (var i = 0; i < memberCount; i++)
        {
            var name        = p.ReadString(13);
            var job         = p.ReadInt();
            var level       = p.ReadInt();
            var rank        = p.ReadInt();
            var online      = p.ReadInt();
            p.ReadInt();                                // commitment
            p.ReadInt();                                // alliance rank
            args.Members.Add(new GuildMemberArg
            {
                CharacterId = ids[i],
                Name = name,
                Job = job,
                Level = level,
                Rank = rank,
                Online = online != 0,
            });
        }
        OnGuildLoad?.Invoke(args);
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
    // FieldPacket.funcKeyMappedInit: byte bDefault, then FUNC_KEY_MAP_SIZE (89)
    // entries of (byte type + int actionId) when !bDefault.

    private void HandleFuncKeyMappedInit(InPacket p)
    {
        var isDefault = p.ReadBool();
        var entries = new List<FuncKeyEntry>(89);
        if (!isDefault)
        {
            for (var i = 0; i < 89; i++)
            {
                var type     = p.ReadByte();
                var actionId = p.ReadInt();
                entries.Add(new FuncKeyEntry { KeyIndex = i, Type = type, ActionId = actionId });
            }
        }
        OnFuncKeyMappedInit?.Invoke(entries);
    }

    // QuickslotMappedInit (175): byte useDefault, then QUICKSLOT_KEY_MAP_SIZE (8) ints
    // (DInput scancodes), written unconditionally by the server.
    private void HandleQuickslotMappedInit(InPacket p)
    {
        p.ReadBool(); // defaults flag; the 8 keys follow regardless
        if (p.Remaining < 8 * sizeof(int)) return;
        var keys = new int[8];
        for (var i = 0; i < 8; i++) keys[i] = p.ReadInt();
        OnQuickslotInit?.Invoke(keys);
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

    // ── NPC shop ────────────────────────────────────────────────────────────────
    // OpenShopDlg: int npcId, short count, then per item: int itemId, int price,
    //   byte discount, int tokenItemId, int tokenPrice, int period, int levelLimited,
    //   then rechargeable → double unitPrice + short maxPerSlot; else short quantity +
    //   short maxPerSlot. Mirrors kinoko ShopDialog.encode.
    private void HandleOpenShopDlg(InPacket p)
    {
        var npcId = p.ReadInt();
        var count = p.ReadShort();
        var items = new List<ShopItemArg>(Math.Max(0, (int)count));
        for (var i = 0; i < count; i++)
        {
            var itemId = p.ReadInt();
            var price = p.ReadInt();
            p.ReadByte();           // discount rate
            p.ReadInt();            // token item id
            p.ReadInt();            // token price
            p.ReadInt();            // item period
            p.ReadInt();            // level limited
            short quantity;
            if (IsRechargeable(itemId))
            {
                p.ReadDouble();     // unit price
                quantity = p.ReadShort();   // max per slot
            }
            else
            {
                quantity = p.ReadShort();
                p.ReadShort();      // max per slot
            }
            items.Add(new ShopItemArg { ItemId = itemId, Price = price, Quantity = quantity });
        }
        OnShopOpen?.Invoke(new ShopOpenArgs { NpcId = npcId, Items = items });
    }

    // Throwing stars (207xxxx) and bullets (233xxxx) recharge by unit price.
    private static bool IsRechargeable(int itemId)
    {
        var prefix = itemId / 10000;
        return prefix == 207 || prefix == 233;
    }

    // ShopResult: byte resultType; LimitLevel(14/15) → int level; ServerMsg(19) →
    // bool hasMsg + string. Other types carry no body.
    private void HandleShopResult(InPacket p)
    {
        var resultType = p.ReadByte();
        var args = new ShopResultArgs { ResultType = resultType };
        try
        {
            if (resultType is 14 or 15)
            {
                args.Level = p.ReadInt();
            }
            else if (resultType == 19 && p.ReadBool())
            {
                args.Message = p.ReadString();
            }
        }
        catch (Exception) { /* trailing fields are best-effort */ }
        OnShopResult?.Invoke(args);
    }

    // ── Player storage / trunk ────────────────────────────────────────────────────
    // TrunkResult(368): byte resultType, then per TrunkResultType:
    //   OpenTrunkDlg(22): int templateId + Trunk.encode
    //   GetSuccess(9)/PutSuccess(13)/SortItem(15)/MoneySuccess(19): Trunk.encode(Items)
    //   ServerMsg(24): bool hasMsg + string. Others carry no body.
    // Trunk.encodeItems: byte slotCount, long dbcharFlag, [MONEY→int money],
    //   then per inventory type bit set in the flag: byte count + count×Item.encode.
    private void HandleTrunkResult(InPacket p)
    {
        var resultType = p.ReadByte();
        var args = new TrunkResultArgs { ResultType = resultType };
        try
        {
            switch (resultType)
            {
                case 22:                       // OpenTrunkDlg
                    args.TemplateId = p.ReadInt();
                    DecodeTrunk(p, args);
                    break;
                case 9:                        // GetSuccess
                case 13:                       // PutSuccess
                case 15:                       // SortItem
                case 19:                       // MoneySuccess (flag = MONEY only)
                    DecodeTrunk(p, args);
                    break;
                case 24:                       // ServerMsg
                    if (p.ReadBool()) args.Message = p.ReadString();
                    break;
                default:
                    break;                     // failure subtypes carry no body
            }
        }
        catch (Exception) { /* trailing fields are best-effort */ }
        OnTrunkResult?.Invoke(args);
    }

    // DBChar flag bits (MONEY=0x2; item slots 0x4..0x40) drive which sections follow.
    private static void DecodeTrunk(InPacket p, TrunkResultArgs args)
    {
        args.HasContents = true;
        args.SlotCount = p.ReadByte();
        var flag = p.ReadLong();
        if ((flag & 0x2L) != 0) args.Money = p.ReadInt();
        ReadTrunkBlock(p, flag, 0x4L,  invType: 1, args);  // EQUIP
        ReadTrunkBlock(p, flag, 0x8L,  invType: 2, args);  // CONSUME
        ReadTrunkBlock(p, flag, 0x10L, invType: 3, args);  // INSTALL
        ReadTrunkBlock(p, flag, 0x20L, invType: 4, args);  // ETC
        ReadTrunkBlock(p, flag, 0x40L, invType: 5, args);  // CASH
    }

    private static void ReadTrunkBlock(InPacket p, long flag, long bit, byte invType, TrunkResultArgs args)
    {
        if ((flag & bit) == 0) return;
        int count = p.ReadByte();
        for (var i = 0; i < count; i++)
        {
            var item = ItemDecoder.Decode(p);
            args.Items.Add(new TrunkItemArg
            {
                InvType = invType,
                PositionInType = i,        // index within this type's block → GetItem position
                ItemId = item.ItemId,
                Quantity = item.Quantity == 0 ? 1 : item.Quantity,  // equips/pets have no bundle qty
            });
        }
    }

    // ── Maple Messenger ───────────────────────────────────────────────────────────
    // Messenger(372): byte action (MessengerProtocol), then per action:
    //   Enter(0): byte idx, AvatarLook, string name, byte channel, byte isNew
    //   SelfEnterResult(1)/Leave(2): byte idx
    //   Invite(3): string inviter, byte channel, int dwSN, byte admin
    //   InviteResult(4): string name, byte success
    //   Blocked(5): string name, byte blocked
    //   Chat(6): string text
    //   Avatar(7): byte idx, AvatarLook
    //   Migrated(8): { byte idx, AvatarLook, string name, byte channel }* then byte 0xFF
    private void HandleMessenger(InPacket p)
    {
        var action = p.ReadByte();
        var args = new MessengerResultArgs { Action = action };
        try
        {
            switch (action)
            {
                case 0:  // Enter
                    args.UserIndex = p.ReadByte();
                    AvatarCodec.DecodeAvatarLook(p);   // consume avatar (name follows it)
                    args.Name = p.ReadString();
                    args.Channel = p.ReadByte();
                    args.Flag = p.ReadBool();          // bNew
                    break;
                case 1:  // SelfEnterResult
                case 2:  // Leave
                    args.UserIndex = p.ReadByte();
                    break;
                case 3:  // Invite
                    args.Name = p.ReadString();        // inviter
                    args.Channel = p.ReadByte();
                    args.MessengerId = p.ReadInt();    // dwSN
                    p.ReadByte();                      // admin
                    break;
                case 4:  // InviteResult
                    args.Name = p.ReadString();
                    args.Flag = p.ReadBool();          // success
                    break;
                case 5:  // Blocked
                    args.Name = p.ReadString();
                    args.Flag = p.ReadBool();          // blocked
                    break;
                case 6:  // Chat
                    args.Chat = p.ReadString();
                    break;
                case 7:  // Avatar
                    args.UserIndex = p.ReadByte();
                    AvatarCodec.DecodeAvatarLook(p);
                    break;
                case 8:  // Migrated
                    while (true)
                    {
                        int idx = p.ReadByte();
                        if (idx == 0xFF) break;        // -1 terminator
                        AvatarCodec.DecodeAvatarLook(p);
                        var name = p.ReadString();
                        var ch = p.ReadByte();
                        args.Migrated.Add((idx, name, ch));
                    }
                    break;
            }
        }
        catch (Exception) { /* trailing fields are best-effort */ }
        OnMessengerResult?.Invoke(args);
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
    public int Money { get; set; }
    /// <summary>Initial inventory delivered in CharacterData (null if it wasn't decoded).
    /// Keyed by <see cref="MapleClaude.Domain.InventoryType"/>; positions are 1-based
    /// (negative for equipped body parts).</summary>
    public Dictionary<InventoryType, List<(short pos, InventoryItem item)>>? Inventory { get; set; }
    /// <summary>Learned skills delivered in CharacterData's SKILLRECORD section.</summary>
    public List<SkillRecord>? Skills { get; set; }
    /// <summary>In-progress quests from CharacterData's QUESTRECORD section.</summary>
    public List<QuestRecordArgs>? Quests { get; set; }
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

public sealed class MobCtrlAckArgs
{
    public int   MobId;
    public short MobCtrlSn;
    public bool  NextAttackPossible;
    public short Mp;
    public byte  NextSkillId;
    public byte  NextSkillLevel;
}

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

public sealed class UserEmotionArgs
{
    /// <summary>The player who triggered the emotion. <c>0</c> is the sentinel for
    /// <see cref="OutHeader.UserEmotionLocal"/> (the local player).</summary>
    public int  CharId;
    /// <summary>Emotion index 0..23 per v95 client <c>s_asEmotionName</c>: 0=default,
    /// 1=hit, 2=smile, 3=troubled, 4=cry, 5=angry, 6=bewildered, 7=stunned, 8=vomit,
    /// 9=oops, 10=cheers, 11=chu, 12=wink, 13=pain, 14=glitter, 15=blaze, 16=shine,
    /// 17=love, 18=despair, 19=hum, 20=bowing, 21=hot, 22=dam, 23=qBlue.</summary>
    public int  Emotion;
    /// <summary>Duration in ms; <c>-1</c> means "use the WZ face's own per-frame total".</summary>
    public int  DurationMs;
    /// <summary>Whether the emotion was triggered by an item option (cash emotion item).</summary>
    public bool ByItemOption;
}

public sealed class CharacterInfoArgs
{
    public int    CharId;
    public byte   Level;
    public short  Job;
    public short  Fame;
    public bool   Married;
    public string Guild    = string.Empty;
    public string Alliance = string.Empty;
    public List<CharInfoPet> Pets = new();
}

public sealed class DropEnterArgs
{
    public int   DropId;
    public bool  IsMoney;
    public int   ItemIdOrAmount;
    public short X, Y;             // landing (resting) position
    public short SourceX, SourceY; // where the drop is tossed from (parabolic arc origin)
    public bool  Animated;         // false for ON_THE_FOOTHOLD (appears in place, no toss)
}

public sealed class DropLeaveArgs { public int DropId; public byte LeaveType; public int PickUpId; }

public sealed class GuildLoadArgs
{
    public int    GuildId;
    public string Name = string.Empty;
    public List<GuildMemberArg> Members = new();
}

public sealed class GuildMemberArg
{
    public int    CharacterId;
    public string Name = string.Empty;
    public int    Job;
    public int    Level;
    public int    Rank;      // 1=Master 2=Jr.Master 3-5=Member
    public bool   Online;
}

public sealed class QuestRecordArgs
{
    public int    QuestId;
    public byte   State;        // 0=None(removed) 1=Perform(active) 2=Complete
    public string Value = string.Empty;
    public bool   IsEx;
}

public sealed class ShopOpenArgs
{
    public int NpcId;
    public List<ShopItemArg> Items = new();
}

public sealed class ShopItemArg
{
    public int   ItemId;
    public int   Price;
    public short Quantity;
}

public sealed class ShopResultArgs
{
    public byte    ResultType;
    public int     Level;
    public string? Message;
}

public sealed class TrunkResultArgs
{
    public byte    ResultType;
    public int     TemplateId;            // OpenTrunkDlg only
    public bool    HasContents;           // true when a trunk listing was decoded
    public byte    SlotCount;
    public int     Money;
    public string? Message;               // ServerMsg only
    public List<TrunkItemArg> Items = new();
}

public sealed class TrunkItemArg
{
    public byte InvType;                  // 1=Equip 2=Consume 3=Install 4=Etc 5=Cash
    public int  PositionInType;           // index within the type block → GetItem position
    public int  ItemId;
    public int  Quantity;
}

public sealed class MessengerResultArgs
{
    public byte    Action;                // MessengerProtocol (Enter=0 … Migrated=8)
    public int     UserIndex;             // Enter / SelfEnterResult / Leave / Avatar slot
    public string? Name;                  // Enter name / Invite inviter / InviteResult / Blocked
    public byte    Channel;               // Enter / Invite channel
    public bool    Flag;                  // Enter isNew / InviteResult success / Blocked blocked
    public int     MessengerId;           // Invite dwSN (use to join)
    public string? Chat;                  // Chat text
    public List<(int Index, string Name, byte Channel)> Migrated = new();
}

public sealed class LootMessageArgs
{
    public bool  IsMoney;
    public int   ItemId;
    public int   Quantity;
    public int   Money;
    /// <summary>The DropPickUp subtype byte; &lt; 0 means a warning
    /// (e.g. inventory full) carried no item/meso body.</summary>
    public sbyte Warning;
}

public sealed class InventoryOpArg
{
    public byte           OpType;   // 0=New 1=Qty 2=Move 3=Del 4=Exp
    public InventoryType  InvType;  // 0=Equipped 1=Equip 2=Consume 3=Install 4=Etc 5=Cash
    public short          Pos;
    public int            ItemId;
    public short          Quantity;
    public short          NewPos;
    public InventoryItem? Item;     // populated for NewItem (op 0)
}

public sealed class UserChatArgs   { public int CharId; public string Text = string.Empty; }

public sealed class PartyMember
{
    public int    CharId;
    public string Name = string.Empty;
    public int    Job;
    public int    Level;
    public int    Channel;
}

public sealed class FriendInfo
{
    public int    FriendId;
    public string Name = string.Empty;
    public byte   Flag;
    public int    Channel;
    /// <summary>Online when on a real game channel (channel id &gt;= 0). The
    /// offline sentinel is CHANNEL_OFFLINE (-2); in-shop is CHANNEL_LOGIN (-1).</summary>
    public bool   Online => Channel >= 0;
}

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
