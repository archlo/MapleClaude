using MapleClaude.Render;
using MapleClaude.Wz;

namespace MapleClaude.UI.Game;

/// <summary>
/// The authentic v95 simple-minimap marker icon set, loaded once from
/// <c>UI.wz / UIWindow2.img / MiniMapSimpleMode / DefaultHelper</c> — the source the real client's
/// <c>CUIMiniMap::MakeIconsForSimpleMiniMap</c> uses (NOT <c>Map.wz/MapHelper.img/minimap</c>, which
/// is a different/older set). These are the player, other characters, party/guild/friend members,
/// NPCs (incl. quest start/end), portals, and the directional edge indicators for off-pane markers.
/// The client draws each at 2× scale, bottom-centre on the marker point (see MiniMap.DrawMarker).
/// </summary>
public sealed class MiniMapMarkers
{
    // Character markers (native canvas sizes; drawn 2× by the minimap)
    public WzSprite? User        { get; }   // own character        (9×12)
    public WzSprite? Another     { get; }   // other player         (7×10)
    public WzSprite? Friend      { get; }   // friend               (7×10)
    public WzSprite? Guild       { get; }   // guild member         (7×10)
    public WzSprite? GuildMaster { get; }   // guild master         (7×13)
    public WzSprite? Party       { get; }   // party member         (7×10)
    public WzSprite? PartyMaster { get; }   // party leader         (7×13)

    // Field markers
    public WzSprite? Npc      { get; }      // npc                  (7×11)
    public WzSprite? StartNpc { get; }      // quest-start npc      (7×17)
    public WzSprite? EndNpc   { get; }      // quest-end npc        (7×17)
    public WzSprite? Portal   { get; }      // portal               (6×13)

    // Edge indicators for off-pane markers
    public WzSprite? ArrowUp        { get; }
    public WzSprite? ArrowDown      { get; }
    public WzSprite? ArrowLeft      { get; }
    public WzSprite? ArrowRight     { get; }
    public WzSprite? ArrowUpLeft    { get; }
    public WzSprite? ArrowUpRight   { get; }
    public WzSprite? ArrowDownLeft  { get; }
    public WzSprite? ArrowDownRight { get; }

    public MiniMapMarkers(WzTextureLoader loader, WzPackage? ui)
    {
        var root = ui?.GetItem("UIWindow2.img/MiniMapSimpleMode/DefaultHelper") as WzProperty;

        WzSprite? Load(string name) =>
            root?.Get(name) is WzCanvas c ? loader.Load(c) : null;

        User        = Load("user");
        Another     = Load("another");
        Friend      = Load("friend");
        Guild       = Load("guild");
        GuildMaster = Load("guildmaster");
        Party       = Load("party");
        PartyMaster = Load("partymaster");

        Npc      = Load("npc");
        StartNpc = Load("startnpc");
        EndNpc   = Load("endnpc");
        Portal   = Load("portal");

        ArrowUp        = Load("arrowup");
        ArrowDown      = Load("arrowdown");
        ArrowLeft      = Load("arrowleft");
        ArrowRight     = Load("arrowright");
        ArrowUpLeft    = Load("arrowupleft");
        ArrowUpRight   = Load("arrowupright");
        ArrowDownLeft  = Load("arrowdownleft");
        ArrowDownRight = Load("arrowdownright");
    }

    /// <summary>Picks the directional edge arrow for a marker that lies off the pane,
    /// given the sign of its offset from the pane (dx/dy: -1, 0, +1).</summary>
    public WzSprite? EdgeArrow(int dx, int dy) => (Math.Sign(dx), Math.Sign(dy)) switch
    {
        (0, -1)  => ArrowUp,
        (0,  1)  => ArrowDown,
        (-1, 0)  => ArrowLeft,
        (1,  0)  => ArrowRight,
        (-1, -1) => ArrowUpLeft,
        (1,  -1) => ArrowUpRight,
        (-1,  1) => ArrowDownLeft,
        (1,   1) => ArrowDownRight,
        _        => null,
    };
}
