using System.Globalization;
using System.Security.Cryptography;
using Hec.Dss;
using Ras.Runtime;

namespace Ras.Backend;

public sealed record DssCatalogRequest(string Path);
public sealed record DssReadRequest(string Path, string Revision, string[] RecordPaths);
public sealed record DssRecordDescriptor(string Path, string RecordType, string[] Parts, string[] ComprisingPaths);
public sealed record DssCatalogResponse(string Path, string Revision, DssRecordDescriptor[] Records);
public sealed record DssReadResponse(string Path, string Revision, DssRecordResult[] Records);
public sealed record DssTimeSeriesData(string[] Times, double?[] Values, int[]? Qualities, string Units, string DataType);
public sealed record DssCurveData(string Label, double?[] Values);
public sealed record DssPairedData(double?[] Ordinates, DssCurveData[] Curves, string IndependentUnits,
    string DependentUnits, string IndependentType, string DependentType);
public sealed record DssLocationData(double? X, double? Y, double? Z, string CoordinateSystem,
    int CoordinateID, int HorizontalUnits, int HorizontalDatum, int VerticalUnits, int VerticalDatum,
    string TimeZoneName, string Supplemental);
public sealed record DssRecordResult(string Path, string RecordType, string Kind, string? Error = null,
    DssTimeSeriesData? TimeSeries = null, DssPairedData? PairedData = null, DssLocationData? Location = null);

/// <summary>
/// Keeps the source's condensed catalog and managed reader. The source native
/// open can create/update a file, so inspection opens an isolated byte-identical
/// copy. Neither catalog nor graph/table reads open the user's DSS for writing.
/// </summary>
public static class DssRecordService
{
    public static DssCatalogResponse Catalog(DssCatalogRequest request)
    {
        using var source = DssReadCopy.Capture(request.Path);
        DssRuntimeService.RequireAvailable();
        using var reader = new DssReader(source.CopyPath);
        DssRecordDescriptor[] records = reader.GetCatalog().Select(Describe).ToArray();
        RequireUniquePaths(records.Select(record => record.Path));
        source.VerifyUnchanged();
        return new(source.SourcePath, source.Revision, records);
    }

    public static DssReadResponse Read(DssReadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("A DSS catalog source revision is required.");
        if (request.RecordPaths is null || request.RecordPaths.Length == 0)
            throw new ArgumentException("Select at least one DSS catalog record.");
        RequireUniquePaths(request.RecordPaths);
        using var source = DssReadCopy.Capture(request.Path, request.Revision);
        DssRuntimeService.RequireAvailable();
        using var reader = new DssReader(source.CopyPath);
        DssPath[] catalog = reader.GetCatalog().ToArray();
        RequireUniquePaths(catalog.Select(path => path.FullPath));
        var byPath = catalog.ToDictionary(path => path.FullPath, StringComparer.Ordinal);
        // Validate the complete selection before performing any record read.
        foreach (string path in request.RecordPaths)
            if (!byPath.ContainsKey(path))
                throw new InvalidOperationException("The selected DSS pathname is not in this source catalog: " + path);
        DssRecordResult[] records = request.RecordPaths.Select(path => ReadRecord(reader, byPath[path])).ToArray();
        source.VerifyUnchanged();
        return new(source.SourcePath, source.Revision, records);
    }

    private static DssRecordDescriptor Describe(DssPath path) => new(path.FullPath, path.RecordType.ToString(),
        [path.Apart, path.Bpart, path.Cpart, path.Dpart, path.Epart, path.Fpart],
        path is DssPathCondensed condensed
            ? condensed.GetComprisingDSSPaths().Select(part => part.FullPath).ToArray() : [path.FullPath]);

    private static DssRecordResult ReadRecord(DssReader reader, DssPath path)
    {
        string recordType = path.RecordType.ToString();
        try
        {
            switch (path.RecordType)
            {
                case RecordType.RegularTimeSeries:
                case RecordType.IrregularTimeSeries:
                {
                    TimeSeries series = reader.GetTimeSeries(path);
                    if (reader.LastTimeSeriesReadStatus != 0)
                        throw new InvalidDataException("The DSS time-series retrieval failed with status " + reader.LastTimeSeriesReadStatus);
                    // The retained reader returns an uninitialized object after
                    // native retrieval errors. Never convert that into success.
                    if (series.Times is null || series.Values is null || series.Path is null
                        || series.Times.Length != series.Values.Length)
                        throw new InvalidDataException("The DSS time-series read did not return complete source arrays.");
                    if (series.Qualities is { Length: > 0 } quality &&
                        (series.Values.Length == 0 || quality.Length % series.Values.Length != 0))
                        throw new InvalidDataException("The DSS quality array does not match the source value count.");
                    return new(path.FullPath, recordType, "timeSeries", TimeSeries: new(
                        series.Times.Select(time => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture)).ToArray(),
                        series.Values.Select(ValueOrMissing).ToArray(), series.Qualities,
                        series.Units ?? "", series.DataType ?? ""), Location: ReadLocation(reader, path));
                }
                case RecordType.PairedData:
                {
                    PairedData paired = reader.GetPairedData(path.FullPath);
                    if (paired.Path is null || paired.Ordinates is null || paired.Values is null
                        || paired.Values.Any(curve => curve.Length != paired.Ordinates.Length))
                        throw new InvalidDataException("The DSS paired-data read did not return complete source arrays.");
                    DssCurveData[] curves = paired.Values.Select((curve, index) => new DssCurveData(
                        paired.Labels is not null && index < paired.Labels.Count ? paired.Labels[index] : "",
                        curve.Select(ValueOrMissing).ToArray())).ToArray();
                    return new(path.FullPath, recordType, "pairedData", PairedData: new(
                        paired.Ordinates.Select(ValueOrMissing).ToArray(), curves, paired.UnitsIndependent ?? "",
                        paired.UnitsDependent ?? "", paired.TypeIndependent ?? "", paired.TypeDependent ?? ""),
                        Location: ReadLocation(reader, path));
                }
                case RecordType.LocationInfo:
                    return new(path.FullPath, recordType, "location", Location: ReadLocation(reader, path)
                        ?? throw new InvalidDataException("The DSS location record could not be retrieved."));
                default:
                    return new(path.FullPath, recordType, "unsupported",
                        "The source DSS graph/table viewer does not support this record type: " + recordType);
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException
            or AccessViolationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException))
        {
            return new(path.FullPath, recordType, "error", error.Message);
        }
    }

    private static double? ValueOrMissing(double value) => double.IsFinite(value) && DssReader.IsValid(value) ? value : null;

    private static DssLocationData? ReadLocation(DssReader reader, DssPath path)
    {
        string pathname = path is DssPathCondensed condensed
            ? condensed.GetComprisingDSSPaths()[0].FullPath : path.FullPath;
        LocationInformation value = reader.GetLocationInfo(pathname, out int status);
        return status == 0 ? Location(value) : null;
    }

    private static DssLocationData? Location(LocationInformation? value) => value is null ? null : new(
        ValueOrMissing(value.XOrdinate), ValueOrMissing(value.YOrdinate), ValueOrMissing(value.ZOrdiante),
        value.CoordinateSystem.ToString(), value.CoordinateID, value.HorizontalUnits, value.HorizontalDatum,
        value.VerticalUnits, value.VerticalDatum, value.TimeZoneName ?? "", value.Supplemental ?? "");

    private static void RequireUniquePaths(IEnumerable<string> paths)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
            if (string.IsNullOrWhiteSpace(path) || !found.Add(path))
                throw new InvalidDataException("DSS catalog pathnames must be nonempty and unique.");
    }

    private sealed class DssReadCopy : IDisposable
    {
        private readonly string directory;
        public string SourcePath { get; }
        public string CopyPath { get; }
        public string Revision { get; }

        private DssReadCopy(string path, string directory, string copyPath, string revision)
            => (SourcePath, this.directory, CopyPath, Revision) = (path, directory, copyPath, revision);

        public static DssReadCopy Capture(string path, string? expectedRevision = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("An absolute DSS source file path is required.");
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path), ".dss", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select a DSS source file.");
            if (!File.Exists(path)) throw new FileNotFoundException("The DSS source file does not exist.", path);
            string revision = Hash(path);
            if (expectedRevision is not null && revision != expectedRevision)
                throw new InvalidOperationException("The DSS source changed after its catalog was loaded. Refresh the catalog.");
            string directory = Path.Combine(Path.GetTempPath(), "HECRAS2025-DSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var copy = new DssReadCopy(path, directory, Path.Combine(directory, "source.dss"), revision);
            try
            {
                File.Copy(path, copy.CopyPath);
                if (Hash(copy.CopyPath) != revision)
                    throw new InvalidOperationException("The DSS source changed while it was being captured. Refresh the catalog.");
                copy.VerifyUnchanged();
                return copy;
            }
            catch { copy.Dispose(); throw; }
        }

        public void VerifyUnchanged()
        {
            if (!File.Exists(SourcePath) || Hash(SourcePath) != Revision)
                throw new InvalidOperationException("The DSS source changed during the read. Refresh the catalog.");
        }

        private static string Hash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
