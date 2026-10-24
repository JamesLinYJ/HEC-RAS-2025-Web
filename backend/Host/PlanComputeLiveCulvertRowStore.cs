using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Ras.Backend;

internal readonly record struct PlanComputeLiveCulvertSnapshotRow(
    ReadOnlyMemory<float> Flow,
    ReadOnlyMemory<float> OutsideDepth,
    ReadOnlyMemory<float> OutletVelocity);

/// <summary>
/// Keeps produced culvert rows in a bounded set of memory chunks and spills
/// later rows to a private, temporary little-endian file. Profile indexes are
/// the storage offsets, so a failed profile leaves a hole instead of shifting
/// later rows. The compact validity bitmap distinguishes those holes from rows.
/// </summary>
internal sealed class PlanComputeLiveCulvertRowStore : IDisposable
{
    internal const long DefaultMemoryBudgetBytes = 32L * 1024 * 1024;
    private const int MemoryChunkBytes = 256 * 1024;
    private const int ValidityChunkBytes = 1024;

    private readonly object gate = new();
    private readonly long memoryBudgetBytes;
    private readonly List<byte[]> memoryChunks = [];
    private readonly List<byte[]> validityChunks = [];
    private long memoryBytes;
    private long bytesPerRow;
    private long memoryCapacityRows;
    private long nextProfileIndex;
    private int barrelCount;
    private string? spoolDirectoryPath;
    private string? spoolFilePath;
    private FileStream? spool;
    private bool disposed;

    internal PlanComputeLiveCulvertRowStore(long memoryBudgetBytes = DefaultMemoryBudgetBytes)
    {
        if (memoryBudgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
        this.memoryBudgetBytes = memoryBudgetBytes;
    }

    internal long MemoryBytes
    {
        get { lock (gate) return memoryBytes; }
    }

    internal string? SpoolDirectoryPath
    {
        get { lock (gate) return spoolDirectoryPath; }
    }

    internal string? SpoolFilePath
    {
        get { lock (gate) return spoolFilePath; }
    }

    /// <summary>
    /// Takes ownership of arrays returned by OutputBlock.TryReadCulvertData.
    /// That API allocates destination arrays and copies the writer row into them.
    /// </summary>
    internal void Append(int profileIndex, float[] flow, float[] outsideDepth, float[] outletVelocity)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(outsideDepth);
        ArgumentNullException.ThrowIfNull(outletVelocity);
        lock (gate)
        {
            ThrowIfDisposed();
            RequireNextProfile(profileIndex);
            if (flow.Length == 0 || outletVelocity.Length != flow.Length ||
                outsideDepth.Length != checked(flow.Length * 2))
                throw new InvalidDataException("The culvert snapshot arrays do not have aligned barrel dimensions.");

            if (barrelCount == 0)
            {
                barrelCount = flow.Length;
                bytesPerRow = checked(((long)flow.Length + outsideDepth.Length + outletVelocity.Length) * sizeof(float));
                memoryCapacityRows = bytesPerRow == 0 ? 0 : memoryBudgetBytes / bytesPerRow;
            }
            else if (flow.Length != barrelCount)
            {
                throw new InvalidDataException("The culvert snapshot barrel count changed during the writer run.");
            }

            long offset = checked((long)profileIndex * bytesPerRow);
            if (profileIndex < memoryCapacityRows)
            {
                long end = checked(offset + bytesPerRow);
                EnsureMemoryCapacity(end);
                CopyToMemory(offset, MemoryMarshal.AsBytes(flow.AsSpan()));
                CopyToMemory(checked(offset + (long)flow.Length * sizeof(float)),
                    MemoryMarshal.AsBytes(outsideDepth.AsSpan()));
                CopyToMemory(checked(offset + ((long)flow.Length + outsideDepth.Length) * sizeof(float)),
                    MemoryMarshal.AsBytes(outletVelocity.AsSpan()));
            }
            else
            {
                EnsureSpool();
                // Every append positions explicitly because a target read may
                // have moved the same serialized stream to an older profile.
                spool!.Position = offset;
                WriteFloats(spool, flow);
                WriteFloats(spool, outsideDepth);
                WriteFloats(spool, outletVelocity);
            }

            // Publish validity only after the complete payload is durable in
            // our private store. A failed spill leaves this index unavailable.
            SetAvailable(profileIndex);
            nextProfileIndex++;
        }
    }

    /// <summary>Reserves an unavailable index without fabricating a data row.</summary>
    internal void AppendUnavailable(int profileIndex)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            RequireNextProfile(profileIndex);
            nextProfileIndex++;
        }
    }

    internal bool TryRead(int profileIndex, out PlanComputeLiveCulvertSnapshotRow row)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (profileIndex < 0 || profileIndex >= nextProfileIndex || !IsAvailable(profileIndex) ||
                bytesPerRow == 0 || barrelCount <= 0)
            {
                row = default;
                return false;
            }

            float[] flow = new float[barrelCount];
            float[] outsideDepth = new float[checked(barrelCount * 2)];
            float[] outletVelocity = new float[barrelCount];
            long offset = checked((long)profileIndex * bytesPerRow);
            if (profileIndex < memoryCapacityRows)
            {
                CopyFromMemory(offset, MemoryMarshal.AsBytes(flow.AsSpan()));
                CopyFromMemory(checked(offset + (long)flow.Length * sizeof(float)),
                    MemoryMarshal.AsBytes(outsideDepth.AsSpan()));
                CopyFromMemory(checked(offset + ((long)flow.Length + outsideDepth.Length) * sizeof(float)),
                    MemoryMarshal.AsBytes(outletVelocity.AsSpan()));
            }
            else
            {
                if (spool is null)
                {
                    row = default;
                    return false;
                }
                spool.Flush();
                spool.Position = offset;
                ReadFloats(spool, flow);
                ReadFloats(spool, outsideDepth);
                ReadFloats(spool, outletVelocity);
            }

            row = new(flow, outsideDepth, outletVelocity);
            return true;
        }
    }

    private void RequireNextProfile(int profileIndex)
    {
        if (profileIndex < 0 || profileIndex != nextProfileIndex)
            throw new InvalidOperationException("Culvert snapshot rows must be appended in profile order.");
    }

    private void EnsureMemoryCapacity(long requiredBytes)
    {
        if (requiredBytes > memoryBudgetBytes)
            throw new InvalidOperationException("A memory-backed culvert row exceeded the configured budget.");
        while (memoryBytes < requiredBytes)
        {
            long remainingBudget = memoryBudgetBytes - memoryBytes;
            int chunkLength = (int)Math.Min(MemoryChunkBytes, remainingBudget);
            if (chunkLength <= 0) throw new InvalidOperationException("The culvert row memory budget is exhausted.");
            memoryChunks.Add(new byte[chunkLength]);
            memoryBytes = checked(memoryBytes + chunkLength);
        }
    }

    private void CopyToMemory(long offset, ReadOnlySpan<byte> source)
    {
        int copied = 0;
        while (copied < source.Length)
        {
            int chunkIndex = checked((int)(offset / MemoryChunkBytes));
            int chunkOffset = (int)(offset % MemoryChunkBytes);
            byte[] chunk = memoryChunks[chunkIndex];
            int count = Math.Min(source.Length - copied, chunk.Length - chunkOffset);
            source.Slice(copied, count).CopyTo(chunk.AsSpan(chunkOffset, count));
            copied += count;
            offset += count;
        }
    }

    private void CopyFromMemory(long offset, Span<byte> destination)
    {
        int copied = 0;
        while (copied < destination.Length)
        {
            int chunkIndex = checked((int)(offset / MemoryChunkBytes));
            int chunkOffset = (int)(offset % MemoryChunkBytes);
            byte[] chunk = memoryChunks[chunkIndex];
            int count = Math.Min(destination.Length - copied, chunk.Length - chunkOffset);
            chunk.AsSpan(chunkOffset, count).CopyTo(destination.Slice(copied, count));
            copied += count;
            offset += count;
        }
    }

    private void SetAvailable(int profileIndex)
    {
        int byteIndex = profileIndex >> 3;
        int chunkIndex = byteIndex / ValidityChunkBytes;
        while (validityChunks.Count <= chunkIndex) validityChunks.Add(new byte[ValidityChunkBytes]);
        int withinChunk = byteIndex % ValidityChunkBytes;
        validityChunks[chunkIndex][withinChunk] |= (byte)(1 << (profileIndex & 7));
    }

    private bool IsAvailable(int profileIndex)
    {
        int byteIndex = profileIndex >> 3;
        int chunkIndex = byteIndex / ValidityChunkBytes;
        return chunkIndex < validityChunks.Count &&
            (validityChunks[chunkIndex][byteIndex % ValidityChunkBytes] & (1 << (profileIndex & 7))) != 0;
    }

    private void EnsureSpool()
    {
        if (spool is not null) return;
        DirectoryInfo? directory = null;
        FileStream? file = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("hecras-culvert-snapshot-");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory.FullName,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            string path = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + ".bin");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.DeleteOnClose | FileOptions.RandomAccess
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            file = new FileStream(path, options);

            spoolDirectoryPath = directory.FullName;
            spoolFilePath = path;
            spool = file;
        }
        catch
        {
            file?.Dispose();
            if (directory is not null && directory.Exists) Directory.Delete(directory.FullName, recursive: true);
            throw;
        }
    }

    private static void WriteFloats(FileStream stream, float[] values)
    {
        if (BitConverter.IsLittleEndian)
        {
            stream.Write(MemoryMarshal.AsBytes(values.AsSpan()));
            return;
        }

        Span<byte> bytes = stackalloc byte[sizeof(float)];
        foreach (float value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
            stream.Write(bytes);
        }
    }

    private static void ReadFloats(FileStream stream, float[] values)
    {
        if (BitConverter.IsLittleEndian)
        {
            stream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
            return;
        }

        Span<byte> bytes = stackalloc byte[sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            stream.ReadExactly(bytes);
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(PlanComputeLiveCulvertRowStore));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            spool?.Dispose();
            spool = null;
            memoryChunks.Clear();
            validityChunks.Clear();
            memoryBytes = 0;
            if (spoolDirectoryPath is { } directory && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            spoolDirectoryPath = null;
            spoolFilePath = null;
        }
    }
}
