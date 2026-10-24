using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;

namespace Ras.Backend;

/// <summary>
/// Serializes OSM cache reads, clears, and final writes across backend child
/// processes. Network fetches carry the persistent epoch observed before the
/// fetch so a response started before a clear cannot repopulate the cache.
/// </summary>
internal sealed class BackgroundMapTileDiskCache
{
    private static readonly TimeSpan LeaseRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LeaseAcquisitionTimeout = TimeSpan.FromSeconds(20);
    private readonly string directory;
    private readonly string lockPath;
    private readonly string epochPath;

    internal BackgroundMapTileDiskCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        string parentDirectory = Path.GetDirectoryName(this.directory) ?? this.directory;
        Directory.CreateDirectory(parentDirectory);
        lockPath = this.directory + ".cache-lock";
        epochPath = this.directory + ".cache-epoch";
    }

    internal string DirectoryPath => directory;

    // This is exposed internally so the backend regression can hold the same
    // OS-level lease while a fresh child process attempts a cache operation.
    internal string LockFilePathForChecks => lockPath;
    internal string EpochFilePathForChecks => epochPath;

    internal string PathFor(OsmCpuTileCoordinate tile)
    {
        if (tile.Zoom < 0 || tile.Column < 0 || tile.TmsRow < 0)
            throw new ArgumentOutOfRangeException(nameof(tile));
        return Path.Combine(directory,
            tile.Zoom.ToString(CultureInfo.InvariantCulture),
            tile.Column.ToString(CultureInfo.InvariantCulture),
            tile.TmsRow.ToString(CultureInfo.InvariantCulture));
    }

    internal async Task<(byte[]? Bytes, long Generation)> ReadAsync(OsmCpuTileCoordinate tile,
        CancellationToken cancellationToken)
    {
        string path = PathFor(tile);
        using FileStream lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
        long capturedGeneration = ReadGeneration();
        if (!File.Exists(path)) return (null, capturedGeneration);
        byte[] cached = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (IsGzip(cached)) return (cached, capturedGeneration);
        File.Delete(path);
        return (null, capturedGeneration);
    }

    internal async Task WriteIfCurrentAsync(OsmCpuTileCoordinate tile, byte[] bytes, long capturedGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string path = PathFor(tile);
        using FileStream lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
        if (capturedGeneration != ReadGeneration()) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal (long Bytes, bool CanClear, bool Cleared) InspectOrClear(bool clear)
    {
        using FileStream lease = AcquireLease();
        bool existed = Directory.Exists(directory);
        if (clear)
        {
            long nextGeneration = checked(ReadGeneration() + 1);
            WriteGeneration(nextGeneration);
            if (existed)
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                    File.Delete(file);
                foreach (string child in Directory.EnumerateDirectories(directory))
                    Directory.Delete(child, recursive: true);
            }
        }

        bool existsAfter = Directory.Exists(directory);
        long bytes = existsAfter ? DirectorySize(new DirectoryInfo(directory)) : 0;
        bool cleared = clear && existed && !Directory.EnumerateFileSystemEntries(directory).Any();
        return (bytes, existsAfter, cleared);
    }

    private FileStream AcquireLease()
    {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return OpenLease();
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < LeaseAcquisitionTimeout)
            {
                // FileShare.None maps to the OS file-sharing lock on macOS.
                // Retrying the open, rather than using FileStream.Lock, keeps
                // the lease portable and releases it automatically on exit.
                Thread.Sleep(LeaseRetryDelay);
            }
        }
    }

    private async Task<FileStream> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return OpenLease();
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < LeaseAcquisitionTimeout)
            {
                await Task.Delay(LeaseRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private FileStream OpenLease()
    {
        // The lease is a stable sibling of the cache directory. Never unlink
        // it during clear: processes holding an old inode could otherwise
        // continue concurrently with processes locking a newly created inode.
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1, FileOptions.None);
    }

    private long ReadGeneration()
    {
        FileStream stream;
        try
        {
            stream = new FileStream(epochPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        using (stream)
        {
            if (stream.Length != sizeof(long))
                throw new InvalidDataException("The background-map cache epoch file is malformed.");
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            stream.ReadExactly(bytes);
            long generation = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            if (generation < 0)
                throw new InvalidDataException("The background-map cache epoch is negative.");
            return generation;
        }
    }

    private void WriteGeneration(long generation)
    {
        string temporaryPath = epochPath + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: sizeof(long), FileOptions.WriteThrough))
            {
                Span<byte> bytes = stackalloc byte[sizeof(long)];
                BinaryPrimitives.WriteInt64LittleEndian(bytes, generation);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Both files are siblings, so the replacement is on one volume.
            // The lease prevents another process from observing an intermediate
            // epoch or replacing it at the same time.
            File.Move(temporaryPath, epochPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool IsGzip(byte[] bytes) => bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

    private static long DirectorySize(DirectoryInfo directory)
    {
        if (!directory.Exists) return 0;
        long total = 0;
        foreach (FileInfo file in directory.EnumerateFiles())
            total = checked(total + file.Length);
        foreach (DirectoryInfo child in directory.EnumerateDirectories())
            total = checked(total + DirectorySize(child));
        return total;
    }
}
