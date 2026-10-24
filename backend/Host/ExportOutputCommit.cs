using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace Ras.Backend;

/// <summary>
/// Output-only no-clobber publication. The named mutex serializes cooperating
/// backend children for one canonical destination; File.Move still provides
/// the final atomic no-overwrite check against unrelated writers. Mutex ownership
/// is thread-affine, so callers must acquire and dispose the lease on the same
/// synchronous worker thread.
/// </summary>
internal sealed class ExportDestinationLease : IDisposable
{
    private readonly Mutex mutex;
    private bool ownsMutex;

    private ExportDestinationLease(Mutex mutex, bool ownsMutex)
    {
        this.mutex = mutex;
        this.ownsMutex = ownsMutex;
    }

    public static ExportDestinationLease Acquire(string destination)
    {
        string canonical = Path.GetFullPath(destination);
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string name = "HECRAS-Export-" + Convert.ToHexString(key);
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Another export is publishing this destination.");
            return new(mutex, ownsMutex: true);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (ownsMutex)
        {
            ownsMutex = false;
            mutex.ReleaseMutex();
        }
        mutex.Dispose();
    }
}

internal static class ExportOutputCommit
{
    private static readonly string[] ShapefileFamilyExtensions = [".shp", ".shx", ".dbf", ".prj", ".cpg"];
    private static readonly string[] ShapefilePublishOrder = [".shx", ".dbf", ".prj", ".cpg", ".shp"];

    internal sealed record PreparedSingleFile(string StagedPath, string DestinationPath,
        ExportArtifactReceipt Receipt);

    internal sealed record PreparedShapefileFamily(string[] StagedPaths, string[] OutputPaths,
        ExportArtifactReceipt[] Receipts);

    public static string ValidateOutputPath(string rawPath, string extension)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) throw new ArgumentException("Choose an export destination.");
        string path = Path.GetFullPath(rawPath);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The output file must use the {extension} extension.");
        string? directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The export destination folder does not exist.");
        return path;
    }

    public static string CreateTemporaryFilePath(string destination)
    {
        string directory = Path.GetDirectoryName(destination)!;
        string stem = Path.GetFileNameWithoutExtension(destination);
        return Path.Combine(directory, $".{stem}.{Guid.NewGuid():N}.tmp{Path.GetExtension(destination)}");
    }

    public static string CreateTemporaryDirectory(string destination)
    {
        string directory = Path.GetDirectoryName(destination)!;
        string stem = Path.GetFileNameWithoutExtension(destination);
        string path = Path.Combine(directory, $".{stem}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(path);
        return path;
    }

    public static ExportArtifactReceipt[] CommitSingleFile(string stagedPath, string destination)
    {
        PreparedSingleFile prepared = PrepareSingleFile(stagedPath, destination);
        VerifyPreparedSingleFile(prepared);
        return CommitSingleFile(prepared);
    }

    internal static PreparedSingleFile PrepareSingleFile(string stagedPath, string destination)
    {
        if (!File.Exists(stagedPath)) throw new IOException("The source exporter did not create its output file.");
        string staged = Path.GetFullPath(stagedPath);
        string output = Path.GetFullPath(destination);
        ExportArtifactReceipt receipt = Describe(staged) with { Path = output };
        return new(staged, output, receipt);
    }

    internal static ExportArtifactReceipt[] CommitSingleFile(PreparedSingleFile prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        EnsureAbsent(prepared.DestinationPath);
        File.Move(prepared.StagedPath, prepared.DestinationPath);
        return [prepared.Receipt];
    }

    internal static void VerifyPreparedSingleFile(PreparedSingleFile prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        VerifyPreparedFile(prepared.StagedPath, prepared.Receipt.SHA256);
    }

    public static ExportArtifactReceipt[] CommitShapefileFamily(string stagedShpPath,
        string destinationShpPath)
    {
        PreparedShapefileFamily prepared = PrepareShapefileFamily(stagedShpPath, destinationShpPath);
        VerifyPreparedShapefileFamily(prepared);
        return CommitPreparedShapefileFamily(prepared);
    }

    internal static PreparedShapefileFamily PrepareShapefileFamily(string stagedShpPath,
        string destinationShpPath)
    {
        string stagedBase = Path.ChangeExtension(stagedShpPath, null);
        string outputBase = Path.ChangeExtension(destinationShpPath, null);
        string[] requiredExtensions = [".shp", ".shx", ".dbf"];
        string[] extensions = ShapefilePublishOrder
            .Where(extension => requiredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                || File.Exists(stagedBase + extension)).ToArray();
        string[] staged = extensions.Select(extension => Path.GetFullPath(stagedBase + extension)).ToArray();
        string[] output = extensions.Select(extension => Path.GetFullPath(outputBase + extension)).ToArray();

        foreach (string extension in requiredExtensions)
            if (!File.Exists(stagedBase + extension))
                throw new IOException("The retained shapefile writer returned an incomplete shapefile family.");
        foreach (string extension in ShapefileFamilyExtensions)
            EnsureAbsent(outputBase + extension);
        ExportArtifactReceipt[] receipts = staged.Select((path, index) => Describe(path) with
        {
            Path = output[index]
        }).ToArray();
        return new(staged, output, receipts);
    }

    internal static ExportArtifactReceipt[] CommitPreparedShapefileFamily(PreparedShapefileFamily prepared) =>
        CommitPreparedShapefileFamily(prepared, File.Move);

    internal static ExportArtifactReceipt[] CommitPreparedShapefileFamily(
        PreparedShapefileFamily prepared, Action<string, string> publishFile)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(publishFile);
        if (prepared.StagedPaths.Length != prepared.OutputPaths.Length ||
            prepared.StagedPaths.Length != prepared.Receipts.Length || prepared.StagedPaths.Length < 3)
            throw new ArgumentException("The prepared Shapefile family is incomplete.", nameof(prepared));

        foreach (string extension in ShapefileFamilyExtensions)
            EnsureAbsent(Path.ChangeExtension(prepared.OutputPaths[^1], null) + extension);

        var moved = new List<(string Path, string SHA256)>(prepared.OutputPaths.Length);
        try
        {
            for (int index = 0; index < prepared.StagedPaths.Length; index++)
            {
                publishFile(prepared.StagedPaths[index], prepared.OutputPaths[index]);
                moved.Add((prepared.OutputPaths[index], prepared.Receipts[index].SHA256));
            }
            return prepared.Receipts;
        }
        catch (Exception publicationError)
        {
            List<string> preserved = [];
            foreach ((string path, string sha256) in moved.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    if (!Describe(path).SHA256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        preserved.Add(path + " (contents changed after publication)");
                        continue;
                    }
                    File.Delete(path);
                }
                catch (Exception error)
                {
                    preserved.Add(path + " (" + error.Message + ")");
                }
            }
            if (preserved.Count > 0)
                throw new IOException(publicationError.Message + " Shapefile components changed or could not be removed during rollback and were preserved: "
                    + string.Join(", ", preserved), publicationError);
            throw;
        }
    }

    internal static void VerifyPreparedShapefileFamily(PreparedShapefileFamily prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.StagedPaths.Length != prepared.Receipts.Length)
            throw new ArgumentException("The prepared Shapefile family is incomplete.", nameof(prepared));
        for (int index = 0; index < prepared.StagedPaths.Length; index++)
            VerifyPreparedFile(prepared.StagedPaths[index], prepared.Receipts[index].SHA256);
    }

    public static void EnsureNoClobber(string destination)
    {
        string outputBase = Path.ChangeExtension(destination, null);
        if (string.Equals(Path.GetExtension(destination), ".shp", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string extension in ShapefileFamilyExtensions) EnsureAbsent(outputBase + extension);
        }
        else EnsureAbsent(destination);
    }

    public static ExportArtifactReceipt Describe(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        string hash = Convert.ToHexString(SHA256.HashData(input));
        long size = input.Length;
        return new(Path.GetFullPath(path), size, hash);
    }

    private static void VerifyPreparedFile(string path, string expectedSHA256)
    {
        if (!File.Exists(path)) throw new IOException("A staged export component is missing before publication: " + path);
        if (!Describe(path).SHA256.Equals(expectedSHA256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A staged export component changed after its receipt was prepared: " + path);
    }

    public static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    internal static void TryCleanup(Action cleanup, string description)
    {
        try { cleanup(); }
        catch (Exception error) { Trace.TraceWarning("Could not clean up {0}: {1}", description, error); }
    }

    internal static async Task StopAndObserveAsync(CancellationTokenSource stop, Task? backgroundTask,
        string description)
    {
        try { stop.Cancel(); }
        catch (Exception error) { Trace.TraceWarning("Could not stop {0}: {1}", description, error); }
        if (backgroundTask is null) return;
        try { await backgroundTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Trace.TraceWarning("{0} failed while shutting down: {1}", description, error); }
    }

    public static bool IsSameOrWithin(string path, string directory)
    {
        string candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(path), Path.GetFullPath(directory), comparison) ||
            candidate.StartsWith(root, comparison);
    }

    private static void EnsureAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("The export destination already exists; choose a new name.");
    }
}
