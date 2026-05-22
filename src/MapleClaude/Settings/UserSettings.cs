namespace MapleClaude.Settings;

/// <summary>
/// Serializable user preferences persisted to
/// <c>%APPDATA%/MapleClaude/settings.json</c>. Every member is a simple type so
/// the System.Text.Json source generator can emit a reflection-free serializer
/// (the single-file build keeps trimming off, but source-gen is cleaner and
/// future-proofs an eventual trim/AOT pass).
///
/// Key bindings are stored as <c>"&lt;Keys&gt;" → "&lt;KeyAction&gt;"</c> name pairs
/// (e.g. <c>"Z" → "PickUp"</c>) rather than raw integers, so the file stays
/// human-editable and survives enum re-ordering.
/// </summary>
public sealed class UserSettings
{
    public Dictionary<string, string> KeyBindings { get; set; } = new();
    public int BgmVolume { get; set; } = 80;
    public int SfxVolume { get; set; } = 100;

    /// <summary>UI language code; selects the <c>strings.&lt;lang&gt;.csv</c> pack
    /// (defaults to English, which is always bundled).</summary>
    public string Language { get; set; } = "en";
}
