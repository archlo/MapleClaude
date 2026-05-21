using MapleClaude.Net;
using MapleClaude.UI.Game;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Net.Handlers;

/// <summary>
/// Handles server→client packets while in-game (after SetField).
/// Registered and unregistered by <see cref="Stages.GameStage"/>.
///
/// Key packets handled:
///   SetField (0x8D)      — initial character/map data on login/warp
///   StatChanged (0x1E)   — HP/MP/EXP/Level/AP/SP changes
///   FuncKeyMappedInit (0x18E) — server-side keybindings
///   QuickslotMappedInit  — quickslot bindings
///   UserChat (0xB5)      — chat from other players
///   UserEnterField (0xB3) — player enters same map
///   UserLeaveField (0xB4) — player leaves map
///   AliveReq             — heartbeat
/// </summary>
public sealed class GamePacketHandler
{
    private readonly ILogger<GamePacketHandler> _log;

    // Callbacks wired by GameStage
    public Action<CharStats>?                OnStatChanged    { get; set; }
    public Action<string, string, int>?      OnUserChat       { get; set; }   // (name, msg, type)
    public Action<int, string, int, int>?    OnUserEnterField { get; set; }   // (objId, name, x, y)
    public Action<int>?                      OnUserLeaveField { get; set; }   // objId
    public Action?                           OnAliveReq       { get; set; }
    public Action<KeyConfig>?                OnFuncKeyInit    { get; set; }
    public Action<string>?                   OnMapChanged     { get; set; }
    public Action<int, string>?              OnSystemMsg      { get; set; }   // (type, text)

    public GamePacketHandler(ILogger<GamePacketHandler> log)
    {
        _log = log;
    }

    public void RegisterAll(MapleSession session)
    {
        session.RegisterHandler(InHeader.SetField,            HandleSetField);
        session.RegisterHandler(InHeader.StatChanged,         HandleStatChanged);
        session.RegisterHandler(InHeader.FuncKeyMappedInit,   HandleFuncKeyMappedInit);
        session.RegisterHandler(InHeader.QuickslotMappedInit, HandleQuickslotInit);
        session.RegisterHandler(InHeader.UserChat,            HandleUserChat);
        session.RegisterHandler(InHeader.UserEnterField,      HandleUserEnterField);
        session.RegisterHandler(InHeader.UserLeaveField,      HandleUserLeaveField);
        session.RegisterHandler(InHeader.AliveReq,            _ => OnAliveReq?.Invoke());
        session.RegisterHandler(InHeader.Message,             HandleMessage);
        session.RegisterHandler(InHeader.NotifyLevelUp,       HandleLevelUp);
        session.RegisterHandler(InHeader.TemporaryStatSet,    HandleTempStatSet);
        session.RegisterHandler(InHeader.TemporaryStatReset,  _ => { });
        session.RegisterHandler(InHeader.ChangeSkillRecordResult, HandleSkillRecord);
        session.RegisterHandler(InHeader.UserHP,              HandleUserHP);
    }

    public void UnregisterAll(MapleSession session)
    {
        foreach (var h in new[]
        {
            InHeader.SetField, InHeader.StatChanged, InHeader.FuncKeyMappedInit,
            InHeader.QuickslotMappedInit, InHeader.UserChat, InHeader.UserEnterField,
            InHeader.UserLeaveField, InHeader.AliveReq, InHeader.Message,
            InHeader.NotifyLevelUp, InHeader.TemporaryStatSet, InHeader.TemporaryStatReset,
            InHeader.ChangeSkillRecordResult, InHeader.UserHP,
        }) session.UnregisterHandler(h);
    }

    // ── SetField ──────────────────────────────────────────────────────────────

    private void HandleSetField(InPacket pkt)
    {
        // SetField (Kinoko StagePacket.setField):
        //   int   channelId
        //   byte  unkn1 = 1
        //   bool  isMigrate
        //   short fieldKey
        //   if isMigrate: write full character data
        //   int   fieldId (mapId)
        //   byte  portal
        //   int   playTime
        //   long  serverTime
        pkt.DecodeInt();  // channelId
        var unkn = pkt.DecodeByte();
        var isMigrate = pkt.DecodeBool();
        pkt.DecodeShort(); // fieldKey

        if (isMigrate)
        {
            // Parse CharStats block embedded in SetField
            var stats = ParseCharStatsBlock(pkt);
            _log.LogInformation("SetField migrate: {Name} Lv{Level} Map{MapId}",
                stats.Name, stats.Level, stats.MapId);
            OnStatChanged?.Invoke(stats);
            OnMapChanged?.Invoke(stats.MapId.ToString());
        }
        else
        {
            var mapId  = pkt.DecodeInt();
            var portal = pkt.DecodeByte();
            _log.LogInformation("SetField warp: mapId={MapId} portal={Portal}", mapId, portal);
            OnMapChanged?.Invoke(mapId.ToString());
        }
    }

    // ── StatChanged ───────────────────────────────────────────────────────────

    private void HandleStatChanged(InPacket pkt)
    {
        pkt.DecodeBool(); // itemReaction
        var mask = (long)pkt.DecodeInt() << 32 | (uint)pkt.DecodeInt();
        var stats = new CharStats();

        // Decode stat mask bits (GW_CharacterStat order)
        if ((mask & 0x0000001) != 0) stats.Skin  = pkt.DecodeByte();
        if ((mask & 0x0000002) != 0) stats.Face  = pkt.DecodeInt();
        if ((mask & 0x0000004) != 0) stats.Hair  = pkt.DecodeInt();
        if ((mask & 0x0000010) != 0) stats.Level = pkt.DecodeByte();
        if ((mask & 0x0000020) != 0) stats.JobId = pkt.DecodeShort();
        if ((mask & 0x0000040) != 0) stats.Str   = pkt.DecodeShort();
        if ((mask & 0x0000080) != 0) stats.Dex   = pkt.DecodeShort();
        if ((mask & 0x0000100) != 0) stats.Int   = pkt.DecodeShort();
        if ((mask & 0x0000200) != 0) stats.Luk   = pkt.DecodeShort();
        if ((mask & 0x0000400) != 0) stats.Hp    = pkt.DecodeInt();
        if ((mask & 0x0000800) != 0) stats.MaxHp = pkt.DecodeInt();
        if ((mask & 0x0001000) != 0) stats.Mp    = pkt.DecodeInt();
        if ((mask & 0x0002000) != 0) stats.MaxMp = pkt.DecodeInt();
        if ((mask & 0x0004000) != 0) stats.AP    = pkt.DecodeShort();
        if ((mask & 0x0008000) != 0) stats.SP    = pkt.DecodeShort();
        if ((mask & 0x0010000) != 0) stats.Exp   = pkt.DecodeInt();
        if ((mask & 0x0020000) != 0) stats.Fame  = pkt.DecodeShort();
        if ((mask & 0x0200000) != 0) stats.Meso  = pkt.DecodeInt();

        _log.LogDebug("StatChanged mask=0x{Mask:X} HP={HP}/{MHP} Lv={Lv}", mask, stats.Hp, stats.MaxHp, stats.Level);
        OnStatChanged?.Invoke(stats);
    }

    // ── FuncKeyMappedInit ─────────────────────────────────────────────────────

    private void HandleFuncKeyMappedInit(InPacket pkt)
    {
        // 90 entries: each is byte type + int actionId
        var entries = new List<(int keyIndex, int type, int actionId)>(90);
        for (var i = 0; i < 90; i++)
        {
            var type     = pkt.DecodeByte();
            var actionId = pkt.DecodeInt();
            entries.Add((i, type, actionId));
        }
        _log.LogInformation("FuncKeyMappedInit: {Count} bindings", entries.Count(e => e.type != 0));
        // Deliver to KeyConfig via callback (GameStage wires this)
        FuncKeyEntries = entries;
        OnFuncKeyInit?.Invoke(null!); // GameStage replaces null with its _keyConfig
    }

    /// <summary>Stored after FuncKeyMappedInit — GameStage reads and applies these.</summary>
    public List<(int keyIndex, int type, int actionId)>? FuncKeyEntries { get; private set; }

    // ── QuickslotMappedInit ───────────────────────────────────────────────────

    private void HandleQuickslotInit(InPacket pkt)
    {
        // 28 entries (quickslots 0-27): each int = actionId
        for (var i = 0; i < 28; i++) pkt.DecodeInt();
        _log.LogDebug("QuickslotMappedInit received");
    }

    // ── UserChat ──────────────────────────────────────────────────────────────

    private void HandleUserChat(InPacket pkt)
    {
        pkt.DecodeInt(); // charId
        pkt.DecodeBool(); // admin
        var text   = pkt.DecodeString();
        var isShow = pkt.DecodeBool(); // show balloon?
        OnUserChat?.Invoke(string.Empty, text, 0);
    }

    // ── UserEnterField / LeaveField ───────────────────────────────────────────

    private void HandleUserEnterField(InPacket pkt)
    {
        var objId = pkt.DecodeInt();
        // Skip the full RemoteCharInfo encoding — just log
        _log.LogDebug("UserEnterField objId={Id}", objId);
        OnUserEnterField?.Invoke(objId, string.Empty, 0, 0);
    }

    private void HandleUserLeaveField(InPacket pkt)
    {
        var objId = pkt.DecodeInt();
        _log.LogDebug("UserLeaveField objId={Id}", objId);
        OnUserLeaveField?.Invoke(objId);
    }

    // ── Message ───────────────────────────────────────────────────────────────

    private void HandleMessage(InPacket pkt)
    {
        var type = pkt.DecodeByte();
        // Type 0 = DropPickUpMessage, 3 = QuestRecord, 5 = NotEnoughMoney, …
        // Type 18 = 'You got N EXP'
        _log.LogDebug("Message type={T}", type);
        OnSystemMsg?.Invoke(type, string.Empty);
    }

    // ── NotifyLevelUp ─────────────────────────────────────────────────────────

    private void HandleLevelUp(InPacket pkt)
    {
        var charId = pkt.DecodeInt();
        _log.LogInformation("NotifyLevelUp charId={Id}", charId);
        // If it's our own character, will be caught via StatChanged with Level bit set
    }

    // ── TemporaryStatSet ──────────────────────────────────────────────────────

    private void HandleTempStatSet(InPacket pkt)
    {
        // Complex buff data — just log receipt
        _log.LogDebug("TemporaryStatSet received");
    }

    // ── ChangeSkillRecordResult ───────────────────────────────────────────────

    private void HandleSkillRecord(InPacket pkt)
    {
        pkt.DecodeBool(); // itemReaction
        var count = pkt.DecodeShort();
        for (var i = 0; i < count; i++)
        {
            var skillId   = pkt.DecodeInt();
            var skillLevel = pkt.DecodeByte();
            var masterLevel = pkt.DecodeByte();
            pkt.DecodeLong(); // expiry
            _log.LogDebug("SkillRecord: skill={Skill} lv={Lv}", skillId, skillLevel);
        }
    }

    // ── UserHP ────────────────────────────────────────────────────────────────

    private void HandleUserHP(InPacket pkt)
    {
        var objId = pkt.DecodeInt();
        var hp    = pkt.DecodeInt();
        var maxHp = pkt.DecodeInt();
        _log.LogDebug("UserHP obj={Id} {HP}/{MHP}", objId, hp, maxHp);
    }

    // ── CharStats full block (from SetField isMigrate path) ──────────────────

    private static CharStats ParseCharStatsBlock(InPacket pkt)
    {
        var id    = pkt.DecodeInt();
        var name  = pkt.DecodeString();
        var gender = pkt.DecodeByte();
        var skin  = pkt.DecodeByte();
        var face  = pkt.DecodeInt();
        var hair  = pkt.DecodeInt();
        pkt.Skip(24); // 3 pet slots
        var level = pkt.DecodeByte();
        var job   = pkt.DecodeShort();
        var str   = pkt.DecodeShort();
        var dex   = pkt.DecodeShort();
        var intel = pkt.DecodeShort();
        var luk   = pkt.DecodeShort();
        var hp    = pkt.DecodeInt();
        var maxHp = pkt.DecodeInt();
        var mp    = pkt.DecodeInt();
        var maxMp = pkt.DecodeInt();
        var ap    = pkt.DecodeShort();
        var sp    = pkt.DecodeShort();
        var exp   = pkt.DecodeInt();
        var fame  = pkt.DecodeShort();
        pkt.Skip(4);  // gachaponExp
        var mapId = pkt.DecodeInt();
        // (skip remainder of fixed block and look blocks)

        return new CharStats
        {
            Id = id, Name = name, Level = level, JobId = job,
            Str = str, Dex = dex, Int = intel, Luk = luk,
            Hp = hp, MaxHp = maxHp, Mp = mp, MaxMp = maxMp,
            AP = ap, SP = sp, Exp = exp, Fame = fame,
            MapId = mapId,
        };
    }
}

/// <summary>Character stat snapshot — populated by StatChanged and SetField packets.</summary>
public sealed class CharStats
{
    public int    Id;
    public string Name   = string.Empty;
    public int    Level  = 1;
    public int    JobId  = 0;
    public int    Skin   = 0;
    public int    Face   = 20000;
    public int    Hair   = 30000;
    public short  Str    = 4;
    public short  Dex    = 4;
    public short  Int    = 4;
    public short  Luk    = 4;
    public int    Hp     = 50;
    public int    MaxHp  = 50;
    public int    Mp     = 5;
    public int    MaxMp  = 5;
    public short  AP     = 0;
    public short  SP     = 0;
    public int    Exp    = 0;
    public short  Fame   = 0;
    public int    Meso   = 0;
    public int    MapId  = 100000000;
}
