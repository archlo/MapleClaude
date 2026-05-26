using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 20 quest wire-format tests: Message QuestRecord(1) / QuestRecordEx(11)
/// decode → OnQuestRecord, and the UserQuestRequest(119) accept/complete/resign
/// encoders.
/// </summary>
public class QuestTests
{
    private static (FieldHandlers fh, PacketRouter router) NewHandlers()
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        return (fh, router);
    }

    private static void Dispatch(PacketRouter router, OutPacket p) =>
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

    [Fact]
    public void UserQuestRequest_HasCanonicalValue() =>
        ((int)InHeader.UserQuestRequest).Should().Be(119);

    // ── QuestRecord (Message 38, type 1) ─────────────────────────────────────────

    [Fact]
    public void QuestRecord_Perform_DecodesProgressValue()
    {
        var (fh, router) = NewHandlers();
        QuestRecordArgs? captured = null;
        fh.OnQuestRecord += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(1);          // MessageType.QuestRecord
        p.WriteShort(2100);      // quest id
        p.WriteByte(1);          // PERFORM
        p.WriteString("003");    // progress value
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.QuestId.Should().Be(2100);
        captured.State.Should().Be(1);
        captured.Value.Should().Be("003");
        captured.IsEx.Should().BeFalse();
    }

    [Fact]
    public void QuestRecord_Complete_DecodesState()
    {
        var (fh, router) = NewHandlers();
        QuestRecordArgs? captured = null;
        fh.OnQuestRecord += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(1);          // QuestRecord
        p.WriteShort(2100);
        p.WriteByte(2);          // COMPLETE
        p.WriteLong(0);          // FileTime
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.State.Should().Be(2);
        captured.QuestId.Should().Be(2100);
    }

    [Fact]
    public void QuestRecordEx_DecodesValue()
    {
        var (fh, router) = NewHandlers();
        QuestRecordArgs? captured = null;
        fh.OnQuestRecord += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.Message);
        p.WriteByte(11);         // MessageType.QuestRecordEx
        p.WriteShort(7777);
        p.WriteString("ex-value");
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.QuestId.Should().Be(7777);
        captured.Value.Should().Be("ex-value");
        captured.IsEx.Should().BeTrue();
    }

    // ── UserQuestRequest encoders ────────────────────────────────────────────────

    [Fact]
    public void QuestAccept_Encodes_Fields()
    {
        var p = new InPacket(GameSender.QuestAccept(questId: 2100, npcId: 9000001, x: 10, y: -20).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserQuestRequest);
        p.ReadByte().Should().Be(1);          // Accept
        p.ReadShort().Should().Be(2100);
        p.ReadInt().Should().Be(9000001);
        p.ReadInt().Should().Be(0);           // itemPos
        p.ReadShort().Should().Be(10);
        p.ReadShort().Should().Be(-20);
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void QuestComplete_Encodes_RewardIndex()
    {
        var p = new InPacket(GameSender.QuestComplete(questId: 2100, npcId: 9000001, x: 0, y: 0, rewardIndex: 2).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserQuestRequest);
        p.ReadByte().Should().Be(2);          // Complete
        p.ReadShort().Should().Be(2100);
        p.ReadInt().Should().Be(9000001);
        p.ReadInt().Should().Be(0);           // itemPos
        p.ReadShort().Should().Be(0);
        p.ReadShort().Should().Be(0);
        p.ReadInt().Should().Be(2);           // reward index
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void QuestResign_Encodes_QuestId()
    {
        var p = new InPacket(GameSender.QuestResign(2100).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserQuestRequest);
        p.ReadByte().Should().Be(3);          // Resign
        p.ReadShort().Should().Be(2100);
        p.Remaining.Should().Be(0);
    }

    // OpeningScript(4)/CompleteScript(5): how a quest NPC starts/completes a quest (the body-click
    // path for NPCs with no general script + the floating quest-marker). Mirrors Kinoko
    // UserHandler.handleUserQuestRequest: byte action, short questId, int npcTemplateId, short x, short y.

    [Fact]
    public void QuestStartScript_Encodes_Fields()
    {
        var p = new InPacket(GameSender.QuestStartScript(questId: 2100, npcTemplateId: 9000001, x: 10, y: -20).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserQuestRequest);
        p.ReadByte().Should().Be(4);          // OpeningScript
        p.ReadShort().Should().Be(2100);
        p.ReadInt().Should().Be(9000001);
        p.ReadShort().Should().Be(10);
        p.ReadShort().Should().Be(-20);
        p.Remaining.Should().Be(0);
    }

    [Fact]
    public void QuestCompleteScript_Encodes_Fields()
    {
        var p = new InPacket(GameSender.QuestCompleteScript(questId: 2100, npcTemplateId: 9000001, x: 5, y: 6).ToArray());
        p.ReadShort().Should().Be((short)InHeader.UserQuestRequest);
        p.ReadByte().Should().Be(5);          // CompleteScript
        p.ReadShort().Should().Be(2100);
        p.ReadInt().Should().Be(9000001);
        p.ReadShort().Should().Be(5);
        p.ReadShort().Should().Be(6);
        p.Remaining.Should().Be(0);
    }
}
