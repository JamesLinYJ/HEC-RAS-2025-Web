using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.MetImport;
using Geospatial.Rasters;
using Hec.Dss;
using Ras.Runtime;

namespace Ras.Backend;

/// <summary>
/// Reads actual DSS GRID records through the retained Hec.Dss/MetImport stack.
/// Native DSS opens can update bookkeeping in their input, so every operation
/// uses an isolated copy and verifies the original SHA-256 before and after.
/// </summary>
public static class DssGridImportService
{
    private const string TemporaryPrefix = "HECRAS2025-DSS-Grid-";
    internal const string RefreshWarning = "The grid data and boundary model were saved, but the editor could not refresh its snapshot. Reload the boundary model before continuing.";

    public static DssGridInspection Inspect(string sourcePath, string draftIdentity, string? previewId = null)
    {
        using var snapshot = SourceSnapshot.Capture(sourcePath);
        DssRuntimeService.RequireAvailable();
        using var reader = new DssReader(snapshot.CopyPath);
        DssGridSeriesInfo[] sources = DescribeSources(snapshot, reader);
        SpatialGridPreview? preview = null;
        DssGridSeriesInfo? previewSource = sources.FirstOrDefault(source => source.Id == previewId)
            ?? sources.FirstOrDefault();
        if (previewSource is not null)
        {
            using var dataSource = OpenSeries(snapshot.CopyPath, previewSource.GroupPath);
            using var selection = CreateSelection(dataSource);
            var raster = (ITimeSeriesRaster<float>)selection.Surface;
            preview = SpatialGridService.Preview(raster, 0);
        }
        snapshot.VerifyUnchanged();
        return new(snapshot.SourcePath, snapshot.Revision, draftIdentity, sources, preview);
    }

    internal static DssGridImportOperation Open(DssGridImportSelection selection, DssGridImportCancellation cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        if (!string.Equals(selection.OperationId, cancellation.OperationId, StringComparison.Ordinal))
            throw new ArgumentException("The DSS import selection does not belong to its active operation.");
        if (string.IsNullOrWhiteSpace(selection.DraftIdentity))
            throw new ArgumentException("The captured boundary draft identity is required for DSS grid import.");
        if (selection.Components is null || selection.Components.Length == 0)
            throw new ArgumentException("Select one complete DSS grid series before importing.");
        if (!Guid.TryParseExact(selection.OperationId, "D", out _))
            throw new ArgumentException("A DSS grid import operation ID is required.");

        var snapshot = SourceSnapshot.Capture(selection.SourcePath, selection.Revision);
        var dataSources = new List<DssTimeSeriesRasterDataSource>();
        var selections = new List<IGriddedDataSelection>();
        var selectedSources = new List<DssGridSeriesInfo>();
        try
        {
            DssRuntimeService.RequireAvailable();
            using var reader = new DssReader(snapshot.CopyPath);
            DssGridSeriesInfo[] currentSources = DescribeSources(snapshot, reader);
            if (selection.Components.Select(component => component.Component).Distinct(StringComparer.Ordinal).Count()
                != selection.Components.Length)
                throw new ArgumentException("DSS grid components must be unique.");
            cancellation.ConfigureFrames(selection.Components);
            foreach (DssGridImportComponent component in selection.Components)
            {
                DssGridSeriesInfo current = currentSources.SingleOrDefault(source => source.Id == component.SeriesId)
                    ?? throw new InvalidOperationException("The selected DSS grid series no longer exists in this source.");
                if (!component.RecordPaths.SequenceEqual(current.RecordPaths, StringComparer.Ordinal)
                    || !component.Times.SequenceEqual(current.Times, StringComparer.Ordinal)
                    || component.GridIdentity != current.GridIdentity)
                    throw new InvalidOperationException("The selected DSS grid series changed after inspection. Refresh the source.");

                DssTimeSeriesRasterDataSource dataSource = OpenSeries(snapshot.CopyPath, current.GroupPath,
                    cancellation.Token,
                    frameIndex => cancellation.ReportFrame(component.Component, frameIndex));
                dataSources.Add(dataSource);
                selections.Add(CreateSelection(dataSource));
                selectedSources.Add(current);
            }
            ValidateSelectionComponents(selection.Components, currentSources);
            snapshot.VerifyUnchanged();
            return new(snapshot, selection.OperationId, cancellation, dataSources, selections, selectedSources, selection.Components);
        }
        catch
        {
            foreach (IGriddedDataSelection item in selections) (item as IDisposable)?.Dispose();
            foreach (DssTimeSeriesRasterDataSource item in dataSources) item.Dispose();
            snapshot.Dispose();
            throw;
        }
    }

    internal static string DraftIdentity(ModelSaveRequest model)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(model, BackendJson.Options));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    internal static DssGridImportCancellation BeginImport(string operationId,
        Action<BackendOperationProgress>? report = null) => new(operationId, report);

    internal static void VerifySourceRevision(string sourcePath, string revision)
    {
        if (!Path.IsPathFullyQualified(sourcePath) || !File.Exists(sourcePath)
            || !string.Equals(Hash(SourceFilePaths.Canonical(sourcePath)), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static DssGridSeriesInfo[] DescribeSources(SourceSnapshot snapshot, DssReader reader)
    {
        DssPath[] gridRecords = reader.GetCatalog().UnCondensedPaths.Where(path => path.RecordType == RecordType.Grid).ToArray();
        var groups = gridRecords.GroupBy(GroupKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var results = new List<DssGridSeriesInfo>();
        foreach (var group in groups)
        {
            DssPath first = group.First();
            string groupPath = Geospatial.IO.DssTimeSeriesRasterDataSource.CleanOutTimeParts(first.FullPath);
            // The import controller's public subset keys are lower-cased A/B/C/F
            // groups. The retained source class requires a valid DSS pathname,
            // so use the original-case dateless group path for opening the series.
            DssPath[] orderedRecords = group.Select(path => (Path: path, Time: ParseGridTime(path)))
                .OrderBy(item => item.Time).Select(item => item.Path).ToArray();
            if (orderedRecords.Select(ParseGridTime).Distinct().Count() != orderedRecords.Length)
                throw new InvalidDataException("A DSS grid series contains duplicate valid times: " + group.Key);

            using var series = OpenSeries(snapshot.CopyPath, groupPath);
            string[] sourcePaths = series.Pathnames.ToArray();
            string[] expectedPaths = orderedRecords.Select(path => path.FullPath).ToArray();
            if (!sourcePaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
                throw new InvalidDataException("The DSS grid series path selection does not match the native raster source grouping: " + group.Key);
            string[] times = series.Times.Select(FormatTime).ToArray();
            string[] expectedTimes = orderedRecords.Select(path => FormatTime(ParseGridTime(path))).ToArray();
            if (!times.SequenceEqual(expectedTimes, StringComparer.Ordinal))
                throw new InvalidDataException("The DSS grid series has invalid or ambiguous valid times: " + group.Key);

            Grid firstGrid = reader.GetGrid(sourcePaths[0], retrieveData: false);
            string gridIdentity = GridIdentity(firstGrid);
            foreach (string recordPath in sourcePaths.Skip(1))
                if (GridIdentity(reader.GetGrid(recordPath, retrieveData: false)) != gridIdentity)
                    throw new InvalidDataException("Every frame in a DSS grid series must retain the same grid definition and projection: " + group.Key);

            RasterDefinition definition = series.RasterDefinition;
            string id = StableSeriesId(snapshot.SourcePath, snapshot.Revision, group.Key, expectedPaths);
            string title = $"/{first.Apart}/{first.Bpart}/{first.Cpart}/…/{first.Fpart}/";
            results.Add(new(id, groupPath, title, times, expectedPaths, firstGrid.Units ?? "",
                firstGrid.DataType.ToString(), series.Projection?.GetWKT() ?? firstGrid.SRSDefinition ?? "",
                definition.Rows, definition.Cols, definition.CellSize,
                definition.Left, definition.Bottom, definition.Right, definition.Top,
                series.NoDataValue ?? -9999f, firstGrid.NullValue, firstGrid.UndefinedValue, gridIdentity));
        }
        snapshot.VerifyUnchanged();
        return results.ToArray();
    }

    private static DssTimeSeriesRasterDataSource OpenSeries(string filename, string groupPath,
        CancellationToken cancellationToken = default, Action<int>? frameRead = null)
    {
        var source = new DssTimeSeriesRasterDataSource(filename, groupPath, keepOpen: false,
            cancellationToken: cancellationToken, frameRead: frameRead);
        if (source.ProfileCount == 0 || source.RasterDefinition.IsNull())
        {
            source.Dispose();
            throw new InvalidDataException("The selected DSS grid series has no readable raster frames.");
        }
        return source;
    }

    private static IGriddedDataSelection CreateSelection(DssTimeSeriesRasterDataSource source)
    {
        var controller = new GriddedDssRasterImportController();
        if (controller.CanCombineSources)
            throw new InvalidOperationException("The retained DSS grid importer unexpectedly allows source combination.");
        IReadOnlyList<(IGriddedDataSource, int)> selected = [(new GriddedDssRaster(source), 0)];
        var result = controller.CreateSelection(selected, out IGriddedDataSelection? selection);
        if (!result.Result || selection is null)
        {
            (selection as IDisposable)?.Dispose();
            throw new InvalidDataException(string.Join("; ", result.Messages.Select(message => message.EvaluatedMessage)));
        }
        return selection;
    }

    private static void ValidateSelectionComponents(DssGridImportComponent[] components, DssGridSeriesInfo[] sources)
    {
        var byID = sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        if (components.Length == 2)
        {
            if (!components.Select(component => component.Component).ToHashSet(StringComparer.Ordinal).SetEquals(["X", "Y"]))
                throw new ArgumentException("Wind DSS imports require one complete X series and one complete Y series.");
            var x = byID[components.Single(component => component.Component == "X").SeriesId];
            var y = byID[components.Single(component => component.Component == "Y").SeriesId];
            if (x.Id == y.Id)
                throw new ArgumentException("Wind X and Y must select two distinct complete DSS grid series.");
            if (!x.Times.SequenceEqual(y.Times, StringComparer.Ordinal))
                throw new ArgumentException("Wind X and Y DSS series must have identical valid timesteps.");
            if (x.GridIdentity != y.GridIdentity || x.Rows != y.Rows || x.Columns != y.Columns
                || x.CellSize != y.CellSize || x.Projection != y.Projection)
                throw new ArgumentException("Wind X and Y DSS series must use the same grid spacing, dimensions, NoData and projection.");
        }
        else if (components.Length != 1 || components[0].Component != "Value")
            throw new ArgumentException("Scalar DSS imports require exactly one Value series.");
    }

    private static string GroupKey(DssPath path)
        => $"/{path.Apart}/{path.Bpart}/{path.Cpart}///{path.Fpart}/".ToLowerInvariant();

    private static DateTime ParseGridTime(DssPath path)
    {
        if (path.TryParseEPartAsDateTime(out DateTime time) || path.TryParseDPartAsDateTime(out time)) return time;
        throw new InvalidDataException("A DSS grid record does not contain a valid D/E timestamp: " + path.FullPath);
    }

    private static string FormatTime(DateTime time)
        => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string StableSeriesId(string sourcePath, string revision, string groupKey, string[] recordPaths)
    {
        string identity = string.Join("\n", [SourceFilePaths.Canonical(sourcePath), revision, groupKey, .. recordPaths]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string GridIdentity(Grid grid)
    {
        static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        string identity = string.Join("\n", grid.GridType, grid.DataType, grid.LowerLeftCellX,
            grid.LowerLeftCellY, grid.NumberOfCellsX, grid.NumberOfCellsY, Number(grid.CellSize),
            Number(grid.XCoordOfGridCellZero), Number(grid.YCoordOfGridCellZero),
            grid.SRSDefinitionType, grid.SRSName ?? "", grid.SRSDefinition ?? "",
            Number(grid.NullValue), Number(grid.UndefinedValue), grid.Units ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    internal sealed class DssGridImportOperation : IDisposable
    {
        private readonly SourceSnapshot snapshot;
        private readonly string operationId;
        private readonly DssGridImportCancellation cancellation;
        private readonly List<DssTimeSeriesRasterDataSource> dataSources;
        private readonly List<IGriddedDataSelection> selections;
        private readonly List<DssGridSeriesInfo> sources;
        private readonly DssGridImportComponent[] components;
        private bool disposed;

        internal DssGridImportOperation(SourceSnapshot snapshot, string operationId, DssGridImportCancellation cancellation,
            List<DssTimeSeriesRasterDataSource> dataSources, List<IGriddedDataSelection> selections,
            List<DssGridSeriesInfo> sources, DssGridImportComponent[] components)
            => (this.snapshot, this.operationId, this.cancellation, this.dataSources, this.selections, this.sources, this.components)
                = (snapshot, operationId, cancellation, dataSources, selections, sources, components);

        internal ITimeSeriesRaster<float> Raster(string component)
        {
            ThrowIfDisposed();
            int index = Array.FindIndex(components, item => item.Component == component);
            if (index < 0) throw new ArgumentException("A selected DSS grid component is missing: " + component);
            return selections[index].Surface as ITimeSeriesRaster<float>
                ?? throw new InvalidDataException("The selected DSS record did not produce a time-series raster.");
        }

        internal Geospatial.GDALAssist.Projection SelectionProjection(string component)
        {
            ThrowIfDisposed();
            int index = Array.FindIndex(components, item => item.Component == component);
            if (index < 0) throw new ArgumentException("A selected DSS grid component is missing: " + component);
            return selections[index].Projection
                ?? throw new InvalidDataException("The selected DSS grid has no valid projection. Spatial boundary condition imports require a valid grid projection.");
        }

        internal DssGridSeriesInfo Source(string component)
        {
            ThrowIfDisposed();
            int index = Array.FindIndex(components, item => item.Component == component);
            if (index < 0) throw new ArgumentException("A selected DSS grid component is missing: " + component);
            return sources[index];
        }

        internal string OperationId => operationId;

        internal void VerifyUnchanged()
        {
            ThrowIfDisposed();
            snapshot.VerifyUnchanged();
            cancellation.ThrowIfCancellationRequested();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DssGridImportOperation));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (IGriddedDataSelection selection in selections) (selection as IDisposable)?.Dispose();
            foreach (DssTimeSeriesRasterDataSource source in dataSources) source.Dispose();
            snapshot.Dispose();
        }
    }

    internal sealed class SourceSnapshot : IDisposable
    {
        private readonly string directory;
        public string SourcePath { get; }
        public string CopyPath { get; }
        public string Revision { get; }

        private SourceSnapshot(string sourcePath, string directory, string copyPath, string revision)
            => (SourcePath, this.directory, CopyPath, Revision) = (sourcePath, directory, copyPath, revision);

        public static SourceSnapshot Capture(string path, string? expectedRevision = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("An absolute DSS source file path is required.");
            path = SourceFilePaths.Canonical(path);
            if (!string.Equals(Path.GetExtension(path), ".dss", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select one DSS source file.");
            if (!File.Exists(path)) throw new FileNotFoundException("The DSS source file does not exist.", path);
            string revision = Hash(path);
            if (expectedRevision is not null && !string.Equals(expectedRevision, revision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            string directory = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var snapshot = new SourceSnapshot(path, directory, Path.Combine(directory, "source.dss"), revision);
            try
            {
                File.Copy(path, snapshot.CopyPath);
                if (Hash(snapshot.CopyPath) != revision) throw new SourceFileChangedException();
                snapshot.VerifyUnchanged();
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }

        public void VerifyUnchanged()
        {
            VerifySourceRevision(SourcePath, Revision);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

}

internal sealed class DssGridImportCancellation : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource source = new();
    private readonly Action<BackendOperationProgress>? report;
    private readonly object progressGate = new();
    private readonly HashSet<string> reportedFrames = new(StringComparer.Ordinal);
    private bool cancellationRequested;
    private bool committed;
    private bool disposed;
    private string? refreshWarning;
    private string? refreshErrorMessage;
    private int totalFrames;
    private int completedFrames;

    internal DssGridImportCancellation(string operationId, Action<BackendOperationProgress>? report)
    {
        if (!Guid.TryParseExact(operationId, "D", out _))
            throw new ArgumentException("A valid DSS grid import operation ID is required.");
        OperationId = operationId;
        this.report = report;
    }

    internal string OperationId { get; }
    internal CancellationToken Token => source.Token;
    internal bool IsCommitted { get { lock (gate) return committed; } }
    internal bool CancellationRequested { get { lock (gate) return cancellationRequested; } }
    internal (string Warning, string ErrorMessage)? RefreshFailure
    {
        get
        {
            lock (gate)
                return refreshWarning is null || refreshErrorMessage is null
                    ? null : (refreshWarning, refreshErrorMessage);
        }
    }

    internal void RecordRefreshFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (gate)
        {
            if (!committed) throw new InvalidOperationException("A DSS refresh warning requires a committed import.");
            refreshWarning ??= DssGridImportService.RefreshWarning;
            refreshErrorMessage ??= error.GetType().Name + ": " + error.Message;
        }
    }

    internal void ConfigureFrames(IEnumerable<DssGridImportComponent> components)
    {
        lock (progressGate)
        {
            totalFrames = components.Sum(component => component.RecordPaths.Length);
            completedFrames = 0;
            reportedFrames.Clear();
        }
    }

    internal void ReportFrame(string component, int frameIndex)
    {
        Action<BackendOperationProgress>? callback;
        double fraction;
        lock (progressGate)
        {
            if (totalFrames <= 0 || !reportedFrames.Add(component + "\0" + frameIndex.ToString(CultureInfo.InvariantCulture)))
                return;
            completedFrames++;
            fraction = Math.Min(0.95, 0.05 + 0.90 * completedFrames / totalFrames);
            callback = report;
        }
        callback?.Invoke(new(Fraction: fraction,
            Message: "Reading selected DSS grid records...", TaskName: "Grid import"));
    }

    internal bool RequestCancel()
    {
        lock (gate)
        {
            if (committed || disposed) return false;
            cancellationRequested = true;
        }
        source.Cancel();
        return true;
    }

    internal void ThrowIfCancellationRequested()
    {
        lock (gate)
            if (cancellationRequested)
                throw new OperationCanceledException("DSS grid import was cancelled.", source.Token);
    }

    internal void Publish(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (gate)
        {
            if (cancellationRequested)
                throw new OperationCanceledException("DSS grid import was cancelled.", source.Token);
            if (committed) throw new InvalidOperationException("The DSS grid import was already committed.");
            publish();
            committed = true;
        }
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        source.Dispose();
    }
}
