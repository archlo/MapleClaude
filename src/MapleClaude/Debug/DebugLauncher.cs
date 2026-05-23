namespace MapleClaude.Debug;

/// <summary>
/// Starts the <see cref="DebugWindow"/> on a dedicated STA thread so it has
/// its own WinForms message pump and doesn't interfere with the MonoGame
/// main loop. Toggled by the <c>MAPLECLAUDE_DEBUG</c> env var (any
/// non-empty value enables it).
/// </summary>
public static class DebugLauncher
{
    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAPLECLAUDE_DEBUG"));

    public static void Launch(DebugRegistry registry, DebugLogSink logSink)
    {
        if (!IsEnabled)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new DebugWindow(registry, logSink);
            Application.Run(form);
        })
        {
            Name = "MapleClaude.Debug",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
