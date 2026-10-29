using System.Security.Cryptography;
using System.Text;

namespace Ras.Backend;

public static partial class FeatureQueryService
{
    private static bool IsStageLink(FileSystemInfo info) => info.LinkTarget is not null ||
        (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0);

    private static void RejectStageLink(string stageDirectory)
    {
        if (IsStageLink(new DirectoryInfo(stageDirectory)))
            throw new IOException("查询暂存目录不能是符号链接。");
    }

    private static void VerifyStageOwner(string stageDirectory, string resultId)
    {
        RejectStageLink(stageDirectory);
        if (!Directory.Exists(stageDirectory))
            throw new FileNotFoundException("查询暂存已不存在，请重新查询。");
        byte[] bytes = ReadStageBytes(OwnedStageFile(stageDirectory, ".owner"), 128);
        if (!string.Equals(Encoding.UTF8.GetString(WithoutUtf8Bom(bytes).Span), resultId,
                StringComparison.Ordinal))
            throw new InvalidDataException("无法确认查询暂存的归属，请重新查询。");
    }

    private sealed class StageLease(FileStream stream, string lockPath, string stageDirectory) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            // Result IDs are never reused. Once the stage is absent, a caller
            // with an earlier existence check will fail the post-lock owner
            // check; removing this zero-byte lock cannot authorize new work.
            try
            {
                if (!Directory.Exists(stageDirectory) && !IsStageLink(new FileInfo(lockPath)))
                    File.Delete(lockPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static StageLease AcquireStageLease(string stageDirectory, string resultId)
    {
        VerifyStageOwner(stageDirectory, resultId);
        string path = Path.Combine(GetStageRoot().FullName, ".lease-" + resultId);
        if (IsStageLink(new FileInfo(path)))
            throw new IOException("查询暂存锁不能是符号链接。");
        FileStream stream;
        try
        {
            // The worker is one process per RPC. A process-local lock cannot
            // protect a simultaneous export/discard; this lock releases on exit.
            // Use FileShare.None: FileShare.Delete is not an exclusive Unix lock.
            // The sibling location permits stage deletion on Windows as well.
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("该查询结果正在预览、导出或清理，请等待当前操作完成。", error);
        }
        try
        {
            if (IsStageLink(new FileInfo(path))) throw new SourceFileChangedException();
            VerifyStageOwner(stageDirectory, resultId);
            return new StageLease(stream, path, stageDirectory);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ValidateStageManifest(StageManifest manifest, bool allowExpired)
    {
        DateTime now = DateTime.UtcNow;
        if (manifest.CreatedUtc == default || manifest.CreatedUtc > now.AddMinutes(5))
            throw new InvalidDataException("查询暂存的创建时间无效，请重新查询。");
        if (!allowExpired && now - manifest.CreatedUtc > StageLifetime)
            throw new InvalidOperationException("查询暂存已超过 24 小时有效期，请重新查询。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StageLayer layer in manifest.Layers)
        {
            if (layer is null || !IsStageFeatureFileName(layer.FileName) || !names.Add(layer.FileName) ||
                !IsStageRevision(layer.Revision) || string.IsNullOrWhiteSpace(layer.Name) ||
                layer.Name.Length > 1024 || layer.FeatureCount < 0 ||
                layer.MapType is not ("PointsVector" or "PolylinesVector" or "PolygonsVector") ||
                !IsStageColor(layer.LineColor) || !IsStageColor(layer.FillColor) ||
                !double.IsFinite(layer.LineWidth) || layer.LineWidth < 0 || layer.LineWidth > 100)
                throw new InvalidDataException("查询暂存的图层清单无效，请重新查询。");
        }
        if ((manifest.NldAlignmentResponseFile is null) != (manifest.NldAlignmentResponseRevision is null) ||
            (manifest.NldAlignmentResponseFile is not null &&
                (manifest.NldAlignmentResponseFile != "nld-alignment-response.json" ||
                 !IsStageRevision(manifest.NldAlignmentResponseRevision))) ||
            (manifest.Kind == "NLD" && manifest.NldLayerId == "SystemRoutes" &&
                manifest.NldAlignmentResponseFile is null))
            throw new InvalidDataException("查询暂存缺少有效的 NLD 原始响应。");
    }

    private static bool IsStageRevision(string? revision) => revision is { Length: 64 } &&
        revision.All(Uri.IsHexDigit);

    private static bool IsStageColor(string? color) => color is { Length: 9 } && color[0] == '#' &&
        color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF".AsSpan()) == false;

    private static bool IsStageFeatureFileName(string? name) => name is { Length: >= 18 } &&
        name.StartsWith("feature-", StringComparison.Ordinal) && name.EndsWith(".geojson", StringComparison.Ordinal) &&
        name.AsSpan(8, name.Length - 16).ContainsAnyExcept("0123456789".AsSpan()) == false;

    private static void VerifyStageSnapshot(string stageDirectory, StageManifest manifest,
        string manifestRevision, bool verifySources = true)
    {
        VerifyStageOwner(stageDirectory, manifest.ResultId);
        if (!string.Equals(HashFile(OwnedStageFile(stageDirectory, "manifest.json")), manifestRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!verifySources) return;
        foreach (StageLayer layer in manifest.Layers)
            if (!string.Equals(HashFile(OwnedStageFile(stageDirectory, layer.FileName)), layer.Revision,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        if (manifest.NldAlignmentResponseFile is { } responseFile &&
            !string.Equals(HashFile(OwnedStageFile(stageDirectory, responseFile)),
                manifest.NldAlignmentResponseRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void DeleteStageDirectory(string stageDirectory, string resultId)
    {
        VerifyStageOwner(stageDirectory, resultId);
        foreach (FileSystemInfo entry in new DirectoryInfo(stageDirectory).EnumerateFileSystemInfos())
            if (IsStageLink(entry) || entry is DirectoryInfo ||
                (entry.Name is not (".owner" or ".lease" or "manifest.json" or "nld-alignment-response.json") &&
                 !IsStageFeatureFileName(entry.Name)))
                throw new IOException("查询暂存中出现未确认归属的文件，未执行清理。");
        Directory.Delete(stageDirectory, recursive: true);
    }

    private static byte[] ReadStageBytes(string path, long maximumBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes || stream.Length > int.MaxValue)
            throw new InvalidDataException("查询暂存文件超过读取预算，请缩小查询范围后重试。");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new SourceFileChangedException();
        return bytes;
    }

    private static ReadOnlyMemory<byte> WithoutUtf8Bom(byte[] bytes) =>
        bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsMemory(3) : bytes;

    private static string HashStageBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
