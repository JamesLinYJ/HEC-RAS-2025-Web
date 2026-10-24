using System.Globalization;
using Ras.Layers;
using Ras.Observables;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    /// <summary>
    /// Reads the static Geometry projection from the exact selected Result
    /// context represented by a serialized Result/MultiResult Geometry child.
    /// It never resolves a project Geometry by the embedded Geometry name.
    /// </summary>
    public static Task<MapResultGeometrySnapshot> LoadMapResultGeometryAsync(MapResultGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapResultGeometryRequest(request);

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(request.Map,
            request.ResultOwner.SelectionOverrides, request.ResultOwner.ContextToken);
        ResolvedMapResultNode selected = ResolveMapResultOwner(context,
            request.ResultOwner.ResultNode, request.ResultOwner.SelectionIndex);
        RequireExactSelectedResult(selected, request.ResultOwner.SelectedResultPath,
            request.ResultOwner.SelectedResultRevision);
        CatalogTreeNode resultRow = ResolveExactMapNode(context.Tree.Roots, request.ResultOwner.ResultNode);
        CatalogTreeNode geometryRow = ResolveResultOwnedChild(resultRow, request.GeometryNode,
            selected.ResultPath, selected.Revision, "Geometry");
        Geometry geometry = selected.Result.Geometry ?? throw new InvalidDataException(
            "The selected Result has no embedded Geometry.");
        if (!string.Equals(geometry.Name, geometryRow.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string geometryPath = SourceFilePaths.Canonical(selected.ResultPath);
        string geometryRevision = selected.Revision;
        string pageContextToken = ResultGeometryPageContextToken(context.Sources.ContextToken,
            request.ResultOwner.ResultNode, request.ResultOwner.SelectionIndex, selected.ResultPath,
            selected.Revision, request.GeometryNode);
        if (request.GeometryContextToken is { Length: > 0 } expectedPageToken &&
            !string.Equals(expectedPageToken, pageContextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        bool geometryChecked = geometryRow.Checked == "checked";
        bool geometryVisible = MapTreeService.IsEffectivelyVisible(context.Tree.Roots, geometryRow);

        var observableProject = new ObservableProject(context.Project, Utility.SynchronizationContext.None);
        var observableResult = new ObservableResult(observableProject, selected.Result);
        ObservableGeometry observableGeometry = observableResult.Geometry;
        GeometryTopologyMap? topology = null;
        int topologyFeatureCount = 0;
        int omittedTopologyCount = 0;
        GeometryMeshFlowAreaRow[] flowAreaRows = [];
        GeometryCulvertFeatureRow[] culvertRows = [];
        MapGeometryLayerState topologyState = LayerState(context.Tree.Roots, geometryRow,
            "ConceptualMesh", "available", null);
        MapGeometryLayerState flowAreasState = LayerState(context.Tree.Roots, geometryRow,
            "FlowAreaLayer", "available", null);
        MapGeometryLayerState culvertsState = LayerState(context.Tree.Roots, geometryRow,
            "CulvertBarrelLayer", "unavailable", "This Geometry has no CulvertBarrelLayer child.");

        try
        {
            if (observableGeometry.ConceptualMeshLayer.Layer.LoadFailed)
                throw new IOException("The source topology could not be loaded; it was left unchanged.");
            ObservableConceptualMeshTopology? sourceTopology =
                observableGeometry.ConceptualMeshLayer.Topology as ObservableConceptualMeshTopology;
            if (sourceTopology is not null)
            {
                GeometryTopologyMapBuild build = MapData(sourceTopology);
                topology = build.Map;
                topologyFeatureCount = SourceTopologyFeatureCount(sourceTopology);
                omittedTopologyCount = Math.Max(0, topologyFeatureCount - topology.Features.Length);
            }
        }
        catch (Exception error) when (IsGeometryMapSourceFailure(error))
        {
            topologyState = LayerState(context.Tree.Roots, geometryRow, "ConceptualMesh", "failed", error.Message);
        }

        try
        {
            flowAreaRows = observableGeometry.FlowAreaLayer.Features
                .Select((area, index) => FlowAreaSummary(area, index, propertyTablesQueuedForParentSave: false)).ToArray();
        }
        catch (Exception error) when (IsGeometryMapSourceFailure(error))
        {
            flowAreasState = LayerState(context.Tree.Roots, geometryRow, "FlowAreaLayer", "failed", error.Message);
        }

        try
        {
            if (observableGeometry.CulvertBarrelLayer is not null)
            {
                var identities = new GeometryCulvertIdentityMap();
                AddSourceRows(identities.Barrels, "barrel", observableGeometry.CulvertBarrelLayer.Features);
                culvertRows = BarrelTable(observableGeometry, identities).Rows;
            }
        }
        catch (Exception error) when (IsGeometryMapSourceFailure(error))
        {
            culvertsState = LayerState(context.Tree.Roots, geometryRow, "CulvertBarrelLayer", "failed", error.Message);
        }

        if (topologyState.Status != "failed")
        {
            topologyState = LayerState(context.Tree.Roots, geometryRow, "ConceptualMesh",
                topology is null || topology.Features.Length == 0 ? "empty" : "available",
                topology is null ? "The saved Geometry has no conceptual topology." : null);
            if (omittedTopologyCount > 0 && topologyState.Status is "available" or "empty")
                topologyState = topologyState with { Status = "partial",
                    Diagnostic = "Some source topology rows could not be projected to map geometry." };
        }
        if (flowAreasState.Status != "failed")
        {
            bool missingMeshes = flowAreaRows.Any(row => !row.HasMesh || row.HasMeshErrors);
            int totalCells = flowAreaRows.Sum(row => Math.Max(0, row.CellCount));
            flowAreasState = LayerState(context.Tree.Roots, geometryRow, "FlowAreaLayer",
                missingMeshes ? "partial" : totalCells == 0 ? "empty" : "available",
                missingMeshes ? "One or more source flow areas have no readable mesh or report mesh errors." :
                totalCells == 0 ? "The saved Geometry has no source mesh cells." : null);
        }
        if (culvertsState.Status != "failed")
        {
            bool hasSourceNode = culvertsState.Locator is not null;
            culvertsState = LayerState(context.Tree.Roots, geometryRow, "CulvertBarrelLayer",
                !hasSourceNode ? "unavailable" : culvertRows.Length == 0 ? "empty" : "available",
                !hasSourceNode ? "This Geometry has no CulvertBarrelLayer child." :
                culvertRows.Length == 0 ? "The saved Geometry has no culvert barrel features." : null);
        }

        var geometryRequest = new MapGeometryRequest(request.Map, request.GeometryNode,
            request.Viewport, request.PageSize, pageContextToken, request.TopologyCursor,
            request.CellCursor, request.CulvertCursor, request.SkipTopologyPage,
            request.SkipFlowAreasPage, request.SkipCulvertsPage);
        MapGeometryTopologyPage? topologyPage = request.SkipTopologyPage ? null :
            BuildTopologyPage(topology, topologyState, geometryRequest, pageContextToken,
                topologyFeatureCount, omittedTopologyCount);
        MapGeometryFlowAreasPage? flowAreaPage = request.SkipFlowAreasPage ? null :
            BuildFlowAreasPage(flowAreaRows, flowAreasState, geometryRequest, pageContextToken, observableGeometry);
        MapGeometryCulvertsPage? culvertPage = request.SkipCulvertsPage ? null :
            BuildCulvertsPage(culvertRows, culvertsState, geometryRequest, pageContextToken);

        if ((topologyState.Status is "available" or "partial") &&
            (omittedTopologyCount > 0 || topologyPage is { InvalidFeatureCount: > 0 }))
        {
            var diagnostics = new List<string>();
            if (omittedTopologyCount > 0)
                diagnostics.Add("Some source topology rows could not be projected to map geometry.");
            if (topologyPage is { InvalidFeatureCount: > 0 })
                diagnostics.Add("Some projected source topology features were invalid and were omitted from this page.");
            topologyState = topologyState with { Status = "partial", Diagnostic = string.Join(" ", diagnostics) };
        }
        if (flowAreaPage is { InvalidCellCount: > 0 } && flowAreasState.Status == "available")
            flowAreasState = flowAreasState with { Status = "partial",
                Diagnostic = "Some source Mesh.CellPolygon values could not be read or contained invalid coordinates." };
        if (culvertPage is { InvalidFeatureCount: > 0 } && culvertsState.Status == "available")
            culvertsState = culvertsState with { Status = "partial",
                Diagnostic = "Some source culvert polylines were invalid and were omitted from this page." };

        GeometryTopologyExtent? extent = UnionExtents(topologyPage?.SourceExtent ?? topology?.Extent,
            flowAreaPage?.SourceExtent ?? FlowAreaExtent(flowAreaRows),
            culvertPage?.SourceExtent ?? CulvertExtent(culvertRows));
        context.EnsureUnchanged();
        CatalogTreeNode finalResultRow = ResolveExactMapNode(context.Tree.Roots, request.ResultOwner.ResultNode);
        _ = ResolveResultOwnedChild(finalResultRow, request.GeometryNode, selected.ResultPath,
            selected.Revision, "Geometry");
        if (Revision(geometryPath) != geometryRevision)
            throw new SourceFileChangedException();

        var geometrySnapshot = new MapGeometrySnapshot(context.Sources.MapID,
            context.Sources.MapIndex, context.Sources.MapName, context.Sources.MapType,
            SourceFilePaths.Canonical(context.ProjectPath), context.ProjectRevision,
            context.ProjectXmlSHA, context.VisualizationRevision, pageContextToken,
            request.GeometryNode, geometry.Name, geometryPath, geometryRevision,
            geometryChecked, geometryVisible, extent, topologyState, flowAreasState,
            culvertsState, topologyPage, flowAreaPage, culvertPage, []);
        return Task.FromResult(new MapResultGeometrySnapshot(context.Sources.ProjectPath,
            context.ProjectRevision, context.ProjectXmlSHA, context.VisualizationRevision,
            context.Sources.MapID, context.Sources.MapIndex, context.Sources.MapName,
            context.Sources.MapType, context.Sources.ContextToken,
            request.ResultOwner.ResultNode, request.ResultOwner.SelectionIndex,
            selected.ResultPath, selected.Revision, request.GeometryNode, geometryPath,
            geometryRevision, geometrySnapshot));
    }

    private static void ValidateMapResultGeometryRequest(MapResultGeometryRequest request)
    {
        if (request.Map is null || request.ResultOwner is null || request.GeometryNode is null)
            throw new ArgumentException("An exact map, Result owner and embedded Geometry child are required.");
        if (request.ResultOwner.SelectionIndex != 0)
            throw new ArgumentException("The source Result Geometry child belongs to keyframe selection slot zero.");
        if (request.ResultOwner.ResultNode.NodeType is not ("Result" or "MultiResult") ||
            request.GeometryNode.NodeType != "Geometry")
            throw new ArgumentException("The selected map rows do not identify a Result-owned Geometry.");
        if (request.PageSize is < 1 or > GeometryMapMaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize),
                $"Page size must be between 1 and {GeometryMapMaximumPageSize}.");
        if (request.Viewport is { } viewport &&
            (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
             !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
             viewport.MinX > viewport.MaxX || viewport.MinY > viewport.MaxY))
            throw new ArgumentException("The map viewport must be a finite, ordered world-coordinate extent.");
    }

    private static string ResultGeometryPageContextToken(string chartContextToken,
        CatalogTreeNodeLocator resultNode, int selectionIndex, string resultPath,
        string resultRevision, CatalogTreeNodeLocator geometryNode)
    {
        string payload = string.Join("\n", "result-geometry-v1", chartContextToken,
            LocatorIdentity(resultNode), selectionIndex.ToString(CultureInfo.InvariantCulture),
            SourceFilePaths.Canonical(resultPath), resultRevision,
            LocatorIdentity(geometryNode));
        return HashGeometryMapIdentity(payload);
    }

    private static ResolvedMapResultNode ResolveMapResultOwner(ResolvedMapResultContext context,
        CatalogTreeNodeLocator requestedResultNode, int selectionIndex)
    {
        if (selectionIndex != 0 || requestedResultNode.NodeType is not ("Result" or "MultiResult"))
            throw new ArgumentException("Only the current Result or MultiResult keyframe at selection slot zero is supported.");
        MapResultActiveNode[] active = context.Sources.ActiveNodes.Where(node =>
            SameLocator(node.Node, requestedResultNode)).ToArray();
        if (active.Length != 1) throw new SourceFileChangedException();
        MapResultActiveNode activeNode = active[0];
        MapResultSelectionState[] slots = activeNode.Selections.Where(slot => slot.SelectionIndex == 0).ToArray();
        if (slots.Length != 1 || slots[0].SelectedResult is not { } selectedResult)
            throw new InvalidDataException("The selected Result node has no current keyframe result.");
        ResolvedMapResultNode[] selectedSources = context.ResultContexts
            .SelectMany(node => node.Selections.Select(source => (Node: node, Source: source)))
            .Where(item => item.Source.SelectionIndex == 0 && SameLocator(item.Source.Node, requestedResultNode))
            .Select(item => item.Node).Distinct().ToArray();
        if (selectedSources.Length != 1) throw new SourceFileChangedException();
        ResolvedMapResultNode resolved = selectedSources[0];
        if (!string.Equals(SourceFilePaths.Canonical(resolved.ResultPath),
                SourceFilePaths.Canonical(selectedResult.ResultPath), PathComparison()) ||
            !string.Equals(resolved.Revision, selectedResult.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return resolved;
    }

    private static void RequireExactSelectedResult(ResolvedMapResultNode result,
        string expectedPath, string expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(expectedPath) || string.IsNullOrWhiteSpace(expectedRevision) ||
            !string.Equals(SourceFilePaths.Canonical(result.ResultPath), SourceFilePaths.Canonical(expectedPath), PathComparison()) ||
            !string.Equals(result.Revision, expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static CatalogTreeNode ResolveResultOwnedChild(CatalogTreeNode resultRow,
        CatalogTreeNodeLocator requestedChild, string selectedResultPath, string selectedResultRevision,
        string childType)
    {
        CatalogTreeNodeLocator resultLocator = resultRow.Locator ?? throw new SourceFileChangedException();
        if (requestedChild.NodeType != childType ||
            resultLocator.NodePath is not { Length: >= 2 } resultPath ||
            (requestedChild.NodePath is { } childPath &&
                (childPath.Length != resultPath.Length + 1 ||
                 !childPath.Take(resultPath.Length).SequenceEqual(resultPath))) ||
            !string.Equals(requestedChild.RootName, resultLocator.RootName, StringComparison.Ordinal) ||
            !string.Equals(requestedChild.ParentResultNodeType, resultLocator.NodeType, StringComparison.Ordinal) ||
            !string.Equals(requestedChild.ParentResultNodeName, resultLocator.NodeName, StringComparison.Ordinal) ||
            requestedChild.ParentResultNodePath is not { } parentPath || !parentPath.SequenceEqual(resultPath) ||
            requestedChild.ParentResultAssetID is not { Length: > 0 } parentAsset ||
            !string.Equals(SourceFilePaths.Canonical(parentAsset), SourceFilePaths.Canonical(selectedResultPath), PathComparison()) ||
            !string.Equals(requestedChild.ParentResultRevision, selectedResultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        CatalogTreeNode[] children = (resultRow.Children ?? []).Where(child =>
            child.Locator is { } locator && SameLocator(locator, requestedChild)).ToArray();
        if (children.Length != 1) throw new SourceFileChangedException();
        return children[0];
    }

    private static CatalogTreeNode ResolveExactMapNode(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator requested)
    {
        CatalogTreeNode[] matches = WalkMapNodes(roots).Where(node => node.Locator is { } locator &&
            SameLocator(locator, requested)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(LocatorIdentity(left), LocatorIdentity(right), StringComparison.Ordinal);

    private static string LocatorIdentity(CatalogTreeNodeLocator locator) => string.Join('\u001f',
        locator.RootName, locator.NodeType, locator.NodeName,
        locator.NodePath is null ? string.Empty : string.Join('.', locator.NodePath),
        CanonicalIdentity(locator.AssetID), locator.MapType ?? string.Empty,
        locator.Filename ?? string.Empty, locator.FilenameRelative ?? string.Empty,
        CanonicalIdentity(locator.ParentGeometryAssetID),
        locator.ParentGeometryNodePath is null ? string.Empty : string.Join('.', locator.ParentGeometryNodePath),
        locator.ParentGeometryNodeName ?? string.Empty,
        CanonicalIdentity(locator.AssociationAssetID), locator.AssociationKind ?? string.Empty,
        CanonicalIdentity(locator.ParentPlanAssetID),
        locator.ParentPlanNodePath is null ? string.Empty : string.Join('.', locator.ParentPlanNodePath),
        locator.ParentPlanNodeName ?? string.Empty,
        CanonicalIdentity(locator.ParentResultAssetID),
        locator.ParentResultNodePath is null ? string.Empty : string.Join('.', locator.ParentResultNodePath),
        locator.ParentResultNodeType ?? string.Empty,
        locator.ParentResultNodeName ?? string.Empty,
        locator.ParentResultRevision ?? string.Empty);

    private static string CanonicalIdentity(string? path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty : SourceFilePaths.Canonical(path);

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
