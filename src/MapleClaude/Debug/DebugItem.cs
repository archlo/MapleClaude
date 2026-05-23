using Microsoft.Xna.Framework;

namespace MapleClaude.Debug;

/// <summary>
/// One entry in the <see cref="DebugRegistry"/>. <see cref="Get"/> /
/// <see cref="Set"/> are the canonical value accessors (used by the debug
/// grid). The optional screen-space mapping members let the drag-mode marker
/// renderer treat the stored value as something other than a raw screen
/// position — e.g. an offset relative to a parent panel.
/// </summary>
public sealed record DebugItem(
    string Category,
    string Name,
    Func<Vector2> Get,
    Action<Vector2> Set)
{
    /// <summary>
    /// Optional: returns the on-screen position to render the drag marker at.
    /// When null, falls back to <see cref="Get"/> (i.e. the stored value IS a
    /// screen-space position). Use this when the value is an offset from a
    /// moving anchor (e.g. signboard top-left).
    /// </summary>
    public Func<Vector2>? GetScreenPos { get; init; }

    /// <summary>
    /// Optional: called with the new screen position when the user drags the
    /// marker. When null, falls back to <see cref="Set"/>. Use this when the
    /// stored value isn't a screen point and needs to be derived from one.
    /// </summary>
    public Action<Vector2>? SetFromScreen { get; init; }

    /// <summary>
    /// When false, the item is excluded from drag-mode marker rendering and
    /// click-pick. Defaults to true. Set to false for non-screen-space values
    /// (e.g. a camera offset in map coordinates) or read-only entries.
    /// </summary>
    public bool Draggable { get; init; } = true;

    /// <summary>Screen-space position to draw the marker at.</summary>
    public Vector2 EffectiveScreenPos() => GetScreenPos?.Invoke() ?? Get();

    /// <summary>Apply a new screen-space position from a drag.</summary>
    public void ApplyScreenPos(Vector2 screen) => (SetFromScreen ?? Set).Invoke(screen);
}
