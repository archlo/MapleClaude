using System.Runtime.InteropServices;
using MapleClaude.Debug;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MapleClaude;

/// <summary>
/// Logger category sentinel — used because <see cref="Program"/> is static and
/// <c>ILogger&lt;static class&gt;</c> is illegal.
/// </summary>
internal sealed class Bootstrap;

/// <summary>
/// Phase 1 entry point. Boots the generic host, wires Serilog (Console +
/// rolling file + Debug sinks + optional debug-window sink), reads
/// <c>MAPLECLAUDE_*</c> config from the environment, optionally launches the
/// WinForms debug window, then hands control to the MonoGame
/// <see cref="MapleClaudeGame"/> loop. ESC exits.
/// </summary>
public static class Program
{
    private const uint MbIconError = 0x00000010u;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static int Main(string[] args)
    {
        var logDir = Environment.GetEnvironmentVariable("MAPLECLAUDE_LOG_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "mapleclaude-.log");

        // Shared debug infrastructure (only used if MAPLECLAUDE_DEBUG=1).
        var debugRegistry = new DebugRegistry();
        var debugLogSink = new DebugLogSink();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(debugLogSink)
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration.AddEnvironmentVariables(prefix: "MAPLECLAUDE_");
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();
            builder.Services.AddSingleton(debugRegistry);
            builder.Services.AddSingleton<MapleClaudeGame>();

            using var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Bootstrap>>();
            logger.LogInformation(
                "MapleClaude {Phase} starting — log dir {LogDir} debug={Debug}",
                "phase-1", logDir, DebugLauncher.IsEnabled);

            // Optional debug window — only when MAPLECLAUDE_DEBUG=1.
            DebugLauncher.Launch(debugRegistry, debugLogSink);

            using var game = host.Services.GetRequiredService<MapleClaudeGame>();
            game.Run();

            logger.LogInformation("MapleClaude exited cleanly");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MapleClaude failed");
            ShowFatalDialog(ex, logDir);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ShowFatalDialog(Exception ex, string logDir)
    {
        var message =
            "MapleClaude failed to start.\n\n" +
            ex.GetType().FullName + ": " + ex.Message + "\n\n" +
            "Log directory:\n" + logDir;
        MessageBoxW(nint.Zero, message, "MapleClaude — fatal error", MbIconError);
    }
}
