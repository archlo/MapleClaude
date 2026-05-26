using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Character-profile wire-format tests: the C→S <c>UserCharacterInfoRequest</c>(109) sent on a
/// double-click, and the S→C <c>CharacterInfo</c>(61) decode. Field order mirrors upstream Kinoko
/// (<c>UserHandler.handleUserCharacterInfoRequest</c> / <c>WvsContext.characterInfo</c>) byte-for-byte
/// through the pet loop.
/// </summary>
public class CharacterInfoTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    // A realistic CharacterInfo(61) body: core fields + one active pet + the (unused) trailing
    // taming-mob / wishlist / medal / chair blocks the server always appends.
    private static OutPacket BuildResponse()
    {
        var p = OutPacket.Of((short)OutHeader.CharacterInfo);
        p.WriteInt(12345);          // dwCharacterId
        p.WriteByte(85);            // nLevel
        p.WriteShort(412);          // nJob (I/L Wizard II)
        p.WriteShort(150);          // nPOP (fame)
        p.WriteByte(0);             // bIsMarried
        p.WriteString("Heroes");    // sCommunity (guild)
        p.WriteString("Legends");   // sAlliance
        p.WriteByte(0);             // bMedalInfo
        // one active pet
        p.WriteByte(1);             // bPetActivated
        p.WriteInt(5000000);        // dwTemplateId
        p.WriteString("Fluffy");    // sName
        p.WriteByte(10);            // nLevel
        p.WriteShort(30000);        // nTameness
        p.WriteByte(100);           // nRepleteness
        p.WriteShort(7);            // usPetSkill
        p.WriteInt(0);              // nItemID (pet wear)
        p.WriteByte(0);             // pet-loop terminator
        // trailing blocks (decoder stops after pets; included so the body is realistic)
        p.WriteByte(0);             // bHasTamingMob
        p.WriteByte(0);             // wishCount
        p.WriteInt(0);              // nEquipedMedalID
        p.WriteShort(0);            // titleQuestCount
        p.WriteInt(0);             // chairCount
        return p;
    }

    // ── Opcode values ───────────────────────────────────────────────────────────

    [Fact]
    public void RequestOpcode_HasCanonicalValue() =>
        ((int)InHeader.UserCharacterInfoRequest).Should().Be(109);

    [Fact]
    public void ResponseOpcode_HasCanonicalValue() =>
        ((int)OutHeader.CharacterInfo).Should().Be(61);

    // ── Request (C→S) ─────────────────────────────────────────────────────────────

    [Fact]
    public void UserCharacterInfoRequest_Encodes_UpdateTimeCharIdPetFlag()
    {
        var p = new InPacket(GameSender.UserCharacterInfoRequest(778899).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserCharacterInfoRequest);
        p.ReadInt().Should().Be(0);         // update_time
        p.ReadInt().Should().Be(778899);    // dwCharacterId
        p.ReadByte().Should().Be(0);        // bPetInfo
        p.Remaining.Should().Be(0);
    }

    // ── Response (S→C) decode ─────────────────────────────────────────────────────

    [Fact]
    public void CharacterInfo_Decodes_CoreFieldsAndPet()
    {
        var p = new InPacket(BuildResponse().ToArray());
        p.ReadShort().Should().Be((short)OutHeader.CharacterInfo); // consume opcode

        var a = FieldHandlers.DecodeCharacterInfo(p);

        a.CharId.Should().Be(12345);
        a.Level.Should().Be(85);
        a.Job.Should().Be(412);
        a.Fame.Should().Be(150);
        a.Married.Should().BeFalse();
        a.Guild.Should().Be("Heroes");
        a.Alliance.Should().Be("Legends");

        a.Pets.Should().HaveCount(1);
        a.Pets[0].TemplateId.Should().Be(5000000);
        a.Pets[0].Name.Should().Be("Fluffy");
        a.Pets[0].Level.Should().Be(10);
        a.Pets[0].Tameness.Should().Be(30000);
        a.Pets[0].Repleteness.Should().Be(100);
        a.Pets[0].PetSkill.Should().Be(7);
        a.Pets[0].PetWear.Should().Be(0);
    }

    [Fact]
    public void CharacterInfo_RegisteredHandler_FiresEvent()
    {
        var (fh, router) = NewHandlers();
        CharacterInfoArgs? captured = null;
        fh.OnCharacterInfo += a => captured = a;

        router.Dispatch(new InPacket(BuildResponse().ToArray()), session: null!);

        captured.Should().NotBeNull();
        captured!.CharId.Should().Be(12345);
        captured.Guild.Should().Be("Heroes");
        captured.Pets.Should().ContainSingle().Which.Name.Should().Be("Fluffy");
    }

    [Fact]
    public void CharacterInfo_NoPets_DecodesEmptyPetList()
    {
        var p = OutPacket.Of((short)OutHeader.CharacterInfo);
        p.WriteInt(7);              // charId
        p.WriteByte(30);           // level
        p.WriteShort(0);           // job (beginner)
        p.WriteShort(-5);          // fame (signed!)
        p.WriteByte(0);            // married
        p.WriteString("");         // guild
        p.WriteString("");         // alliance
        p.WriteByte(0);            // medalInfo
        p.WriteByte(0);            // pet-loop terminator (no pets)

        var ip = new InPacket(p.ToArray());
        ip.ReadShort();            // consume opcode
        var a = FieldHandlers.DecodeCharacterInfo(ip);

        a.CharId.Should().Be(7);
        a.Level.Should().Be(30);
        a.Fame.Should().Be(-5);
        a.Guild.Should().BeEmpty();
        a.Pets.Should().BeEmpty();
    }
}
