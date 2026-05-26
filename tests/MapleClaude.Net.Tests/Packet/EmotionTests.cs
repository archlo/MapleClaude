using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Senders;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Face-emotion wire-format tests. The client sends <c>UserEmotion(56)</c>:
/// <c>int nEmotion, int nDuration, byte bByItemOption</c> (no <c>update_time</c>
/// prefix). The server broadcasts <c>UserEmotion(219)</c> to other players in the
/// field — <c>int charId, int nEmotion, int nDuration, byte bByItemOption</c> —
/// and a self-echo channel <c>UserEmotionLocal(232)</c>
/// (<c>int nEmotion, int nDuration, byte bByItemOption</c>) exists for forked-server
/// / cash-item flows; vanilla Kinoko does not emit it.
///
/// Mirrors upstream <c>kinoko/handler/user/UserHandler.handleUserEmotion</c>,
/// <c>kinoko/packet/user/UserRemote.emotion</c>, and
/// <c>kinoko/packet/user/UserLocal.emotion</c>.
/// </summary>
public class EmotionTests
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
    public void Opcodes_HaveCanonicalValues()
    {
        ((int)InHeader.UserEmotion).Should().Be(56);
        ((int)OutHeader.UserEmotion).Should().Be(219);
        ((int)OutHeader.UserEmotionLocal).Should().Be(232);
    }

    [Fact]
    public void UserEmotion_Encodes_IntIntByte_NoUpdateTime()
    {
        // FuncKey path sends emotion=5 (angry), duration=-1, byItemOption=false.
        // No update_time prefix — UserHandler.handleUserEmotion reads int/int/bool
        // directly (unlike UserChat/UserAbilityUp which prefix update_time).
        var p = GameSender.UserEmotion(emotion: 5, duration: -1, byItemOption: false);
        var r = new InPacket(p.ToArray());

        r.ReadShort().Should().Be((short)InHeader.UserEmotion);
        r.ReadInt().Should().Be(5);     // nEmotion
        r.ReadInt().Should().Be(-1);    // nDuration
        r.ReadByte().Should().Be(0);    // bByItemOption (false)
        r.Remaining.Should().Be(0);
    }

    [Fact]
    public void UserEmotion_ByItemOption_TrueEncodesOne()
    {
        var p = GameSender.UserEmotion(emotion: 7, duration: 1500, byItemOption: true);
        var r = new InPacket(p.ToArray());

        r.ReadShort().Should().Be((short)InHeader.UserEmotion);
        r.ReadInt().Should().Be(7);
        r.ReadInt().Should().Be(1500);
        r.ReadByte().Should().Be(1);    // bByItemOption (true)
        r.Remaining.Should().Be(0);
    }

    [Fact]
    public void UserEmotion_DefaultArgs_SendMinusOneDuration()
    {
        // The FuncKey dispatch (CWvsContext::SendEmotionChange) always sends
        // duration = -1 so the server echoes -1, and every client computes the
        // duration from the WZ face's own per-frame delay sum.
        var p = GameSender.UserEmotion(emotion: 2);
        var r = new InPacket(p.ToArray());

        r.ReadShort().Should().Be((short)InHeader.UserEmotion);
        r.ReadInt().Should().Be(2);
        r.ReadInt().Should().Be(-1);
        r.ReadByte().Should().Be(0);
        r.Remaining.Should().Be(0);
    }

    [Fact]
    public void UserEmotionRemote_Decodes_CharId_Emotion_Duration_ByItem()
    {
        // OutHeader.UserEmotion(219): the broadcast — int charId leads, then the
        // same three fields as the C→S packet. Mirrors UserRemote.emotion.
        var (fh, router) = NewHandlers();
        UserEmotionArgs? captured = null;
        fh.OnUserEmotion += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.UserEmotion);
        p.WriteInt(12345);     // dwCharacterId
        p.WriteInt(3);         // nEmotion (troubled)
        p.WriteInt(2000);      // nDuration
        p.WriteByte(0);        // bByItemOption
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.CharId.Should().Be(12345);
        captured.Emotion.Should().Be(3);
        captured.DurationMs.Should().Be(2000);
        captured.ByItemOption.Should().BeFalse();
    }

    [Fact]
    public void UserEmotionLocal_Decodes_NoCharId_UsesSentinel()
    {
        // OutHeader.UserEmotionLocal(232): the local-self echo — three fields,
        // no leading charId. Surfaced with CharId = 0 (the "this is the local
        // player" sentinel), so GameStage routes it to the local avatar.
        var (fh, router) = NewHandlers();
        UserEmotionArgs? captured = null;
        fh.OnUserEmotion += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.UserEmotionLocal);
        p.WriteInt(10);        // nEmotion (cheers)
        p.WriteInt(-1);        // nDuration (let face's own frame total drive it)
        p.WriteByte(1);        // bByItemOption
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.CharId.Should().Be(0);
        captured.Emotion.Should().Be(10);
        captured.DurationMs.Should().Be(-1);
        captured.ByItemOption.Should().BeTrue();
    }

    [Fact]
    public void UserEmotionRemote_DurationCanBeMinusOne()
    {
        // The server is a pass-through: when the sender's UserEmotion encoded
        // duration = -1, the broadcast echoes -1. Every receiving client then
        // resolves the duration from its own WZ face's per-frame delay sum.
        var (fh, router) = NewHandlers();
        UserEmotionArgs? captured = null;
        fh.OnUserEmotion += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.UserEmotion);
        p.WriteInt(67890);
        p.WriteInt(1);          // hit
        p.WriteInt(-1);
        p.WriteByte(0);
        Dispatch(router, p);

        captured!.DurationMs.Should().Be(-1);
    }
}
