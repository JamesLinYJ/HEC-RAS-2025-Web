using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Geospatial;
using Geospatial.Core.Rasters;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Mapping;
using Ras.Output;
using Utility.Logging;
using Utility.Progress;
using Utility.Validation;

namespace Ras.Backend;

/// <summary>
/// Portable implementations of the retained source geoprocessing operations
/// that operate on real captured map vectors. The algorithms below call the
/// same Geospatial collection operations as the source tools.
/// </summary>
internal static class GeoprocessingSourceOperations
{
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    {
        "BufferPolyline", "PolylineToPolygon", "BufferPolygon", "DensifyPolygons", "PolygonToPolyline",
        "RasterizePolygons", "DistanceToLines", "ProximityHeatmap", "SnapToHighGround",
        "ClipRasterToPolygon", "RasterCutStats", "LaplacianTerrain",
        "ResampleRaster", "RoundRaster", "NoDataLaplacianFill", "FlatAreaDetector"
    };

    private sealed record Parameters(
        double BufferWidth, double MaxSegmentLength, string SmoothingMethod,
        int MovingAverageWindow, int MovingAveragePasses,
        double PolygonBufferDistance, double CellSize, double FillValue, double BackgroundValue,
        int MaxDistanceCells, DistanceAlgorithm DistanceAlgorithm, double SigmaCells,
        double SearchRadius, double SampleSpacing, string SearchMode, string ElevationMode,
        int PointsPerRing, int Iterations, double OutputCellSize, double BoundarySpacing,
        double InteriorGridSpacing, double LaplacianOutputCellSize, int BinCount,
        RoundTo RoundToValue, RasterCellNeighborSet Connectivity, string NoDataOutputMode,
        double MaxSlopePercent, double SimplifyDistance, double MinimumRingArea, Extent ClipExtent);

    internal static GeoprocessingPolylineResponse Execute(GeoprocessingPolylineRequest request,
        Action<BackendOperationProgress>? report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Operations.Contains(request.Operation))
            throw new ArgumentException("Choose a supported geoprocessing tool.");
        if (request.InputMode is not ("All" or "Selected"))
            throw new ArgumentException("Choose all features or the selected features.");
        bool rasterOnly = request.Operation is "ResampleRaster" or "RoundRaster";
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath))
            throw new ArgumentException("A canonical source path is required.");
        if (!rasterOnly && !GeoprocessingToolService.IsSHA256(request.SourceRevision))
            throw new ArgumentException("A captured SHA-256 revision is required for the selected vector source.");
        if (UsesRasterInput(request.Operation) && RasterCaptureCount(request) != 1)
            throw new ArgumentException("Choose exactly one current map raster, Terrain, or selected Result profile.");
        if (rasterOnly && !Path.GetFullPath(RasterSourcePath(request)!).Equals(
                Path.GetFullPath(request.SourcePath), StringComparison.Ordinal))
            throw new ArgumentException("Choose the exact raster source that is attached to the active map.");

        string sourcePath = Path.GetFullPath(request.SourcePath);
        string capturedRevision = rasterOnly ? string.Empty : GeoprocessingToolService.CaptureSourceRevision(request);
        if (!rasterOnly && !capturedRevision.Equals(request.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!rasterOnly)
        {
            GeoprocessingToolService.VerifySource(request, sourcePath, capturedRevision);
            GeoprocessingToolService.VerifyTargetProjection(request);
        }

        GeoprocessingPolylineValidationResponse validation = ValidateInputs(ToValidationRequest(request));
        if (validation.ValidationIssues.Any(IsError))
            return InvalidResponse(request, validation.ValidationIssues);
        Parameters parameters = Parse(request);

        bool lineInput = request.Operation is "BufferPolyline" or "PolylineToPolygon"
            or "DistanceToLines" or "ProximityHeatmap" or "SnapToHighGround";
        bool pointInput = request.Operation is "NoDataLaplacianFill" or "FlatAreaDetector";
        bool polygonInput = !lineInput && !pointInput && !rasterOnly;
        GeoprocessingPolylineInput[] lines = request.InputLines ?? [];
        GeoprocessingPolygonInput[] polygons = request.InputPolygons ?? [];
        GeoprocessingPointInput[] points = request.InputPoints ?? [];
        if ((lineInput && lines.Length == 0) || (polygonInput && polygons.Length == 0) ||
            (pointInput && points.Length == 0) ||
            (pointInput && request.Operation == "FlatAreaDetector" && polygons.Length != 0))
            throw new InvalidDataException(lineInput ? "Input polylines cannot be null or empty."
                : pointInput ? "Seed points cannot be null or empty."
                : "Input polygons cannot be null or empty.");
        if (lines.Length > 100_000 || polygons.Length > 100_000 || points.Length > 100_000)
            throw new InvalidDataException("The captured feature selection exceeds the supported count.");
        ValidateInputsIdentity(lines, polygons, points, lineInput, pointInput);
        foreach (GeoprocessingPolylineInput input in lines) ValidateLine(input);
        foreach (GeoprocessingPolygonInput input in polygons) ValidatePolygon(input);
        foreach (GeoprocessingPointInput input in points) ValidatePoint(input);

        var lineOutputs = new List<GeoprocessingPolylineOutput>();
        var polygonOutputs = new List<GeoprocessingPolygonOutput>();
        string outputKind;
        string outputTitle;
        bool producesRaster = request.Operation is "RasterizePolygons" or "DistanceToLines"
            or "ProximityHeatmap" or "ClipRasterToPolygon" or "LaplacianTerrain"
            or "ResampleRaster" or "RoundRaster" or "NoDataLaplacianFill";
        bool producesText = request.Operation == "RasterCutStats";
        string? outputPath = null;
        string? clipboardText = null;
        string? textOutput = null;
        int? rasterWidth = null;
        int? rasterHeight = null;
        double[]? rasterExtent = null;

        report?.Invoke(new(Fraction: 0.02, TaskName: "Checking captured map features"));
        switch (request.Operation)
        {
            case "BufferPolyline":
                outputKind = "Polygon"; outputTitle = "Buffered Polygons";
                for (int index = 0; index < lines.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Polyline line = GeoprocessingToolService.ToPolyline(lines[index]);
                    Polygon polygon = line.GetOffsetPolygon(parameters.BufferWidth * 2,
                        out Polyline left, out Polyline right);
                    polygon = parameters.SmoothingMethod switch
                    {
                        "MovingAverage" => OffsetPolylineFactory.BufferedPolygonEdgesToPolygon(
                            left.CloneWithMovingAverageSmoothing(parameters.MovingAverageWindow, parameters.MovingAveragePasses),
                            right.CloneWithMovingAverageSmoothing(parameters.MovingAverageWindow, parameters.MovingAveragePasses)),
                        "Standard" => polygon,
                        _ => throw new InvalidDataException("Choose a supported buffer smoothing method.")
                    };
                    if (polygon is not null && polygon.PointCount >= 3)
                        polygonOutputs.Add(CreatePolygonOutput(polygon, lines[index], polygonOutputs.Count));
                    Report(report, index, lines.Length, "Buffering polylines");
                }
                break;
            case "PolylineToPolygon":
                outputKind = "Polygon"; outputTitle = "Polygons";
                for (int index = 0; index < lines.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Polyline line = GeoprocessingToolService.ToPolyline(lines[index]);
                    if (line.Count >= 3)
                    {
                        Polygon polygon = Polygon.CreateWithBuffer(line);
                        polygonOutputs.Add(CreatePolygonOutput(polygon, lines[index], polygonOutputs.Count));
                    }
                    Report(report, index, lines.Length, "Converting polylines to polygons");
                }
                break;
            case "BufferPolygon":
                outputKind = "Polygon"; outputTitle = "Buffered Polygons";
                for (int index = 0; index < polygons.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ComplexPolygon source = ToComplexPolygon(polygons[index]);
                    var bufferedRings = new List<Polygon>();
                    for (int ringIndex = 0; ringIndex < source.Rings.Count; ringIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // A positive buffer expands the outer ring and contracts
                        // holes. The source polygon buffer uses Clipper's signed
                        // offset distance for each simple ring.
                        double distance = ringIndex == 0
                            ? parameters.PolygonBufferDistance : -parameters.PolygonBufferDistance;
                        bufferedRings.AddRange(source.Rings[ringIndex].Buffer(distance));
                    }
                    if (bufferedRings.Count > 0)
                    {
                        if (!ComplexPolygon.TryCreateComplexPolygons(bufferedRings, out List<ComplexPolygon> results))
                            throw new InvalidDataException("The buffered polygon rings could not be assembled into valid polygon features.");
                        foreach (ComplexPolygon result in results)
                            polygonOutputs.Add(CreatePolygonOutput(result, polygons[index], polygonOutputs.Count));
                    }
                    Report(report, index, polygons.Length, "Buffering polygons");
                }
                break;
            case "DensifyPolygons":
                outputKind = "Polygon"; outputTitle = "Densified Polygons";
                for (int index = 0; index < polygons.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ComplexPolygon source = ToComplexPolygon(polygons[index]);
                    foreach (Polygon ring in source.Rings) ring.Densify(parameters.MaxSegmentLength);
                    polygonOutputs.Add(CreatePolygonOutput(source, polygons[index], index));
                    Report(report, index, polygons.Length, "Densifying polygons");
                }
                break;
            case "PolygonToPolyline":
                outputKind = "Polyline"; outputTitle = "Polylines";
                for (int index = 0; index < polygons.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ComplexPolygon polygon = ToComplexPolygon(polygons[index]);
                    foreach (Polygon ring in polygon.Rings)
                        lineOutputs.Add(CreateGeneratedLineOutput(new Polyline(ring), lineOutputs.Count));
                    Report(report, index, polygons.Length, "Converting polygon boundaries");
                }
                break;
            case "RasterizePolygons":
                outputKind = "Raster"; outputTitle = "Rasterized Polygons";
                (outputPath, rasterWidth, rasterHeight, rasterExtent) = RasterizePolygons(request, polygons,
                    parameters, cancellationToken, report);
                break;
            case "DistanceToLines":
            case "ProximityHeatmap":
                outputKind = "Raster";
                outputTitle = request.Operation == "DistanceToLines" ? "Distance To Lines" : "Proximity Heatmap";
                (outputPath, rasterWidth, rasterHeight, rasterExtent) = DistanceRaster(request, lines,
                    parameters, cancellationToken, report);
                break;
            case "SnapToHighGround":
                outputKind = "Polyline"; outputTitle = "Snapped Lines";
                using (RasterContext raster = OpenRaster(request))
                {
                    for (int index = 0; index < lines.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Polyline line = GeoprocessingToolService.ToPolyline(lines[index]);
                        Polyline snapped = SnapToHighGround(line, raster.Raster, parameters, cancellationToken);
                        lineOutputs.Add(CreateSourceLineOutput(lines[index], snapped));
                        Report(report, index, lines.Length, "Snapping lines to the elevation raster");
                    }
                    raster.EnsureUnchanged();
                }
                break;
            case "ClipRasterToPolygon":
                outputKind = "Raster"; outputTitle = "Clipped Raster";
                using (RasterContext raster = OpenRaster(request))
                {
                    (outputPath, rasterWidth, rasterHeight, rasterExtent) = ClipRaster(request, polygons,
                        parameters, raster.Raster, ProjectProjection(request.ProjectXml!), cancellationToken, report);
                    raster.EnsureUnchanged();
                }
                break;
            case "RasterCutStats":
                outputKind = "Text"; outputTitle = "Raster Cut Statistics";
                using (RasterContext raster = OpenRaster(request))
                {
                    textOutput = RasterCutStats(polygons, parameters.BinCount, raster.Raster,
                        cancellationToken, report, GetCulture(request.CultureName));
                    raster.EnsureUnchanged();
                }
                break;
            case "LaplacianTerrain":
                outputKind = "Raster"; outputTitle = "Interpolated Terrain";
                using (RasterContext raster = OpenRaster(request))
                {
                    (outputPath, rasterWidth, rasterHeight, rasterExtent) = LaplacianTerrain(request, polygons,
                        parameters, raster.Raster, ProjectProjection(request.ProjectXml!), cancellationToken, report);
                    raster.EnsureUnchanged();
                }
                break;
            case "ResampleRaster":
                outputKind = "Raster"; outputTitle = "Resampled Raster";
                using (RasterContext raster = OpenRaster(request))
                {
                    IRaster<float> output = raster.Raster.AsRaster(parameters.CellSize);
                    RasterDefinition definition = CheckedDefinition(output.RasterDefinition);
                    (outputPath, rasterWidth, rasterHeight, rasterExtent) = WriteDerivedRaster(
                        request, output, definition, ProjectProjection(request.ProjectXml!), cancellationToken, report);
                    raster.EnsureUnchanged();
                }
                break;
            case "RoundRaster":
                outputKind = "Raster"; outputTitle = "Rounded Raster";
                using (RasterContext raster = OpenRaster(request))
                {
                    IRaster<float> output = raster.Raster.AsRaster(parameters.CellSize)
                        .WithValueTransformer(parameters.RoundToValue);
                    RasterDefinition definition = CheckedDefinition(output.RasterDefinition);
                    (outputPath, rasterWidth, rasterHeight, rasterExtent) = WriteDerivedRaster(
                        request, output, definition, ProjectProjection(request.ProjectXml!), cancellationToken, report);
                    raster.EnsureUnchanged();
                }
                break;
            case "NoDataLaplacianFill":
                outputKind = "Raster"; outputTitle = "Filled Raster";
                using (RasterContext raster = OpenRaster(request))
                {
                    (outputPath, rasterWidth, rasterHeight, rasterExtent) = NoDataLaplacianFill(
                        request, points.Select(ToPoint).ToArray(), parameters, raster.Raster,
                        ProjectProjection(request.ProjectXml!), cancellationToken, report);
                    raster.EnsureUnchanged();
                }
                break;
            case "FlatAreaDetector":
                outputKind = "Polygon"; outputTitle = "Detected Flat Areas";
                using (RasterContext raster = OpenRaster(request))
                {
                    var options = new FlatAreaDetectorOptions
                    {
                        CellSize = parameters.CellSize,
                        MaxSlope = parameters.MaxSlopePercent / 100.0,
                        Connectivity = parameters.Connectivity,
                        SimplifyDistance = parameters.SimplifyDistance,
                        MinimumRingArea = parameters.MinimumRingArea,
                        ClipExtent = parameters.ClipExtent
                    };
                    ComplexPolygonCollection detected = FlatAreaDetector.Detect(raster.Raster,
                        points.Select(ToPoint).ToArray(), options, diagnostics: null,
                        cancellationToken: cancellationToken);
                    foreach (ComplexPolygon polygon in detected)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        polygonOutputs.Add(CreatePolygonOutput(polygon, polygonOutputs.Count));
                    }
                    raster.EnsureUnchanged();
                }
                break;
            default:
                throw new ArgumentException("Choose a supported geoprocessing tool.");
        }

        if (!rasterOnly)
            GeoprocessingToolService.EnsureSourceSnapshot(request, sourcePath, capturedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        string? outputFormat = null;
        if (producesText)
        {
            outputFormat = request.OutputHandler switch
            {
                "Clipboard" => "text/plain",
                "CSV File" => "text/csv",
                "Text File" => "text/plain",
                _ => throw new ArgumentException("Choose a supported text output handler.")
            };
            if (request.OutputHandler is "CSV File" or "Text File")
                outputPath = WriteTextOutput(request, textOutput ?? "", request.OutputHandler == "CSV File" ? ".csv" : ".txt",
                    cancellationToken);
        }
        else if (producesRaster)
        {
            if (request.OutputHandler is not ("Tiff" or "Map Layer"))
                throw new ArgumentException("Choose Tiff or Map Layer output.");
        }
        else
        {
            if (request.OutputHandler is not ("Clipboard" or "Shapefile" or "Map Layer"))
                throw new ArgumentException("Choose Clipboard, Shapefile, or Map Layer output.");
            if (request.OutputHandler is "Shapefile" or "Map Layer")
            {
                outputPath ??= outputKind == "Polyline"
                    ? GeoprocessingToolService.WriteShapefile(request, request.OutputName ?? "",
                        lineOutputs.Select(GeoprocessingToolService.ToPolyline).ToArray(), cancellationToken)
                    : WritePolygonShapefile(request, request.OutputName ?? "",
                        polygonOutputs, cancellationToken);
                outputFormat = "application/x-esri-shapefile";
            }
            else
            {
                outputFormat = outputKind == "Polyline" ? "application/x-hecras-polylines+json" : "application/geo+json";
                clipboardText = outputKind == "Polyline"
                    ? GeoprocessingToolService.ToClipboardText(lineOutputs.Select(GeoprocessingToolService.ToPolyline).ToArray())
                    : ToGeoJson(polygonOutputs);
            }
        }
        if (!rasterOnly)
            GeoprocessingToolService.EnsureSourceSnapshot(request, sourcePath, capturedRevision);
        report?.Invoke(new(Fraction: 1, TaskName: "Finishing geoprocessing output"));
        string[] capturedLineIDs = lines.Select(line => line.PathIdentity).ToArray();
        string[] capturedPolygonIDs = polygons.Select(polygon => polygon.PathIdentity).ToArray();
        string[] capturedPointIDs = points.Select(point => point.PathIdentity).ToArray();
        return new(true, request.Operation, outputTitle, lines.Length, lineOutputs.Count,
            outputPath, clipboardText, lineOutputs.ToArray(), validation.ValidationIssues, capturedLineIDs,
            outputKind, polygonOutputs.ToArray(), polygons.Length, polygonOutputs.Count, textOutput,
            outputFormat, capturedPolygonIDs, rasterWidth, rasterHeight, rasterExtent,
            points.Length, capturedPointIDs);
    }

    internal static GeoprocessingPolylineValidationResponse ValidateInputs(
        GeoprocessingPolylineValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Operations.Contains(request.Operation))
            throw new ArgumentException("Choose a supported geoprocessing tool.");
        var issues = new List<GeoprocessingValidationIssue>();
        CultureInfo culture = GetCulture(request.CultureName);
        switch (request.Operation)
        {
            case "BufferPolyline":
                ValidateDouble(request.BufferWidthText, request.CultureName, "BufferWidth", issues, positive: true);
                if (request.BufferSmoothingMethod is not ("Standard" or "MovingAverage"))
                    issues.Add(new("SmoothingMethod", "Choose a supported buffer smoothing method.", "TypeError"));
                if (request.BufferSmoothingMethod == "MovingAverage")
                {
                    ValidateIntRange(request.BufferMovingAverageWindowText, culture, "MovingAverageWindow", 3, 101, issues);
                    ValidateIntRange(request.BufferMovingAveragePassesText, culture, "MovingAveragePasses", 1, 10, issues);
                }
                break;
            case "PolylineToPolygon":
            case "PolygonToPolyline":
                break;
            case "BufferPolygon":
                ValidateDouble(request.BufferPolygonDistanceText, request.CultureName, "BufferDistance", issues, finiteOnly: true);
                break;
            case "DensifyPolygons":
                ValidateDouble(request.MaxSegmentLengthText, request.CultureName, "MaxSegmentLength", issues, positive: true);
                break;
            case "RasterizePolygons":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                ValidateDouble(request.FillValueText, request.CultureName, "FillValue", issues, finiteOnly: true);
                ValidateDouble(request.BackgroundValueText, request.CultureName, "BackgroundValue", issues, finiteOnly: true);
                break;
            case "DistanceToLines":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                ValidateIntRange(request.MaxDistanceCellsText, culture, "MaxDistanceCells", 1, 23000, issues);
                ValidateDistanceAlgorithm(request.DistanceAlgorithm, issues);
                break;
            case "ProximityHeatmap":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                ValidateIntRange(request.SigmaCellsText, culture, "SigmaCells", 1, int.MaxValue, issues);
                ValidateIntRange(request.MaxDistanceCellsText, culture, "MaxDistanceCells", 1, 23000, issues);
                ValidateDistanceAlgorithm(request.DistanceAlgorithm, issues);
                break;
            case "SnapToHighGround":
                ValidateDouble(request.SearchRadiusText, request.CultureName, "SearchRadius", issues, positive: true);
                ValidateDouble(request.SampleSpacingText, request.CultureName, "SampleSpacing", issues, positive: true);
                if (request.SearchMode is not ("Perpendicular" or "Radial"))
                    issues.Add(new("SearchMode", "Choose a supported high-ground search mode.", "TypeError"));
                if (request.ElevationMode is not ("Highest" or "Lowest"))
                    issues.Add(new("ElevationMode", "Choose Highest or Lowest elevation.", "TypeError"));
                if (request.SearchMode == "Radial")
                    ValidateIntRange(request.PointsPerRingText, culture, "PointsPerRing", 1, 4096, issues);
                ValidateIntRange(request.IterationsText, culture, "Iterations", 1, 100, issues);
                break;
            case "ClipRasterToPolygon":
                ValidateDouble(request.OutputCellSizeText, request.CultureName, "OutputCellSize", issues, finiteOnly: true);
                break;
            case "RasterCutStats":
                if (!int.TryParse(request.BinCountText, NumberStyles.Integer, culture, out int binCount))
                    issues.Add(new("BinCount", "Not valid " + Utility.Types.ToFriendlyName(typeof(int)) + " type.", "TypeError"));
                else if (binCount > 512)
                    issues.Add(new("BinCount", "The histogram bin count exceeds the supported limit.", "Error"));
                break;
            case "LaplacianTerrain":
                if (request.UseAdvancedSizing)
                {
                    ValidateDouble(request.BoundarySpacingText, request.CultureName, "BoundarySpacing", issues, positive: true);
                    ValidateDouble(request.InteriorGridSpacingText, request.CultureName, "InteriorGridSpacing", issues, positive: true);
                    ValidateDouble(request.OutputCellSizeText, request.CultureName, "OutputCellSize", issues, positive: true);
                }
                else
                    ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                break;
            case "ResampleRaster":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                break;
            case "RoundRaster":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                if (!Enum.TryParse(request.RoundTo, ignoreCase: false, out RoundTo _))
                    issues.Add(new("RoundTo", "Choose a supported raster rounding precision.", "TypeError"));
                break;
            case "NoDataLaplacianFill":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                ValidateDouble(request.BoundarySpacingText, request.CultureName, "BoundarySpacing", issues, positive: true);
                ValidateDouble(request.InteriorGridSpacingText, request.CultureName, "InteriorGridSpacing", issues, positive: true);
                if (request.Connectivity is not ("FourSides" or "EightCorners"))
                    issues.Add(new("Connectivity", "Choose Four Sides or Eight Corners.", "TypeError"));
                if (request.NoDataOutputMode is not ("Patch" or "Full"))
                    issues.Add(new("OutputMode", "Choose Patch or Full output.", "TypeError"));
                break;
            case "FlatAreaDetector":
                ValidateDouble(request.CellSizeText, request.CultureName, "CellSize", issues, positive: true);
                ValidateDouble(request.MaxSlopePercentText, request.CultureName, "MaxSlopePercent", issues, positive: true);
                ValidateDouble(request.SimplifyDistanceText, request.CultureName, "SimplifyDistance", issues, finiteOnly: true);
                ValidateDouble(request.MinimumRingAreaText, request.CultureName, "MinimumRingArea", issues, finiteOnly: true);
                if (request.Connectivity is not ("FourSides" or "EightCorners"))
                    issues.Add(new("Connectivity", "Choose Four Sides or Eight Corners.", "TypeError"));
                if (request.ClipExtent is { } extent && (extent.Length != 4 || extent.Any(value => !double.IsFinite(value)
                    ) || extent[0] >= extent[2] || extent[1] >= extent[3]))
                    issues.Add(new("ClipExtent", "The optional clip extent must be finite and non-empty.", "Error"));
                break;
        }

        bool raster = request.Operation is "RasterizePolygons" or "DistanceToLines" or "ProximityHeatmap"
            or "ClipRasterToPolygon" or "LaplacianTerrain" or "ResampleRaster" or "RoundRaster"
            or "NoDataLaplacianFill";
        bool text = request.Operation == "RasterCutStats";
        string[] handlers = raster ? ["Tiff", "Map Layer"] : text ? ["Clipboard", "CSV File", "Text File"]
            : ["Clipboard", "Shapefile", "Map Layer"];
        if (!handlers.Contains(request.OutputHandler, StringComparer.Ordinal))
            issues.Add(new("OutputHandler", "Choose an available output handler.", "TypeError"));
        bool writesNamedFile = request.OutputHandler is "Shapefile" or "Map Layer" or "Tiff" or "CSV File" or "Text File";
        if (writesNamedFile)
        {
            var context = new ValidationContext(new object()) { MemberName = "Name" };
            AddLeveled(issues, "OutputName", new NotNullAttribute().ValidateValue(request.OutputName, context));
            ValidationResult? result = new MinLengthAttribute(1).GetValidationResult(request.OutputName, context);
            if (result != ValidationResult.Success)
                issues.Add(new("OutputName", result?.ErrorMessage ?? "The output name is too short.", "Error"));
        }
        return new(issues.ToArray());
    }

    private static GeoprocessingPolylineValidationRequest ToValidationRequest(GeoprocessingPolylineRequest request) => new(
        request.Operation, request.CultureName, request.ToleranceText, request.MovingAverageWindowText,
        request.MovingAveragePassesText, request.OutputHandler, request.OutputName, request.MinimumLengthText,
        request.MaxSegmentLengthText, request.MergeToleranceText, request.MergeMode, request.OffsetText,
        request.OffsetSides, request.IncludeEndpoints, request.BufferWidthText, request.BufferSmoothingMethod,
        request.BufferMovingAverageWindowText, request.BufferMovingAveragePassesText, request.BufferPolygonDistanceText,
        request.CellSizeText, request.FillValueText, request.BackgroundValueText, request.MaxDistanceCellsText,
        request.DistanceAlgorithm, request.SigmaCellsText, request.SearchRadiusText, request.SampleSpacingText,
        request.SearchMode, request.ElevationMode, request.PointsPerRingText, request.IterationsText,
        request.OutputCellSizeText, request.BinCountText, request.UseAdvancedSizing,
        request.BoundarySpacingText, request.InteriorGridSpacingText, request.RoundTo,
        request.Connectivity, request.NoDataOutputMode, request.MaxSlopePercentText,
        request.SimplifyDistanceText, request.MinimumRingAreaText, request.ClipExtent);

    private static GeoprocessingPolylineResponse InvalidResponse(GeoprocessingPolylineRequest request,
        GeoprocessingValidationIssue[] issues) => new(false, request.Operation, request.Operation,
        request.InputLines?.Length ?? 0, 0, null, null, [], issues,
        request.InputLines?.Select(line => line.PathIdentity).ToArray() ?? [],
        "Polyline", [], request.InputPolygons?.Length ?? 0, 0, null, null,
        request.InputPolygons?.Select(polygon => polygon.PathIdentity).ToArray() ?? [],
        null, null, null, request.InputPoints?.Length ?? 0,
        request.InputPoints?.Select(point => point.PathIdentity).ToArray() ?? []);

    private static Parameters Parse(GeoprocessingPolylineRequest request)
    {
        CultureInfo culture = GetCulture(request.CultureName);
        double cellSize = ParseDouble(request.CellSizeText, culture, 10);
        double boundarySpacing = request.Operation == "NoDataLaplacianFill"
            ? ParseDouble(request.BoundarySpacingText, culture, 10)
            : request.UseAdvancedSizing ? ParseDouble(request.BoundarySpacingText, culture, 10) : cellSize;
        double interiorSpacing = request.Operation == "NoDataLaplacianFill"
            ? ParseDouble(request.InteriorGridSpacingText, culture, 20)
            : request.UseAdvancedSizing ? ParseDouble(request.InteriorGridSpacingText, culture, 10) : cellSize;
        double laplacianOutputSize = request.UseAdvancedSizing
            ? ParseDouble(request.OutputCellSizeText, culture, 10) : cellSize;
        int parsedBinCount = ParseInt(request.BinCountText, culture, 25);
        return new(
            ParseDouble(request.BufferWidthText, culture, 10),
            request.Operation == "DensifyPolygons"
                ? ParseDouble(request.MaxSegmentLengthText, culture, 10) : 10,
            request.BufferSmoothingMethod,
            ParseInt(request.BufferMovingAverageWindowText, culture, 5),
            ParseInt(request.BufferMovingAveragePassesText, culture, 1),
            ParseDouble(request.BufferPolygonDistanceText, culture, 10),
            ParseDouble(request.CellSizeText, culture, 10), ParseDouble(request.FillValueText, culture, 1),
            ParseDouble(request.BackgroundValueText, culture, 0),
            ParseInt(request.MaxDistanceCellsText, culture, 100),
            request.DistanceAlgorithm == "SpatialIndex" ? DistanceAlgorithm.SpatialIndex : DistanceAlgorithm.EDT,
            ParseDouble(request.SigmaCellsText, culture, 10), ParseDouble(request.SearchRadiusText, culture, 50),
            ParseDouble(request.SampleSpacingText, culture, 3), request.SearchMode, request.ElevationMode,
            ParseInt(request.PointsPerRingText, culture, 16), ParseInt(request.IterationsText, culture, 1),
            ParseDouble(request.OutputCellSizeText, culture, 0),
            boundarySpacing, interiorSpacing, laplacianOutputSize,
            parsedBinCount < 1 ? 25 : parsedBinCount,
            Enum.Parse<RoundTo>(request.RoundTo),
            Enum.Parse<RasterCellNeighborSet>(request.Connectivity), request.NoDataOutputMode,
            ParseDouble(request.MaxSlopePercentText, culture, 5),
            ParseDouble(request.SimplifyDistanceText, culture, 0),
            ParseDouble(request.MinimumRingAreaText, culture, 0),
            request.ClipExtent is { Length: 4 } extent
                ? new Extent(extent[0], extent[2], extent[1], extent[3]) : Extent.NullExtent);
    }

    private const long MaximumOutputCells = 100_000_000;
    private const int MaximumGeneratedSearchSamples = 1_000_000;

    private sealed class RasterContext : IDisposable
    {
        public IResample<float> Raster { get; }
        public Projection? SourceProjection { get; }
        public string VisualizationRevision { get; }
        public string SourceRevision { get; }
        private readonly IDisposable? _cleanup;
        private readonly Action verifyUnchanged;

        public RasterContext(IResample<float> raster, Projection? sourceProjection,
            string visualizationRevision, string sourceRevision, IDisposable? cleanup,
            Action? verifyUnchanged = null)
        {
            Raster = raster;
            SourceProjection = sourceProjection;
            VisualizationRevision = visualizationRevision;
            SourceRevision = sourceRevision;
            _cleanup = cleanup;
            this.verifyUnchanged = verifyUnchanged ?? (() => { });
        }

        public void EnsureUnchanged() => verifyUnchanged();
        public void Dispose() => _cleanup?.Dispose();
    }

    private static (string Path, int Width, int Height, double[] Extent) RasterizePolygons(
        GeoprocessingPolylineRequest request, GeoprocessingPolygonInput[] inputs, Parameters parameters,
        CancellationToken cancellationToken, Action<BackendOperationProgress>? report)
    {
        List<ComplexPolygon> polygons = inputs.Select(ToComplexPolygon).ToList();
        Extent extent = UnionPolygonExtent(polygons);
        if (!extent.IsValid()) throw new InvalidDataException("Could not determine valid extent from input polygons.");
        RasterDefinition definition = CheckedDefinition(new RasterDefinition(extent, parameters.CellSize).ExtendRaster(2));
        var values = new float[definition.CellCount];
        Array.Fill(values, (float)parameters.BackgroundValue);
        var polygonRasterizer = new PolygonRasterizer(definition);
        foreach (ComplexPolygon polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            polygonRasterizer.Rasterize(polygon, values, (index, output) =>
            {
                if ((uint)index < (uint)output.Length) output[index] = (float)parameters.FillValue;
            });
        }
        var raster = new RasterData<float>(definition, values);
        string path = WriteRasterOutput(request, raster, definition,
            GeoprocessingToolService.ResolveProjection(request), cancellationToken, report);
        return (path, definition.Cols, definition.Rows,
            [definition.MinX, definition.MinY, definition.MaxX, definition.MaxY]);
    }

    private static (string Path, int Width, int Height, double[] Extent) DistanceRaster(
        GeoprocessingPolylineRequest request, GeoprocessingPolylineInput[] inputs, Parameters parameters,
        CancellationToken cancellationToken, Action<BackendOperationProgress>? report)
    {
        List<Polyline> lines = inputs.Select(GeoprocessingToolService.ToPolyline).ToList();
        Extent extent = UnionLineExtent(lines);
        if (!extent.IsValid()) throw new InvalidDataException("Could not determine valid extent from input polylines.");
        int halo = request.Operation == "DistanceToLines" ? parameters.MaxDistanceCells : 2;
        RasterDefinition definition = CheckedDefinition(new RasterDefinition(extent, parameters.CellSize).ExtendRaster(halo));
        var distance = new DistanceToPolylinesRaster(definition, lines, parameters.MaxDistanceCells,
            parameters.DistanceAlgorithm);
        IResample<float> output = distance;
        if (request.Operation == "ProximityHeatmap")
        {
            double sigma = parameters.SigmaCells * parameters.CellSize;
            float maxDistance = (float)(parameters.MaxDistanceCells * parameters.CellSize);
            output = distance.WithValueTransformer(new GaussianDecayTransformer(sigma, maxDistance));
        }
        string path = WriteRasterOutput(request, output, definition,
            GeoprocessingToolService.ResolveProjection(request), cancellationToken, report);
        return (path, definition.Cols, definition.Rows,
            [definition.MinX, definition.MinY, definition.MaxX, definition.MaxY]);
    }

    private static (string Path, int Width, int Height, double[] Extent) WriteDerivedRaster(
        GeoprocessingPolylineRequest request, IResample<float> raster, RasterDefinition definition,
        Projection? projection, CancellationToken cancellationToken, Action<BackendOperationProgress>? report)
    {
        RasterDefinition checkedDefinition = CheckedDefinition(definition);
        string path = WriteRasterOutput(request, raster, checkedDefinition, projection, cancellationToken, report);
        return (path, checkedDefinition.Cols, checkedDefinition.Rows,
            [checkedDefinition.MinX, checkedDefinition.MinY, checkedDefinition.MaxX, checkedDefinition.MaxY]);
    }

    private static (string Path, int Width, int Height, double[] Extent) NoDataLaplacianFill(
        GeoprocessingPolylineRequest request, IReadOnlyList<Point> seedPoints, Parameters parameters,
        IResample<float> source, Projection? projection, CancellationToken cancellationToken,
        Action<BackendOperationProgress>? report)
    {
        if (seedPoints.Count == 0) throw new ArgumentException("Seed points cannot be null or empty.");
        RasterDefinition definition = CheckedDefinition(source.AsRaster(parameters.CellSize).RasterDefinition);
        int rows = definition.Rows, columns = definition.Cols;
        float[] sampled = new float[definition.CellCount];
        source.Resample(new ResampleArgs(definition, InterpolationMode.Linear), sampled);
        float noData = source.NoDataValue ?? float.MinValue;
        float[] output = parameters.NoDataOutputMode == "Patch"
            ? Enumerable.Repeat(noData, definition.CellCount).ToArray() : (float[])sampled.Clone();
        var filledMask = new BitArray(definition.CellCount);
        int minRow = int.MaxValue, maxRow = int.MinValue, minColumn = int.MaxValue, maxColumn = int.MinValue;
        bool anyFilled = false;
        int[] neighborBuffer = new int[8];
        bool includeDiagonals = parameters.Connectivity == RasterCellNeighborSet.EightCorners;

        Memory<int> Neighbors(int index)
        {
            int row = index / columns, column = index % columns, length = 0;
            if (column > 0) neighborBuffer[length++] = index - 1;
            if (column < columns - 1) neighborBuffer[length++] = index + 1;
            if (row > 0) neighborBuffer[length++] = index - columns;
            if (row < rows - 1) neighborBuffer[length++] = index + columns;
            if (includeDiagonals)
            {
                if (column > 0 && row > 0) neighborBuffer[length++] = index - columns - 1;
                if (column < columns - 1 && row > 0) neighborBuffer[length++] = index - columns + 1;
                if (column > 0 && row < rows - 1) neighborBuffer[length++] = index + columns - 1;
                if (column < columns - 1 && row < rows - 1) neighborBuffer[length++] = index + columns + 1;
            }
            return neighborBuffer.AsMemory(0, length);
        }
        bool IsNoData(float value) => value == noData || float.IsNaN(value) || float.IsInfinity(value);
        bool CanReachNoData(int _, int target) => IsNoData(sampled[target]);

        for (int seedIndex = 0; seedIndex < seedPoints.Count; seedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Point seed = seedPoints[seedIndex];
            if (!definition.Extent.Contains(seed)) continue;
            Pixel pixel = definition.CellIndex2D(seed);
            if (!definition.InBounds(pixel)) continue;
            int seedCell = pixel.To1D(columns);
            if (filledMask[seedCell] || !IsNoData(sampled[seedCell])) continue;

            var regionMask = new BitArray(definition.CellCount);
            new FloodFillHelper(Neighbors, CanReachNoData).Start([seedCell], regionMask, emitSeeds: true);
            for (int index = 0; index < regionMask.Length; index++)
                if (regionMask[index]) filledMask[index] = true;

            var boundaryCells = new HashSet<int>();
            for (int index = 0; index < regionMask.Length; index++)
            {
                if ((index & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!regionMask[index]) continue;
                foreach (int neighbor in Neighbors(index).Span)
                    if (!regionMask[neighbor] && !IsNoData(sampled[neighbor])) boundaryCells.Add(neighbor);
            }
            if (boundaryCells.Count < 3) continue;

            float[] maskValues = new float[regionMask.Length];
            for (int index = 0; index < regionMask.Length; index++)
                maskValues[index] = regionMask[index] ? 1f : 0f;
            ContourShapeOperations.GetContourShapes(maskValues, definition, 0.5f,
                out ComplexPolygonCollection boundaries, out _);
            foreach (ComplexPolygon boundary in boundaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Polygon? polygon = boundary.OuterRing?.Polygon;
                if (polygon is null || polygon.PointCount < 3) continue;
                (Tin Tin, double[] Values)? solution = ProcessNoDataPolygon(polygon, sampled,
                    definition, noData, boundaryCells, parameters);
                if (solution is { } result)
                    FillNoDataFromTin(result.Tin, result.Values, regionMask, definition, output,
                        ref minRow, ref maxRow, ref minColumn, ref maxColumn, ref anyFilled,
                        cancellationToken);
            }
            report?.Invoke(new(Fraction: (double)(seedIndex + 1) / seedPoints.Count,
                TaskName: "Filling NoData regions"));
        }

        if (parameters.NoDataOutputMode == "Patch" && anyFilled)
        {
            minRow = Math.Max(0, minRow - 1); maxRow = Math.Min(rows - 1, maxRow + 1);
            minColumn = Math.Max(0, minColumn - 1); maxColumn = Math.Min(columns - 1, maxColumn + 1);
            int outputRows = maxRow - minRow + 1, outputColumns = maxColumn - minColumn + 1;
            Point upperLeft = new(definition.MinX + minColumn * definition.CellSize,
                definition.MaxY - minRow * definition.CellSize);
            RasterDefinition patchDefinition = CheckedDefinition(new RasterDefinition(
                upperLeft, definition.CellSize, outputRows, outputColumns));
            float[] patch = new float[patchDefinition.CellCount];
            for (int row = 0; row < outputRows; row++)
                Array.Copy(output, (minRow + row) * columns + minColumn, patch,
                    row * outputColumns, outputColumns);
            return WriteRasterData(request, new RasterData<float>(patchDefinition, patch) { NoDataValue = noData },
                patchDefinition, projection, cancellationToken, report);
        }

        return WriteRasterData(request, new RasterData<float>(definition, output) { NoDataValue = noData },
            definition, projection, cancellationToken, report);
    }

    private static (Tin Tin, double[] Values)? ProcessNoDataPolygon(Polygon polygon, float[] rasterData,
        RasterDefinition raster, float noData, HashSet<int> boundaryPixels, Parameters parameters)
    {
        Polygon perimeter = polygon.Clone();
        perimeter.Densify(parameters.BoundarySpacing);
        List<Point> interior = GenerateInteriorGrid(polygon, parameters.InteriorGridSpacing);
        var factory = new DelaunayTinFactory();
        factory.AddPerimeter(perimeter);
        factory.AddMassPoints(new PointCollection(perimeter));
        if (interior.Count > 0) factory.AddMassPoints(new PointCollection(interior));
        Tin tin = factory.ComputeTIN();
        if (tin.PointCount == 0) return null;

        double[] values = new double[tin.PointCount];
        int known = 0, columns = raster.Cols;
        double tolerance = parameters.BoundarySpacing * 0.1;
        for (int index = 0; index < tin.PointCount; index++)
        {
            Point point = tin.Points[index];
            if (!IsPointOnBoundary(point, perimeter, tolerance)) { values[index] = double.NaN; continue; }
            Pixel pixel = raster.CellIndex2D(point);
            if (!raster.InBounds(pixel)) { values[index] = double.NaN; continue; }
            float sampled = rasterData[pixel.To1D(columns)];
            if (!IsNoDataValue(sampled, noData)) { values[index] = sampled; known++; continue; }
            double nearest = FindNearestBoundaryValue(point, boundaryPixels, rasterData, raster, noData);
            values[index] = nearest;
            if (!double.IsNaN(nearest)) known++;
        }
        if (known < 3) return null;
        new TinLaplacian(tin).SolveLaplacian(values);
        return (tin, values);
    }

    private static List<Point> GenerateInteriorGrid(Polygon polygon, double spacing)
    {
        var points = new List<Point>();
        Extent extent = polygon.Extent;
        if (!double.IsFinite(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        for (double y = Math.Ceiling(extent.MinY / spacing) * spacing; y < extent.MaxY; y += spacing)
            for (double x = Math.Ceiling(extent.MinX / spacing) * spacing; x < extent.MaxX; x += spacing)
            {
                Point point = new(x, y);
                if (polygon.Contains(point)) points.Add(point);
            }
        return points;
    }

    private static double FindNearestBoundaryValue(Point point, HashSet<int> boundaryPixels,
        float[] rasterData, RasterDefinition raster, float noData)
    {
        double bestDistance = double.MaxValue, result = double.NaN;
        foreach (int index in boundaryPixels)
        {
            Point candidate = raster.CellCenter(index);
            double distance = point.DistanceSquaredTo(candidate);
            if (distance >= bestDistance) continue;
            float value = rasterData[index];
            if (IsNoDataValue(value, noData)) continue;
            bestDistance = distance; result = value;
        }
        return result;
    }

    private static bool IsPointOnBoundary(Point point, Polygon polygon, double tolerance) =>
        polygon.Segments.Any(segment => segment.DistanceToPoint(point) <= tolerance);

    private static void FillNoDataFromTin(Tin tin, double[] nodeValues, BitArray noDataMask,
        RasterDefinition raster, float[] output, ref int minRow, ref int maxRow,
        ref int minColumn, ref int maxColumn, ref bool anyFilled, CancellationToken cancellationToken)
    {
        int columns = raster.Cols, triangleIndex = 0;
        IReadOnlyList<TriangleDefinition> definitions = tin.TriangleDefinition;
        IReadOnlyList<Triangle> triangles = tin.Triangles;
        for (int index = 0; index < noDataMask.Length; index++)
        {
            if ((index & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!noDataMask[index]) continue;
            Point point = raster.CellCenter(index);
            tin.FindTriangleContainingPoint(point, ref triangleIndex);
            if (triangleIndex < 0 || triangleIndex >= tin.TriangleCount || definitions[triangleIndex].IsDeleted) continue;
            TriangleDefinition triangle = definitions[triangleIndex];
            float value = (float)triangles[triangleIndex].SamplePoint(point,
                nodeValues[triangle.p0], nodeValues[triangle.p1], nodeValues[triangle.p2]);
            if (!float.IsFinite(value)) continue;
            output[index] = value;
            int row = index / columns, column = index % columns;
            minRow = Math.Min(minRow, row); maxRow = Math.Max(maxRow, row);
            minColumn = Math.Min(minColumn, column); maxColumn = Math.Max(maxColumn, column);
            anyFilled = true;
        }
    }

    private static bool IsNoDataValue(float value, float noData) =>
        value == noData || float.IsNaN(value) || float.IsInfinity(value);

    private static (string Path, int Width, int Height, double[] Extent) WriteRasterData(
        GeoprocessingPolylineRequest request, RasterData<float> raster, RasterDefinition definition,
        Projection? projection, CancellationToken cancellationToken, Action<BackendOperationProgress>? report) =>
        WriteDerivedRaster(request, raster, definition, projection, cancellationToken, report);

    private static Point ToPoint(GeoprocessingPointInput input) =>
        new(input.Coordinates[0], input.Coordinates[1]);

    private static bool UsesRasterInput(string operation) => operation is
        "SnapToHighGround" or "ClipRasterToPolygon" or "RasterCutStats" or "LaplacianTerrain" or
        "ResampleRaster" or "RoundRaster" or "NoDataLaplacianFill" or "FlatAreaDetector";

    private static int RasterCaptureCount(GeoprocessingPolylineRequest request) =>
        (request.RasterSource is null ? 0 : 1) +
        (request.ResultRasterSource is null ? 0 : 1) +
        (request.TerrainRasterSource is null ? 0 : 1);

    private static string? RasterSourcePath(GeoprocessingPolylineRequest request) =>
        request.RasterSource?.SourcePath ?? request.ResultRasterSource?.ResultPath ??
        request.TerrainRasterSource?.SourcePath;

    private static RasterContext OpenRaster(GeoprocessingPolylineRequest request)
    {
        if (RasterCaptureCount(request) != 1)
            throw new ArgumentException("Choose exactly one current map raster, Terrain, or selected Result profile.");
        if (request.ResultRasterSource is { } resultSource)
            return OpenResultRaster(resultSource);
        if (request.TerrainRasterSource is { } terrainSource)
            return OpenTerrainRaster(terrainSource);

        RasterMapSourceIdentity source = request.RasterSource
            ?? throw new ArgumentException("Select a checked single-band raster source.");
        if (request.ProjectXml is null)
            throw new ArgumentException("The current project XML is required to read this map raster.");

        // Inspect resolves the exact .vis map/node and captures its source hash
        // before the direct sampler is opened. It also rejects non-single-band,
        // stale, missing, or unconnected rasters.
        RasterSymbologyInspectResult inspected = RasterSymbologyService.Inspect(
            new RasterSymbologyRequest(source, request.ProjectXml));
        if (source.SourceRevision is not null &&
            !source.SourceRevision.Equals(inspected.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string path = Path.GetFullPath(source.SourcePath);
        string sourceRevision = GeoprocessingToolService.ReadRevision(path);
        if (!sourceRevision.Equals(inspected.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Projection? targetProjection = ProjectProjection(request.ProjectXml);
        using var gdal = new GDALRaster(path);
        if (gdal.GetBandCount() != 1)
            throw new NotSupportedException("Only single-band float and double rasters are supported by this geoprocessing tool.");
        Type dataType = gdal.GetDataType();
        if (!dataType.IsEquivalentTo(typeof(float)) && !dataType.IsEquivalentTo(typeof(double)))
            throw new NotSupportedException("Only single-band float and double rasters are supported by this geoprocessing tool.");
        Projection? sourceProjection = gdal.GetProjection();
        IDisposable? cleanup = null;
        try
        {
            IResample<float> raster;
            if (dataType.IsEquivalentTo(typeof(float)))
                raster = RasterReader.TryGetBestRasterizer<float>(path, out _, out cleanup)
                    ?? throw new InvalidDataException("The selected raster could not be opened for sampling.");
            else
            {
                IResample<double> doubleRaster = RasterReader.TryGetBestRasterizer<double>(path, out _, out cleanup)
                    ?? throw new InvalidDataException("The selected raster could not be opened for sampling.");
                raster = doubleRaster.ConvertType(ConvertTypeResamplers.DoubleToFloatConverter,
                    (float?)doubleRaster.NoDataValue);
            }

            if (sourceProjection is not null && targetProjection is not null &&
                !targetProjection.IsEqual(sourceProjection))
                raster = raster.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));
            return new RasterContext(raster, sourceProjection, inspected.VisualizationRevision,
                inspected.SourceRevision, cleanup, () => VerifyRasterSnapshot(source,
                    request.ProjectXml, inspected.VisualizationRevision, inspected.SourceRevision));
        }
        catch
        {
            cleanup?.Dispose();
            throw;
        }
    }

    private static RasterContext OpenResultRaster(GeoprocessingResultRasterSourceCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Map is null || capture.SelectionOverrides is null ||
            string.IsNullOrWhiteSpace(capture.ContextToken) || capture.Node is null ||
            capture.SelectionIndex < 0 || !Path.IsPathFullyQualified(capture.ResultPath) ||
            !GeoprocessingToolService.IsSHA256(capture.ResultRevision) ||
            !GeoprocessingToolService.IsSHA256(capture.TerrainRevision) ||
            !Path.IsPathFullyQualified(capture.TerrainPath) || capture.ProfileIndex < 0 ||
            string.IsNullOrWhiteSpace(capture.MapType) || capture.ValidExtent is not { Length: 4 } extent ||
            extent.Any(value => !double.IsFinite(value)) || extent[0] >= extent[2] || extent[1] >= extent[3])
            throw new ArgumentException("The selected Result raster capture is incomplete.");

        ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            capture.Map, capture.SelectionOverrides, capture.ContextToken);
        bool transferred = false;
        try
        {
            MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(
                capture.Map, context.ProjectPath);
            if (readLease?.IsProtected(capture.ResultPath) == true ||
                readLease?.IsProtected(capture.TerrainPath) == true)
                throw new SourceFileChangedException();
            MapResultActiveNode activeNode = context.Sources.ActiveNodes.SingleOrDefault(node =>
                MapResultRenderService.NodeIdentity(node.Node) == MapResultRenderService.NodeIdentity(capture.Node))
                ?? throw new SourceFileChangedException();
            // ProfileIndex in the map tree is the last persisted .vis value. The
            // current native profile picker may already have rendered a newer
            // frame while its asynchronous .vis update is still pending. Validate
            // the captured runtime profile against the exact Result block below.
            if (!string.Equals(activeNode.SelectedMapType, capture.MapType, StringComparison.Ordinal) ||
                capture.MapProfileIndex < 0)
                throw new SourceFileChangedException();
            MapResultSelectionState slot = activeNode.Selections.SingleOrDefault(item =>
                item.SelectionIndex == capture.SelectionIndex)
                ?? throw new SourceFileChangedException();
            MapResultSourceChoice selected = slot.SelectedResult
                ?? throw new SourceFileChangedException();
            if (selected.IsComputing || !SourceFilePaths.SameSourceFile(selected.ResultPath, capture.ResultPath) ||
                !selected.Revision.Equals(capture.ResultRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            ResolvedMapResultNode result = context.ResultContexts.SingleOrDefault(item =>
                item.Selections.Any(selection =>
                    MapResultRenderService.NodeIdentity(selection.Node) == MapResultRenderService.NodeIdentity(capture.Node) &&
                    selection.SelectionIndex == capture.SelectionIndex) &&
                SourceFilePaths.SameSourceFile(item.ResultPath, capture.ResultPath) &&
                item.Revision.Equals(capture.ResultRevision, StringComparison.OrdinalIgnoreCase))
                ?? throw new SourceFileChangedException();
            Terrain terrain = result.Result.AssociatedTerrain
                ?? throw new InvalidDataException("The selected Result has no associated Terrain.");
            if (!SourceFilePaths.SameSourceFile(terrain.Filename, capture.TerrainPath))
                throw new SourceFileChangedException();
            MapTypes mapType = MapResultRenderService.ParseSupportedMapType(capture.MapType)
                ?? throw new ArgumentException("The selected Result map type is not supported.");
            IHorizontalOutputBlock outputBlock = result.Result.BaseOutputBlock
                ?? throw new InvalidDataException("The selected Result has no stored horizontal output profiles.");
            MapResultRenderService.ValidateProfiles(outputBlock);
            int expectedResultProfile = capture.MapProfileIndex;
            if (activeNode.NodeKind == "MultiResult")
            {
                ResolvedMapResultNode key = context.ResultContexts.SingleOrDefault(item =>
                    item.Selections.Any(selection =>
                        MapResultRenderService.NodeIdentity(selection.Node) == MapResultRenderService.NodeIdentity(capture.Node) &&
                        selection.SelectionIndex == 0))
                    ?? throw new SourceFileChangedException();
                IHorizontalOutputBlock keyBlock = key.Result.BaseOutputBlock
                    ?? throw new InvalidDataException("The MultiResult has no selected first Result profile.");
                expectedResultProfile = MapResultRenderService.MatchKeyedProfile(
                    keyBlock, capture.MapProfileIndex, outputBlock)
                    ?? throw new SourceFileChangedException();
            }
            if (capture.ProfileIndex != expectedResultProfile || capture.ProfileIndex >= outputBlock.ProfileCount)
                throw new ArgumentOutOfRangeException(nameof(capture.ProfileIndex));
            MapResultRenderService.TerrainSourceSnapshot terrainSnapshot = MapResultRenderService.CaptureTerrainSources(terrain);
            if (!terrainSnapshot.Path.Equals(SourceFilePaths.Canonical(capture.TerrainPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !string.Equals(terrainSnapshot.Revision, capture.TerrainRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            var resampler = new ResultMapResampler(result.Result, mapType);
            Extent validExtent = resampler.ValidExtent;
            if (!validExtent.IsFiniteAndValid() || validExtent.MinX != extent[0] ||
                validExtent.MinY != extent[1] || validExtent.MaxX != extent[2] ||
                validExtent.MaxY != extent[3])
                throw new SourceFileChangedException();
            IResample<float> raster = resampler.AsSingleProfile(capture.ProfileIndex);

            void EnsureCurrent()
            {
                context.EnsureUnchanged();
                MapResultRenderService.VerifyTerrainSources(terrainSnapshot);
                if (readLease?.IsProtected(capture.ResultPath) == true ||
                    readLease?.IsProtected(capture.TerrainPath) == true)
                    throw new SourceFileChangedException();
            }

            EnsureCurrent();
            var rasterContext = new RasterContext(raster, null,
                capture.Map.ExpectedVisualizationRevision ?? string.Empty,
                capture.ResultRevision, context, EnsureCurrent);
            transferred = true;
            return rasterContext;
        }
        finally
        {
            if (!transferred) context.Dispose();
        }
    }

    private static RasterContext OpenTerrainRaster(GeoprocessingTerrainRasterSourceCapture capture)
    {
        AssociatedMapLayersService.GeoprocessingTerrainSourceLease lease =
            AssociatedMapLayersService.OpenGeoprocessingTerrainSource(capture);
        try
        {
            IResample<float> raster = lease.Terrain;
            return new RasterContext(raster, null,
                capture.Request.Map.ExpectedVisualizationRevision ?? string.Empty,
                lease.SourceRevision, lease, lease.EnsureUnchanged);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static void VerifyRasterSnapshot(RasterMapSourceIdentity source, string? projectXml,
        string visualizationRevision, string sourceRevision)
    {
        if (!GeoprocessingToolService.ReadRevision(source.SourcePath)
                .Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        RasterSymbologyInspectResult current = RasterSymbologyService.Inspect(
            new RasterSymbologyRequest(source, projectXml));
        if (!current.VisualizationRevision.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            !current.SourceRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static Projection? ProjectProjection(string projectXml)
    {
        XDocument document = XDocument.Parse(projectXml, LoadOptions.None);
        XElement? projectionElement = document.Root?.Element("Projection");
        if (projectionElement is null) return null;
        ProjectionDefinition definition = ProjectionDefinition.Deserialize(projectionElement);
        var evaluated = definition.Evaluate(out Projection? projection, out _);
        if (!evaluated.Result) throw new InvalidDataException("The current project projection could not be evaluated.");
        return projection;
    }

    private static string WriteRasterOutput(GeoprocessingPolylineRequest request, IResample<float> raster,
        RasterDefinition definition, Projection? projection, CancellationToken cancellationToken,
        Action<BackendOperationProgress>? report)
    {
        string projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException("Open a saved project before writing a geoprocessing output.");
        string exportDirectory = Path.Combine(projectDirectory, "Exports");
        Directory.CreateDirectory(exportDirectory);
        string baseName = GeoprocessingToolService.SanitizeFileName(request.OutputName ?? "");
        if (baseName.Length == 0) throw new InvalidDataException("Enter a valid output name.");
        string destination = GeoprocessingToolService.UniqueBasePath(exportDirectory, baseName) + ".tif";
        var reporter = new ProgressReporter();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(reporter.RequestCancel);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projection is null)
                TiffExportEngine.Export(destination, raster, definition, pr: reporter);
            else
                TiffExportEngine.Export(destination, raster, definition, projection, reporter);
            cancellationToken.ThrowIfCancellationRequested();
            if (reporter.CancelRequested) throw new OperationCanceledException(cancellationToken);
            if (!File.Exists(destination))
                throw new IOException("The TIFF writer completed without creating its output file.");
            return destination;
        }
        catch
        {
            GeoprocessingToolService.TryDelete(destination);
            throw;
        }
    }

    private static (string Path, int Width, int Height, double[] Extent) ClipRaster(
        GeoprocessingPolylineRequest request, GeoprocessingPolygonInput[] inputs, Parameters parameters,
        IResample<float> source, Projection? sourceProjection, CancellationToken cancellationToken,
        Action<BackendOperationProgress>? report)
    {
        List<ComplexPolygon> polygons = inputs.Select(ToComplexPolygon).ToList();
        Extent extent = UnionPolygonExtent(polygons);
        if (!extent.IsValid() || extent.Area <= 0)
            throw new ArgumentException("Polygons have no valid extent.");
        double cellSize = parameters.OutputCellSize;
        if (cellSize <= 0)
            cellSize = source is IRaster<float> { RasterDefinition: var sourceDefinition }
                ? sourceDefinition.CellSize : Math.Sqrt(extent.Area) / 1000.0;
        RasterDefinition definition = CheckedDefinition(new RasterDefinition(extent, cellSize).ExtendRaster(1));
        var values = new float[definition.CellCount];
        float noData = source.NoDataValue ?? float.MinValue;
        source.Resample(new ResampleArgs(definition, InterpolationMode.Linear), values);
        var inside = new bool[definition.CellCount];
        var rasterizer = new PolygonRasterizer(definition);
        foreach (ComplexPolygon polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rasterizer.Rasterize(polygon, index =>
            {
                if ((uint)index < (uint)inside.Length) inside[index] = true;
            });
        }
        for (int index = 0; index < values.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inside[index]) values[index] = noData;
        }
        var output = new RasterData<float>(definition, values) { NoDataValue = noData };
        string path = WriteRasterOutput(request, output, definition,
            GeoprocessingToolService.ResolveProjection(request), cancellationToken, report);
        return (path, definition.Cols, definition.Rows,
            [definition.MinX, definition.MinY, definition.MaxX, definition.MaxY]);
    }

    private static string RasterCutStats(GeoprocessingPolygonInput[] inputs, int binCount,
        IResample<float> source, CancellationToken cancellationToken, Action<BackendOperationProgress>? report,
        CultureInfo culture)
    {
        List<ComplexPolygon> polygons = inputs.Select(ToComplexPolygon).ToList();
        Extent extent = UnionPolygonExtent(polygons);
        if (!extent.IsValid() || extent.Area <= 0)
            throw new ArgumentException("Polygons have no valid extent.");
        double cellSize = source is IRaster<float> { RasterDefinition: var sourceDefinition }
            ? sourceDefinition.CellSize : Math.Sqrt(extent.Area) / 1000.0;
        RasterDefinition definition = CheckedDefinition(new RasterDefinition(extent, cellSize).ExtendRaster(1));
        var values = new float[definition.CellCount];
        float noData = source.NoDataValue ?? -9999f;
        source.Resample(new ResampleArgs(definition, InterpolationMode.Linear), values);
        var builder = new StringBuilder();
        builder.AppendLine("Raster Cut Statistics");
        builder.AppendLine(new string('=', 60));
        var polygonRasterizer = new PolygonRasterizer(definition);
        for (int index = 0; index < polygons.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComplexPolygon polygon = polygons[index];
            var selectedCells = new List<int>();
            polygonRasterizer.Rasterize(polygon, selectedCells, (cellIndex, cells) =>
            {
                if ((uint)cellIndex < (uint)values.Length) cells.Add(cellIndex);
            });
            var sampleValues = selectedCells.Select(cellIndex => values[cellIndex]).ToArray();
            Histogram histogram = Histogram.ComputeFrom(new Memory<float>(sampleValues), binCount,
                definition.CellSize * definition.CellSize, noData);
            int totalCells = sampleValues.Length;
            int noDataCells = sampleValues.Count(value => value == noData);
            double missingRatio = totalCells > 0 ? (double)noDataCells / totalCells : 0;
            if (index > 0) builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant($"Polygon {index + 1}"));
            builder.AppendLine(new string('-', 60));
            builder.AppendLine("  Min:       " + histogram.Minimum.ToString("G6", culture));
            builder.AppendLine("  Max:       " + histogram.Maximum.ToString("G6", culture));
            builder.AppendLine("  Mean:      " + histogram.ComputeMean().ToString("G6", culture));
            builder.AppendLine("  Std Dev:   " + histogram.ComputeStandardDeviation().ToString("G6", culture));
            builder.AppendLine("  NoData:    " + missingRatio.ToString("P1", culture));
            builder.AppendLine("  Area:      " + histogram.ComputeArea().ToString("G6", culture));
            builder.AppendLine();
            FormatHistogramAscii(builder, histogram, culture);
            Report(report, index, polygons.Count, "Computing raster cut statistics");
        }
        return builder.ToString();
    }

    private static void FormatHistogramAscii(StringBuilder builder, Histogram histogram, CultureInfo culture)
    {
        double maxArea = histogram.BinArea.Max();
        if (maxArea <= 0) return;
        string[] ranges = Enumerable.Range(0, histogram.BinCount)
            .Select(index => string.Format(culture, "{0:G6} - {1:G6}",
                histogram.BinMinimum(index), histogram.BinMaximum(index))).ToArray();
        int width = ranges.Max(range => range.Length);
        builder.Append("  ").Append("Range".PadRight(width)).Append("  ")
            .Append("Area".PadLeft(12)).AppendLine("  Distribution");
        builder.Append("  ").Append(new string('-', width)).Append("  ")
            .Append(new string('-', 12)).Append("  ").AppendLine(new string('-', 40));
        for (int index = 0; index < histogram.BinCount; index++)
        {
            double area = histogram.BinArea[index];
            int bars = (int)Math.Round(area / maxArea * 40.0);
            if (area > 0 && bars == 0) bars = 1;
            builder.Append("  ").Append(ranges[index].PadRight(width)).Append("  ")
                .Append(area.ToString("G6", culture).PadLeft(12)).Append("  ")
                .AppendLine(new string('#', bars));
        }
    }

    private static Polyline SnapToHighGround(Polyline input, IResample<float> elevations,
        Parameters parameters, CancellationToken cancellationToken)
    {
        Polyline current = input;
        for (int iteration = 0; iteration < parameters.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapped = new Point[current.Count];
            for (int vertexIndex = 0; vertexIndex < current.Count; vertexIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Point vertex = current[vertexIndex];
                Point[] candidates = GenerateSearchPoints(vertex, current, vertexIndex, parameters);
                var samples = new float[candidates.Length];
                elevations.SamplePoints(candidates, samples);
                float? noData = elevations.NoDataValue;
                float best = parameters.ElevationMode == "Highest" ? float.MinValue : float.MaxValue;
                int bestIndex = -1;
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    float value = samples[sampleIndex];
                    if (noData.HasValue && value == noData.Value || !float.IsFinite(value)) continue;
                    bool better = parameters.ElevationMode == "Highest" ? value > best : value < best;
                    if (better) { best = value; bestIndex = sampleIndex; }
                }
                snapped[vertexIndex] = bestIndex >= 0 ? candidates[bestIndex] : vertex;
            }
            current = PreserveLineOrdinates(input, snapped);
            input = current;
        }
        return current;
    }

    private static Point[] GenerateSearchPoints(Point vertex, Polyline line, int vertexIndex, Parameters parameters)
    {
        int ringCount = (int)Math.Ceiling(parameters.SearchRadius / parameters.SampleSpacing);
        long pointCount = parameters.SearchMode == "Perpendicular"
            ? 2L * ringCount + 1
            : 1L + (long)ringCount * parameters.PointsPerRing;
        if (pointCount <= 0 || pointCount > MaximumGeneratedSearchSamples)
            throw new InvalidDataException("The selected high-ground search settings exceed the supported sample count.");
        var points = new Point[(int)pointCount];
        if (parameters.SearchMode == "Perpendicular")
        {
            Vector direction = LineDirectionAt(line, vertexIndex).Perpendicular(clockwise: true);
            for (int sample = -ringCount; sample <= ringCount; sample++)
            {
                double distance = sample * parameters.SampleSpacing;
                points[sample + ringCount] = new Point(vertex.X + direction.DeltaX * distance,
                    vertex.Y + direction.DeltaY * distance);
            }
            return points;
        }
        points[0] = vertex;
        int target = 1;
        for (int ring = 1; ring <= ringCount; ring++)
        {
            double distance = ring * parameters.SampleSpacing;
            for (int sample = 0; sample < parameters.PointsPerRing; sample++)
            {
                double angle = Math.PI * 2.0 * sample / parameters.PointsPerRing;
                points[target++] = new Point(vertex.X + distance * Math.Cos(angle),
                    vertex.Y + distance * Math.Sin(angle));
            }
        }
        return points;
    }

    private static Vector LineDirectionAt(Polyline line, int vertexIndex)
    {
        if (vertexIndex == 0) return new Segment(line[0], line[1]).UnitVector();
        if (vertexIndex == line.Count - 1) return new Segment(line[vertexIndex - 1], line[vertexIndex]).UnitVector();
        return new Segment(line[vertexIndex - 1], line[vertexIndex + 1]).UnitVector();
    }

    private static Polyline PreserveLineOrdinates(Polyline source, Point[] points)
    {
        var result = new Polyline(points);
        if (source.HasZ) result.TrackZ(source.Z.ToArray());
        if (source.HasM) result.TrackM(source.M.ToArray());
        return result;
    }

    private static (string Path, int Width, int Height, double[] Extent) LaplacianTerrain(
        GeoprocessingPolylineRequest request, GeoprocessingPolygonInput[] inputs, Parameters parameters,
        IResample<float> elevations, Projection? sourceProjection, CancellationToken cancellationToken,
        Action<BackendOperationProgress>? report)
    {
        var surfaces = new List<(Tin Tin, double[] Values)>();
        Extent targetExtent = Extent.NullExtent;
        for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComplexPolygon original = ToComplexPolygon(inputs[inputIndex]);
            foreach (Polygon ring in original.Rings) ring.Densify(parameters.BoundarySpacing);
            List<Point> interior = GenerateInteriorGrid(original, parameters.InteriorGridSpacing);
            var factory = new DelaunayTinFactory();
            factory.AddPerimeter(original);
            Point[] boundaryPoints = original.Rings.SelectMany(ring => ring.Points).ToArray();
            factory.AddMassPoints(new PointCollection(boundaryPoints));
            if (interior.Count > 0) factory.AddMassPoints(new PointCollection(interior));
            Tin tin = factory.ComputeTIN();
            if (tin.PointCount == 0)
            {
                Report(report, inputIndex, inputs.Length, "Building terrain interpolation");
                continue;
            }
            var values = new double[tin.PointCount];
            var sampled = new float[tin.PointCount];
            Point[] samplePoints = tin.Points.ToArray();
            elevations.SamplePoints(samplePoints, sampled);
            float? noData = elevations.NoDataValue;
            double boundaryTolerance = parameters.BoundarySpacing * 0.1;
            for (int index = 0; index < tin.PointCount; index++)
            {
                if (IsPointOnBoundary(tin.Points[index], original, boundaryTolerance) &&
                    (!noData.HasValue || sampled[index] != noData.Value) && float.IsFinite(sampled[index]))
                    values[index] = sampled[index];
                else
                    values[index] = double.NaN;
            }
            if (!values.Any(double.IsFinite))
                throw new ArgumentException("No valid elevation samples found on polygon boundary. Check that the polygon overlaps the elevation raster.");
            new TinLaplacian(tin).SolveLaplacian(values);
            surfaces.Add((tin, values));
            targetExtent = targetExtent.Union(tin.Extent);
            Report(report, inputIndex, inputs.Length, "Solving terrain interpolation");
        }
        if (surfaces.Count == 0 || !targetExtent.IsValid())
            throw new ArgumentException("No valid polygons to process.");
        RasterDefinition definition = CheckedDefinition(new RasterDefinition(targetExtent,
            parameters.LaplacianOutputCellSize).ExtendRaster(1));
        var outputValues = new float[definition.CellCount];
        Array.Fill(outputValues, float.MinValue);
        foreach ((Tin tin, double[] values) in surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RasterizeTinNonDeleted(tin, values, definition, outputValues);
        }
        var output = new RasterData<float>(definition, outputValues) { NoDataValue = float.MinValue };
        string path = WriteRasterOutput(request, output, definition,
            GeoprocessingToolService.ResolveProjection(request), cancellationToken, report);
        return (path, definition.Cols, definition.Rows,
            [definition.MinX, definition.MinY, definition.MaxX, definition.MaxY]);
    }

    private static List<Point> GenerateInteriorGrid(ComplexPolygon polygon, double spacing)
    {
        var points = new List<Point>();
        Extent extent = polygon.Extent;
        double firstX = Math.Ceiling(extent.MinX / spacing) * spacing;
        double firstY = Math.Ceiling(extent.MinY / spacing) * spacing;
        for (double y = firstY; y < extent.MaxY; y += spacing)
        {
            for (double x = firstX; x < extent.MaxX; x += spacing)
            {
                if (points.Count >= 5_000_000)
                    throw new InvalidDataException("The selected interior spacing creates too many terrain points.");
                Point point = new(x, y);
                if (polygon.ContainsPoint_Slow(point)) points.Add(point);
            }
        }
        return points;
    }

    private static bool IsPointOnBoundary(Point point, ComplexPolygon polygon, double tolerance)
    {
        foreach (Polygon ring in polygon.Rings)
            foreach (Segment segment in ring.Segments)
                if (segment.DistanceToPoint(point) <= tolerance) return true;
        return false;
    }

    private static void RasterizeTinNonDeleted(Tin tin, double[] nodeValues,
        RasterDefinition raster, float[] output)
    {
        if (!tin.Extent.Overlaps(raster.Extent)) return;
        List<TriangleDefinition> triangles = tin.TriangleDefinition;
        for (int index = 0; index < tin.TriangleCount; index++)
        {
            TriangleDefinition triangle = triangles[index];
            if (triangle.IsDeleted) continue;
            tin.Triangles[index].Rasterize(raster, (int pixel, BarycentricWeights weights) =>
            {
                if ((uint)pixel < (uint)output.Length)
                    output[pixel] = (float)weights.Interpolate(nodeValues[triangle.p0],
                        nodeValues[triangle.p1], nodeValues[triangle.p2]);
            });
        }
    }

    private static RasterDefinition CheckedDefinition(RasterDefinition definition)
    {
        if (!definition.IsValid() || definition.CellCountLong <= 0 || definition.CellCountLong > MaximumOutputCells)
            throw new InvalidDataException("The requested output raster is too large or has invalid bounds.");
        return definition;
    }

    private static Extent UnionPolygonExtent(IEnumerable<ComplexPolygon> polygons)
    {
        Extent extent = Extent.NullExtent;
        foreach (ComplexPolygon polygon in polygons) if (polygon.RingCount > 0) extent = extent.Union(polygon.Extent);
        return extent;
    }

    private static Extent UnionLineExtent(IEnumerable<Polyline> lines)
    {
        Extent extent = Extent.NullExtent;
        foreach (Polyline line in lines) if (line.Count > 0) extent = extent.Union(line.Extent);
        return extent;
    }


    private static Polyline Filter(Polyline source, double tolerance)
    {
        Polyline filtered = source.Clone();
        filtered.Filter(tolerance);
        return filtered;
    }

    private static void ValidateInputsIdentity(GeoprocessingPolylineInput[] lines,
        GeoprocessingPolygonInput[] polygons, GeoprocessingPointInput[] points,
        bool lineInput, bool pointInput)
    {
        if (lineInput && (lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.FeatureIdentity)
                || string.IsNullOrWhiteSpace(line.PathIdentity) || line.SourceFeatureIndex < 0 || line.PathIndex < 0)
            || lines.Select(line => line.PathIdentity).Distinct(StringComparer.Ordinal).Count() != lines.Length))
            throw new InvalidDataException("The captured line selection is empty or has duplicate or missing identities.");
        if (!lineInput && !pointInput && (polygons.Any(polygon => polygon is null || string.IsNullOrWhiteSpace(polygon.FeatureIdentity)
                || string.IsNullOrWhiteSpace(polygon.PathIdentity) || polygon.SourceFeatureIndex < 0 || polygon.PathIndex < 0)
            || polygons.Select(polygon => polygon.PathIdentity).Distinct(StringComparer.Ordinal).Count() != polygons.Length))
            throw new InvalidDataException("The captured polygon selection is empty or has duplicate or missing identities.");
        if (pointInput && (points.Any(point => point is null || string.IsNullOrWhiteSpace(point.FeatureIdentity)
                || string.IsNullOrWhiteSpace(point.PathIdentity) || point.SourceFeatureIndex < 0 || point.PathIndex < 0)
            || points.Select(point => point.PathIdentity).Distinct(StringComparer.Ordinal).Count() != points.Length))
            throw new InvalidDataException("The captured seed-point selection is empty or has duplicate or missing identities.");
    }

    private static void ValidatePoint(GeoprocessingPointInput input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.FeatureIdentity) ||
            string.IsNullOrWhiteSpace(input.PathIdentity) || input.SourceFeatureIndex < 0 || input.PathIndex < 0 ||
            input.Coordinates is null || input.Coordinates.Length != 2 ||
            input.Coordinates.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("The captured seed point has invalid coordinates or identity.");
    }

    private static void ValidateLine(GeoprocessingPolylineInput input)
    {
        if (input.Coordinates is null || input.Coordinates.Length < 2
            || input.Coordinates.Any(point => point is null || point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
            throw new InvalidDataException("Only finite two-dimensional XY polylines with at least two vertices are supported.");
        ValidateOrdinates(input.ZValues, input.Coordinates.Length, "Z");
        ValidateOrdinates(input.MValues, input.Coordinates.Length, "M");
    }

    private static void ValidatePolygon(GeoprocessingPolygonInput input)
    {
        ValidatePolygonRing(input.Coordinates, input.ZValues, input.MValues);
        double[][][] holes = input.Holes ?? [];
        string[]?[]? zValues = input.HoleZValues;
        string[]?[]? mValues = input.HoleMValues;
        if (zValues is not null && zValues.Length != holes.Length)
            throw new InvalidDataException("Polygon hole Z dimensions do not match the captured hole rings.");
        if (mValues is not null && mValues.Length != holes.Length)
            throw new InvalidDataException("Polygon hole M dimensions do not match the captured hole rings.");
        for (int index = 0; index < holes.Length; index++)
            ValidatePolygonRing(holes[index], zValues?[index], mValues?[index]);
    }

    private static void ValidatePolygonRing(double[][]? coordinates, string[]? zValues, string[]? mValues)
    {
        if (coordinates is null || coordinates.Length < 4
            || coordinates.Any(point => point is null || point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
            throw new InvalidDataException("Only finite two-dimensional closed polygon rings with at least four coordinates are supported.");
        if (coordinates[0][0] != coordinates[^1][0] || coordinates[0][1] != coordinates[^1][1])
            throw new InvalidDataException("Polygon input rings must be closed.");
        ValidateOrdinates(zValues, coordinates.Length, "Z");
        ValidateOrdinates(mValues, coordinates.Length, "M");
    }

    private static ComplexPolygon ToComplexPolygon(GeoprocessingPolygonInput input)
    {
        var rings = new List<Polygon>(1 + (input.Holes?.Length ?? 0))
        {
            ToRing(input.Coordinates, input.ZValues, input.MValues)
        };
        for (int index = 0; index < (input.Holes?.Length ?? 0); index++)
            rings.Add(ToRing(input.Holes![index], input.HoleZValues?[index], input.HoleMValues?[index]));

        // The source GeoJSON reader reverses a negative outer ring and all of
        // its holes before calling ComplexPolygon validation. Keep that exact
        // normalization while refusing invalid topology instead of flattening
        // a hole into an independent filled polygon.
        if (rings[0].Area < 0)
            foreach (Polygon ring in rings) ring.Reverse();
        var result = ComplexPolygon.TryCreateComplexPolygon(rings, out ComplexPolygon? polygon,
            checkIntersections: false);
        if (!result.Result || polygon is null)
            throw new InvalidDataException("Polygon rings do not form a valid outer ring and hole set.");
        return polygon;
    }

    private static ComplexPolygon ToComplexPolygon(GeoprocessingPolygonOutput output) =>
        ToComplexPolygon(new GeoprocessingPolygonInput(output.FeatureIdentity, output.PathIdentity,
            output.SourceFeatureIndex, output.PathIndex, output.Coordinates, output.ZValues, output.MValues,
            output.Holes, output.HoleZValues, output.HoleMValues));

    private static Polygon ToRing(double[][] coordinates, string[]? zValues, string[]? mValues)
    {
        Point[] points = coordinates.Select(point => new Point(point[0], point[1])).ToArray();
        double[]? z = ParseNullableOrdinates(zValues);
        double[]? m = ParseNullableOrdinates(mValues);
        PointCollection pointCollection;
        if (z is not null && m is not null)
            pointCollection = new PointCollection(points.AsMemory(), z.AsMemory(), m.AsMemory());
        else if (z is not null)
            pointCollection = new PointCollection(points.AsMemory(), z.AsMemory());
        else if (m is not null)
            pointCollection = new PointCollection(points.AsMemory(), Memory<double>.Empty, m.AsMemory());
        else
            pointCollection = new PointCollection(points);
        return Polygon.CreateWithBuffer(pointCollection);
    }

    private static GeoprocessingPolygonOutput CreatePolygonOutput(Polygon polygon,
        GeoprocessingPolylineInput source, int index) =>
        CreatePolygonOutput(ComplexPolygon.FromOuterRing(polygon), index);

    private static GeoprocessingPolygonOutput CreatePolygonOutput(Polygon polygon,
        GeoprocessingPolygonInput source, int index) =>
        CreatePolygonOutput(ComplexPolygon.FromOuterRing(polygon), index);

    private static GeoprocessingPolygonOutput CreatePolygonOutput(ComplexPolygon polygon,
        GeoprocessingPolygonInput source, int index) => CreatePolygonOutput(polygon, index);

    private static GeoprocessingPolygonOutput CreatePolygonOutput(ComplexPolygon polygon, int index)
    {
        var outer = PolygonOutputRing(polygon.Rings[0]);
        var holes = polygon.Rings.Skip(1).Select(PolygonOutputRing).ToArray();
        return new("geoprocessing-polygon-feature:" + index.ToString(CultureInfo.InvariantCulture),
            "geoprocessing-polygon-path:" + index.ToString(CultureInfo.InvariantCulture), index, 0,
            outer.Coordinates, outer.ZValues, outer.MValues,
            holes.Select(ring => ring.Coordinates).ToArray(),
            holes.Select(ring => ring.ZValues).ToArray(),
            holes.Select(ring => ring.MValues).ToArray());
    }

    private static (double[][] Coordinates, string[]? ZValues, string[]? MValues) PolygonOutputRing(Polygon polygon)
    {
        // The retained geometry library closes rings implicitly. Public polygon
        // results also feed GeoJSON and Shapefile output, whose rings must carry
        // the closing vertex explicitly, including its corresponding Z/M.
        double[][] coordinates = PolygonCoordinates(polygon);
        string[]? zValues = GeoprocessingToolService.ToOrdinates(polygon.HasZ ? polygon.Z : null);
        string[]? mValues = GeoprocessingToolService.ToOrdinates(polygon.HasM ? polygon.M : null);
        if (coordinates.Length < 3 || coordinates.Any(point => !double.IsFinite(point[0]) || !double.IsFinite(point[1]))
            || (zValues is not null && zValues.Length != coordinates.Length)
            || (mValues is not null && mValues.Length != coordinates.Length))
            throw new InvalidDataException("The polygon result contains an invalid ring or mismatched ordinates.");
        if (coordinates[0][0] != coordinates[^1][0] || coordinates[0][1] != coordinates[^1][1]
            || (zValues is not null && zValues[0] != zValues[^1])
            || (mValues is not null && mValues[0] != mValues[^1]))
        {
            coordinates = [.. coordinates, coordinates[0].ToArray()];
            if (zValues is not null) zValues = [.. zValues, zValues[0]];
            if (mValues is not null) mValues = [.. mValues, mValues[0]];
        }
        if (coordinates.Length < 4)
            throw new InvalidDataException("The polygon result contains a degenerate ring.");
        return (coordinates, zValues, mValues);
    }

    private static double[][] PolygonCoordinates(Polygon polygon)
    {
        IReadOnlyList<Point> points = polygon;
        return Enumerable.Range(0, points.Count)
            .Select(index => new[] { points[index].X, points[index].Y }).ToArray();
    }

    private static GeoprocessingPolylineOutput CreateSourceLineOutput(GeoprocessingPolylineInput source, Polyline line) =>
        new(source.FeatureIdentity, source.PathIdentity, source.SourceFeatureIndex, source.PathIndex,
            GeoprocessingToolService.ToCoordinates(line),
            GeoprocessingToolService.ToOrdinates(line.HasZ ? line.Z : null),
            GeoprocessingToolService.ToOrdinates(line.HasM ? line.M : null));

    private static GeoprocessingPolylineOutput CreateGeneratedLineOutput(Polyline line, int index) =>
        new("geoprocessing-line-feature:" + index.ToString(CultureInfo.InvariantCulture),
            "geoprocessing-line-path:" + index.ToString(CultureInfo.InvariantCulture), index, 0,
            GeoprocessingToolService.ToCoordinates(line),
            GeoprocessingToolService.ToOrdinates(line.HasZ ? line.Z : null),
            GeoprocessingToolService.ToOrdinates(line.HasM ? line.M : null));

    private static string ToGeoJson(IReadOnlyList<GeoprocessingPolygonOutput> polygons)
    {
        var features = polygons.Select(polygon => new
        {
            type = "Feature",
            id = polygon.FeatureIdentity,
            properties = new
            {
                sourceFeatureIndex = polygon.SourceFeatureIndex,
                pathIndex = polygon.PathIndex,
                zValues = polygon.ZValues,
                mValues = polygon.MValues,
                holeZValues = polygon.HoleZValues,
                holeMValues = polygon.HoleMValues
            },
            geometry = new { type = "Polygon", coordinates = new[] { polygon.Coordinates }.Concat(polygon.Holes ?? []).ToArray() }
        });
        return JsonSerializer.Serialize(new { type = "FeatureCollection", features });
    }

    private static string WritePolygonShapefile(GeoprocessingPolylineRequest request, string name,
        IReadOnlyList<GeoprocessingPolygonOutput> outputs, CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException("Open a saved project before writing a geoprocessing output.");
        string exportDirectory = Path.Combine(projectDirectory, "Exports");
        Directory.CreateDirectory(exportDirectory);
        string safeName = GeoprocessingToolService.SanitizeFileName(name);
        if (safeName.Length == 0) throw new InvalidDataException("Enter a valid output name.");
        string targetBase = GeoprocessingToolService.UniqueBasePath(exportDirectory, safeName);
        string stagingDirectory = Path.Combine(exportDirectory, ".geoprocessing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        string stagedPath = Path.Combine(stagingDirectory, safeName + ".shp");
        var moved = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Projection? projection = GeoprocessingToolService.ResolveProjection(request);
            ComplexPolygon[] polygons = outputs.Select(ToComplexPolygon).ToArray();
            OperationResult write = projection is null
                ? ShapefileIO.Write(stagedPath, polygons)
                : ShapefileIO.Write(stagedPath, polygons, projection: projection);
            if (!write.Result)
                throw new IOException("The retained Shapefile writer failed to write the polygon geometry.");
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string extension in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
            {
                string source = Path.ChangeExtension(stagedPath, extension);
                if (!File.Exists(source)) continue;
                string destination = Path.ChangeExtension(targetBase, extension);
                File.Move(source, destination); moved.Add(destination);
            }
            if (!File.Exists(targetBase + ".shp") || !File.Exists(targetBase + ".shx") || !File.Exists(targetBase + ".dbf"))
                throw new InvalidDataException("The retained Shapefile writer did not produce the required source family.");
            return targetBase + ".shp";
        }
        catch
        {
            foreach (string path in moved) GeoprocessingToolService.TryDelete(path);
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static string WriteTextOutput(GeoprocessingPolylineRequest request, string contents,
        string extension, CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException("Open a saved project before writing a geoprocessing output.");
        string exportDirectory = Path.Combine(projectDirectory, "Exports");
        Directory.CreateDirectory(exportDirectory);
        string baseName = GeoprocessingToolService.SanitizeFileName(request.OutputName ?? "");
        if (baseName.Length == 0) throw new InvalidDataException("Enter a valid output name.");
        string path = Path.Combine(exportDirectory, baseName + extension);
        for (int suffix = 2; File.Exists(path); suffix++) path = Path.Combine(exportDirectory, $"{baseName} ({suffix}){extension}");
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(staged, contents, new System.Text.UTF8Encoding(false));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staged, path);
            return path;
        }
        finally { GeoprocessingToolService.TryDelete(staged); }
    }

    private static void ValidateOrdinates(string[]? values, int count, string dimension)
    {
        if (values is null) return;
        if (values.Length != count || values.Any(text => !double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)))
            throw new InvalidDataException("The captured " + dimension + " ordinates do not match the selected line coordinates.");
    }

    private static double[]? ParseNullableOrdinates(string[]? values) => values is null
        ? null
        : values.Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    private static void ValidateDouble(string text, string cultureName, string field,
        List<GeoprocessingValidationIssue> issues, bool positive = false, bool finiteOnly = false)
    {
        if (!TryDouble(text, GetCulture(cultureName), out double value))
        {
            issues.Add(new(field, "Not valid " + Utility.Types.ToFriendlyName(typeof(double)) + " type.", "TypeError"));
            return;
        }
        if (positive)
        {
            LeveledValidationResult? result = new GreaterThanZero().ValidateValue(value,
                new ValidationContext(new object()) { MemberName = field });
            AddLeveled(issues, field, result);
        }
        else if (finiteOnly && !double.IsFinite(value))
            issues.Add(new(field, "Enter a finite number.", "Error"));
    }

    private static void ValidateDouble(string text, string cultureName, string field,
        bool positive, List<GeoprocessingValidationIssue> issues) =>
        ValidateDouble(text, cultureName, field, issues, positive, finiteOnly: true);

    private static void ValidateIntRange(string text, CultureInfo culture, string field,
        int minimum, int maximum, List<GeoprocessingValidationIssue> issues)
    {
        if (!int.TryParse(text, NumberStyles.Integer, culture, out int value))
        {
            issues.Add(new(field, "Not valid " + Utility.Types.ToFriendlyName(typeof(int)) + " type.", "TypeError"));
            return;
        }
        var result = new RangeAttribute(minimum, maximum).GetValidationResult(value,
            new ValidationContext(new object()) { MemberName = field });
        if (result != ValidationResult.Success)
            issues.Add(new(field, result?.ErrorMessage ?? "The value is outside the allowed range.", "Error"));
    }

    private static void ValidateDistanceAlgorithm(string algorithm, List<GeoprocessingValidationIssue> issues)
    {
        if (algorithm is not ("EDT" or "SpatialIndex"))
            issues.Add(new("DistanceAlgorithm", "Choose a supported distance algorithm.", "TypeError"));
    }

    private static void AddLeveled(List<GeoprocessingValidationIssue> issues, string field, LeveledValidationResult? result)
    {
        if (result is not null) issues.Add(new(field, result.ErrorMessage ?? "The value is invalid.", result.ErrorLevel.ToString()));
    }

    private static bool IsError(GeoprocessingValidationIssue issue) => issue.Severity is "Error" or "TypeError";

    private static bool TryDouble(string? text, CultureInfo culture, out double value) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out value) && double.IsFinite(value);

    private static double ParseDouble(string? text, CultureInfo culture, double fallback) =>
        TryDouble(text, culture, out double value) ? value : fallback;

    private static int ParseInt(string? text, CultureInfo culture, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, culture, out int value) ? value : fallback;

    private static CultureInfo GetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return CultureInfo.GetCultureInfo("en-US");
        try { return CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static void Report(Action<BackendOperationProgress>? report, int index, int count, string taskName) =>
        report?.Invoke(new(Fraction: 0.05 + 0.75 * (index + 1) / Math.Max(1, count), TaskName: taskName));
}
