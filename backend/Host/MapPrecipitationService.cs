using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Files;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Typed endpoints for the source-backed precipitation layer. The native
/// model resampler is the single source for inspect, frame and hover values.
/// </summary>
public static class MapPrecipitationService
{
    public static MapPrecipitationInspectResult Inspect(MapPrecipitationInspectRequest request) =>
        ModelEditorAdapter.InspectMapPrecipitation(request);

    public static MapPrecipitationFrameResult Frame(MapPrecipitationFrameRequest request) =>
        ModelEditorAdapter.RenderMapPrecipitation(request);
}

public static partial class ModelEditorAdapter
{
    private const string DirectBoundaryHdf = "directBoundaryHdf";
    private const string ResultSlotZeroHdf = "resultSlotZeroHdf";
    private const string PrecipitationKind = "precipitation";
    private const float LegacyNoData = -9999f;

    private sealed class PrecipitationCaptureScope : IDisposable
    {
        private readonly ResolvedMapResultContext context;

        internal ResolvedMapResultContext Context => context;
        internal BoundaryCondition Boundary { get; }
        internal PrecipitationLayer Layer { get; }
        internal CatalogTreeNode BoundaryRow { get; }
        internal CatalogTreeNode PrecipitationRow { get; }
        internal string SourceMode { get; }
        internal string SourcePath { get; }
        internal string SourceRevision { get; }
        internal MapResultGeometryOwner? ResultOwner { get; }
        internal bool IsChecked { get; }
        internal bool IsEffectivelyVisible { get; }
        internal string MapContextToken => context.Sources.ContextToken;
        internal string Unit { get; }
        internal ITimeSeriesResampler<float>? Resampler { get; }
        internal string Status { get; }
        internal MapPrecipitationProfile[] Profiles { get; }
        internal string TimelineKind { get; }
        internal bool IsTemporal { get; }
        internal string CaptureToken { get; }
        internal string SpatialDataType { get; }
        internal bool IsEnabled => Layer.IsEnabled;
        internal float? NoDataValue { get; }
        internal MapPrecipitationExtent? ValidExtent { get; }

        internal PrecipitationCaptureScope(ResolvedMapResultContext context,
            BoundaryCondition boundary, PrecipitationLayer layer,
            CatalogTreeNode boundaryRow, CatalogTreeNode precipitationRow,
            string sourceMode, string sourcePath, string sourceRevision,
            MapResultGeometryOwner? resultOwner, bool isChecked, bool isEffectivelyVisible,
            string unit, ITimeSeriesResampler<float>? resampler, string status,
            MapPrecipitationProfile[] profiles, string timelineKind, bool isTemporal,
            string captureToken, string spatialDataType, float? noDataValue,
            MapPrecipitationExtent? validExtent)
        {
            this.context = context;
            Boundary = boundary;
            Layer = layer;
            BoundaryRow = boundaryRow;
            PrecipitationRow = precipitationRow;
            SourceMode = sourceMode;
            SourcePath = sourcePath;
            SourceRevision = sourceRevision;
            ResultOwner = resultOwner;
            IsChecked = isChecked;
            IsEffectivelyVisible = isEffectivelyVisible;
            Unit = unit;
            Resampler = resampler;
            Status = status;
            Profiles = profiles;
            TimelineKind = timelineKind;
            IsTemporal = isTemporal;
            CaptureToken = captureToken;
            SpatialDataType = spatialDataType;
            NoDataValue = noDataValue;
            ValidExtent = validExtent;
        }

        public void Dispose() => context.Dispose();
    }

    public static MapPrecipitationInspectResult InspectMapPrecipitation(
        MapPrecipitationInspectRequest request)
    {
        ValidateInspectRequest(request);
        if (IsProtectedBeforeOpen(request.Map, request.ExpectedSourcePath,
                request.BoundaryNode.AssetID, request.PrecipitationNode.AssetID,
                request.PrecipitationNode.ParentBoundaryConditionAssetID,
                request.BoundaryNode.ParentResultAssetID,
                request.PrecipitationNode.ParentResultAssetID,
                request.ResultOwner?.SelectedResultPath))
            return ProtectedInspectResult(request);

        using PrecipitationCaptureScope scope = OpenPrecipitationCapture(request.Map,
            request.SelectionOverrides, request.BoundaryNode, request.PrecipitationNode,
            request.ExpectedSourcePath, request.ResultOwner, request.ExpectedMapContextToken,
            request.ExpectedCaptureToken, request.ExpectedSourceRevision);
        EnsureCaptureCurrent(scope);
        return new MapPrecipitationInspectResult(request.RequestID, scope.Status,
            scope.Context.ProjectPath, scope.Context.ProjectRevision, scope.Context.ProjectXmlSHA,
            scope.Context.VisualizationRevision, scope.Context.Sources.MapID,
            scope.Context.Sources.MapIndex, scope.Context.Sources.MapName,
            scope.Context.Sources.MapType, scope.MapContextToken, scope.CaptureToken,
            scope.Context.Map.CoreLayerContext?.IdentityToken ?? string.Empty,
            scope.BoundaryRow.Locator!, scope.PrecipitationRow.Locator!, scope.SourceMode,
            scope.SourcePath, scope.SourceRevision, scope.ResultOwner, scope.IsEnabled,
            scope.IsChecked, scope.IsEffectivelyVisible, scope.SpatialDataType,
            scope.TimelineKind, scope.IsTemporal, scope.Profiles.Length, scope.Profiles,
            scope.Unit, scope.NoDataValue, scope.ValidExtent);
    }

    public static MapPrecipitationFrameResult RenderMapPrecipitation(
        MapPrecipitationFrameRequest request)
    {
        ValidateFrameRequest(request);
        if (IsProtectedBeforeOpen(request.Map, request.ExpectedSourcePath,
                request.BoundaryNode.AssetID, request.PrecipitationNode.AssetID,
                request.PrecipitationNode.ParentBoundaryConditionAssetID,
                request.BoundaryNode.ParentResultAssetID,
                request.PrecipitationNode.ParentResultAssetID,
                request.ResultOwner?.SelectedResultPath))
            return ProtectedFrameResult(request);

        using PrecipitationCaptureScope scope = OpenPrecipitationCapture(request.Map,
            request.SelectionOverrides, request.BoundaryNode, request.PrecipitationNode,
            request.ExpectedSourcePath, request.ResultOwner, request.ExpectedMapContextToken,
            request.ExpectedCaptureToken, request.ExpectedSourceRevision);
        EnsureCaptureCurrent(scope);
        if (scope.Status != "ready")
            return FrameResult(request, scope, scope.Status, null);

        if ((uint)request.ProfileIndex >= (uint)scope.Profiles.Length)
            throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex));
        MapPrecipitationProfile profile = scope.Profiles[request.ProfileIndex];
        if (!string.Equals(request.ExpectedProfileTime, profile.Time, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        InterpolatedSurfaceFill fill = scope.Context.Project.Units == UnitSystem.SI
            ? SurfaceFills.PrecipMillimeters.Clone()
            : SurfaceFills.PrecipInches.Clone();
        var renderer = scope.Resampler!.AsRenderLayer();
        renderer.RasterSymbology = new RasterSymbology(fill)
        {
            InterpolationMode = InterpolationMode.Nearest
        };
        renderer.ProfileState.CurrentProfileIndex = request.ProfileIndex;
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(renderer,
            request.Viewport, projectToSource: null, renderScale: request.RenderScale);

        EnsureCaptureCurrent(scope);
        return FrameResult(request, scope, "rendered", frame);
    }

    internal static MapHoverIdentifyEntry? SampleMapHoverPrecipitation(
        MapHoverIdentifyRequest request, MapHoverLayerCapture layerCapture)
    {
        ValidateMapHoverPrecipitationCaptureShape(request, layerCapture);
        MapHoverPrecipitationCapture hover = layerCapture.Precipitation
            ?? throw new ArgumentException("The precipitation hover capture is incomplete.");
        int profileIndex = layerCapture.ProfileIndex!.Value;
        string sourcePath = RequireAbsolutePath(layerCapture.SourcePath);
        if (IsProtectedBeforeOpen(request.Map, sourcePath,
                layerCapture.Node.AssetID, layerCapture.Node.ParentBoundaryConditionAssetID,
                layerCapture.Node.ParentResultAssetID, hover.BoundaryNode.AssetID,
                hover.BoundaryNode.ParentResultAssetID, hover.ResultOwner?.SelectedResultPath))
            throw new NotSupportedException("The precipitation source is currently owned by an active writer.");

        using PrecipitationCaptureScope scope = OpenPrecipitationCapture(request.Map,
            request.SelectionOverrides, hover.BoundaryNode, layerCapture.Node,
            sourcePath, hover.ResultOwner, request.ContextToken,
            hover.CaptureToken, layerCapture.SourceRevision);
        EnsureCaptureCurrent(scope);
        if (scope.Status != "ready") return null;
        if ((uint)profileIndex >= (uint)scope.Profiles.Length ||
            !string.Equals(scope.Profiles[profileIndex].Time, hover.ExpectedProfileTime,
                StringComparison.Ordinal))
            throw new SourceFileChangedException();

        float value = scope.Resampler!.SamplePoint(new Point(request.Point.X, request.Point.Y), profileIndex);
        EnsureCaptureCurrent(scope);
        if (!float.IsFinite(value) || value == LegacyNoData ||
            scope.NoDataValue is float noData && value == noData)
            return null;
        return new MapHoverIdentifyEntry(layerCapture.Node, PrecipitationKind,
            "Precipitation", value.ToString("G9", CultureInfo.InvariantCulture), "number",
            scope.SourcePath, scope.SourceRevision, null, profileIndex, null, null, scope.Unit);
    }

    internal static void ValidateMapHoverPrecipitationCaptureShape(
        MapHoverIdentifyRequest request, MapHoverLayerCapture layerCapture)
    {
        MapHoverPrecipitationCapture? hover = layerCapture.Precipitation;
        if (layerCapture.Kind != PrecipitationKind || layerCapture.Node.NodeType != "PrecipitationLayer" ||
            hover is null || hover.BoundaryNode.NodeType != "BoundaryCondition" ||
            layerCapture.ProfileIndex is not int profileIndex || profileIndex < 0 ||
            string.IsNullOrWhiteSpace(layerCapture.SourcePath) ||
            !Path.IsPathFullyQualified(layerCapture.SourcePath) ||
            !MapHoverIdentifyService.IsSHA256(layerCapture.SourceRevision) ||
            !MapHoverIdentifyService.IsSHA256(hover.CaptureToken) ||
            string.IsNullOrWhiteSpace(hover.ExpectedProfileTime))
            throw new SourceFileChangedException();

        ValidatePrecipitationNodePaths(hover.BoundaryNode, layerCapture.Node,
            layerCapture.SourcePath, hover.ResultOwner);
        ValidateResultOwnerShape(request.Map, hover.BoundaryNode, layerCapture.Node,
            request.SelectionOverrides, hover.ResultOwner, request.ContextToken);
        if (request.Map.ComputeReadLease is { } lease)
        {
            if ((request.Map.ProjectSessionID is { Length: > 0 } projectSession &&
                 !string.Equals(projectSession, lease.ProjectSessionID, StringComparison.OrdinalIgnoreCase)) ||
                (request.Map.CoreLayerContext is { } core &&
                 !string.Equals(core.ProjectSessionID, lease.ProjectSessionID, StringComparison.OrdinalIgnoreCase)))
                throw new SourceFileChangedException();
            _ = MapResultComputeReadLeaseScope.Resolve(request.Map,
                SourceFilePaths.Canonical(request.Map.ProjectPath))
                ?? throw new SourceFileChangedException();
        }
    }

    private static PrecipitationCaptureScope OpenPrecipitationCapture(MapTreeRequest map,
        MapResultSelectionOverride[]? selectionOverrides,
        CatalogTreeNodeLocator boundaryNode,
        CatalogTreeNodeLocator precipitationNode,
        string expectedSourcePath,
        MapResultGeometryOwner? requestedOwner,
        string? expectedMapContextToken,
        string? expectedCaptureToken,
        string? expectedSourceRevision)
    {
        string? expectedContext = expectedMapContextToken;
        if (requestedOwner is { } owner)
        {
            if (owner.SelectionIndex != 0 || owner.ResultNode.NodeType is not ("Result" or "MultiResult") ||
                !string.Equals(owner.ContextToken, expectedMapContextToken ?? owner.ContextToken,
                    StringComparison.Ordinal) ||
                !SameSelectionOverrides(owner.SelectionOverrides, selectionOverrides))
                throw new SourceFileChangedException();
            expectedContext ??= owner.ContextToken;
        }
        else if (HasResultOwner(boundaryNode) || HasResultOwner(precipitationNode))
        {
            throw new SourceFileChangedException();
        }

        ResolvedMapResultContext context = MapResultChartService.ResolveContext(map,
            selectionOverrides, expectedContext);
        try
        {
            CatalogTreeNode boundaryRow;
            BoundaryCondition boundary;
            string sourceMode;
            string sourcePath;
            MapResultGeometryOwner? resolvedOwner = null;
            if (requestedOwner is { } resultOwner)
            {
                ResolvedMapResultNode selected = ResolveMapResultOwner(context,
                    resultOwner.ResultNode, resultOwner.SelectionIndex);
                RequireExactSelectedResult(selected, resultOwner.SelectedResultPath,
                    resultOwner.SelectedResultRevision);
                CatalogTreeNode resultRow = ResolveExactMapNode(context.Tree.Roots,
                    resultOwner.ResultNode);
                boundaryRow = ResolveResultOwnedChild(resultRow, boundaryNode,
                    selected.ResultPath, selected.Revision, "BoundaryCondition");
                boundary = selected.Result.BoundaryConditions
                    ?? throw new SourceFileChangedException();
                if (!string.Equals(boundary.Name, boundaryRow.Name, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                sourceMode = ResultSlotZeroHdf;
                sourcePath = selected.ResultPath;
                resolvedOwner = new MapResultGeometryOwner(resultOwner.ResultNode,
                    context.Sources.ContextToken, selectionOverrides, 0,
                    selected.ResultPath, selected.Revision);
            }
            else
            {
                boundaryRow = ResolveDirectBoundaryRow(context.Tree.Roots, boundaryNode, map);
                string boundaryPath = boundaryRow.AssetID ?? boundaryRow.Locator?.AssetID
                    ?? throw new SourceFileChangedException();
                string canonicalBoundaryPath = SourceFilePaths.Canonical(boundaryPath);
                BoundaryCondition[] matches = context.Project.BoundaryConditions.Where(condition =>
                    !string.IsNullOrWhiteSpace(condition.Filename) &&
                    SameSourcePath(ResolveProjectAssetPath(context.ProjectPath, condition.Filename),
                        canonicalBoundaryPath)).ToArray();
                if (matches.Length != 1 || !string.Equals(matches[0].Name, boundaryRow.Name,
                        StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                boundary = matches[0];
                sourceMode = DirectBoundaryHdf;
                sourcePath = canonicalBoundaryPath;
            }

            if (boundaryNode.AssetID is not { Length: > 0 } boundaryAsset ||
                !SameSourcePath(boundaryAsset, boundaryRow.AssetID ?? boundaryAsset))
                throw new SourceFileChangedException();
            if (requestedOwner is not null &&
                (!SameSourcePath(sourcePath, requestedOwner.SelectedResultPath) ||
                 !string.Equals(precipitationNode.ParentResultNodeType, requestedOwner.ResultNode.NodeType,
                     StringComparison.Ordinal) ||
                 !string.Equals(precipitationNode.ParentResultNodeName, requestedOwner.ResultNode.NodeName,
                     StringComparison.Ordinal) ||
                 !SameNodePath(precipitationNode.ParentResultNodePath,
                     requestedOwner.ResultNode.NodePath) ||
                 precipitationNode.ParentResultAssetID is not { } resultAsset ||
                 !SameSourcePath(resultAsset, requestedOwner.SelectedResultPath) ||
                 !string.Equals(precipitationNode.ParentResultRevision,
                     requestedOwner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase)))
                throw new SourceFileChangedException();

            CatalogTreeNode precipitationRow = ResolvePrecipitationChild(boundaryRow,
                precipitationNode, boundaryNode, requestedOwner);
            PrecipitationLayer layer = boundary.Precipitation
                ?? throw new SourceFileChangedException();
            string canonicalSourcePath = SourceFilePaths.Canonical(sourcePath);
            (string? stablePath, string? sourceRevision) = CatalogTreeService.TryReadStableRevision(canonicalSourcePath);
            if (stablePath is null || sourceRevision is null)
            {
                if (!File.Exists(canonicalSourcePath))
                    throw new FileNotFoundException("The exact precipitation source HDF was not found.", canonicalSourcePath);
                throw new SourceFileChangedException();
            }
            if (!SameSourcePath(expectedSourcePath, stablePath) ||
                expectedSourceRevision is { Length: > 0 } expectedRevision &&
                    !expectedRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase) ||
                requestedOwner is not null &&
                    !sourceRevision.Equals(requestedOwner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            bool isChecked = precipitationRow.Checked == "checked";
            bool isVisible = MapTreeService.IsEffectivelyVisible(context.Tree.Roots, precipitationRow);
            string spatialDataType = layer.SpatialDataType.ToString();
            string unit = DefaultUnits.FromDefault(context.Project.Units).PrecipRateUnits.Name;
            ITimeSeriesResampler<float>? resampler = null;
            MapPrecipitationProfile[] profiles = [];
            int profileCount = 0;
            float? noDataValue = null;
            MapPrecipitationExtent? validExtent = null;
            string status;
            if (!layer.IsEnabled || !isChecked || !isVisible)
            {
                status = "disabled";
            }
            else
            {
                try
                {
                    resampler = layer.Resampler;
                }
                catch (Exception error) when (error is NotImplementedException or NotSupportedException)
                {
                    status = "unsupported";
                    string unsupportedToken = PrecipitationToken(context, boundaryNode,
                        precipitationNode, sourceMode, canonicalSourcePath, sourceRevision,
                        resolvedOwner, spatialDataType, null, unit, null,
                        layer.IsEnabled, isChecked, isVisible, []);
                    ValidateExpectedCapture(expectedCaptureToken, unsupportedToken);
                    EnsureSourceCurrent(canonicalSourcePath, sourceRevision);
                    context.EnsureUnchanged();
                    return new PrecipitationCaptureScope(context, boundary, layer, boundaryRow,
                        precipitationRow, sourceMode, canonicalSourcePath, sourceRevision,
                        resolvedOwner, isChecked, isVisible, unit, null, status, [],
                        layer.SpatialDataType == SpatialDataType.Constant ? "constant" : "timeSeries",
                        layer.SpatialDataType != SpatialDataType.Constant, unsupportedToken,
                        spatialDataType, null, null);
                }
                if (resampler is null)
                {
                    status = "sourceUnavailable";
                }
                else
                {
                    IReadOnlyList<DateTime>? times = resampler.Times;
                    profileCount = resampler.ProfileCount;
                    profiles = times?.Select((time, index) => new MapPrecipitationProfile(index,
                        time.ToString("O", CultureInfo.InvariantCulture))).ToArray() ?? [];
                    float? sourceNoDataValue = layer.NoDataValue;
                    noDataValue = sourceNoDataValue is float finiteNoData && float.IsFinite(finiteNoData)
                        ? finiteNoData : null;
                    validExtent = DescribeExtent(layer.ValidExtent);
                    status = profileCount > 0 && profileCount == profiles.Length
                        ? "ready" : "noProfiles";
                }
            }

            string timelineKind = layer.SpatialDataType == SpatialDataType.Constant
                ? "constant" : "timeSeries";
            bool isTemporal = timelineKind != "constant";
            string captureToken = PrecipitationToken(context, boundaryNode, precipitationNode,
                sourceMode, canonicalSourcePath, sourceRevision, resolvedOwner,
                spatialDataType, resampler?.GetType().AssemblyQualifiedName, unit, noDataValue,
                layer.IsEnabled, isChecked, isVisible, profiles);
            ValidateExpectedCapture(expectedCaptureToken, captureToken);
            EnsureSourceCurrent(canonicalSourcePath, sourceRevision);
            context.EnsureUnchanged();
            return new PrecipitationCaptureScope(context, boundary, layer, boundaryRow,
                precipitationRow, sourceMode, canonicalSourcePath, sourceRevision,
                resolvedOwner, isChecked, isVisible, unit, resampler, status, profiles,
                timelineKind, isTemporal, captureToken, spatialDataType, noDataValue, validExtent);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static CatalogTreeNode ResolvePrecipitationChild(CatalogTreeNode boundaryRow,
        CatalogTreeNodeLocator requestedPrecipitationNode,
        CatalogTreeNodeLocator requestedBoundaryNode,
        MapResultGeometryOwner? resultOwner)
    {
        CatalogTreeNode[] children = (boundaryRow.Children ?? []).Where(child =>
            child.Locator is { } locator && SameLocator(locator, requestedPrecipitationNode)).ToArray();
        if (children.Length != 1 || requestedPrecipitationNode.NodeType != "PrecipitationLayer" ||
            requestedPrecipitationNode.ParentBoundaryConditionAssetID is not { Length: > 0 } parentAsset ||
            requestedPrecipitationNode.ParentBoundaryConditionNodeName is not { Length: > 0 } parentName ||
            !SameNodePath(requestedPrecipitationNode.ParentBoundaryConditionNodePath,
                requestedBoundaryNode.NodePath) ||
            !string.Equals(parentName, requestedBoundaryNode.NodeName, StringComparison.Ordinal) ||
            requestedBoundaryNode.AssetID is not { Length: > 0 } boundaryAsset ||
            !SameSourcePath(parentAsset, boundaryAsset) ||
            !SameSourcePath(requestedPrecipitationNode.AssetID ?? string.Empty, boundaryAsset))
            throw new SourceFileChangedException();

        if (resultOwner is null)
        {
            if (HasResultOwner(requestedPrecipitationNode)) throw new SourceFileChangedException();
        }
        else if (requestedPrecipitationNode.ParentResultAssetID is not { Length: > 0 } resultAsset ||
                 !SameSourcePath(resultAsset, resultOwner.SelectedResultPath) ||
                 !SameNodePath(requestedPrecipitationNode.ParentResultNodePath,
                     resultOwner.ResultNode.NodePath) ||
                 !string.Equals(requestedPrecipitationNode.ParentResultNodeType,
                     resultOwner.ResultNode.NodeType, StringComparison.Ordinal) ||
                 !string.Equals(requestedPrecipitationNode.ParentResultNodeName,
                     resultOwner.ResultNode.NodeName, StringComparison.Ordinal) ||
                 !string.Equals(requestedPrecipitationNode.ParentResultRevision,
                     resultOwner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceFileChangedException();
        }
        return children[0];
    }

    private static void ValidateInspectRequest(MapPrecipitationInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.BoundaryNode);
        ArgumentNullException.ThrowIfNull(request.PrecipitationNode);
        if (string.IsNullOrWhiteSpace(request.RequestID) || request.RequestID.Length > 256 ||
            request.RequestID.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.ExpectedSourcePath) ||
            !Path.IsPathFullyQualified(request.ExpectedSourcePath) ||
            request.BoundaryNode.NodeType != "BoundaryCondition" ||
            request.PrecipitationNode.NodeType != "PrecipitationLayer")
            throw new ArgumentException("The exact map precipitation inspect capture is incomplete.");
        ValidatePrecipitationNodePaths(request.BoundaryNode, request.PrecipitationNode,
            request.ExpectedSourcePath, request.ResultOwner);
        ValidateResultOwnerShape(request.Map, request.BoundaryNode, request.PrecipitationNode,
            request.SelectionOverrides, request.ResultOwner, request.ExpectedMapContextToken);
        if (request.ExpectedSourceRevision is { Length: > 0 } revision &&
            !MapHoverIdentifyService.IsSHA256(revision) ||
            request.ExpectedCaptureToken is { Length: > 0 } token &&
            !MapHoverIdentifyService.IsSHA256(token))
            throw new SourceFileChangedException();
    }

    private static void ValidateFrameRequest(MapPrecipitationFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.BoundaryNode);
        ArgumentNullException.ThrowIfNull(request.PrecipitationNode);
        if (string.IsNullOrWhiteSpace(request.RequestID) || request.RequestID.Length > 256 ||
            request.RequestID.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.ExpectedMapContextToken) ||
            string.IsNullOrWhiteSpace(request.ExpectedCaptureToken) ||
            string.IsNullOrWhiteSpace(request.ExpectedSourcePath) ||
            !Path.IsPathFullyQualified(request.ExpectedSourcePath) ||
            request.BoundaryNode.NodeType != "BoundaryCondition" ||
            request.PrecipitationNode.NodeType != "PrecipitationLayer")
            throw new ArgumentException("The exact map precipitation frame capture is incomplete.");
        if (!MapHoverIdentifyService.IsSHA256(request.ExpectedCaptureToken) ||
            !MapHoverIdentifyService.IsSHA256(request.ExpectedSourceRevision))
            throw new SourceFileChangedException();
        ValidatePrecipitationNodePaths(request.BoundaryNode, request.PrecipitationNode,
            request.ExpectedSourcePath, request.ResultOwner);
        ValidateResultOwnerShape(request.Map, request.BoundaryNode, request.PrecipitationNode,
            request.SelectionOverrides, request.ResultOwner, request.ExpectedMapContextToken);
        MapResultRenderService.ValidateViewport(request.Viewport);
        if (!double.IsFinite(request.RenderScale) || request.RenderScale <= 0 || request.RenderScale > 64)
            throw new ArgumentOutOfRangeException(nameof(request.RenderScale));
        if (request.ProfileIndex < 0 || string.IsNullOrWhiteSpace(request.ExpectedProfileTime))
            throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex));
    }

    private static void ValidatePrecipitationNodePaths(CatalogTreeNodeLocator boundaryNode,
        CatalogTreeNodeLocator precipitationNode, string expectedSourcePath,
        MapResultGeometryOwner? resultOwner)
    {
        string boundaryPath = RequireAbsolutePath(boundaryNode.AssetID);
        string precipitationPath = RequireAbsolutePath(precipitationNode.AssetID);
        string parentBoundaryPath = RequireAbsolutePath(
            precipitationNode.ParentBoundaryConditionAssetID);
        if (!SameSourcePath(boundaryPath, precipitationPath) ||
            !SameSourcePath(boundaryPath, parentBoundaryPath))
            throw new SourceFileChangedException();

        if (resultOwner is null)
        {
            if (!SameSourcePath(boundaryPath, expectedSourcePath))
                throw new SourceFileChangedException();
            return;
        }

        string selectedResultPath = RequireAbsolutePath(resultOwner.SelectedResultPath);
        if (!SameSourcePath(selectedResultPath, expectedSourcePath))
            throw new SourceFileChangedException();
        if (precipitationNode.ParentResultAssetID is { } precipitationResultPath)
            _ = RequireAbsolutePath(precipitationResultPath);
        if (boundaryNode.ParentResultAssetID is { } boundaryResultPath)
            _ = RequireAbsolutePath(boundaryResultPath);
    }

    private static string RequireAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new SourceFileChangedException();
        return path;
    }

    private static void ValidateResultOwnerShape(MapTreeRequest map,
        CatalogTreeNodeLocator boundaryNode, CatalogTreeNodeLocator precipitationNode,
        MapResultSelectionOverride[]? selectionOverrides, MapResultGeometryOwner? resultOwner,
        string? expectedContextToken)
    {
        if (resultOwner is null)
        {
            if (HasResultOwner(boundaryNode) || HasResultOwner(precipitationNode))
                throw new ArgumentException("A Result-owned precipitation row requires its exact slot-zero owner.");
            return;
        }
        if (resultOwner.SelectionIndex != 0 ||
            resultOwner.ResultNode.NodeType is not ("Result" or "MultiResult") ||
            string.IsNullOrWhiteSpace(resultOwner.ContextToken) ||
            string.IsNullOrWhiteSpace(resultOwner.SelectedResultPath) ||
            !Path.IsPathFullyQualified(resultOwner.SelectedResultPath) ||
            !MapHoverIdentifyService.IsSHA256(resultOwner.SelectedResultRevision) ||
            expectedContextToken is { Length: > 0 } expected &&
                !string.Equals(expected, resultOwner.ContextToken, StringComparison.Ordinal) ||
            !SameSelectionOverrides(resultOwner.SelectionOverrides, selectionOverrides) ||
            !string.Equals(boundaryNode.ParentResultNodeType, resultOwner.ResultNode.NodeType,
                StringComparison.Ordinal) ||
            !string.Equals(boundaryNode.ParentResultNodeName, resultOwner.ResultNode.NodeName,
                StringComparison.Ordinal) ||
            !SameNodePath(boundaryNode.ParentResultNodePath,
                resultOwner.ResultNode.NodePath) ||
            boundaryNode.ParentResultAssetID is not { } boundaryResultAsset ||
            !SameSourcePath(boundaryResultAsset, resultOwner.SelectedResultPath) ||
            !string.Equals(boundaryNode.ParentResultRevision,
                resultOwner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(precipitationNode.ParentResultNodeType,
                resultOwner.ResultNode.NodeType, StringComparison.Ordinal) ||
            !string.Equals(precipitationNode.ParentResultNodeName,
                resultOwner.ResultNode.NodeName, StringComparison.Ordinal) ||
            !SameNodePath(precipitationNode.ParentResultNodePath,
                resultOwner.ResultNode.NodePath) ||
            precipitationNode.ParentResultAssetID is not { } precipitationResultAsset ||
            !SameSourcePath(precipitationResultAsset, resultOwner.SelectedResultPath) ||
            !string.Equals(precipitationNode.ParentResultRevision,
                resultOwner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        if (map.ComputeReadLease is { } lease &&
            !string.Equals(lease.ProjectSessionID, map.ProjectSessionID ?? lease.ProjectSessionID,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static bool SameSelectionOverrides(MapResultSelectionOverride[]? left,
        MapResultSelectionOverride[]? right)
    {
        MapResultSelectionOverride[] first = left ?? [];
        MapResultSelectionOverride[] second = right ?? [];
        if (first.Length != second.Length) return false;
        return first.All(item => item is not null && second.Count(candidate => candidate is not null &&
            SameLocator(item.Node, candidate.Node) && item.SelectionIndex == candidate.SelectionIndex &&
            string.Equals(item.Mode, candidate.Mode, StringComparison.Ordinal) &&
            SameOptionalPath(item.ResultPath, candidate.ResultPath) &&
            string.Equals(item.ResultRevision, candidate.ResultRevision, StringComparison.OrdinalIgnoreCase)) == 1);
    }

    private static bool SameOptionalPath(string? left, string? right) =>
        left is null ? right is null : right is not null && SameSourcePath(left, right);

    private static bool SameNodePath(int[]? left, int[]? right) =>
        (left ?? []).SequenceEqual(right ?? []);

    private static bool HasResultOwner(CatalogTreeNodeLocator node) =>
        node.ParentResultNodeType is not null || node.ParentResultAssetID is not null ||
        node.ParentResultNodePath is not null || node.ParentResultNodeName is not null ||
        node.ParentResultRevision is not null;

    private static string ResolveProjectAssetPath(string projectPath, string filename) =>
        SourceFilePaths.Canonical(Path.IsPathFullyQualified(filename) ? filename :
            Path.Combine(Path.GetDirectoryName(projectPath) ?? throw new SourceFileChangedException(), filename));

    private static bool SameSourcePath(string left, string right) =>
        SourceFilePaths.SameSourceFile(left, right);

    private static string PrecipitationToken(ResolvedMapResultContext context,
        CatalogTreeNodeLocator boundaryNode, CatalogTreeNodeLocator precipitationNode,
        string sourceMode, string sourcePath, string sourceRevision,
        MapResultGeometryOwner? owner, string spatialDataType,
        string? resamplerType, string unit, float? noDataValue,
        bool isEnabled, bool isChecked, bool isVisible, MapPrecipitationProfile[] profiles)
    {
        string[] fields =
        [
            "map-precipitation-v1",
            context.Sources.ContextToken,
            context.Map.ProjectSessionID ?? string.Empty,
            context.Map.CoreLayerContext?.IdentityToken ?? string.Empty,
            MapResultRenderService.NodeIdentity(boundaryNode),
            MapResultRenderService.NodeIdentity(precipitationNode),
            sourceMode,
            SourceFilePaths.Canonical(sourcePath),
            sourceRevision.ToUpperInvariant(),
            owner is null ? string.Empty : MapResultRenderService.NodeIdentity(owner.ResultNode),
            owner?.ContextToken ?? string.Empty,
            owner?.SelectionIndex.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            owner?.SelectedResultPath is { } resultPath ? SourceFilePaths.Canonical(resultPath) : string.Empty,
            owner?.SelectedResultRevision?.ToUpperInvariant() ?? string.Empty,
            spatialDataType,
            resamplerType ?? string.Empty,
            unit,
            noDataValue?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            isEnabled ? "1" : "0",
            isChecked ? "1" : "0",
            isVisible ? "1" : "0",
            string.Join("\n", profiles.Select(profile =>
                profile.Index.ToString(CultureInfo.InvariantCulture) + "=" + profile.Time))
        ];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", fields))));
    }

    private static void ValidateExpectedCapture(string? expected, string actual)
    {
        if (expected is { Length: > 0 } && !string.Equals(expected, actual, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void EnsureCaptureCurrent(PrecipitationCaptureScope scope)
    {
        scope.Context.EnsureUnchanged();
        EnsureSourceCurrent(scope.SourcePath, scope.SourceRevision);
        if (scope.ResultOwner is { } owner)
            RequireExactSelectedResult(ResolveMapResultOwner(scope.Context, owner.ResultNode,
                owner.SelectionIndex), owner.SelectedResultPath, owner.SelectedResultRevision);
    }

    private static void EnsureSourceCurrent(string path, string revision)
    {
        (string? canonicalPath, string? currentRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (canonicalPath is null || currentRevision is null || !SameSourcePath(canonicalPath, path) ||
            !string.Equals(currentRevision, revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static MapPrecipitationExtent? DescribeExtent(Extent extent)
    {
        if (extent.Boundedness == Extent.State.Infinite)
            return new MapPrecipitationExtent(null, null, null, null, true);
        return extent.Boundedness == Extent.State.Finite &&
            double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY) &&
            double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY) &&
            extent.MinX < extent.MaxX && extent.MinY < extent.MaxY
            ? new MapPrecipitationExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY, false)
            : null;
    }

    private static bool IsProtectedBeforeOpen(MapTreeRequest map, params string?[] sourcePaths)
    {
        if (map.ComputeReadLease is not { } lease) return false;
        if ((map.ProjectSessionID is { Length: > 0 } projectSession &&
             !string.Equals(projectSession, lease.ProjectSessionID, StringComparison.OrdinalIgnoreCase)) ||
            (map.CoreLayerContext is { } core &&
             !string.Equals(core.ProjectSessionID, lease.ProjectSessionID, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();
        MapResultComputeReadLeaseScope scope = MapResultComputeReadLeaseScope.Resolve(map,
            SourceFilePaths.Canonical(map.ProjectPath)) ?? throw new SourceFileChangedException();
        return sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Any(path => scope.IsProtected(path!));
    }

    private static MapPrecipitationInspectResult ProtectedInspectResult(
        MapPrecipitationInspectRequest request)
    {
        string sourcePath = SourceFilePaths.Canonical(request.ExpectedSourcePath);
        string sourceMode = request.ResultOwner is null ? DirectBoundaryHdf : ResultSlotZeroHdf;
        return new MapPrecipitationInspectResult(request.RequestID, "writerProtected",
            SourceFilePaths.Canonical(request.Map.ProjectPath), string.Empty,
            request.Map.ProjectXmlSHA ?? string.Empty,
            request.Map.ExpectedVisualizationRevision ?? string.Empty,
            request.Map.MapID, request.Map.MapIndex, request.Map.MapName, request.Map.MapType,
            request.ExpectedMapContextToken ?? request.ResultOwner?.ContextToken ?? string.Empty,
            string.Empty, request.Map.CoreLayerContext?.IdentityToken ?? string.Empty,
            request.BoundaryNode, request.PrecipitationNode, sourceMode, sourcePath,
            request.ExpectedSourceRevision ?? string.Empty, request.ResultOwner, false, false,
            false, string.Empty, "timeSeries", false, 0, [], string.Empty, null, null);
    }

    private static MapPrecipitationFrameResult ProtectedFrameResult(
        MapPrecipitationFrameRequest request) =>
        new(request.RequestID, "writerProtected", request.ExpectedMapContextToken,
            request.ExpectedCaptureToken, SourceFilePaths.Canonical(request.ExpectedSourcePath),
            request.ExpectedSourceRevision, request.ProfileIndex, request.ExpectedProfileTime,
            string.Empty, null);

    private static MapPrecipitationFrameResult FrameResult(
        MapPrecipitationFrameRequest request, PrecipitationCaptureScope scope,
        string status, MapSourceRasterFrame? frame) =>
        new(request.RequestID, status, scope.MapContextToken, scope.CaptureToken,
            scope.SourcePath, scope.SourceRevision, request.ProfileIndex,
            (uint)request.ProfileIndex < (uint)scope.Profiles.Length
                ? scope.Profiles[request.ProfileIndex].Time : request.ExpectedProfileTime,
            scope.Unit, frame);
}
