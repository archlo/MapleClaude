namespace MapleClaude.Net.Packet;

/// <summary>
/// NPC script dialog message types. Values match upstream Kinoko's
/// <c>kinoko/script/common/ScriptMessageType.java</c> exactly — they are the
/// <c>nMsgType</c> byte in both the S→C <c>ScriptMessage(363)</c> packet and
/// the C→S <c>UserScriptMessageAnswer(65)</c> answer (the client must echo the
/// type the server sent).
/// </summary>
public enum ScriptMessageType : byte
{
    Say = 0,
    SayImage = 1,
    AskYesNo = 2,
    AskText = 3,
    AskNumber = 4,
    AskMenu = 5,
    AskQuiz = 6,           // not produced by the v95 server (builder throws)
    AskSpeedQuiz = 7,      // ditto
    AskAvatar = 8,
    AskMemberShopAvatar = 9,
    AskPet = 10,
    AskPetAll = 11,
    Script = 12,
    AskAccept = 13,
    AskBoxText = 14,
    AskSlideMenu = 15,
    AskCenter = 16,
}

/// <summary>
/// <c>bParam</c> flags on a <c>ScriptMessage</c> — mirrors
/// <c>kinoko/script/common/ScriptMessageParam.java</c>.
/// </summary>
[Flags]
public enum ScriptMessageParam
{
    None = 0x0,
    NotCancellable = 0x1,
    PlayerAsSpeaker = 0x2,
    SpeakerOnRight = 0x4,
    FlipSpeaker = 0x8,
}

public static class ScriptMessageTypeExtensions
{
    public static ScriptMessageType FromValue(byte value) =>
        Enum.IsDefined(typeof(ScriptMessageType), value)
            ? (ScriptMessageType)value
            : ScriptMessageType.Say;
}
