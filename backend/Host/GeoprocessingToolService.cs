using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Validation;

namespace Ras.Backend;

public sealed record GeoprocessingPolylineInput(
    string FeatureIdentity,
    string PathIdentity,
    int SourceFeatureIndex,
    int PathIndex,
    double[][] Coordinates,
    string[]? ZValues = null,
    string[]? MValues = null);

public sealed record GeoprocessingPolylineOutput(
    string FeatureIdentity,
    string PathIdentity,
    int SourceFeatureIndex,
    int PathIndex,
    double[][] Coordinates,
    string[]? ZValues = null,
    string[]? MValues = null);

public sealed record GeoprocessingPointInput(
    string FeatureIdentity,
    string PathIdentity,
    int SourceFeatureIndex,
    int PathIndex,
    double[] Coordinates);

public sealed record GeoprocessingPolygonInput(
    string FeatureIdentity,
    string PathIdentity,
    int SourceFeatureIndex,
    int PathIndex,
    double[][] Coordinates,
    string[]? ZValues = null,
    string[]? MValues = null,
    double[][][]? Holes = null,
    string[]?[]? HoleZValues = null,
    string[]?[]? HoleMValues = null);

public sealed record GeoprocessingPolygonOutput(
    string FeatureIdentity,
    string PathIdentity,
    int SourceFeatureIndex,
    int PathIndex,
    double[][] Coordinates,
    string[]? ZValues = null,
    string[]? MValues = null,
    double[][][]? Holes = null,
    string[]?[]? HoleZValues = null,
    string[]?[]? HoleMValues = null);

/// <summary>
/// Exact persisted map child and full GDB-directory revision for a vector
/// geoprocessing input. AssetID alone is not a layer identity inside a GDB.
/// </summary>
public sealed record GeoprocessingGdbSourceCapture(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string ExpectedSourceRevision,
    string LayerName,
    string VectorType);

/// <summary>Exact active map Result slot/profile used as a float raster input.</summary>
public sealed record GeoprocessingResultRasterSourceCapture(
    MapTreeRequest Map,
    MapResultSelectionOverride[] SelectionOverrides,
    string ContextToken,
    CatalogTreeNodeLocator Node,
    int SelectionIndex,
    string ResultPath,
    string ResultRevision,
    int ProfileIndex,
    int MapProfileIndex,
    string MapType,
    string TerrainPath,
    string TerrainRevision,
    double[] ValidExtent);

/// <summary>Exact checked Terrain association and its persisted/draft owners.</summary>
public sealed record GeoprocessingTerrainRasterSourceCapture(
    AssociatedMapLayersRequest Request,
    CatalogTreeNodeLocator Node,
    string SourcePath,
    string SourceRevision,
    AssociatedMapLayerDependency[] Dependencies,
    double[] ExpectedExtent);

public sealed record GeoprocessingPolylineRequest(
    string SourcePath,
    string SourceRevision,
    string ProjectDirectory,
    string ProjectProjectionType,
    string ProjectProjectionValue,
    string Operation,
    string InputMode,
    GeoprocessingPolylineInput[] InputLines,
    string CultureName = "en-US",
    string ToleranceText = "0.05",
    string MovingAverageWindowText = "5",
    string MovingAveragePassesText = "1",
    string OutputHandler = "Clipboard",
    string? OutputName = null,
    MapSourceFamilyCapture? SourceFamily = null,
    string? TargetProjectionWKT = null,
    string MinimumLengthText = "10",
    string MaxSegmentLengthText = "10",
    string MergeToleranceText = "1",
    string MergeMode = "PreferClosest",
    string OffsetText = "10",
    string OffsetSides = "Both",
    bool IncludeEndpoints = false,
    GeoprocessingPolygonInput[]? InputPolygons = null,
    RasterMapSourceIdentity? RasterSource = null,
    string? ProjectXml = null,
    string BufferWidthText = "10",
    string BufferSmoothingMethod = "Standard",
    string BufferMovingAverageWindowText = "5",
    string BufferMovingAveragePassesText = "1",
    string BufferPolygonDistanceText = "10",
    string CellSizeText = "10",
    string FillValueText = "1",
    string BackgroundValueText = "0",
    string MaxDistanceCellsText = "100",
    string DistanceAlgorithm = "EDT",
    string SigmaCellsText = "10",
    string SearchRadiusText = "50",
    string SampleSpacingText = "3",
    string SearchMode = "Perpendicular",
    string ElevationMode = "Highest",
    string PointsPerRingText = "16",
    string IterationsText = "1",
    string OutputCellSizeText = "0",
    string BinCountText = "25",
    bool UseAdvancedSizing = false,
    string BoundarySpacingText = "10",
    string InteriorGridSpacingText = "10",
    string RoundTo = "Tenths",
    string Connectivity = "EightCorners",
    string NoDataOutputMode = "Patch",
    string MaxSlopePercentText = "5",
    string SimplifyDistanceText = "0",
    string MinimumRingAreaText = "0",
    double[]? ClipExtent = null,
    GeoprocessingPointInput[]? InputPoints = null,
    GeoprocessingGdbSourceCapture? GdbSource = null,
    GeoprocessingResultRasterSourceCapture? ResultRasterSource = null,
    GeoprocessingTerrainRasterSourceCapture? TerrainRasterSource = null);

public sealed record GeoprocessingValidationIssue(string Field, string Message, string Severity);
public sealed record GeoprocessingPolylineValidationRequest(
    string Operation,
    string CultureName = "en-US",
    string ToleranceText = "0.05",
    string MovingAverageWindowText = "5",
    string MovingAveragePassesText = "1",
    string OutputHandler = "Clipboard",
    string? OutputName = null,
    string MinimumLengthText = "10",
    string MaxSegmentLengthText = "10",
    string MergeToleranceText = "1",
    string MergeMode = "PreferClosest",
    string OffsetText = "10",
    string OffsetSides = "Both",
    bool IncludeEndpoints = false,
    string BufferWidthText = "10",
    string BufferSmoothingMethod = "Standard",
    string BufferMovingAverageWindowText = "5",
    string BufferMovingAveragePassesText = "1",
    string BufferPolygonDistanceText = "10",
    string CellSizeText = "10",
    string FillValueText = "1",
    string BackgroundValueText = "0",
    string MaxDistanceCellsText = "100",
    string DistanceAlgorithm = "EDT",
    string SigmaCellsText = "10",
    string SearchRadiusText = "50",
    string SampleSpacingText = "3",
    string SearchMode = "Perpendicular",
    string ElevationMode = "Highest",
    string PointsPerRingText = "16",
    string IterationsText = "1",
    string OutputCellSizeText = "0",
    string BinCountText = "25",
    bool UseAdvancedSizing = false,
    string BoundarySpacingText = "10",
    string InteriorGridSpacingText = "10",
    string RoundTo = "Tenths",
    string Connectivity = "EightCorners",
    string NoDataOutputMode = "Patch",
    string MaxSlopePercentText = "5",
    string SimplifyDistanceText = "0",
    string MinimumRingAreaText = "0",
    double[]? ClipExtent = null);
public sealed record GeoprocessingPolylineValidationResponse(GeoprocessingValidationIssue[] ValidationIssues);
public sealed record GeoprocessingPolylineResponse(
    bool CanExecute,
    string Operation,
    string OutputTitle,
    int InputLineCount,
    int OutputLineCount,
    string? OutputPath,
    string? ClipboardText,
    GeoprocessingPolylineOutput[] Outputs,
    GeoprocessingValidationIssue[] ValidationIssues,
    string[] CapturedPathIdentities,
    string OutputKind = "Polyline",
    GeoprocessingPolygonOutput[]? PolygonOutputs = null,
    int InputPolygonCount = 0,
    int OutputPolygonCount = 0,
    string? TextOutput = null,
    string? OutputFormat = null,
    string[]? CapturedPolygonPathIdentities = null,
    int? OutputRasterWidth = null,
    int? OutputRasterHeight = null,
    double[]? OutputRasterExtent = null,
    int InputPointCount = 0,
    string[]? CapturedPointIdentities = null);

/// <summary>
/// Executes the retained line geoprocessing operations on the exact
/// captured line components of a live map layer. The source file is revision
/// checked before and after every operation; output handlers write new files.
/// </summary>
public static class GeoprocessingToolService
{
    internal const int MaximumSourceBytes = 128 * 1024 * 1024;
    private const int MaximumLineCount = 100_000;
    private const int MaximumPointCountPerLine = 1_000_000;
    private const long MaximumTotalPointCount = 5_000_000;
    private static readonly string[] OutputHandlers = ["Clipboard", "Shapefile", "Map Layer"];
    private static readonly string[] Operations =
        ["Filter", "Smooth", "Reverse", "CullByLength", "DensifyPolylines", "MergePolylines", "LineOffset"];

    private static readonly string[] ExtendedOperations =
        ["BufferPolyline", "PolylineToPolygon", "BufferPolygon", "DensifyPolygons", "PolygonToPolyline",
         "SnapToHighGround",
         "RasterizePolygons", "DistanceToLines", "ProximityHeatmap",
         "ClipRasterToPolygon", "RasterCutStats", "LaplacianTerrain",
         "ResampleRaster", "RoundRaster", "NoDataLaplacianFill", "FlatAreaDetector"];

    public static GeoprocessingPolylineResponse Execute(
        GeoprocessingPolylineRequest request,
        Action<BackendOperationProgress>? report = null)
        => ExecuteCore(request, report, CancellationToken.None);

    internal static async Task<GeoprocessingPolylineResponse> ExecuteDuplexAsync(
        GeoprocessingPolylineRequest request,
        Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        using var cancellation = new CancellationTokenSource();
        using var stopControlPump = new CancellationTokenSource();
        Task? controlPump = StartControlPump(duplexControls, cancellation, stopControlPump.Token);
        try
        {
            return await Task.Run(() => ExecuteCore(request, report, cancellation.Token), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            stopControlPump.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static GeoprocessingPolylineResponse ExecuteCore(
        GeoprocessingPolylineRequest request,
        Action<BackendOperationProgress>? report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (ExtendedOperations.Contains(request.Operation, StringComparer.Ordinal))
            return GeoprocessingSourceOperations.Execute(request, report, cancellationToken);
        if (!Operations.Contains(request.Operation, StringComparer.Ordinal))
            throw new ArgumentException("Choose a supported polyline processing tool.");
        if (request.InputMode is not ("All" or "Selected"))
            throw new ArgumentException("Choose all lines or the selected lines.");
        if (!OutputHandlers.Contains(request.OutputHandler, StringComparer.Ordinal))
            throw new ArgumentException("Choose Clipboard, Shapefile, or Map Layer output.");
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath)
            || !IsSHA256(request.SourceRevision))
            throw new ArgumentException("A canonical source path and captured SHA-256 revision are required.");

        string sourcePath = Path.GetFullPath(request.SourcePath);
        string capturedRevision = CaptureSourceRevision(request);
        if (!capturedRevision.Equals(request.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        VerifySource(request, sourcePath, capturedRevision);
        VerifyTargetProjection(request);

        GeoprocessingValidationIssue[] issues = Validate(request).ToArray();
        if (issues.Any(issue => issue.Severity is "Error" or "TypeError"))
            return new(false, request.Operation, OutputTitle(request.Operation), request.InputLines?.Length ?? 0,
                0, null, null, [], issues,
                request.InputLines?.Select(line => line?.PathIdentity ?? "").ToArray() ?? []);

        GeoprocessingPolylineInput[] inputs = request.InputLines ?? [];
        if (inputs.Length == 0 || inputs.Length > MaximumLineCount
            || inputs.Any(line => line is null || string.IsNullOrWhiteSpace(line.FeatureIdentity)
                || string.IsNullOrWhiteSpace(line.PathIdentity) || line.SourceFeatureIndex < 0 || line.PathIndex < 0)
            || inputs.Select(line => line.PathIdentity).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
            throw new InvalidDataException("The captured line selection is empty or has duplicate/missing source identities.");
        if (request.InputMode == "Selected" && inputs.Length == 0)
            throw new InvalidDataException("Input polylines cannot be null or empty.");

        long pointCount = 0;
        foreach (GeoprocessingPolylineInput input in inputs)
        {
            double[][] coordinates = input.Coordinates ?? [];
            if (coordinates.Length < 2 || coordinates.Length > MaximumPointCountPerLine
                || coordinates.Any(point => point is null || point.Length != 2
                    || !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
                throw new InvalidDataException("Only finite two-dimensional XY polylines with at least two vertices are supported.");
            ValidateOrdinates(input.ZValues, coordinates.Length, "Z");
            ValidateOrdinates(input.MValues, coordinates.Length, "M");
            pointCount += coordinates.Length;
            if (pointCount > MaximumTotalPointCount)
                throw new InvalidDataException("The captured line selection exceeds the supported vertex count.");
        }

        ParsedInputs parsed = ParseInputs(request);
        var outputs = new List<GeoprocessingPolylineOutput>(inputs.Length);
        report?.Invoke(new BackendOperationProgress(Fraction: 0.02, TaskName: "Checking source lines"));
        if (request.Operation == "MergePolylines")
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Polyline> sourceLines = inputs.Select(ToPolyline).ToList();
            List<Polyline> merged = [];
            List<int> unmerged = [];
            if (sourceLines.Count == 1)
                unmerged.Add(0);
            else
            {
                PolylineMergeHelpers.MergeResult result = PolylineMergeHelpers.MergePolylines(
                    sourceLines, parsed.MergeTolerance, parsed.MergeMode);
                merged.AddRange(result.MergedPolylines);
                unmerged.AddRange(result.UnmergedIndices);
            }
            foreach (Polyline line in merged)
                outputs.Add(CreateGeneratedOutput(line, outputs.Count));
            foreach (int index in unmerged)
                outputs.Add(CreateGeneratedOutput(sourceLines[index].Clone(), outputs.Count));
        }
        else if (request.Operation == "LineOffset")
        {
            for (int index = 0; index < inputs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var generated = new List<Polyline>();
                _ = OffsetPolylineFactory.TryComputeOffsetParts(ToPolyline(inputs[index]), parsed.Offset,
                    parsed.OffsetSides, request.IncludeEndpoints, generated);
                foreach (Polyline line in generated)
                    outputs.Add(CreateGeneratedOutput(line, outputs.Count));
                report?.Invoke(ProgressFor(index, inputs.Length, "Offsetting polylines"));
            }
        }
        else
        {
            for (int index = 0; index < inputs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeoprocessingPolylineInput input = inputs[index];
                Polyline source = ToPolyline(input);
                if (request.Operation == "CullByLength" && source.Length < parsed.MinimumLength)
                {
                    report?.Invoke(ProgressFor(index, inputs.Length, "Filtering polylines by length"));
                    continue;
                }
                Polyline transformed = Transform(source, request.Operation, parsed);
                outputs.Add(CreateSourceOutput(input, transformed));
                report?.Invoke(ProgressFor(index, inputs.Length, OperationProgressName(request.Operation)));
            }
        }

        EnsureSourceSnapshot(request, sourcePath, capturedRevision);
        string? clipboardText = null;
        string? outputPath = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (request.OutputHandler == "Clipboard")
        {
            clipboardText = ToClipboardText(outputs.Select(ToPolyline).ToArray());
        }
        else
        {
            if (issues.Any(issue => issue.Field == "OutputName" && issue.Severity is "Error" or "TypeError"))
                return new(false, request.Operation, OutputTitle(request.Operation), inputs.Length, outputs.Count,
                    null, null, outputs.ToArray(), issues, inputs.Select(line => line.PathIdentity).ToArray());
            report?.Invoke(new BackendOperationProgress(Fraction: 0.84, TaskName: "Writing Shapefile output"));
            outputPath = WriteShapefile(request, request.OutputName ?? "", outputs.Select(ToPolyline).ToArray(), cancellationToken);
            try
            {
                EnsureSourceSnapshot(request, sourcePath, capturedRevision);
            }
            catch
            {
                DeleteShapefileFamily(outputPath);
                throw;
            }
        }

        EnsureSourceSnapshot(request, sourcePath, capturedRevision);
        report?.Invoke(new BackendOperationProgress(Fraction: 1, TaskName: "Finishing output"));
        return new(true, request.Operation, OutputTitle(request.Operation), inputs.Length, outputs.Count,
            outputPath, clipboardText, outputs.ToArray(), issues, inputs.Select(line => line.PathIdentity).ToArray());
    }

    public static GeoprocessingPolylineValidationResponse ValidateInputs(GeoprocessingPolylineValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ExtendedOperations.Contains(request.Operation, StringComparer.Ordinal))
            return GeoprocessingSourceOperations.ValidateInputs(request);
        if (!Operations.Contains(request.Operation, StringComparer.Ordinal))
            throw new ArgumentException("Choose a supported polyline processing tool.");
        if (!OutputHandlers.Contains(request.OutputHandler, StringComparer.Ordinal))
            throw new ArgumentException("Choose Clipboard, Shapefile, or Map Layer output.");
        var executionRequest = new GeoprocessingPolylineRequest(
            SourcePath: "", SourceRevision: "", ProjectDirectory: "", ProjectProjectionType: "",
            ProjectProjectionValue: "", Operation: request.Operation, InputMode: "All", InputLines: [],
            CultureName: request.CultureName, ToleranceText: request.ToleranceText,
            MovingAverageWindowText: request.MovingAverageWindowText,
            MovingAveragePassesText: request.MovingAveragePassesText,
            OutputHandler: request.OutputHandler, OutputName: request.OutputName,
            MinimumLengthText: request.MinimumLengthText, MaxSegmentLengthText: request.MaxSegmentLengthText,
            MergeToleranceText: request.MergeToleranceText, MergeMode: request.MergeMode,
            OffsetText: request.OffsetText, OffsetSides: request.OffsetSides,
            IncludeEndpoints: request.IncludeEndpoints);
        return new(Validate(executionRequest).ToArray());
    }

    private sealed record ParsedInputs(double Tolerance, int Window, int Passes, double MinimumLength,
        double MaxSegmentLength, double MergeTolerance, MergeMode MergeMode, double Offset, OffsetSides OffsetSides);

    private static IEnumerable<GeoprocessingValidationIssue> Validate(GeoprocessingPolylineRequest request)
    {
        var issues = new List<GeoprocessingValidationIssue>();
        if (request.Operation is "Filter" or "LineOffset" or "CullByLength" or "DensifyPolylines")
        {
            string text = request.Operation switch
            {
                "Filter" => request.ToleranceText,
                "LineOffset" => request.OffsetText,
                "CullByLength" => request.MinimumLengthText,
                _ => request.MaxSegmentLengthText
            };
            string field = request.Operation switch
            {
                "Filter" => "Tolerance",
                "LineOffset" => "BufferWidth",
                "CullByLength" => "MinLength",
                _ => "MaxSegmentLength"
            };
            if (!TryParseDouble(text, request.CultureName, out double value))
            {
                issues.Add(new(field, "Not valid " + Utility.Types.ToFriendlyName(typeof(double)) + " type.", "TypeError"));
            }
            else
            {
                AddLeveled(issues, field, new GreaterThanZero().ValidateValue(value,
                    new ValidationContext(new object()) { MemberName = field }));
            }
        }
        else if (request.Operation == "Smooth")
        {
            CultureInfo culture = GetCulture(request.CultureName);
            if (!int.TryParse(request.MovingAverageWindowText, NumberStyles.Integer, culture, out int window))
                issues.Add(new("MovingAverageWindow", "Not valid " + Utility.Types.ToFriendlyName(typeof(int)) + " type.", "TypeError"));
            else AddRange(issues, "MovingAverageWindow", window, 3, 101);
            if (!int.TryParse(request.MovingAveragePassesText, NumberStyles.Integer, culture, out int passes))
                issues.Add(new("MovingAveragePasses", "Not valid " + Utility.Types.ToFriendlyName(typeof(int)) + " type.", "TypeError"));
            else AddRange(issues, "MovingAveragePasses", passes, 1, 10);
        }
        else if (request.Operation == "MergePolylines")
        {
            if (!TryParseDouble(request.MergeToleranceText, request.CultureName, out double tolerance))
                issues.Add(new("Tolerance", "Not valid " + Utility.Types.ToFriendlyName(typeof(double)) + " type.", "TypeError"));
            else AddDoubleRange(issues, "Tolerance", tolerance, 0, 10000);
            if (request.MergeMode is not ("RejectBranching" or "PreferClosest"))
                issues.Add(new("Mode", "Choose a supported merge mode.", "TypeError"));
        }
        if (request.Operation == "LineOffset" && request.OffsetSides is not ("Both" or "Left" or "Right"))
            issues.Add(new("Sides", "Choose a supported offset side.", "TypeError"));

        if (request.OutputHandler != "Clipboard")
        {
            var context = new ValidationContext(new object()) { MemberName = "Name" };
            AddLeveled(issues, "OutputName", new NotNullAttribute().ValidateValue(request.OutputName, context));
            var minimum = new MinLengthAttribute(1).GetValidationResult(request.OutputName, context);
            if (minimum != ValidationResult.Success)
                issues.Add(new("OutputName", minimum?.ErrorMessage ?? "The output name is too short.", "Error"));
        }
        return issues;
    }

    private static ParsedInputs ParseInputs(GeoprocessingPolylineRequest request)
    {
        CultureInfo culture = GetCulture(request.CultureName);
        double tolerance = 0.05;
        int window = 5;
        int passes = 1;
        double minimumLength = 10;
        double maxSegmentLength = 10;
        double mergeTolerance = 1;
        double offset = 10;
        if (request.Operation == "Filter")
            _ = double.TryParse(request.ToleranceText, NumberStyles.Float | NumberStyles.AllowThousands, culture, out tolerance);
        if (request.Operation == "Smooth")
        {
            _ = int.TryParse(request.MovingAverageWindowText, NumberStyles.Integer, culture, out window);
            _ = int.TryParse(request.MovingAveragePassesText, NumberStyles.Integer, culture, out passes);
        }
        if (request.Operation == "CullByLength")
            _ = double.TryParse(request.MinimumLengthText, NumberStyles.Float | NumberStyles.AllowThousands, culture, out minimumLength);
        if (request.Operation == "DensifyPolylines")
            _ = double.TryParse(request.MaxSegmentLengthText, NumberStyles.Float | NumberStyles.AllowThousands, culture, out maxSegmentLength);
        if (request.Operation == "MergePolylines")
            _ = double.TryParse(request.MergeToleranceText, NumberStyles.Float | NumberStyles.AllowThousands, culture, out mergeTolerance);
        if (request.Operation == "LineOffset")
            _ = double.TryParse(request.OffsetText, NumberStyles.Float | NumberStyles.AllowThousands, culture, out offset);
        MergeMode mergeMode = request.MergeMode == "RejectBranching" ? MergeMode.RejectBranching : MergeMode.PreferClosest;
        OffsetSides offsetSides = request.OffsetSides switch
        {
            "Left" => OffsetSides.Left,
            "Right" => OffsetSides.Right,
            _ => OffsetSides.Both
        };
        return new(tolerance, window, passes, minimumLength, maxSegmentLength, mergeTolerance, mergeMode, offset, offsetSides);
    }

    private static bool TryParseDouble(string? text, string? cultureName, out double value) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
            GetCulture(cultureName), out value) && double.IsFinite(value);

    private static CultureInfo GetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return CultureInfo.GetCultureInfo("en-US");
        try { return CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static void AddRange(List<GeoprocessingValidationIssue> issues, string field, int value, int minimum, int maximum)
    {
        var attribute = new RangeAttribute(minimum, maximum);
        ValidationResult? result = attribute.GetValidationResult(value,
            new ValidationContext(new object()) { MemberName = field });
        if (result != ValidationResult.Success)
            issues.Add(new(field, result?.ErrorMessage ?? "The value is outside the allowed range.", "Error"));
    }

    private static void AddDoubleRange(List<GeoprocessingValidationIssue> issues, string field,
        double value, double minimum, double maximum)
    {
        var attribute = new RangeAttribute(minimum, maximum);
        ValidationResult? result = attribute.GetValidationResult(value,
            new ValidationContext(new object()) { MemberName = field });
        if (result != ValidationResult.Success)
            issues.Add(new(field, result?.ErrorMessage ?? "The value is outside the allowed range.", "Error"));
    }

    private static void AddLeveled(List<GeoprocessingValidationIssue> issues, string field, LeveledValidationResult? result)
    {
        if (result is not null)
            issues.Add(new(field, result.ErrorMessage ?? "The value is invalid.", result.ErrorLevel.ToString()));
    }

    private static Polyline Transform(Polyline source, string operation, ParsedInputs parsed)
    {
        if (operation == "CullByLength") return source.Clone();
        if (operation == "Filter")
        {
            Polyline filtered = source.Clone();
            filtered.Filter(parsed.Tolerance);
            return filtered;
        }
        if (operation == "Smooth") return source.CloneWithMovingAverageSmoothing(parsed.Window, parsed.Passes);
        if (operation == "Reverse")
        {
            Polyline reversed = source.Clone();
            reversed.Reverse();
            return reversed;
        }
        if (operation == "DensifyPolylines")
        {
            Polyline densified = source.Clone();
            densified.Densify(parsed.MaxSegmentLength);
            return densified;
        }
        throw new InvalidOperationException("The operation does not use a one-to-one line transform.");
    }

    private static GeoprocessingPolylineOutput CreateSourceOutput(GeoprocessingPolylineInput source, Polyline line) =>
        new(source.FeatureIdentity, source.PathIdentity, source.SourceFeatureIndex, source.PathIndex,
            ToCoordinates(line), ToOrdinates(line.HasZ ? line.Z : null), ToOrdinates(line.HasM ? line.M : null));

    private static GeoprocessingPolylineOutput CreateGeneratedOutput(Polyline line, int index) =>
        new("geoprocessing-output-feature:" + index.ToString(CultureInfo.InvariantCulture),
            "geoprocessing-output-path:" + index.ToString(CultureInfo.InvariantCulture), index, 0,
            ToCoordinates(line), ToOrdinates(line.HasZ ? line.Z : null), ToOrdinates(line.HasM ? line.M : null));

    private static BackendOperationProgress ProgressFor(int index, int total, string taskName) =>
        new(Fraction: 0.05 + 0.75 * (index + 1) / total, TaskName: taskName);

    private static string OperationProgressName(string operation) => operation switch
    {
        "Filter" => "Filtering polylines",
        "Smooth" => "Smoothing polylines",
        "Reverse" => "Reversing polylines",
        "CullByLength" => "Filtering polylines by length",
        "DensifyPolylines" => "Densifying polylines",
        "MergePolylines" => "Merging polylines",
        "LineOffset" => "Offsetting polylines",
        _ => "Processing polylines"
    };

    internal static double[][] ToCoordinates(Polyline line)
    {
        if (line.Count < 2) throw new InvalidDataException("The source polyline operation produced fewer than two vertices.");
        var result = new double[line.Count][];
        for (int index = 0; index < line.Count; index++)
        {
            Point point = line[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException("The source polyline operation produced a non-finite coordinate.");
            result[index] = [point.X, point.Y];
        }
        return result;
    }

    internal static string[]? ToOrdinates(IList<double>? ordinates) => ordinates is null
        ? null
        : ordinates.Select(value => value.ToString("R", CultureInfo.InvariantCulture)).ToArray();

    private static double[] ParseOrdinates(IReadOnlyList<string> ordinates) => ordinates
        .Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    private static void ValidateOrdinates(string[]? ordinates, int coordinateCount, string dimension)
    {
        if (ordinates is null) return;
        if (ordinates.Length != coordinateCount || ordinates.Any(value =>
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                || double.IsInfinity(parsed)))
            throw new InvalidDataException("The captured " + dimension + " ordinates do not match the selected line coordinates.");
    }

    internal static Polyline ToPolyline(GeoprocessingPolylineInput input) =>
        ToPolyline(input.Coordinates, input.ZValues, input.MValues);

    internal static Polyline ToPolyline(GeoprocessingPolylineOutput output) =>
        ToPolyline(output.Coordinates, output.ZValues, output.MValues);

    private static Polyline ToPolyline(double[][] coordinates, string[]? zValues, string[]? mValues)
    {
        var line = new Polyline(coordinates.Length);
        foreach (double[] coordinate in coordinates) line.Add(new Point(coordinate[0], coordinate[1]));
        if (zValues is not null) line.TrackZ(ParseOrdinates(zValues));
        if (mValues is not null) line.TrackM(ParseOrdinates(mValues));
        return line;
    }

    internal static string ToClipboardText(IReadOnlyList<Polyline> lines) =>
        string.Join(Environment.NewLine, lines.Select(line => line.ToString()));

    private static string OutputTitle(string operation) => operation switch
    {
        "Filter" => "Filtered Polylines",
        "Smooth" => "Smoothed Lines",
        "Reverse" => "Reversed Lines",
        "CullByLength" => "Remaining Polylines",
        "DensifyPolylines" => "Densified Lines",
        "MergePolylines" => "Merged Lines",
        "LineOffset" => "Offset Lines",
        _ => operation
    };

    internal static string WriteShapefile(GeoprocessingPolylineRequest request, string name,
        IReadOnlyList<Polyline> lines, CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        if (!Directory.Exists(projectDirectory)) throw new DirectoryNotFoundException("Open a saved project before writing a geoprocessing output.");
        string exportDirectory = Path.Combine(projectDirectory, "Exports");
        Directory.CreateDirectory(exportDirectory);
        string safeName = SanitizeFileName(name);
        if (safeName.Length == 0) throw new InvalidDataException("Enter a valid output name.");
        string targetBase = UniqueBasePath(exportDirectory, safeName);
        string stagingDirectory = Path.Combine(exportDirectory, ".geoprocessing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        string stagingPath = Path.Combine(stagingDirectory, safeName + ".shp");
        var movedFiles = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Projection? projection = ResolveProjection(request);
            if (projection is null)
                ShapefileIO.Write_GDAL(stagingPath, safeName, lines.ToArray());
            else
                ShapefileIO.Write_GDAL(stagingPath, safeName, lines.ToArray(), projection);
            cancellationToken.ThrowIfCancellationRequested();
            string[] extensions = [".shp", ".shx", ".dbf", ".prj"];
            if (!File.Exists(stagingPath) || !File.Exists(Path.ChangeExtension(stagingPath, ".shx"))
                || !File.Exists(Path.ChangeExtension(stagingPath, ".dbf")))
                throw new InvalidDataException("The retained Shapefile writer did not produce the required source family.");
            foreach (string extension in extensions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string staged = Path.ChangeExtension(stagingPath, extension);
                if (!File.Exists(staged)) continue;
                string target = Path.ChangeExtension(targetBase, extension);
                File.Move(staged, target);
                movedFiles.Add(target);
            }
            return targetBase + ".shp";
        }
        catch
        {
            foreach (string path in movedFiles) TryDelete(path);
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    internal static Projection? ResolveProjection(GeoprocessingPolylineRequest request)
    {
        if ((request.SourceFamily is not null || request.GdbSource is not null) &&
            !string.IsNullOrWhiteSpace(request.TargetProjectionWKT))
            return new WKTProjection(request.TargetProjectionWKT);
        ProjectionType type = ProjectionDefinition.TryParseProjectionType(request.ProjectProjectionType ?? "");
        if (type == ProjectionType.Local) return null;
        if (type == ProjectionType.Unknown) throw new InvalidDataException("The project projection cannot be read for Shapefile output.");
        var definition = new ProjectionDefinition(type, request.ProjectProjectionValue ?? "");
        var result = definition.Evaluate(out Projection? projection, out _);
        if (!result.Result || projection is null)
            throw new InvalidDataException("The project projection could not be written to the Shapefile output.");
        return projection;
    }

    internal static void VerifyTargetProjection(GeoprocessingPolylineRequest request)
    {
        if (request.SourceFamily is null && request.GdbSource is null) return;
        ProjectionType type = ProjectionDefinition.TryParseProjectionType(request.ProjectProjectionType ?? "");
        if (string.IsNullOrWhiteSpace(request.TargetProjectionWKT) &&
            request.GdbSource is not null && type == ProjectionType.Local)
            return;
        if (string.IsNullOrWhiteSpace(request.TargetProjectionWKT))
        {
            string sourceKind = request.GdbSource is null ? "Shapefile" : "File Geodatabase";
            throw new InvalidDataException($"The selected {sourceKind} layer has no captured target projection.");
        }

        if (type == ProjectionType.Local) return;
        if (type == ProjectionType.Unknown)
        {
            if (request.GdbSource is null) return;
            throw new InvalidDataException("The project projection cannot be verified for the selected source layer.");
        }
        var definition = new ProjectionDefinition(type, request.ProjectProjectionValue ?? "");
        var result = definition.Evaluate(out Projection? projectProjection, out _);
        if (!result.Result || projectProjection is null)
            throw new InvalidDataException("The project projection could not be verified for the selected line layer.");
        var capturedProjection = new WKTProjection(request.TargetProjectionWKT);
        if (!capturedProjection.IsEqual(projectProjection))
            throw new InvalidDataException("The selected source layer was loaded in a different project projection.");
    }

    internal static string UniqueBasePath(string directory, string name)
    {
        string candidate = Path.Combine(directory, name);
        for (int suffix = 2; File.Exists(candidate + ".shp") || File.Exists(candidate + ".shx") || File.Exists(candidate + ".dbf"); suffix++)
            candidate = Path.Combine(directory, $"{name} ({suffix})");
        return candidate;
    }

    internal static string SanitizeFileName(string value)
    {
        string name = value.Trim();
        if (name.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(character => !invalid.Contains(character) && character is not '/' and not '\\').ToArray()).Trim();
    }

    internal static bool IsSHA256(string value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static Task? StartControlPump(BackendDuplexControlReader? controls,
        CancellationTokenSource cancellation, CancellationToken stopToken)
    {
        if (controls is null) return null;
        return Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stopToken).ConfigureAwait(false))
                {
                    if (frame.Control.TryGetProperty("type", out JsonElement type) &&
                        string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    {
                        try { cancellation.Cancel(); }
                        catch (ObjectDisposedException) { }
                    }
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
            catch
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                throw;
            }
        }, stopToken);
    }

    internal static string CaptureSourceRevision(GeoprocessingPolylineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GdbSource is not null) return request.SourceRevision;
        if (string.Equals(Path.GetExtension(request.SourcePath), ".gdb", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("A File Geodatabase operation must identify the exact persisted vector sublayer.");
        return ReadRevision(request.SourcePath);
    }

    internal static void VerifySource(GeoprocessingPolylineRequest request, string sourcePath,
        string capturedRevision)
    {
        ArgumentNullException.ThrowIfNull(request);
        GeoprocessingGdbSourceCapture? gdbSource = request.GdbSource;
        if (gdbSource is null)
        {
            VerifySourceFamily(request.SourceFamily, sourcePath, capturedRevision);
            return;
        }

        if (request.SourceFamily is not null || !Path.IsPathFullyQualified(sourcePath) ||
            !Path.IsPathFullyQualified(gdbSource.Map.ProjectPath) ||
            !Path.IsPathFullyQualified(request.ProjectDirectory))
            throw new SourceFileChangedException();

        string canonicalPath = SourceFilePaths.Canonical(Path.GetFullPath(sourcePath));
        if (!PathComparer.Equals(Path.GetExtension(canonicalPath), ".gdb") ||
            !PathComparer.Equals(canonicalPath, SourceFilePaths.Canonical(gdbSource.Node.AssetID ?? "")) ||
            !IsSHA256(capturedRevision) ||
            !capturedRevision.Equals(request.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !capturedRevision.Equals(gdbSource.ExpectedSourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string? mapProjectDirectory = Path.GetDirectoryName(Path.GetFullPath(gdbSource.Map.ProjectPath));
        if (mapProjectDirectory is null || !PathComparer.Equals(
                SourceFilePaths.Canonical(mapProjectDirectory),
                SourceFilePaths.Canonical(Path.GetFullPath(request.ProjectDirectory))))
            throw new SourceFileChangedException();

        GdbMapLayerDescriptor layer = GdbMapSourceService.VerifyVectorLayer(
            gdbSource.Map, gdbSource.Node, gdbSource.ExpectedSourceRevision);
        if (!string.Equals(layer.Name, gdbSource.LayerName, StringComparison.Ordinal) ||
            !string.Equals(layer.VectorType, gdbSource.VectorType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        ValidateGdbInputGeometry(request, layer);
    }

    private static void ValidateGdbInputGeometry(GeoprocessingPolylineRequest request,
        GdbMapLayerDescriptor layer)
    {
        bool line = layer.VectorType is "Line" or "LineCollection";
        bool polygon = layer.VectorType is "Polygon" or "PolygonCollection";
        bool point = layer.VectorType is "Point" or "PointCollection";
        if (request.InputLines is { Length: > 0 } && !line &&
            !(polygon && request.Operation == "SnapToHighGround"))
            throw new InvalidDataException("The exact GDB sublayer does not provide the line geometry selected for this operation.");
        if (request.InputPolygons is { Length: > 0 } && !polygon)
            throw new InvalidDataException("The exact GDB sublayer does not provide the polygon geometry selected for this operation.");
        if (request.InputPoints is { Length: > 0 } && !point)
            throw new InvalidDataException("The exact GDB sublayer does not provide the point geometry selected for this operation.");
    }

    internal static void VerifySourceFamily(MapSourceFamilyCapture? expected, string sourcePath,
        string capturedPrimaryRevision)
    {
        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            if (expected is null) throw new SourceFileChangedException();
            MapShapefileImportService.VerifyExpectedFamily(expected, sourcePath);
            MapSourceFileRevision[] primaryFiles = expected.Files.Where(file =>
                file.Role == "source" && file.Required).ToArray();
            if (primaryFiles.Length != 1 || primaryFiles[0].Revision is not { } primaryRevision ||
                !primaryRevision.Equals(capturedPrimaryRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return;
        }

        if (expected is not null || !(extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Geoprocessing supports loaded GeoJSON, Shapefile and exact File Geodatabase vector layers.");
    }

    internal static void EnsureSourceSnapshot(GeoprocessingPolylineRequest request, string path,
        string revision)
    {
        if (request.GdbSource is null) EnsureSourceRevision(path, revision);
        VerifySource(request, path, revision);
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string ReadRevision(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected map source no longer exists.", path);
        var info = new FileInfo(path);
        if (info.Length > MaximumSourceBytes) throw new InvalidDataException("The selected map source exceeds the supported size.");
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void EnsureSourceRevision(string path, string expected)
    {
        string actual = ReadRevision(path);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
    }

    internal static void DeleteShapefileFamily(string sourcePath)
    {
        string stem = Path.ChangeExtension(sourcePath, null);
        foreach (string extension in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
            TryDelete(stem + extension);
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
