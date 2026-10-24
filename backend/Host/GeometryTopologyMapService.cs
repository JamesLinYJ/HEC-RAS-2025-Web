using System.Globalization;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Rendering.Layers;
using Geospatial.Rasters;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.Vectors;
using Geospatial.Features;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Observables;
using Ras.Layers;
using Utility.DataStructures;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private const int MaximumMapCommandFeatures = 10_000;
    private const int MaximumMapCommandCoordinates = 100_000;

    public static Task<GeometryTopologySpatialSelectResult> SelectGeometryTopologySpatialAsync(
        GeometryTopologySpatialSelectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        VerifyGeometryTopologyModel(request.Model);
        ValidateSpatialRequest(request);

        string sourceRevision = Revision(request.Model.AssetPath);
        if (!string.Equals(sourceRevision, request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        GeometryTopologyDraftState state = ReadTopologyDraft(request.DraftState,
            request.Model.AssetPath, request.Revision);
        string draftIdentity = DraftIdentity(request.DraftState);
        if (!string.Equals(draftIdentity, request.DraftIdentity, StringComparison.Ordinal)
            || !string.Equals(request.GeometryRevision, sourceRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, state);
        ObservableConceptualMeshTopology observable = replay.Session.ObservableTopology;
        Topology topology = replay.Session.Topology;

        var arcs = SelectionSet(observable.Arcs, request.CurrentArcFIDs, arc => arc.Fid, "arc");
        var nodes = SelectionSet(observable.Nodes, request.CurrentNodeFIDs, node => node.Fid, "node");
        var regions = SelectionSet(observable.ControlPoints, request.CurrentRegionFIDs,
            point => point.Fid, "region");
        ApplySpatialSelection(topology, arcs, nodes, regions, request);

        GeometryTopologyMap map = MapData(observable).Map;
        GeometryTopologySelectedFeature[] selected =
        [
            .. SelectedFeatures(observable.Arcs, arcs, arc => arc.Fid, "arc", map),
            .. SelectedFeatures(observable.Nodes, nodes, node => node.Fid, "node", map),
            .. SelectedFeatures(observable.ControlPoints, regions, point => point.Fid, "region", map)
        ];

        if (!string.Equals(Revision(request.Model.AssetPath), sourceRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return Task.FromResult(new GeometryTopologySpatialSelectResult(request.RequestID,
            request.MapID, request.ContextToken, request.GeometryRevision, request.Revision,
            draftIdentity, selected));
    }

    private static void ValidateSpatialRequest(GeometryTopologySpatialSelectRequest request)
    {
        ValidateMapIdentity(request.RequestID, request.MapID, request.ContextToken);
        if (string.IsNullOrWhiteSpace(request.DraftState) || string.IsNullOrWhiteSpace(request.DraftIdentity)
            || !IsTopologyDraftSHA256(request.DraftIdentity) || string.IsNullOrWhiteSpace(request.Revision)
            || string.IsNullOrWhiteSpace(request.GeometryRevision))
            throw new ArgumentException("The spatial selection has an incomplete Geometry draft identity.");
        if (request.WorldPoints is null || request.WorldPoints.Length > MaximumMapCommandCoordinates
            || request.CurrentArcFIDs is null || request.CurrentNodeFIDs is null || request.CurrentRegionFIDs is null)
            throw new ArgumentException("The spatial selection request is incomplete or too large.");
        if (!double.IsFinite(request.MapCellSize) || request.MapCellSize <= 0d)
            throw new ArgumentException("Spatial selection requires the current positive map-cell size.");
        foreach (GeometryTopologyCoordinate point in request.WorldPoints)
            ValidateCoordinate(point);
        _ = ParseSelectionScope(request.SelectionScope);
        _ = ParseSelectionMode(request.SelectionMode);
        _ = request.Gesture switch
        {
            "Click" => request.WorldPoints.Length == 1,
            "Extent" => request.WorldPoints.Length == 2,
            "Path" => request.WorldPoints.Length >= 2,
            "Lasso" => request.WorldPoints.Length >= 3,
            _ => false
        } ? 0 : throw new ArgumentException("The spatial selection gesture has an invalid point count or type.");
        if (request.Gesture is "Extent" or "Lasso") _ = ParseMarqueeHitMode(request.MarqueeHitMode);
    }

    private static void ApplySpatialSelection(Topology topology, SelectedSet arcs, SelectedSet nodes,
        SelectedSet regions, GeometryTopologySpatialSelectRequest request)
    {
        SelectMode mode = ParseSelectionMode(request.SelectionMode);
        TopologySelectionScope scope = ParseSelectionScope(request.SelectionScope);
        GeometryTopologyCoordinate[] points = request.WorldPoints;
        switch (request.Gesture)
        {
            case "Click":
            {
                // The retained map-layer click route uses NetworkRenderLayer.HitTest
                // and its native Select(HitTestArgs, SelectMode) behavior.
                var layer = new TopologyRenderLayer(new Any<Topology>(topology));
                CopySelection(arcs, layer.ArcSelectedSet);
                CopySelection(nodes, layer.NodeSelectedSet);
                CopySelection(regions, layer.ControlPointSelectedSet);
                _ = layer.Select(HitTestArgs.Generate(PointFor(points[0]), request.MapCellSize), mode);
                CopySelection(layer.ArcSelectedSet, arcs);
                CopySelection(layer.NodeSelectedSet, nodes);
                CopySelection(layer.ControlPointSelectedSet, regions);
                break;
            }
            case "Extent":
            {
                GeometryTopologyCoordinate first = points[0], second = points[1];
                var extent = new Extent(Math.Min(first.X, second.X), Math.Max(first.X, second.X),
                    Math.Min(first.Y, second.Y), Math.Max(first.Y, second.Y));
                _ = TopologyRenderLayer.Select(topology, arcs, nodes, regions, extent,
                    ParseMarqueeHitMode(request.MarqueeHitMode), mode, scope);
                break;
            }
            case "Path":
                _ = TopologyRenderLayer.Select(topology, arcs, nodes, regions,
                    new Polyline(points.Select(PointFor)), mode, scope);
                break;
            case "Lasso":
                _ = TopologyRenderLayer.Select(topology, arcs, nodes, regions,
                    new Polygon(points.Select(PointFor)), ParseMarqueeHitMode(request.MarqueeHitMode), mode, scope);
                break;
            default:
                throw new ArgumentException("The spatial selection gesture is unsupported.");
        }
    }

    private static GeometryTopologySelectedFeature[] SelectedFeatures<T>(IEnumerable<T> sourceRows,
        SelectedSet selection, Func<T, int> getFID, string kind, GeometryTopologyMap map)
    {
        var selectedIndexes = selection.Elements.ToHashSet();
        var result = new List<GeometryTopologySelectedFeature>();
        int index = 0;
        foreach (T row in sourceRows)
        {
            if (!selectedIndexes.Contains(index++)) continue;
            int fid = getFID(row);
            string rowID = FeatureID(kind, fid);
            string[] mapIDs = map.Features.Where(feature =>
                    feature.FeatureKind == kind && feature.SelectionIDs.Contains(rowID, StringComparer.Ordinal))
                .Select(feature => feature.ID).Distinct(StringComparer.Ordinal).ToArray();
            result.Add(new GeometryTopologySelectedFeature(kind, fid, rowID, mapIDs));
        }
        return result.ToArray();
    }

    private static SelectedSet SelectionSet<T>(IEnumerable<T> rows, int[] fids,
        Func<T, int> getFID, string sourceName)
    {
        if (fids.Length > MaximumMapCommandFeatures || fids.Any(fid => fid < 0)
            || fids.Distinct().Count() != fids.Length)
            throw new ArgumentException("The current " + sourceName + " selection is invalid or duplicated.");
        var selected = new SelectedSet();
        foreach (int index in ResolveSourceFeatureIndices(rows, fids, getFID, sourceName))
            selected.Add(index);
        return selected;
    }

    private static void CopySelection(SelectedSet source, SelectedSet destination)
    {
        destination.Clear();
        foreach (int index in source.Elements) destination.Add(index);
    }

    private static TopologySelectionScope ParseSelectionScope(string value) => value switch
    {
        "All" => TopologySelectionScope.All,
        "Arcs" => TopologySelectionScope.Arcs,
        "Nodes" => TopologySelectionScope.Nodes,
        "Regions" => TopologySelectionScope.Regions,
        _ => throw new ArgumentException("Unknown topology selection scope.")
    };

    private static SelectMode ParseSelectionMode(string value) => value switch
    {
        "Replace" => SelectMode.Replace,
        "Append" => SelectMode.Append,
        "EnsureIncluded" => SelectMode.EnsureIncluded,
        "Toggle" => SelectMode.Toggle,
        "Remove" => SelectMode.Remove,
        _ => throw new ArgumentException("Unknown topology selection mode.")
    };

    private static MarqueeHitMode ParseMarqueeHitMode(string? value) => value switch
    {
        null or "Intersects" => MarqueeHitMode.Intersects,
        "Contained" => MarqueeHitMode.Contained,
        _ => throw new ArgumentException("Unknown topology marquee hit mode.")
    };

    private static GeometryTopologyMutation PrepareGeometryTopologyMapMutation(ModelContext context,
        GeometryTopologyModelRequest model, string revision, string draftState,
        GeometryTopologyMapRequestContext mapRequestContext, TopologyCompletionSession completion,
        GeometryTopologyMutation mutation)
    {
        ValidateMapRequestContext(mapRequestContext, revision, draftState);
        GeometryTopologyMapCommand command = mutation.MapCommand
            ?? throw new ArgumentException("A topology map edit is missing its typed command.");
        GeometryTopologyMapCommand stable = PrepareMapCommand(context, model, draftState, mapRequestContext,
            completion, command);
        return new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = stable };
    }

    private static GeometryTopologyMapCommand PrepareMapCommand(ModelContext context,
        GeometryTopologyModelRequest model, string draftState, GeometryTopologyMapRequestContext mapRequestContext,
        TopologyCompletionSession completion, GeometryTopologyMapCommand command)
    {
        ValidateMapCommandShape(command);
        if (command.Action == "deepConnect")
            return command with { Coordinates = ResolveDeepConnectCoordinates(model, draftState,
                    mapRequestContext, completion, command.DeepConnectSource!),
                DeepConnectSource = null, SmartAddSource = null };
        if (command.Action == "smartAdd")
            return command with { Coordinates = ResolveSmartAddCoordinates(context, model,
                    completion, command), DeepConnectSource = null, SmartAddSource = null };
        return command;
    }

    private static GeometryTopologyCoordinate[] ResolveSmartAddCoordinates(ModelContext context,
        GeometryTopologyModelRequest model, TopologyCompletionSession completion,
        GeometryTopologyMapCommand command)
    {
        if (command.DeepConnectSource is not null || command.SmartAddSource is null
            || command.Coordinates is not { Length: 2 } endpoints)
            throw new ArgumentException("SmartAdd requires two captured endpoints, a current terrain capture, and its raster window.");
        GeometryTopologySmartAddTerrainCapture current = CaptureGeometryTopologySmartAddTerrain(context,
            model, Revision(model.AssetPath)) ?? throw new InvalidOperationException(
                "SmartAdd is unavailable because this Geometry has no loaded associated Terrain sampler.");
        if (command.SmartAddSource.TerrainCapture != current)
            throw new SourceFileChangedException();

        GeometryCulvertModelRequest samplerModel = new(model.AssetPath, model.Kind, model.ProjectPath,
            model.ProjectXml, model.CoreLayerContext);
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(samplerModel);
        if (!string.Equals(dependencyRevision, current.SourceRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        GeometryTopologySmartAddSource source = command.SmartAddSource;
        GeometryCulvertSmartAddWindow window = new(
            new GeometryCulvertExtent(source.Window.Extent.MinX, source.Window.Extent.MinY,
                source.Window.Extent.MaxX, source.Window.Extent.MaxY),
            source.Window.Rows, source.Window.Columns);
        GeometryCulvertSmartAddOptions options = SmartAddOptions(source.Options);
        if (!ValidSmartAddOptions(options))
            throw new ArgumentException("SmartAdd algorithm options are outside the source-supported bounds.");
        GeometryTopologyCoordinate samplerStart = endpoints[0];
        if (command.StartNodeFID is int capturedStartNodeFID)
        {
            int nodeIndex = ResolveCapturedStartNodeIndex(completion.ObservableTopology,
                capturedStartNodeFID, endpoints[0], command.MapCellSize);
            Point nodePoint = completion.Topology.Nodes[nodeIndex].Point;
            samplerStart = new GeometryTopologyCoordinate(nodePoint.X, nodePoint.Y);
        }
        GeometryCulvertDraftState emptyDraft = NewGeometryCulvertDraft(model.AssetPath,
            current.GeometryRevision, dependencyRevision);
        string encodedDraft = System.Text.Json.JsonSerializer.Serialize(emptyDraft, BackendJson.Options);
        var previewRequest = new GeometryCulvertSmartAddPreviewRequest(samplerModel,
            current.GeometryRevision, encodedDraft, Guid.NewGuid().ToString("N"),
            new GeometryCulvertCoordinate(samplerStart.X, samplerStart.Y),
            new GeometryCulvertCoordinate(endpoints[1].X, endpoints[1].Y), window, options);
        ObservableTerrain? terrain = GeometryCulvertGeometry(context).AssociatedTerrain;
        if (terrain?.Layer is null)
            throw new InvalidOperationException("SmartAdd is unavailable because its associated Terrain sampler is not loaded.");
        GeometryCulvertSmartAddPreviewResponse preview = TryComputeSmartAddSegment(previewRequest,
            options, terrain, current.GeometryRevision, dependencyRevision);
        VerifyGeometryCulvertDependencyRevision(samplerModel, dependencyRevision);
        if (!preview.CanAppend || preview.Path is null || preview.Path.Length < 2)
            throw new InvalidOperationException("The source Terrain sampler could not build a SmartAdd path: "
                + (preview.ErrorCode ?? "pathUnavailable"));
        GeometryTopologyCoordinate[] path = preview.Path.Select(point =>
            new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();
        ValidatePolylineCoordinates(path);
        if (path[0].X != samplerStart.X || path[0].Y != samplerStart.Y)
            throw new SourceFileChangedException();
        return path;
    }

    private static GeometryCulvertSmartAddOptions SmartAddOptions(GeometryTopologySmartAddOptions? options)
    {
        options ??= new GeometryTopologySmartAddOptions();
        return new GeometryCulvertSmartAddOptions(options.Algorithm, options.DpTolerancePixels,
            options.MaxSegmentPixels, options.PreviewThrottlePixels, options.HighGroundAlpha,
            options.HighGroundBailRatio, options.HighGroundSnapRadiusCells, options.HessianFrangiBeta,
            options.HessianFeaturePenaltyKappa, options.HessianOffFeatureBailMultiplier,
            options.HessianMinExpansionsBudget, options.HessianExpansionsPerEuclidPx,
            options.HessianMaxEuclidCells);
    }

    private static GeometryTopologySmartAddTerrainCapture? CaptureGeometryTopologySmartAddTerrain(
        ModelContext context, GeometryTopologyModelRequest model, string revision)
    {
        if (string.IsNullOrWhiteSpace(model.ProjectPath)) return null;
        ObservableGeometry geometry = GeometryCulvertGeometry(context);
        ObservableTerrain? terrain = geometry.AssociatedTerrain;
        if (terrain?.Layer is null || string.IsNullOrWhiteSpace(terrain.Filename)
            || !Path.IsPathFullyQualified(terrain.Filename)) return null;
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(
            new GeometryCulvertModelRequest(model.AssetPath, model.Kind, model.ProjectPath,
                model.ProjectXml, model.CoreLayerContext));
        return new GeometryTopologySmartAddTerrainCapture(SourceFilePaths.Canonical(model.AssetPath),
            revision, SourceFilePaths.Canonical(model.ProjectPath), model.CoreLayerContext?.ProjectSessionID,
            model.CoreLayerContext?.IdentityToken, SourceFilePaths.Canonical(terrain.Filename), terrain.Name,
            dependencyRevision);
    }

    private static GeometryTopologyCoordinate[] ResolveDeepConnectCoordinates(GeometryTopologyModelRequest model,
        string draftState, GeometryTopologyMapRequestContext mapRequestContext,
        TopologyCompletionSession completion, GeometryTopologyDeepConnectSource source)
    {
        if (source is null || source.Points is null || source.Points.Length < 2
            || source.Points.Length > MaximumMapCommandCoordinates
            || source.PathIndex < 0 || source.FeatureFID < 0)
            throw new ArgumentException("DeepConnect requires one exact persisted line source and two anchors.");
        foreach (GeometryTopologyCoordinate point in source.Points) ValidateCoordinate(point);
        Polyline line;
        if (source.SourceKind == "topology")
        {
            GeometryTopologyDeepConnectTopologySource owner = source.TopologySource
                ?? throw new ArgumentException("The topology DeepConnect source is missing its exact Geometry draft capture.");
            if (source.VectorSource is not null || source.PathIndex != 0
                || source.SourceLocator != FeatureID("arc", source.FeatureFID)
                || source.ComponentIdentity != $"arc:{source.FeatureFID}:path:0"
                || source.SourceRevision != Revision(model.AssetPath)
                || owner.Model is null || owner.MapRequestContext is null
                || owner.FeatureKind != "arc" || owner.FeatureID != FeatureID("arc", source.FeatureFID)
                || !SameGeometryModel(owner.Model, model) || owner.Revision != mapRequestContext.GeometryRevision
                || owner.DraftState != draftState
                || owner.DraftIdentity != DraftIdentity(draftState)
                || !SameMapOwner(owner.MapRequestContext, mapRequestContext))
                throw new SourceFileChangedException();
            ObservableConceptualMeshArc arc = FindArc(completion.ObservableTopology, source.FeatureFID);
            if (arc.Fid != source.FeatureFID) throw new SourceFileChangedException();
            line = completion.Topology.GetArcPolyline(arc.Fid);
        }
        else if (source.SourceKind == "vector")
        {
            GeometryTopologyDeepConnectVectorSource owner = source.VectorSource
                ?? throw new ArgumentException("The vector DeepConnect source is missing its persisted map owner.");
            if (source.TopologySource is not null || source.SourceLocator != SourceFilePaths.Canonical(
                    MapSourceIdentity.ResolvePersistedNode(owner.Map, owner.Node).SourcePath)
                || source.ComponentIdentity != $"feature:{source.FeatureFID}:part:{source.PathIndex}")
                throw new SourceFileChangedException();
            line = ReadPersistedDeepConnectVector(model, mapRequestContext, source, owner);
        }
        else
        {
            throw new ArgumentException("DeepConnect accepts only a saved vector line source or an exact Geometry topology arc.");
        }

        GeometryTopologyCoordinate[] actualPoints = Coordinates(line);
        if (!CoordinateSequenceEqual(actualPoints, source.Points)) throw new SourceFileChangedException();
        PointOnPolyline start = Anchor(line, source.StartSegmentIndex, source.StartSegmentFraction);
        PointOnPolyline end = Anchor(line, source.EndSegmentIndex, source.EndSegmentFraction);
        Polyline subsection = line.SubsectionDirected(start, end)
            ?? throw new ArgumentException("The selected DeepConnect anchors do not form a source path.");
        GeometryTopologyCoordinate[] result = Coordinates(subsection);
        if (result.Length < 2) throw new ArgumentException("DeepConnect needs two distinct source anchors.");
        return result;
    }

    private static bool SameGeometryModel(GeometryTopologyModelRequest left, GeometryTopologyModelRequest right) =>
        left.Kind == right.Kind
        && SourceFilePaths.SameSourceFile(left.AssetPath, right.AssetPath)
        && SameOptionalCanonicalPath(left.ProjectPath, right.ProjectPath)
        && string.Equals(left.ProjectXml, right.ProjectXml, StringComparison.Ordinal)
        && string.Equals(left.CoreLayerContext?.ProjectSessionID, right.CoreLayerContext?.ProjectSessionID,
            StringComparison.Ordinal)
        && string.Equals(left.CoreLayerContext?.IdentityToken, right.CoreLayerContext?.IdentityToken,
            StringComparison.Ordinal);

    private static bool SameMapOwner(GeometryTopologyMapRequestContext? left,
        GeometryTopologyMapRequestContext? right) =>
        left is not null && right is not null
        && string.Equals(left.MapID, right.MapID, StringComparison.Ordinal)
        && string.Equals(left.ContextToken, right.ContextToken, StringComparison.Ordinal)
        && string.Equals(left.GeometryRevision, right.GeometryRevision, StringComparison.Ordinal)
        && string.Equals(left.ExpectedDraftIdentity, right.ExpectedDraftIdentity, StringComparison.Ordinal);

    private static bool SameOptionalCanonicalPath(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) ? string.IsNullOrWhiteSpace(right)
            : !string.IsNullOrWhiteSpace(right) && SourceFilePaths.SameSourceFile(left, right);

    private static Polyline ReadPersistedDeepConnectVector(GeometryTopologyModelRequest model,
        GeometryTopologyMapRequestContext mapRequestContext, GeometryTopologyDeepConnectSource source,
        GeometryTopologyDeepConnectVectorSource owner)
    {
        MapTreeRequest map = owner.Map;
        CatalogTreeNodeLocator node = owner.Node;
        if (node.MapType is not ("PolylinesVector" or "PolygonsVector")
            || map.MapID != mapRequestContext.MapID
            || !SameOptionalCanonicalPath(map.ProjectPath, model.ProjectPath)
            || !string.Equals(map.ProjectSessionID, model.CoreLayerContext?.ProjectSessionID,
                StringComparison.Ordinal)
            || !string.Equals(map.CoreLayerContext?.IdentityToken, model.CoreLayerContext?.IdentityToken,
                StringComparison.Ordinal))
            throw new SourceFileChangedException();

        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedNode(map, node);
        string path = Path.GetFullPath(location.SourcePath);
        MapSourceFamilyCapture family;
        if (Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            if (owner.ExpectedFamily is null) throw new SourceFileChangedException();
            MapShapefileImportService.VerifyExpectedFamily(owner.ExpectedFamily, path);
            family = owner.ExpectedFamily;
        }
        else if (GeoJsonReader.HasJsonFileExtension(path))
        {
            family = owner.ExpectedFamily ?? MapSourceIdentity.CaptureFamily("geojson", path, []);
            if (!string.Equals(family.Family, "geojson", StringComparison.Ordinal)
                || !SourceFilePaths.SameSourceFile(family.SourcePath, path))
                throw new SourceFileChangedException();
        }
        else
        {
            throw new NotSupportedException("DeepConnect supports persisted Shapefile or GeoJSON line-source layers.");
        }
        MapSourceIdentity.VerifyFamily(family);
        if (!string.Equals(SourceRevisionOf(family), source.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        MapSourceNodeCapture capture = MapSourceIdentity.ResolvePersistedNode(map, node, family);
        VerifyVisibleMapVectorSource(map, node);

        Polyline line;
        bool isShapefile = Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase);
        if (node.MapType == "PolylinesVector" && isShapefile)
        {
            // Shapefile Polyline captures retain the existing source contract:
            // FeatureFID is the persisted record ordinal and PathIndex is the
            // source part ordinal. Those are not the flattened renderer IDs.
            MapVectorSourceLoadResult sourceData = MapVectorSourceService.Load(
                new MapVectorSourceLoadRequest(map, node, family));
            MapVectorSourceFeature feature = sourceData.Data.Features.SingleOrDefault(item =>
                item.SourceOrdinal == source.FeatureFID)
                ?? throw new SourceFileChangedException();
            MapVectorSourcePart part = feature.Parts.SingleOrDefault(item => item.PartOrdinal == source.PathIndex)
                ?? throw new SourceFileChangedException();
            GeometryTopologyCoordinate[] projected = part.Vertices.Select(vertex =>
                new GeometryTopologyCoordinate(vertex.X, vertex.Y)).ToArray();
            if (projected.Length < 2 || !CoordinateSequenceEqual(projected, source.Points))
                throw new SourceFileChangedException();
            line = new Polyline(projected.Select(PointFor));
        }
        else
        {
            // For every other vector route FeatureFID is the first-party
            // renderer's line-reader ordinal. In particular, GeoJSON line IDs
            // are counted after filtering the FeatureCollection to LineString;
            // polygon IDs are flattened PolygonRenderLayer ring ordinals. Do
            // not reinterpret these values as raw FeatureCollection indexes,
            // Shapefile record ordinals, or source part ordinals.
            if (source.PathIndex != 0)
                throw new SourceFileChangedException();
            Project project = CoreLayerImportService.OpenProject(Path.GetFullPath(map.ProjectPath), map.ProjectXml,
                map.CoreLayerContext);
            try
            {
                if (node.MapType == "PolylinesVector")
                {
                    var read = PolylineMapLayerVisualContext.RenderLayerFrom(path, project.Projection,
                        out IPolylineCollection polylines, out _, out _);
                    if (!read.Result || polylines is null || source.FeatureFID >= polylines.Count)
                        throw new SourceFileChangedException();
                    line = polylines[source.FeatureFID];
                }
                else
                {
                    var read = PolygonMapLayerVisualContext.RenderLayerFrom(path, project.Projection,
                        out IPolygonCollection polygons, out _, out _);
                    if (!read.Result || polygons is null || source.FeatureFID >= polygons.Count)
                        throw new SourceFileChangedException();
                    // PolygonMapLayerVisualContext exposes each Shapefile ring
                    // and each supported GeoJSON Polygon outer ring as one
                    // ILineSegmentSource reader entry. There is no path index
                    // for another ring in this persisted map source identity.
                    line = ((ILineSegmentSource)polygons).GetLine(source.FeatureFID);
                }
                if (line.Count < 2 || !CoordinateSequenceEqual(Coordinates(line), source.Points))
                    throw new SourceFileChangedException();
            }
            finally
            {
                MapResultChartService.DisposeProject(project);
            }
        }
        MapSourceIdentity.VerifyPersistedNode(map, capture);
        VerifyVisibleMapVectorSource(map, node);
        return line;
    }

    private static void VerifyVisibleMapVectorSource(MapTreeRequest request,
        CatalogTreeNodeLocator locator)
    {
        MapTreeSnapshot tree = MapTreeService.Load(request);
        if (!FindVisibleMapNode(tree.Roots ?? [], locator, ancestorsVisible: true, out bool visible)
            || !visible)
            throw new SourceFileChangedException();
    }

    private static bool FindVisibleMapNode(IEnumerable<CatalogTreeNode> nodes,
        CatalogTreeNodeLocator target, bool ancestorsVisible, out bool visible)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            bool currentVisible = ancestorsVisible && !string.Equals(node.Checked, "unchecked", StringComparison.Ordinal);
            if (node.Locator is CatalogTreeNodeLocator locator && SameTopologyMapLocator(locator, target))
            {
                visible = currentVisible && string.Equals(node.Checked, "checked", StringComparison.Ordinal);
                return true;
            }
            if (node.Children is { Length: > 0 } children
                && FindVisibleMapNode(children, target, currentVisible, out visible)) return true;
        }
        visible = false;
        return false;
    }

    private static bool SameTopologyMapLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        left.RootName == right.RootName && left.NodeType == right.NodeType && left.NodeName == right.NodeName
        && SamePath(left.NodePath, right.NodePath)
        && left.AssetID == right.AssetID && left.MapType == right.MapType && left.Filename == right.Filename
        && left.FilenameRelative == right.FilenameRelative
        && left.ParentGeometryAssetID == right.ParentGeometryAssetID
        && SamePath(left.ParentGeometryNodePath, right.ParentGeometryNodePath)
        && left.ParentGeometryNodeName == right.ParentGeometryNodeName
        && left.AssociationAssetID == right.AssociationAssetID && left.AssociationKind == right.AssociationKind
        && left.ParentPlanAssetID == right.ParentPlanAssetID
        && SamePath(left.ParentPlanNodePath, right.ParentPlanNodePath)
        && left.ParentPlanNodeName == right.ParentPlanNodeName
        && left.ParentResultNodeType == right.ParentResultNodeType
        && left.ParentResultAssetID == right.ParentResultAssetID
        && SamePath(left.ParentResultNodePath, right.ParentResultNodePath)
        && left.ParentResultNodeName == right.ParentResultNodeName
        && left.ParentResultRevision == right.ParentResultRevision
        && left.ParentBoundaryConditionAssetID == right.ParentBoundaryConditionAssetID
        && SamePath(left.ParentBoundaryConditionNodePath, right.ParentBoundaryConditionNodePath)
        && left.ParentBoundaryConditionNodeName == right.ParentBoundaryConditionNodeName
        && Equals(left.SessionHdfResultOwner, right.SessionHdfResultOwner);

    private static bool SamePath(int[]? left, int[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static string SourceRevisionOf(MapSourceFamilyCapture family)
    {
        MapSourceFileRevision[] source = family.Files.Where(file => file.Role == "source").ToArray();
        if (source.Length != 1 || source[0].Revision is not string revision || revision.Length != 64)
            throw new SourceFileChangedException();
        return revision;
    }

    private static PointOnPolyline Anchor(Polyline line, int segmentIndex, double fraction)
    {
        if (segmentIndex < 0 || segmentIndex >= line.Count - 1 || !double.IsFinite(fraction)
            || fraction < 0d || fraction > 1d)
            throw new ArgumentException("A DeepConnect segment/fraction anchor is invalid.");
        Point first = line[segmentIndex], second = line[segmentIndex + 1];
        Point point = new(first.X + (second.X - first.X) * fraction,
            first.Y + (second.Y - first.Y) * fraction);
        return new PointOnPolyline(point, segmentIndex, fraction);
    }

    private static bool CoordinateSequenceEqual(GeometryTopologyCoordinate[] left,
        GeometryTopologyCoordinate[] right) => left.Length == right.Length
        && left.Zip(right).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y);

    private static void ValidateMapMutationEnvelope(GeometryTopologyMutation mutation)
    {
        if (mutation.Operation != "mapCommand" || mutation.FeatureKind != "map"
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is not null || mutation.TagName is not null
            || mutation.TagType is not null || mutation.TagValue is not null || mutation.ApplyToSelectedOnly
            || mutation.FactorText is not null || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet || mutation.PointKind is not null
            || mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex is not null
            || mutation.Axis is not null || mutation.RawValue is not null || mutation.ImportPayload is not null)
            throw new ArgumentException("A topology map edit must contain only one typed map command.");
    }

    private static void ValidateMapRequestContext(GeometryTopologyMapRequestContext owner,
        string revision, string draftState)
    {
        ValidateMapIdentity(owner.RequestID, owner.MapID, owner.ContextToken);
        if (!string.Equals(owner.GeometryRevision, revision, StringComparison.Ordinal)
            || !IsTopologyDraftSHA256(owner.ExpectedDraftIdentity)
            || !string.Equals(owner.ExpectedDraftIdentity, DraftIdentity(draftState), StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void ValidateMapIdentity(string requestID, string mapID, string contextToken)
    {
        if (!Guid.TryParse(requestID, out _) || string.IsNullOrWhiteSpace(mapID) || mapID.Length > 1024
            || string.IsNullOrWhiteSpace(contextToken) || contextToken.Length > 4096)
            throw new ArgumentException("The map request does not have an exact request and owner identity.");
    }

    private static bool IsTopologyDraftSHA256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateMapCommandShape(GeometryTopologyMapCommand command,
        bool allowResolvedSourcePath = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.FeatureIDs is null || command.FeatureIDs.Length > MaximumMapCommandFeatures
            || command.FeatureIDs.Any(fid => fid < 0)
            || command.FeatureIDs.Distinct().Count() != command.FeatureIDs.Length)
            throw new ArgumentException("A topology map command contains invalid source feature identities.");
        if (command.NodeFIDs is null || command.NodeFIDs.Length > MaximumMapCommandFeatures
            || command.NodeFIDs.Any(fid => fid < 0)
            || command.NodeFIDs.Distinct().Count() != command.NodeFIDs.Length)
            throw new ArgumentException("A topology map command contains invalid selected node identities.");
        if (!double.IsFinite(command.MapCellSize) || command.MapCellSize <= 0d)
            throw new ArgumentException("A topology map command requires the current positive map-cell size.");
        if (command.Coordinates is { Length: > MaximumMapCommandCoordinates })
            throw new ArgumentException("A topology map command has too many coordinates.");
        if (command.Coordinates is not null)
            foreach (GeometryTopologyCoordinate point in command.Coordinates) ValidateCoordinate(point);

        bool noTarget = command.NodeFID is null && command.ArcFID is null && command.PointIndex is null
            && command.SourceSegmentIndex is null && command.SourceSegmentFraction is null
            && command.StartArcTarget is null && command.StartNodeFID is null;
        bool noLegacyTarget = command.NodeFID is null && command.ArcFID is null && command.PointIndex is null
            && command.SourceSegmentIndex is null && command.SourceSegmentFraction is null;
        bool noStartTarget = command.StartArcTarget is null && command.StartNodeFID is null;
        bool noNodeSelection = command.NodeFIDs.Length == 0;
        bool noTransform = command.Epsilon is null && command.SmoothWindowSize is null
            && command.SmoothIterations is null && command.SplitMode is null;
        bool noArcTransform = command.Epsilon is null && command.SmoothWindowSize is null
            && command.SmoothIterations is null;
        bool noSources = command.DeepConnectSource is null && command.SmartAddSource is null;
        bool emptyFeatures = command.FeatureIDs.Length == 0;
        bool oneCoordinate = command.Coordinates is { Length: 1 };
        bool lineCoordinates = command.Coordinates is { Length: >= 2 };
        bool validStartTargetShape = allowResolvedSourcePath
            ? ValidStartTargetShape(command)
            : ValidStartTargetSourceShape(command);
        bool valid = command.Action switch
        {
            "addArc" => command.FeatureKind == "arc" && emptyFeatures && noLegacyTarget && noNodeSelection
                && noArcTransform && noSources && lineCoordinates && ValidStartTargetShape(command),
            "moveNode" => command.FeatureKind == "node" && emptyFeatures && command.NodeFID is >= 0
                && command.NodeFIDs.Length > 0 && command.NodeFIDs.Contains(command.NodeFID.Value)
                && command.ArcFID is null && command.PointIndex is null && command.SourceSegmentIndex is null
                && command.SourceSegmentFraction is null && noStartTarget
                && oneCoordinate && noTransform && noSources,
            "moveArcPoint" => command.FeatureKind == "arc" && emptyFeatures && command.NodeFID is null
                && command.ArcFID is >= 0 && command.PointIndex is >= 0 && command.SourceSegmentIndex is null
                && command.SourceSegmentFraction is null && noStartTarget && oneCoordinate && noTransform
                && noSources && noNodeSelection,
            "insertArcPoint" => command.FeatureKind == "arc" && emptyFeatures && command.NodeFID is null
                && command.ArcFID is >= 0 && command.PointIndex is null && command.SourceSegmentIndex is >= 0
                && command.SourceSegmentFraction is null && noStartTarget && oneCoordinate && noTransform
                && noSources && noNodeSelection,
            "deleteArcPoint" => command.FeatureKind == "arc" && emptyFeatures && command.NodeFID is null
                && command.ArcFID is >= 0 && command.PointIndex is >= 0 && command.SourceSegmentIndex is null
                && command.SourceSegmentFraction is null && noStartTarget && command.Coordinates is null
                && noTransform && noSources && noNodeSelection,
            "splitArc" => command.FeatureKind == "arc" && emptyFeatures && command.NodeFID is null
                && command.ArcFID is >= 0 && command.PointIndex is null && command.SourceSegmentIndex is null
                && command.SourceSegmentFraction is null && noStartTarget && oneCoordinate && command.Epsilon is null
                && command.SmoothWindowSize is null && command.SmoothIterations is null && noSources && noNodeSelection
                && (command.SplitMode is null or "Anywhere" or "AtVertex"),
            "reverseArc" => command.FeatureKind == "arc" && command.FeatureIDs.Length > 0 && noTarget
                && command.Coordinates is null && noTransform && noSources && noNodeSelection,
            "filterArc" => command.FeatureKind == "arc" && command.FeatureIDs.Length > 0 && noTarget
                && command.Coordinates is null && command.SmoothWindowSize is null
                && command.SmoothIterations is null && command.SplitMode is null && noSources && noNodeSelection,
            "smoothArc" => command.FeatureKind == "arc" && command.FeatureIDs.Length > 0 && noTarget
                && command.Coordinates is null && command.Epsilon is null && command.SplitMode is null && noSources
                && noNodeSelection,
            "deepConnect" => command.FeatureKind == "arc" && emptyFeatures && noLegacyTarget
                && noArcTransform && validStartTargetShape
                && noNodeSelection
                && (allowResolvedSourcePath
                    ? command.DeepConnectSource is null && command.SmartAddSource is null
                        && lineCoordinates
                    : command.DeepConnectSource is not null && command.SmartAddSource is null
                        && command.Coordinates is null),
            "smartAdd" => command.FeatureKind == "arc" && emptyFeatures && noLegacyTarget
                && noArcTransform && validStartTargetShape
                && noNodeSelection
                && (allowResolvedSourcePath
                    ? command.SmartAddSource is null && command.DeepConnectSource is null
                        && lineCoordinates
                    : command.SmartAddSource is not null && command.DeepConnectSource is null
                        && command.Coordinates is { Length: 2 }),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The topology map command does not match its typed action fields.");
        if (command.Action == "filterArc" && (command.Epsilon is double epsilon
                && (!double.IsFinite(epsilon) || epsilon < 0d)))
            throw new ArgumentException("The source arc-filter tolerance must be finite and nonnegative.");
        if (command.Action == "smoothArc" && (command.SmoothWindowSize is int window
                && (window < 1 || window > 1001)
            || command.SmoothIterations is int iterations && (iterations < 1 || iterations > 1000)))
            throw new ArgumentException("The source arc-smoothing parameters are outside supported bounds.");
        if (command.Action == "smartAdd" && command.SmartAddSource is { } smartAdd)
            ValidateSmartAddWindow(smartAdd.Window);
    }

    private static bool ValidStartTargetShape(GeometryTopologyMapCommand command) =>
        command.StartNodeFID is not null
            ? ValidStartNodeTargetShape(command, allowResolvedSourcePath: true)
            : ValidStartArcTargetShape(command);

    private static bool ValidStartTargetSourceShape(GeometryTopologyMapCommand command) =>
        command.StartNodeFID is not null
            ? ValidStartNodeTargetShape(command, allowResolvedSourcePath: false)
            : ValidStartArcTargetSourceShape(command);

    private static bool ValidStartNodeTargetShape(GeometryTopologyMapCommand command,
        bool allowResolvedSourcePath)
    {
        if (command.StartNodeFID is not int startNodeFID || startNodeFID < 0
            || command.StartArcTarget is not null
            || command.SplitMode is not null
            || command.Action is not ("addArc" or "smartAdd" or "deepConnect"))
            return false;
        GeometryTopologyCoordinate? firstPoint = command.Action == "deepConnect" && !allowResolvedSourcePath
            ? DeepConnectStartPoint(command.DeepConnectSource)
            : command.Coordinates is { Length: >= 2 } coordinates ? coordinates[0] : null;
        return firstPoint is not null && double.IsFinite(firstPoint.X) && double.IsFinite(firstPoint.Y);
    }

    private static GeometryTopologyCoordinate? DeepConnectStartPoint(GeometryTopologyDeepConnectSource? source)
    {
        if (source?.Points is not { Length: >= 2 } points || source.StartSegmentIndex < 0
            || source.StartSegmentIndex >= points.Length - 1
            || !double.IsFinite(source.StartSegmentFraction) || source.StartSegmentFraction is < 0d or > 1d)
            return null;
        GeometryTopologyCoordinate first = points[source.StartSegmentIndex];
        GeometryTopologyCoordinate second = points[source.StartSegmentIndex + 1];
        return new GeometryTopologyCoordinate(first.X + (second.X - first.X) * source.StartSegmentFraction,
            first.Y + (second.Y - first.Y) * source.StartSegmentFraction);
    }

    private static bool ValidStartArcTargetShape(GeometryTopologyMapCommand command)
    {
        GeometryTopologyArcSnapTarget? target = command.StartArcTarget;
        if (target is null) return command.SplitMode is null;
        if (command.StartNodeFID is not null) return false;
        if (target.ArcFID < 0 || target.SourceSegmentIndex < 0 || target.Point is null
            || command.Coordinates is not { Length: >= 2 } coordinates
            || command.SplitMode is not (null or "Anywhere" or "AtVertex"))
            return false;
        return coordinates[0].X == target.Point.X && coordinates[0].Y == target.Point.Y;
    }

    private static bool ValidStartArcTargetSourceShape(GeometryTopologyMapCommand command)
    {
        GeometryTopologyArcSnapTarget? target = command.StartArcTarget;
        if (target is null) return command.SplitMode is null;
        if (command.StartNodeFID is not null) return false;
        if (target.ArcFID < 0 || target.SourceSegmentIndex < 0 || target.Point is null
            || command.SplitMode is not (null or "Anywhere" or "AtVertex"))
            return false;
        if (command.Action is "addArc" or "smartAdd")
            return command.Coordinates is { Length: >= 2 } coordinates
                && coordinates[0].X == target.Point.X && coordinates[0].Y == target.Point.Y;
        if (command.Action != "deepConnect" || command.DeepConnectSource is not { } source
            || source.Points is null || source.StartSegmentIndex < 0
            || source.StartSegmentIndex >= source.Points.Length - 1
            || !double.IsFinite(source.StartSegmentFraction) || source.StartSegmentFraction < 0d
            || source.StartSegmentFraction > 1d)
            return false;
        GeometryTopologyCoordinate first = source.Points[source.StartSegmentIndex];
        GeometryTopologyCoordinate second = source.Points[source.StartSegmentIndex + 1];
        double x = first.X + (second.X - first.X) * source.StartSegmentFraction;
        double y = first.Y + (second.Y - first.Y) * source.StartSegmentFraction;
        return x == target.Point.X && y == target.Point.Y;
    }

    private static void ValidateSmartAddWindow(GeometryTopologySmartAddWindow window)
    {
        if (window is null || window.Extent is null || window.Rows < 2 || window.Columns < 2
            || (long)window.Rows * window.Columns > 8_000_000L
            || !double.IsFinite(window.Extent.MinX) || !double.IsFinite(window.Extent.MinY)
            || !double.IsFinite(window.Extent.MaxX) || !double.IsFinite(window.Extent.MaxY)
            || window.Extent.MaxX <= window.Extent.MinX || window.Extent.MaxY <= window.Extent.MinY)
            throw new ArgumentException("SmartAdd requires the exact finite raster window used by the map.");
    }

    private static bool ApplyGeometryTopologyMapCommand(ObservableConceptualMeshTopology observable,
        TopologyEditContext editContext, TopologyCompletionContext completionContext,
        GeometryTopologyMapCommand command)
    {
        ValidateMapCommandShape(command, allowResolvedSourcePath: true);
        if (command.DeepConnectSource is not null || command.SmartAddSource is not null)
            throw new ArgumentException("Source captures must be resolved before a map command enters the draft journal.");
        Topology topology = observable.Topology;
        switch (command.Action)
        {
            case "addArc":
                return AddArc(observable, editContext, completionContext, command.Coordinates!,
                    command.MapCellSize, command.StartArcTarget, command.SplitMode, command.StartNodeFID);
            case "deepConnect":
            case "smartAdd":
                return AddArc(observable, editContext, completionContext, command.Coordinates!,
                    command.MapCellSize, command.StartArcTarget, command.SplitMode, command.StartNodeFID,
                    preserveDeepConnectStartAnchor: command.Action == "deepConnect");
            case "moveNode":
            {
                int nodeIndex = ResolveSingleIndex(observable.Nodes, command.NodeFID, node => node.Fid, "node");
                int[] selectedNodeIndices = ResolveSourceFeatureIndices(observable.Nodes, command.NodeFIDs,
                    node => node.Fid, "node");
                if (!selectedNodeIndices.Contains(nodeIndex))
                    throw new ArgumentException("The dragged source node must belong to the captured node selection.");
                GeometryTopologyCoordinate targetCoordinate = command.Coordinates![0];
                Point next = PointFor(targetCoordinate);
                Point current = topology.Nodes[nodeIndex].Point;
                Vector delta = new(next.X - current.X, next.Y - current.Y);
                if (delta.X == 0d && delta.Y == 0d) return false;
                editContext.NodeSelectedSet.Clear();
                foreach (int selectedNodeIndex in selectedNodeIndices)
                    editContext.NodeSelectedSet.Add(selectedNodeIndex);
                if (!NodeArcEditGeometry.CanMoveNodes(topology, delta, editContext.NodeSelectedSet, command.MapCellSize))
                    return false;
                ExecuteTopologyOperation(completionContext,
                    editContext.CreateTranslateSelectedNodesOperation(Hit(targetCoordinate, command.MapCellSize), delta));
                return true;
            }
            case "moveArcPoint":
            {
                int arcIndex = ResolveSingleIndex(observable.Arcs, command.ArcFID, arc => arc.Fid, "arc");
                int pointIndex = command.PointIndex!.Value;
                if (pointIndex >= topology.Arcs[arcIndex].InternalPointCount)
                    throw new ArgumentException("The selected internal arc point no longer exists.");
                Point next = PointFor(command.Coordinates![0]);
                if (topology.Arcs[arcIndex].InternalPoints[pointIndex].EqualXY(in next)) return false;
                ExecuteTopologyOperation(completionContext,
                    new SetArcInternalPointOperation(editContext, arcIndex, pointIndex, next));
                return true;
            }
            case "insertArcPoint":
            {
                int arcIndex = ResolveSingleIndex(observable.Arcs, command.ArcFID, arc => arc.Fid, "arc");
                var wrapper = new ArcPolylineWrapper(new Any<BaseTopology>(topology), arcIndex);
                int segment = command.SourceSegmentIndex!.Value;
                if (segment < 0 || segment >= wrapper.Count - 1)
                    throw new ArgumentException("The selected arc segment no longer exists.");
                var selected = new SelectedSet();
                ExecuteTopologyOperation(completionContext, new InsertPointOperation(() => wrapper,
                    segment + 1, Hit(command.Coordinates![0], command.MapCellSize), selected));
                return true;
            }
            case "deleteArcPoint":
            {
                int arcIndex = ResolveSingleIndex(observable.Arcs, command.ArcFID, arc => arc.Fid, "arc");
                int pointIndex = command.PointIndex!.Value;
                if (pointIndex >= topology.Arcs[arcIndex].InternalPointCount)
                    throw new ArgumentException("The selected internal arc point no longer exists.");
                var selected = new SelectedSet();
                selected.Add(pointIndex + 1); // Source point operations include both endpoint bookends.
                ArcPolylineWrapper wrapper = new(new Any<BaseTopology>(topology), arcIndex);
                EditOperation operation = editContext.CreateDeleteArcPointsOperation(arcIndex,
                    () => wrapper, selected);
                ExecuteTopologyOperation(completionContext, operation);
                return true;
            }
            case "splitArc":
                SplitExactArc(observable, editContext, completionContext, command);
                return true;
            case "reverseArc":
            {
                int[] arcIndices = ResolveSourceFeatureIndices(observable.Arcs, command.FeatureIDs,
                    arc => arc.Fid, "arc");
                foreach (int arcIndex in arcIndices.OrderBy(index => observable.Arcs[index].Fid))
                    ExecuteTopologyOperation(completionContext,
                        new ReverseArcDirectionOperation(editContext, arcIndex));
                return arcIndices.Length > 0;
            }
            case "filterArc":
            case "smoothArc":
                return TransformArcs(observable, editContext, completionContext, command);
            default:
                throw new ArgumentException("The topology map action is unsupported.");
        }
    }

    private static bool AddArc(ObservableConceptualMeshTopology observable,
        TopologyEditContext editContext, TopologyCompletionContext completionContext,
        GeometryTopologyCoordinate[] coordinates, double cellSize,
        GeometryTopologyArcSnapTarget? startTarget = null, string? splitMode = null,
        int? capturedStartNodeFID = null, bool preserveDeepConnectStartAnchor = false)
    {
        Topology topology = observable.Topology;
        if (coordinates is null || coordinates.Length < 2)
            throw new ArgumentException("Drawing an arc requires at least two ordered map coordinates.");
        if (startTarget is not null && capturedStartNodeFID is not null)
            throw new ArgumentException("An Add start cannot capture both a source arc and a source node.");
        ValidatePolylineCoordinates(coordinates);
        int startNodeFID = NewArcOperation.FallbackToHitTest;
        if (startTarget is not null)
        {
            HitTestArgs? splitHit = ResolveStartArcSplitHit(observable, startTarget, coordinates[0], cellSize, splitMode);
            if (splitHit is null) return false; // Source AtVertex behavior: no internal vertex means no Add.
            int previousNodeCount = topology.NodeCount;
            ExecuteTopologyOperation(completionContext, new SplitArcOperation(editContext, splitHit));
            if (topology.NodeCount != previousNodeCount + 1)
                throw new InvalidOperationException("Splitting the captured source arc did not create its continuation node.");
            startNodeFID = topology.NodeCount - 1;
        }
        else if (capturedStartNodeFID is int nodeFID)
        {
            startNodeFID = ResolveCapturedStartNodeIndex(observable, nodeFID, coordinates[0], cellSize);
        }
        ExecuteTopologyOperation(completionContext,
            new NewArcOperation(editContext, Hit(coordinates[0], cellSize), startNodeFID));
        if (editContext.ArcSelectedSet.Count != 1)
            throw new InvalidOperationException("The retained topology editor did not select the newly added arc.");
        int arcFID = editContext.ArcSelectedSet.Elements.Single();
        if (preserveDeepConnectStartAnchor)
        {
            int actualStartNodeIndex = topology.Arcs[arcFID].StartNode;
            Point actualStartNode = topology.Nodes[actualStartNodeIndex].Point;
            if (actualStartNode.X != coordinates[0].X || actualStartNode.Y != coordinates[0].Y)
                AppendArcPoint(topology, completionContext, arcFID, coordinates[0], cellSize);
        }
        foreach (GeometryTopologyCoordinate coordinate in coordinates.Skip(1).Take(coordinates.Length - 2))
            AppendArcPoint(topology, completionContext, arcFID, coordinate, cellSize);
        ExecuteTopologyOperation(completionContext,
            new CompleteArcOperation(editContext, arcFID, Hit(coordinates[^1], cellSize)));
        return true;
    }

    private static int ResolveCapturedStartNodeIndex(ObservableConceptualMeshTopology observable,
        int nodeFID, GeometryTopologyCoordinate firstCoordinate, double cellSize)
    {
        int nodeIndex = ResolveSingleIndex(observable.Nodes, nodeFID, node => node.Fid, "node");
        Point nodePoint = observable.Topology.Nodes[nodeIndex].Point;
        double hitWindowSize = 12d * cellSize;
        if (!double.IsFinite(hitWindowSize))
            throw new ArgumentException("The captured node hit window is outside the source map range.");
        Extent sourceHitWindow = Extent.CenteredOn(PointFor(firstCoordinate), hitWindowSize);
        if (!sourceHitWindow.Contains(nodePoint))
            throw new ArgumentException("The exact captured start node is outside the source six-cell hit window.");
        return nodeIndex;
    }

    private static void AppendArcPoint(Topology topology, TopologyCompletionContext completionContext,
        int arcFID, GeometryTopologyCoordinate coordinate, double cellSize)
    {
        var selectedPoints = new SelectedSet();
        ExecuteTopologyOperation(completionContext, new AppendPointOperation(() => new ArcPolylineWrapper(
            new Any<BaseTopology>(topology), arcFID), Hit(coordinate, cellSize), selectedPoints));
    }

    private static HitTestArgs? ResolveStartArcSplitHit(ObservableConceptualMeshTopology observable,
        GeometryTopologyArcSnapTarget target, GeometryTopologyCoordinate firstCoordinate, double cellSize,
        string? splitMode)
    {
        if (firstCoordinate.X != target.Point.X || firstCoordinate.Y != target.Point.Y)
            throw new ArgumentException("The Add start coordinate must equal its exact captured arc hit.");
        int arcIndex = ResolveSingleIndex(observable.Arcs, target.ArcFID, arc => arc.Fid, "arc");
        Topology topology = observable.Topology;
        Polyline arcLine = topology.GetArcPolyline(arcIndex);
        if (target.SourceSegmentIndex >= arcLine.SegmentCount)
            throw new ArgumentException("The captured Add source segment no longer exists.");

        HitTestArgs capturedHit = Hit(target.Point, cellSize);
        if (!NetworkRenderLayer.HitTest(topology, capturedHit, out NetworkElementType hitElement,
                out int hitArcIndex, out _)
            || hitElement is not (NetworkElementType.ArcSegment or NetworkElementType.ArcInternalPoint)
            || hitArcIndex != arcIndex)
            throw new SourceFileChangedException();
        if (!NetworkRenderLayer.HitTestArcs(topology, capturedHit, out _, out IList<FeatureSegmentSubset> hitArcs)
            || !hitArcs.Any(hitArc => hitArc.Fid == arcIndex && hitArc.Contains(target.SourceSegmentIndex)))
            throw new SourceFileChangedException();

        if (splitMode != "AtVertex") return capturedHit;
        if (arcLine.Count < 3) return null;
        int nearestVertex = arcLine.NearestPoint(PointFor(target.Point));
        if (nearestVertex == 0) nearestVertex = 1;
        else if (nearestVertex == arcLine.Count - 1) nearestVertex = arcLine.Count - 2;
        Point vertex = arcLine[nearestVertex];
        return Hit(new GeometryTopologyCoordinate(vertex.X, vertex.Y), cellSize);
    }

    private static void SplitExactArc(ObservableConceptualMeshTopology observable,
        TopologyEditContext editContext, TopologyCompletionContext completionContext,
        GeometryTopologyMapCommand command)
    {
        Topology topology = observable.Topology;
        int arcIndex = ResolveSingleIndex(observable.Arcs, command.ArcFID, arc => arc.Fid, "arc");
        Point query = PointFor(command.Coordinates![0]);
        Polyline line = topology.GetArcPolyline(arcIndex);
        if (command.SplitMode is not (null or "Anywhere" or "AtVertex"))
            throw new ArgumentException("The source SplitArc mode is invalid.");
        if (command.SplitMode == "AtVertex")
        {
            if (line.Count < 3) throw new ArgumentException("This arc has no internal vertex to split at.");
            int nearest = line.NearestPoint(query);
            nearest = Math.Clamp(nearest, 1, line.Count - 2);
            query = line[nearest];
        }

        if (!topology.HitTestArcs(query, 5d * command.MapCellSize, 5d * command.MapCellSize,
                out _, out IList<FeatureSegmentSubset> hitArcs))
            throw new ArgumentException("The map point does not hit the selected source arc.");
        int[] hitFIDs = hitArcs.Select(hit => hit.Fid).Distinct().ToArray();
        if (hitFIDs.Length != 1 || hitFIDs[0] != arcIndex)
            throw new ArgumentException("The split hit would affect a different or overlapping source arc.");
        var hit = Hit(new GeometryTopologyCoordinate(query.X, query.Y), command.MapCellSize);
        if (!topology.HitTestArcs(query, 5d * command.MapCellSize, 5d * command.MapCellSize,
                out _, out hitArcs) || hitArcs.Select(item => item.Fid).Distinct().Any(fid => fid != arcIndex))
            throw new ArgumentException("The split target changed during hit testing.");
        ExecuteTopologyOperation(completionContext, new SplitArcOperation(editContext, hit));
    }

    private static bool TransformArcs(ObservableConceptualMeshTopology observable,
        TopologyEditContext editContext, TopologyCompletionContext completionContext,
        GeometryTopologyMapCommand command)
    {
        int[] arcIndices = ResolveSourceFeatureIndices(observable.Arcs, command.FeatureIDs,
            arc => arc.Fid, "arc");
        if (arcIndices.Length == 0)
            throw new ArgumentException("Select at least one source arc to transform.");
        double epsilon = command.Epsilon ?? 1d;
        int window = command.SmoothWindowSize ?? 3;
        int iterations = command.SmoothIterations ?? 2;
        if (command.Action == "filterArc" && (!double.IsFinite(epsilon) || epsilon < 0d))
            throw new ArgumentException("The source arc-filter tolerance must be finite and nonnegative.");
        if (command.Action == "smoothArc" && (window < 1 || window > 1001 || iterations < 1 || iterations > 1000))
            throw new ArgumentException("The source arc-smoothing parameters are outside supported bounds.");

        bool changed = false;
        foreach (int arcIndex in arcIndices.OrderBy(index => observable.Arcs[index].Fid))
        {
            Arc arc = observable.Topology.Arcs[arcIndex];
            if (arc.InternalPointCount == 0)
                throw new ArgumentException("A selected source arc has no internal points to transform.");
            Polyline full = observable.Topology.GetArcPolyline(arcIndex);
            Polyline transformed = command.Action == "filterArc"
                ? new Polyline(full.DouglasPeuckerReduction(epsilon))
                : full.CloneWithMovingAverageSmoothing(window, iterations);
            var newPoints = new PointCollection(Math.Max(0, transformed.Count - 2));
            for (int index = 1; index < transformed.Count - 1; index++) newPoints.Add(transformed[index]);
            if (arc.InternalPoints.SequenceEqual(newPoints)) continue;
            ExecuteTopologyOperation(completionContext, new ReplaceArcInternalPointsOperation(editContext,
                arcIndex, newPoints, command.Action == "filterArc" ? "Filter arc" : "Smooth arc"));
            changed = true;
        }
        return changed;
    }

    private static int ResolveSingleIndex<T>(IEnumerable<T> rows, int? fid, Func<T, int> getFID,
        string sourceName)
    {
        if (fid is not int value || value < 0)
            throw new ArgumentException("A topology map command requires one exact source " + sourceName + " FID.");
        return ResolveSourceFeatureIndices(rows, [value], getFID, sourceName).Single();
    }

    private static void ExecuteTopologyOperation(TopologyCompletionContext context, EditOperation operation)
    {
        if (!context.AttachListenersAndExecute(operation))
            throw new InvalidOperationException("The retained source topology edit stack rejected a map operation.");
    }

    private static HitTestArgs Hit(GeometryTopologyCoordinate coordinate, double cellSize) =>
        HitTestArgs.Generate(PointFor(coordinate), cellSize);

    private static Point PointFor(GeometryTopologyCoordinate coordinate) => new(coordinate.X, coordinate.Y);

    private static void ValidateCoordinate(GeometryTopologyCoordinate point)
    {
        if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("Topology map coordinates must be finite.");
    }

    private static void ValidatePolylineCoordinates(GeometryTopologyCoordinate[] points)
    {
        if (points is null || points.Length < 2 || points.Length > MaximumMapCommandCoordinates)
            throw new ArgumentException("A topology map line requires an ordered finite path.");
        foreach (GeometryTopologyCoordinate point in points) ValidateCoordinate(point);
        for (int index = 1; index < points.Length; index++)
            if (points[index - 1].X == points[index].X && points[index - 1].Y == points[index].Y)
                throw new ArgumentException("A topology map line cannot contain duplicate adjacent coordinates.");
    }
}
