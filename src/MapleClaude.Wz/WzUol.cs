namespace MapleClaude.Wz;

/// <summary>
/// A WZ <c>UOL</c> property: a symbolic link to another node inside the same
/// <see cref="WzImage"/>. Resolution is on-demand because UOLs can chain.
/// </summary>
public sealed class WzUol
{
    private readonly WzImage _parent;
    private object? _resolved;
    private bool _resolveAttempted;

    /// <summary>The raw target path, relative to the UOL node's parent property.</summary>
    public string Target { get; }

    internal WzUol(WzImage parent, string target)
    {
        _parent = parent;
        Target = target;
    }

    /// <summary>
    /// Resolves the UOL target to a property value. Returns <c>null</c> if the
    /// target doesn't exist. Idempotent and cached after the first call.
    /// </summary>
    public object? Resolve()
    {
        if (_resolveAttempted)
        {
            return _resolved;
        }
        _resolveAttempted = true;
        // UOL target paths are resolved against the image's root property tree;
        // they can use "../" to walk up. Phase-1 login assets don't appear to
        // use "../", so this implementation handles the common forward-path case.
        // Full ancestor walking can be added when a real asset needs it.
        _resolved = _parent.GetItem(Target);
        return _resolved;
    }
}
