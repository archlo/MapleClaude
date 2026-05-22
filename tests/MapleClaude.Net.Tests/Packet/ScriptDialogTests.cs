using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

public class ScriptDialogTests
{
    // Build a ScriptMessage(363) body the way upstream ScriptMessage.encode does,
    // dispatch it through FieldHandlers, and capture the decoded args.
    private static ScriptMessageArgs Decode(Action<OutPacket> writeBody)
    {
        var fh = new FieldHandlers(NullLogger<FieldHandlers>.Instance);
        var router = new PacketRouter(NullLogger<PacketRouter>.Instance);
        fh.Register(router);
        ScriptMessageArgs? captured = null;
        fh.OnScriptMessage += a => captured = a;

        var p = OutPacket.Of((short)OutHeader.ScriptMessage);
        p.WriteByte(0);          // nSpeakerTypeID
        p.WriteInt(2100);        // speakerId
        writeBody(p);            // msgType, param, type-specific body
        router.Dispatch(new InPacket(p.ToArray()), session: null!);

        captured.Should().NotBeNull();
        return captured!;
    }

    [Fact]
    public void Decode_Say_ReadsTextThenPrevNext()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.Say);
            p.WriteByte(0);                  // bParam
            p.WriteString("Hello there!");
            p.WriteByte(0);                  // hasPrev
            p.WriteByte(1);                  // hasNext
        });
        a.MsgType.Should().Be(ScriptMessageType.Say);
        a.Text.Should().Be("Hello there!");
        a.HasPrev.Should().BeFalse();
        a.HasNext.Should().BeTrue();
    }

    [Fact]
    public void Decode_Say_WithSpeakerOnRight_SkipsExtraInt()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.Say);
            p.WriteByte(0x4);                // SPEAKER_ON_RIGHT
            p.WriteInt(2100);               // repeated speaker template id
            p.WriteString("On the right");
            p.WriteByte(1);                  // hasPrev
            p.WriteByte(0);                  // hasNext
        });
        a.Text.Should().Be("On the right");
        a.HasPrev.Should().BeTrue();
        a.HasNext.Should().BeFalse();
    }

    [Fact]
    public void Decode_AskYesNo_ReadsTextOnly()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.AskYesNo);
            p.WriteByte(0);
            p.WriteString("Proceed?");
        });
        a.MsgType.Should().Be(ScriptMessageType.AskYesNo);
        a.Text.Should().Be("Proceed?");
    }

    [Fact]
    public void Decode_AskMenu_ReadsTextOnly()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.AskMenu);
            p.WriteByte(0);
            p.WriteString("#L0#One#l\r\n#L1#Two#l");
        });
        a.MsgType.Should().Be(ScriptMessageType.AskMenu);
        a.Text.Should().Contain("#L0#One#l");
    }

    [Fact]
    public void Decode_AskText_ReadsDefaultAndBounds()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.AskText);
            p.WriteByte(0);
            p.WriteString("Your name?");
            p.WriteString("default");
            p.WriteShort(2);
            p.WriteShort(12);
        });
        a.MsgType.Should().Be(ScriptMessageType.AskText);
        a.Text.Should().Be("Your name?");
        a.DefaultText.Should().Be("default");
        a.MinLength.Should().Be(2);
        a.MaxLength.Should().Be(12);
    }

    [Fact]
    public void Decode_AskNumber_ReadsDefaultMinMax()
    {
        var a = Decode(p =>
        {
            p.WriteByte((byte)ScriptMessageType.AskNumber);
            p.WriteByte(0);
            p.WriteString("How many?");
            p.WriteInt(5);
            p.WriteInt(1);
            p.WriteInt(99);
        });
        a.MsgType.Should().Be(ScriptMessageType.AskNumber);
        a.DefaultNum.Should().Be(5);
        a.MinNum.Should().Be(1);
        a.MaxNum.Should().Be(99);
    }

    // The root-cause bug was inverted msgType constants. Lock the canonical
    // values from upstream kinoko/script/common/ScriptMessageType.java.
    [Theory]
    [InlineData(ScriptMessageType.Say, 0)]
    [InlineData(ScriptMessageType.SayImage, 1)]
    [InlineData(ScriptMessageType.AskYesNo, 2)]
    [InlineData(ScriptMessageType.AskText, 3)]
    [InlineData(ScriptMessageType.AskNumber, 4)]
    [InlineData(ScriptMessageType.AskMenu, 5)]
    [InlineData(ScriptMessageType.AskQuiz, 6)]
    [InlineData(ScriptMessageType.AskAccept, 13)]
    [InlineData(ScriptMessageType.AskBoxText, 14)]
    [InlineData(ScriptMessageType.AskSlideMenu, 15)]
    public void ScriptMessageType_HasCanonicalValue(ScriptMessageType type, int expected)
    {
        ((int)type).Should().Be(expected);
    }
}
