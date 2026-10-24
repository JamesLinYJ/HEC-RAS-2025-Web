using System.Globalization;
using System.Security.Cryptography;
using Geospatial.Vectors;
using Utility.Systems;

namespace Ras.Backend;

/// <summary>
/// Replays exact source .stream records across short-lived backend processes.
/// The externally visible reference contains only GUIDs, sequence, and digest;
/// it never grants a caller control over a filesystem path.
/// </summary>
internal static class StreamModeTinAssetStore
{
    private const int MaximumRecordBytes = StreamableObjectHelper.MaximumObjectPacketLength +
        StreamableObjectHelper.MaximumNamePacketLength + sizeof(int) * 2;

    internal static StreamModeTinAssetReference Store(Guid sessionID, long sequence, ReadOnlySpan<byte> record)
    {
        ValidateIdentity(sessionID, sequence);
        if (record.IsEmpty || record.Length > MaximumRecordBytes)
            throw new InvalidDataException("The streamed TIN record has an invalid size.");

        Guid assetID = Guid.NewGuid();
        string sha256 = Convert.ToHexString(SHA256.HashData(record)).ToLowerInvariant();
        var reference = new StreamModeTinAssetReference(sessionID, sequence, assetID, sha256);
        string path = ResolvePath(reference);
        string root = ResolveRoot();
        string sessionDirectory = Path.Combine(root, sessionID.ToString("N"));
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(root);
        EnsureNotReparsePoint(root);
        RestrictUnixPermissions(root, directory: true);

        Directory.CreateDirectory(sessionDirectory);
        EnsureNotReparsePoint(sessionDirectory);
        RestrictUnixPermissions(sessionDirectory, directory: true);

        Directory.CreateDirectory(directory);
        EnsureNotReparsePoint(directory);
        RestrictUnixPermissions(directory, directory: true);

        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.WriteThrough))
        {
            output.Write(record);
            output.Flush(flushToDisk: true);
        }
        RestrictUnixPermissions(path, directory: false);
        return reference;
    }

    internal static async Task<byte[]> ReadAsync(StreamModeTinAssetReference reference,
        CancellationToken cancellationToken)
    {
        ValidateReference(reference);
        string path = ResolvePath(reference);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0 || bytes.Length > MaximumRecordBytes)
            throw new InvalidDataException("The cached streamed TIN record has an invalid size.");

        string actualDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualDigest), Convert.FromHexString(reference.Sha256)))
            throw new InvalidDataException("The cached streamed TIN record failed its SHA-256 check.");
        return bytes;
    }

    internal static bool Release(StreamModeTinAssetReference reference)
    {
        ValidateReference(reference);
        string path = ResolvePath(reference);
        if (!File.Exists(path)) return false;

        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string actualDigest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualDigest), Convert.FromHexString(reference.Sha256)))
                throw new InvalidDataException("The cached streamed TIN record failed its SHA-256 check.");
        }

        File.Delete(path);
        PruneEmptyDirectories(Path.GetDirectoryName(path));
        return true;
    }

    /// <summary>
    /// Removes only canonical record files below the server-derived directory
    /// for one session. Unexpected entries and reparse points are preserved and
    /// reported so callers can retry with the same session identity.
    /// </summary>
    internal static void ReleaseSession(Guid sessionID)
    {
        if (sessionID == Guid.Empty)
            throw new ArgumentException("A streamed TIN session identity is required.", nameof(sessionID));

        string root = ResolveRoot();
        if (!Directory.Exists(root)) return;
        EnsureNotReparsePoint(root);

        string sessionDirectory = Path.Combine(root, sessionID.ToString("N"));
        if (!Directory.Exists(sessionDirectory)) return;
        EnsureNotReparsePoint(sessionDirectory);

        foreach (string entry in Directory.EnumerateFileSystemEntries(sessionDirectory))
        {
            if (!Directory.Exists(entry))
                continue;

            EnsureNotReparsePoint(entry);
            string sequenceName = Path.GetFileName(entry);
            if (!long.TryParse(sequenceName, NumberStyles.None, CultureInfo.InvariantCulture,
                    out long sequence) || sequence < 0 ||
                !string.Equals(sequence.ToString(CultureInfo.InvariantCulture), sequenceName,
                    StringComparison.Ordinal))
                continue;

            foreach (string file in Directory.EnumerateFileSystemEntries(entry))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) != 0)
                    continue;

                string fileName = Path.GetFileName(file);
                if (!fileName.EndsWith(".stream", StringComparison.Ordinal)) continue;
                string assetName = fileName[..^".stream".Length];
                if (!Guid.TryParseExact(assetName, "N", out Guid assetID) || assetID == Guid.Empty ||
                    !string.Equals(assetID.ToString("N") + ".stream", fileName, StringComparison.Ordinal))
                    continue;

                File.Delete(file);
            }

            if (!Directory.EnumerateFileSystemEntries(entry).Any())
                Directory.Delete(entry);
        }

        if (Directory.EnumerateFileSystemEntries(sessionDirectory).Any())
            throw new InvalidDataException("The streamed TIN session directory contains unexpected entries.");

        Directory.Delete(sessionDirectory);
    }

    internal static void ValidateReference(StreamModeTinAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ValidateIdentity(reference.SessionID, reference.Sequence);
        if (reference.AssetID == Guid.Empty || reference.Sha256 is null || reference.Sha256.Length != 64 ||
            reference.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The streamed TIN asset reference is invalid.", nameof(reference));
    }

    private static void ValidateIdentity(Guid sessionID, long sequence)
    {
        if (sessionID == Guid.Empty || sequence < 0)
            throw new ArgumentException("The streamed TIN source identity is invalid.");
    }

    private static string ResolvePath(StreamModeTinAssetReference reference)
    {
        string root = ResolveRoot();
        string session = reference.SessionID.ToString("N");
        string sequence = reference.Sequence.ToString(CultureInfo.InvariantCulture);
        string asset = reference.AssetID.ToString("N") + ".stream";
        return Path.Combine(root, session, sequence, asset);
    }

    private static string ResolveRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "Library", "Application Support", "HEC", "RAS", "StreamMode", "TIN");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HEC", "RAS", "StreamMode", "TIN");
    }

    private static void PruneEmptyDirectories(string? directory)
    {
        string root = ResolveRoot();
        while (directory is not null && !string.Equals(directory, root, StringComparison.Ordinal) &&
            directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
                return;
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void RestrictUnixPermissions(string path, bool directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        File.SetUnixFileMode(path, directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A streamed TIN owner directory is a reparse point.");
    }
}

/// <summary>Captures exactly the bytes consumed by one StreamableObjectHelper read.</summary>
internal sealed class StreamModeTinRecordCaptureStream(Stream inner) : Stream
{
    private readonly MemoryStream _capture = new();

    public byte[] CapturedBytes => _capture.ToArray();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        Capture(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        Capture(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Capture(buffer.Span[..read]);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        int read = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        Capture(buffer.AsSpan(offset, read));
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _capture.Dispose();
        base.Dispose(disposing);
    }

    private void Capture(ReadOnlySpan<byte> bytes)
    {
        if (_capture.Length + bytes.Length > StreamableObjectHelper.MaximumObjectPacketLength +
            StreamableObjectHelper.MaximumNamePacketLength + sizeof(int) * 2)
            throw new InvalidDataException("The streamed object record exceeds its capture limit.");
        _capture.Write(bytes);
    }
}
