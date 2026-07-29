using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Isley;

internal sealed record StagedIsleyUpdate(
    IsleyRelease Release,
    string PackageDirectory,
    string UpdaterExecutable,
    bool IsDelta);

internal sealed record IsleyReleaseFetchResult(
    IsleyRelease Release,
    bool BetaFallback);

internal static class IsleyUpdateClient
{
    private const int MinimumFullPackageEntries = 20;
    private const int MinimumDeltaPackageEntries = 1;
    private const int MaxBootOkMarkerBytes = 1024;

    private static readonly HttpClient Client = CreateClient();

    internal static async Task<IsleyReleaseFetchResult> FetchReleaseAsync(
        bool preferBeta,
        CancellationToken cancellationToken)
    {
        if (preferBeta)
        {
            try
            {
                var beta = await FetchManifestAsync(
                    IsleyReleaseLogic.BetaReleaseEndpoint,
                    IsleyReleaseLogic.BetaChannel,
                    cancellationToken);
                return new IsleyReleaseFetchResult(beta, BetaFallback: false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A missing, unreachable, or invalid beta manifest never breaks
                // the stable channel; the caller surfaces the fallback honestly.
            }
        }

        var stable = await FetchManifestAsync(
            IsleyReleaseLogic.ReleaseEndpoint,
            IsleyReleaseLogic.StableChannel,
            cancellationToken);
        return new IsleyReleaseFetchResult(stable, BetaFallback: preferBeta);
    }

    internal static async Task<StagedIsleyUpdate> StageAsync(
        IsleyRelease release,
        Version currentVersion,
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
            if (release.Delta is { } delta
                && IsleyReleaseLogic.IsSameVersion(currentVersion, delta.FromVersion))
            {
                try
                {
                    return await StageDeltaAsync(
                        release,
                        delta,
                        stageDirectory,
                        progress,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Any delta mismatch or failure falls back to the full
                    // verified package; a broken delta never bricks an update.
                    DeleteContainedDirectory(updatesRoot, stageDirectory);
                    Directory.CreateDirectory(packageDirectory);
                    progress?.Report(0);
                }
            }

            await DownloadArchiveAsync(
                release.DownloadUri,
                release.Bytes,
                archivePath,
                progress,
                cancellationToken);
            ValidateArchiveHash(archivePath, release.Sha256);
            ExtractArchive(archivePath, packageDirectory, MinimumFullPackageEntries);
            ValidateStagedPackage(packageDirectory, release.Version);

            var updaterExecutable = Path.Combine(
                packageDirectory,
                "Updater",
                "Isley.Updater.exe");
            return new StagedIsleyUpdate(
                release,
                packageDirectory,
                updaterExecutable,
                IsDelta: false);
        }
        catch
        {
            DeleteContainedDirectory(updatesRoot, stageDirectory);
            throw;
        }
    }

    private static async Task<StagedIsleyUpdate> StageDeltaAsync(
        IsleyRelease release,
        IsleyDeltaOffer delta,
        string stageDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var deltaDirectory = Path.Combine(stageDirectory, "delta-package");
        var deltaArchivePath = Path.Combine(stageDirectory, "Isley-delta.zip");
        Directory.CreateDirectory(deltaDirectory);

        await DownloadArchiveAsync(
            delta.DownloadUri,
            delta.Bytes,
            deltaArchivePath,
            progress,
            cancellationToken);
        ValidateArchiveHash(deltaArchivePath, delta.Sha256);
        ExtractArchive(deltaArchivePath, deltaDirectory, MinimumDeltaPackageEntries);
        ValidateDeltaPackage(deltaDirectory, delta, release.Version);

        var updaterExecutable = Path.Combine(
            deltaDirectory,
            "Updater",
            "Isley.Updater.exe");
        return new StagedIsleyUpdate(
            release,
            deltaDirectory,
            updaterExecutable,
            IsDelta: true);
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
        if (staged.IsDelta)
        {
            info.ArgumentList.Add("--mode");
            info.ArgumentList.Add("delta");
        }

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

    internal static void WriteBootOkMarker(string markerPath, string versionText)
    {
        if (!IsleyReleaseLogic.IsValidVersionText(versionText))
        {
            throw new InvalidDataException("The boot confirmation version was invalid.");
        }

        var directory = Path.GetDirectoryName(markerPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("The boot confirmation path was invalid.");
        }

        var json = JsonSerializer.Serialize(new
        {
            version = versionText.Trim(),
            confirmedAt = DateTimeOffset.UtcNow
        });
        if (Encoding.UTF8.GetByteCount(json) > MaxBootOkMarkerBytes)
        {
            throw new InvalidDataException("The boot confirmation exceeded its safety limit.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".last-boot-ok.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, markerPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    internal static bool TryReadBootOkMarker(string markerPath, out string? versionText)
    {
        versionText = null;
        try
        {
            if (!File.Exists(markerPath)
                || new FileInfo(markerPath).Length is 0 or > MaxBootOkMarkerBytes)
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var root = document.RootElement;
            var version = root.TryGetProperty("version", out var versionValue)
                          && versionValue.ValueKind == JsonValueKind.String
                ? versionValue.GetString() ?? string.Empty
                : string.Empty;
            var confirmedAtValid = root.TryGetProperty("confirmedAt", out var confirmedAtValue)
                && confirmedAtValue.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    confirmedAtValue.GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out _);
            if (!IsleyReleaseLogic.IsValidVersionText(version) || !confirmedAtValid)
            {
                return false;
            }

            versionText = version.Trim();
            return true;
        }
        catch
        {
            // A malformed marker is diagnostic only; it must never throw.
            return false;
        }
    }

    private static bool FilesHaveIdenticalSha256(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        var leftHash = SHA256.HashData(left);
        var rightHash = SHA256.HashData(right);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static async Task<IsleyRelease> FetchManifestAsync(
        string endpoint,
        string channel,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
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
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != endpoint
            || response.Content.Headers.ContentLength
                is > IsleyReleaseLogic.MaxManifestBytes)
        {
            throw new InvalidDataException("The Isley release channel returned an unexpected response.");
        }

        var json = await ReadBoundedTextAsync(
            response.Content,
            IsleyReleaseLogic.MaxManifestBytes,
            timeout.Token);
        return IsleyReleaseLogic.ParseManifest(json, DateTimeOffset.UtcNow, channel);
    }

    private static async Task DownloadArchiveAsync(
        Uri downloadUri,
        long expectedBytes,
        string archivePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
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
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != downloadUri.AbsoluteUri
            || response.Content.Headers.ContentLength is { } contentLength
                && contentLength != expectedBytes)
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
            if (total > expectedBytes
                || total > IsleyReleaseLogic.MaximumArchiveBytes)
            {
                throw new InvalidDataException("The Isley update download exceeded its declared size.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var percent = (int)Math.Clamp(total * 100 / expectedBytes, 0, 100);
            if (percent != lastPercent)
            {
                progress?.Report(percent);
                lastPercent = percent;
            }
        }

        await output.FlushAsync(cancellationToken);
        if (total != expectedBytes)
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

    private static void ExtractArchive(
        string archivePath,
        string packageDirectory,
        int minimumEntries)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count < minimumEntries
            || archive.Entries.Count > IsleyReleaseLogic.MaximumArchiveEntries)
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

    private static void ValidateDeltaPackage(
        string packageDirectory,
        IsleyDeltaOffer delta,
        Version expectedVersion)
    {
        var manifestPath = Path.Combine(packageDirectory, "isley-delta-manifest.json");
        if (!File.Exists(manifestPath)
            || new FileInfo(manifestPath).Length is 0
                or > IsleyReleaseLogic.MaxDeltaManifestBytes)
        {
            throw new InvalidDataException("The Isley delta update was missing its file list.");
        }

        _ = IsleyReleaseLogic.ParseDeltaManifest(
            File.ReadAllText(manifestPath),
            delta.FromVersion,
            expectedVersion);

        // Delta packages always carry the updater helper so the install-side
        // delete-list step runs the verified new helper, never an older one.
        if (!File.Exists(Path.Combine(packageDirectory, "Updater", "Isley.Updater.exe")))
        {
            throw new InvalidDataException("The Isley delta update was missing the updater helper.");
        }

        var isleyLibrary = Path.Combine(packageDirectory, "Isley.dll");
        if (File.Exists(isleyLibrary))
        {
            var stagedVersion = AssemblyName
                .GetAssemblyName(isleyLibrary)
                .Version
                ?? new Version(0, 0, 0);
            if (stagedVersion.CompareTo(expectedVersion) < 0)
            {
                throw new InvalidDataException("The Isley delta archive contained an older application build.");
            }
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
