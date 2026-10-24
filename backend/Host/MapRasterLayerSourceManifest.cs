using System.Security.Cryptography;
using System.Text;
using Geospatial.GDALAssist;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Stable identity for every local file GDAL reports as part of a dataset.
/// This includes a VRT's referenced rasters and companion files, so importing
/// a VRT never treats its XML file alone as the rendered source.
/// </summary>
internal static class MapRasterLayerSourceManifest
{
    internal static string CanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("The raster source path must be absolute.");
        return SourceFilePaths.Canonical(path);
    }

    internal static string CaptureRevision(string path)
    {
        string canonicalRoot = CanonicalPath(path);
        if (!File.Exists(canonicalRoot)) throw new FileNotFoundException("The raster source was not found.", canonicalRoot);

        // GetVRTSources opens the dataset and returns GDAL's complete file
        // list (including non-VRT datasets), so avoid opening the same source
        // once here and again inside that helper.
        string[] datasetFiles = GDALRaster.GetVRTSources(canonicalRoot)
            ?? throw new InvalidDataException("GDAL did not return a source file list for the raster.");

        var paths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            canonicalRoot
        };
        string rootDirectory = Path.GetDirectoryName(canonicalRoot)!;
        foreach (string value in datasetFiles)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value.StartsWith("/vsi", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("This raster references a virtual GDAL source that cannot be fingerprinted as a local project file.");
            string normalized = value.Replace('\\', Path.DirectorySeparatorChar);
            string resolved = SourceFilePaths.Canonical(Path.IsPathFullyQualified(normalized)
                ? normalized : Path.Combine(rootDirectory, normalized));
            if (!File.Exists(resolved))
                throw new FileNotFoundException("A file used by the GDAL raster is missing.", resolved);
            paths.Add(resolved);
        }

        // Preserve the established raw-SHA revision for a standalone dataset
        // with no GDAL-reported dependencies. Its canonical path is validated
        // separately by every map operation. As soon as GDAL reports a sidecar
        // or VRT dependency, use the complete path/length/content manifest.
        if (paths.Count == 1)
        {
            using var singleFile = new FileStream(canonicalRoot, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(singleFile));
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string source in paths.OrderBy(value => value, OperatingSystem.IsWindows()
                     ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(source);
            hash.AppendData(BitConverter.GetBytes(pathBytes.Length));
            hash.AppendData(pathBytes);
            var info = new FileInfo(source);
            hash.AppendData(BitConverter.GetBytes(info.Length));
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            byte[] buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static bool Matches(string path, string expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision)) return false;
        string current = CaptureRevision(path);
        return current.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase);
    }

    internal static void RequireCurrent(string path, string expectedRevision)
    {
        if (!Matches(path, expectedRevision)) throw new SourceFileChangedException();
    }
}
