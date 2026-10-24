using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Handlers;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;
using Ras.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private sealed record GeometryCulvertMapPlan(string[] CapturedFeatureIDs,
        string[] RemovedFeatureIDs, string[] RetainedFeatureIDs,
        Polyline[] OutputPolylines, string? ErrorCode);

    /// <summary>
    /// Request-local scratch and raster cache for a multi-waypoint SmartAdd.
    /// The owner is deliberately bound to one loaded Terrain, its canonical
    /// source identity, dependency revision, and every raster-window value.
    /// </summary>
    private sealed class SmartAddSegmentWorkScope
    {
        private readonly ObservableTerrain _terrain;
        private readonly string _canonicalTerrainPath;
        private readonly string _dependencyRevision;
        private readonly GeometryCulvertSmartAddWindow _window;
        private RasterDefinition _raster;
        private bool _rasterCreated;
        private float[]? _elevations;
        private double _zMin;
        private double _zMax;
        private bool _sampleAttempted;
        private bool _sampleUsable;
        private HessianRidgenessWalker? _hessianWalker;
        private int[]? _parent;
        private double[]? _gScore;
        private double[]? _length;
        private PriorityQueue<int, double>? _open;
        private List<(int row, int col)>? _pixelPath;

        public SmartAddSegmentWorkScope(ObservableTerrain terrain, string canonicalTerrainPath,
            string dependencyRevision, GeometryCulvertSmartAddWindow window)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            if (string.IsNullOrWhiteSpace(canonicalTerrainPath) ||
                !Path.IsPathFullyQualified(canonicalTerrainPath) ||
                string.IsNullOrWhiteSpace(terrain.Filename) ||
                !SourceFilePaths.SameSourceFile(canonicalTerrainPath, terrain.Filename))
                throw new SourceFileChangedException();
            _canonicalTerrainPath = SourceFilePaths.Canonical(canonicalTerrainPath);
            _dependencyRevision = dependencyRevision ?? throw new ArgumentNullException(nameof(dependencyRevision));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            if (window.Extent is null) throw new SourceFileChangedException();
        }

        public void Verify(ObservableTerrain terrain, GeometryCulvertSmartAddWindow window,
            string dependencyRevision)
        {
            if (!ReferenceEquals(_terrain, terrain) || string.IsNullOrWhiteSpace(terrain.Filename) ||
                !SourceFilePaths.SameSourceFile(_canonicalTerrainPath, terrain.Filename) ||
                !string.Equals(_dependencyRevision, dependencyRevision, StringComparison.Ordinal) ||
                !SameWindow(_window, window))
                throw new SourceFileChangedException();
        }

        public RasterDefinition GetRaster()
        {
            if (!_rasterCreated)
            {
                GeometryCulvertExtent extent = _window.Extent;
                _raster = new RasterDefinition(new Extent(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY),
                    _window.Rows, _window.Columns);
                _rasterCreated = true;
            }
            return _raster;
        }

        public bool TryGetTerrainGrid(ObservableTerrain terrain, RasterDefinition raster,
            out float[] elevations, out double zMin, out double zMax)
        {
            if (!_sampleAttempted)
            {
                _sampleAttempted = true;
                int count = checked(raster.Rows * raster.Cols);
                float[] sampled = new float[count];
                terrain.Layer.Resample(new ResampleArgs(raster, SamplingParams.Default), sampled.AsMemory());
                _sampleUsable = TryNormalizeTerrainElevations(sampled, out _zMin, out _zMax);
                _elevations = sampled;
            }

            elevations = _elevations ?? [];
            zMin = _zMin;
            zMax = _zMax;
            return _sampleUsable;
        }

        public HessianRidgenessWalker HessianWalker =>
            _hessianWalker ??= new HessianRidgenessWalker();

        public void GetHighGroundWorkspace(int count, out int[] parent, out double[] gScore,
            out double[] length, out PriorityQueue<int, double> open,
            out List<(int row, int col)> pixelPath)
        {
            if (_parent is null || _parent.Length < count) _parent = new int[count];
            if (_gScore is null || _gScore.Length < count) _gScore = new double[count];
            if (_length is null || _length.Length < count) _length = new double[count];
            _open ??= new PriorityQueue<int, double>();
            _pixelPath ??= new List<(int row, int col)>(256);
            parent = _parent;
            gScore = _gScore;
            length = _length;
            open = _open;
            pixelPath = _pixelPath;
        }

        private static bool SameWindow(GeometryCulvertSmartAddWindow left,
            GeometryCulvertSmartAddWindow right) => left is not null && right is not null &&
            left.Rows == right.Rows && left.Columns == right.Columns &&
            left.Extent is not null && right.Extent is not null &&
            left.Extent.MinX == right.Extent.MinX && left.Extent.MinY == right.Extent.MinY &&
            left.Extent.MaxX == right.Extent.MaxX && left.Extent.MaxY == right.Extent.MaxY;
    }

    /// <summary>Builds a non-mutating map preview from the current typed child draft.</summary>
    public static Task<GeometryCulvertMapCommandPreviewResponse> PreviewGeometryCulvertMapCommandAsync(
        GeometryCulvertMapCommandPreviewRequest request)
    {
        VerifyGeometryCulvertModel(request.Model);
        ValidatePreviewID(request.PreviewID);
        string revision = Revision(request.Model.AssetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(request.Model);
        GeometryCulvertDraftState state = ReadGeometryCulvertDraft(request.DraftState,
            request.Model.AssetPath, request.Revision, dependencyRevision);
        GeometryCulvertMapPlan plan;
        using (var context = new ModelContext(ToModelRequest(request.Model)))
        {
            GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
            ApplyGeometryCulvertMutations(context, request.Model.AssetPath, request.Revision, state, identities);
            EnsureGeometryCulvertEditable(context);
            plan = BuildGeometryCulvertMapPlan(context, identities, request.Operation,
                request.FeatureIDs ?? [], request.TargetFeatureID, request.SplitPoint, request.SplitMode,
                request.MapCellSize, request.MergeTolerance, request.OffsetDistance, request.OffsetSides,
                request.IncludeEndpoints, request.KeepOriginal);
        }
        if (!string.Equals(Revision(request.Model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        VerifyGeometryCulvertDependencyRevision(request.Model, dependencyRevision);

        string[] outputDraftIDs = ValidateOutputDraftIDs(request.OutputDraftIDs, plan.OutputPolylines.Length,
            requireExactly: false);
        GeometryCulvertMapCommandOutput[] outputs = plan.OutputPolylines.Select((polyline, index) =>
            new GeometryCulvertMapCommandOutput(outputDraftIDs[index], CoordinatesFor(polyline))).ToArray();
        bool changed = plan.OutputPolylines.Length > 0
            && (plan.RemovedFeatureIDs.Length > 0 || outputs.Length > 0);
        return Task.FromResult(new GeometryCulvertMapCommandPreviewResponse(request.PreviewID,
            revision, request.DraftState, dependencyRevision, request.Operation,
            plan.CapturedFeatureIDs, plan.RemovedFeatureIDs, plan.RetainedFeatureIDs, outputs,
            changed, changed, outputs.Length > 0 ? "generatedBarrelsRequireProperties" : null,
            plan.ErrorCode));
    }

    /// <summary>
    /// Computes one source SmartAdd segment against the actual associated terrain.
    /// The response is a preview only; final insertion uses the regular addBarrel
    /// child mutation and therefore remains in the existing undo/save ledger.
    /// </summary>
    public static Task<GeometryCulvertSmartAddPreviewResponse> PreviewGeometryCulvertSmartAddAsync(
        GeometryCulvertSmartAddPreviewRequest request)
    {
        if (request.Window is null || request.Start is null || request.End is null)
            throw new ArgumentException("SmartAdd requires endpoints and the current map raster window.");
        VerifyGeometryCulvertModel(request.Model);
        ValidatePreviewID(request.PreviewID);
        string revision = Revision(request.Model.AssetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(request.Model);
        GeometryCulvertDraftState state = ReadGeometryCulvertDraft(request.DraftState,
            request.Model.AssetPath, request.Revision, dependencyRevision);
        GeometryCulvertSmartAddPreviewResponse result;
        using (var context = new ModelContext(ToModelRequest(request.Model)))
        {
            GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
            ApplyGeometryCulvertMutations(context, request.Model.AssetPath, request.Revision, state, identities);
            EnsureGeometryCulvertEditable(context);
            ObservableGeometry geometry = GeometryCulvertGeometry(context);
            ObservableTerrain? terrain = geometry.AssociatedTerrain;
            if (terrain is null || terrain.Layer is null)
            {
                result = new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                    terrain?.Name, false, [], "associatedTerrainUnavailable");
            }
            else
            {
                GeometryCulvertSmartAddOptions options = request.Options ?? new GeometryCulvertSmartAddOptions();
                result = TryComputeSmartAddSegment(request, options, terrain, revision,
                    dependencyRevision);
            }
        }
        if (!string.Equals(Revision(request.Model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        VerifyGeometryCulvertDependencyRevision(request.Model, dependencyRevision);
        return Task.FromResult(result);
    }

    private static GeometryCulvertMapPlan BuildGeometryCulvertMapPlan(ModelContext context,
        GeometryCulvertIdentityMap identities, string operation, string[] featureIDs,
        string? targetFeatureID, GeometryCulvertCoordinate? splitPoint, string? splitMode,
        double? mapCellSize, double? mergeTolerance, double? offsetDistance,
        string? offsetSides, bool includeEndpoints, bool keepOriginal)
    {
        ObservableGeometry geometry = GeometryCulvertGeometry(context);
        ObservableCulvertBarrelLayer layer = geometry.CulvertBarrelLayer;
        IPolylineCollection polylines = layer.AsPolylineCollection();
        switch (operation)
        {
            case "split":
            {
                if (featureIDs.Length != 0)
                    throw new ArgumentException("Split preview takes its clicked barrel separately from captured source rows.");
                if (splitPoint is null || !double.IsFinite(splitPoint.X) || !double.IsFinite(splitPoint.Y)
                    || mapCellSize is not double cellSize || !double.IsFinite(cellSize) || cellSize <= 0d)
                    throw new ArgumentException("Split requires a finite map point and positive map-units-per-pixel scale.");
                if (string.IsNullOrWhiteSpace(targetFeatureID) || !identities.Barrels.ContainsKey(targetFeatureID))
                    throw new ArgumentException("Split requires the stable identity of the clicked barrel.");
                SplitMode mode = ParseSplitMode(splitMode);
                Point point = new(splitPoint.X, splitPoint.Y);
                HitTestArgs hit = HitTestArgs.Generate(point, cellSize);
                var indexed = (ISpatiallyIndexedSegmentCollection)polylines;
                List<FeatureSegmentSubset> candidates = indexed.GetSegmentSpatialIndex()
                    .GetElements(hit.QueryPoint, hit.Window.CellSize * 100d);
                var splits = new List<SplitLineResult>();
                if (mode == SplitMode.AtVertex)
                {
                    if (SnapHelpers.TrySnapToVertexOnly(hit, candidates, fid => polylines[fid],
                        out SplitLineResult vertex) != SnapMode.NoSnap)
                        splits.Add(vertex);
                }
                else
                {
                    SnapResult snapped = SnapHelpers.TrySnap(hit, candidates, fid => polylines[fid]);
                    if (snapped.Success) splits.AddRange(snapped.SplitLines);
                }
                if (splits.Count == 0)
                    return new([], [], [], [], "splitNoSnap");

                // SplitPolylineOperation applies lines in descending FID order and appends
                // before/after subsections for each line in that same order.
                splits.Sort((left, right) => right.FID.CompareTo(left.FID));
                string[] captured = splits.Select(split =>
                    IdentityFor(identities.Barrels, layer.Features[split.FID])).ToArray();
                if (!captured.Contains(targetFeatureID, StringComparer.Ordinal))
                    return new(captured, [], [], [], "targetMismatch");
                var outputs = new List<Polyline>(splits.Count * 2);
                foreach (SplitLineResult split in splits)
                {
                    Polyline source = polylines[split.FID];
                    outputs.Add(source.SubsectionBefore(split.Point));
                    outputs.Add(source.SubsectionAfter(split.Point));
                }
                if (outputs.Any(line => line.Count < 2 || !double.IsFinite(line.Length) || line.Length <= 0d))
                    return new(captured, [], [], [], "splitProducesInvalidLine");
                return new(captured, captured, [], outputs.ToArray(), null);
            }
            case "merge":
            {
                if (featureIDs.Length < 2)
                    throw new ArgumentException("Merge requires at least two selected barrel identities.");
                double tolerance = mergeTolerance ?? 5d;
                if (!double.IsFinite(tolerance) || tolerance < 0d)
                    throw new ArgumentOutOfRangeException(nameof(mergeTolerance), "Merge tolerance must be finite and nonnegative.");
                ObservableCulvertBarrel[] selected = ResolveRows(identities.Barrels, featureIDs);
                int[] fids = selected.Select(row => layer.Features.IndexOf(row)).OrderByDescending(fid => fid).ToArray();
                if (fids.Any(fid => fid < 0)) throw new ArgumentException("A selected culvert barrel is no longer in the source layer.");
                string[] captured = fids.Select(fid => IdentityFor(identities.Barrels, layer.Features[fid])).ToArray();
                Polyline[] selectedLines = fids.Select(fid => polylines[fid]).ToArray();
                PolylineMergeHelpers.MergeResult result = PolylineMergeHelpers.MergePolylines(
                    selectedLines, tolerance, MergeMode.PreferClosest);
                if (result.MergedPolylines.Count == 0)
                    return new(captured, [], captured, [], "mergeNoConnectedLines");
                string[] retained = result.UnmergedIndices.Select(index => captured[index]).ToArray();
                var retainedSet = retained.ToHashSet(StringComparer.Ordinal);
                string[] removed = captured.Where(id => !retainedSet.Contains(id)).ToArray();
                return new(captured, removed, retained, result.MergedPolylines.ToArray(), null);
            }
            case "offset":
            {
                if (featureIDs.Length != 1)
                    throw new ArgumentException("Offset requires exactly one selected barrel identity.");
                double distance = offsetDistance ?? 10d;
                if (!double.IsFinite(distance) || distance <= 0d)
                    throw new ArgumentOutOfRangeException(nameof(offsetDistance), "Offset distance must be finite and positive.");
                OffsetSides sides = ParseOffsetSides(offsetSides);
                ObservableCulvertBarrel barrel = ResolveRows(identities.Barrels, featureIDs).Single();
                int fid = layer.Features.IndexOf(barrel);
                if (fid < 0) throw new ArgumentException("The selected culvert barrel is no longer in the source layer.");
                var output = new List<Polyline>(4);
                if (!OffsetPolylineFactory.TryComputeOffsetParts(polylines[fid], distance, sides,
                    includeEndpoints, output) || output.Count == 0
                    || output.Any(line => line.Count < 2 || !double.IsFinite(line.Length) || line.Length <= 0d))
                    return new([featureIDs[0]], [], keepOriginal ? [featureIDs[0]] : [], [], "offsetUnavailable");
                return new([featureIDs[0]], keepOriginal ? [] : [featureIDs[0]],
                    keepOriginal ? [featureIDs[0]] : [], output.ToArray(), null);
            }
            default:
                throw new ArgumentException("Unsupported culvert map command: " + operation);
        }
    }

    private static bool ApplyGeometryCulvertMapCommand(ModelContext context,
        GeometryCulvertIdentityMap identities, GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "barrel")
            throw new ArgumentException("Culvert map commands apply to barrel rows only.");
        ValidatePreviewID(mutation.PreviewID);
        string[] expectedFeatureIDs = mutation.FeatureIDs;
        string[] inputs = mutation.Operation == "split" ? [] : mutation.FeatureIDs;
        GeometryCulvertMapPlan plan = BuildGeometryCulvertMapPlan(context, identities,
            mutation.Operation, inputs, mutation.TargetFeatureID, mutation.SplitPoint,
            mutation.SplitMode, mutation.MapCellSize, mutation.MergeTolerance,
            mutation.OffsetDistance, mutation.OffsetSides, mutation.IncludeEndpoints ?? false,
            mutation.KeepOriginal ?? false);
        if (plan.OutputPolylines.Length == 0) return false;
        if (!plan.CapturedFeatureIDs.SequenceEqual(expectedFeatureIDs, StringComparer.Ordinal))
            throw new SourceFileChangedException();
        string[] outputIDs = ValidateOutputDraftIDs(mutation.OutputDraftIDs,
            plan.OutputPolylines.Length, requireExactly: true);
        if (plan.RemovedFeatureIDs.Intersect(plan.RetainedFeatureIDs, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("A culvert map edit cannot both remove and retain a barrel identity.");

        string[] generatedIdentities = outputIDs.Select(id => DraftFeatureID("barrel", id)).ToArray();
        if (generatedIdentities.Any(identities.Barrels.ContainsKey))
            throw new ArgumentException("A generated culvert barrel draft identity is already in use.");

        ObservableGeometry geometry = GeometryCulvertGeometry(context);
        ObservableCulvertBarrelLayer layer = geometry.CulvertBarrelLayer;
        // Merge removes every selected row and re-adds its unmerged originals
        // after merged outputs; offset Keep Original leaves the input untouched.
        (string ID, ObservableCulvertBarrel Row)[] retainedRows = mutation.Operation == "merge" ? plan.RetainedFeatureIDs.Select(id =>
            (ID: id, Row: identities.Barrels.TryGetValue(id, out ObservableCulvertBarrel? row)
                ? row : throw new SourceFileChangedException())).ToArray()
            : Array.Empty<(string ID, ObservableCulvertBarrel Row)>();

        // Remove only source rows that the original operation replaces. Descending
        // FID order matches SplitPolylineOperation/ReplacePolylinesOperation.
        string[] rowIDsToReplace = mutation.Operation == "merge"
            ? plan.CapturedFeatureIDs : plan.RemovedFeatureIDs;
        ObservableCulvertBarrel[] removedRows = rowIDsToReplace
            .Select(id => identities.Barrels.TryGetValue(id, out ObservableCulvertBarrel? row)
                ? row : throw new SourceFileChangedException())
            .OrderByDescending(row => layer.Features.IndexOf(row)).ToArray();
        foreach (ObservableCulvertBarrel row in removedRows)
        {
            string id = IdentityFor(identities.Barrels, row);
            layer.DeleteFeature(row);
            identities.Barrels.Remove(id);
        }

        foreach ((Polyline polyline, string draftID) in plan.OutputPolylines.Zip(outputIDs))
        {
            var created = new ObservableCulvertBarrel(new CulvertBarrel
            {
                Name = "Culvert Barrel",
                Polyline = ToValidCulvertPolyline(CoordinatesFor(polyline))
            });
            layer.AddFeature(created);
            identities.Barrels.Add(DraftFeatureID("barrel", draftID), created);
        }

        // MergePolylineOperation re-adds unmerged source vector instances after
        // generated rows. The observable wrapper's cache resolves those vectors
        // back to the same CulvertBarrel objects and preserves hydraulic fields.
        foreach ((string id, ObservableCulvertBarrel row) in retainedRows)
        {
            layer.AddFeature(row);
            if (!identities.Barrels.ContainsKey(id)) identities.Barrels.Add(id, row);
        }
        return true;
    }

    private static GeometryCulvertSmartAddPreviewResponse TryComputeSmartAddSegment(
        GeometryCulvertSmartAddPreviewRequest request, GeometryCulvertSmartAddOptions options,
        ObservableTerrain terrain, string revision, string dependencyRevision,
        SmartAddSegmentWorkScope? workScope = null)
    {
        workScope?.Verify(terrain, request.Window, dependencyRevision);
        if (!ValidSmartAddOptions(options))
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "invalidOptions");
        if (request.Window.Extent is null)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "invalidRasterWindow");
        GeometryCulvertExtent windowExtent = request.Window.Extent;
        if (request.Window.Rows < 2 || request.Window.Columns < 2
            || (long)request.Window.Rows * request.Window.Columns > 8_000_000L
            || !double.IsFinite(windowExtent.MinX) || !double.IsFinite(windowExtent.MaxX)
            || !double.IsFinite(windowExtent.MinY) || !double.IsFinite(windowExtent.MaxY)
            || windowExtent.MaxX <= windowExtent.MinX || windowExtent.MaxY <= windowExtent.MinY)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "invalidRasterWindow");
        if (!double.IsFinite(request.Start.X) || !double.IsFinite(request.Start.Y)
            || !double.IsFinite(request.End.X) || !double.IsFinite(request.End.Y))
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "invalidEndpoint");

        RasterDefinition raster = workScope is null
            ? new RasterDefinition(new Extent(windowExtent.MinX, windowExtent.MaxX,
                windowExtent.MinY, windowExtent.MaxY), request.Window.Rows, request.Window.Columns)
            : workScope.GetRaster();
        int rows = raster.Rows;
        int columns = raster.Cols;
        double cellSize = raster.CellSize;
        double cellDx = (request.End.X - request.Start.X) / cellSize;
        double cellDy = (request.End.Y - request.Start.Y) / cellSize;
        double euclidPixels = Math.Sqrt(cellDx * cellDx + cellDy * cellDy);
        if (!double.IsFinite(cellSize) || cellSize <= 0d || !double.IsFinite(euclidPixels))
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "invalidRasterWindow");
        if (euclidPixels > options.MaxSegmentPixels)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "segmentTooLong");
        if (euclidPixels < 1e-6)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "segmentTooShort");

        int count = checked(rows * columns);
        float[] elevations;
        double zMin;
        double zMax;
        if (workScope is null)
        {
            elevations = new float[count];
            terrain.Layer.Resample(new ResampleArgs(raster, SamplingParams.Default), elevations.AsMemory());
            if (!TryNormalizeTerrainElevations(elevations, out zMin, out zMax))
                return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                    terrain.Name, false, [], "terrainSamplingUnavailable");
        }
        else if (!workScope.TryGetTerrainGrid(terrain, raster, out elevations, out zMin, out zMax))
        {
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "terrainSamplingUnavailable");
        }

        int startRow = ClampGridIndex((int)Math.Floor((raster.MaxY - request.Start.Y) / cellSize), rows);
        int startColumn = ClampGridIndex((int)Math.Floor((request.Start.X - raster.MinX) / cellSize), columns);
        int endRow = ClampGridIndex((int)Math.Floor((raster.MaxY - request.End.Y) / cellSize), rows);
        int endColumn = ClampGridIndex((int)Math.Floor((request.End.X - raster.MinX) / cellSize), columns);
        double endpointX = request.End.X;
        double endpointY = request.End.Y;
        if (options.Algorithm == "HighGround")
        {
            (int snappedRow, int snappedColumn) = SnapSmartAddTargetToHighest(elevations,
                rows, columns, options.HighGroundSnapRadiusCells, endRow, endColumn);
            if (snappedRow != endRow || snappedColumn != endColumn)
            {
                endRow = snappedRow;
                endColumn = snappedColumn;
                endpointX = raster.MinX + (endColumn + 0.5d) * cellSize;
                endpointY = raster.MaxY - (endRow + 0.5d) * cellSize;
            }
        }

        List<(int row, int col)> pixelPath;
        if (options.Algorithm == "HessianRidgeness")
        {
            HessianRidgenessWalker walker = workScope?.HessianWalker ?? new HessianRidgenessWalker();
            if (!walker.TryFindPath(elevations, rows, columns, cellSize, raster.MinX, raster.MaxY,
                zMin, zMax, startRow, startColumn, endRow, endColumn,
                options.HessianFrangiBeta, options.HessianFeaturePenaltyKappa,
                options.HessianOffFeatureBailMultiplier, options.HessianMinExpansionsBudget,
                options.HessianExpansionsPerEuclidPx, options.HessianMaxEuclidCells, out pixelPath))
                return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                    terrain.Name, false, [], "pathUnavailable");
        }
        else
        {
            int[] parent;
            double[] gScore;
            double[] length;
            PriorityQueue<int, double> open;
            List<(int row, int col)> pathBuffer;
            if (workScope is null)
            {
                parent = new int[count];
                gScore = new double[count];
                length = new double[count];
                open = new PriorityQueue<int, double>();
                pathBuffer = new List<(int row, int col)>();
            }
            else
            {
                workScope.GetHighGroundWorkspace(count, out parent, out gScore, out length,
                    out open, out pathBuffer);
            }
            double bailPixelLength = options.HighGroundBailRatio * euclidPixels;
            int expansionLimit = Math.Max(50000, (int)(128d * euclidPixels * options.HighGroundBailRatio));
            if (!AStarRidgeWalker.TryFindPath(elevations, rows, columns, startRow, startColumn,
                endRow, endColumn, options.HighGroundAlpha, zMin, zMax, expansionLimit,
                bailPixelLength, parent, gScore, length, open, pathBuffer, out pixelPath))
                return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                    terrain.Name, false, [], "pathUnavailable");
        }
        if (pixelPath is null || pixelPath.Count < 2)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "pathUnavailable");

        var polyline = new Polyline();
        polyline.Add(new Point(request.Start.X, request.Start.Y));
        for (int index = 1; index < pixelPath.Count - 1; index++)
        {
            (int row, int col) = pixelPath[index];
            polyline.Add(new Point(raster.MinX + (col + 0.5d) * cellSize,
                raster.MaxY - (row + 0.5d) * cellSize));
        }
        polyline.Add(new Point(endpointX, endpointY));
        Polyline smoothed = polyline.CloneWithMovingAverageSmoothing(3, 2);
        List<Point> simplified = smoothed.DouglasPeuckerReduction(options.DpTolerancePixels * cellSize);
        Point startPoint = new(request.Start.X, request.Start.Y);
        if (simplified.Count > 0 && simplified[0].EqualXY(in startPoint))
            simplified.RemoveAt(0);
        if (simplified.Count == 0)
            return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
                terrain.Name, false, [], "pathUnavailable");
        GeometryCulvertCoordinate[] segment = [new GeometryCulvertCoordinate(request.Start.X, request.Start.Y), .. simplified.Select(point =>
            new GeometryCulvertCoordinate(point.X, point.Y))];
        return new(request.PreviewID, revision, request.DraftState, dependencyRevision,
            terrain.Name, true, segment, null);
    }

    private static bool TryNormalizeTerrainElevations(float[] elevations, out double zMin, out double zMax)
    {
        zMin = double.PositiveInfinity;
        zMax = double.NegativeInfinity;
        for (int index = 0; index < elevations.Length; index++)
        {
            float elevation = elevations[index];
            if (elevation < -1e30f) elevations[index] = float.NaN;
            else if (!float.IsNaN(elevation))
            {
                if (elevation < zMin) zMin = elevation;
                if (elevation > zMax) zMax = elevation;
            }
        }
        return !double.IsInfinity(zMin) && !double.IsInfinity(zMax);
    }

    private static (int Row, int Column) SnapSmartAddTargetToHighest(float[] elevations,
        int rows, int columns, int radius, int row, int column)
    {
        int minRow = Math.Max(0, row - radius);
        int maxRow = Math.Min(rows - 1, row + radius);
        int minColumn = Math.Max(0, column - radius);
        int maxColumn = Math.Min(columns - 1, column + radius);
        float highest = float.NegativeInfinity;
        int bestRow = row;
        int bestColumn = column;
        double bestDistanceSquared = double.PositiveInfinity;
        double radiusSquared = (double)radius * radius;
        for (int candidateRow = minRow; candidateRow <= maxRow; candidateRow++)
        {
            for (int candidateColumn = minColumn; candidateColumn <= maxColumn; candidateColumn++)
            {
                int rowOffset = candidateRow - row;
                int columnOffset = candidateColumn - column;
                double distanceSquared = rowOffset * rowOffset + columnOffset * columnOffset;
                if (distanceSquared > radiusSquared) continue;
                float elevation = elevations[candidateRow * columns + candidateColumn];
                if (!float.IsNaN(elevation)
                    && (elevation > highest || (elevation == highest && distanceSquared < bestDistanceSquared)))
                {
                    highest = elevation;
                    bestRow = candidateRow;
                    bestColumn = candidateColumn;
                    bestDistanceSquared = distanceSquared;
                }
            }
        }
        return (bestRow, bestColumn);
    }

    private static bool ValidSmartAddOptions(GeometryCulvertSmartAddOptions options) =>
        options.Algorithm is "HessianRidgeness" or "HighGround"
        && double.IsFinite(options.DpTolerancePixels) && options.DpTolerancePixels >= 0d
        && double.IsFinite(options.MaxSegmentPixels) && options.MaxSegmentPixels > 0d
        && double.IsFinite(options.PreviewThrottlePixels) && options.PreviewThrottlePixels > 0d
        && double.IsFinite(options.HighGroundAlpha) && options.HighGroundAlpha >= 0d
        && double.IsFinite(options.HighGroundBailRatio) && options.HighGroundBailRatio > 0d
        && options.HighGroundSnapRadiusCells >= 0
        && double.IsFinite(options.HessianFrangiBeta) && options.HessianFrangiBeta > 0d
        && double.IsFinite(options.HessianFeaturePenaltyKappa) && options.HessianFeaturePenaltyKappa >= 0d
        && double.IsFinite(options.HessianOffFeatureBailMultiplier) && options.HessianOffFeatureBailMultiplier > 0d
        && options.HessianMinExpansionsBudget > 0
        && options.HessianExpansionsPerEuclidPx > 0
        && options.HessianMaxEuclidCells > 0;

    private static SplitMode ParseSplitMode(string? value) => value switch
    {
        null or "Anywhere" => SplitMode.Anywhere,
        "AtVertex" => SplitMode.AtVertex,
        _ => throw new ArgumentException("Unknown culvert split mode: " + value)
    };

    private static OffsetSides ParseOffsetSides(string? value) => value switch
    {
        null or "Both" => OffsetSides.Both,
        "Left" => OffsetSides.Left,
        "Right" => OffsetSides.Right,
        _ => throw new ArgumentException("Unknown culvert offset sides: " + value)
    };

    private static string[] ValidateOutputDraftIDs(string[]? values, int count, bool requireExactly)
    {
        values ??= [];
        if (requireExactly ? values.Length != count : values.Length < count)
            throw new ArgumentException("The preview does not include enough generated barrel draft identities.");
        string[] normalized = values.Take(count).Select(value => RequireDraftID(value)).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Generated barrel draft identities must be unique.");
        return normalized;
    }

    private static void ValidatePreviewID(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
            throw new ArgumentException("A culvert map preview requires a valid caller correlation ID.");
    }

    private static int ClampGridIndex(int index, int length) => Math.Clamp(index, 0, length - 1);
}
