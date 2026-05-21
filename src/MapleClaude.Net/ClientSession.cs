using MapleClaude.Net.Crypto;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace MapleClaude.Net;

/// <summary>
/// Low-level TCP session for one MapleStory v95 connection (login or channel).
/// Handles the PacketCipher framing/encryption pipeline.
///
/// Protocol:
///   Server→Client: 4-byte header (ParseHeader validates version + extracts length)
///                  followed by N encrypted payload bytes (DecryptBody).
///   Client→Server: 4-byte header (BuildHeader) followed by N encrypted payload bytes (EncryptBody).
///
/// On connect the server sends a plain-text handshake:
///   short  gameVersion
///   string patchLocation
///   byte[4] recvIv
///   byte[4] sendIv
///   byte   locale
/// After parsing the handshake, send and receive IVs are established and all
/// further packets are encrypted.
/// </summary>
public sealed class ClientSession : IDisposable
{
    private readonly ILogger<ClientSession> _log;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly byte[] _sendIv = new byte[4];
    private readonly byte[] _recvIv = new byte[4];
    private CancellationTokenSource? _cts;
    private bool _handshakeDone;
    private bool _disposed;

    /// <summary>Raised on the reader thread when a decrypted payload arrives. Must be thread-safe.</summary>
    public event Action<InPacket>? PacketReceived;
    public event Action? Disconnected;

    public bool IsConnected => _tcp?.Connected == true && !_disposed;

    public ClientSession(ILogger<ClientSession> log)
    {
        _log = log;
    }

    // ── Connect ────────────────────────────────────────────────────────────────

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _tcp    = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcp.GetStream();
        _cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _handshakeDone = false;
        _log.LogInformation("Connected to {Host}:{Port}", host, port);
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
    }

    // ── Send ───────────────────────────────────────────────────────────────────

    public void Send(OutPacket pkt)
    {
        if (_stream is null || !_handshakeDone) return;
        var payload = pkt.GetBytes();
        var frame   = new byte[PacketCipher.HeaderSize + payload.Length];

        PacketCipher.BuildHeader(payload.Length, _sendIv, frame.AsSpan(0, PacketCipher.HeaderSize));
        payload.CopyTo(frame, PacketCipher.HeaderSize);
        PacketCipher.EncryptBody(frame.AsSpan(PacketCipher.HeaderSize), _sendIv);

        _stream.Write(frame, 0, frame.Length);
    }

    // ── Read loop ──────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            var reader = PipeReader.Create(_stream!);
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (true)
                {
                    if (!_handshakeDone)
                    {
                        if (!TryReadHandshake(ref buffer)) break;
                    }
                    else
                    {
                        if (!TryReadPacket(ref buffer)) break;
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ClientSession read loop ended");
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    // ── Handshake parsing ─────────────────────────────────────────────────────

    private bool TryReadHandshake(ref System.Buffers.ReadOnlySequence<byte> buffer)
    {
        // Minimum handshake: 2 + 2 + patchLen + 4 + 4 + 1
        if (buffer.Length < 15) return false;

        var reader = new System.Buffers.SequenceReader<byte>(buffer);

        reader.TryReadLittleEndian(out short gameVersion);
        reader.TryReadLittleEndian(out short patchLen);
        if (buffer.Length < 2 + 2 + patchLen + 4 + 4 + 1) return false;

        reader.Advance(patchLen);         // skip patch string bytes
        Span<byte> recvIvBuf = stackalloc byte[4];
        Span<byte> sendIvBuf = stackalloc byte[4];
        reader.TryCopyTo(recvIvBuf); reader.Advance(4);
        recvIvBuf.CopyTo(_recvIv);
        reader.TryCopyTo(sendIvBuf); reader.Advance(4);
        sendIvBuf.CopyTo(_sendIv);
        reader.TryRead(out _); // locale

        buffer = buffer.Slice(reader.Position);
        _handshakeDone = true;
        _log.LogInformation("Handshake OK: ver={V} recvIV={R} sendIV={S}",
            gameVersion,
            BitConverter.ToString(_recvIv),
            BitConverter.ToString(_sendIv));
        return true;
    }

    // ── Packet parsing ────────────────────────────────────────────────────────

    private bool TryReadPacket(ref System.Buffers.ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length < PacketCipher.HeaderSize) return false;

        var headerBuf = new byte[PacketCipher.HeaderSize];
        buffer.Slice(0, PacketCipher.HeaderSize).CopyTo(headerBuf);

        var (valid, length) = PacketCipher.ParseHeader(headerBuf, _recvIv);
        if (!valid)
        {
            _log.LogWarning("Invalid packet header — disconnecting");
            _cts?.Cancel();
            return false;
        }

        var total = PacketCipher.HeaderSize + length;
        if (buffer.Length < total) return false;

        var payload = new byte[length];
        buffer.Slice(PacketCipher.HeaderSize, length).CopyTo(payload);
        PacketCipher.DecryptBody(payload, _recvIv);

        buffer = buffer.Slice(total);

        var pkt = new InPacket(payload, 0);
        try { PacketReceived?.Invoke(pkt); }
        catch (Exception ex) { _log.LogWarning(ex, "PacketReceived handler threw"); }
        return true;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _stream?.Dispose();
        _tcp?.Dispose();
    }
}
