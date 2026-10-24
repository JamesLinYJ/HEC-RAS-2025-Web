using System.Text.Json;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Ras.Layers;
using Visual.Mapping.Observables;

namespace Ras.Backend;

/// <summary>One exact vector node from the currently selected saved map.</summary>
public sealed record GeometryTopologyVectorSourceLayerRequest(
    CatalogTreeNodeLocator Node,
    MapSourceFamilyCapture? ExpectedFamily = null);

/// <summary>
/// Reads persisted vector components that the existing Geometry DeepConnect
/// resolver can identify without guessing renderer feature IDs.
/// </summary>
public sealed record GeometryTopologyVectorSourceInspectRequest(
    string RequestID,
    MapTreeRequest Map,
    GeometryTopologyVectorSourceLayerRequest[] Sources);

/// <summary>Exact saved map, project session, .vis and external core owner.</summary>
public sealed record GeometryTopologyVectorSourceMapIdentity(
    string ProjectPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string? ProjectSessionID,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string VisualizationPath,
    string VisualizationRevision,
    string? CoreLayerContextIdentityToken);

/// <summary>
/// One renderer line component. FeatureFID, pathIndex and componentIdentity
/// follow GeometryTopologyMapService.ReadPersistedDeepConnectVector exactly;
/// source ordinals are supplementary provenance only.
/// </summary>
public sealed record GeometryTopologyVectorSourceComponent(
    int FeatureFID,
    int PathIndex,
    string ComponentIdentity,
    GeometryTopologyCoordinate[] Points,
    int SourceFeatureOrdinal,
    int SourcePartOrdinal,
    int? SourcePolygonIndex = null,
    bool? SourceIsHole = null);

/// <summary>Capture and explicit availability diagnostic for one map node.</summary>
public sealed record GeometryTopologyVectorSourceLayerResult(
    CatalogTreeNodeLocator Node,
    bool Checked,
    bool EffectiveVisible,
    bool CanDeepConnect,
    string? SourceKind,
    string? CanonicalSourcePath,
    MapSourceFamilyCapture? SourceFamily,
    string? SourceSHA256,
    GeometryTopologyVectorSourceComponent[] Components,
    string[] UnsupportedGeometryTypes,
    string DiagnosticCode,
    string? Diagnostic,
    int OversizedComponentCount = 0);

/// <summary>Read-only DeepConnect source inspection bound to one exact map owner.</summary>
public sealed record GeometryTopologyVectorSourceInspectResult(
    string RequestID,
    GeometryTopologyVectorSourceMapIdentity Owner,
    GeometryTopologyVectorSourceLayerResult[] Sources);

/// <summary>
/// Resolves persisted map vectors through the same source-family and retained
/// geometry readers used by rendering and DeepConnect. This service never
/// writes the map, vector source, Geometry draft, or HDF files.
/// </summary>
public static class GeometryTopologyVectorSourceService
{
    private const int MaximumSourceLayers = 128;
    private const int MaximumResponseComponents = 100_000;
    private const int MaximumResponsePoints = 1_000_000;
    // Keep aligned with GeometryTopologyMapService.MaximumMapCommandCoordinates.
    private const int MaximumComponentPoints = 100_000;
    private const long MaximumGeoJsonBytes = 128L * 1024 * 1024;
    private const int MaximumGeoJsonFeatures = 250_000;

    private sealed record Visibility(bool Checked, bool EffectiveVisible);
    private sealed record GeoJsonGeometryRows(string?[] Types);

    public static GeometryTopologyVectorSourceInspectResult Inspect(
        GeometryTopologyVectorSourceInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ValidateRequest(request);

        MapSourceMapCapture mapCapture = MapSourceIdentity.CaptureMap(request.Map);
        string? projectSessionID = ResolveProjectSessionID(request.Map);
        var owner = ToMapIdentity(request.Map, mapCapture, projectSessionID);
        MapTreeSnapshot beforeTree = MapTreeService.Load(request.Map);
        if (!SameRevision(beforeTree.VisualizationRevision, owner.VisualizationRevision))
            throw new SourceFileChangedException();

        var captured = new List<(GeometryTopologyVectorSourceLayerRequest Request,
            MapSourceNodeCapture? Source, GeometryTopologyVectorSourceLayerResult Result)>(request.Sources.Length);
        int totalComponents = 0;
        int totalPoints = 0;

        foreach (GeometryTopologyVectorSourceLayerRequest sourceRequest in request.Sources)
        {
            Visibility visibility = FindVisibility(beforeTree.Roots ?? [], sourceRequest.Node)
                ?? throw new SourceFileChangedException();
            if (!visibility.Checked || !visibility.EffectiveVisible)
            {
                captured.Add((sourceRequest, null, Unavailable(sourceRequest.Node, visibility,
                    "source-not-visible", "图层未勾选或父级未显示，不能作为 DeepConnect 来源。")));
                continue;
            }

            GeometryTopologyVectorSourceLayerResult result = InspectSource(request.Map, sourceRequest,
                visibility, out MapSourceNodeCapture? sourceCapture);

            GeometryTopologyVectorSourceComponent[] oversizedComponents = result.Components
                .Where(component => component.Points.Length > MaximumComponentPoints).ToArray();
            if (oversizedComponents.Length > 0)
            {
                GeometryTopologyVectorSourceComponent[] usableComponents = result.Components
                    .Where(component => component.Points.Length <= MaximumComponentPoints).ToArray();
                string pointLimitDiagnostic = $"已跳过 {oversizedComponents.Length} 个超过单组件 {MaximumComponentPoints} 点上限的组件；剩余组件仍按原始来源编号返回。";
                result = result with
                {
                    CanDeepConnect = usableComponents.Length > 0,
                    Components = usableComponents,
                    DiagnosticCode = usableComponents.Length > 0
                        ? "partial-component-point-limit"
                        : "component-point-limit-exceeded",
                    Diagnostic = string.Join(" ", new[] { result.Diagnostic, pointLimitDiagnostic }
                        .Where(message => !string.IsNullOrWhiteSpace(message))),
                    OversizedComponentCount = oversizedComponents.Length
                };
            }

            long pointCount = result.Components.Sum(component => (long)component.Points.Length);
            if (result.Components.Length > MaximumResponseComponents - totalComponents ||
                pointCount > MaximumResponsePoints - totalPoints)
            {
                result = result with
                {
                    CanDeepConnect = false,
                    Components = [],
                    DiagnosticCode = "response-limit-exceeded",
                    Diagnostic = $"可返回的组件或坐标超过上限（{MaximumResponseComponents} 个组件、{MaximumResponsePoints} 个点）；请只检查较少图层。"
                };
            }
            else
            {
                totalComponents += result.Components.Length;
                totalPoints += (int)pointCount;
            }

            captured.Add((sourceRequest, sourceCapture, result));
        }

        MapTreeSnapshot afterTree = MapTreeService.Load(request.Map);
        if (!SameRevision(afterTree.VisualizationRevision, owner.VisualizationRevision))
            throw new SourceFileChangedException();

        foreach ((GeometryTopologyVectorSourceLayerRequest sourceRequest, MapSourceNodeCapture? source,
                     GeometryTopologyVectorSourceLayerResult result) in captured)
        {
            Visibility afterVisibility = FindVisibility(afterTree.Roots ?? [], sourceRequest.Node)
                ?? throw new SourceFileChangedException();
            if (source is not null)
            {
                if (!result.Checked || !result.EffectiveVisible || !afterVisibility.Checked ||
                    !afterVisibility.EffectiveVisible)
                    throw new SourceFileChangedException();
                MapSourceIdentity.VerifyPersistedNode(request.Map, source);
            }
            else if (result.Checked != afterVisibility.Checked ||
                     result.EffectiveVisible != afterVisibility.EffectiveVisible)
            {
                throw new SourceFileChangedException();
            }

            if (source is null && result.SourceKind == "gdb" &&
                result.CanonicalSourcePath is string gdbPath)
            {
                MapSourceNodeLocation currentGdb = MapSourceIdentity.ResolvePersistedGdbNode(request.Map,
                    sourceRequest.Node);
                MapSourceIdentity.VerifyPersistedGdbNode(request.Map, currentGdb);
                if (!SourceFilePaths.SameSourceFile(gdbPath, currentGdb.SourcePath))
                    throw new SourceFileChangedException();
            }
        }

        MapSourceIdentity.VerifyMap(request.Map, mapCapture);
        return new(request.RequestID, owner, captured.Select(item => item.Result).ToArray());
    }

    private static GeometryTopologyVectorSourceLayerResult InspectSource(MapTreeRequest map,
        GeometryTopologyVectorSourceLayerRequest request, Visibility visibility,
        out MapSourceNodeCapture? sourceCapture)
    {
        sourceCapture = null;
        CatalogTreeNodeLocator node = request.Node;

        if (string.Equals(node.NodeType, "GdbVectorLayer", StringComparison.Ordinal) ||
            string.Equals(node.MapType, "GdbVectorLayer", StringComparison.Ordinal))
        {
            MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedGdbNode(map, node);
            MapSourceIdentity.VerifyPersistedGdbNode(map, location);
            return Unavailable(node, visibility, "gdb-deepconnect-unsupported",
                "此版本尚未为 GDB 图层定义与地图渲染一致的 DeepConnect 组件编号；GDB 来源不会生成可提交的组件。",
                "gdb", location.SourcePath);
        }

        if (!string.Equals(node.NodeType, "MapLayer", StringComparison.Ordinal))
            return Unavailable(node, visibility, "unsupported-node-type",
                "所选节点不是已保存地图中的矢量图层。", sourceKind: null);

        MapSourceNodeLocation nodeLocation = MapSourceIdentity.ResolvePersistedNode(map, node);
        string canonicalPath = nodeLocation.SourcePath;
        string sourceKind = SourceKind(nodeLocation.PresentationPath ?? canonicalPath);

        if (node.MapType is not ("PolylinesVector" or "PolygonsVector" or "PointsVector"))
            return Unavailable(node, visibility, "unsupported-map-layer-type",
                "此地图图层类型没有已定义的 DeepConnect 组件编号。", sourceKind, canonicalPath);

        if (sourceKind is not ("shapefile" or "geojson"))
            return Unavailable(node, visibility, "unsupported-source-format",
                "DeepConnect 目前只支持持久化 Shapefile 与 GeoJSON 矢量来源。",
                sourceKind, canonicalPath);

        try
        {
            MapVectorSourceLoadResult loaded = MapVectorSourceService.Load(
                new MapVectorSourceLoadRequest(map, node, request.ExpectedFamily));
            sourceCapture = loaded.Source;

            string? sourceSHA = PrimarySourceSHA256(loaded.Source.Family);
            if (node.MapType == "PointsVector")
            {
                return new(node, visibility.Checked, visibility.EffectiveVisible, false,
                    sourceKind, loaded.Source.Family.SourcePath, loaded.Source.Family, sourceSHA,
                    [], [], "point-layer-unsupported", "DeepConnect 只接受线组件；点图层已捕获来源，但没有可提交的线组件。");
            }

            List<GeometryTopologyVectorSourceComponent> components;
            string[] unsupportedGeometryTypes = [];
            if (sourceKind == "shapefile" && node.MapType == "PolylinesVector")
            {
                components = ShapefilePolylineComponents(loaded.Data);
            }
            else
            {
                components = ReadRendererComponents(map, node, canonicalPath, sourceKind, loaded.Data,
                    out unsupportedGeometryTypes);
            }

            string diagnosticCode = components.Count == 0
                ? unsupportedGeometryTypes.Length > 0 ? "unsupported-multipart-geometry" : "no-line-components"
                : unsupportedGeometryTypes.Length > 0 ? "partial-unsupported-geometry" : "ready";
            string? diagnostic = diagnosticCode switch
            {
                "unsupported-multipart-geometry" => "来源中只有当前 Host 未定义组件编号的多部件 GeoJSON 几何；未生成 DeepConnect 组件。",
                "no-line-components" => "此来源没有可映射到当前 DeepConnect Host 线读取器的有效组件。",
                "partial-unsupported-geometry" => $"已列出可精确映射的线组件；同时跳过不支持的 GeoJSON 类型：{string.Join("、", unsupportedGeometryTypes)}。",
                _ => null
            };

            return new(node, visibility.Checked, visibility.EffectiveVisible, components.Count > 0,
                sourceKind, loaded.Source.Family.SourcePath, loaded.Source.Family, sourceSHA,
                components.ToArray(), unsupportedGeometryTypes, diagnosticCode, diagnostic);
        }
        catch (SourceFileChangedException)
        {
            throw;
        }
        catch (NotSupportedException exception)
        {
            return Unavailable(node, visibility, "unsupported-source-content", exception.Message,
                sourceKind, sourceCapture?.Family.SourcePath ?? canonicalPath,
                sourceCapture?.Family, sourceCapture is null ? null : PrimarySourceSHA256(sourceCapture.Family));
        }
        catch (InvalidDataException exception)
        {
            return Unavailable(node, visibility, "invalid-source-content", exception.Message,
                sourceKind, sourceCapture?.Family.SourcePath ?? canonicalPath,
                sourceCapture?.Family, sourceCapture is null ? null : PrimarySourceSHA256(sourceCapture.Family));
        }
        catch (IOException exception)
        {
            return Unavailable(node, visibility, "source-read-failed", exception.Message,
                sourceKind, sourceCapture?.Family.SourcePath ?? canonicalPath,
                sourceCapture?.Family, sourceCapture is null ? null : PrimarySourceSHA256(sourceCapture.Family));
        }
    }

    private static List<GeometryTopologyVectorSourceComponent> ShapefilePolylineComponents(
        MapVectorSourceData data)
    {
        var components = new List<GeometryTopologyVectorSourceComponent>();
        foreach (MapVectorSourceFeature feature in data.Features.OrderBy(feature => feature.SourceOrdinal))
        {
            foreach (MapVectorSourcePart part in feature.Parts.OrderBy(part => part.PartOrdinal))
            {
                GeometryTopologyCoordinate[] points = ProjectedPoints(part.Vertices);
                if (points.Length < 2) continue;
                int featureFID = feature.SourceOrdinal;
                int pathIndex = part.PartOrdinal;
                components.Add(Component(featureFID, pathIndex, points, feature.SourceOrdinal,
                    part.PartOrdinal, part.PolygonIndex, part.IsHole));
            }
        }
        return components;
    }

    private static List<GeometryTopologyVectorSourceComponent> ReadRendererComponents(MapTreeRequest map,
        CatalogTreeNodeLocator node, string canonicalPath, string sourceKind, MapVectorSourceData data,
        out string[] unsupportedGeometryTypes)
    {
        GeoJsonGeometryRows? geoJsonRows = sourceKind == "geojson"
            ? ReadGeoJsonGeometryRows(canonicalPath)
            : null;
        string expectedGeometryType = node.MapType == "PolylinesVector" ? "LineString" : "Polygon";
        unsupportedGeometryTypes = geoJsonRows?.Types
            .Where(type => IsUnsupportedMultiType(type, expectedGeometryType))
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        Project project = CoreLayerImportService.OpenProject(Path.GetFullPath(map.ProjectPath), map.ProjectXml,
            map.CoreLayerContext);
        try
        {
            if (node.MapType == "PolylinesVector")
            {
                var read = PolylineMapLayerVisualContext.RenderLayerFrom(canonicalPath, project.Projection,
                    out IPolylineCollection rendered, out _, out _);
                if (!read.Result || rendered is null)
                    throw new InvalidDataException("The retained Polyline map reader did not return its source components.");

                IEnumerable<MapVectorSourceFeature> sourceRows;
                if (geoJsonRows is not null)
                {
                    int[] ordinals = geoJsonRows.Types.Select((type, ordinal) => (type, ordinal))
                        .Where(item => IsExactGeometryType(item.type, "LineString"))
                        .Select(item => item.ordinal).ToArray();
                    if (ordinals.Length != rendered.Count)
                        throw new InvalidDataException("GeoJSON LineString reader order did not match the retained map renderer.");
                    sourceRows = ordinals.Select(ordinal => FindSourceFeature(data, ordinal));
                }
                else
                {
                    // Shapefile line DeepConnect uses the record ordinal and source part,
                    // not the renderer's exploded line ordinal. Handle this separately.
                    throw new InvalidOperationException("Shapefile polyline components use their source record and part identity.");
                }

                MapVectorSourceFeature[] orderedRows = sourceRows.ToArray();
                var components = new List<GeometryTopologyVectorSourceComponent>(rendered.Count);
                for (int fid = 0; fid < rendered.Count; fid++)
                {
                    MapVectorSourceFeature feature = orderedRows[fid];
                    MapVectorSourcePart? part = feature.Parts.SingleOrDefault(candidate => candidate.PartOrdinal == 0);
                    if (part is null) throw new InvalidDataException("A GeoJSON LineString source row has no unique source path.");
                    GeometryTopologyCoordinate[] points = ProjectedPoints(rendered[fid]);
                    if (points.Length < 2) continue;
                    components.Add(Component(fid, 0, points, feature.SourceOrdinal, part.PartOrdinal,
                        part.PolygonIndex, part.IsHole));
                }
                return components;
            }

            var polygonRead = PolygonMapLayerVisualContext.RenderLayerFrom(canonicalPath, project.Projection,
                out IPolygonCollection polygons, out _, out _);
            if (!polygonRead.Result || polygons is null)
                throw new InvalidDataException("The retained Polygon map reader did not return its source components.");
            ILineSegmentSource polygonLines = polygons;
            int lineCount = polygons.Count;

            MapVectorSourcePart[] orderedParts;
            if (geoJsonRows is not null)
            {
                int[] ordinals = geoJsonRows.Types.Select((type, ordinal) => (type, ordinal))
                    .Where(item => IsExactGeometryType(item.type, "Polygon"))
                    .Select(item => item.ordinal).ToArray();
                if (ordinals.Length != lineCount)
                    throw new InvalidDataException("GeoJSON Polygon outer-ring reader order did not match the retained map renderer.");
                orderedParts = ordinals.Select(ordinal =>
                {
                    MapVectorSourceFeature feature = FindSourceFeature(data, ordinal);
                    return feature.Parts.SingleOrDefault(part => part.PartOrdinal == 0)
                        ?? throw new InvalidDataException("A GeoJSON Polygon source row has no unique outer ring.");
                }).ToArray();
                int[] sourceOrdinals = ordinals;
                var components = new List<GeometryTopologyVectorSourceComponent>(lineCount);
                for (int fid = 0; fid < lineCount; fid++)
                {
                    GeometryTopologyCoordinate[] points = ProjectedPoints(polygonLines.GetLine(fid));
                    if (points.Length < 2) continue;
                    MapVectorSourceFeature feature = FindSourceFeature(data, sourceOrdinals[fid]);
                    MapVectorSourcePart part = orderedParts[fid];
                    components.Add(Component(fid, 0, points, feature.SourceOrdinal, part.PartOrdinal,
                        part.PolygonIndex, part.IsHole));
                }
                return components;
            }

            (MapVectorSourceFeature Feature, MapVectorSourcePart Part)[] shapefileRows = data.Features
                .OrderBy(feature => feature.SourceOrdinal)
                .SelectMany(feature => feature.Parts.OrderBy(part => part.PartOrdinal)
                    .Select(part => (feature, part)))
                .ToArray();
            if (shapefileRows.Length != lineCount)
                throw new InvalidDataException("Shapefile ring order did not match the retained map renderer.");
            var shapefileComponents = new List<GeometryTopologyVectorSourceComponent>(lineCount);
            for (int fid = 0; fid < lineCount; fid++)
            {
                GeometryTopologyCoordinate[] points = ProjectedPoints(polygonLines.GetLine(fid));
                (MapVectorSourceFeature feature, MapVectorSourcePart part) = shapefileRows[fid];
                GeometryTopologyCoordinate[] sourcePoints = ProjectedPoints(part.Vertices);
                if (!EqualPoints(points, sourcePoints))
                    throw new InvalidDataException("Shapefile ring source row/part did not match the renderer's exact point order.");
                if (points.Length < 2) continue;
                shapefileComponents.Add(Component(fid, 0, points, feature.SourceOrdinal, part.PartOrdinal,
                    part.PolygonIndex, part.IsHole));
            }
            return shapefileComponents;
        }
        finally
        {
            MapResultChartService.DisposeProject(project);
        }
    }

    private static GeometryTopologyVectorSourceComponent Component(int featureFID, int pathIndex,
        GeometryTopologyCoordinate[] points, int sourceFeatureOrdinal, int sourcePartOrdinal,
        int? sourcePolygonIndex, bool? sourceIsHole) => new(
            featureFID, pathIndex, $"feature:{featureFID}:part:{pathIndex}", points,
            sourceFeatureOrdinal, sourcePartOrdinal, sourcePolygonIndex, sourceIsHole);

    private static GeometryTopologyVectorSourceLayerResult Unavailable(CatalogTreeNodeLocator node,
        Visibility visibility, string code, string diagnostic, string? sourceKind = null,
        string? canonicalSourcePath = null, MapSourceFamilyCapture? sourceFamily = null,
        string? sourceSHA256 = null) => new(node, visibility.Checked, visibility.EffectiveVisible,
        false, sourceKind, canonicalSourcePath, sourceFamily, sourceSHA256, [], [], code, diagnostic);

    private static string? PrimarySourceSHA256(MapSourceFamilyCapture family)
    {
        MapSourceFileRevision[] sources = family.Files.Where(file => file.Role == "source" &&
            file.Required && file.Exists && file.Revision is not null).ToArray();
        if (sources.Length != 1 || !IsSHA256(sources[0].Revision))
            throw new SourceFileChangedException();
        return sources[0].Revision;
    }

    private static MapVectorSourceFeature FindSourceFeature(MapVectorSourceData data, int sourceOrdinal) =>
        data.Features.SingleOrDefault(feature => feature.SourceOrdinal == sourceOrdinal)
        ?? throw new InvalidDataException("A retained renderer component has no matching source-row identity.");

    private static GeometryTopologyCoordinate[] ProjectedPoints(MapVectorSourceVertex[] vertices) =>
        vertices.Select(vertex => new GeometryTopologyCoordinate(vertex.X, vertex.Y)).ToArray();

    private static GeometryTopologyCoordinate[] ProjectedPoints(Polyline line)
    {
        var points = new GeometryTopologyCoordinate[line.Count];
        for (int index = 0; index < line.Count; index++)
        {
            if (!double.IsFinite(line[index].X) || !double.IsFinite(line[index].Y))
                throw new InvalidDataException("The retained renderer returned a non-finite project coordinate.");
            points[index] = new(line[index].X, line[index].Y);
        }
        return points;
    }

    private static bool EqualPoints(GeometryTopologyCoordinate[] left, GeometryTopologyCoordinate[] right) =>
        left.Length == right.Length && left.Zip(right).All(pair =>
            pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y);

    private static GeoJsonGeometryRows ReadGeoJsonGeometryRows(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("The GeoJSON map source was not found.", sourcePath);
        if (info.Length <= 0 || info.Length > MaximumGeoJsonBytes)
            throw new NotSupportedException("The GeoJSON source exceeds the map vector size limit.");

        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > MaximumGeoJsonBytes || stream.Length > int.MaxValue)
            throw new NotSupportedException("The GeoJSON source exceeds the map vector size limit.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new NotSupportedException("The GeoJSON source grew beyond the map vector size limit while it was read.");
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            MaxDepth = 64,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out string rootType))
            throw new InvalidDataException("GeoJSON 根节点必须是 FeatureCollection 或 Feature。");

        JsonElement[] features;
        if (GeoJsonTypes.Matches(rootType, GeoJsonTypes.FeatureCollection))
        {
            if (!root.TryGetProperty("features", out JsonElement featureArray) ||
                featureArray.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GeoJSON FeatureCollection 缺少有效的 features 数组。");
            if (featureArray.GetArrayLength() > MaximumGeoJsonFeatures)
                throw new NotSupportedException("GeoJSON 要素数量超过地图读取上限。");
            features = featureArray.EnumerateArray().ToArray();
        }
        else if (GeoJsonTypes.Matches(rootType, GeoJsonTypes.Feature))
        {
            features = [root];
        }
        else
        {
            throw new NotSupportedException("DeepConnect 仅支持 GeoJSON Feature 或 FeatureCollection 根节点。");
        }

        var types = new string?[features.Length];
        for (int index = 0; index < features.Length; index++)
        {
            JsonElement feature = features[index];
            if (feature.ValueKind != JsonValueKind.Object ||
                !feature.TryGetProperty("geometry", out JsonElement geometry) ||
                geometry.ValueKind == JsonValueKind.Null)
                continue;
            if (geometry.ValueKind != JsonValueKind.Object || !TryGetString(geometry, "type", out string geometryType))
                throw new InvalidDataException("GeoJSON geometry 缺少有效的 type 字段。");
            types[index] = geometryType;
        }
        return new(types);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool IsExactGeometryType(string? type, string expected) =>
        type is not null && GeoJsonTypes.Matches(type, expected);

    private static bool IsUnsupportedMultiType(string? type, string expected) =>
        type is not null && (expected == "LineString"
            ? GeoJsonTypes.IsMultiLineString(type)
            : GeoJsonTypes.IsMultiPolygon(type));

    private static string SourceKind(string presentationPath) => Path.GetExtension(presentationPath) switch
    {
        var extension when extension.Equals(".shp", StringComparison.OrdinalIgnoreCase) => "shapefile",
        var extension when extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase) => "geojson",
        _ => "unsupported"
    };

    private static Visibility? FindVisibility(IEnumerable<CatalogTreeNode> nodes,
        CatalogTreeNodeLocator target, bool ancestorsVisible = true)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            bool currentVisible = ancestorsVisible &&
                !string.Equals(node.Checked, "unchecked", StringComparison.Ordinal);
            if (node.Locator is CatalogTreeNodeLocator locator && SameLocator(locator, target))
                return new(string.Equals(node.Checked, "checked", StringComparison.Ordinal),
                    currentVisible && string.Equals(node.Checked, "checked", StringComparison.Ordinal));
            if (node.Children is { Length: > 0 })
            {
                Visibility? child = FindVisibility(node.Children, target, currentVisible);
                if (child is not null) return child;
            }
        }
        return null;
    }

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        left.RootName == right.RootName && left.NodeType == right.NodeType && left.NodeName == right.NodeName &&
        SameArray(left.NodePath, right.NodePath) && left.AssetID == right.AssetID && left.MapType == right.MapType &&
        left.Filename == right.Filename && left.FilenameRelative == right.FilenameRelative &&
        left.ParentGeometryAssetID == right.ParentGeometryAssetID &&
        SameArray(left.ParentGeometryNodePath, right.ParentGeometryNodePath) &&
        left.ParentGeometryNodeName == right.ParentGeometryNodeName &&
        left.AssociationAssetID == right.AssociationAssetID && left.AssociationKind == right.AssociationKind &&
        left.ParentPlanAssetID == right.ParentPlanAssetID &&
        SameArray(left.ParentPlanNodePath, right.ParentPlanNodePath) && left.ParentPlanNodeName == right.ParentPlanNodeName &&
        left.ParentResultNodeType == right.ParentResultNodeType && left.ParentResultAssetID == right.ParentResultAssetID &&
        SameArray(left.ParentResultNodePath, right.ParentResultNodePath) &&
        left.ParentResultNodeName == right.ParentResultNodeName &&
        SameRevision(left.ParentResultRevision, right.ParentResultRevision) &&
        left.ParentBoundaryConditionAssetID == right.ParentBoundaryConditionAssetID &&
        SameArray(left.ParentBoundaryConditionNodePath, right.ParentBoundaryConditionNodePath) &&
        left.ParentBoundaryConditionNodeName == right.ParentBoundaryConditionNodeName &&
        Equals(left.SessionHdfResultOwner, right.SessionHdfResultOwner);

    private static bool SameArray(int[]? left, int[]? right) => left is null
        ? right is null
        : right is not null && left.SequenceEqual(right);

    private static GeometryTopologyVectorSourceMapIdentity ToMapIdentity(MapTreeRequest request,
        MapSourceMapCapture capture, string? projectSessionID) => new(
        capture.ProjectPath, capture.ProjectRevision, capture.ProjectXmlSHA, projectSessionID,
        capture.MapID ?? throw new SourceFileChangedException(),
        capture.MapIndex ?? throw new SourceFileChangedException(),
        capture.MapName ?? throw new SourceFileChangedException(), capture.MapType,
        capture.VisualizationPath,
        capture.VisualizationRevision ?? throw new SourceFileChangedException(),
        request.CoreLayerContext?.IdentityToken);

    private static string? ResolveProjectSessionID(MapTreeRequest map)
    {
        string? contextSession = map.CoreLayerContext?.ProjectSessionID;
        if (map.ProjectSessionID is not null && contextSession is not null &&
            !string.Equals(map.ProjectSessionID, contextSession, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return map.ProjectSessionID ?? contextSession;
    }

    private static void ValidateRequest(GeometryTopologyVectorSourceInspectRequest request)
    {
        if (!Guid.TryParseExact(request.RequestID, "D", out _) &&
            !Guid.TryParseExact(request.RequestID, "N", out _))
            throw new ArgumentException("Vector source inspection requires a GUID requestID.", nameof(request.RequestID));
        if (request.Sources is null || request.Sources.Length is < 1 or > MaximumSourceLayers ||
            request.Sources.Any(source => source is null || source.Node is null))
            throw new ArgumentException($"Vector source inspection accepts between one and {MaximumSourceLayers} exact map nodes.", nameof(request.Sources));
        for (int left = 0; left < request.Sources.Length; left++)
        for (int right = left + 1; right < request.Sources.Length; right++)
            if (SameLocator(request.Sources[left].Node, request.Sources[right].Node))
                throw new ArgumentException("A map node may appear only once in a vector source inspection.", nameof(request.Sources));
        _ = ResolveProjectSessionID(request.Map);
    }

    private static bool IsSHA256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool SameRevision(string? left, string? right) => left is null
        ? right is null
        : right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
