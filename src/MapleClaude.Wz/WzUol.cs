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

    /// <summary>The property that holds this UOL (set by <see cref="WzProperty"/> on read).
    /// The starting directory for resolving <c>..</c> segments in <see cref="Target"/>.</summary>
    internal WzProperty? ParentProperty { get; set; }

    internal WzUol(WzImage parent, string target)
    {
        _parent = parent;
        Target = target;
    }

    /// <summary>
    /// Resolves the UOL target to a property value. Returns <c>null</c> if the
    /// target doesn't exist. Idempotent and cached after the first call. Handles
    /// relative <c>../</c> targets (e.g. a head canvas linking to
    /// <c>../../front/head</c>) by walking the parent-property chain, and follows
    /// UOL <em>chains</em> — a link whose target is itself a link — to the final
    /// non-UOL value. (Non-zero skins do exactly this: a per-action head node links
    /// to the skin's shared head, which is itself a link to the canvas; without
    /// chain-following the avatar's head/face/hair would fail to resolve.)
    /// </summary>
    public object? Resolve()
    {
        if (_resolveAttempted)
        {
            return _resolved;
        }
        _resolveAttempted = true;
        var result = ResolveTarget(Target);
        // Follow the chain to the final non-UOL target. The hop cap plus each link's
        // own _resolveAttempted guard make a cyclic chain resolve to null instead of
        // looping forever.
        for (var hops = 0; result is WzUol next && hops < 16; hops++)
        {
            result = next.Resolve();
        }
        _resolved = result;
        return _resolved;
    }

    private object? ResolveTarget(string target)
    {
        // Forward paths resolve against the image root (original behaviour).
        if (!target.Contains("..", StringComparison.Ordinal))
        {
            return _parent.GetItem(target);
        }
        // ".." walks up from the property that holds this UOL. Each leading ".."
        // moves to the current property's parent; the remainder descends from there.
        var segments = target.Split('/');
        var current = ParentProperty;
        var i = 0;
        for (; i < segments.Length && segments[i] == ".."; i++)
        {
            current = current?.ParentProperty;
        }
        var rest = string.Join('/', segments[i..]);
        if (current is not null)
        {
            return rest.Length == 0 ? current : current.GetItem(rest);
        }
        // Walked to (or above) the image root: resolve the remainder from the root.
        return rest.Length == 0 ? null : _parent.GetItem(rest);
    }
}
