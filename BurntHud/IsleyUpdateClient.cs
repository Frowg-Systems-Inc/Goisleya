using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Isley;

internal sealed record StagedIsleyUpdate(
    IsleyRelease Release,
    string PackageDirectory,
    string UpdaterExecutable);

internal static class IsleyUpdateClient
{
    private static readonly HttpClient Client = CreateClient();

    internal static async Task<IsleyRelease> FetchReleaseAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            IsleyReleaseLogic.ReleaseEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri
                != IsleyReleaseLogic.ReleaseEndpoint
            || response.Content.Headers.ContentLength
                is > IsleyReleaseLogic.MaxManifestBytes)
        {
            throw new InvalidDataException("The Isley release channel returned an unexpected response.");
        }

        var json = await ReadBoundedTextAsync(
            response.Content,
            IsleyReleaseLogic.MaxManifestBytes,
            timeout.Token);
        return IsleyReleaseLogic.ParseManifest(json, DateTimeOffset.UtcNow);
    }

    internal static async Task<StagedIsleyUpdate> StageAsync(
        IsleyRelease release,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var updatesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Updates");
        var stageDirectory = Path.Combine(updatesRoot, release.VersionText);
        var packageDirectory = Path.Combine(stageDirectory, "package");
        var archivePath = Path.Combine(stageDirectory, "Isley-Windows-x64.zip");

        Directory.CreateDirectory(updatesRoot);
        DeleteContainedDirectory(updatesRoot, stageDirectory);
        Directory.CreateDirectory(packageDirectory);

        try
        {
            await DownloadArchiveAsync(
                release,
                archivePath,
                progress,
                cancellationToken);
            ValidateArchiveHash(archivePath, release.Sha256);
            ExtractArchive(archivePath, packageDirectory);
            ValidateStagedPackage(packageDirectory, release.Version);

            var updaterExecutable = Path.Combine(
                packageDirectory,
                "Updater",
                "Isley.Updater.exe");
            return new StagedIsleyUpdate(
                release,
                packageDirectory,
                updaterExecutable);
        }
        catch
        {
            DeleteContainedDirectory(updatesRoot, stageDirectory);
            throw;
        }
    }

    internal static bool CanWriteInstallDirectory(string installDirectory)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(installDirectory);
            Directory.CreateDirectory(fullDirectory);
            var probePath = Path.Combine(
                fullDirectory,
                $".isley-update-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static Process LaunchUpdater(
        StagedIsleyUpdate staged,
        int currentProcessId,
        string installDirectory)
    {
        if (!File.Exists(staged.UpdaterExecutable))
        {
            throw new FileNotFoundException(
                "The Isley updater helper is missing.",
                staged.UpdaterExecutable);
        }

        var updaterExecutable = ResolveUpdaterExecutable(
            staged.UpdaterExecutable,
            installDirectory);
        var info = new ProcessStartInfo
        {
            FileName = updaterExecutable,
            WorkingDirectory = Path.GetDirectoryName(updaterExecutable)!,
            UseShellExecute = false,
            // Do not hide the helper. A silent CreateNoWindow launch from the
            // freshly downloaded Updates tree is a common Defender ML false-positive
            // pattern even when the updater is hash-verified and benign.
            CreateNoWindow = false
        };
        info.ArgumentList.Add("--pid");
        info.ArgumentList.Add(currentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--source");
        info.ArgumentList.Add(staged.PackageDirectory);
        info.ArgumentList.Add("--target");
        info.ArgumentList.Add(Path.GetFullPath(installDirectory));
        info.ArgumentList.Add("--launch");
        info.ArgumentList.Add("Isley.exe");
        info.ArgumentList.Add("--version");
        info.ArgumentList.Add(staged.Release.VersionText);

        return Process.Start(info)
               ?? throw new InvalidOperationException("Windows could not start the Isley updater.");
    }

    internal static string ResolveUpdaterExecutable(
        string stagedUpdaterExecutable,
        string installDirectory)
    {
        var installedUpdater = Path.Combine(
            Path.GetFullPath(installDirectory),
            "Updater",
            "Isley.Updater.exe");
        if (!File.Exists(installedUpdater))
        {
            return stagedUpdaterExecutable;
        }

        // Reuse the already-installed helper when it matches the staged package.
        // Launching a brand-new EXE from %LocalAppData%\Isley\Updates\... looks like
        // a dropper to heuristic scanners even though the bytes are hash-verified.
        return FilesHaveIdenticalSha256(installedUpdater, stagedUpdaterExecutable)
            ? installedUpdater
            : stagedUpdaterExecutable;
    }

    private static bool FilesHaveIdenticalSha256(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        var leftHash = SHA256.HashData(left);
        var rightHash = SHA256.HashData(right);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static async Task DownloadArchiveAsync(
        IsleyRelease release,
        string archivePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUri);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri
                != IsleyReleaseLogic.StableDownloadUrl
            || response.Content.Headers.ContentLength is { } contentLength
                && contentLength != release.Bytes)
        {
            throw new InvalidDataException("The Isley update download did not match its release notice.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[65536];
        long total = 0;
        var lastPercent = -1;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > release.Bytes
                || total > IsleyReleaseLogic.MaximumArchiveBytes)
            {
                throw new InvalidDataException("The Isley update download exceeded its declared size.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var percent = (int)Math.Clamp(total * 100 / release.Bytes, 0, 100);
            if (percent != lastPercent)
            {
                progress?.Report(percent);
                lastPercent = percent;
            }
        }

        await output.FlushAsync(cancellationToken);
        if (total != release.Bytes)
        {
            throw new InvalidDataException("The Isley update download was incomplete.");
        }
        progress?.Report(100);
    }

    private static void ValidateArchiveHash(string archivePath, string expectedSha256)
    {
        using var stream = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(expectedSha256)))
        {
            throw new InvalidDataException("The Isley update failed its SHA-256 safety check.");
        }
    }

    private static void ExtractArchive(string archivePath, string packageDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is < 20 or > IsleyReleaseLogic.MaximumArchiveEntries)
        {
            throw new InvalidDataException("The Isley update archive was incomplete or oversized.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("The Isley update contained an unsupported link.");
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > IsleyReleaseLogic.MaximumExpandedBytes)
            {
                throw new InvalidDataException("The Isley update expanded beyond its safety limit.");
            }

            var destination = IsleyReleaseLogic.ResolveSafePackageEntry(
                packageDirectory,
                entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private static void ValidateStagedPackage(string packageDirectory, Version expectedVersion)
    {
        var requiredFiles = new[]
        {
            Path.Combine(packageDirectory, "Isley.exe"),
            Path.Combine(packageDirectory, "Isley.dll"),
            Path.Combine(packageDirectory, "Map", "index.html"),
            Path.Combine(packageDirectory, "Map", "isley-map-controller.js"),
            Path.Combine(packageDirectory, "Voice", "voice.html"),
            Path.Combine(packageDirectory, "Voice", "voice.js"),
            Path.Combine(packageDirectory, "Voice", "voice-crypto.js"),
            Path.Combine(packageDirectory, "Voice", "voice.css"),
            Path.Combine(packageDirectory, "VoiceServer", "Isley.VoiceServer.exe"),
            Path.Combine(packageDirectory, "VoiceServer", "Isley.VoiceServer.dll"),
            Path.Combine(packageDirectory, "VoiceServer", "appsettings.json"),
            Path.Combine(packageDirectory, "Updater", "Isley.Updater.exe")
        };
        if (requiredFiles.Any(path => !File.Exists(path)))
        {
            throw new InvalidDataException("The Isley update was missing required application files.");
        }

        var stagedVersion = AssemblyName
            .GetAssemblyName(Path.Combine(packageDirectory, "Isley.dll"))
            .Version
            ?? new Version(0, 0, 0);
        if (stagedVersion.CompareTo(expectedVersion) < 0)
        {
            throw new InvalidDataException("The Isley update archive contained an older application build.");
        }
    }

    private static void DeleteContainedDirectory(string rootDirectory, string targetDirectory)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(targetDirectory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                target.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clear an unsafe update directory.");
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(block.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The Isley release notice exceeded its safety limit.");
            }
            buffer.Write(block, 0, read);
        }
        return Encoding.UTF8.GetString(
            buffer.GetBuffer(),
            0,
            checked((int)buffer.Length));
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley-Updater/1.2");
        return client;
    }
}
