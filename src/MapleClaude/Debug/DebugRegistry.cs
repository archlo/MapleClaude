using System.Collections.Concurrent;

namespace MapleClaude.Debug;

/// <summary>
/// Thread-safe registry of tunable items the debug window exposes for live
/// editing. Stages register their key positions (camera offset, signboard
/// centre, button offsets) and the debug window mutates them via the
/// supplied setter.
/// </summary>
public sealed class DebugRegistry
{
    private readonly ConcurrentDictionary<string, DebugItem> _items = new();

    public event Action? ItemsChanged;

    /// <summary>
    /// When true, the game window enters drag-pick mode: a left-click finds the
    /// nearest registered <see cref="DebugItem"/> within a small radius and drags
    /// it under the cursor while held. Normal stage mouse-click dispatch is
    /// suppressed so the user doesn't fire button handlers while moving them.
    /// Toggled by the checkbox at the top of the debug window.
    /// </summary>
    public bool DragMode { get; set; }

    public void Register(DebugItem item)
    {
        _items[Key(item.Category, item.Name)] = item;
        ItemsChanged?.Invoke();
    }

    public void Unregister(string category, string name)
    {
        _items.TryRemove(Key(category, name), out _);
        ItemsChanged?.Invoke();
    }

    public IReadOnlyCollection<DebugItem> Snapshot() => _items.Values.ToArray();

    private static string Key(string category, string name) => $"{category}::{name}";
}
