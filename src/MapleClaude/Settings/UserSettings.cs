namespace MapleClaude.Settings;

/// <summary>
/// Serializable user preferences persisted to
/// <c>%APPDATA%/MapleClaude/settings.json</c>. Every member is a simple type so
/// the System.Text.Json source generator can emit a reflection-free serializer
/// (the single-file build keeps trimming off, but source-gen is cleaner and
/// future-proofs an eventual trim/AOT pass).
///
/// The func-key map is stored as <c>"&lt;scancode&gt;" → "&lt;typeInt&gt;:&lt;id&gt;"</c>
/// pairs (e.g. <c>"18" → "4:0"</c> = scancode 18 bound to MENU/Equipment), mirroring
/// the server's <c>FuncKeyMapped</c> model. Only bound slots are written; a
/// server-sent keymap overrides the file on login.
/// </summary>
public sealed class UserSettings
{
    /// <summary>Func-key map: <c>"&lt;scancode&gt;" → "&lt;typeInt&gt;:&lt;id&gt;"</c>
    /// (e.g. <c>"57" → "5:54"</c> = Space bound to BASICACTION/Interact).</summary>
    public Dictionary<string, string> FuncKeyMap { get; set; } = new();

    public int BgmVolume { get; set; } = 80;
    public int SfxVolume { get; set; } = 100;

    /// <summary>In-game window resolution (the login flow always runs at 800×600).</summary>
    public int ResW { get; set; } = 1024;
    public int ResH { get; set; } = 768;

    /// <summary>UI language code; selects the <c>strings.&lt;lang&gt;.csv</c> pack
    /// (defaults to English, which is always bundled).</summary>
    public string Language { get; set; } = "en";
}
