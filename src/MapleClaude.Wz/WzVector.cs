namespace MapleClaude.Wz;

/// <summary>A WZ <c>Shape2D#Vector2D</c> property: an integer (x, y) pair.</summary>
public readonly record struct WzVector(int X, int Y)
{
    public static readonly WzVector Zero = new(0, 0);

    public override string ToString() => $"({X}, {Y})";
}
