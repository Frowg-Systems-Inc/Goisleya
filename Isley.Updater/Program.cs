using System.Diagnostics;
using System.Text.Json;

namespace Isley.Updater;

internal static class Program
{
    private const int WaitForExitMilliseconds = 120_000;
    private const int CopyRetryCount = 20;

    [STAThread]
    private static int Main(string[] args)
    {
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Updater");
        Directory.CreateDirectory(localRoot);
        var logPath = Path.Combine(localRoot, "updater.log");
        var resultPath = Path.Combine(localRoot, "last-result.json");

        try
        {
            var options = ParseArguments(args);
            Log(logPath, $"Starting update to {options.Version}.");
            ValidateOptions(options);
            WaitForIsleyToClose(options.ProcessId, logPath);
            ApplyPackageWithBackup(
                options.SourceDirectory,
                options.TargetDirectory,
                logPath,
                options.DeltaMode,
                options.Version);
            WriteResult(resultPath, true, options.Version, string.Empty);
            LaunchIsley(options.TargetDirectory, options.LaunchFile, logPath);
            Log(logPath, $"Update to {options.Version} completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Log(logPath, $"Update failed: {exception.GetType().Name}: {exception.Message}");
            WriteResult(resultPath, false, string.Empty, exception.Message);
            TryRelaunchFromArguments(args, logPath);
            return 1;
        }
    }

    private static UpdateOptions ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException("The update request was incomplete.");
            }
            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--pid", out var pidText)
            || !int.TryParse(pidText, out var processId)
            || !values.TryGetValue("--source", out var source)
            || !values.TryGetValue("--target", out var target)
            || !values.TryGetValue("--launch", out var launch)
            || !values.TryGetValue("--version", out var version))
        {
            throw new ArgumentException("The update request was incomplete.");
        }

        var deltaMode = false;
        if (values.TryGetValue("--mode", out var mode))
        {
            if (!string.Equals(mode, "delta", StringComparison.Ordinal))
            {
                throw new ArgumentException("The update request used an unknown mode.");
            }
            deltaMode = true;
        }

        return new UpdateOptions(
            processId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            launch,
            version,
            deltaMode);
    }

    private static void ValidateOptions(UpdateOptions options)
    {
        if (options.ProcessId <= 0
            || !Directory.Exists(options.SourceDirectory)
            || !Directory.Exists(options.TargetDirectory)
            || string.Equals(
                options.SourceDirectory,
                options.TargetDirectory,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(options.LaunchFile, "Isley.exe", StringComparison.Ordinal)
            || Path.IsPathRooted(options.LaunchFile)
            || options.LaunchFile.Contains("..", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(options.SourceDirectory, "Isley.exe"))
            || !File.Exists(Path.Combine(options.TargetDirectory, "Isley.exe"))
            || string.Equals(
                options.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetPathRoot(options.TargetDirectory)?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The update directories were not safe.");
        }

        if (options.DeltaMode
            && !File.Exists(Path.Combine(
                options.SourceDirectory,
                "isley-delta-manifest.json")))
        {
            throw new InvalidOperationException("The delta update was missing its file list.");
        }
    }

    private static void WaitForIsleyToClose(int processId, string logPath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, "Isley", StringComparison.OrdinalIgnoreCase))
            {
                Log(logPath, $"Process {processId} is no longer Isley; treating it as closed.");
                return;
            }
            Log(logPath, $"Waiting for Isley process {processId} to close.");
            if (!process.WaitForExit(WaitForExitMilliseconds))
            {
                throw new TimeoutException("Isley did not close in time for the update.");
            }
        }
        catch (ArgumentException)
        {
            // The application already closed between the button click and helper startup.
        }
    }

    private static void ApplyPackageWithBackup(
        string sourceDirectory,
        string targetDirectory,
        string logPath,
        bool deltaMode,
        string expectedVersion)
    {
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Updater",
            $"backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);
        try
        {
            CopyPackage(sourceDirectory, targetDirectory, backupRoot, logPath, deltaMode);
            if (deltaMode)
            {
                // Delta packages only remove files named by their verified file
                // list; the full-package orphan sweep would delete everything
                // the delta did not carry, so it must not run here.
                ApplyDeltaDeleteList(
                    sourceDirectory,
                    targetDirectory,
                    backupRoot,
                    logPath,
                    expectedVersion);
            }
            else
            {
                RemoveOrphanedPackageFiles(sourceDirectory, targetDirectory, backupRoot, logPath);
            }
            TryDeleteDirectory(backupRoot);
            Log(logPath, "Update files were applied and the rollback backup was cleared.");
        }
        catch
        {
            Log(logPath, "Restoring the previous Isley installation after an update failure.");
            RestoreBackup(backupRoot, targetDirectory, logPath);
            TryDeleteDirectory(backupRoot);
            throw;
        }
    }

    private static void CopyPackage(
        string sourceDirectory,
        string targetDirectory,
        string backupRoot,
        string logPath,
        bool deltaMode)
    {
        var sourceRoot = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var targetRoot = targetDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var backupRootPath = backupRoot.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(
            sourceDirectory,
            "*",
            SearchOption.AllDirectories);
        if (!deltaMode && files.Length < 20)
        {
            throw new InvalidDataException("The staged Isley package was incomplete.");
        }

        var copied = 0;
        foreach (var sourceFile in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            if (relative.Equals("IsleyData", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(
                    $"IsleyData{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                || relative.Equals("isley-delta-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged Isley package contained an unsafe path.");
            }

            if (File.Exists(destination))
            {
                var backupPath = Path.GetFullPath(Path.Combine(backupRoot, relative));
                if (!backupPath.StartsWith(backupRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The update backup path escaped the backup root.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                CopyWithRetry(destination, backupPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyWithRetry(sourceFile, destination);
            copied++;
        }
        Log(logPath, $"Copied {copied} package files with rollback backups.");
    }

    private static void RestoreBackup(
        string backupRoot,
        string targetDirectory,
        string logPath)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var backupRootPath = backupRoot.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var targetRoot = targetDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var restored = 0;
        foreach (var backupFile in Directory.GetFiles(
                     backupRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupRootPath, backupFile);
            var destination = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backupFile, destination, overwrite: true);
                restored++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                Log(logPath, $"Could not restore {relative}: {exception.Message}");
            }
        }
        Log(logPath, $"Restored {restored} files from the update backup.");
    }

    private static void RemoveOrphanedPackageFiles(
        string sourceDirectory,
        string targetDirectory,
        string backupRoot,
        string logPath)
    {
        var sourceRoot = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var targetRoot = targetDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var backupRootPath = backupRoot.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var packagedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in Directory.GetFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            if (relative.Equals("IsleyData", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(
                    $"IsleyData{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            packagedRelativePaths.Add(relative);
        }

        var removed = 0;
        foreach (var targetFile in Directory.GetFiles(
                     targetDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(targetRoot, targetFile);
            if (relative.Equals("IsleyData", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(
                    $"IsleyData{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                || packagedRelativePaths.Contains(relative))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(targetFile);
            if (!fullPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var backupPath = Path.GetFullPath(Path.Combine(backupRoot, relative));
            if (!backupPath.StartsWith(backupRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The orphan backup path escaped the backup root.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            CopyWithRetry(fullPath, backupPath);

            try
            {
                File.Delete(fullPath);
                removed++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                Log(logPath, $"Could not remove obsolete file {relative}: {exception.Message}");
            }
        }

        if (removed > 0)
        {
            Log(logPath, $"Removed {removed} obsolete install files.");
        }
    }

    private static void ApplyDeltaDeleteList(
        string sourceDirectory,
        string targetDirectory,
        string backupRoot,
        string logPath,
        string expectedVersion)
    {
        var manifestPath = Path.Combine(sourceDirectory, "isley-delta-manifest.json");
        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The delta update file list could not be read.", exception);
        }
        if (json.Length > 64 * 1024)
        {
            throw new InvalidDataException("The delta update file list exceeded its safety limit.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The delta update file list was not valid JSON.", exception);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out var formatVersion)
            || formatVersion != 1
            || !root.TryGetProperty("toVersion", out var toVersion)
            || toVersion.ValueKind != JsonValueKind.String
            || !string.Equals(toVersion.GetString(), expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The delta update file list did not match the update.");
        }

        var targetRoot = targetDirectory.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var backupRootPath = backupRoot.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var removed = 0;
        JsonElement[] entries =
            root.TryGetProperty("deletedFiles", out var deletedFiles)
            && deletedFiles.ValueKind == JsonValueKind.Array
                ? deletedFiles.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        if (entries.Length > 2000)
        {
            throw new InvalidDataException("The delta update file list exceeded its safety limit.");
        }

        foreach (var entry in entries)
        {
            var relative = (entry.ValueKind == JsonValueKind.String
                    ? entry.GetString() ?? string.Empty
                    : string.Empty)
                .Trim()
                .Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0
                || relative.Length > 512
                || Path.IsPathRooted(relative)
                || relative.Split(
                       new[] { Path.DirectorySeparatorChar },
                       StringSplitOptions.RemoveEmptyEntries)
                   .Any(part => part == "..")
                || relative.Equals("IsleyData", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(
                    $"IsleyData{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The delta update file list contained an unsafe path.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!fullPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The delta update file list escaped the install folder.");
            }
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var backupPath = Path.GetFullPath(Path.Combine(backupRoot, relative));
            if (!backupPath.StartsWith(backupRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update backup path escaped the backup root.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            CopyWithRetry(fullPath, backupPath);

            try
            {
                File.Delete(fullPath);
                removed++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                Log(logPath, $"Could not remove delta-listed file {relative}: {exception.Message}");
            }
        }

        if (removed > 0)
        {
            Log(logPath, $"Removed {removed} delta-listed install files.");
        }
    }

    private static void CopyWithRetry(string source, string destination)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < CopyRetryCount; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
            }
            Thread.Sleep(500);
        }
        throw new IOException($"Could not replace {Path.GetFileName(destination)}.", lastError);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Backup cleanup is best-effort; the next update creates a new folder.
        }
    }

    private static void LaunchIsley(string targetDirectory, string launchFile, string logPath)
    {
        var executable = Path.Combine(targetDirectory, launchFile);
        var process = Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = targetDirectory,
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new InvalidOperationException("Windows could not reopen Isley.");
        }
        Log(logPath, $"Reopened Isley as process {process.Id}.");
    }

    private static void TryRelaunchFromArguments(string[] args, string logPath)
    {
        try
        {
            var options = ParseArguments(args);
            var executable = Path.Combine(options.TargetDirectory, options.LaunchFile);
            if (File.Exists(executable))
            {
                Process.Start(new ProcessStartInfo(executable)
                {
                    WorkingDirectory = options.TargetDirectory,
                    UseShellExecute = true
                });
                Log(logPath, "Reopened the existing Isley installation after an update failure.");
            }
        }
        catch
        {
            // The log and result file remain available if even recovery launch is unavailable.
        }
    }

    private static void WriteResult(
        string resultPath,
        bool success,
        string version,
        string error)
    {
        var result = JsonSerializer.Serialize(new
        {
            success,
            version,
            error,
            completedAt = DateTimeOffset.UtcNow
        });
        File.WriteAllText(resultPath, result);
    }

    private static void Log(string path, string message)
    {
        try
        {
            File.AppendAllText(
                path,
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Updating must not depend on diagnostic logging.
        }
    }

    private sealed record UpdateOptions(
        int ProcessId,
        string SourceDirectory,
        string TargetDirectory,
        string LaunchFile,
        string Version,
        bool DeltaMode);
}
