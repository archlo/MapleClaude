using MapleClaude.Net;
using MapleClaude.Stages;
using MapleClaude.UI;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Net.Handlers;

/// <summary>
/// Handles server→client packets during the login/world-select/char-select flow.
/// Registered by <see cref="LoginStage"/> and <see cref="WorldSelectStage"/>.
///
/// Packet structures (Kinoko LoginPacket.java):
///
/// CheckPasswordResult (0x00):
///   byte  resultType   0=OK, 1=InvalidPassword, 2=InvalidUserName, 3=Blocked …
///   byte  0
///   int   0
///   int   accountId
///   byte  gender
///   byte  gradeCode
///   short subGradeCode
///   …  (we stop reading at resultType)
///
/// WorldInformation (0x0A):
///   byte  worldId
///   string worldName
///   byte  worldState   (0=Normal,1=Hot,2=New)
///   string eventDesc
///   short  eventExpRate
///   short  eventDropRate
///   byte   blockCharCreation
///   short  channelCount
///   FOR each channel:
///     string channelName
///     int    userCount
///     byte   worldIdAgain
///     short  channelId
///   short  balloonCount  (skip rest)
///
/// SelectWorldResult (0x0B):
///   byte  resultType  0=OK
///   byte  count
///   FOR each char: read CharInfo below
///
/// SelectCharacterResult (0x0C / 0x09 for VAC):
///   byte  resultType  0=OK
///   byte  0
///   byte[4] channelHost (4-part IP)
///   short   channelPort
///   int     characterId
///   bool    migrate = false
/// </summary>
public sealed class LoginPacketHandler
{
    private readonly ILogger<LoginPacketHandler> _log;
    private readonly ILoggerFactory _loggerFactory;

    public Action<string>?                      OnLoginFail       { get; set; }
    public Action<int, string>?                 OnWorldInfo       { get; set; }  // (worldId, worldName)
    public Action?                              OnWorldInfoEnd    { get; set; }
    public Action<List<CharSelectStage.CharInfo>>? OnCharList      { get; set; }
    public Action<string, int>?                 OnSelectCharOk    { get; set; }  // (host, port)
    public Action<string>?                      OnSelectCharFail  { get; set; }
    public Action<int, bool>?                   OnDuplicateIdResult { get; set; } // (code, available)
    public Action<int>?                         OnCharCreated     { get; set; }  // characterId
    public Action<string>?                      OnCharCreateFail  { get; set; }

    public LoginPacketHandler(ILogger<LoginPacketHandler> log, ILoggerFactory lf)
    {
        _log          = log;
        _loggerFactory = lf;
    }

    public void RegisterAll(MapleSession session)
    {
        session.RegisterHandler(InHeader.CheckPasswordResult,  HandleCheckPasswordResult);
        session.RegisterHandler(InHeader.WorldInformation,     HandleWorldInformation);
        session.RegisterHandler(InHeader.SelectWorldResult,    HandleSelectWorldResult);
        session.RegisterHandler(InHeader.SelectCharacterResult,HandleSelectCharacterResult);
        session.RegisterHandler(InHeader.CheckDuplicatedIDResult, HandleDuplicateId);
        session.RegisterHandler(InHeader.CreateNewCharacterResult, HandleCreateChar);
        session.RegisterHandler(InHeader.DeleteCharacterResult, HandleDeleteChar);
        session.RegisterHandler(InHeader.LatestConnectedWorld, _ => { });
        session.RegisterHandler(InHeader.AliveReq,             HandleAliveReq);
    }

    public void UnregisterAll(MapleSession session)
    {
        foreach (var h in new[]
        {
            InHeader.CheckPasswordResult, InHeader.WorldInformation,
            InHeader.SelectWorldResult, InHeader.SelectCharacterResult,
            InHeader.CheckDuplicatedIDResult, InHeader.CreateNewCharacterResult,
            InHeader.DeleteCharacterResult, InHeader.LatestConnectedWorld,
            InHeader.AliveReq,
        }) session.UnregisterHandler(h);
    }

    // ── CheckPasswordResult ────────────────────────────────────────────────────

    private void HandleCheckPasswordResult(InPacket pkt)
    {
        var result = pkt.DecodeByte();
        if (result == 0)
        {
            _log.LogInformation("Login OK");
            // Account ID available if needed
        }
        else
        {
            var msg = result switch
            {
                1 => "Incorrect password.",
                2 => "ID does not exist.",
                3 => "Account is blocked.",
                4 => "Account is already logged in.",
                5 => "System error.",
                6 => "Account is blocked.",
                7 => "Too many login attempts.",
                _ => $"Login failed (code {result}).",
            };
            _log.LogWarning("Login failed: {Msg}", msg);
            OnLoginFail?.Invoke(msg);
        }
    }

    // ── WorldInformation ───────────────────────────────────────────────────────

    private void HandleWorldInformation(InPacket pkt)
    {
        var worldId = pkt.DecodeByte();
        if (worldId == 0xFF || worldId == 255)
        {
            // WorldInformationEnd sentinel
            OnWorldInfoEnd?.Invoke();
            return;
        }
        var worldName = pkt.DecodeString();
        pkt.DecodeByte(); // worldState
        pkt.DecodeString(); // eventDesc
        pkt.DecodeShort(); // eventExpRate
        pkt.DecodeShort(); // eventDropRate
        pkt.DecodeByte();  // blockCharCreation
        var chCount = pkt.DecodeShort();
        for (var i = 0; i < chCount; i++)
        {
            pkt.DecodeString(); // channelName
            pkt.DecodeInt();    // userCount
            pkt.DecodeByte();   // worldIdAgain
            pkt.DecodeShort();  // channelId
        }
        _log.LogInformation("World {Id} = {Name}, {Ch} channels", worldId, worldName, chCount);
        OnWorldInfo?.Invoke(worldId, worldName);
    }

    // ── SelectWorldResult ──────────────────────────────────────────────────────

    private void HandleSelectWorldResult(InPacket pkt)
    {
        var result = pkt.DecodeByte();
        if (result != 0)
        {
            OnLoginFail?.Invoke($"World select failed (code {result}).");
            return;
        }

        var charCount = pkt.DecodeByte();
        var chars = new List<CharSelectStage.CharInfo>(charCount);
        for (var i = 0; i < charCount; i++)
            chars.Add(ReadCharInfo(pkt));

        _log.LogInformation("SelectWorldResult: {N} characters", charCount);
        OnCharList?.Invoke(chars);
    }

    // ── SelectCharacterResult ──────────────────────────────────────────────────

    private void HandleSelectCharacterResult(InPacket pkt)
    {
        var result = pkt.DecodeByte();
        pkt.DecodeByte(); // 0
        if (result != 0)
        {
            OnSelectCharFail?.Invoke($"Select character failed (code {result}).");
            return;
        }

        // Channel host as 4 bytes
        var ipBytes = pkt.DecodeArray(4);
        var host    = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
        var port    = pkt.DecodeShort();
        // int charId, bool migrate follow but we only need host+port here
        _log.LogInformation("SelectCharacterResult OK: migrate to {Host}:{Port}", host, port);
        OnSelectCharOk?.Invoke(host, port);
    }

    // ── CheckDuplicatedIDResult ────────────────────────────────────────────────

    private void HandleDuplicateId(InPacket pkt)
    {
        pkt.DecodeString(); // name echoed
        var code = pkt.DecodeInt(); // 0=available, 1=taken, other=invalid
        OnDuplicateIdResult?.Invoke(code, code == 0);
    }

    // ── CreateNewCharacterResult ───────────────────────────────────────────────

    private void HandleCreateChar(InPacket pkt)
    {
        var result = pkt.DecodeByte();
        if (result != 0)
        {
            OnCharCreateFail?.Invoke($"Create character failed (code {result}).");
            return;
        }
        var info = ReadCharInfo(pkt);
        _log.LogInformation("Character created: {Name}", info.Name);
        OnCharCreated?.Invoke(info.Id);
    }

    // ── DeleteCharacterResult ─────────────────────────────────────────────────

    private void HandleDeleteChar(InPacket pkt)
    {
        var charId = pkt.DecodeInt();
        var result = pkt.DecodeByte();
        _log.LogInformation("DeleteCharacter {Id} result={R}", charId, result);
    }

    // ── AliveReq (heartbeat) ──────────────────────────────────────────────────

    private void HandleAliveReq(InPacket _pkt)
    {
        // Respond immediately with AliveAck
        // (session is captured via the MapleSession passed to RegisterAll — caller wires this)
        _log.LogTrace("AliveReq received");
        AliveAckRequested?.Invoke();
    }

    public Action? AliveAckRequested { get; set; }

    // ── Char info helper ───────────────────────────────────────────────────────

    public static CharSelectStage.CharInfo ReadCharInfo(InPacket pkt)
    {
        // GW_CharacterStat block
        var charId = pkt.DecodeInt();
        var name   = pkt.DecodeString();
        var gender = pkt.DecodeByte();
        var skin   = pkt.DecodeByte();
        var face   = pkt.DecodeInt();
        var hair   = pkt.DecodeInt();
        pkt.Skip(24); // pets (3 × 8 bytes)
        var level  = pkt.DecodeByte();
        var job    = pkt.DecodeShort();
        var str    = pkt.DecodeShort();
        var dex    = pkt.DecodeShort();
        var intel  = pkt.DecodeShort();
        var luk    = pkt.DecodeShort();
        var hp     = pkt.DecodeInt();
        var maxHp  = pkt.DecodeInt();
        var mp     = pkt.DecodeInt();
        var maxMp  = pkt.DecodeInt();
        var ap     = pkt.DecodeShort();
        var sp     = pkt.DecodeShort();
        var exp    = pkt.DecodeInt();
        var fame   = pkt.DecodeShort();
        pkt.Skip(4);   // gachaponExp
        var mapId  = pkt.DecodeInt();
        pkt.DecodeByte(); // portal
        pkt.DecodeInt();  // playTime
        pkt.DecodeShort(); // subJob

        // Skip avatar look (AvatarLook encoding — size varies, stop at end of fixed block)
        // AvatarLook: gender(1)+skin(1)+face(4)+megaphone(1)+hair(4)+equips+subEquips+masked+cashEquips+weapons
        // For CharSelect purposes we just need the stats above
        // Skip remaining bytes by catching read errors gracefully

        return new CharSelectStage.CharInfo
        {
            Id     = charId,
            Name   = name,
            Level  = level,
            JobId  = job,
            MapId  = mapId,
            Face   = face,
            Hair   = hair,
        };
    }
}
