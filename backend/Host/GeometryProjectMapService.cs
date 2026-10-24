using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Ras.Observables;
using Ras.Layers;
using Utility.Files;
using Visual.Mapping.Observables;

namespace Ras.Backend;

/// <summary>
/// Source-backed, read-only saved-map data for one exact Geometry node.
/// Project maps resolve under Geometries, Buildable maps under Layers,
/// Geometry maps under Focused Geometries, and Plan maps under an exact
/// Focused Plans child or an exact shared Matching Layers row. Focused Plan
/// Geometry carries one exact parent Plan locator; shared Geometry carries
/// the complete ordered Focused Plans set. Map type is not inferred from the
/// project's separate sidebar tree mode.
/// </summary>
public static partial class ModelEditorAdapter
{
    private const int GeometryMapMaximumPageSize = 512;
    private const int GeometryMapMaximumScannedSourceItems = 16_384;
    private const int GeometryMapMaximumVerticesPerPage = 500_000;
    public static Task<MapGeometrySnapshot> LoadMapGeometryAsync(MapGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapGeometryRequest(request);

        // Keep ProjectPath exactly as supplied by the native model session. It
        // is the relative-path base used by the retained Project/HDF readers.
        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        string projectCanonicalPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectAliasPath);
        MapTreeSnapshot mapTree = MapTreeService.Load(request.Map);
        string visualizationRevision = mapTree.VisualizationRevision;
        ResolvedGeometryPlanOwner[] planOwners = ResolveMapGeometryPlanOwners(mapTree.Roots,
            request.Map, request.GeometryNode, request.PlanNodes);
        VerifyPlanOwnerRevisions(planOwners);
        CatalogTreeNode geometryRow = ResolveMapGeometryRow(mapTree.Roots,
            request.Map, request.GeometryNode, request.PlanNodes);
        CatalogTreeNodeLocator geometryLocator = geometryRow.Locator
            ?? throw new SourceFileChangedException();
        string assetPath = geometryRow.AssetID ?? geometryLocator.AssetID
            ?? throw new SourceFileChangedException();
        string canonicalGeometryPath = CanonicalGeometryMapPath(assetPath);
        string geometryRevision = File.Exists(canonicalGeometryPath)
            ? Revision(canonicalGeometryPath)
            : "missing";
        string contextToken = CreateMapGeometryContextToken(request.Map, geometryLocator,
            projectCanonicalPath, projectRevision, visualizationRevision,
            canonicalGeometryPath, geometryRevision, planOwners);
        if (request.ContextToken is { Length: > 0 } expectedContext &&
            !string.Equals(expectedContext, contextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        bool geometryChecked = geometryRow.Checked == "checked";
        bool geometryEffectiveVisible = MapTreeService.IsEffectivelyVisible(mapTree.Roots, geometryRow);
        MapGeometryLayerState topologyState = LayerState(mapTree.Roots, geometryRow, "ConceptualMesh",
            "available", null);
        MapGeometryLayerState flowAreasState = LayerState(mapTree.Roots, geometryRow, "FlowAreaLayer",
            "available", null);
        MapGeometryLayerState culvertsState = LayerState(mapTree.Roots, geometryRow, "CulvertBarrelLayer",
            "unavailable", "This Geometry has no CulvertBarrelLayer child.");

        GeometryTopologyMap? topology = null;
        int topologySourceFeatureCount = 0;
        int topologyOmittedSourceFeatureCount = 0;
        GeometryMeshFlowAreaRow[] flowAreaRows = [];
        GeometryCulvertFeatureRow[] culvertRows = [];
        string? sourceFailure = geometryRevision == "missing" ? "The Geometry HDF source file is missing." : null;

        if (sourceFailure is null)
        {
            try
            {
                using var context = new ModelContext(new ModelRequest(assetPath, "Geometry",
                    request.Map.ProjectPath, request.Map.ProjectXml,
                    CoreLayerContext: request.Map.CoreLayerContext));
                ObservableGeometry geometry = context.Observable as ObservableGeometry
                    ?? throw new InvalidDataException("The selected source is not an ObservableGeometry.");

                try
                {
                    if (geometry.ConceptualMeshLayer.Layer.LoadFailed)
                        throw new IOException("The source topology could not be loaded; it was left unchanged.");
                    ObservableConceptualMeshTopology? sourceTopology =
                        geometry.ConceptualMeshLayer.Topology as ObservableConceptualMeshTopology;
                    if (sourceTopology is not null)
                    {
                        GeometryTopologyMapBuild mapBuild = MapData(sourceTopology);
                        topology = mapBuild.Map;
                        topologySourceFeatureCount = SourceTopologyFeatureCount(sourceTopology);
                        topologyOmittedSourceFeatureCount = Math.Max(0,
                            topologySourceFeatureCount - topology.Features.Length);
                    }
                }
                catch (Exception error) when (IsGeometryMapSourceFailure(error))
                {
                    topologyState = LayerState(mapTree.Roots, geometryRow, "ConceptualMesh",
                        "failed", error.Message);
                }

                try
                {
                    flowAreaRows = geometry.FlowAreaLayer.Features
                        .Select((flowArea, index) => FlowAreaSummary(flowArea, index, propertyTablesQueuedForParentSave: false)).ToArray();
                }
                catch (Exception error) when (IsGeometryMapSourceFailure(error))
                {
                    flowAreasState = LayerState(mapTree.Roots, geometryRow, "FlowAreaLayer",
                        "failed", error.Message);
                }

                try
                {
                    if (geometry.CulvertBarrelLayer is not null)
                    {
                        GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
                        culvertRows = BarrelTable(geometry, identities).Rows;
                    }
                }
                catch (Exception error) when (IsGeometryMapSourceFailure(error))
                {
                    culvertsState = LayerState(mapTree.Roots, geometryRow, "CulvertBarrelLayer",
                        "failed", error.Message);
                }
            }
            catch (Exception error) when (IsGeometryMapSourceFailure(error))
            {
                sourceFailure = error.Message;
            }
        }

        if (sourceFailure is not null)
        {
            topologyState = LayerState(mapTree.Roots, geometryRow, "ConceptualMesh", "failed", sourceFailure);
            flowAreasState = LayerState(mapTree.Roots, geometryRow, "FlowAreaLayer", "failed", sourceFailure);
            culvertsState = LayerState(mapTree.Roots, geometryRow, "CulvertBarrelLayer", "failed", sourceFailure);
        }
        else
        {
            if (topologyState.Locator is not null && topologyState.Status != "failed")
                topologyState = LayerState(mapTree.Roots, geometryRow, "ConceptualMesh",
                    topology is null ? "empty" : topology.Features.Length == 0 ? "empty" : "available",
                    topology is null ? "The saved Geometry has no conceptual topology." : null);
            if (topologyOmittedSourceFeatureCount > 0 &&
                (topologyState.Status is "available" or "empty"))
                topologyState = topologyState with { Status = "partial",
                    Diagnostic = "Some source topology rows could not be projected to map geometry." };
            if (flowAreasState.Locator is not null && flowAreasState.Status != "failed")
            {
                bool missingMeshes = flowAreaRows.Any(row => !row.HasMesh || row.HasMeshErrors);
                int totalCells = flowAreaRows.Sum(row => Math.Max(0, row.CellCount));
                flowAreasState = LayerState(mapTree.Roots, geometryRow, "FlowAreaLayer",
                    missingMeshes ? "partial" : totalCells == 0 ? "empty" : "available",
                    missingMeshes ? "One or more source flow areas have no readable mesh or report mesh errors." :
                    totalCells == 0 ? "The saved Geometry has no source mesh cells." : null);
            }
            if (culvertsState.Status != "failed")
            {
                bool hasSourceNode = culvertsState.Locator is not null;
                culvertsState = LayerState(mapTree.Roots, geometryRow, "CulvertBarrelLayer",
                    !hasSourceNode ? "unavailable" : culvertRows.Length == 0 ? "empty" : "available",
                    !hasSourceNode ? "This Geometry has no CulvertBarrelLayer child." :
                    culvertRows.Length == 0 ? "The saved Geometry has no culvert barrel features." : null);
            }
        }

        MapGeometryTopologyPage? topologyPage = request.SkipTopologyPage ? null :
            BuildTopologyPage(topology, topologyState, request, contextToken,
                topologySourceFeatureCount, topologyOmittedSourceFeatureCount);
        MapGeometryFlowAreasPage? flowAreaPage = null;
        if (!request.SkipFlowAreasPage)
        {
            try
            {
                flowAreaPage = BuildFlowAreasPage(flowAreaRows, flowAreasState, request, contextToken, assetPath);
            }
            catch (Exception error) when (IsGeometryMapSourceFailure(error))
            {
                // Page reads reopen the retained HDF reader after summary extraction.
                // Surface a failed layer explicitly if this second source read fails.
                flowAreasState = LayerState(mapTree.Roots, geometryRow, "FlowAreaLayer", "failed", error.Message);
                flowAreaPage = BuildFlowAreasPage(flowAreaRows, flowAreasState, request, contextToken, assetPath);
            }
        }
        MapGeometryCulvertsPage? culvertPage = request.SkipCulvertsPage ? null :
            BuildCulvertsPage(culvertRows, culvertsState, request, contextToken);

        if ((topologyState.Status is "available" or "partial") &&
            (topologyOmittedSourceFeatureCount > 0 || topologyPage is { InvalidFeatureCount: > 0 }))
        {
            var diagnostics = new List<string>();
            if (topologyOmittedSourceFeatureCount > 0)
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

        GeometryTopologyExtent? globalExtent = UnionExtents(
            topologyPage?.SourceExtent ?? topology?.Extent,
            flowAreaPage?.SourceExtent ?? FlowAreaExtent(flowAreaRows),
            culvertPage?.SourceExtent ?? CulvertExtent(culvertRows));

        // Recheck exact on-disk identities after all source objects were read.
        // MapTree.Load performs the .vis revision guard and preserves this
        // request's original standardized project path for relative references.
        bool geometryStillMatches = geometryRevision == "missing"
            ? !File.Exists(canonicalGeometryPath)
            : File.Exists(canonicalGeometryPath) && Revision(canonicalGeometryPath) == geometryRevision;
        if (Revision(projectAliasPath) != projectRevision ||
            SourceFilePaths.Canonical(projectAliasPath) != projectCanonicalPath || !geometryStillMatches ||
            !PlanOwnerRevisionsMatch(planOwners))
            throw new SourceFileChangedException();
        MapTreeSnapshot finalTree = MapTreeService.Load(request.Map with
        { ExpectedVisualizationRevision = visualizationRevision });
        ResolvedGeometryPlanOwner[] finalPlanOwners = ResolveMapGeometryPlanOwners(finalTree.Roots,
            request.Map, request.GeometryNode, request.PlanNodes);
        _ = ResolveMapGeometryRow(finalTree.Roots, request.Map with
        { ExpectedVisualizationRevision = visualizationRevision }, request.GeometryNode, request.PlanNodes);
        bool finalGeometryMatch = geometryRevision == "missing"
            ? !File.Exists(canonicalGeometryPath)
            : File.Exists(canonicalGeometryPath) && Revision(canonicalGeometryPath) == geometryRevision;
        if (finalTree.VisualizationRevision != visualizationRevision ||
            Revision(projectAliasPath) != projectRevision || !finalGeometryMatch ||
            !PlanOwnerRevisionsMatch(finalPlanOwners) ||
            !SamePlanOwnerCaptures(planOwners, finalPlanOwners))
            throw new SourceFileChangedException();

        var result = new MapGeometrySnapshot(
            request.Map.MapID, request.Map.MapIndex, request.Map.MapName, request.Map.MapType,
            projectCanonicalPath, projectRevision, request.Map.ProjectXmlSHA,
            visualizationRevision, contextToken, geometryLocator, geometryRow.Name,
            canonicalGeometryPath, geometryRevision, geometryChecked, geometryEffectiveVisible,
            globalExtent, topologyState, flowAreasState, culvertsState,
            topologyPage, flowAreaPage, culvertPage,
            planOwners.Select(owner => owner.Locator).ToArray());
        return Task.FromResult(result);
    }

    private static void ValidateMapGeometryRequest(MapGeometryRequest request)
    {
        if (request.Map is null || request.GeometryNode is null)
            throw new ArgumentException("A saved map and exact Geometry locator are required.");
        if (request.Map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("Geometry source rendering is supported only for saved Project, Buildable, Geometry, and Plan maps.");
        bool requiresSerializedPath = request.Map.MapType is "Buildable" or "Geometry" ||
            request.Map.MapType == "Plan" && request.GeometryNode.RootName == "Matching Layers";
        if (request.GeometryNode.NodeType != "Geometry" || string.IsNullOrWhiteSpace(request.GeometryNode.AssetID))
            throw new ArgumentException("The selected map row is not an exact Geometry source node.");
        if ((requiresSerializedPath && request.GeometryNode.NodePath is not { Length: >= 2 }) ||
            request.GeometryNode.NodePath is { Length: < 2 })
            throw new SourceFileChangedException();
        if (request.Map.MapType == "Plan")
        {
            CatalogTreeNodeLocator[] planNodes = request.PlanNodes ?? [];
            if (planNodes.Length > 256 || planNodes.Any(node => node is null))
                throw new ArgumentException("Plan geometry requests must contain at most 256 exact Plan locators.",
                    nameof(request.PlanNodes));
            if (request.GeometryNode.RootName == "Focused Plans")
            {
                if (string.IsNullOrWhiteSpace(request.GeometryNode.ParentPlanAssetID) ||
                    request.GeometryNode.ParentPlanNodePath is not { Length: >= 2 } parentPath ||
                    string.IsNullOrWhiteSpace(request.GeometryNode.ParentPlanNodeName) ||
                    planNodes.Length != 1 || !SamePlanIdentity(planNodes[0], request.GeometryNode))
                    throw new SourceFileChangedException();
                if (request.GeometryNode.NodePath is { } focusedGeometryPath &&
                    (focusedGeometryPath.Length < parentPath.Length + 1 ||
                     !focusedGeometryPath.Take(parentPath.Length).SequenceEqual(parentPath)))
                    throw new SourceFileChangedException();
                if (request.GeometryNode.AssociationKind != "Geometry" ||
                    request.GeometryNode.AssociationAssetID is not { Length: > 0 } focusedAssociation ||
                    !SamePath(focusedAssociation, request.GeometryNode.AssetID!))
                    throw new SourceFileChangedException();
            }
            else if (request.GeometryNode.RootName == "Matching Layers")
            {
                if (planNodes.Length < 2 || HasPlanOwner(request.GeometryNode) ||
                    request.GeometryNode.AssociationKind != "Geometry" ||
                    string.IsNullOrWhiteSpace(request.GeometryNode.AssociationAssetID))
                    throw new SourceFileChangedException();
            }
            else throw new SourceFileChangedException();
        }
        else if ((request.PlanNodes?.Length ?? 0) != 0 || HasPlanOwner(request.GeometryNode) ||
                 request.GeometryNode.RootName != GeometryRootName(request.Map.MapType) ||
                 request.GeometryNode.AssociationAssetID is not null ||
                 request.GeometryNode.AssociationKind is not null)
            throw new SourceFileChangedException();
        if (request.PageSize is < 1 or > GeometryMapMaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize),
                $"Page size must be between 1 and {GeometryMapMaximumPageSize}.");
        if (request.Viewport is { } viewport &&
            (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
             !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
             viewport.MinX > viewport.MaxX || viewport.MinY > viewport.MaxY))
            throw new ArgumentException("The map viewport must be a finite, ordered world-coordinate extent.");
    }

    private static string GeometryRootName(string mapType) => mapType switch
    {
        "Project" => "Geometries",
        "Buildable" => "Layers",
        "Geometry" => "Focused Geometries",
        "Plan" => "Focused Plans",
        _ => throw new NotSupportedException("Geometry source rendering is not supported for map type " + mapType + ".")
    };

    private static CatalogTreeNode ResolveMapGeometryRow(CatalogTreeNode[] roots,
        MapTreeRequest map, CatalogTreeNodeLocator requested, CatalogTreeNodeLocator[]? planNodes)
    {
        string mapType = map.MapType;
        bool requiresSerializedPath = mapType is "Buildable" or "Geometry" ||
            mapType == "Plan" && requested.RootName == "Matching Layers";
        if ((requiresSerializedPath && requested.NodePath is not { Length: >= 2 }) ||
            requested.NodePath is { Length: < 2 } || string.IsNullOrWhiteSpace(requested.AssetID))
            throw new SourceFileChangedException();
        if (mapType == "Plan")
            return requested.RootName switch
            {
                "Focused Plans" => ResolvePlanGeometryRow(roots, map, requested, planNodes ?? []),
                "Matching Layers" => ResolvePlanMatchingGeometryRow(roots, map, requested, planNodes ?? []),
                _ => throw new SourceFileChangedException()
            };
        string expectedRootName = GeometryRootName(mapType);
        if (!string.Equals(requested.RootName, expectedRootName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        int[]? requestedPath = requested.NodePath;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string expected = SourceFilePaths.Canonical(requested.AssetID!);
        CatalogTreeNode[] matches = WalkMapNodes(roots).Where(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            if (locator?.NodeType != "Geometry" || node.AssetID is not { Length: > 0 } assetPath)
                return false;
            if (!string.Equals(locator.RootName, expectedRootName, StringComparison.Ordinal) ||
                !string.Equals(CanonicalGeometryMapPath(assetPath), expected, pathComparison) ||
                !string.Equals(locator.NodeName, requested.NodeName, StringComparison.Ordinal) ||
                !string.Equals(locator.NodeType, requested.NodeType, StringComparison.Ordinal))
                return false;
            return requestedPath is null ||
                locator.NodePath is not null && locator.NodePath.SequenceEqual(requestedPath);
        }).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static CatalogTreeNode ResolvePlanGeometryRow(CatalogTreeNode[] roots,
        MapTreeRequest map, CatalogTreeNodeLocator requested, CatalogTreeNodeLocator[] planNodes)
    {
        if (requested.ParentPlanNodePath is not { Length: >= 2 } parentPath ||
            string.IsNullOrWhiteSpace(requested.ParentPlanAssetID) ||
            string.IsNullOrWhiteSpace(requested.ParentPlanNodeName))
            throw new SourceFileChangedException();

        CatalogTreeNode[] planRoots = roots.Where(root => root.Name == "Focused Plans").ToArray();
        if (planRoots.Length != 1) throw new SourceFileChangedException();
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string expectedPlanAsset = SourceFilePaths.Canonical(requested.ParentPlanAssetID);
        string expectedGeometryAsset = SourceFilePaths.Canonical(requested.AssetID!);
        CatalogTreeNode[] planMatches = WalkMapNodes(planRoots[0].Children ?? []).Where(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            return locator?.NodeType == "Plan" && locator.NodePath is not null &&
                locator.NodePath.SequenceEqual(parentPath) &&
                string.Equals(locator.RootName, "Focused Plans", StringComparison.Ordinal) &&
                string.Equals(locator.NodeName, requested.ParentPlanNodeName, StringComparison.Ordinal) &&
                node.AssetID is { Length: > 0 } asset &&
                string.Equals(SourceFilePaths.Canonical(asset), expectedPlanAsset, pathComparison);
        }).ToArray();
        if (planMatches.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNode plan = planMatches[0];
        CatalogTreeNodeLocator planLocator = plan.Locator ?? throw new SourceFileChangedException();
        if (planNodes.Length != 1 || !SameLocator(planLocator, planNodes[0]) ||
            !SamePlanIdentity(planNodes[0], requested))
            throw new SourceFileChangedException();

        EnsurePlanGeometryAssociation(map, requested, [planLocator], matching: false);

        CatalogTreeNode[] geometryMatches = (plan.Children ?? []).Where(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            if (locator?.NodeType != "Geometry" || node.AssetID is not { Length: > 0 } asset ||
                !string.Equals(locator.RootName, "Focused Plans", StringComparison.Ordinal) ||
                !string.Equals(locator.NodeName, requested.NodeName, StringComparison.Ordinal) ||
                !string.Equals(SourceFilePaths.Canonical(asset), expectedGeometryAsset, pathComparison))
                return false;
            if (requested.NodePath is null) return locator.NodePath is null;
            return locator.NodePath is not null && locator.NodePath.SequenceEqual(requested.NodePath);
        }).ToArray();
        if (geometryMatches.Length != 1) throw new SourceFileChangedException();

        CatalogTreeNodeLocator actual = geometryMatches[0].Locator!;
        if (!string.Equals(actual.ParentPlanAssetID, requested.ParentPlanAssetID, pathComparison) ||
            actual.ParentPlanNodePath is null || !actual.ParentPlanNodePath.SequenceEqual(parentPath) ||
            !string.Equals(actual.ParentPlanNodeName, requested.ParentPlanNodeName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return geometryMatches[0];
    }

    private static CatalogTreeNode ResolvePlanMatchingGeometryRow(CatalogTreeNode[] roots,
        MapTreeRequest map, CatalogTreeNodeLocator requested, CatalogTreeNodeLocator[] planNodes)
    {
        if (HasPlanOwner(requested) || requested.NodePath is not { Length: >= 2 } requestedPath ||
            requested.AssociationKind != "Geometry" ||
            requested.AssociationAssetID is not { Length: > 0 } associationPath ||
            requested.AssetID is not { Length: > 0 } geometryAsset || planNodes.Length < 2)
            throw new SourceFileChangedException();
        CatalogTreeNode focused = roots.SingleOrDefault(root => root.Name == "Focused Plans")
            ?? throw new SourceFileChangedException();
        CatalogTreeNode matching = roots.SingleOrDefault(root => root.Name == "Matching Layers")
            ?? throw new SourceFileChangedException();
        CatalogTreeNodeLocator[] actualPlanLocators = (focused.Children ?? [])
            .Where(node => node.Locator?.NodeType == "Plan")
            .Select(node => node.Locator!).ToArray();
        if (actualPlanLocators.Length < 2 || actualPlanLocators.Length != planNodes.Length ||
            !actualPlanLocators.Zip(planNodes).All(pair => SameLocator(pair.First, pair.Second)))
            throw new SourceFileChangedException();

        StringComparison pathComparison = PathComparison();
        string expectedAsset = CanonicalGeometryMapPath(geometryAsset);
        string expectedAssociation = CanonicalGeometryMapPath(associationPath);
        if (!string.Equals(expectedAsset, expectedAssociation, pathComparison))
            throw new SourceFileChangedException();
        CatalogTreeNode[] matchingRows = (matching.Children ?? []).Where(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            return locator is { NodeType: "Geometry", AssociationKind: "Geometry" } &&
                locator.AssociationAssetID is { Length: > 0 } associatedAsset &&
                node.AssetID is { Length: > 0 } sourceAsset &&
                string.Equals(locator.RootName, "Matching Layers", StringComparison.Ordinal) &&
                string.Equals(locator.NodeName, requested.NodeName, StringComparison.Ordinal) &&
                locator.NodePath is { Length: >= 2 } path && path.SequenceEqual(requestedPath) &&
                string.Equals(CanonicalGeometryMapPath(sourceAsset), expectedAsset, pathComparison) &&
                string.Equals(CanonicalGeometryMapPath(associatedAsset), expectedAsset, pathComparison) &&
                SameLocator(locator, requested);
        }).ToArray();
        if (matchingRows.Length != 1) throw new SourceFileChangedException();
        EnsurePlanGeometryAssociation(map, requested, actualPlanLocators, matching: true);
        return matchingRows[0];
    }

    private static void EnsurePlanGeometryAssociation(MapTreeRequest map,
        CatalogTreeNodeLocator requested, CatalogTreeNodeLocator[] planLocators, bool matching)
    {
        if (planLocators.Length == 0) throw new SourceFileChangedException();
        string[] planIDs = planLocators.Select(locator => locator.AssetID ??
            throw new SourceFileChangedException()).ToArray();
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            map.ProjectPath, map.ProjectXml, Mode: "Plan", FocusedPlanIds: planIDs), "Plan");
        string rootName = matching ? "Matching Layers" : "Focused Plans";
        CatalogTreeNode root = defaults.SingleOrDefault(node => node.Name == rootName)
            ?? throw new SourceFileChangedException();
        CatalogTreeNode[] expectedRows;
        if (matching)
        {
            if (planLocators.Length < 2) throw new SourceFileChangedException();
            // CatalogTreeService reproduces the retained Plan tree's actual
            // source-object match, while the saved map row carries an explicit
            // AssociationKind/AssociationAssetID annotation. Do not require
            // those map-only annotations from the source-default projection.
            expectedRows = (root.Children ?? []).Where(node => node.Locator is
                { NodeType: "Geometry" } && node.AssetID is { Length: > 0 })
                .Where(node => node.Locator is { } locator &&
                    SamePath(node.AssetID!, requested.AssetID!) &&
                    string.Equals(locator.NodeName, requested.NodeName, StringComparison.Ordinal))
                .ToArray();
        }
        else
        {
            // Source-default Plan rows have a concrete source class and asset,
            // but gain their map locator only when MapTreeService annotates
            // the saved tree. Validate this raw projection by its exact source
            // type/path/name instead of requiring a map-only locator.
            CatalogTreeNode[] planRows = (root.Children ?? []).Where(node =>
                node.SourceClass == "ObservablePlan" &&
                node.AssetID is { Length: > 0 } asset && SamePath(asset, planIDs[0]) &&
                string.Equals(node.Name, planLocators[0].NodeName, StringComparison.Ordinal))
                .ToArray();
            if (planRows.Length != 1) throw new SourceFileChangedException();
            expectedRows = (planRows[0].Children ?? []).Where(node => node.Locator is
                { NodeType: "Geometry" } locator && node.AssetID is { Length: > 0 })
                .Where(node => node.Locator is { } locator &&
                    SamePath(node.AssetID!, requested.AssetID!) &&
                    string.Equals(locator.NodeName, requested.NodeName, StringComparison.Ordinal))
                .ToArray();
        }
        if (expectedRows.Length != 1) throw new SourceFileChangedException();
    }

    private static ResolvedGeometryPlanOwner[] ResolveMapGeometryPlanOwners(CatalogTreeNode[] roots,
        MapTreeRequest map, CatalogTreeNodeLocator requested, CatalogTreeNodeLocator[]? requestPlanNodes)
    {
        CatalogTreeNodeLocator[] requestedPlans = requestPlanNodes ?? [];
        if (map.MapType != "Plan")
        {
            if (requestedPlans.Length != 0) throw new SourceFileChangedException();
            return [];
        }

        CatalogTreeNode focusedRoot = roots.SingleOrDefault(root => root.Name == "Focused Plans")
            ?? throw new SourceFileChangedException();
        CatalogTreeNode[] focusedPlanRows = (focusedRoot.Children ?? [])
            .Where(node => node.Locator?.NodeType == "Plan").ToArray();
        if (requestedPlans.Any(locator => locator is null)) throw new SourceFileChangedException();
        if (focusedPlanRows.Length == 0) throw new SourceFileChangedException();
        CatalogTreeNode[] selectedRows;
        if (requested.RootName == "Focused Plans")
        {
            if (requested.ParentPlanNodePath is not { Length: >= 2 } parentPath ||
                string.IsNullOrWhiteSpace(requested.ParentPlanAssetID) ||
                string.IsNullOrWhiteSpace(requested.ParentPlanNodeName) || requestedPlans.Length != 1)
                throw new SourceFileChangedException();
            selectedRows = focusedPlanRows.Where(row => row.Locator is { } locator &&
                locator.NodePath is not null && locator.NodePath.SequenceEqual(parentPath) &&
                string.Equals(locator.NodeName, requested.ParentPlanNodeName, StringComparison.Ordinal) &&
                locator.AssetID is { Length: > 0 } asset && SamePath(asset, requested.ParentPlanAssetID)).ToArray();
            if (selectedRows.Length != 1 || selectedRows[0].Locator is not { } actualPlan ||
                !SameLocator(actualPlan, requestedPlans[0]))
                throw new SourceFileChangedException();
        }
        else if (requested.RootName == "Matching Layers")
        {
            if (focusedPlanRows.Length < 2 || requestedPlans.Length != focusedPlanRows.Length ||
                !focusedPlanRows.Zip(requestedPlans).All(pair => pair.First.Locator is { } actual &&
                    SameLocator(actual, pair.Second)))
                throw new SourceFileChangedException();
            selectedRows = focusedPlanRows;
        }
        else throw new SourceFileChangedException();

        var owners = new List<ResolvedGeometryPlanOwner>(selectedRows.Length);
        foreach (CatalogTreeNode row in selectedRows)
        {
            CatalogTreeNodeLocator locator = row.Locator ?? throw new SourceFileChangedException();
            string path = Path.GetFullPath(row.AssetID ?? locator.AssetID ??
                throw new SourceFileChangedException());
            string canonical = SourceFilePaths.Canonical(path);
            owners.Add(new ResolvedGeometryPlanOwner(locator, path, canonical, Revision(path)));
        }
        return owners.ToArray();
    }

    private static bool SamePlanIdentity(CatalogTreeNodeLocator plan,
        CatalogTreeNodeLocator geometry) =>
        plan.RootName == "Focused Plans" && plan.NodeType == "Plan" &&
        plan.NodePath is { Length: >= 2 } planPath &&
        geometry.ParentPlanNodePath is { Length: >= 2 } parentPath &&
        planPath.SequenceEqual(parentPath) &&
        string.Equals(plan.NodeName, geometry.ParentPlanNodeName, StringComparison.Ordinal) &&
        plan.AssetID is { Length: > 0 } asset && geometry.ParentPlanAssetID is { Length: > 0 } parentAsset &&
        SamePath(asset, parentAsset);

    private static bool HasPlanOwner(CatalogTreeNodeLocator locator) =>
        locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
        locator.ParentPlanNodeName is not null;

    private static void VerifyPlanOwnerRevisions(ResolvedGeometryPlanOwner[] owners)
    {
        if (!PlanOwnerRevisionsMatch(owners)) throw new SourceFileChangedException();
    }

    private static bool PlanOwnerRevisionsMatch(ResolvedGeometryPlanOwner[] owners) =>
        owners.All(owner => File.Exists(owner.Path) &&
            string.Equals(SourceFilePaths.Canonical(owner.Path), owner.CanonicalPath, PathComparison()) &&
            string.Equals(Revision(owner.Path), owner.Revision, StringComparison.Ordinal));

    private static bool SamePlanOwnerCaptures(ResolvedGeometryPlanOwner[] left,
        ResolvedGeometryPlanOwner[] right) => left.Length == right.Length &&
        left.Zip(right).All(pair => SameLocator(pair.First.Locator, pair.Second.Locator) &&
            SamePath(pair.First.CanonicalPath, pair.Second.CanonicalPath) &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.Ordinal));

    private static bool SamePath(string left, string right) =>
        string.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right), PathComparison());

    private static string CanonicalGeometryMapPath(string path) =>
        File.Exists(path) ? SourceFilePaths.Canonical(path) : Path.GetFullPath(path);

    private static IEnumerable<CatalogTreeNode> WalkMapNodes(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode nested in WalkMapNodes(children)) yield return nested;
        }
    }

    private static MapGeometryLayerState LayerState(CatalogTreeNode[] roots, CatalogTreeNode geometryRow,
        string nodeType, string status, string? diagnostic)
    {
        CatalogTreeNode[] matches = (geometryRow.Children ?? []).Where(node =>
            node.Locator?.NodeType == nodeType).ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        CatalogTreeNode? node = matches.SingleOrDefault();
        if (node is null)
            return new MapGeometryLayerState(null, null, false, false, "unavailable",
                diagnostic ?? $"The saved map tree does not contain a {nodeType} child.");
        bool checkedValue = node.Checked == "checked";
        return new MapGeometryLayerState(node.Id, node.Locator, checkedValue,
            MapTreeService.IsEffectivelyVisible(roots, node), status, diagnostic);
    }

    private static MapGeometryTopologyPage? BuildTopologyPage(GeometryTopologyMap? topology,
        MapGeometryLayerState state, MapGeometryRequest request, string contextToken,
        int sourceFeatureCount, int omittedSourceFeatureCount)
    {
        if (state.Status is "failed" or "unavailable" || topology is null)
            return new(topology?.Extent, sourceFeatureCount, 0, 0, 0,
                omittedSourceFeatureCount, [], true, null);
        GeometryTopologyMapFeature[] source = topology.Features;
        GeometryMapCursor? cursor = ReadMapGeometryCursor(request.TopologyCursor,
            contextToken, "topology", request.Viewport);
        int index = cursor?.NextSourceOrdinal ?? 0;
        if (index > source.Length) throw new SourceFileChangedException();
        int scanned = 0;
        int invalid = 0;
        List<GeometryTopologyMapFeature> features = [];
        while (index < source.Length && scanned < GeometryMapMaximumScannedSourceItems &&
               features.Count < request.PageSize)
        {
            GeometryTopologyMapFeature feature = source[index++];
            scanned++;
            if (!ValidTopologyFeature(feature)) { invalid++; continue; }
            if (request.Viewport is { } viewport && !MayIntersect(feature.Paths, viewport)) continue;
            features.Add(feature);
        }
        bool complete = index >= source.Length;
        string? next = complete ? null : WriteMapGeometryCursor(
            new(contextToken, "topology", ViewportIdentity(request.Viewport), index, 0, 0));
        return new(topology.Extent, sourceFeatureCount, scanned, features.Count, invalid,
            omittedSourceFeatureCount, features.ToArray(), complete, next);
    }

    private static MapGeometryFlowAreasPage? BuildFlowAreasPage(GeometryMeshFlowAreaRow[] areas,
        MapGeometryLayerState state, MapGeometryRequest request, string contextToken, string assetPath)
    {
        if (state.Status is "failed" or "unavailable")
            return new(FlowAreaExtent(areas), ToFlowAreaSources(areas), 0, 0, 0, 0, [], true, null);

        using var context = new ModelContext(new ModelRequest(assetPath, "Geometry",
            request.Map.ProjectPath, request.Map.ProjectXml,
            CoreLayerContext: request.Map.CoreLayerContext));
        ObservableGeometry geometry = context.Observable as ObservableGeometry
            ?? throw new InvalidDataException("The selected source is not an ObservableGeometry.");
        return BuildFlowAreasPage(areas, state, request, contextToken, geometry);
    }

    private static MapGeometryFlowAreasPage? BuildFlowAreasPage(GeometryMeshFlowAreaRow[] areas,
        MapGeometryLayerState state, MapGeometryRequest request, string contextToken, ObservableGeometry geometry)
    {
        if (state.Status is "failed" or "unavailable")
            return new(FlowAreaExtent(areas), ToFlowAreaSources(areas), 0, 0, 0, 0, [], true, null);
        GeometryMapCursor? cursor = ReadMapGeometryCursor(request.CellCursor,
            contextToken, "cells", request.Viewport);
        int meshIndex = cursor?.NextMeshIndex ?? 0;
        int cellIndex = cursor?.NextCellIndex ?? 0;
        int sourceCellCount = areas.Sum(row => Math.Max(0, row.CellCount));
        int scanned = 0;
        int invalid = 0;
        int vertices = 0;
        List<GeometryMeshMapCell> cells = [];
        IReadOnlyList<Observable2DFlowArea> flowAreas = geometry.FlowAreaLayer.Features;

        if (meshIndex > flowAreas.Count ||
            (meshIndex == flowAreas.Count && cellIndex != 0) ||
            (meshIndex < flowAreas.Count && (flowAreas[meshIndex].Mesh is not { } cursorMesh
                ? cellIndex != 0 : cellIndex > cursorMesh.CellCount)))
            throw new SourceFileChangedException();

        while (meshIndex < flowAreas.Count && scanned < GeometryMapMaximumScannedSourceItems &&
               cells.Count < request.PageSize)
        {
            Observable2DFlowArea area = flowAreas[meshIndex];
            Mesh? mesh = area.Mesh;
            if (mesh is null || cellIndex >= mesh.CellCount)
            {
                meshIndex++;
                cellIndex = 0;
                continue;
            }

            int currentMesh = meshIndex;
            int currentCell = cellIndex;
            Polygon polygon;
            try { polygon = mesh.CellPolygon(currentCell); }
            catch (Exception error) when (IsGeometryMapSourceFailure(error))
            {
                invalid++;
                scanned++;
                AdvanceCell(ref meshIndex, ref cellIndex, flowAreas);
                continue;
            }

            Point[] points = polygon.Points.ToArray();
            if (points.Length < 3 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            {
                invalid++;
                scanned++;
                AdvanceCell(ref meshIndex, ref cellIndex, flowAreas);
                continue;
            }
            if (request.Viewport is { } requestedViewport &&
                !polygon.AreaIntersects(ToSourceExtent(requestedViewport)))
            {
                scanned++;
                AdvanceCell(ref meshIndex, ref cellIndex, flowAreas);
                continue;
            }
            if (points.Length > GeometryMapMaximumVerticesPerPage)
            {
                invalid++;
                scanned++;
                AdvanceCell(ref meshIndex, ref cellIndex, flowAreas);
                continue;
            }
            if (cells.Count > 0 && vertices + points.Length > GeometryMapMaximumVerticesPerPage)
                break; // Leave this source cell unconsumed; the next cursor resumes here.

            string id = FlowAreaID(currentMesh) + ":cell:" + currentCell.ToString(System.Globalization.CultureInfo.InvariantCulture);
            cells.Add(new GeometryMeshMapCell(id, FlowAreaID(currentMesh), area.Name,
                currentMesh, currentCell,
                points.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray()));
            vertices += points.Length;
            scanned++;
            AdvanceCell(ref meshIndex, ref cellIndex, flowAreas);
        }

        bool complete = meshIndex >= flowAreas.Count;
        string? next = complete ? null : WriteMapGeometryCursor(
            new(contextToken, "cells", ViewportIdentity(request.Viewport), 0, meshIndex, cellIndex));
        return new(FlowAreaExtent(areas), ToFlowAreaSources(areas), sourceCellCount,
            scanned, cells.Count, invalid, cells.ToArray(), complete, next);
    }

    private static void AdvanceCell(ref int meshIndex, ref int cellIndex,
        IReadOnlyList<Observable2DFlowArea> flowAreas)
    {
        cellIndex++;
        while (meshIndex < flowAreas.Count)
        {
            Mesh? mesh = flowAreas[meshIndex].Mesh;
            if (mesh is not null && cellIndex < mesh.CellCount) return;
            meshIndex++;
            cellIndex = 0;
        }
    }

    private static MapGeometryCulvertsPage? BuildCulvertsPage(GeometryCulvertFeatureRow[] source,
        MapGeometryLayerState state, MapGeometryRequest request, string contextToken)
    {
        if (state.Status is "failed" or "unavailable")
            return new(CulvertExtent(source), 0, 0, 0, 0, [], true, null);
        GeometryMapCursor? cursor = ReadMapGeometryCursor(request.CulvertCursor,
            contextToken, "culverts", request.Viewport);
        int index = cursor?.NextSourceOrdinal ?? 0;
        if (index > source.Length) throw new SourceFileChangedException();
        int scanned = 0;
        int invalid = 0;
        List<GeometryCulvertFeatureRow> features = [];
        while (index < source.Length && scanned < GeometryMapMaximumScannedSourceItems &&
               features.Count < request.PageSize)
        {
            GeometryCulvertFeatureRow feature = source[index++];
            scanned++;
            if (feature.Polyline.Length < 2 || feature.Polyline.Any(point =>
                    !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            {
                invalid++;
                continue;
            }
            if (request.Viewport is { } viewport && !MayIntersect(feature.Polyline, viewport)) continue;
            features.Add(feature);
        }
        bool complete = index >= source.Length;
        string? next = complete ? null : WriteMapGeometryCursor(
            new(contextToken, "culverts", ViewportIdentity(request.Viewport), index, 0, 0));
        return new(CulvertExtent(source), source.Length, scanned, features.Count,
            invalid, features.ToArray(), complete, next);
    }

    private static MapGeometryFlowAreaSource[] ToFlowAreaSources(GeometryMeshFlowAreaRow[] rows) =>
        rows.Select(row => new MapGeometryFlowAreaSource(row.ID, row.RowIndex, row.Name,
            row.TargetedCellSpacing is { } spacing && double.IsFinite(spacing) ? spacing : null,
            row.HasMesh, row.CellCount, row.FaceCount, row.NodeCount,
            row.Extent is { } extent && IsValidMeshExtent(extent) ? extent : null,
            row.HasMeshErrors, row.HasPropertyTables, row.PropertyTablesPinned, row.Metadata)).ToArray();

    private static int SourceTopologyFeatureCount(ObservableConceptualMeshTopology topology) => checked(
        topology.Arcs.Count + topology.ControlPoints.Count + topology.Nodes.Count +
        topology.Topology.CompletedRegions.Count);

    private static bool IsValidMeshExtent(GeometryMeshExtent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY) &&
        double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY) &&
        extent.MinX <= extent.MaxX && extent.MinY <= extent.MaxY;

    private static GeometryTopologyExtent? FlowAreaExtent(GeometryMeshFlowAreaRow[] areas)
    {
        var extents = new List<GeometryTopologyExtent?>();
        foreach (GeometryMeshFlowAreaRow area in areas)
        {
            if (area.Extent is not { } extent) continue;
            extents.Add(new GeometryTopologyExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY));
        }
        return UnionExtents(extents);
    }

    private static GeometryTopologyExtent? CulvertExtent(GeometryCulvertFeatureRow[] rows)
    {
        GeometryCulvertCoordinate[] points = rows.SelectMany(row => row.Polyline)
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).ToArray();
        return points.Length == 0 ? null : new GeometryTopologyExtent(
            points.Min(point => point.X), points.Min(point => point.Y),
            points.Max(point => point.X), points.Max(point => point.Y));
    }

    private static GeometryTopologyExtent? UnionExtents(params GeometryTopologyExtent?[] extents) =>
        UnionExtents((IEnumerable<GeometryTopologyExtent?>)extents);

    private static GeometryTopologyExtent? UnionExtents(IEnumerable<GeometryTopologyExtent?> extents)
    {
        GeometryTopologyExtent[] valid = extents.Where(extent => extent is not null)
            .Select(extent => extent!).Where(IsValidGeometryExtent).ToArray();
        return valid.Length == 0 ? null : new GeometryTopologyExtent(
            valid.Min(extent => extent.MinX), valid.Min(extent => extent.MinY),
            valid.Max(extent => extent.MaxX), valid.Max(extent => extent.MaxY));
    }

    private static bool IsValidGeometryExtent(GeometryTopologyExtent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY) &&
        double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY) &&
        extent.MinX <= extent.MaxX && extent.MinY <= extent.MaxY;

    private static bool ValidTopologyFeature(GeometryTopologyMapFeature feature) =>
        feature.Paths.Length > 0 && feature.Paths.All(path => path.Length > 0 &&
            path.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));

    private static bool MayIntersect(GeometryTopologyCoordinate[][] paths, MapGeometryViewport viewport) =>
        paths.Any(path => MayIntersect(path, viewport));

    private static bool MayIntersect(GeometryTopologyCoordinate[] path, MapGeometryViewport viewport) =>
        path.Length > 0 &&
        path.Any(point => double.IsFinite(point.X) && double.IsFinite(point.Y)) &&
        path.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).Max(point => point.X) >= viewport.MinX &&
        path.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).Min(point => point.X) <= viewport.MaxX &&
        path.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).Max(point => point.Y) >= viewport.MinY &&
        path.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).Min(point => point.Y) <= viewport.MaxY;

    private static bool MayIntersect(GeometryCulvertCoordinate[] path, MapGeometryViewport viewport) =>
        path.Length > 0 &&
        path.Max(point => point.X) >= viewport.MinX && path.Min(point => point.X) <= viewport.MaxX &&
        path.Max(point => point.Y) >= viewport.MinY && path.Min(point => point.Y) <= viewport.MaxY;

    private static Extent ToSourceExtent(MapGeometryViewport viewport) =>
        new(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);

    private static GeometryMapCursor? ReadMapGeometryCursor(string? encoded,
        string contextToken, string layer, MapGeometryViewport? viewport)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            string padded = encoded.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            byte[] bytes = Convert.FromBase64String(padded);
            if (bytes.Length > 4096) throw new ArgumentException("The map cursor is too large.");
            GeometryMapCursor cursor = JsonSerializer.Deserialize<GeometryMapCursor>(bytes, BackendJson.Options)
                ?? throw new ArgumentException("The map cursor is invalid.");
            if (cursor.ContextToken != contextToken || cursor.Layer != layer ||
                cursor.ViewportIdentity != ViewportIdentity(viewport) ||
                cursor.NextSourceOrdinal < 0 || cursor.NextMeshIndex < 0 || cursor.NextCellIndex < 0 ||
                (layer == "cells" ? cursor.NextSourceOrdinal != 0 :
                    cursor.NextMeshIndex != 0 || cursor.NextCellIndex != 0))
                throw new SourceFileChangedException();
            return cursor;
        }
        catch (SourceFileChangedException) { throw; }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException("The map page cursor is invalid or belongs to another source context.", error);
        }
    }

    private static string WriteMapGeometryCursor(GeometryMapCursor cursor)
    {
        string token = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, BackendJson.Options));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ViewportIdentity(MapGeometryViewport? viewport) => viewport is null ? "all" :
        HashGeometryMapIdentity(string.Join("|", viewport.MinX.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            viewport.MinY.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            viewport.MaxX.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            viewport.MaxY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    private static string CreateMapGeometryContextToken(MapTreeRequest map,
        CatalogTreeNodeLocator geometry, string canonicalProjectPath, string projectRevision,
        string visualizationRevision, string canonicalGeometryPath, string geometryRevision,
        ResolvedGeometryPlanOwner[] planOwners)
    {
        string nodePath = geometry.NodePath is null ? "default" : string.Join('.', geometry.NodePath);
        string planOwnerIdentity = string.Join("\n", planOwners.Select(owner => string.Join('\u001f',
            LocatorIdentity(owner.Locator), owner.CanonicalPath, owner.Revision)));
        string payload = string.Join("\n", map.MapID, map.MapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            map.MapName, map.MapType, Path.GetFullPath(map.ProjectPath), canonicalProjectPath, projectRevision,
            map.ProjectXmlSHA ?? "", visualizationRevision, geometry.RootName, geometry.NodeType,
            geometry.NodeName, nodePath, canonicalGeometryPath, geometryRevision,
            geometry.ParentPlanAssetID ?? "",
            geometry.ParentPlanNodePath is null ? "" : string.Join('.', geometry.ParentPlanNodePath),
            geometry.ParentPlanNodeName ?? "", geometry.AssociationAssetID ?? "",
            geometry.AssociationKind ?? "", planOwnerIdentity);
        return HashGeometryMapIdentity(payload);
    }

    private static string HashGeometryMapIdentity(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsGeometryMapSourceFailure(Exception error) =>
        error is not (SourceFileChangedException or OperationCanceledException or
            OutOfMemoryException or StackOverflowException or AccessViolationException);

    private sealed record ResolvedGeometryPlanOwner(CatalogTreeNodeLocator Locator,
        string Path, string CanonicalPath, string Revision);

    private sealed record GeometryMapCursor(string ContextToken, string Layer,
        string ViewportIdentity, int NextSourceOrdinal, int NextMeshIndex, int NextCellIndex);
}
