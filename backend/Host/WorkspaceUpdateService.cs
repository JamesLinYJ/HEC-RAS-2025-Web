using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ras.Updates;
using Utility.Communication;
using Utility.Progress;

namespace Ras.Backend;

public static class WorkspaceUpdateService
{
    private sealed record Channel(string ID, string Title, string Bucket, bool RequiresCredentials, bool DebugOnly);
    internal sealed record Package(MappedFile File, string Platform, string Architecture);

    private static readonly Channel Beta = new("Beta", "Beta", Updater.BetaReleasesS3Bucket, false, false);
    private static readonly Channel Nightly = new("Nightly", "Live-Dev", Updater.NightlyReleasesS3Bucket, true, false);
    private static readonly Channel Staging = new("Staging", "Staging", Updater.StagingReleasesS3Bucket, true, true);

    public static async Task<WorkspaceUpdateCatalogResponse> CatalogAsync(
        WorkspaceUpdateCatalogRequest request, Action<BackendOperationProgress>? report = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCredentials(request.Credentials);
        report?.Invoke(new BackendOperationProgress(TaskName: "Checking release channels"));

        var available = new List<Channel> { Beta };
        bool hasCredentials = request.Credentials is not null;
        bool anyAuthenticated = false;
        if (request.Credentials is not null)
        {
            foreach (Channel channel in new[] { Nightly, Staging })
            {
                if (channel.DebugOnly && !request.DebugMode) continue;
                bool authenticated = await IsAuthenticated(channel.Bucket, request.Credentials);
                if (authenticated)
                {
                    available.Add(channel);
                    anyAuthenticated = true;
                }
            }
        }

        string credentialStatus = !hasCredentials ? "notConfigured"
            : anyAuthenticated ? "authenticated" : "invalid";
        Channel? selected = available.FirstOrDefault(channel =>
            string.Equals(channel.ID, request.Channel, StringComparison.Ordinal));
        if (selected is null || (selected.DebugOnly && !request.DebugMode)) selected = Beta;
        bool requestedPrivilegedChannel = IsPrivilegedChannel(request.Channel) &&
            available.All(channel => !string.Equals(channel.ID, request.Channel, StringComparison.Ordinal));
        if (requestedPrivilegedChannel && hasCredentials && !anyAuthenticated) credentialStatus = "invalid";

        string selectedBranch = "master";
        string[] branches = Array.Empty<string>();
        ReleaseCollection releases = await FetchReleasesAsync(selected, request.Credentials);
        IReadOnlyList<ReleaseDetails> selectedReleases = releases.AllReleases ?? new List<ReleaseDetails>();
        if (selected.ID == Nightly.ID)
        {
            branches = selectedReleases.Select(release => ExtractBranch(release.Name))
                .Where(branch => !string.IsNullOrWhiteSpace(branch))
                .Distinct(StringComparer.Ordinal).OrderBy(branch => branch, StringComparer.Ordinal).ToArray();
            selectedBranch = branches.Contains(request.Branch, StringComparer.Ordinal) ? request.Branch
                : branches.Contains("master", StringComparer.Ordinal) ? "master"
                : branches.FirstOrDefault() ?? "master";
            selectedReleases = selectedReleases.Where(release =>
                string.Equals(ExtractBranch(release.Name), selectedBranch, StringComparison.Ordinal)).ToArray();
        }

        var projected = new WorkspaceUpdateRelease[selectedReleases.Count];
        for (int index = 0; index < selectedReleases.Count; index++)
        {
            ReleaseDetails release = selectedReleases[index];
            report?.Invoke(new BackendOperationProgress(Fraction: selectedReleases.Count == 0 ? null :
                (double)index / selectedReleases.Count, TaskName: "Inspecting release packages"));
            projected[index] = await ProjectReleaseAsync(selected, selectedBranch, release, request.Credentials);
        }

        string? latestVersion = selectedReleases.FirstOrDefault()?.Version;
        bool? upToDate = IsUpToDate(request.InstalledVersion, latestVersion);
        var channelChoices = available.Select(channel => new SourceEditorChoice(channel.ID, channel.Title,
            DebugOnly: channel.DebugOnly ? true : null)).ToArray();
        report?.Invoke(new BackendOperationProgress(Fraction: 1, TaskName: "Release catalog ready"));
        return new WorkspaceUpdateCatalogResponse(channelChoices, branches, selected.ID, selectedBranch,
            channelChoices.Length > 1, hasCredentials, credentialStatus, projected, upToDate);
    }

    public static async Task<WorkspaceUpdatePackageReceipt> PreparePackageAsync(
        WorkspaceUpdatePackagePrepareRequest request, Action<BackendOperationProgress>? report = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCredentials(request.Credentials);
        if (string.IsNullOrWhiteSpace(request.PackageManifestKey) || string.IsNullOrWhiteSpace(request.PackageFilename) ||
            !IsSHA256(request.PackageSHA256))
            throw new ArgumentException("The selected update package identity is invalid.", nameof(request));

        Channel channel = ResolveChannel(request.Channel, request.DebugMode);
        if (channel.RequiresCredentials)
        {
            if (request.Credentials is null || !await IsAuthenticated(channel.Bucket, request.Credentials))
                throw new InvalidOperationException("Developer credentials are no longer valid for this update channel.");
        }

        ReleaseCollection releases = await FetchReleasesAsync(channel, request.Credentials);
        ReleaseDetails[] releaseMatches = releases.AllReleases?.Where(candidate =>
            string.Equals(candidate.ManifestJSONKey, request.PackageManifestKey, StringComparison.Ordinal)).ToArray()
            ?? Array.Empty<ReleaseDetails>();
        if (releaseMatches.Length != 1)
            throw new InvalidOperationException("The selected update release is no longer unique in the current release catalog.");
        ReleaseDetails release = releaseMatches[0];
        if (channel.ID == Nightly.ID && !string.Equals(ExtractBranch(release.Name), request.Branch, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected update release belongs to a different branch.");

        ReleaseManifest manifest = await ReadReleaseManifestAsync(channel, release, request.Credentials);
        if (manifest.Details is null || manifest.Files is null ||
            (manifest.Details.Version is not null && !string.Equals(manifest.Details.Version, release.Version, StringComparison.Ordinal)) ||
            (manifest.Details.ManifestJSONKey is not null && !string.Equals(manifest.Details.ManifestJSONKey, release.ManifestJSONKey, StringComparison.Ordinal)))
            throw new InvalidDataException("The selected update manifest no longer matches the release catalog.");
        MappedFile[] matching = manifest.Files?.Where(file =>
            string.Equals(file.Filename, request.PackageFilename, StringComparison.Ordinal) &&
            string.Equals(file.SHA256, request.PackageSHA256, StringComparison.OrdinalIgnoreCase)).ToArray()
            ?? Array.Empty<MappedFile>();
        if (matching.Length != 1)
            throw new InvalidOperationException("The selected package no longer matches its release manifest.");
        Package? package = ClassifyMacPackage(matching[0]);
        if (package is null || !IsRunnableArchitecture(package.Architecture))
            throw new InvalidOperationException("The selected release does not contain a compatible macOS installer package.");
        string expectedReleaseID = ReleaseIdentity(channel.ID, request.Branch, release, package.File);
        if (!string.Equals(request.ReleaseID, expectedReleaseID, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected update package identity has changed.");

        string cache = RasSpecialDirectories.UpdaterCacheDirectory;
        Directory.CreateDirectory(cache);
        string digest = package.File.SHA256.ToLowerInvariant();
        string extension = Path.GetExtension(package.File.Filename).ToLowerInvariant();
        string finalPath = Path.Combine(cache, "hecras-update-" + digest + extension);
        if (File.Exists(finalPath))
        {
            if (await FileMatchesAsync(finalPath, package.File.Length, package.File.SHA256))
                return new WorkspaceUpdatePackageReceipt(finalPath, Path.GetFileName(package.File.Filename),
                    digest, package.File.Length, package.Platform, package.Architecture);
            File.Delete(finalPath);
        }

        string partialPath = Path.Combine(cache, ".hecras-update-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            report?.Invoke(new BackendOperationProgress(TaskName: "Downloading update package"));
            using var transfer = new S3Client("https://s3.hecdev.net",
                request.Credentials?.AccessKey ?? string.Empty, request.Credentials?.SecretKey ?? string.Empty,
                forcePathStyleQueries: true);
            ProgressReporter reporter = new(new UpdateProgressTarget(report));
            await transfer.PullSingleFileParallelAsync(channel.Bucket, package.File.Filename, partialPath,
                reporter, ct: CancellationToken.None);
            if (!await FileMatchesAsync(partialPath, package.File.Length, package.File.SHA256))
                throw new InvalidDataException("The downloaded update package did not match the release manifest.");
            try { File.Move(partialPath, finalPath); }
            catch (IOException error)
            {
                if (!File.Exists(finalPath) ||
                    !await FileMatchesAsync(finalPath, package.File.Length, package.File.SHA256))
                    throw new IOException("The verified update package could not be published to its cache.", error);
                File.Delete(partialPath);
            }
            report?.Invoke(new BackendOperationProgress(Fraction: 1, TaskName: "Update package verified"));
            return new WorkspaceUpdatePackageReceipt(finalPath, Path.GetFileName(package.File.Filename),
                digest, package.File.Length, package.Platform, package.Architecture);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); }
                catch { /* A process interruption may leave a private partial file for later cleanup. */ }
            }
        }
    }

    internal static Package? ClassifyMacPackage(MappedFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Filename) || !IsSHA256(file.SHA256) || file.Length <= 0)
            return null;
        string extension = Path.GetExtension(file.Filename);
        if (!string.Equals(extension, ".pkg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".dmg", StringComparison.OrdinalIgnoreCase)) return null;
        string[] tokens = System.Text.RegularExpressions.Regex.Split(file.Filename.ToLowerInvariant(), "[^a-z0-9]+");
        bool explicitlyMac = tokens.Any(token => token is "mac" or "macos" or "darwin" or "osx");
        if (!explicitlyMac || tokens.Any(token => token is "windows" or "win" or "win64" or "linux")) return null;
        string? architecture = tokens.Any(token => token is "universal" or "universal2") ? "universal"
            : tokens.Any(token => token is "arm64" or "aarch64") && !tokens.Any(token => token is "x64" or "x86_64" or "x86") ? "arm64" : null;
        return architecture is null ? null : new Package(file, "macOS", architecture);
    }

    internal static bool? IsUpToDate(string installedVersion, string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion)) return null;
        if (installedVersion == "0.1.0-localdev") return true;
        string installed = BaseVersion(installedVersion);
        string latest = BaseVersion(latestVersion);
        if (!Version.TryParse(installed, out Version? current) || !Version.TryParse(latest, out Version? available))
            return null;
        return current >= available;
    }

    private static async Task<WorkspaceUpdateRelease> ProjectReleaseAsync(Channel channel, string branch,
        ReleaseDetails release, WorkspaceUpdateCredentials? credentials)
    {
        ReleaseManifest? manifest;
        try { manifest = await ReadReleaseManifestAsync(channel, release, credentials); }
        catch (Exception)
        {
            return ReleaseRow(channel, branch, release, null, "manifestUnavailable");
        }
        if (manifest.Details is null || manifest.Files is null ||
            (manifest.Details.Version is not null && !string.Equals(manifest.Details.Version, release.Version, StringComparison.Ordinal)) ||
            (manifest.Details.ManifestJSONKey is not null && !string.Equals(manifest.Details.ManifestJSONKey, release.ManifestJSONKey, StringComparison.Ordinal)))
            return ReleaseRow(channel, branch, release, null, "invalidManifest");
        Package? best = manifest.Files.Select(ClassifyMacPackage).Where(package => package is not null)
            .Cast<Package>().Where(package => IsRunnableArchitecture(package.Architecture))
            .OrderBy(package => package.Architecture == "arm64" ? 0 : 1).FirstOrDefault();
        return ReleaseRow(channel, branch, release, best,
            best is null ? "unavailable" : "available");
    }

    private static WorkspaceUpdateRelease ReleaseRow(Channel channel, string branch, ReleaseDetails release,
        Package? package, string packageStatus)
    {
        string id = ReleaseIdentity(channel.ID, branch, release, package?.File);
        DateTimeOffset? date = release.ReleaseDate == default ? null
            : new DateTimeOffset(DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc));
        string? notesURL = Uri.TryCreate(release.ReleaseNotesURL, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
        return new WorkspaceUpdateRelease(id, release.Version ?? string.Empty, date,
            release.ReleaseNotesSummary ?? string.Empty, notesURL,
            package?.Platform ?? "unknown", package?.Architecture ?? "unknown",
            package?.File.Filename, package?.File.SHA256, package?.File.Length,
            release.ManifestJSONKey, packageStatus);
    }

    private static async Task<ReleaseManifest> ReadReleaseManifestAsync(Channel channel,
        ReleaseDetails release, WorkspaceUpdateCredentials? credentials)
    {
        if (string.IsNullOrWhiteSpace(release.ManifestJSONKey))
            throw new InvalidDataException("The release manifest key is missing.");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "HEC", "RAS", "UpdateManifests");
        Directory.CreateDirectory(tempDirectory);
        string path = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var transfer = new S3TransferHelper("https://s3.hecdev.net", channel.Bucket,
                credentials?.AccessKey ?? string.Empty, credentials?.SecretKey ?? string.Empty,
                forcePathStyle: true);
            if (!await transfer.ObjectExists(release.ManifestJSONKey))
                throw new FileNotFoundException("The selected release manifest is not available.");
            await transfer.PullSingleFileAsync(path, release.ManifestJSONKey);
            return ReleaseManifest.DeserializeJSON(await File.ReadAllTextAsync(path))
                ?? throw new InvalidDataException("The release manifest is empty.");
        }
        finally
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* Keep manifest cleanup from hiding the source result. */ }
            }
        }
    }

    private static async Task<ReleaseCollection> FetchReleasesAsync(Channel channel,
        WorkspaceUpdateCredentials? credentials)
    {
        string user = channel.RequiresCredentials ? credentials?.AccessKey ?? string.Empty : string.Empty;
        string secret = channel.RequiresCredentials ? credentials?.SecretKey ?? string.Empty : string.Empty;
        return await Updater.TryGetReleasesAsync(string.Empty, channel.Bucket, user, secret)
            ?? throw new InvalidOperationException("The release catalog could not be loaded from the selected channel.");
    }

    private static Channel ResolveChannel(string id, bool debugMode) => id switch
    {
        "Beta" => Beta,
        "Nightly" => Nightly,
        "Staging" when debugMode => Staging,
        "Staging" => throw new InvalidOperationException("The Staging update channel is available only in debug mode."),
        _ => throw new InvalidOperationException("The selected update channel is not supported.")
    };

    private static bool IsPrivilegedChannel(string id) => id is "Nightly" or "Staging";

    private static async Task<bool> IsAuthenticated(string bucket, WorkspaceUpdateCredentials? credentials)
    {
        if (credentials is null) return false;
        using var client = new S3Client("https://s3.hecdev.net", credentials.AccessKey,
            credentials.SecretKey, forcePathStyleQueries: true);
        return await client.IsAuthenticatedForBucket(bucket);
    }

    private static void ValidateCredentials(WorkspaceUpdateCredentials? credentials)
    {
        if (credentials is not null && (string.IsNullOrWhiteSpace(credentials.AccessKey) ||
            string.IsNullOrWhiteSpace(credentials.SecretKey)))
            throw new ArgumentException("The configured S3 credentials are incomplete.", nameof(credentials));
    }

    private static bool IsRunnableArchitecture(string architecture) =>
        architecture == "universal" || (architecture == "arm64" && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);

    private static string ExtractBranch(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "master";
        int start = name.IndexOf('(');
        int end = name.IndexOf(')');
        return start >= 0 && end > start ? name[(start + 1)..end].Trim() : "master";
    }

    private static string ReleaseIdentity(string channel, string branch, ReleaseDetails release, MappedFile? package) =>
        string.Join("\n", channel, branch, release.Name ?? string.Empty, release.Version ?? string.Empty,
            release.ManifestJSONKey ?? string.Empty, package?.Filename ?? string.Empty, package?.SHA256 ?? string.Empty);

    private static string BaseVersion(string value)
    {
        string withoutBuild = value.Split('+')[0];
        return withoutBuild.Split('-')[0];
    }

    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static async Task<bool> FileMatchesAsync(string path, long expectedLength, string expectedSHA256)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedLength) return false;
        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        return string.Equals(actual, expectedSHA256, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UpdateProgressTarget(Action<BackendOperationProgress>? report) : IProgressTarget
    {
        public void WriteMessage(ProgressReporter sender, Utility.Logging.Message message) =>
            report?.Invoke(new BackendOperationProgress(Message: message.EvaluatedMessage,
                TaskName: sender.FullyQualifiedTaskName));

        public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress) =>
            report?.Invoke(new BackendOperationProgress(Fraction: Math.Clamp(globalProgress, 0, 100) / 100d,
                TaskName: sender.FullyQualifiedTaskName));
    }
}
