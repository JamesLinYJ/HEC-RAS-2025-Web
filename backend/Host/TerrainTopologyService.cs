using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Handlers;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using System.Reflection;
using System.Data;
using System.Globalization;
using Ras.Layers.Terrains;
using Ras.Observables;
using Ras.Observables.Layers;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static async Task<TerrainTopologyResponse> TerrainTopologyAsync(TerrainTopologyRequest request)
    {
        CheckTerrain(request.Model);
        using var context = new ModelContext(request.Model);
        TerrainEditor editor = TerrainInputs(context, request.Model.AssetPath, request.Model.Revision,
            request.Model.SourceState) ?? throw new ArgumentException("This model is not a terrain.");
        editor.CaptureSelectedInput(request.Model.Fields);
        editor.ApplySelections(request.Model.TableSelections);
        ApplyFields(context, null, request.Model.Fields);
        editor.Apply(request.Model.Fields);
        editor.ApplyRows(request.Model.Rows, request.Model.ChangedCells);
        editor.VerifyDependencies();
        TerrainTopologyData topology = editor.ApplyTopologyOperation(request);
        ModelSnapshot snapshot = await Snapshot(context, request.Model.Revision, true, null, terrain: editor);
        editor.VerifyDependencies();
        CheckTerrain(request.Model);
        return new TerrainTopologyResponse(snapshot, topology);
    }
}

/// <summary>
/// Runs polygon raster modifications through the retained vector collection,
/// EditStack, NewFeatureOperation, and DeleteFeatureOperation contracts.
/// The enclosing TerrainEditor owns the durable parent-source journal.
/// </summary>
internal sealed class TerrainPolygonTopologyEditSession
{
    private readonly string _inputNodeID;
    private readonly PolygonRasterModTerrainInput _input;
    private readonly ObservablePolygonRasterModTerrainInput _observable;
    private readonly ObservablePolygonFtLayerWrapped<PolygonRasterModTerrainInput, PolygonRasterMod, ObservablePolygonRasterMod> _polygons;
    private readonly SelectedSet _selected = new();
    private readonly EditStack _editStack = new();
    private TerrainPolygonEditSettings _currentSettings = new("Triangle", 500, 500, 0, 1000, "SetValue");

    public TerrainPolygonTopologyEditSession(string inputNodeID, PolygonRasterModTerrainInput input,
        ObservableTerrain terrain)
    {
        _inputNodeID = inputNodeID;
        _input = input;
        _observable = new ObservablePolygonRasterModTerrainInput(terrain, input);
        _polygons = new ObservablePolygonFtLayerWrapped<PolygonRasterModTerrainInput, PolygonRasterMod, ObservablePolygonRasterMod>(
            _observable, feature => feature.Polygon, polygon => new PolygonRasterMod(polygon,
                ParseModificationType(_currentSettings.ModificationType), _currentSettings.Value));
    }

    public TerrainTopologyFeature[] Features(Func<TerrainMapCoordinate[], TerrainMapCoordinate[]> toMap)
    {
        var features = new TerrainTopologyFeature[_input.Count];
        for (int index = 0; index < _input.Count; index++)
        {
            Polygon polygon = _input[index].Polygon;
            if (!Polygon.IsValidPolygon(polygon) || polygon.GetPointBufferUnsafe().ToArray().Any(point =>
                    !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new InvalidDataException("A source polygon raster modification contains invalid geometry.");
            TerrainMapCoordinate[] source = polygon.GetPointBufferUnsafe().Span
                .ToArray().Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray();
            features[index] = new TerrainTopologyFeature(FeatureID(index), "polygon", toMap(source),
                Metadata: [
                    new TerrainTopologyMetadataField("ModificationType", "Modification Type", "RasterModType",
                        _input[index].ModificationType.ToString(), true),
                    new TerrainTopologyMetadataField("Value", "Value", "float",
                        _input[index].Value.ToString("R", CultureInfo.InvariantCulture), true)
                ], Rings: [toMap(source)]);
        }
        return features;
    }

    public string[] SelectedFeatureIDs() => _selected.Elements.Order()
        .Where(index => index >= 0 && index < _input.Count).Select(FeatureID).ToArray();

    public void Select(string[] featureIDs)
    {
        _selected.Clear();
        foreach (string featureID in featureIDs) _selected.Add(FeatureIndex(featureID));
    }

    public string[] SelectAt(TerrainMapCoordinate sourcePoint, double cellSize)
    {
        if (!double.IsFinite(sourcePoint.X) || !double.IsFinite(sourcePoint.Y) ||
            !double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentException("Polygon selection needs a finite map point and a positive map cell size.");
        _selected.Clear();
        Func<HitTestArgs, int> hitTest = ToolBuilders.CreatePolygonHitTest(_polygons);
        int index = hitTest(HitTestArgs.Generate(new Point(sourcePoint.X, sourcePoint.Y), cellSize));
        if (index >= 0) _selected.Add(index);
        return SelectedFeatureIDs();
    }

    public string Stamp(TerrainMapCoordinate sourceAnchor, double cellSize, TerrainPolygonEditSettings settings)
    {
        ValidateSettings(settings);
        if (!double.IsFinite(sourceAnchor.X) || !double.IsFinite(sourceAnchor.Y) ||
            !double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentException("Polygon placement needs a finite map point and a positive map cell size.");
        _currentSettings = settings;
        Polygon template = settings.Shape switch
        {
            "Triangle" => Polygon.CreateTriangle(new Point(0, 0), settings.Width, settings.Height, settings.Rotation),
            "Rectangle" => Polygon.CreateRectangle(new Point(0, 0), settings.Width, settings.Height, settings.Rotation),
            _ => throw new ArgumentException("Choose Triangle or Rectangle for Stamp; use Draw for a custom polygon.")
        };
        template.Translate(sourceAnchor.X, sourceAnchor.Y);
        ExecuteNewPolygon(template);
        return FeatureID(_input.Count - 1);
    }

    public string Draw(TerrainMapCoordinate[] sourcePoints, TerrainPolygonEditSettings settings)
    {
        ValidateSettings(settings);
        TerrainMapCoordinate[] points = sourcePoints;
        if (points.Length > 3 && points[0].X == points[^1].X && points[0].Y == points[^1].Y)
            points = points[..^1];
        if (points.Length < 3 || points.Any(point => point is null ||
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("A drawn terrain modification needs at least three finite vertices.");
        _currentSettings = settings;
        Polygon polygon = new(points.Select(point => new Point(point.X, point.Y)));
        if (!Polygon.IsValidPolygon(polygon) || !double.IsFinite(polygon.Area) || polygon.Area == 0)
            throw new ArgumentException("A drawn terrain modification must form a non-zero-area polygon.");
        ExecuteNewPolygon(polygon);
        return FeatureID(_input.Count - 1);
    }

    public void Remove(string[] featureIDs)
    {
        if (featureIDs.Length == 0) throw new ArgumentException("Select at least one polygon to remove.");
        int[] indexes = featureIDs.Select(FeatureIndex).Distinct().OrderByDescending(index => index).ToArray();
        EditOperation[] deletes = indexes.Select(index => (EditOperation)
            new DeleteFeatureOperation<Polygon>(_polygons, _selected, index)).ToArray();
        if (!_editStack.Execute(new MultiOperationEditOperation(deletes)))
            throw new InvalidOperationException("The source polygon delete operation did not complete.");
        _selected.Clear();
    }

    public bool Undo() => _editStack.TryUndo();
    public bool Redo() => _editStack.TryRedo();

    public void Apply(TerrainPolygonTopologyMutation mutation)
    {
        switch (mutation.Operation)
        {
            case "stamp":
                if (mutation.Points is not { Length: 1 } stampPoint || mutation.Settings is null)
                    throw new ArgumentException("The polygon stamp journal is incomplete.");
                _ = Stamp(stampPoint[0], 1, mutation.Settings);
                break;
            case "draw":
                if (mutation.Points is not { Length: >= 3 } drawPoints || mutation.Settings is null)
                    throw new ArgumentException("The polygon draw journal is incomplete.");
                _ = Draw(drawPoints, mutation.Settings);
                break;
            case "remove":
                Remove(mutation.FeatureIDs);
                break;
            default:
                throw new ArgumentException("The terrain polygon journal contains an unsupported operation.");
        }
    }

    private void ExecuteNewPolygon(Polygon polygon)
    {
        if (!Polygon.IsValidPolygon(polygon) || !double.IsFinite(polygon.Area) || polygon.Area == 0)
            throw new ArgumentException("A terrain modification must be a valid non-zero-area polygon.");
        // The retained Triangle and Rectangle builders use different winding.
        // Normalize valid geometry before adding either shape to the source.
        polygon.EnsureClockwise();
        _selected.Clear();
        if (!_editStack.Execute(new NewFeatureOperation<Polygon>(_polygons, _selected,
                () => new Polygon(polygon))))
            throw new InvalidOperationException("The source polygon add operation did not complete.");
        _selected.Clear();
        _selected.Add(_input.Count - 1);
    }

    private int FeatureIndex(string featureID)
    {
        string prefix = _inputNodeID + ":feature:";
        if (!featureID.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(featureID[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture,
                out int index) || index < 0 || index >= _input.Count || FeatureID(index) != featureID)
            throw new ArgumentException("A selected source polygon no longer exists.");
        return index;
    }

    private string FeatureID(int index) => _inputNodeID + ":feature:" + index.ToString(CultureInfo.InvariantCulture);

    private static Geospatial.Rasters.RasterModType ParseModificationType(string value) =>
        Enum.TryParse(value, false, out Geospatial.Rasters.RasterModType parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("The polygon modification type is invalid.");

    private static void ValidateSettings(TerrainPolygonEditSettings settings)
    {
        if (settings is null || settings.Shape is not ("Triangle" or "Rectangle" or "Custom") ||
            !double.IsFinite(settings.Width) || settings.Width <= 0 || !double.IsFinite(settings.Height) ||
            settings.Height <= 0 || !double.IsFinite(settings.Rotation) || !float.IsFinite(settings.Value))
            throw new ArgumentException("The terrain polygon shape settings are invalid.");
        _ = ParseModificationType(settings.ModificationType);
    }
}

/// <summary>
/// Headless host for the retained channel topology edit operations. The source
/// completion context is preserved so each edit runs through the same graph
/// completion and ChannelModTerrainInput resampler preparation as the editor.
/// </summary>
internal sealed class TerrainTopologyEditSession
{
    private readonly string _inputID;
    private readonly ChannelModTerrainInput _channel;
    private readonly ObservableChannelModTopology _observableTopology;
    private readonly EditStack _editStack;
    private readonly TopologyEditContext _editContext;
    private readonly TopologyCompletionContext _completionContext;

    public TerrainTopologyEditSession(string inputID, ChannelModTerrainInput channel)
    {
        _inputID = inputID;
        _channel = channel;
        var observableInput = new ObservableChannelModTerrainInput(channel);
        _observableTopology = new ObservableChannelModTopology(observableInput, channel.Topology);
        _editStack = new EditStack();
        IVectorManager vectorManager = DispatchProxy.Create<IVectorManager, GeometryTopologyHeadlessVectorManagerProxy>();
        _editContext = new TopologyEditContext
        {
            ArcSelectedSet = new SelectedSet(),
            NodeSelectedSet = new SelectedSet(),
            ControlPointSelectedSet = new SelectedSet()
        };
        _editContext.ArcAdded += _observableTopology.AlertNewArc;
        _editContext.ArcInserted += _observableTopology.AlertArcInserted;
        _editContext.ArcRemoved += _observableTopology.AlertArcAtDeleted;
        _editContext.ControlPointsAdded += _observableTopology.AlertNewControlPointsAppended;
        _editContext.ControlPointsInserted += _observableTopology.AlertNewControlPointsInserted;
        _editContext.ControlPointsRemoved += _observableTopology.AlertControlPointsAtDeleted;
        _editContext.NodeAdded += _observableTopology.AlertNewNode;
        _editContext.NodeInserted += _observableTopology.AlertNodeInserted;
        _editContext.NodeRemoved += _observableTopology.AlertNodeAtDeleted;
        _completionContext = new TopologyCompletionContext(_editContext, _editStack,
            TopologyCompletionContext.GetIsAddingForEditStack(_editStack), CompleteChannelTopology);
        var editableTopology = new EditableTopology(new Any<Topology>(_observableTopology.Topology),
            new OverlayChannels(vectorManager), vectorManager, _completionContext.AttachListenersAndExecute);
        _editContext.AnyEditableTopology = new Any<EditableTopology>(editableTopology);
    }

    public void Initialize()
    {
        if (!_completionContext.TryInitialize())
            throw new InvalidOperationException("The channel topology completion context could not initialize.");
    }

    private OperationResult CompleteChannelTopology(Topology topology, TopologyBuilderParams buildParams,
        TopologyBuildDiagnostics diagnostics)
    {
        OperationResult result = topology.Complete(buildParams, diagnostics);
        _channel.Prepare(result);
        return result;
    }

    public TerrainTopologyFeature[] Features()
    {
        Topology topology = _observableTopology.Topology;
        var result = new List<TerrainTopologyFeature>(topology.ArcCount * 2 + topology.NodeCount + topology.RegionControlPoints.Count + topology.CompletedRegions.Count);
        for (int fid = 0; fid < topology.ArcCount; fid++)
        {
            Polyline? line = topology.GetArcPolyline(fid);
            if (line is null || line.Count < 2) continue;
            string arcId = FeatureID("arc", fid);
            result.Add(new(arcId, "arc", Coordinates(line), Metadata: Metadata(topology.ArcMetadata, fid)));
            for (int index = 1; index < line.Count - 1; index++)
                result.Add(new(FeatureID("arcVertex", fid, index), "arcVertex", [Coordinate(line[index])],
                    ParentId: arcId, VertexIndex: index));
        }
        for (int fid = 0; fid < topology.NodeCount; fid++)
        {
            Point point = topology.Nodes[fid].Point;
            if (double.IsFinite(point.X) && double.IsFinite(point.Y))
                result.Add(new(FeatureID("node", fid), "node", [Coordinate(point)], Metadata: Metadata(topology.NodeMetadata, fid)));
        }
        for (int fid = 0; fid < topology.RegionControlPoints.Count; fid++)
        {
            Point point = topology.RegionControlPoints[fid];
            if (double.IsFinite(point.X) && double.IsFinite(point.Y))
                result.Add(new(FeatureID("controlPoint", fid), "controlPoint", [Coordinate(point)],
                    Metadata: Metadata(topology.ControlPointMetadata, fid)));
        }
        for (int fid = 0; fid < topology.CompletedRegions.Count; fid++)
        {
            int controlPoint = topology.CompletedRegions[fid].ControlPointIndex;
            Geospatial.Vectors.ComplexPolygon complexPolygon = topology.GetRegionComplexPolygon(fid);
            TerrainMapCoordinate[][] rings = complexPolygon.Rings.Select(ring => Coordinates(ring)).ToArray();
            TerrainMapCoordinate[] points = rings.FirstOrDefault() ?? [];
            bool? isMask = controlPoint >= 0 && topology.ControlPointMetadata.GetColumn(ChannelMod.MaskColumnName) != null
                ? ChannelMod.GetRegionIsMask(topology, fid) : null;
            result.Add(new(FeatureID("region", fid), "region", points,
                ControlPointIndex: controlPoint >= 0 ? controlPoint : null, IsMask: isMask,
                Metadata: controlPoint >= 0 ? Metadata(topology.ControlPointMetadata, controlPoint) : [], Rings: rings));
        }
        return result.ToArray();
    }

    public void Select(string[] featureIDs)
    {
        _editContext.ClearSelection();
        TerrainTopologyFeature[] available = Features();
        Dictionary<string, TerrainTopologyFeature> byId = available.ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        foreach (string id in featureIDs)
        {
            if (!byId.TryGetValue(id, out TerrainTopologyFeature? feature))
                throw new ArgumentException("A selected channel topology feature no longer exists.");
            (string kind, int fid, _) = ParseFeatureID(id);
            switch (kind)
            {
                case "arc":
                case "arcVertex": _editContext.ArcSelectedSet.Add(fid); break;
                case "node": _editContext.NodeSelectedSet.Add(fid); break;
                case "controlPoint": _editContext.ControlPointSelectedSet.Add(fid); break;
                case "region":
                    if (feature.ControlPointIndex is int controlPoint) _editContext.ControlPointSelectedSet.Add(controlPoint);
                    break;
                default: throw new ArgumentException("The selected channel topology feature cannot be selected.");
            }
        }
    }

    public string[] SelectedFeatureIDs()
    {
        var ids = new List<string>();
        ids.AddRange(_editContext.ArcSelectedSet.Elements.Select(fid => FeatureID("arc", fid)));
        ids.AddRange(_editContext.NodeSelectedSet.Elements.Select(fid => FeatureID("node", fid)));
        ids.AddRange(_editContext.ControlPointSelectedSet.Elements.Select(fid => FeatureID("controlPoint", fid)));
        HashSet<string> available = Features().Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        return ids.Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }

    public string Draw(TerrainMapCoordinate[] points, double cellSize)
    {
        ValidatePoints(points, cellSize);
        Execute(new NewArcOperation(_editContext, Hit(points[0], cellSize)));
        if (_editContext.ArcSelectedSet.Count != 1)
            throw new InvalidOperationException("The source topology editor did not select the new arc.");
        int arcFID = _editContext.ArcSelectedSet.Elements.Single();
        foreach (TerrainMapCoordinate point in points.Skip(1).Take(points.Length - 2))
        {
            var selectedPoints = new SelectedSet();
            Execute(new AppendPointOperation(() => new ArcPolylineWrapper(
                new Any<BaseTopology>(_observableTopology.Topology), arcFID),
                Hit(point, cellSize), selectedPoints));
        }
        Execute(new CompleteArcOperation(_editContext, arcFID, Hit(points[^1], cellSize)));
        return FeatureID("arc", arcFID);
    }

    public void Remove(string[] featureIDs, double cellSize)
    {
        if (featureIDs.Length == 0 || !double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentException("Select at least one graph feature to remove.");
        _editContext.ClearSelection();
        TerrainTopologyFeature[] availableFeatures = Features();
        Dictionary<string, TerrainTopologyFeature> features = availableFeatures.ToDictionary(feature => feature.Id,
            feature => feature, StringComparer.Ordinal);
        foreach (string id in featureIDs)
        {
            if (!features.TryGetValue(id, out TerrainTopologyFeature? feature)) throw new ArgumentException("A selected channel topology feature no longer exists.");
            (string kind, int fid, _) = ParseFeatureID(id);
            switch (kind)
            {
                case "arc": _editContext.ArcSelectedSet.Add(fid); break;
                case "node": _editContext.NodeSelectedSet.Add(fid); break;
                case "controlPoint": _editContext.ControlPointSelectedSet.Add(fid); break;
                case "region":
                    if (feature.ControlPointIndex is int controlPoint) _editContext.ControlPointSelectedSet.Add(controlPoint);
                    else throw new ArgumentException("The selected channel topology feature cannot be removed.");
                    break;
                default: throw new ArgumentException("The selected channel topology feature cannot be removed.");
            }
        }
        TerrainTopologyFeature anchor = availableFeatures.First(feature => feature.Id == featureIDs[0]);
        Execute(new DeleteTopologySelectionOperation(_editContext, Hit(anchor.Points[0], cellSize)));
    }

    public bool Apply(TerrainTopologyMutation mutation) => Apply(mutation, out _);

    public bool Apply(TerrainTopologyMutation mutation, out string? selectedFeatureID)
    {
        selectedFeatureID = null;
        switch (mutation.Operation)
        {
            case "draw":
                selectedFeatureID = Draw(mutation.Points ?? throw new ArgumentException("The channel topology draft is missing draw points."),
                    mutation.MapCellSize ?? throw new ArgumentException("The channel topology draft is missing a map cell size."));
                return true;
            case "remove":
                Remove(mutation.FeatureIDs, mutation.MapCellSize ?? throw new ArgumentException("The channel topology draft is missing a map cell size."));
                return true;
            case "split": return Split(mutation.FeatureIDs, mutation.Points, mutation.MapCellSize, mutation.SplitMode);
            case "moveNodes": return MoveNodes(mutation.FeatureIDs, mutation.Points, mutation.MapCellSize);
            case "moveVertex": return MoveVertex(mutation.FeatureIDs, mutation.Points, mutation.MapCellSize, mutation.VertexIndex);
            case "insertVertex":
                bool inserted = InsertVertex(mutation.FeatureIDs, mutation.Points, mutation.MapCellSize, out int index);
                if (inserted) selectedFeatureID = FeatureID("arcVertex", RequireSingleArc(mutation.FeatureIDs), index);
                return inserted;
            case "deleteVertex": return DeleteVertex(mutation.FeatureIDs, mutation.VertexIndex);
            case "maskRegion":
                bool masked = ToggleMask(mutation.FeatureIDs);
                if (masked) selectedFeatureID = mutation.FeatureIDs.Single();
                return masked;
            case "setMetadata":
                bool updated = SetMetadata(mutation.FeatureIDs, mutation.MetadataKind,
                    mutation.MetadataColumn, mutation.MetadataValue);
                if (updated) selectedFeatureID = mutation.FeatureIDs.Single();
                return updated;
            default: throw new ArgumentException("The channel topology draft contains an unsupported operation.");
        }
    }

    private bool Split(string[] featureIDs, TerrainMapCoordinate[]? points, double? cellSize, string? splitModeName)
    {
        if (featureIDs.Length != 1 || points is not { Length: 1 } || cellSize is not double size
            || !double.IsFinite(size) || size <= 0
            || !Enum.TryParse(splitModeName, false, out SplitMode splitMode) || !Enum.IsDefined(splitMode))
            throw new ArgumentException("A topology split requires one selected arc, one map point, a split mode, and a positive map cell size.");
        (string kind, int fid, _) = ParseFeatureID(featureIDs[0]);
        if (kind != "arc" || fid >= _observableTopology.Topology.ArcCount)
            throw new ArgumentException("The selected channel topology feature is not an arc.");
        HitTestArgs hit = Hit(points[0], size);
        if (!NetworkRenderLayer.HitTest(_observableTopology.Topology, hit, out NetworkElementType element, out int hitFeatureIndex, out _)
            || element is not (NetworkElementType.ArcSegment or NetworkElementType.ArcInternalPoint)
            || hitFeatureIndex != fid)
            throw new ArgumentException("The selected channel topology feature no longer exists at the split point.");
        if (splitMode == SplitMode.AtVertex)
        {
            Polyline? line = _observableTopology.Topology.GetArcPolyline(fid);
            if (line == null || line.Count < 3) return false;
            int nearest = line.NearestPoint(hit.QueryPoint);
            if (nearest == 0) nearest = 1;
            else if (nearest == line.Count - 1) nearest = line.Count - 2;
            hit = new HitTestArgs(line[nearest], hit.Window);
        }
        return TryExecute(new SplitArcOperation(_editContext, hit));
    }

    private bool MoveNodes(string[] featureIDs, TerrainMapCoordinate[]? points, double? cellSize)
    {
        if (featureIDs.Length == 0 || points is not { Length: 2 } || cellSize is not double size
            || !double.IsFinite(size) || size <= 0)
            throw new ArgumentException("Moving topology nodes requires selected nodes, a start and end point, and a positive map cell size.");
        Topology topology = _observableTopology.Topology;
        var parsed = featureIDs.Select(ParseFeatureID).ToArray();
        if (parsed.Any(item => item.Kind != "node" || item.FID >= topology.NodeCount))
            throw new ArgumentException("Only selected nodes can be moved.");
        int[] nodeFids = parsed.Select(item => item.FID).ToArray();
        Point anchor = topology.Nodes[nodeFids[0]].Point;
        if (anchor.X != points[0].X || anchor.Y != points[0].Y)
            throw new ArgumentException("The selected channel topology node changed before it was moved.");
        _editContext.ClearSelection();
        foreach (int fid in nodeFids) _editContext.NodeSelectedSet.Add(fid);
        var delta = new Vector(points[1].X - points[0].X, points[1].Y - points[0].Y);
        return TryExecute(new TranslateSelectedNodesOperation(_editContext, Hit(points[1], size), delta));
    }

    private bool MoveVertex(string[] featureIDs, TerrainMapCoordinate[]? points, double? cellSize, int? vertexIndex)
    {
        int arcFid = RequireSingleArc(featureIDs);
        if (points is not { Length: 2 } || vertexIndex is not int index || cellSize is not double size
            || !double.IsFinite(size) || size <= 0)
            throw new ArgumentException("Moving an arc vertex requires its index, a start and end point, and a positive map cell size.");
        Polyline? line = _observableTopology.Topology.GetArcPolyline(arcFid);
        if (line == null || index <= 0 || index >= line.Count - 1)
            throw new ArgumentException("The selected arc vertex no longer exists.");
        if (line[index].X != points[0].X || line[index].Y != points[0].Y)
            throw new ArgumentException("The selected arc vertex changed before it was moved.");
        SelectedSet selection = new(); selection.Add(index);
        EditableArc editable = CreateEditableArc(arcFid, selection);
        var delta = new Vector(points[1].X - points[0].X, points[1].Y - points[0].Y);
        return TryExecute(new TranslatePointOperation(() => editable.Vector, selection, Hit(points[1], size), delta));
    }

    private bool InsertVertex(string[] featureIDs, TerrainMapCoordinate[]? points, double? cellSize, out int insertedIndex)
    {
        insertedIndex = -1;
        int arcFid = RequireSingleArc(featureIDs);
        if (points is not { Length: 1 } || cellSize is not double size || !double.IsFinite(size) || size <= 0)
            throw new ArgumentException("Inserting an arc vertex requires one map point and a positive map cell size.");
        Polyline? before = _observableTopology.Topology.GetArcPolyline(arcFid);
        if (before == null) throw new ArgumentException("The selected channel topology feature no longer exists.");
        int oldCount = before.Count;
        EditableArc editable = CreateEditableArc(arcFid, new SelectedSet());
        if (!editable.TryInsertPoint(Hit(points[0], size), out insertedIndex)) return false;
        Polyline? after = _observableTopology.Topology.GetArcPolyline(arcFid);
        if (after != null && after.Count == oldCount + 1) return true;
        insertedIndex = -1;
        return false;
    }

    private bool DeleteVertex(string[] featureIDs, int? vertexIndex)
    {
        int arcFid = RequireSingleArc(featureIDs);
        if (vertexIndex is not int index)
            throw new ArgumentException("Deleting an arc vertex requires its index.");
        Polyline? line = _observableTopology.Topology.GetArcPolyline(arcFid);
        if (line == null || index <= 0 || index >= line.Count - 1)
            throw new ArgumentException("The selected arc vertex no longer exists.");
        SelectedSet selection = new(); selection.Add(index);
        EditableArc editable = CreateEditableArc(arcFid, selection);
        return _completionContext.AttachListenersAndExecute(
            _editContext.CreateDeleteArcPointsOperation(arcFid, () => editable.Vector, selection));
    }

    private bool ToggleMask(string[] featureIDs)
    {
        if (featureIDs.Length != 1) throw new ArgumentException("Masking requires one selected topology region.");
        (string kind, int regionIndex, _) = ParseFeatureID(featureIDs[0]);
        Topology topology = _observableTopology.Topology;
        if (kind != "region" || regionIndex >= topology.CompletedRegions.Count)
            throw new ArgumentException("The selected topology region no longer exists.");
        if (topology.CompletedRegions[regionIndex].ControlPointIndex < 0)
            throw new ArgumentException("This topology region has no control point and cannot be masked.");
        ChannelMod.EnsureTopologyColumns(topology);
        Polygon outerRing = topology.GetRegionOuterRingPolygon(regionIndex);
        if (outerRing.Count < 3) throw new ArgumentException("The selected topology region no longer exists.");
        Point anchor = outerRing[0];
        return TryExecute(new TerrainTopologyMaskOperation(_editContext, Hit(Coordinate(anchor), 1), regionIndex));
    }

    private bool SetMetadata(string[] featureIDs, string? kind, string? column, string? value)
    {
        if (featureIDs.Length != 1 || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(column) || value == null)
            throw new ArgumentException("A topology attribute edit requires one feature, column, and value.");
        (string parsedKind, int fid, _) = ParseFeatureID(featureIDs[0]);
        if (parsedKind != kind || kind is not ("arc" or "node" or "controlPoint"))
            throw new ArgumentException("The selected topology feature does not match this attribute table.");
        Utility.Memory.Table table = MetadataTable(kind);
        if (fid >= table.Rows.Count) throw new ArgumentException("The selected topology metadata row no longer exists.");
        if (!table.TryGetColumnIndex(column, out int columnIndex)) throw new ArgumentException("The selected topology metadata column no longer exists.");
        TableColumn schema = table.Columns[columnIndex];
        if (schema.IsReadOnly || column == ChannelMod.MaskColumnName)
            throw new ArgumentException("This topology attribute is read-only or has a dedicated source operation.");
        object parsed = ParseMetadataValue(value, schema.Type);
        Point point = _observableTopology.Topology.NodeCount > 0 ? _observableTopology.Topology.Nodes[0].Point : Point.Origin;
        return TryExecute(new TerrainTopologyMetadataOperation(_editContext, Hit(Coordinate(point), 1), table, fid, column, parsed));
    }

    private EditableArc CreateEditableArc(int arcFid, SelectedSet selectedPoints)
    {
        IVectorManager vectorManager = DispatchProxy.Create<IVectorManager, GeometryTopologyHeadlessVectorManagerProxy>();
        return new EditableArc(arcFid, new Any<BaseTopology>(_observableTopology.Topology), EditableArc.EditingMode.Editing,
            vectorManager, (operation, _) => _completionContext.AttachListenersAndExecute(operation), selectedPoints,
            new OverlayChannels(vectorManager));
    }

    private int RequireSingleArc(string[] featureIDs)
    {
        if (featureIDs.Length != 1) throw new ArgumentException("Select one topology arc.");
        (string kind, int fid, _) = ParseFeatureID(featureIDs[0]);
        if (kind != "arc" || fid >= _observableTopology.Topology.ArcCount)
            throw new ArgumentException("The selected channel topology feature is not an arc.");
        return fid;
    }

    private bool TryExecute(EditOperation operation) => _completionContext.AttachListenersAndExecute(operation);

    private void Execute(EditOperation operation)
    {
        if (!_completionContext.AttachListenersAndExecute(operation))
            throw new InvalidOperationException("The source channel topology edit stack rejected the graph command.");
    }

    private HitTestArgs Hit(TerrainMapCoordinate coordinate, double cellSize) =>
        HitTestArgs.Generate(new Point(coordinate.X, coordinate.Y), cellSize);

    private string FeatureID(string kind, int fid) => $"terrain-topology:{_inputID}:{kind}:{fid}";
    private string FeatureID(string kind, int fid, int vertexIndex) => $"terrain-topology:{_inputID}:{kind}:{fid}:{vertexIndex}";

    private (string Kind, int FID, int? VertexIndex) ParseFeatureID(string id)
    {
        string prefix = $"terrain-topology:{_inputID}:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("The selected topology feature belongs to another channel.");
        string[] components = id[prefix.Length..].Split(':');
        bool hasVertex = components.Length == 3 && components[0] == "arcVertex";
        if ((!hasVertex && (components.Length != 2 || components[0] is not ("arc" or "node" or "controlPoint" or "region"))
                || hasVertex && !int.TryParse(components[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            || !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out int fid) || fid < 0)
            throw new ArgumentException("The selected channel topology feature identity is invalid.");
        int? vertexIndex = hasVertex ? int.Parse(components[2], NumberStyles.None, CultureInfo.InvariantCulture) : null;
        return (components[0], fid, vertexIndex);
    }

    private static TerrainMapCoordinate Coordinate(Point point) => new(point.X, point.Y);

    private static TerrainMapCoordinate[] Coordinates(Polyline line)
    {
        var result = new TerrainMapCoordinate[line.Count];
        for (int index = 0; index < line.Count; index++)
        {
            Point point = line[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidOperationException("The source channel topology contains a non-finite coordinate.");
            result[index] = new(point.X, point.Y);
        }
        return result;
    }

    private static TerrainMapCoordinate[] Coordinates(IEnumerable<Point> line) => line.Select(point =>
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new InvalidOperationException("The source channel topology contains a non-finite coordinate.");
        return Coordinate(point);
    }).ToArray();

    private static TerrainTopologyMetadataField[] Metadata(Utility.Memory.Table table, int fid)
    {
        if (fid < 0 || fid >= table.Rows.Count) return [];
        Utility.Memory.TableRow row = table.Rows[fid];
        return table.Columns.Select(column =>
        {
            object? value = row[column.Name];
            bool supported = IsSupportedMetadataType(column.Type);
            string[]? options = column.Type.IsEnum ? Enum.GetNames(column.Type) : null;
            bool dedicatedOperation = column.Name == ChannelMod.MaskColumnName;
            return new TerrainTopologyMetadataField(column.Name, column.Name, column.Type.Name,
                value == null ? null : FormatMetadataValue(value), column.IsReadOnly || !supported || dedicatedOperation, options);
        }).ToArray();
    }

    private Utility.Memory.Table MetadataTable(string kind) => kind switch
    {
        "arc" => _observableTopology.Topology.ArcMetadata,
        "node" => _observableTopology.Topology.NodeMetadata,
        "controlPoint" => _observableTopology.Topology.ControlPointMetadata,
        _ => throw new ArgumentException("Unknown topology metadata table.")
    };

    private static bool IsSupportedMetadataType(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual == typeof(string) || actual == typeof(bool) || actual.IsEnum
            || actual == typeof(byte) || actual == typeof(sbyte) || actual == typeof(short) || actual == typeof(ushort)
            || actual == typeof(int) || actual == typeof(uint) || actual == typeof(long) || actual == typeof(ulong)
            || actual == typeof(float) || actual == typeof(double) || actual == typeof(decimal);
    }

    private static string FormatMetadataValue(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    private static object ParseMetadataValue(string value, Type sourceType)
    {
        Type type = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        if (type == typeof(string)) return value;
        if (type == typeof(bool) && bool.TryParse(value, out bool boolean)) return boolean;
        if (type.IsEnum && Enum.TryParse(type, value, false, out object? enumeration)
            && Enum.IsDefined(type, enumeration!)) return enumeration!;
        try
        {
            object parsed = Convert.ChangeType(value, type, CultureInfo.InvariantCulture)
                ?? throw new ArgumentException("The topology attribute value is invalid.");
            if (parsed is double doubleValue && !double.IsFinite(doubleValue)
                || parsed is float floatValue && !float.IsFinite(floatValue))
                throw new ArgumentException("The topology attribute value must be finite.");
            return parsed;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw new ArgumentException("The topology attribute value does not match its source type.");
        }
    }

    private static void ValidatePoints(TerrainMapCoordinate[] points, double cellSize)
    {
        if (points.Length < 2 || points.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            || !double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentException("A channel topology arc requires at least two finite points and a positive map cell size.");
    }

    private sealed class TerrainTopologyMaskOperation(TopologyEditContext context, HitTestArgs args, int regionIndex)
        : TopologyEditOperation(context, args)
    {
        private bool _previousMask;
        public override string Description => "Mask";
        protected override bool ApplyInternal()
        {
            if (regionIndex < 0 || regionIndex >= _context.Topology.CompletedRegions.Count) return false;
            if (_context.Topology.CompletedRegions[regionIndex].ControlPointIndex < 0) return false;
            _previousMask = ChannelMod.GetRegionIsMask(_context.Topology, regionIndex);
            ChannelMod.SetRegionIsMask(_context.Topology, regionIndex, !_previousMask);
            return true;
        }
        protected override bool UndoInternal()
        {
            if (regionIndex >= 0 && regionIndex < _context.Topology.CompletedRegions.Count)
                ChannelMod.SetRegionIsMask(_context.Topology, regionIndex, _previousMask);
            UndoSelectionState();
            return true;
        }
    }

    private sealed class TerrainTopologyMetadataOperation(TopologyEditContext context, HitTestArgs args,
        Utility.Memory.Table table, int rowIndex, string column, object value) : TopologyEditOperation(context, args)
    {
        private object? _previous;
        public override string Description => "Edit Topology Attribute";
        protected override bool ApplyInternal()
        {
            if (rowIndex < 0 || rowIndex >= table.Rows.Count) return false;
            _previous = table[rowIndex].Value(column);
            table[rowIndex].SetValue(column, value);
            return true;
        }
        protected override bool UndoInternal()
        {
            if (rowIndex >= 0 && rowIndex < table.Rows.Count)
                table[rowIndex].SetValue(column, _previous!);
            UndoSelectionState();
            return true;
        }
    }
}
