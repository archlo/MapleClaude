using MapleClaude.Net;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MapleClaude;

/// <summary>
/// High-level MapleStory v95 session. Owns one <see cref="ClientSession"/> at a time
/// (login server or channel server — never both simultaneously).
///
/// Incoming packets are queued thread-safely and drained by the game thread each
/// frame via <see cref="DrainQueue"/>. All packet handlers run on the MonoGame
/// update thread so they can safely mutate game state.
///
/// Typical flow:
///   1. <c>await Session.ConnectLoginAsync(host, 8484)</c>
///   2. Stage sends via <c>Session.Send(new OutPacket((short)OutHeader.CheckPassword)...)</c>
///   3. <c>GameStage.Update</c> calls <c>Session.DrainQueue()</c> — handlers fire here
///   4. After SelectCharacterResult, call <c>ConnectChannelAsync(channelHost, port)</c>
/// </summary>
public sealed class MapleSession : IDisposable
{
    private readonly ILogger<MapleSession> _log;
    private readonly ILoggerFactory _loggerFactory;
    private ClientSession? _session;

    private readonly ConcurrentQueue<(bool isDisconnect, InPacket? pkt)> _incoming = new();
    private readonly Dictionary<InHeader, Action<InPacket>> _handlers = new();

    public bool IsConnected => _session?.IsConnected == true;

    public event Action? OnDisconnected;

    public MapleSession(ILogger<MapleSession> log, ILoggerFactory loggerFactory)
    {
        _log          = log;
        _loggerFactory = loggerFactory;
    }

    // ── Connection ─────────────────────────────────────────────────────────────

    public async Task ConnectLoginAsync(string host, int port = 8484)
    {
        _log.LogInformation("Connecting to login server {Host}:{Port}", host, port);
        await ConnectAsync(host, port).ConfigureAwait(false);
    }

    public async Task ConnectChannelAsync(string host, int port)
    {
        _log.LogInformation("Connecting to channel server {Host}:{Port}", host, port);
        await ConnectAsync(host, port).ConfigureAwait(false);
    }

    private async Task ConnectAsync(string host, int port)
    {
        _session?.Dispose();
        _session = new ClientSession(_loggerFactory.CreateLogger<ClientSession>());
        _session.PacketReceived += p  => _incoming.Enqueue((false, p));
        _session.Disconnected   += () => _incoming.Enqueue((true,  null));
        await _session.ConnectAsync(host, port).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        _session?.Dispose();
        _session = null;
    }

    // ── Send ───────────────────────────────────────────────────────────────────

    public void Send(OutPacket pkt) => _session?.Send(pkt);

    // ── Drain (call every frame from game thread) ──────────────────────────────

    public void DrainQueue()
    {
        while (_incoming.TryDequeue(out var item))
        {
            if (item.isDisconnect)
            {
                _log.LogWarning("Session disconnected");
                OnDisconnected?.Invoke();
                continue;
            }

            var pkt = item.pkt!;
            var opcode = (InHeader)pkt.Opcode;
            if (_handlers.TryGetValue(opcode, out var handler))
            {
                try { handler(pkt); }
                catch (Exception ex) { _log.LogWarning(ex, "Handler {Op} threw", opcode); }
            }
            else
            {
                _log.LogTrace("Unhandled {Op}(0x{V:X4})", opcode, (int)(short)opcode);
            }
        }
    }

    // ── Handler registry ───────────────────────────────────────────────────────

    public void RegisterHandler(InHeader opcode, Action<InPacket> handler)
        => _handlers[opcode] = handler;

    public void UnregisterHandler(InHeader opcode) => _handlers.Remove(opcode);
    public void ClearHandlers()                    => _handlers.Clear();

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
