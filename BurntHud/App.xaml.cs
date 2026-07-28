using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Isley;

public partial class App : Application
{
    private const int MaxCrashReports = 10;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashReport("appdomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashReport("task", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        // Log only; the default crash behavior is preserved so a broken
        // overlay never silently keeps running in a corrupt state.
        WriteCrashReport("dispatcher", args.Exception);
    }

    private static void WriteCrashReport(string origin, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var directory = ResolveCrashDirectory();
            Directory.CreateDirectory(directory);
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var path = Path.Combine(
                directory,
                $"crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{origin}.txt");
            File.WriteAllText(
                path,
                $"Isley {version}{Environment.NewLine}" +
                $"When (UTC): {DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
                $"Origin: {origin}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}{Environment.NewLine}" +
                exception);
            PruneCrashReports(directory);
        }
        catch
        {
            // A crash reporter must never crash the reporter path.
        }
    }

    private static string ResolveCrashDirectory()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "IsleyData");
        if (Directory.Exists(portable))
        {
            return Path.Combine(portable, "Logs");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Logs");
    }

    private static void PruneCrashReports(string directory)
    {
        var reports = new DirectoryInfo(directory)
            .GetFiles("crash-*.txt")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(MaxCrashReports);
        foreach (var report in reports)
        {
            report.Delete();
        }
    }
}
