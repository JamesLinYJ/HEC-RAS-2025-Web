using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.MetImport;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Meteorology;
using Utility.Units;

namespace Ras.Backend;

public sealed record SpatialGridBand(string Id, string File, string Dataset, int Band, string Revision, string? Time, string? IssueTime,
    string Units, string DataType, bool Included, string Component);
public sealed record SpatialGridPreview(int Width, int Height, byte[] Rgba, double MinX, double MinY, double MaxX, double MaxY, double? Minimum, double? Maximum);
public sealed record SpatialGridData(string LayerId, string[] Times, string[] Components, string Units, string Projection, int Rows, int Columns,
    string? SelectedTimeId = null, string? SelectedComponent = null, SpatialGridPreview? Preview = null, string? Error = null);
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpatialGridTimeBehavior
{
    ProjectBackwards,
    ProjectForwards
}
public sealed record SpatialGridSelection(string LayerId, SpatialGridBand[] Bands, string Units, string DataType)
{
    // The retained raster-importer control initializes this enum to its zero
    // value, ProjectBackwards. It stores the choice but does not consume it
    // while importing or persist it to the boundary HDF.
    public SpatialGridTimeBehavior TimeBehavior { get; init; } = SpatialGridTimeBehavior.ProjectBackwards;
    public DssGridImportSelection? DssSource { get; init; }
}
public sealed record SpatialGridInspection(SpatialGridBand[] Bands, string[] Subdatasets, SpatialGridPreview? Preview,
    string[] UnitOptions, string Units, string DataType, DssGridSeriesInfo[]? DssGridSources = null,
    string? DssGridDraftIdentity = null, string? DssGridSourcePath = null, string? DssGridRevision = null);
public sealed record SpatialGridInspectRequest(ModelSaveRequest Model, string LayerId, string[] Files, string? Subdataset = null, SpatialGridBand[]? Bands = null, string? PreviewId = null);
public sealed record SpatialGridImportRequest(ModelSaveRequest Model, SpatialGridSelection Selection);
public sealed record SpatialGridFrameRequest(ModelSaveRequest Model, string LayerId, string? TimeId, string Component = "Value");

public static partial class ModelEditorAdapter
{
    public static Task<SpatialGridInspection> InspectGridAsync(SpatialGridInspectRequest request)
    {
        CheckGridModel(request.Model);
        using var context = new ModelContext(request.Model);
        var condition = context.Layer as BoundaryCondition ?? throw new ArgumentException("Grid inputs require boundary conditions.");
        var result = SpatialGridService.Inspect(condition, request);
        CheckGridModel(request.Model);
        return Task.FromResult(result);
    }
    public static async Task<ModelSnapshot> ImportGridAsync(SpatialGridImportRequest request)
    {
        CheckGridModel(request.Model);
        if (request.Selection.DssSource is { } dss)
        {
            string draftIdentity = DssGridImportService.DraftIdentity(request.Model);
            if (!string.Equals(dss.DraftIdentity, draftIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("The boundary draft changed after DSS grid inspection. Refresh the grid source.");
            using var cancellation = DssGridImportService.BeginImport(dss.OperationId);
            return await SaveModelCoreAsync(request.Model, request.Selection, dssGridCancellation: cancellation);
        }
        return await SaveAsync(request.Model, request.Selection);
    }

    internal static async Task<DssGridImportDuplexResult> ImportGridDuplexAsync(
        SpatialGridImportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader controls)
    {
        CheckGridModel(request.Model);
        DssGridImportSelection source = request.Selection.DssSource
            ?? throw new ArgumentException("The duplex grid import route is reserved for DSS grid series.");
        if (!string.Equals(source.DraftIdentity, DssGridImportService.DraftIdentity(request.Model), StringComparison.Ordinal))
            throw new InvalidOperationException("The boundary draft changed after DSS grid inspection. Refresh the grid source.");

        using var cancellation = DssGridImportService.BeginImport(source.OperationId, report);
        using var stopControls = new CancellationTokenSource();
        Exception? controlError = null;
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stopControls.Token).ConfigureAwait(false))
                {
                    if (!frame.Control.TryGetProperty("type", out System.Text.Json.JsonElement type)
                        || type.ValueKind != System.Text.Json.JsonValueKind.String
                        || !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    {
                        controlError = new InvalidDataException("The DSS grid import received an unsupported control type.");
                        cancellation.RequestCancel();
                        return;
                    }
                    cancellation.RequestCancel();
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            catch (Exception error)
            {
                controlError = error;
                cancellation.RequestCancel();
            }
        }, CancellationToken.None);

        try
        {
            cancellation.ThrowIfCancellationRequested();
            report?.Invoke(new(Fraction: 0.05, Message: "Reading selected DSS grid records...", TaskName: "Grid import"));
            ModelSnapshot snapshot = await SaveModelCoreAsync(request.Model, request.Selection,
                dssGridCancellation: cancellation);
            if (controlError is not null && !cancellation.IsCommitted)
                throw new InvalidDataException("The DSS grid import received an invalid control frame.", controlError);
            if (cancellation.RefreshFailure is { } refreshFailure)
            {
                report?.Invoke(new(Fraction: 1, Message: refreshFailure.Warning, TaskName: "Grid import"));
                return new("completed", snapshot, refreshFailure.ErrorMessage, refreshFailure.Warning);
            }
            report?.Invoke(new(Fraction: 1, Message: "Grid data and boundary edits were saved.", TaskName: "Grid import"));
            return new("completed", snapshot);
        }
        catch (OperationCanceledException) when (cancellation.CancellationRequested && !cancellation.IsCommitted)
        {
            if (controlError is not null)
                throw new InvalidDataException("The DSS grid import received an invalid control frame.", controlError);
            return new("cancelled", null);
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); } catch { }
        }
    }
    public static Task<SpatialGridData> GridFrameAsync(SpatialGridFrameRequest request)
    {
        CheckGridModel(request.Model);
        using var context = new ModelContext(request.Model);
        var frame = SpatialGridService.Frame(context.Layer as BoundaryCondition ?? throw new ArgumentException("Grid inputs require boundary conditions."), request.LayerId, request.TimeId, request.Component);
        CheckGridModel(request.Model);
        return Task.FromResult(frame);
    }
    private static void CheckGridModel(ModelSaveRequest model)
    {
        VerifyKind(model);
        if (model.Kind != "Boundary Conditions" || string.IsNullOrEmpty(model.ProjectPath)) throw new ArgumentException("Grid inputs require an owning boundary project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
    }
}

internal static class SpatialGridService
{
    private static readonly string[] Layers = ["precipitation", "wind", "airPressure", "airDensity"];
    internal static string Date(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
    internal static DateTime ParseDate(string? value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            || time.Year <= 1)
            throw new ArgumentException("Every selected raster band needs a valid timestep.");
        // Raster band timestamps without a suffix are wall-clock values. Keep
        // their ticks as Unspecified; only explicit UTC/local inputs undergo
        // the existing conversion to the machine's local time.
        return time.Kind == DateTimeKind.Unspecified ? time : time.ToLocalTime();
    }
    private static SpatialBoundaryConditionBase Layer(BoundaryCondition boundary, string id) => id switch {
        "precipitation" => boundary.Precipitation, "wind" => boundary.Wind, "airPressure" => boundary.AirPressure,
        "airDensity" => boundary.AirDensity, _ => throw new ArgumentException("This layer does not support grid inputs.")
    };
    private static string Hash(string filename)
    {
        // A VRT may refer to other raster files. Bind the complete local
        // dependency set, not just its small XML descriptor.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in (GDALRaster.GetVRTSources(filename) ?? [filename]).Append(filename).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            byte[] name = Encoding.UTF8.GetBytes(file); hash.AppendData(BitConverter.GetBytes(name.Length)); hash.AppendData(name);
            using var input = File.OpenRead(file); hash.AppendData(BitConverter.GetBytes(input.Length)); byte[] bytes = new byte[65536]; int count;
            while ((count = input.Read(bytes)) > 0) hash.AppendData(bytes.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string Id(string dataset, int band) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataset + "\n" + band)));
    private static string Canonical(BoundaryCondition boundary, string layer) => layer switch {
        "precipitation" => boundary.Project.Units == UnitSystem.USCustomary ? "in/hr" : "mm/hr", "wind" => "m/s", "airPressure" => "mb", _ => "kg/m3"
    };
    private static string[] Units(string layer) => layer switch {
        "precipitation" => ["mm/hr", "in/hr", "mm", "in", "m"], "wind" => ["m/s", "ft/s"], "airPressure" => ["mb", "Pa", "kPa", "mmHg"], _ => ["kg/m3"]
    };

    public static SpatialGridInspection Inspect(BoundaryCondition boundary, SpatialGridInspectRequest request)
    {
        _ = Layer(boundary, request.LayerId);
        if (request.Files.Any(path => string.Equals(Path.GetExtension(path), ".dss", StringComparison.OrdinalIgnoreCase)))
        {
            if (request.Files.Length != 1
                || !string.Equals(Path.GetExtension(request.Files[0]), ".dss", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A DSS grid import uses one DSS source file at a time.");
            DssGridInspection dss = DssGridImportService.Inspect(request.Files[0],
                DssGridImportService.DraftIdentity(request.Model), request.PreviewId);
            return new([], dss.Sources.Select(source => source.Id).ToArray(), dss.Preview,
                Units(request.LayerId), Canonical(boundary, request.LayerId),
                request.LayerId == "precipitation" ? "per-avg" : "inst-val", dss.Sources, dss.DraftIdentity,
                dss.SourcePath, dss.Revision);
        }
        List<SpatialGridBand> bands = []; HashSet<string> subsets = [];
        var previous = (request.Bands ?? []).ToDictionary(band => band.Id);
        foreach (string path in request.Files.Distinct(StringComparer.Ordinal))
        {
            string file = Path.GetFullPath(path), hash = Hash(file);
            using var root = new GDALRaster(file);
            var children = root.GetSubDatasets();
            foreach (string key in children.Keys) subsets.Add(key[(key.LastIndexOf(':') + 1)..]);
            if (children.Count > 0 && string.IsNullOrEmpty(request.Subdataset)) continue;
            string dataset = string.IsNullOrEmpty(request.Subdataset) ? file
                : children.Keys.SingleOrDefault(key => key[(key.LastIndexOf(':') + 1)..] == request.Subdataset)
                    ?? throw new ArgumentException("The selected subdataset is missing from a source file.");
            using var source = new GDALRaster(dataset);
            var summary = new GdalRasterSummary(source);
            for (int index = 0; index < summary.BandCount; index++)
            {
                string id = Id(dataset, index);
                var initial = new SpatialGridBand(id, file, dataset, index, hash,
                    summary.BandTimestamps[index].Year > 1 ? Date(summary.BandTimestamps[index]) : null,
                    summary.ForecastIssueTimes[index].Year > 1 ? Date(summary.ForecastIssueTimes[index]) : null,
                    summary.Units ?? "", summary.DataType ?? "", true, request.LayerId == "wind" ? "X" : "Value");
                if (previous.TryGetValue(id, out var edit) && edit.Revision == hash)
                    initial = initial with { Included = edit.Included, Time = edit.Time, Component = edit.Component };
                bands.Add(initial);
            }
            if (Hash(file) != hash) throw new SourceFileChangedException();
        }
        SpatialGridPreview? preview = null;
        var selected = bands.FirstOrDefault(band => band.Id == request.PreviewId) ?? bands.FirstOrDefault();
        if (selected != null)
        {
            using var source = new GDALRaster(selected.Dataset);
            using var data = new GdalBandedRasterDataSource<float>(source);
            preview = Preview(data.AsBandedRasterizer(), selected.Band);
        }
        var choices = Units(request.LayerId);
        string units = bands.Select(band => band.Units).Distinct().Count() == 1 && choices.Contains(bands[0].Units) ? bands[0].Units : Canonical(boundary, request.LayerId);
        string dataType = request.LayerId == "precipitation" && !units.Contains('/') ? "per-cum" : request.LayerId == "precipitation" ? "per-avg" : "inst-val";
        return new(bands.ToArray(), subsets.Order(StringComparer.Ordinal).ToArray(), preview, choices, units, dataType);
    }

    public static void Import(BoundaryCondition boundary, SpatialGridSelection selection,
        DssGridImportCancellation? dssGridCancellation = null)
    {
        // Keep the retained transient UI choice in the typed import contract.
        // The source importer does not apply a projection algorithm or store it.
        if (selection.TimeBehavior is not (SpatialGridTimeBehavior.ProjectBackwards or SpatialGridTimeBehavior.ProjectForwards))
            throw new ArgumentException("Unknown raster import time behavior.");
        var layer = Layer(boundary, selection.LayerId);
        if (!Units(selection.LayerId).Contains(selection.Units)) throw new ArgumentException("Unsupported source raster units.");
        if (selection.DssSource is { } dssSelection)
        {
            if (selection.Bands.Any(band => band.Included))
                throw new ArgumentException("A DSS series selection cannot be mixed with GDAL raster bands.");
            dssGridCancellation?.ThrowIfCancellationRequested();
            if (dssGridCancellation is null || dssGridCancellation.OperationId != dssSelection.OperationId)
                throw new InvalidOperationException("A DSS grid import requires its owning cancellation context.");
            using var imported = DssGridImportService.Open(dssSelection, dssGridCancellation);
            string[] expectedComponents = selection.LayerId == "wind" ? ["X", "Y"] : ["Value"];
            if (dssSelection.Components.Select(component => component.Component).Order(StringComparer.Ordinal)
                .SequenceEqual(expectedComponents.Order(StringComparer.Ordinal)) is false)
                throw new ArgumentException(selection.LayerId == "wind"
                    ? "Wind DSS import requires one complete X and one complete Y series from the same DSS file."
                    : "This boundary layer accepts one DSS Value series.");

            if (selection.LayerId == "wind")
            {
                var x = imported.Raster("X"); var y = imported.Raster("Y");
                if (!x.Times.SequenceEqual(y.Times) || !x.RasterDefinition.Equals(y.RasterDefinition)
                    || (imported.Raster("X").NoDataValue ?? -9999f) != (imported.Raster("Y").NoDataValue ?? -9999f))
                    throw new ArgumentException("Wind X and Y DSS series must have identical valid times, grid spacing, dimensions and NoData values.");
                string projection = dssSelection.Components.Single(component => component.Component == "X").GridIdentity;
                string yProjection = dssSelection.Components.Single(component => component.Component == "Y").GridIdentity;
                if (!string.Equals(projection, yProjection, StringComparison.Ordinal))
                    throw new ArgumentException("Wind X and Y DSS series must use the same grid definition and projection.");
                ValidateDssUnits(imported.Source("X"), selection.Units, "Wind X");
                ValidateDssUnits(imported.Source("Y"), selection.Units, "Wind Y");
                if (!string.Equals(imported.Source("X").DataType, "INST_VAL", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(imported.Source("Y").DataType, "INST_VAL", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Wind DSS grid records must use instantaneous-value data type.");
                if (!string.Equals(imported.Source("X").Units, imported.Source("Y").Units, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Wind X and Y DSS series must use the same source units.");
                float scale = selection.Units == "ft/s" ? .3048f : 1;
                if (layer is not SpatialVectorBoundaryConditionBase vector)
                    throw new ArgumentException("Wind grids require a vector boundary layer.");
                vector.ImportRasters(Scaled(x, scale), Scaled(y, scale), imported.SelectionProjection("X"), "inst-val", "m/s");
            }
            else
            {
                ITimeSeriesRaster<float> raster = imported.Raster("Value");
                DssGridSeriesInfo source = imported.Source("Value");
                ValidateDssUnits(source, selection.Units, "DSS grid");
                if (layer is PrecipitationLayer precip)
                {
                    if (selection.DataType is not ("per-avg" or "per-cum"))
                        throw new ArgumentException("Select a supported DSS precipitation data type.");
                    string expectedDssType = selection.DataType == "per-avg" ? "PER_AVER" : "PER_CUM";
                    if (!string.Equals(source.DataType, expectedDssType, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("The selected DSS precipitation record type does not match the requested period average/cumulative data type.");
                    precip.ImportRaster(raster, imported.SelectionProjection("Value"),
                        Precipitation.TryParse(selection.Units) ?? throw new ArgumentException("Invalid precipitation units."),
                        boundary.Project.Units == UnitSystem.USCustomary ? Length.Inches : Length.Millimeters);
                }
                else
                {
                    if (!string.Equals(source.DataType, "INST_VAL", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Air pressure and air density DSS grids must use instantaneous-value data type.");
                    float scale = selection.LayerId == "airPressure"
                        ? (float)(AirPressure.TryParse(selection.Units) ?? throw new ArgumentException("Invalid pressure units.")).ConvertTo(AirPressure.MilliBars, 1)
                        : 1;
                    ((SpatialScalarBoundaryConditionBase)layer).ImportRaster(Scaled(raster, scale),
                        imported.SelectionProjection("Value"), "inst-val", Canonical(boundary, selection.LayerId));
                }
            }
            imported.VerifyUnchanged();
            return;
        }
        var included = selection.Bands.Where(band => band.Included).ToArray();
        if (included.Length == 0) throw new ArgumentException("Select raster bands before importing.");
        if (included.Select(band => band.Id).Distinct().Count() != included.Length) throw new ArgumentException("A source band was selected more than once.");
        foreach (var band in included)
        {
            if (band.Revision != Hash(band.File) || band.Id != Id(band.Dataset, band.Band)) throw new SourceFileChangedException();
            // GDAL connection strings are source identities, not arbitrary virtual URLs.
            using var original = new GDALRaster(band.File);
            if (band.Dataset != Path.GetFullPath(band.File) && !original.GetSubDatasets().ContainsKey(band.Dataset))
                throw new ArgumentException("The selected raster does not belong to its source file.");
        }
        var components = selection.LayerId == "wind" ? new[] { "X", "Y" } : ["Value"];
        if (included.Any(band => !components.Contains(band.Component))) throw new ArgumentException("Unknown raster component.");
        var selections = new List<IGriddedDataSelection>();
        try
        {
            foreach (string component in components)
            {
                var ordered = included.Where(band => band.Component == component).OrderBy(band => ParseDate(band.Time)).ToArray();
                if (ordered.Length == 0 || ordered.Select(band => ParseDate(band.Time)).Distinct().Count() != ordered.Length)
                    throw new ArgumentException("Each component needs one selected band per timestep; resolve overlapping issue times first.");
                List<(IGriddedDataSource, int)> chosen = [];
                foreach (var band in ordered)
                {
                    using var source = new GDALRaster(band.Dataset); var summary = new GdalRasterSummary(source);
                    if (band.Band < 0 || band.Band >= summary.BandCount) throw new ArgumentException("A selected source band is missing.");
                    summary.BandTimestamps[band.Band] = ParseDate(band.Time);
                    chosen.Add((new GriddedGdalRaster(summary), band.Band));
                }
                var result = new GriddedGdalRasterImportController().CreateSelection(chosen, out var rasterSelection);
                if (!result.Result) { rasterSelection?.Dispose(); throw new IOException(string.Join("; ", result.Messages.Select(message => message.EvaluatedMessage))); }
                selections.Add(rasterSelection);
            }
            var first = (ITimeSeriesRaster<float>)selections[0].Surface;
            if (layer is SpatialVectorBoundaryConditionBase vector)
            {
                var second = (ITimeSeriesRaster<float>)selections[1].Surface;
                if (!first.Times.SequenceEqual(second.Times) || !first.RasterDefinition.Equals(second.RasterDefinition)
                    || (selections[0].Projection?.GetWKT() ?? "") != (selections[1].Projection?.GetWKT() ?? ""))
                    throw new ArgumentException("Wind X/Y components must have the same timesteps, grid and projection.");
                float scale = selection.Units == "ft/s" ? .3048f : 1;
                vector.ImportRasters(Scaled(first, scale), Scaled(second, scale), selections[0].Projection, "inst-val", "m/s");
            }
            else if (layer is PrecipitationLayer precip)
            {
                if (selection.DataType is not ("per-avg" or "per-cum") || (selection.DataType == "per-avg") != selection.Units.Contains('/'))
                    throw new ArgumentException("Precipitation data type and units must agree.");
                precip.ImportRaster(first, selections[0].Projection, Precipitation.TryParse(selection.Units) ?? throw new ArgumentException("Invalid precipitation units."),
                    boundary.Project.Units == UnitSystem.USCustomary ? Length.Inches : Length.Millimeters);
            }
            else
            {
                float scale = 1;
                if (selection.LayerId == "airPressure") scale = (float)(AirPressure.TryParse(selection.Units) ?? throw new ArgumentException("Invalid pressure units.")).ConvertTo(AirPressure.MilliBars, 1);
                ((SpatialScalarBoundaryConditionBase)layer).ImportRaster(Scaled(first, scale), selections[0].Projection, "inst-val", Canonical(boundary, selection.LayerId));
            }
            foreach (var band in included) if (Hash(band.File) != band.Revision) throw new SourceFileChangedException();
        }
        finally { foreach (var value in selections) value.Dispose(); }
    }
    private static ITimeSeriesRaster<float> Scaled(ITimeSeriesRaster<float> source, float scale) => scale == 1 ? source
        : source.WithTransformer_Raster(null, new ScaleValueTransformer<float>(scale));

    private static void ValidateDssUnits(DssGridSeriesInfo source, string units, string label)
    {
        if (!string.Equals(source.Units.Trim(), units.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{label} source units '{source.Units}' do not match the selected units '{units}'.");
    }

    public static Dictionary<string, SpatialGridData> Overviews(BoundaryCondition boundary) => Layers.ToDictionary(id => id, id => {
        try { return Frame(boundary, id, null); }
        catch (Exception error) { return new SpatialGridData(id, [], id == "wind" ? ["X", "Y"] : ["Value"], Canonical(boundary, id), "", 0, 0, Error: error.Message); }
    });
    public static SpatialGridData Frame(BoundaryCondition boundary, string layerId, string? timeId, string component = "Value")
    {
        var layer = Layer(boundary, layerId);
        string[] components = layerId == "wind" ? ["X", "Y"] : ["Value"];
        if (!components.Contains(component))
        {
            if (layerId == "wind" && component == "Value") component = "X";
            else throw new ArgumentException("Unknown raster component.");
        }
        if (layer.GridDataLocation == DataLocation.Remote)
            return new(layerId, [], components, Canonical(boundary, layerId), "", 0, 0, Error: "Remote grid evaluation is not implemented in the retained source.");
        string dataset = layer is SpatialVectorBoundaryConditionBase vector
            ? component == "X" ? vector.ImportedRasterDatasetA : vector.ImportedRasterDatasetB : layer.ImportedRasterDataset;
        bool exists = false; boundary.UseReader(reader => exists = reader.ExistsAsDataset(dataset));
        if (!exists) return new(layerId, [], components, Canonical(boundary, layerId), "", 0, 0);
        var opened = H5RasterWriter.TryReadTimeSeriesRaster(boundary.GetH5Wrapper(), dataset, out RasterMetadata metadata, out H5TimeSeriesRasterDataSource<float> source);
        if (!opened.Result) throw new IOException(string.Join("; ", opened.Messages.Select(message => message.EvaluatedMessage)));
        using var ownedSource = source;
        string[] times = source.Times.Select(Date).ToArray(); var definition = source.RasterDefinition;
        int index = timeId == null ? -1 : Array.IndexOf(times, timeId);
        if (timeId != null && index < 0) throw new ArgumentException("The selected raster timestep is missing.");
        var preview = index < 0 ? null : Preview(source.AsTimeSeriesRasterizer(), index);
        return new(layerId, times, components, metadata.Units ?? "", metadata.Projection ?? "", definition.Rows, definition.Cols, timeId, component, preview);
    }
    internal static SpatialGridPreview Preview(IMultiBandRaster<float> raster, int band)
    {
        var d = raster.RasterDefinition;
        if (d.IsNull() || d.Rows <= 0 || d.Cols <= 0) throw new InvalidDataException("The source raster has invalid dimensions.");
        double reduction = Math.Max(1, Math.Max(d.Rows, d.Cols) / 256d);
        int width = Math.Max(1, (int)Math.Ceiling(d.Cols / reduction)), height = Math.Max(1, (int)Math.Ceiling(d.Rows / reduction));
        Point[] points = new Point[width * height]; float[] values = new float[points.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) points[y * width + x] = new(d.Left + (x + .5) * d.Width / width, d.Top - (y + .5) * d.Height / height);
        raster.SamplePoints(points, band, values);
        bool Valid(float value) => float.IsFinite(value) && (!raster.NoDataValue.HasValue || value != raster.NoDataValue.Value);
        var valid = values.Where(Valid).ToArray(); double? min = valid.Length == 0 ? null : valid.Min(), max = valid.Length == 0 ? null : valid.Max();
        byte[] rgba = new byte[points.Length * 4];
        for (int index = 0; index < values.Length; index++) if (Valid(values[index]))
        {
            double t = max > min ? (values[index] - min!.Value) / (max!.Value - min.Value) : .5;
            rgba[index * 4] = (byte)(30 + 180 * t); rgba[index * 4 + 1] = (byte)(80 + 120 * t); rgba[index * 4 + 2] = 230; rgba[index * 4 + 3] = 255;
        }
        return new(width, height, rgba, d.Left, d.Bottom, d.Right, d.Top, min, max);
    }
}
