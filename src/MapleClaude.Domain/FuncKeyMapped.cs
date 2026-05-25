namespace MapleClaude.Domain;

/// <summary>
/// What a keyboard key is bound to, mirroring the v95 client's
/// <c>FUNCKEY_MAPPED::nType</c> (and Kinoko's <c>FuncKeyType</c>). The integer
/// values are the wire values used by <c>FuncKeyMappedInit</c> /
/// <c>FuncKeyMappedModified</c>, so they round-trip straight to/from the server.
/// </summary>
public enum FuncKeyType : byte
{
    None        = 0,
    Skill       = 1,
    Item        = 2,
    Emotion     = 3,   // face id 100..106 (legacy emote channel)
    Menu        = 4,   // UI panel/action id 0..29
    BasicAction = 5,   // in-game action id 50..54 (pickup/sit/attack/jump/…)
    BasicMotion = 6,   // face id 100..106
    Effect      = 7,   // equip/effect item id
    MacroSkill  = 8,   // macro id
}

/// <summary>
/// One key-binding slot: a <see cref="FuncKeyType"/> plus its id (a panel/action
/// id for <see cref="FuncKeyType.Menu"/>/<see cref="FuncKeyType.BasicAction"/>/
/// face types, or a skill / item / macro id otherwise). The keymap is an array of
/// these indexed by DInput scancode — exactly the shape the server speaks.
/// </summary>
public readonly record struct FuncKeyMapped(FuncKeyType Type, int Id)
{
    public static readonly FuncKeyMapped None = new(FuncKeyType.None, 0);

    public bool IsBound => Type != FuncKeyType.None;
}
