using MapleClaude.Domain;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Net.Handlers;

/// <summary>
/// Decoders for the login-server S→C opcodes. Each handler updates session
/// state and raises a typed event for the active <c>Stage</c> to consume.
/// Wire formats mirror upstream Kinoko's <c>LoginPacket</c> byte-for-byte.
/// </summary>
public sealed class LoginHandlers
{
    private readonly ILogger<LoginHandlers> _logger;
    private readonly ClientSession _session;

    public event Action<CheckPasswordResultArgs>? OnCheckPasswordResult;
    public event Action<List<WorldInfo>>? OnWorldListComplete;
    public event Action<byte>? OnLatestConnectedWorld;
    public event Action<SelectWorldResultArgs>? OnSelectWorldResult;
    public event Action<CheckDuplicatedIdArgs>? OnCheckDuplicatedIdResult;
    public event Action<CreateNewCharacterResultArgs>? OnCreateCharacterResult;
    public event Action<DeleteCharacterArgs>? OnDeleteCharacterResult;
    public event Action<SelectCharacterResultArgs>? OnSelectCharacterResult;

    /// <summary>Fired when the login server rejects the secondary password (PIC) sent in a
    /// CheckSPWRequest — i.e. the entered PIC was wrong. A correct PIC instead produces a normal
    /// <see cref="OnSelectCharacterResult"/> (the server migrates on success).</summary>
    public event Action? OnCheckSpwFailed;

    public LoginHandlers(ILogger<LoginHandlers> logger, ClientSession session)
    {
        _logger = logger;
        _session = session;
    }

    /// <summary>Subscribe to every login-server opcode on the router.</summary>
    public void Register(PacketRouter router)
    {
        router.Register(OutHeader.CheckPasswordResult, (p, s) => HandleCheckPasswordResult(p));
        router.Register(OutHeader.WorldInformation, (p, s) => HandleWorldInformation(p));
        router.Register(OutHeader.LatestConnectedWorld, (p, s) => HandleLatestConnectedWorld(p));
        router.Register(OutHeader.CheckUserLimitResult, (p, s) => HandleCheckUserLimitResult(p));
        router.Register(OutHeader.SelectWorldResult, (p, s) => HandleSelectWorldResult(p));
        router.Register(OutHeader.CheckDuplicatedIDResult, (p, s) => HandleCheckDuplicatedIdResult(p));
        router.Register(OutHeader.CreateNewCharacterResult, (p, s) => HandleCreateNewCharacterResult(p));
        router.Register(OutHeader.DeleteCharacterResult, (p, s) => HandleDeleteCharacterResult(p));
        router.Register(OutHeader.SelectCharacterResult, (p, s) => HandleSelectCharacterResult(p, byVac: false));
        router.Register(OutHeader.SelectCharacterByVACResult, (p, s) => HandleSelectCharacterResult(p, byVac: true));
        router.Register(OutHeader.CheckSPWResult, (p, s) => HandleCheckSpwResult(p));
        router.Register(OutHeader.AliveReq, (p, s) => HandleAliveReq(s));
    }

    private void HandleCheckPasswordResult(InPacket p)
    {
        var result = p.ReadByte();
        if (result != 0)
        {
            // Failure path: byte result + byte(0) + int(0); blocked variant adds byte+8 bytes.
            _logger.LogWarning("CheckPasswordResult failure code={Code}", result);
            OnCheckPasswordResult?.Invoke(new CheckPasswordResultArgs { Success = false, ResultCode = result });
            return;
        }
        p.ReadByte();           // 0/1
        p.ReadInt();            // 0
        var acc = _session.Account;
        acc.AccountId = p.ReadInt();
        acc.Gender = p.ReadByte();
        acc.GradeCode = p.ReadByte();
        acc.SubGradeCode = p.ReadShort();
        acc.CountryId = p.ReadByte();
        acc.NexonClubId = p.ReadString();
        acc.PurchaseExp = p.ReadByte();
        acc.ChatBlockReason = p.ReadByte();
        acc.ChatUnblockDate = p.ReadLong();
        acc.RegisterDate = p.ReadLong();
        acc.CharacterSlotCount = p.ReadInt();
        acc.SkipPinCode = p.ReadByte() != 0;
        acc.LoginOpt = p.ReadByte();
        acc.ClientKey = p.ReadBytes(8);

        _logger.LogInformation("CheckPasswordResult OK accountId={Id} slots={Slots} skipPin={Skip}",
            acc.AccountId, acc.CharacterSlotCount, acc.SkipPinCode);
        OnCheckPasswordResult?.Invoke(new CheckPasswordResultArgs { Success = true, ResultCode = 0 });
    }

    // Per Kinoko: each WorldInformation packet carries ONE world + its channels;
    // the server sends one packet per world followed by a terminator packet whose
    // first byte is -1 (signed). The client accumulates worlds until terminator.
    private void HandleWorldInformation(InPacket p)
    {
        var worldId = p.ReadSByte();
        if (worldId < 0)
        {
            _logger.LogInformation("WorldInformation terminator received — {N} worlds", _session.Worlds.Count);
            OnWorldListComplete?.Invoke(_session.Worlds);
            return;
        }
        var w = new WorldInfo
        {
            WorldId = worldId,
            Name = p.ReadString(),
            State = p.ReadByte(),
            EventDescription = p.ReadString(),
            EventExpRate = p.ReadShort(),
            EventDropRate = p.ReadShort(),
            BlockCharCreation = p.ReadByte(),
        };
        var channelCount = p.ReadByte();
        for (var i = 0; i < channelCount; i++)
        {
            w.Channels.Add(new ChannelInfo
            {
                Name = p.ReadString(),
                UserCount = p.ReadInt(),
                WorldId = p.ReadByte(),
                ChannelId = p.ReadByte(),
                Adult = p.ReadByte() != 0,
            });
        }
        w.BalloonCount = p.ReadShort();
        _session.Worlds.Add(w);
    }

    private void HandleLatestConnectedWorld(InPacket p)
    {
        var worldId = (byte)p.ReadInt();
        OnLatestConnectedWorld?.Invoke(worldId);
    }

    private void HandleCheckUserLimitResult(InPacket p)
    {
        var over = p.ReadByte();
        var populate = p.ReadByte();
        _logger.LogDebug("CheckUserLimitResult over={Over} populate={Pop}", over, populate);
    }

    private void HandleSelectWorldResult(InPacket p)
    {
        var result = p.ReadByte();
        if (result != 0)
        {
            _logger.LogWarning("SelectWorldResult failure code={Code}", result);
            OnSelectWorldResult?.Invoke(new SelectWorldResultArgs { Success = false, ResultCode = result });
            return;
        }

        var characters = new List<CharacterEntry>();
        var count = p.ReadByte();
        for (var i = 0; i < count; i++)
        {
            var stat = AvatarCodec.DecodeCharacterStat(p);
            var look = AvatarCodec.DecodeAvatarLook(p);
            var onFamily = p.ReadByte() != 0;
            CharacterRank? rank = null;
            if (p.ReadByte() != 0)
            {
                rank = new CharacterRank
                {
                    WorldRank = p.ReadInt(),
                    WorldRankMove = p.ReadInt(),
                    JobRank = p.ReadInt(),
                    JobRankMove = p.ReadInt(),
                };
            }
            characters.Add(new CharacterEntry { Stat = stat, Look = look, OnFamily = onFamily, Rank = rank });
        }
        var loginOpt = p.ReadByte();
        var slotCount = p.ReadInt();
        var buyCharCount = p.ReadInt();

        _session.Account.LoginOpt = loginOpt;
        _session.Account.CharacterSlotCount = slotCount;
        _session.Characters.Clear();
        _session.Characters.AddRange(characters);

        _logger.LogInformation("SelectWorldResult OK {N} chars (slots={Slots}, buy={Buy})",
            characters.Count, slotCount, buyCharCount);
        OnSelectWorldResult?.Invoke(new SelectWorldResultArgs
        {
            Success = true,
            ResultCode = 0,
            Characters = characters,
        });
    }

    private void HandleCheckDuplicatedIdResult(InPacket p)
    {
        var name = p.ReadString();
        var code = p.ReadByte();
        OnCheckDuplicatedIdResult?.Invoke(new CheckDuplicatedIdArgs { Name = name, ResultCode = code });
    }

    private void HandleCreateNewCharacterResult(InPacket p)
    {
        var code = p.ReadByte();
        CharacterEntry? entry = null;
        if (code == 0)
        {
            var stat = AvatarCodec.DecodeCharacterStat(p);
            var look = AvatarCodec.DecodeAvatarLook(p);
            entry = new CharacterEntry { Stat = stat, Look = look };
            _session.Characters.Insert(0, entry);
        }
        OnCreateCharacterResult?.Invoke(new CreateNewCharacterResultArgs
        {
            Success = code == 0,
            ResultCode = code,
            Entry = entry,
        });
    }

    private void HandleDeleteCharacterResult(InPacket p)
    {
        var charId = p.ReadInt();
        var code = p.ReadByte();
        if (code == 0)
        {
            _session.Characters.RemoveAll(c => c.Stat.CharacterId == charId);
        }
        OnDeleteCharacterResult?.Invoke(new DeleteCharacterArgs
        {
            Success = code == 0,
            ResultCode = code,
            CharacterId = charId,
        });
    }

    private void HandleCheckSpwResult(InPacket p)
    {
        // checkSecondaryPasswordResult: one ignored byte. The server sends this ONLY on failure
        // (wrong PIC); a correct PIC migrates the client via SelectCharacterResult instead.
        p.ReadByte(); // ignored (-1)
        _logger.LogInformation("CheckSPWResult — secondary password (PIC) rejected");
        OnCheckSpwFailed?.Invoke();
    }

    private void HandleSelectCharacterResult(InPacket p, bool byVac)
    {
        var code = p.ReadByte();
        if (code != 0)
        {
            p.ReadByte(); // "trouble logging in"
            _logger.LogWarning("SelectCharacterResult failure code={Code}", code);
            OnSelectCharacterResult?.Invoke(new SelectCharacterResultArgs { Success = false, ResultCode = code });
            return;
        }
        p.ReadByte(); // 0
        var host = p.ReadBytes(4);
        var port = (ushort)p.ReadShort();
        var characterId = p.ReadInt();
        var authenCode = p.ReadByte();
        var ulPremiumArg = p.ReadInt();
        _logger.LogInformation("SelectCharacterResult OK byVac={ByVac} charId={Cid} host={Host} port={Port}",
            byVac, characterId, string.Join(".", host), port);
        OnSelectCharacterResult?.Invoke(new SelectCharacterResultArgs
        {
            Success = true,
            ResultCode = 0,
            ChannelHost = host,
            ChannelPort = port,
            CharacterId = characterId,
            AuthenCode = authenCode,
            PremiumArgument = ulPremiumArg,
        });
    }

    private void HandleAliveReq(ClientSession s)
    {
        var ack = OutPacket.Of(InHeader.AliveAck);
        s.Send(ack);
        _logger.LogDebug("AliveAck sent");
    }
}

public sealed class CheckPasswordResultArgs
{
    public bool Success { get; init; }
    public byte ResultCode { get; init; }
}

public sealed class SelectWorldResultArgs
{
    public bool Success { get; init; }
    public byte ResultCode { get; init; }
    public List<CharacterEntry> Characters { get; init; } = new();
}

public sealed class CheckDuplicatedIdArgs
{
    public required string Name { get; init; }
    public byte ResultCode { get; init; }
}

public sealed class CreateNewCharacterResultArgs
{
    public bool Success { get; init; }
    public byte ResultCode { get; init; }
    public CharacterEntry? Entry { get; init; }
}

public sealed class DeleteCharacterArgs
{
    public bool Success { get; init; }
    public byte ResultCode { get; init; }
    public int CharacterId { get; init; }
}

public sealed class SelectCharacterResultArgs
{
    public bool Success { get; init; }
    public byte ResultCode { get; init; }
    public byte[]? ChannelHost { get; init; }
    public ushort ChannelPort { get; init; }
    public int CharacterId { get; init; }
    public byte AuthenCode { get; init; }
    public int PremiumArgument { get; init; }
}
