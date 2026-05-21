namespace MapleClaude.Map;

/// <summary>
/// Backdrop tile / scroll behavior, encoded as the <c>type</c> property
/// on each <c>back/N</c> entry in a map blob. Values match the v95 client's
/// enum.
/// </summary>
public enum BackType
{
    Normal = 0,
    HTiled = 1,
    VTiled = 2,
    Tiled = 3,
    HMoveA = 4,
    VMoveA = 5,
    HMoveB = 6,
    VMoveB = 7,
}
