namespace MapleClaude.Map;

/// <summary>One ladder/rope entry parsed from <c>...img/ladderRope/&lt;sn&gt;</c>. Mirrors the v95
/// client's <c>CLadderOrRope</c> (dwSN, bLadder, bUpperFoothold, x, y1, y2, nPage). A character climbs
/// when its x is within ±10 of <see cref="X"/> and its y is inside the [<see cref="Top"/>,
/// <see cref="Bottom"/>] span.</summary>
public sealed class LadderRope
{
    public int Sn { get; init; }
    /// <summary>WZ <c>l</c>: true = ladder (climb on the spot), false = rope (sway). Picks the avatar stance.</summary>
    public bool IsLadder { get; init; }
    /// <summary>WZ <c>uf</c>: there is a foothold at the top, so climbing up steps onto the platform above.</summary>
    public bool UpperFoothold { get; init; }
    public int X { get; init; }
    public int Y1 { get; init; }
    public int Y2 { get; init; }
    public int Page { get; init; }

    /// <summary>Topmost world Y (smaller value).</summary>
    public int Top => Math.Min(Y1, Y2);
    /// <summary>Bottommost world Y (larger value).</summary>
    public int Bottom => Math.Max(Y1, Y2);
}
