using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Ras.Mapping;
using Ras.Rendering.RenderLayers;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Samples values from the source objects used by the map renderers. The map
/// canvas supplies exact visible node captures; rendered RGBA is never used as
/// a substitute for hydraulic or raster data.
/// </summary>
public static class MapHoverIdentifyService
{
    private const int MaximumCaptures = 256;
    private const int MaximumAssociatedDependencies = 128;

    public static async Task<MapHoverIdentifyResult> Identify(MapHoverIdentifyRequest request)
    {
        ValidateRequest(request);
        RejectProtectedCapturePaths(request);

        // Associated values and model-vector callbacks validate their own
        // geometry/plan/result owners. Finish those reads before opening the
        // result context below so two Project objects do not retain the same
        // HDF source simultaneously. The outer visible tree is checked after.
        var ownedSourceEntries = new Dictionary<int, MapHoverIdentifyEntry?>();
        for (int index = 0; index < request.Layers.Length; index++)
        {
            MapHoverLayerCapture capture = request.Layers[index];
            if (capture.Kind is "nvalue" or "classification")
                ownedSourceEntries[index] = SampleAssociatedValue(request, capture);
            else if (capture.Kind == "modelVector")
                ownedSourceEntries[index] = await ModelEditorAdapter.SampleMapHoverModelVectorAsync(
                    request, capture);
            else if (capture.Kind == "precipitation")
                ownedSourceEntries[index] = ModelEditorAdapter.SampleMapHoverPrecipitation(
                    request, capture);
        }

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.SelectionOverrides, request.ContextToken);
        CatalogTreeNode[] treeNodes = Flatten(context.Tree.Roots).ToArray();
        var rows = new CatalogTreeNode[request.Layers.Length];
        var nodeKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < request.Layers.Length; index++)
        {
            MapHoverLayerCapture capture = request.Layers[index];
            if (capture is null || capture.Node is null ||
                !nodeKeys.Add(MapResultRenderService.NodeIdentity(capture.Node) + "|" + capture.Kind))
                throw new ArgumentException("Hover captures must identify distinct exact map nodes.", nameof(request.Layers));
            CatalogTreeNode[] matches = treeNodes.Where(node => node.Locator is { } locator &&
                MapResultRenderService.SameLocator(locator, capture.Node)).ToArray();
            bool draftVisibilityOverride = capture.Kind == "mesh" && capture.Mesh is
                { FlowAreaVisibilityOverride: true, OwnerMode: "parentDraft" };
            if (matches.Length != 1 || (!draftVisibilityOverride &&
                    !MapTreeService.IsEffectivelyVisible(context.Tree.Roots, matches[0])))
                throw new SourceFileChangedException();
            rows[index] = matches[0];
            ValidateCapture(context, request, capture, matches[0]);
        }

        MapDynamicProfileResult? dynamicValues = null;
        if (request.Layers.Any(capture => capture.Kind == "terrain"))
        {
            var line = new MapDynamicProfileLine(request.RequestID,
                [new(request.Point.X, request.Point.Y), new(request.ProbePoint.X, request.ProbePoint.Y)]);
            dynamicValues = MapDynamicProfileService.Sample(new MapDynamicProfileRequest(
                request.Map, request.ContextToken, request.SelectionOverrides, [line], request.RequestID,
                TargetSampleCount: 1, PlotTerrains: request.Layers.Any(capture => capture.Kind == "terrain"),
                PlotResults: false,
                TerrainRequiresElevationResult: false, MapType: null,
                ProfileSelections: request.ProfileSelections ?? []));
            ValidateDynamicResponse(context, request, dynamicValues);
        }

        var entries = new List<MapHoverIdentifyEntry>();
        for (int index = 0; index < request.Layers.Length; index++)
        {
            MapHoverLayerCapture capture = request.Layers[index];
            CatalogTreeNode row = rows[index];
            switch (capture.Kind)
            {
                case "result":
                    var resultLine = new MapDynamicProfileLine(request.RequestID,
                        [new(request.Point.X, request.Point.Y), new(request.ProbePoint.X, request.ProbePoint.Y)]);
                    MapDynamicProfileResult resultValues = MapDynamicProfileService.SampleForMapNode(
                        new MapDynamicProfileRequest(request.Map, request.ContextToken,
                            request.SelectionOverrides, [resultLine], request.RequestID,
                            TargetSampleCount: 1, PlotTerrains: false, PlotResults: true,
                            TerrainRequiresElevationResult: false, MapType: capture.MapType,
                            ProfileSelections: request.ProfileSelections ?? []), capture.Node);
                    ValidateDynamicResponse(context, request, resultValues);
                    AddDynamicEntries(context, request, capture, resultValues, entries);
                    break;
                case "terrain":
                    AddDynamicEntries(context, request, capture, dynamicValues!, entries);
                    break;
                case "raster":
                    MapHoverIdentifyEntry? raster = SampleRaster(context, request, capture, row);
                    if (raster is not null) entries.Add(raster);
                    break;
                case "nvalue":
                case "classification":
                case "modelVector":
                case "precipitation":
                    if (ownedSourceEntries[index] is { } owned) entries.Add(owned);
                    break;
                case "mesh":
                    MapHoverIdentifyEntry? mesh = await ModelEditorAdapter.SampleMapHoverMeshAsync(
                        request, capture);
                    if (mesh is not null) entries.Add(mesh);
                    break;
                default:
                    throw new ArgumentException("The hover source kind is not supported.", nameof(request.Layers));
            }
        }

        context.EnsureUnchanged();
        // These owners were read before the outer Project was opened. Recheck
        // them after every later asynchronous sample as well, so a source
        // replacement during another layer's read cannot publish an old label.
        foreach (MapHoverLayerCapture capture in request.Layers)
        {
            if (capture.Kind == "modelVector" && capture.ModelVector is { } vector)
                MapResultChartService.EnsureUnchanged(vector.SourcePath, vector.SourceRevision);
            else if (capture.Kind == "precipitation")
                MapResultChartService.EnsureUnchanged(capture.SourcePath!, capture.SourceRevision!);
            else if (capture.Kind is "nvalue" or "classification")
            {
                MapResultChartService.EnsureUnchanged(capture.SourcePath!, capture.SourceRevision!);
                foreach (AssociatedMapLayerDependency dependency in capture.Associated!.Dependencies)
                    MapResultChartService.EnsureUnchanged(dependency.Path, dependency.Revision);
            }
        }
        return new MapHoverIdentifyResult(request.RequestID, context.Sources.ProjectPath,
            context.Sources.ProjectRevision, context.Sources.ProjectXmlSHA,
            context.Sources.VisualizationRevision, context.Sources.MapID,
            context.Sources.MapIndex, context.Sources.MapName, context.Sources.MapType,
            context.Sources.ContextToken, request.Point, entries.ToArray());
    }

    private static void ValidateRequest(MapHoverIdentifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        if (string.IsNullOrWhiteSpace(request.ContextToken) || request.ContextToken.Length > 512 ||
            string.IsNullOrWhiteSpace(request.RequestID) || request.RequestID.Length > 256 ||
            request.RequestID.Any(char.IsControl) || request.Point is null || request.ProbePoint is null ||
            !double.IsFinite(request.Point.X) || !double.IsFinite(request.Point.Y) ||
            !double.IsFinite(request.ProbePoint.X) || !double.IsFinite(request.ProbePoint.Y) ||
            request.Point.X == request.ProbePoint.X && request.Point.Y == request.ProbePoint.Y ||
            !double.IsFinite(request.WorldUnitsPerScreenPoint) || request.WorldUnitsPerScreenPoint <= 0 ||
            request.Layers is null || request.Layers.Length > MaximumCaptures)
            throw new ArgumentException("The exact map hover capture is incomplete or outside its supported bounds.");
        if (request.ProfileSelections is { Length: > 512 })
            throw new ArgumentException("A hover capture may select at most 512 Result profiles.", nameof(request.ProfileSelections));
        if ((request.ProfileSelections ?? []).Any(selection => selection is null || selection.Node is null) ||
            (request.ProfileSelections ?? []).Select(selection => MapResultRenderService.NodeIdentity(selection.Node))
                .Distinct(StringComparer.Ordinal).Count() != (request.ProfileSelections ?? []).Length)
            throw new ArgumentException("Each visible Result node may have only one exact profile capture.", nameof(request.ProfileSelections));
        if (request.Layers.Any(capture => capture is null || capture.Node is null ||
                string.IsNullOrWhiteSpace(capture.Kind)) ||
            request.Layers.Select(capture => MapResultRenderService.NodeIdentity(capture.Node) + "|" + capture.Kind)
                .Distinct(StringComparer.Ordinal).Count() != request.Layers.Length)
            throw new ArgumentException("Each hover capture must identify one distinct exact map node.", nameof(request.Layers));
        foreach (MapHoverLayerCapture capture in request.Layers.Where(capture => capture.Kind == "precipitation"))
            ModelEditorAdapter.ValidateMapHoverPrecipitationCaptureShape(request, capture);
    }

    private static void ValidateCapture(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, MapHoverLayerCapture capture, CatalogTreeNode row)
    {
        switch (capture.Kind)
        {
            case "result":
            {
                if (capture.Node.NodeType is not ("Result" or "MultiResult") ||
                    capture.ProfileIndex is null ||
                    MapResultRenderService.ParseSupportedMapType(capture.MapType) is not { } mapType ||
                    request.ProfileSelections is null || request.ProfileSelections.Count(selection =>
                        MapResultRenderService.SameLocator(selection.Node, capture.Node) &&
                        selection.ProfileIndex == capture.ProfileIndex.Value) != 1)
                    throw new SourceFileChangedException();

                MapResultActiveNode[] matches = context.Sources.ActiveNodes.Where(node =>
                    MapResultRenderService.SameLocator(node.Node, capture.Node)).ToArray();
                if (matches.Length != 1 || matches[0].SelectedMapType != capture.MapType)
                    throw new SourceFileChangedException();
                var selected = matches[0].Selections.Where(selection => selection.SelectedResult is not null)
                    .Select(selection => (selection.SelectionIndex, Choice: selection.SelectedResult!)).ToArray();
                MapHoverResultSelectionCapture[] supplied = capture.ResultSelections ?? [];
                if (selected.Length == 0 || selected.Length != supplied.Length || selected.Any(selection =>
                    supplied.Count(item => item.SelectionIndex == selection.SelectionIndex &&
                        SamePath(item.ResultPath, selection.Choice.ResultPath) &&
                        item.ResultRevision.Equals(selection.Choice.Revision, StringComparison.OrdinalIgnoreCase)) != 1))
                    throw new SourceFileChangedException();
                _ = mapType;
                break;
            }
            case "terrain":
                if (capture.Node.NodeType != "Terrain" || capture.Node.AssociationKind != "Terrain" ||
                    row.Checked != "checked" || string.IsNullOrWhiteSpace(capture.SourcePath) ||
                    string.IsNullOrWhiteSpace(capture.SourceRevision) ||
                    !SamePath(ResolveProjectAssetPath(context.Project, capture.Node.AssetID ?? row.AssetID ?? ""),
                        capture.SourcePath))
                    throw new SourceFileChangedException();
                break;
            case "raster":
                if (capture.Node.NodeType != "MapLayer" || capture.RasterSource is null ||
                    capture.MapType != capture.RasterSource.MapLayerType)
                    throw new SourceFileChangedException();
                ValidateRasterCapture(context, request, row, capture);
                break;
            case "nvalue":
            case "classification":
                if (capture.Node.NodeType != (capture.Kind == "nvalue" ? "NValue" : "Classification") ||
                    capture.Node.AssociationKind != capture.Node.NodeType ||
                    capture.Associated is null || string.IsNullOrWhiteSpace(capture.SourcePath) ||
                    string.IsNullOrWhiteSpace(capture.SourceRevision) ||
                    !SamePath(ResolveProjectAssetPath(context.Project,
                        capture.Node.AssociationAssetID ?? capture.Node.AssetID ?? ""), capture.SourcePath) ||
                    row.Checked != "checked")
                    throw new SourceFileChangedException();
                ValidateAssociatedCapture(capture.Associated);
                break;
            case "mesh":
                if (capture.Mesh is null || !MapResultRenderService.SameLocator(
                        capture.Node, capture.Mesh.Node))
                    throw new SourceFileChangedException();
                break;
            case "modelVector":
                if (capture.ModelVector is null ||
                    !MapResultRenderService.SameLocator(capture.Node, capture.ModelVector.Node) ||
                    capture.Node.NodeType is not ("CulvertBarrelLayer" or "BoundaryCondition"))
                    throw new SourceFileChangedException();
                break;
            case "precipitation":
                if (capture.Node.NodeType != "PrecipitationLayer" ||
                    capture.Precipitation is not { } precipitation ||
                    precipitation.BoundaryNode.NodeType != "BoundaryCondition" ||
                    capture.ProfileIndex is null ||
                    string.IsNullOrWhiteSpace(capture.SourcePath) ||
                    !IsSHA256(capture.SourceRevision) || !IsSHA256(precipitation.CaptureToken) ||
                    string.IsNullOrWhiteSpace(precipitation.ExpectedProfileTime) ||
                    (precipitation.ResultOwner is { } owner &&
                        !string.Equals(owner.ContextToken, request.ContextToken, StringComparison.Ordinal)))
                    throw new SourceFileChangedException();
                break;
            default:
                throw new ArgumentException("The hover source kind is not supported.", nameof(capture.Kind));
        }
    }

    private static void ValidateRasterCapture(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, CatalogTreeNode row, MapHoverLayerCapture capture)
    {
        RasterMapSourceIdentity source = capture.RasterSource!;
        if (source.SourceMapID is null || !Guid.TryParse(context.Sources.MapID, out Guid currentMapID) ||
            !Guid.TryParse(source.SourceMapID, out Guid sourceMapID) || sourceMapID != currentMapID ||
            !SamePath(source.ProjectPath, context.ProjectPath) ||
            !SamePath(source.VisualizationPath, context.VisualizationPath) ||
            !string.Equals(source.VisualizationRevision, context.VisualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            source.MapIndex != context.Sources.MapIndex || source.MapName != context.Sources.MapName ||
            source.MapType != context.Sources.MapType || source.NodePath is not { Length: > 0 } ||
            source.MapLayerType is not (MapRasterLayerRendering.SingleBandMapType or
                MapRasterLayerRendering.DiscreteMapType or MapRasterLayerRendering.ImageMapType) ||
            row.Locator is not { NodeType: "MapLayer" } locator ||
            locator.NodePath is null || !locator.NodePath.SequenceEqual(source.NodePath) ||
            locator.NodeName != source.NodeName || locator.MapType != source.MapLayerType ||
            !string.Equals(locator.Filename, source.Filename, StringComparison.Ordinal) ||
            !string.Equals(locator.FilenameRelative, source.FilenameRelative, StringComparison.Ordinal) ||
            !source.Id.Equals($"vis-map-{source.MapIndex}-node-{string.Join('.', source.NodePath)}", StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (source.GdbSource is { } gdbSource)
        {
            ValidateGdbRasterCapture(context, request, row, capture, source, gdbSource);
            return;
        }

        string resolved = ResolveRasterPath(context.ProjectPath, locator.FilenameRelative, locator.Filename);
        if (!SamePath(resolved, source.SourcePath) || string.IsNullOrWhiteSpace(source.SourceRevision) ||
            string.IsNullOrWhiteSpace(capture.SourcePath) || !SamePath(capture.SourcePath, source.SourcePath) ||
            !string.Equals(capture.SourceRevision, source.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(MapRasterLayerSourceManifest.CaptureRevision(resolved), source.SourceRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static MapHoverIdentifyEntry? SampleRaster(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, MapHoverLayerCapture capture, CatalogTreeNode row)
    {
        RasterMapSourceIdentity source = capture.RasterSource!;
        if (source.GdbSource is { } gdbSource)
            return SampleGdbRaster(request, capture, row, source, gdbSource);

        string path = ResolveRasterPath(context.ProjectPath, row.Locator!.FilenameRelative, row.Locator.Filename);
        if (!SamePath(path, source.SourcePath) ||
            !string.Equals(MapRasterLayerSourceManifest.CaptureRevision(path), source.SourceRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Projection? targetProjection = ProjectProjection(request.Map.ProjectXml);
        using MapRasterLayerRendering.IDisposableRenderer renderer = MapRasterLayerRendering.Open(
            path, source.MapLayerType, targetProjection);
        string? value = null;
        string? valueType = null;
        if (renderer.Layer is RasterRenderLayer continuous)
        {
            float sample = continuous.Raster.SamplePoint(new Point(request.Point.X, request.Point.Y));
            float? noData = continuous.Raster.NoDataValue;
            if (float.IsFinite(sample) && (!noData.HasValue || sample != noData.Value) && sample != -9999f)
            {
                value = sample.ToString("G7", CultureInfo.InvariantCulture);
                valueType = "number";
            }
        }
        else if (renderer.Layer is DiscreteRasterRenderLayer or UInt32DiscreteRasterRenderLayer)
        {
            // This retained discrete-raster renderer ignores its legacy ref
            // cache. Keep the request independent while satisfying the API's
            // non-nullable ref contract.
            object hitCache = new object();
            List<MapHover>? hits = renderer.Layer.MapHover(
                HitTestArgs.Generate(new Point(request.Point.X, request.Point.Y),
                    request.WorldUnitsPerScreenPoint), ref hitCache);
            string? label = hits?.FirstOrDefault()?.Label;
            if (!string.IsNullOrWhiteSpace(label))
            {
                value = label;
                valueType = long.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? "integer" : "category";
            }
        }

        if (!string.Equals(MapRasterLayerSourceManifest.CaptureRevision(path), source.SourceRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (value is null)
        {
            // The retained RGB renderer has no MapHover scalar contract.
            // Identify its exact source field without inferring values from
            // rendered bytes; scalar no-data remains silent.
            return renderer.Layer is ImageRenderLayer
                ? new(capture.Node, "raster", row.Name, null, "image",
                    source.SourcePath, source.SourceRevision, source.MapLayerType, null, null, null)
                : null;
        }
        return new(capture.Node, "raster", $"{row.Name}: {value}", value, valueType,
            source.SourcePath, source.SourceRevision, source.MapLayerType, null, null, null);
    }

    private static void ValidateGdbRasterCapture(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, CatalogTreeNode row, MapHoverLayerCapture capture,
        RasterMapSourceIdentity source, GdbRasterSymbologyIdentity gdbSource)
    {
        MapTreeRequest map = gdbSource.Map;
        CatalogTreeNodeLocator node = gdbSource.Node;
        GdbMapSourceIdentity directory = gdbSource.Source;
        CatalogTreeNodeLocator locator = row.Locator!;
        int[]? nodePath = source.NodePath;
        if (!MapRasterLayerRendering.SupportedTypes.Contains(source.MapLayerType, StringComparer.Ordinal) ||
            !string.Equals(capture.MapType, source.MapLayerType, StringComparison.Ordinal) ||
            nodePath is null || node.NodePath is null || !nodePath.SequenceEqual(node.NodePath) ||
            source.Id != $"vis-map-{source.MapIndex}-node-{string.Join('.', nodePath)}" ||
            source.NodeName != node.NodeName || !MapResultRenderService.SameLocator(node, locator) ||
            node.NodeType != "MapLayer" || node.MapType != source.MapLayerType ||
            string.IsNullOrWhiteSpace(directory.SourcePath) || string.IsNullOrWhiteSpace(source.SourcePath) ||
            string.IsNullOrWhiteSpace(capture.SourcePath) || string.IsNullOrWhiteSpace(node.AssetID) ||
            !SamePath(source.SourcePath, directory.SourcePath) ||
            !SamePath(capture.SourcePath, directory.SourcePath) ||
            string.IsNullOrWhiteSpace(source.SourceRevision) ||
            !IsSHA256(directory.Revision) ||
            !source.SourceRevision.Equals(directory.Revision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(capture.SourceRevision, directory.Revision, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(node.AssetID, directory.SourcePath) ||
            !string.Equals(source.Filename, node.Filename, StringComparison.Ordinal) ||
            !string.Equals(source.FilenameRelative, node.FilenameRelative, StringComparison.Ordinal) ||
            !SamePath(map.ProjectPath, context.ProjectPath) ||
            !string.Equals(map.ProjectXml, request.Map.ProjectXml, StringComparison.Ordinal) ||
            !string.Equals(map.ProjectXmlSHA, context.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !SameMapID(map.MapID, context.Sources.MapID) ||
            map.MapIndex != context.Sources.MapIndex || map.MapName != context.Sources.MapName ||
            map.MapType != context.Sources.MapType ||
            !string.Equals(map.ExpectedVisualizationRevision, context.VisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            !SameReadLease(map.ComputeReadLease, request.Map.ComputeReadLease) ||
            !SameMapID(source.SourceMapID ?? string.Empty, context.Sources.MapID) ||
            !SamePath(source.ProjectPath, context.ProjectPath) ||
            !SamePath(source.VisualizationPath, context.VisualizationPath) ||
            !string.Equals(source.VisualizationRevision, context.VisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            source.MapIndex != context.Sources.MapIndex || source.MapName != context.Sources.MapName ||
            source.MapType != context.Sources.MapType ||
            source.MapIndex != map.MapIndex || source.MapName != map.MapName || source.MapType != map.MapType ||
            !string.Equals(source.VisualizationRevision, map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Filename, locator.Filename, StringComparison.Ordinal) ||
            !string.Equals(source.FilenameRelative, locator.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static MapHoverIdentifyEntry? SampleGdbRaster(MapHoverIdentifyRequest request,
        MapHoverLayerCapture capture, CatalogTreeNode row, RasterMapSourceIdentity source,
        GdbRasterSymbologyIdentity gdbSource)
    {
        if (source.MapLayerType != MapRasterLayerRendering.SingleBandMapType)
        {
            return GdbRasterSourceService.WithOpenedDataset(
                new GdbRasterSourceLoadRequest(gdbSource.Map, gdbSource.Node, gdbSource.Source), opened =>
                {
                    using MapRasterLayerRendering.IDisposableRenderer renderer = MapRasterLayerRendering.Open(
                        opened.Subdataset.ConnectionString, source.MapLayerType, ProjectProjection(request.Map.ProjectXml));
                    object cache = new object();
                    List<MapHover>? hits = renderer.Layer.MapHover(
                        HitTestArgs.Generate(new Point(request.Point.X, request.Point.Y),
                            request.WorldUnitsPerScreenPoint), ref cache);
                    string? label = hits?.FirstOrDefault()?.Label;
                    if (string.IsNullOrWhiteSpace(label)) return null;
                    return new MapHoverIdentifyEntry(capture.Node, "raster", $"{row.Name}: {label}", label,
                        long.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? "integer" : "category",
                        opened.Capture.Source.SourcePath, opened.Capture.Source.Revision,
                        source.MapLayerType, null, null, null);
                });
        }
        return GdbRasterSourceService.WithOpenedRasterSource(
            new GdbRasterSourceLoadRequest(gdbSource.Map, gdbSource.Node, gdbSource.Source), opened =>
            {
                IResample<float> raster = opened.Raster;
                Projection? targetProjection = ProjectProjection(request.Map.ProjectXml);
                Projection? sourceProjection = string.IsNullOrWhiteSpace(opened.ProjectionWKT)
                    ? null : new WKTProjection(opened.ProjectionWKT);
                if (targetProjection is not null && sourceProjection is not null &&
                    !targetProjection.IsEqual(sourceProjection))
                    raster = raster.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));

                float sample = raster.SamplePoint(new Point(request.Point.X, request.Point.Y));
                float? noData = opened.Raster.NoDataValue;
                if (!float.IsFinite(sample) || sample == -9999f || noData.HasValue && sample == noData.Value)
                    return null;

                string value = sample.ToString("G7", CultureInfo.InvariantCulture);
                return new MapHoverIdentifyEntry(capture.Node, "raster", $"{row.Name}: {value}", value,
                    "number", opened.Capture.Source.SourcePath, opened.Capture.Source.Revision,
                    source.MapLayerType, null, null, null);
            });
    }

    private static bool SameReadLease(MapResultComputeReadLease? left, MapResultComputeReadLease? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.ProtectedSourcePaths is null || right.ProtectedSourcePaths is null) return false;
        return string.Equals(left.ProjectSessionID, right.ProjectSessionID, StringComparison.Ordinal) &&
            string.Equals(left.RunID, right.RunID, StringComparison.Ordinal) &&
            SamePath(left.ProjectPath, right.ProjectPath) &&
            SameOptionalPath(left.CallbackResultPath, right.CallbackResultPath) &&
            left.ProtectedSourcePaths.Length == right.ProtectedSourcePaths.Length &&
            left.ProtectedSourcePaths.Select(SourceFilePaths.Canonical).OrderBy(path => path, PathComparer)
                .SequenceEqual(right.ProtectedSourcePaths.Select(SourceFilePaths.Canonical)
                    .OrderBy(path => path, PathComparer), PathComparer);
    }

    private static bool SameOptionalPath(string? left, string? right) =>
        left is null ? right is null : right is not null && SamePath(left, right);

    private static MapHoverIdentifyEntry? SampleAssociatedValue(MapHoverIdentifyRequest request,
        MapHoverLayerCapture capture)
    {
        MapHoverAssociatedCapture owners = capture.Associated
            ?? throw new ArgumentException("The exact associated-layer ownership capture is missing.");
        ValidateAssociatedCapture(owners);
        MapGeometryViewport viewport = PointSampleViewport(request.Point, request.WorldUnitsPerScreenPoint);
        AssociatedMapLayersSnapshot snapshot = AssociatedMapLayersService.Load(new AssociatedMapLayersRequest(
            request.Map, owners.GeometryNodes, [capture.Node], viewport, 1, 1,
            PlanNodes: owners.PlanNodes, ResultOwners: owners.ResultOwners));
        if (snapshot.MapID != request.Map.MapID || snapshot.MapIndex != request.Map.MapIndex ||
            snapshot.MapName != request.Map.MapName || snapshot.MapType != request.Map.MapType ||
            !SamePath(snapshot.ProjectCanonicalPath, request.Map.ProjectPath) ||
            !string.Equals(snapshot.ProjectXmlSHA, request.Map.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.VisualizationRevision, request.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        AssociatedMapLayerSnapshot[] exactLayers = snapshot.Layers.Where(layer =>
            MapResultRenderService.SameLocator(layer.Locator, capture.Node)).ToArray();
        if (exactLayers.Length != 1 || exactLayers[0].Status != "available" ||
            !exactLayers[0].IsChecked || !exactLayers[0].EffectiveVisible ||
            !SamePath(exactLayers[0].AssetPath, capture.SourcePath!) ||
            !exactLayers[0].Revision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !SameDependencies(exactLayers[0].Dependencies, owners.Dependencies))
            throw new SourceFileChangedException();

        Project? project = null;
        try
        {
            string projectPath = Path.GetFullPath(request.Map.ProjectPath);
            XElement? xml = string.IsNullOrWhiteSpace(request.Map.ProjectXml)
                ? null : XElement.Parse(request.Map.ProjectXml, LoadOptions.PreserveWhitespace);
            project = CoreLayerImportService.OpenProjectWithMetadata(projectPath, xml,
                request.Map.CoreLayerContext);
            MapHoverIdentifyEntry? result;
            Point[] points = [new(request.Point.X, request.Point.Y)];
            switch (capture.Kind)
            {
                case "nvalue":
                {
                    NValueLayer[] matches = project.NValues.Where(layer =>
                        SamePath(ResolveProjectAssetPath(project, layer.Filename), capture.SourcePath!) &&
                        layer.Name == capture.Node.NodeName).ToArray();
                    if (matches.Length != 1 || matches[0].Mode is not
                        (NValueSource.Constant or NValueSource.ClassificationLayer))
                        throw new SourceFileChangedException();
                    float[] samples = new float[1];
                    matches[0].SamplePoints(points, samples);
                    float noData = matches[0].NoDataValue ?? -9999f;
                    result = !float.IsFinite(samples[0]) || samples[0] == noData || samples[0] == -9999f
                        ? null
                        : new(capture.Node, "nvalue", $"{capture.Node.NodeName}: {samples[0].ToString("G7", CultureInfo.InvariantCulture)}",
                            samples[0].ToString("G7", CultureInfo.InvariantCulture), "number",
                            capture.SourcePath, capture.SourceRevision, null, null, null, null);
                    break;
                }
                case "classification":
                {
                    ClassificationLayer[] matches = project.ClassificationLayers.Where(layer =>
                        SamePath(ResolveProjectAssetPath(project, layer.Filename), capture.SourcePath!) &&
                        layer.Name == capture.Node.NodeName).ToArray();
                    if (matches.Length != 1) throw new SourceFileChangedException();
                    var renderer = new ClassificationRenderLayer(matches[0]);
                    // ClassificationRenderLayer inherits the discrete
                    // renderer's MapHover implementation, which ignores its
                    // legacy ref cache.
                    object hitCache = new object();
                    List<MapHover>? hits = renderer.MapHover(HitTestArgs.Generate(
                        new Point(request.Point.X, request.Point.Y), request.WorldUnitsPerScreenPoint), ref hitCache);
                    string? value = hits?.FirstOrDefault()?.Label;
                    result = string.IsNullOrWhiteSpace(value) ? null : new(capture.Node,
                        "classification", $"{capture.Node.NodeName}: {value}", value,
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                            ? "integer" : "category", capture.SourcePath, capture.SourceRevision,
                        null, null, null, null);
                    break;
                }
                default:
                    throw new ArgumentException("The associated value kind is unsupported.", nameof(capture.Kind));
            }

            VerifyAssociatedDependencies(snapshot.ProjectRevision, snapshot.VisualizationRevision,
                snapshot.ProjectCanonicalPath, exactLayers[0].Dependencies);
            return result;
        }
        finally
        {
            if (project is not null) MapResultChartService.DisposeProject(project);
        }
    }

    private static void ValidateAssociatedCapture(MapHoverAssociatedCapture capture)
    {
        if (capture.GeometryNodes is null || capture.PlanNodes is null || capture.ResultOwners is null ||
            capture.Dependencies is null || capture.GeometryNodes.Length > 256 ||
            capture.PlanNodes.Length > 256 || capture.ResultOwners.Length > 256 ||
            capture.Dependencies.Length is < 1 or > MaximumAssociatedDependencies ||
            capture.Dependencies.Any(item => item is null || string.IsNullOrWhiteSpace(item.Path) ||
                !Path.IsPathFullyQualified(item.Path) || !IsSHA256(item.Revision) ||
                item.Role is not ("source-hdf" or "terrain-input-raster" or "classification-input-raster")))
            throw new ArgumentException("The associated value source has an incomplete dependency capture.");
    }

    private static void ValidateDynamicResponse(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, MapDynamicProfileResult response)
    {
        if (!SamePath(response.ProjectPath, context.ProjectPath) ||
            !string.Equals(response.ProjectRevision, context.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.ProjectXmlSHA, context.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.VisualizationRevision, context.VisualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            !SameMapID(response.MapID, context.Sources.MapID) || response.MapIndex != context.Sources.MapIndex ||
            response.MapName != context.Sources.MapName || response.MapType != context.Sources.MapType ||
            response.ContextToken != context.Sources.ContextToken || response.LinesToken != request.RequestID ||
            response.Lines.Length != 1 || response.Lines[0].Id != request.RequestID ||
            response.Lines[0].SamplePoints.Length < 2 ||
            response.Lines[0].SamplePoints[0].X != request.Point.X ||
            response.Lines[0].SamplePoints[0].Y != request.Point.Y)
            throw new SourceFileChangedException();
    }

    private static void AddDynamicEntries(ResolvedMapResultContext context,
        MapHoverIdentifyRequest request, MapHoverLayerCapture capture,
        MapDynamicProfileResult sample, ICollection<MapHoverIdentifyEntry> entries)
    {
        IEnumerable<MapDynamicProfileSeries> series = sample.Lines[0].Series
            .Where(item => item.SourceKind == (capture.Kind == "result" ? "Result" : "Terrain"));
        if (capture.Kind == "terrain")
        {
            series = series.Where(item => SamePath(item.SourcePath, capture.SourcePath!) &&
                item.SourceRevision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            MapResultActiveNode active = context.Sources.ActiveNodes.Single(node =>
                MapResultRenderService.SameLocator(node.Node, capture.Node));
            MapTypes selectedMapType = MapResultRenderService.ParseSupportedMapType(capture.MapType)!.Value;
            MapHoverResultSelectionCapture[] selectedSources = capture.ResultSelections ?? [];
            // SampleForMapNode has already matched MultiResult children to
            // slot zero by timestamp. Their native indices need not equal
            // the anchor index captured by the UI.
            series = series.Where(item => (active.NodeKind == "MultiResult" ||
                    item.ProfileIndex == capture.ProfileIndex) &&
                item.MapType == selectedMapType.ToString() && selectedSources.Any(selected =>
                    SamePath(item.SourcePath, selected.ResultPath) &&
                    item.SourceRevision.Equals(selected.ResultRevision, StringComparison.OrdinalIgnoreCase)));
        }

        MapDynamicProfileSeries[] matches = series.ToArray();
        if (capture.Kind == "terrain" && matches.Length != 1)
            throw new SourceFileChangedException();
        foreach (MapDynamicProfileSeries item in matches)
        {
            if (item.UnavailableReason is not null || item.Samples.Length == 0 ||
                item.Samples[0] is not double number || !double.IsFinite(number)) continue;
            string value = number.ToString("G8", CultureInfo.InvariantCulture);
            string field = capture.Kind == "result" ? item.MapType ?? capture.MapType ?? "Result" : "Terrain";
            string units = string.IsNullOrWhiteSpace(item.Units) ? string.Empty : " " + item.Units;
            string label = $"{item.Name} — {field}: {value}{units}";
            entries.Add(new(capture.Node, capture.Kind, label, value, "number",
                item.SourcePath, item.SourceRevision, item.MapType, item.ProfileIndex, null, null));
        }
    }

    private static Projection? ProjectProjection(string? projectXml)
    {
        if (string.IsNullOrWhiteSpace(projectXml)) return null;
        XDocument document = XDocument.Parse(projectXml, LoadOptions.None);
        XElement? element = document.Root?.Element("Projection");
        if (element is null) return null;
        ProjectionDefinition definition = ProjectionDefinition.Deserialize(element);
        var result = definition.Evaluate(out Projection? projection, out _);
        if (!result.Result) throw new InvalidDataException("The current project projection could not be evaluated.");
        return projection;
    }

    private static string ResolveRasterPath(string projectPath, string? relative, string? filename)
    {
        string? projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        foreach (string? candidate in new[] { relative, filename })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string normalized = candidate.Replace('\\', Path.DirectorySeparatorChar);
            string resolved = SourceFilePaths.Canonical(Path.IsPathFullyQualified(normalized)
                ? normalized
                : Path.Combine(projectDirectory ?? throw new ArgumentException("The project has no parent directory."), normalized));
            if (File.Exists(resolved)) return resolved;
        }
        throw new FileNotFoundException("The exact source raster file was not found.");
    }

    private static void RejectProtectedCapturePaths(MapHoverIdentifyRequest request)
    {
        string[] protectedPaths = (request.Map.ComputeReadLease?.ProtectedSourcePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(SourceFilePaths.Canonical).ToArray();
        if (protectedPaths.Length == 0) return;
        foreach (MapHoverLayerCapture capture in request.Layers)
        {
            // Result reads go through ResolvedMapResultContext, which applies
            // the exact compute lease and never opens a protected writer path.
            if (capture.Kind == "result") continue;
            IEnumerable<string> paths = new[] { capture.SourcePath, capture.RasterSource?.SourcePath,
                capture.Mesh?.SourcePath, capture.Mesh?.Model?.AssetPath,
                capture.ModelVector?.SourcePath, capture.ModelVector?.Model?.AssetPath,
                capture.ModelVector?.ResultOwner?.SelectedResultPath,
                capture.Precipitation?.ResultOwner?.SelectedResultPath,
                capture.Kind == "precipitation" ? capture.Node.AssetID : null,
                capture.Kind == "precipitation" ? capture.Node.ParentBoundaryConditionAssetID : null,
                capture.Kind == "precipitation" ? capture.Node.ParentResultAssetID : null,
                capture.Precipitation?.BoundaryNode.AssetID,
                capture.Precipitation?.BoundaryNode.ParentResultAssetID }
                .Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>()
                .Concat(capture.Associated?.Dependencies?.Select(item => item.Path) ?? []);
            if (paths.Select(SourceFilePaths.Canonical).Any(path => protectedPaths.Contains(path, PathComparer)))
                throw new NotSupportedException("The selected map source is currently owned by an active writer.");
        }
    }

    private static void VerifyAssociatedDependencies(string projectRevision, string visualizationRevision,
        string projectPath, IEnumerable<AssociatedMapLayerDependency> dependencies)
    {
        MapResultChartService.EnsureUnchanged(projectPath, projectRevision);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        MapResultChartService.EnsureUnchanged(visualizationPath, visualizationRevision);
        foreach (AssociatedMapLayerDependency dependency in dependencies)
            MapResultChartService.EnsureUnchanged(dependency.Path, dependency.Revision);
    }

    private static bool SameDependencies(AssociatedMapLayerDependency[] actual,
        AssociatedMapLayerDependency[] expected)
    {
        if (actual.Length != expected.Length) return false;
        AssociatedMapLayerDependency[] orderedActual = actual.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => SourceFilePaths.Canonical(item.Path), PathComparer).ToArray();
        AssociatedMapLayerDependency[] orderedExpected = expected.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => SourceFilePaths.Canonical(item.Path), PathComparer).ToArray();
        return orderedActual.Where((item, index) => item.Role != orderedExpected[index].Role ||
            !SamePath(item.Path, orderedExpected[index].Path) ||
            !item.Revision.Equals(orderedExpected[index].Revision, StringComparison.OrdinalIgnoreCase)).Any() == false;
    }

    private static MapGeometryViewport PointSampleViewport(MapHoverCoordinate point, double worldUnitsPerScreenPoint)
    {
        double half = Math.Max(worldUnitsPerScreenPoint, 1e-9);
        double minX = point.X - half, minY = point.Y - half, maxX = point.X + half, maxY = point.Y + half;
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY) ||
            !(minX < maxX) || !(minY < maxY))
            throw new ArgumentException("The map hover point cannot be represented as a finite source viewport.");
        return new(minX, minY, maxX, maxY);
    }

    private static string ResolveProjectAssetPath(Project project, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new SourceFileChangedException();
        string path = Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(project.ProjectDirectory, sourcePath);
        return SourceFilePaths.Canonical(path);
    }

    private static bool SameMapID(string left, string right) =>
        Guid.TryParse(left, out Guid leftID) && Guid.TryParse(right, out Guid rightID)
            ? leftID == rightID
            : string.Equals(left, right, StringComparison.Ordinal);

    internal static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            foreach (CatalogTreeNode child in Flatten(node.Children ?? [])) yield return child;
        }
    }

    internal static bool SamePath(string left, string right) => PathComparer.Equals(
        SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed record MapHoverMeshHit(string Label, string Element, int MeshIndex, int ElementIndex);

/// <summary>Exact source hit-testing for ordinary and Result-owned FlowArea Mesh layers.</summary>
public static partial class ModelEditorAdapter
{
    internal static async Task<MapHoverIdentifyEntry?> SampleMapHoverMeshAsync(
        MapHoverIdentifyRequest request, MapHoverLayerCapture layerCapture)
    {
        MapHoverMeshCapture capture = layerCapture.Mesh
            ?? throw new ArgumentException("The Mesh capture is incomplete.");
        if (capture.Node.NodeType != "FlowAreaLayer" || capture.GeometryNode.NodeType != "Geometry" ||
            string.IsNullOrWhiteSpace(capture.SourceIdentity) ||
            string.IsNullOrWhiteSpace(capture.GeometryContextToken) ||
            !MapHoverIdentifyService.IsSHA256(capture.SourceRevision) || string.IsNullOrWhiteSpace(capture.SourcePath) ||
            !double.IsFinite(request.WorldUnitsPerScreenPoint) || request.WorldUnitsPerScreenPoint <= 0)
            throw new ArgumentException("The exact FlowArea Mesh capture is incomplete.");

        bool hasParentDraft = capture.OwnerMode == "parentDraft";
        if (capture.ResultOwner is not null && capture.OwnerMode != "resultSource" ||
            capture.ResultOwner is null && capture.OwnerMode is not ("parentDraft" or "savedSource") ||
            capture.FlowAreaVisibilityOverride && !hasParentDraft)
            throw new SourceFileChangedException();

        MapGeometrySnapshot sourceSnapshot;
        MapResultGeometrySnapshot? resultSnapshot = null;
        if (capture.ResultOwner is { } resultOwner)
        {
            resultSnapshot = await LoadMapResultGeometryAsync(new MapResultGeometryRequest(
                request.Map, resultOwner, capture.GeometryNode, capture.GeometryContextToken,
                PageSize: 1, SkipTopologyPage: true, SkipFlowAreasPage: true, SkipCulvertsPage: true));
            sourceSnapshot = resultSnapshot.Geometry;
        }
        else
        {
            sourceSnapshot = await LoadMapGeometryAsync(new MapGeometryRequest(
                request.Map, capture.GeometryNode, PageSize: 1,
                ContextToken: capture.GeometryContextToken,
                SkipTopologyPage: true, SkipFlowAreasPage: true, SkipCulvertsPage: true,
                PlanNodes: capture.PlanNodes));
        }

        if (sourceSnapshot.FlowAreasState.Locator is not { } flowAreaLocator ||
            !MapResultRenderService.SameLocator(flowAreaLocator, capture.Node) ||
            !(sourceSnapshot.FlowAreasState.EffectiveVisible || capture.FlowAreaVisibilityOverride) ||
            !string.Equals(sourceSnapshot.GeometryRevision, capture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !MapHoverIdentifyService.SamePath(sourceSnapshot.GeometryCanonicalPath, capture.SourcePath))
            throw new SourceFileChangedException();

        IMeshCollection meshes;
        ModelContext? modelContext = null;
        try
        {
            if (resultSnapshot is not null)
            {
                using ResolvedMapResultContext resultContext = MapResultChartService.ResolveContext(
                    request.Map, capture.ResultOwner!.SelectionOverrides, capture.ResultOwner.ContextToken);
                ResolvedMapResultNode selected = ResolveMapResultOwner(resultContext,
                    capture.ResultOwner.ResultNode, capture.ResultOwner.SelectionIndex);
                RequireExactSelectedResult(selected, capture.ResultOwner.SelectedResultPath,
                    capture.ResultOwner.SelectedResultRevision);
                if (selected.Result.Geometry is not { FlowAreaLayer.Count: > 0 } geometry)
                    return null;
                meshes = geometry.FlowAreaLayer.AsMeshCollection();
                MapHoverIdentifyEntry? entry = BuildMeshEntry(new MeshRenderLayer(meshes),
                    layerCapture.Node, request, selected.ResultPath, selected.Revision);
                resultContext.EnsureUnchanged();
                return entry;
            }

            GeometryMeshModelRequest model = capture.Model
                ?? throw new ArgumentException("The saved Geometry hover capture has no model request.");
            VerifyGeometryMeshModel(model);
            if (!MapHoverIdentifyService.SamePath(model.AssetPath, capture.SourcePath) || model.Kind != "Geometry" ||
                string.IsNullOrWhiteSpace(model.ProjectPath) ||
                !MapHoverIdentifyService.SamePath(model.ProjectPath, request.Map.ProjectPath) ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    model.ProjectXml ?? string.Empty))), request.Map.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
                model.CoreLayerContext?.IdentityToken != request.Map.CoreLayerContext?.IdentityToken ||
                !string.Equals(Revision(model.AssetPath), capture.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            if (hasParentDraft)
            {
                if (string.IsNullOrEmpty(capture.MeshDraftState))
                    throw new ArgumentException("A parent-draft Mesh hover requires its exact draft payload.");
                VerifyDraftDigest(capture.MeshDraftState, capture.MeshDraftSHA256, "mesh");
                VerifyDraftDigest(capture.TopologyDraftState ?? string.Empty,
                    capture.TopologyDraftSHA256, "topology");
            }
            else if (capture.MeshDraftState.Length != 0 || capture.TopologyDraftState is not null ||
                     !string.Equals(capture.MeshDraftSHA256, Sha256(string.Empty), StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(capture.TopologyDraftSHA256, Sha256(string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                throw new SourceFileChangedException();
            }

            modelContext = new ModelContext(ToModelRequest(model));
            if (hasParentDraft)
            {
                ApplyGeometryTopologyDraft(modelContext, model.AssetPath, capture.SourceRevision,
                    capture.TopologyDraftState);
                string topologyFingerprint = GeometryMeshTopologyFingerprint(modelContext);
                GeometryMeshDraftState meshState = ReadGeometryMeshDraft(capture.MeshDraftState,
                    model.AssetPath, capture.SourceRevision);
                EnsureTopologyFingerprint(meshState, topologyFingerprint);
                _ = await ApplyGeometryMeshMutationsAsync(modelContext, model.AssetPath,
                    capture.SourceRevision, meshState, replayPropertyTableComputations: false);
            }

            var (observableGeometry, _) = GeometryMeshTopologyFor(modelContext);
            meshes = observableGeometry.FlowAreaLayer.Layer.AsMeshCollection();
            MapHoverIdentifyEntry? result = BuildMeshEntry(new MeshRenderLayer(meshes),
                layerCapture.Node, request, capture.SourcePath, capture.SourceRevision);
            if (!string.Equals(Revision(model.AssetPath), capture.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return result;
        }
        finally
        {
            modelContext?.Dispose();
        }
    }

    private static MapHoverIdentifyEntry? BuildMeshEntry(MeshRenderLayer renderer,
        CatalogTreeNodeLocator node, MapHoverIdentifyRequest request,
        string sourcePath, string sourceRevision)
    {
        HitTestArgs args = HitTestArgs.Generate(new Point(request.Point.X, request.Point.Y),
            request.WorldUnitsPerScreenPoint);
        if (!renderer.HitTest(args, out MeshElementType element, out int meshIndex, out int elementIndex))
            return null;
        // MeshRenderLayer.MapHover performs a fresh exact hit and ignores its
        // legacy ref cache, so each identify call stays independent.
        object hitCache = new object();
        string? label = renderer.MapHover(args, ref hitCache)?.FirstOrDefault()?.Label;
        if (string.IsNullOrWhiteSpace(label)) return null;
        return new(node, "mesh", label, label, element.ToString(), sourcePath,
            sourceRevision, null, null, meshIndex, elementIndex);
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
