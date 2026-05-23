using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace MapleClaude.Debug;

/// <summary>
/// Serilog sink that pushes formatted log lines into a thread-safe queue.
/// The debug window polls and drains it onto its log <c>TextBox</c>.
/// </summary>
public sealed class DebugLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<string> _queue = new();
    public const int MaxQueueDepth = 2000;

    public void Emit(LogEvent logEvent)
    {
        var line = $"[{logEvent.Timestamp:HH:mm:ss.fff}] [{logEvent.Level.ToString().ToUpperInvariant()[..3]}] {logEvent.RenderMessage()}";
        if (logEvent.Exception is not null)
        {
            line += " | " + logEvent.Exception.Message;
        }
        _queue.Enqueue(line);
        // Drop oldest if we're flooded so the queue doesn't grow without bound.
        while (_queue.Count > MaxQueueDepth && _queue.TryDequeue(out _)) { }
    }

    /// <summary>Drains up to <paramref name="max"/> queued lines.</summary>
    public List<string> Drain(int max = 200)
    {
        var result = new List<string>();
        for (var i = 0; i < max && _queue.TryDequeue(out var line); i++)
        {
            result.Add(line);
        }
        return result;
    }
}
