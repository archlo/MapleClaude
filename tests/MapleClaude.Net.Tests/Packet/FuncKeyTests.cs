using FluentAssertions;
using MapleClaude.Net.Handlers;
using MapleClaude.Net.Packet;
using MapleClaude.Net.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Key-config wire-format tests. The server sends <c>FuncKeyMappedInit</c> on field
/// entry (<c>byte bDefault</c> then 89 × (<c>byte type</c>, <c>int id</c>)); the client
/// saves edits with <c>FuncKeyMappedModified</c> (<c>int type=KeyModified</c>,
/// <c>int size</c>, then size × (<c>int index</c>, <c>byte type</c>, <c>int id</c>)).
/// Mirrors upstream Kinoko <c>FieldPacket.funcKeyMappedInit</c> /
/// <c>UserHandler.handleFuncKeyMappedModified</c>.
/// </summary>
public class FuncKeyTests
{
    private const int FuncKeyMapSize = 89;

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
        ((int)OutHeader.FuncKeyMappedInit).Should().Be(398);
        ((int)InHeader.FuncKeyMappedModified).Should().Be(159);
        ((int)InHeader.QuickslotKeyMappedModified).Should().Be(216);
    }

    [Fact]
    public void FuncKeyMappedInit_NonDefault_Decodes89Entries()
    {
        var (fh, router) = NewHandlers();
        List<FuncKeyEntry>? captured = null;
        fh.OnFuncKeyMappedInit += e => captured = e;

        var p = OutPacket.Of((short)OutHeader.FuncKeyMappedInit);
        p.WriteByte(0);                          // bDefault = false
        for (var i = 0; i < FuncKeyMapSize; i++)
        {
            p.WriteByte((byte)(i % 8));          // type
            p.WriteInt(1000 + i);                // id
        }
        Dispatch(router, p);

        captured.Should().NotBeNull();
        var e = captured!;
        e.Should().HaveCount(FuncKeyMapSize);
        e[18].KeyIndex.Should().Be(18);
        e[18].Type.Should().Be((byte)(18 % 8));
        e[18].ActionId.Should().Be(1018);
    }

    [Fact]
    public void FuncKeyMappedInit_Default_DecodesNoEntries()
    {
        var (fh, router) = NewHandlers();
        List<FuncKeyEntry>? captured = null;
        fh.OnFuncKeyMappedInit += e => captured = e;

        var p = OutPacket.Of((short)OutHeader.FuncKeyMappedInit);
        p.WriteByte(1);                          // bDefault = true → no entries follow
        Dispatch(router, p);

        captured.Should().NotBeNull();
        captured!.Should().BeEmpty();
    }

    [Fact]
    public void FuncKeyMappedModified_Encodes_KeyModifiedTriples()
    {
        // Mirror GameStage.SendFuncKeyMapModified: type=KeyModified(0), size, then
        // (index, byte type, int id) per changed slot — the exact order
        // UserHandler.handleFuncKeyMappedModified reads.
        var p = OutPacket.Of(InHeader.FuncKeyMappedModified);
        p.WriteInt(0);          // FuncKeyMappedType.KeyModified
        p.WriteInt(2);          // size
        p.WriteInt(18); p.WriteByte(4); p.WriteInt(0);   // E -> MENU/Equipment
        p.WriteInt(57); p.WriteByte(5); p.WriteInt(54);  // Space -> BASICACTION/Interact

        var r = new InPacket(p.ToArray());
        r.ReadShort().Should().Be((short)InHeader.FuncKeyMappedModified);
        r.ReadInt().Should().Be(0);
        r.ReadInt().Should().Be(2);
        r.ReadInt().Should().Be(18); r.ReadByte().Should().Be(4); r.ReadInt().Should().Be(0);
        r.ReadInt().Should().Be(57); r.ReadByte().Should().Be(5); r.ReadInt().Should().Be(54);
        r.Remaining.Should().Be(0);
    }
}
