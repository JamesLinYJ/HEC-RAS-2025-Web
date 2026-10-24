using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters.Modifiers;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Vectors;
using H5Assist;
using OSGeo.OSR;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Observables;
using Ras.Observables.Layers;
using Ras.Rendering;
using Utility.Files;

namespace Ras.Backend;

public sealed record CatalogTreeMapDocument(string Id, string Name, string Kind);
public sealed record CatalogTreeRequest(
    string ProjectPath,
    string? ProjectXml = null,
    string Mode = "Catalog",
    string[]? FocusedGeometryIds = null,
    string[]? FocusedPlanIds = null,
    string? MapId = null,
    CatalogTreeMapDocument[]? Maps = null,
    string[]? FocusedBoundaryConditionIds = null,
    string? ProjectSessionID = null,
    CoreLayerReferenceCapture[]? ExternalCoreLayerReferences = null,
    CoreLayerRequestContext? CoreLayerContext = null);
public sealed record CatalogTreeProperty(string Id, string Label, string Value);
public sealed record CatalogTreeSummary(string? Name = null, string? Filename = null,
    string? DateCreated = null, string? DateLastModified = null, string? Description = null);
public sealed record CatalogTreeBackupSummary(string Filename, string DateCreated, string DateLastModified,
    bool CanSaveAsNewGeometry = false, string? Revision = null,
    string? ParentGeometryPath = null, string? ParentGeometryRevision = null,
    string? ProjectRevision = null);
public sealed record CatalogTreeNode(
    string Id,
    string SourceClass,
    string Name,
    bool LocalizeName = false,
    CatalogTreeNode[]? Children = null,
    string? AssetID = null,
    CatalogTreeSummary? CatalogSummary = null,
    CatalogTreeBackupSummary? BackupSummary = null,
    CatalogTreeProperty[]? Properties = null,
    string? Checked = null,
    bool IsPinned = false,
    bool CanPin = false,
    bool CanRename = false,
    bool CanMove = false,
    bool CanAcceptChildren = false,
    bool CanCreateFolder = false,
    CatalogTreeNodeLocator? Locator = null,
    CatalogTreeLayerError[]? LayerErrors = null,
    string? DiagnosticStatus = null,
    string? DiagnosticSourceIdentity = null,
    string? DiagnosticSourceRevision = null,
    string? DiagnosticReadError = null,
    string? ResultComputeState = null,
    string? ResultComputeFailureReason = null,
    string? ResultComputeSourcePath = null,
    string? ResultComputeSourceRevision = null,
    int? ClassificationEditModeOpacity = null)
{
    // Request-scoped marker used only while a compute writer owns this exact
    // Result source. It is deliberately not a public AssetID and never crosses
    // the map-tree wire contract.
    [JsonIgnore]
    internal string? ProtectedResultSourcePath { get; init; }

    // Retained internally so MapTreeService can mirror MultiResultVisualContext's
    // Latest key-frame selection without adding implementation metadata to the
    // native tree wire model.
    internal DateTime? ResultEndTime { get; init; }
    internal string? ResultPlanName { get; init; }
    internal string? SelectedResultAssetID { get; init; }
    internal string? SelectedResultRevision { get; init; }
    internal SessionHdfResultOwner? SelectedSessionHdfResultOwner { get; init; }
}
public sealed record CatalogTreeNodeLocator(
    string RootName,
    string NodeType,
    string NodeName,
    int[]? NodePath = null,
    string? AssetID = null,
    string? MapType = null,
    string? Filename = null,
    string? FilenameRelative = null,
    string? ParentGeometryAssetID = null,
    int[]? ParentGeometryNodePath = null,
    string? ParentGeometryNodeName = null,
    string? AssociationAssetID = null,
    string? AssociationKind = null,
    string? ParentPlanAssetID = null,
    int[]? ParentPlanNodePath = null,
    string? ParentPlanNodeName = null,
    string? ParentResultNodeType = null,
    string? ParentResultAssetID = null,
    int[]? ParentResultNodePath = null,
    string? ParentResultNodeName = null,
    string? ParentResultRevision = null,
    string? ParentBoundaryConditionAssetID = null,
    int[]? ParentBoundaryConditionNodePath = null,
    string? ParentBoundaryConditionNodeName = null,
    SessionHdfResultOwner? SessionHdfResultOwner = null);
public sealed record CatalogTreeSnapshot(CatalogTreeNode[] Roots);

/// <summary>
/// Read-only source adapter for the two distinct HEC-RAS tree models. Catalog
/// roots match RasCatalogTree; map/project trees match ProjectTreeModeVM and
/// are assembled from the same loaded Project/ObservableProject objects.
/// </summary>
public static class CatalogTreeService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static CatalogTreeSnapshot LoadCatalog(CatalogTreeRequest request)
    {
        using var project = new CatalogProjectContext(request);
        CatalogTreeNode[] roots = BuildCatalog(project.Project, project.Observables, request.Maps ?? [],
            project.Diagnostics);
        return new CatalogTreeSnapshot(project.Diagnostics.Attach(roots));
    }

    public static CatalogTreeSnapshot LoadProject(CatalogTreeRequest request)
    {
        using var project = new CatalogProjectContext(request);
        CatalogTreeNode[] roots = BuildProjectTree(project.Project, project.Observables, request);
        return new CatalogTreeSnapshot(project.Diagnostics.Attach(roots));
    }

    public static CatalogTreeNode[] LoadMapTreeDefaults(CatalogTreeRequest request, string mapType)
        => LoadMapTreeDefaults(request, mapType, protectedReadPaths: null);

    internal static CatalogTreeNode[] LoadMapTreeDefaults(CatalogTreeRequest request, string mapType,
        string[]? protectedReadPaths)
    {
        using var project = new CatalogProjectContext(request, protectedReadPaths);
        CatalogTreeNode[] roots = mapType switch
        {
            "Project" => BuildProjectTree(project.Project, project.Observables, request with { Mode = "Project" })
                .Where(node => node.Name != "Layers").ToArray(),
            "Buildable" => BuildableMapTreeDefaults(project.Observables, request.FocusedGeometryIds ?? [],
                request.FocusedBoundaryConditionIds ?? []),
            "Geometry" => GeometryMapTreeDefaults(project.Project, project.Observables, request),
            "Plan" => PlanMapTreeDefaults(project.Project, project.Observables, request),
            _ => throw new NotSupportedException("Persistent map trees are supported only for Project, Buildable, Geometry and Plan maps.")
        };
        return project.Diagnostics.Attach(roots);
    }

    private static CatalogTreeNode[] BuildableMapTreeDefaults(ObservableProject observables,
        IEnumerable<string> geometryIDs, IEnumerable<string> boundaryConditionIDs)
    {
        var included = geometryIDs.Select(SourceFilePaths.Canonical).ToHashSet(PathComparer);
        CatalogTreeNode[] geometries = observables.Geometries
            .Select(GeometryProjectNode)
            .Where(node => node.AssetID is { } path && included.Contains(SourceFilePaths.Canonical(path)))
            .ToArray();
        var includedBoundaryConditions = boundaryConditionIDs.Select(SourceFilePaths.Canonical).ToHashSet(PathComparer);
        CatalogTreeNode[] boundaries = observables.BoundaryConditions
            .Select(BoundaryConditionProjectNode)
            .Where(node => node.AssetID is { } path &&
                includedBoundaryConditions.Contains(SourceFilePaths.Canonical(path)))
            .ToArray();
        return [Group("LayersSectionContext", "Layers", [..geometries, ..boundaries])];
    }

    private static CatalogTreeNode[] GeometryMapTreeDefaults(Project project, ObservableProject observables,
        CatalogTreeRequest request)
    {
        CatalogTreeNode[] roots = BuildProjectTree(project, observables, request with { Mode = "Geometry" });
        CatalogTreeNode focused = roots.Single(node => node.Name == "Focused Geometries");
        CatalogTreeNode matching = roots.Single(node => node.Name == "Matching Layers");
        CatalogTreeNode layers = WithDirectBoundaryConditions(roots.Single(node => node.Name == "Layers"),
            observables, request.FocusedBoundaryConditionIds ?? []);
        return [focused, matching, layers];
    }

    private static CatalogTreeNode[] PlanMapTreeDefaults(Project project, ObservableProject observables,
        CatalogTreeRequest request)
    {
        CatalogTreeNode[] roots = BuildProjectTree(project, observables, request with { Mode = "Plan" });
        CatalogTreeNode focused = roots.Single(node => node.Name == "Focused Plans");
        CatalogTreeNode matching = roots.Single(node => node.Name == "Matching Layers");
        CatalogTreeNode layers = roots.Single(node => node.Name == "Layers");

        CatalogTreeNode[] focusedPlans = (focused.Children ?? []).Select(plan => plan with
        {
            Children = (plan.Children ?? []).Select(child => PlanAssociation(child)).ToArray()
        }).ToArray();
        CatalogTreeNode[] matchingChildren = (matching.Children ?? []).Select(child =>
            PlanAssociation(child)).ToArray();
        CatalogTreeNode layersWithBoundaries = WithDirectBoundaryConditions(layers, observables,
            request.FocusedBoundaryConditionIds ?? []);
        return [focused with { Children = focusedPlans.Length == 0 ? null : focusedPlans },
            matching with { Children = matchingChildren.Length == 0 ? null : matchingChildren }, layersWithBoundaries];
    }

    private static CatalogTreeNode WithDirectBoundaryConditions(CatalogTreeNode layers,
        ObservableProject observables, IEnumerable<string> boundaryConditionIDs)
    {
        var included = boundaryConditionIDs.Select(SourceFilePaths.Canonical).ToHashSet(PathComparer);
        CatalogTreeNode[] boundaries = observables.BoundaryConditions
            .Select(BoundaryConditionProjectNode)
            .Where(node => node.AssetID is { } path && included.Contains(SourceFilePaths.Canonical(path)))
            .ToArray();
        return layers with { Children = [..layers.Children ?? [], ..boundaries] };
    }


    private static CatalogTreeNode PlanAssociation(CatalogTreeNode node)
    {
        string? kind = node.SourceClass switch
        {
            "ObservableGeometry" => "Geometry",
            "ObservableTerrain" => "Terrain",
            "ObservableNValueLayer" => "NValue",
            "ObservableClassificationLayer" => "Classification",
            "ObservableBoundaryCondition" => "BoundaryCondition",
            _ => null
        };
        return kind is null ? node : WithAssociation(node, kind);
    }

    private static CatalogTreeNode[] BuildCatalog(Project project, ObservableProject observables,
        IReadOnlyList<CatalogTreeMapDocument> maps, CatalogDiagnosticsService.Context diagnostics)
    {
        var modelChildren = new[]
        {
            Group("TerrainRootContext", "Terrains", observables.Terrains.Select(item => Node(item)).ToArray()),
            Group("GeometryRootContext", "Geometries",
                observables.Geometries.Select(item => GeometryCatalogNode(item, diagnostics)).ToArray()),
            Group("NValueRootContext", "NValues", observables.NValues.Select(item => Node(item)).ToArray()),
            Group("BoundaryConditionRootContext", "Boundary Conditions", observables.BoundaryConditions.Select(BoundaryConditionCatalogNode).ToArray()),
            Group("PlanRootContext", "Plans", observables.Plans.Select(PlanCatalogNode).ToArray()),
            Group("ResultRootContext", "Results", observables.Results.Select(ResultNode).ToArray()),
            Group("ClassificationRootContext", "Classifications", observables.Classifications.Select(item => Node(item)).ToArray())
        };

        var projectProperties = new List<CatalogTreeProperty>
        {
            new("ProjectCount/Terrains", "Terrains", observables.Terrains.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/Geometries", "Geometries", observables.Geometries.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/Plans", "Plans", observables.Plans.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/Results", "Results", observables.Results.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/BoundaryConditions", "Boundary Conditions", observables.BoundaryConditions.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/NValues", "N-Value Layers", observables.NValues.Count.ToString(CultureInfo.InvariantCulture)),
            new("ProjectCount/Classifications", "Classifications", observables.Classifications.Count.ToString(CultureInfo.InvariantCulture)),
            new("Project/Units", "Units", observables.Units.ToString())
        };
        if (observables.Projection is { } projection)
        {
            projectProperties.Add(new("Project/ProjectionName", "Projection", projection.GetName()));
            projectProperties.Add(new("Project/ProjectionWKT", "Well-Known Text", projection.GetWKT()));
        }
        var model = new CatalogTreeNode("catalog/model", "ModelSectionContext", "Project", true,
            modelChildren, CatalogSummary: new CatalogTreeSummary(project.Name, FullProjectPath(project),
                Description: project.Description), Properties: projectProperties.ToArray());
        var connections = project.ConnectionSource.Connections.Select((connection, index) =>
        {
            string id = "connection:" + index.ToString(CultureInfo.InvariantCulture);
            string type = connection.SaveXML().Attribute("Type")?.Value ?? connection.GetType().Name;
            var properties = new List<CatalogTreeProperty>
            {
                new("Name", "Name", connection.Name),
                new("Type", "Type", type),
                new("Description", "Description", connection.Description)
            };
            if (connection is PathExternalConnection pathConnection)
            {
                properties.Add(new("RelativePath", "Relative Path", pathConnection.RelativePath ?? ""));
                properties.Add(new("AbsolutePath", "Absolute Path", pathConnection.AbsolutePath ?? ""));
            }
            if (connection is DirectoryExternalConnection directoryConnection)
            {
                properties.Add(new("SearchPattern", "Filename Pattern", directoryConnection.SearchPattern ?? "*"));
                properties.Add(new("Recurse", "Include Subfolders", directoryConnection.Recurse.ToString()));
            }
            string sourceClass = connection is FileExternalConnection
                ? "ExternalFileConnectionCatalogNode"
                : "ExternalConnectionCatalogNode";
            return new CatalogTreeNode(id, sourceClass, connection.Name,
                Properties: properties.ToArray());
        }).ToArray();
        var externalConnections = Group("ExternalConnectionsSectionContext", "External Connections", connections);
        var mapNodes = maps.Select(map => new CatalogTreeNode("map:" + map.Id, "MapDocumentNode", map.Name,
            Properties: [new CatalogTreeProperty("Kind", "Kind", map.Kind)])).ToArray();
        var mapRoot = Group("MapsSectionContext", "Maps", mapNodes);
        return [model, externalConnections, mapRoot];
    }

    private static CatalogTreeNode PlanCatalogNode(ObservablePlan plan)
    {
        ObservableGeometry? geometry = plan.AssociatedGeometry;
        ObservableNValueLayer? nvalue = geometry?.AssociatedNValueLayer;
        var node = Node(plan);
        return node with
        {
            Properties = (node.Properties ?? []).Concat([
                new CatalogTreeProperty("PlanAssociation/Geometry", "Associated Geometry", geometry?.Name ?? ""),
                new CatalogTreeProperty("PlanAssociation/NValueMode", "Associated N-Value Mode", nvalue?.Mode.ToString() ?? ""),
                new CatalogTreeProperty("PlanAssociation/Classification", "Classification Layer", nvalue?.ClassificationLayer?.Name ?? "")
            ]).ToArray()
        };
    }

    private static CatalogTreeNode BoundaryConditionCatalogNode(ObservableBoundaryCondition condition)
    {
        var node = Node(condition);
        return node with
        {
            Properties = (node.Properties ?? []).Concat([
                new CatalogTreeProperty("BoundaryCondition/StartTime", "Start Time", condition.StartTime.ToString()),
                new CatalogTreeProperty("BoundaryCondition/EndTime", "End Time", condition.EndTime.ToString())
            ]).ToArray()
        };
    }

    private static CatalogTreeNode[] BuildProjectTree(Project project, ObservableProject observables,
        CatalogTreeRequest request)
    {
        var plans = observables.Plans.ToDictionary(item => SourceFilePaths.Canonical(AssetPath(project, item.Filename)!), PathComparer);
        var geometries = observables.Geometries.ToDictionary(item => SourceFilePaths.Canonical(AssetPath(project, item.Filename)!), PathComparer);
        var results = observables.Results.ToArray();

        CatalogTreeNode[] baseRoots =
        [
            Group("PlansSectionContext", "Plans", observables.Plans.Select(item => Node(item)).ToArray()),
            Group("ResultsSectionContext", "Results",
                ProjectResults(project, results, request.CoreLayerContext)),
            Group("GeometriesSectionContext", "Geometries", observables.Geometries.Select(GeometryProjectNode).ToArray()),
            Group("TerrainsSectionContext", "Terrains", observables.Terrains.Select(TerrainProjectNode).ToArray()),
            Group("SurfaceLayersSectionContext", "Surface Layers",
                observables.NValues.Select(item => Node(item)).Concat(observables.Classifications.Select(item => Node(item))).ToArray()),
            Group("MapLayersSectionContext", "Map Layers", []),
            Group("FeaturesSectionContext", "Features", []),
            Group("BoundaryConditionsSectionContext", "Boundary Conditions",
                observables.BoundaryConditions.Select(BoundaryConditionProjectNode).ToArray()),
            Group("LayersSectionContext", "Layers", [])
        ];

        return request.Mode switch
        {
            "Project" => baseRoots,
            "Plan" => PlanModeRoots(project, request.FocusedPlanIds ?? [], plans, results, baseRoots),
            "Geometry" => GeometryModeRoots(request.FocusedGeometryIds ?? [], geometries, baseRoots),
            _ => throw new NotSupportedException("Unsupported project tree mode: " + request.Mode)
        };
    }

    private static CatalogTreeNode[] PlanModeRoots(Project project,
        IEnumerable<string> focusedIDs, IReadOnlyDictionary<string, ObservablePlan> plans,
        IReadOnlyList<ObservableResult> results, CatalogTreeNode[] baseRoots)
    {
        var focused = focusedIDs.Select(SourceFilePaths.Canonical).Distinct(PathComparer)
            .Select(id => plans.GetValueOrDefault(id))
            .Where(item => item != null).Cast<ObservablePlan>().ToArray();
        var matchingChildren = new List<CatalogTreeNode>();
        bool geometriesMatch = false, terrainsMatch = false, nvaluesMatch = false, boundariesMatch = false;
        if (focused.Length > 1)
        {
            geometriesMatch = MatchSourceAssociations(focused.Select(plan => plan.AssociatedGeometry));
            terrainsMatch = MatchSourceAssociations(focused.Select(plan => plan.AssociatedGeometry?.AssociatedTerrain));
            nvaluesMatch = MatchSourceAssociations(focused.Select(plan => plan.AssociatedGeometry?.AssociatedNValueLayer));
            boundariesMatch = MatchSourceAssociations(focused.Select(plan => plan.AssociatedBoundaryCondition));
            ObservablePlan first = focused[0];
            if (terrainsMatch && first.AssociatedGeometry?.AssociatedTerrain is { } commonTerrain)
                matchingChildren.Add(TerrainProjectNode(commonTerrain));
            if (geometriesMatch && first.AssociatedGeometry is { } commonGeometry)
                matchingChildren.Add(GeometryDependencyNode(commonGeometry));
            if (nvaluesMatch && first.AssociatedGeometry?.AssociatedNValueLayer is { } commonNValue)
                matchingChildren.Add(Node(commonNValue));
            if (boundariesMatch && first.AssociatedBoundaryCondition is { } commonBoundary)
                matchingChildren.Add(BoundaryConditionProjectNode(commonBoundary));
        }

        var focusedNodes = focused.Select(plan => FocusedPlanNode(project, plan, results,
            buildGeometry: !geometriesMatch, buildTerrain: !terrainsMatch,
            buildNValue: !nvaluesMatch, buildBoundaryCondition: !boundariesMatch)).ToArray();
        return [
            Group("FocusedPlansSectionContext", "Focused Plans", focusedNodes),
            Group("MatchingPlanLayersSectionContext", "Matching Layers", matchingChildren.ToArray()),
            ..baseRoots
        ];
    }

    private static CatalogTreeNode[] GeometryModeRoots(IEnumerable<string> focusedIDs,
        IReadOnlyDictionary<string, ObservableGeometry> geometries,
        CatalogTreeNode[] baseRoots)
    {
        var focused = focusedIDs.Select(SourceFilePaths.Canonical).Distinct(PathComparer)
            .Select(id => geometries.GetValueOrDefault(id))
            .Where(item => item != null).Cast<ObservableGeometry>().ToArray();
        var matchingChildren = new List<CatalogTreeNode>();
        bool terrainsMatch = false, nvaluesMatch = false, classificationsMatch = false;
        if (focused.Length > 1)
        {
            terrainsMatch = MatchSourceAssociations(focused.Select(item => item.AssociatedTerrain));
            nvaluesMatch = MatchSourceAssociations(focused.Select(item => item.AssociatedNValueLayer));
            classificationsMatch = MatchSourceAssociations(focused.Where(item => item.AssociatedNValueLayer != null)
                .Select(item => item.AssociatedNValueLayer?.ClassificationLayer));
            if (terrainsMatch && focused[0].AssociatedTerrain is { } terrain)
                matchingChildren.Add(WithAssociation(TerrainProjectNode(terrain), "Terrain"));
            if (nvaluesMatch && focused[0].AssociatedNValueLayer is { } nvalue)
                matchingChildren.Add(WithAssociation(Node(nvalue), "NValue"));
            if (classificationsMatch && focused[0].AssociatedNValueLayer?.ClassificationLayer is { } classification)
                matchingChildren.Add(WithAssociation(Node(classification), "Classification"));
        }
        var focusedNodes = focused.Select(item => FocusedGeometryNode(item, buildTerrain: !terrainsMatch,
            buildNValue: !nvaluesMatch, buildClassification: !classificationsMatch)).ToArray();
        return [
            Group("FocusedGeometriesSectionContext", "Focused Geometries", focusedNodes),
            Group("MatchingGeometryLayersSectionContext", "Matching Layers", matchingChildren.ToArray()),
            ..baseRoots
        ];
    }

    private static CatalogTreeNode FocusedPlanNode(Project project, ObservablePlan plan,
        IReadOnlyList<ObservableResult> results, bool buildGeometry, bool buildTerrain,
        bool buildNValue, bool buildBoundaryCondition)
    {
        var children = new List<CatalogTreeNode>();
        if (plan.AssociatedGeometry is { } geometry)
        {
            if (buildGeometry) children.Add(GeometryDependencyNode(geometry));
            if (geometry.AssociatedNValueLayer is { } nvalue)
            {
                if (buildNValue) children.Add(Node(nvalue));
                if (nvalue.ClassificationLayer is { } classification) children.Add(Node(classification));
            }
            if (buildTerrain && geometry.AssociatedTerrain is { } terrain) children.Add(TerrainProjectNode(terrain));
        }
        if (buildBoundaryCondition && plan.AssociatedBoundaryCondition is { } boundary)
            children.Add(BoundaryConditionProjectNode(boundary));
        var planResults = results.Where(result => result.Plan?.Name == plan.Name).ToArray();
        if (planResults.Length > 0)
            children.Add(new CatalogTreeNode(NodeID("multi-result", AssetPath(project, plan.Filename) ?? plan.Filename),
                "MultiResultNodeVM", plan.Name,
                Children: planResults.OrderBy(item => item.Name, StringComparer.CurrentCulture).Select(ResultNode).ToArray(),
                AssetID: AssetPath(project, plan.Filename)));
        return Node(plan, children.ToArray());
    }

    private static CatalogTreeNode GeometryDependencyNode(ObservableGeometry geometry) =>
        Node(geometry, GeometryComponents(geometry));

    private static CatalogTreeNode FocusedGeometryNode(ObservableGeometry geometry, bool buildTerrain,
        bool buildNValue, bool buildClassification)
    {
        var children = GeometryComponents(geometry).ToList();
        if (geometry.AssociatedNValueLayer is { } nvalue)
        {
            if (buildNValue) children.Add(WithAssociation(Node(nvalue), "NValue"));
            if (buildClassification && nvalue.ClassificationLayer is { } classification)
                children.Add(WithAssociation(Node(classification), "Classification"));
        }
        if (buildTerrain && geometry.AssociatedTerrain is { } terrain)
            children.Add(WithAssociation(TerrainProjectNode(terrain), "Terrain"));
        return Node(geometry, children.ToArray());
    }

    private static CatalogTreeNode WithAssociation(CatalogTreeNode node, string kind)
    {
        if (node.AssetID is not { Length: > 0 } assetID)
            throw new InvalidDataException("A source-associated map layer has no asset identity.");
        string nodeType = node.SourceClass switch
        {
            "ObservableGeometry" => "Geometry",
            "ObservableTerrain" => "Terrain",
            "ObservableNValueLayer" => "NValue",
            "ObservableClassificationLayer" => "Classification",
            "ObservableBoundaryCondition" => "BoundaryCondition",
            _ => throw new InvalidDataException("The associated source layer type is not supported.")
        };
        var locator = node.Locator ?? new CatalogTreeNodeLocator("", nodeType, node.Name, AssetID: assetID);
        return node with
        {
            Locator = locator with { AssociationAssetID = assetID, AssociationKind = kind }
        };
    }

    private static CatalogTreeNode[] ProjectResults(Project project, IReadOnlyList<ObservableResult> results,
        CoreLayerRequestContext? coreLayerContext)
    {
        var sessionResults = new List<(ObservableResult Observable, SessionHdfReferenceCapture Capture,
            string Path, string Revision)>();
        var sessionResultObjects = new HashSet<ObservableResult>(ReferenceEqualityComparer.Instance);
        foreach (ObservableResult observable in results)
        {
            Result result = observable.Result;
            if (result.Plan is null || string.IsNullOrWhiteSpace(result.Filename)) continue;
            string? path = AssetPath(project, result.Filename);
            if (string.IsNullOrWhiteSpace(path)) continue;
            (string? stablePath, string? revision) = TryReadStableRevision(path);
            if (stablePath is null || revision is null ||
                !SourceFilePaths.SameSourceFile(stablePath, path)) continue;

            SessionHdfReferenceCapture[] references = (coreLayerContext?.SessionHdfReferences ?? [])
                .Where(reference => reference.Kind == "result" &&
                    SourceFilePaths.SameSourceFile(reference.SourcePath, stablePath) &&
                    reference.SourceRevision.Equals(revision, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(reference.Name, result.Name, StringComparison.Ordinal) &&
                    string.Equals(reference.ResultPlanName, result.Plan.Name, StringComparison.Ordinal))
                .ToArray();
            if (references.Length > 1) throw new SourceFileChangedException();
            if (references.Length != 1) continue;
            SessionHdfReferenceCapture capture = references[0];
            if (!capture.SessionOnly || capture.ProjectSessionID != coreLayerContext!.ProjectSessionID ||
                !SourceFilePaths.SameSourceFile(capture.ProjectPath, coreLayerContext.ProjectPath))
                throw new SourceFileChangedException();
            sessionResults.Add((observable, capture, stablePath, revision));
            sessionResultObjects.Add(observable);
        }

        var projectGrouped = results.Where(result => result.Plan != null &&
                !sessionResultObjects.Contains(result))
            .GroupBy(result => result.Plan.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                Plan[] plans = project.Plans.Where(item =>
                    string.Equals(item.Name, group.Key, StringComparison.Ordinal)).ToArray();
                string? planAsset = plans.Length == 1 ? AssetPath(project, plans[0].Filename) : null;
                return new CatalogTreeNode(NodeID("multi-result", planAsset ?? group.Key), "MultiResultNodeVM", group.Key,
                    Children: group.OrderBy(item => item.Name, StringComparer.CurrentCulture).Select(ResultNode).ToArray(),
                    AssetID: planAsset);
            });
        var unmatched = results.Where(result => result.Plan == null).Select(ResultNode).ToArray();
        var sessionGrouped = sessionResults.GroupBy(item => item.Capture.ResultPlanName!, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.Path, PathComparer).ToArray();
                var selected = ordered[0];
                CoreLayerRequestContext context = coreLayerContext
                    ?? throw new SourceFileChangedException();
                var owner = new SessionHdfResultOwner(selected.Capture.ReferenceID,
                    selected.Capture.ProjectSessionID, selected.Capture.ProjectPath,
                    selected.Path, selected.Revision.ToUpperInvariant(), group.Key,
                    context.IdentityToken);
                return new CatalogTreeNode(NodeID("multi-result-session", group.Key),
                    "MultiResultNodeVM", group.Key,
                    Children: ordered.Select(item => ResultNode(item.Observable)).ToArray())
                {
                    SelectedResultAssetID = selected.Path,
                    SelectedResultRevision = selected.Revision,
                    SelectedSessionHdfResultOwner = owner
                };
            });
        return projectGrouped.Concat(sessionGrouped)
            .Concat(unmatched.Length == 0 ? [] : [new CatalogTreeNode("folder:unmatched-results", "FolderVM", "Unmatched Results", Children: unmatched)])
            .ToArray();
    }

    private static CatalogTreeNode GeometryProjectNode(ObservableGeometry geometry) => Node(geometry, GeometryComponents(geometry));

    private static CatalogTreeNode[] GeometryComponents(ObservableGeometry geometry)
    {
        var children = new List<CatalogTreeNode>
        {
            Component("ConceptualMeshLayerVisualContext", "Conceptual Mesh") with { LocalizeName = true },
            Component("FlowAreaLayerVisualContext", "Computational Mesh") with { LocalizeName = true }
        };
        if (geometry.CulvertBarrelLayer != null)
            children.Add(Component("CulvertBarrelLayerVisualContext", "Culvert Barrels") with { LocalizeName = true });
        return children.ToArray();
    }

    private static CatalogTreeNode TerrainProjectNode(ObservableTerrain terrain)
    {
        string? terrainPath = AssetPath(terrain.Layer.Project!, terrain.Layer.Filename);
        var inputs = terrain.VectorModTerrainInputs.Select((input, index) =>
            TerrainInputNode(terrainPath, input, index)).ToArray();
        return Node(terrain, inputs);
    }

    private static CatalogTreeNode TerrainInputNode(string? terrainPath, IObservableTerrainInput input, int inputIndex)
    {
        string sourceClass = TerrainInputSourceClass(input);
        CatalogTreeNode[]? children = input.GetType().Name == "ObservableChannelModTerrainInput"
            ? [Component("ChannelModTopologyLayerVisualContext", "Topology") with { LocalizeName = true }, Component("ChannelModTerrainInputVC", "Cross Sections") with { LocalizeName = true }]
            : null;
        if (terrainPath is null)
            return Component(sourceClass, input.Name, children);

        string id = TerrainInputNodeID(terrainPath, sourceClass, input.Name, inputIndex);
        bool canRender = sourceClass is "ChannelModTerrainInputParentVC" or "LeveeModTerrainInputVC"
            or "PolygonRasterModTerrainInputVisualContext";
        return new CatalogTreeNode(id, sourceClass, input.Name,
            Children: children is { Length: > 0 } ? children : null,
            AssetID: terrainPath,
            Checked: canRender ? "unchecked" : null,
            CanPin: false,
            Properties: [
                new CatalogTreeProperty("TerrainInput/SourceClass", "Source Class", sourceClass),
                new CatalogTreeProperty("TerrainInput/Index", "Source Index", inputIndex.ToString(CultureInfo.InvariantCulture))
            ]);
    }

    private static string TerrainInputSourceClass(IObservableTerrainInput input) => input.GetType().Name switch
    {
        "ObservableChannelModTerrainInput" => "ChannelModTerrainInputParentVC",
        "ObservableLeveeModTerrainInput" => "LeveeModTerrainInputVC",
        "ObservablePolygonRasterModTerrainInput" => "PolygonRasterModTerrainInputVisualContext",
        "ObservableEllipseRasterModificationLayer" => "EllipseRasterModTerrainInputVisualContext",
        _ => "TerrainInputNode"
    };

    internal static string TerrainInputNodeID(string terrainPath, string sourceClass, string inputName, int inputIndex)
    {
        string canonicalPath = SourceFilePaths.Canonical(terrainPath);
        string identity = string.Join("\n", canonicalPath, sourceClass, inputIndex.ToString(CultureInfo.InvariantCulture), inputName);
        return "terrain-input:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(identity)))[..24];
    }

    internal static TerrainInputMapContextData ReadTerrainInputMapContext(
        CatalogTreeRequest request, string inputNodeID, string expectedSourceClass, string expectedTerrainPath,
        TerrainInputMapRenderOptions? renderOptions = null)
    {
        using var project = new CatalogProjectContext(request with { Mode = "Project" });
        string canonicalTerrainPath = SourceFilePaths.Canonical(expectedTerrainPath);
        var terrainMatches = project.Observables.Terrains
            .Where(candidate => AssetPath(candidate.Layer.Project!, candidate.Layer.Filename) is { } path &&
                PathComparer.Equals(SourceFilePaths.Canonical(path), canonicalTerrainPath))
            .ToArray();
        if (terrainMatches.Length != 1) throw new SourceFileChangedException();
        ObservableTerrain terrain = terrainMatches[0];
        var inputMatches = terrain.VectorModTerrainInputs.Select((input, index) => (input, index))
            .Where(pair => string.Equals(TerrainInputSourceClass(pair.input), expectedSourceClass, StringComparison.Ordinal) &&
                string.Equals(TerrainInputNodeID(canonicalTerrainPath, expectedSourceClass, pair.input.Name, pair.index),
                    inputNodeID, StringComparison.Ordinal))
            .ToArray();
        if (inputMatches.Length != 1) throw new SourceFileChangedException();

        (string? stablePath, string? revision) = TryReadStableRevision(canonicalTerrainPath);
        if (stablePath is null || revision is null || !PathComparer.Equals(stablePath, canonicalTerrainPath))
            throw new SourceFileChangedException();

        Projection? sourceProjection = terrain.Layer.Project?.Projection;
        Projection? mapProjection = project.Project.Projection;
        if ((sourceProjection is null) != (mapProjection is null))
            throw new InvalidDataException("The terrain input and map projections do not share a valid transformation.");
        ReprojectionTransformer? transform = sourceProjection is not null && mapProjection is not null &&
            !sourceProjection.IsEqual(mapProjection)
                ? ReprojectionTransformer.GetOrMake(sourceProjection, mapProjection) : null;

        IObservableTerrainInput observable = inputMatches[0].input;
        TerrainInputMapContextFeature[] features = observable.TerrainInput switch
        {
            ChannelModTerrainInput channel => channel.Features.Select((feature, index) =>
                LineFeature(inputNodeID, index, feature.Line, transform)).Where(feature => feature is not null)
                .Cast<TerrainInputMapContextFeature>().ToArray(),
            LeveeModTerrainInput levee => levee.Features.Select((feature, index) =>
                LineFeature(inputNodeID, index, feature.Line, transform)).Where(feature => feature is not null)
                .Cast<TerrainInputMapContextFeature>().ToArray(),
            PolygonRasterModTerrainInput polygon => polygon.Features.Select((feature, index) =>
                PolygonFeature(inputNodeID, index, feature.Polygon, transform)).Where(feature => feature is not null)
                .Cast<TerrainInputMapContextFeature>().ToArray(),
            _ => throw new NotSupportedException("This terrain input does not expose a source map render layer.")
        };

        TerrainInputMapLeveeRendererData? leveeRenderer = null;
        if (renderOptions is { ShowProfileStations: true } or { ShowTin: true })
        {
            if (!double.IsFinite(renderOptions.RenderScale) || renderOptions.RenderScale <= 0 ||
                renderOptions.RenderScale > 64 || renderOptions.ShowTin != (renderOptions.TinOptions is not null))
                throw new ArgumentException("The Levee source-render options are invalid.");
            if (observable is not ObservableLeveeModTerrainInput leveeInput)
                throw new NotSupportedException("Only a Levee Mod terrain input exposes these map render options.");
            if (renderOptions.ShowTin && !renderOptions.DebugMode)
                throw new InvalidOperationException("The Levee TIN map layer is available only in debug mode.");

            Projection? viewportProjection = mapProjection;
            Projection? inputProjection = sourceProjection;
            ReprojectionTransformer? projectToSource = viewportProjection is not null && inputProjection is not null &&
                !viewportProjection.IsEqual(inputProjection)
                    ? ReprojectionTransformer.GetOrMake(viewportProjection, inputProjection) : null;

            MapSourceRasterFrame? profileStations = null;
            if (renderOptions.ShowProfileStations)
            {
                var baseline = new LeveeModRenderLayer(leveeInput.AsPolylineCollection(), leveeInput.Layer);
                var withStations = new LeveeModRenderLayer(leveeInput.AsPolylineCollection(), leveeInput.Layer)
                {
                    ShowProfileStations = true
                };
                profileStations = MapSourceRasterRendering.Difference(
                    baseline, withStations, renderOptions.Viewport, projectToSource,
                    renderOptions.RenderScale);
            }

            MapSourceRasterFrame? tinFrame = null;
            int tinFeatureCount = 0;
            string? tinWarning = null;
            TerrainInputMapTinPlotOptionDescriptor[] tinPlotOptions = [];
            if (renderOptions.ShowTin)
            {
                var tins = new List<Tin>();
                foreach (ObservableLeveeMod feature in leveeInput.Features)
                {
                    LeveeMod baseFeature = feature.BaseFeature;
                    if (baseFeature.TryEvaluateAssociations(out var properties) &&
                        LeveeResampler.TryBuildTin(baseFeature, properties, out Tin tin))
                        tins.Add(tin);
                }
                tinFeatureCount = tins.Count;
                if (tins.Count > 0)
                {
                    var tinRenderer = new MultiTinRenderLayer(tins);
                    tinPlotOptions = ApplyTinPlotOptions(tinRenderer,
                        renderOptions.TinOptions ?? throw new ArgumentException("The Levee TIN options are missing."));
                    tinFrame = MapSourceRasterRendering.Render(tinRenderer, renderOptions.Viewport,
                        projectToSource, renderOptions.RenderScale);
                }
                else
                    tinWarning = "No simple cut/fill feature produced a TIN. Check each feature's station/elevation profile and its property association.";
            }

            leveeRenderer = new TerrainInputMapLeveeRendererData(renderOptions.Viewport,
                profileStations, tinFrame, tinFeatureCount, tinWarning, tinPlotOptions);
        }

        (string? currentPath, string? currentRevision) = TryReadStableRevision(canonicalTerrainPath);
        if (!PathComparer.Equals(currentPath, stablePath) || !string.Equals(currentRevision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return new TerrainInputMapContextData(inputNodeID, inputMatches[0].index, canonicalTerrainPath, revision,
            expectedSourceClass, features, leveeRenderer);
    }

    /// <summary>Converts coordinates between one exact terrain input CRS and its owning map CRS.</summary>
    internal static TerrainMapCoordinate[] TransformTerrainInputCoordinates(CatalogTreeRequest request,
        string inputNodeID, string expectedSourceClass, string expectedTerrainPath, int inputIndex,
        TerrainMapCoordinate[] coordinates, bool projectToSource)
    {
        if (coordinates.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("The terrain polygon contains invalid map coordinates.");
        using var project = new CatalogProjectContext(request with { Mode = "Project" });
        string canonicalTerrainPath = SourceFilePaths.Canonical(expectedTerrainPath);
        var terrainMatches = project.Observables.Terrains
            .Where(candidate => AssetPath(candidate.Layer.Project!, candidate.Layer.Filename) is { } path &&
                PathComparer.Equals(SourceFilePaths.Canonical(path), canonicalTerrainPath))
            .ToArray();
        if (terrainMatches.Length != 1) throw new SourceFileChangedException();
        ObservableTerrain terrain = terrainMatches[0];
        var vectorInputs = terrain.VectorModTerrainInputs;
        if (inputIndex < 0 || inputIndex >= vectorInputs.Count) throw new SourceFileChangedException();
        IObservableTerrainInput input = vectorInputs[inputIndex];
        if (!string.Equals(TerrainInputSourceClass(input), expectedSourceClass, StringComparison.Ordinal) ||
            !string.Equals(TerrainInputNodeID(canonicalTerrainPath, expectedSourceClass, input.Name, inputIndex),
                inputNodeID, StringComparison.Ordinal) || input.TerrainInput is not PolygonRasterModTerrainInput)
            throw new SourceFileChangedException();

        Projection? sourceProjection = terrain.Layer.Project?.Projection;
        Projection? mapProjection = project.Project.Projection;
        return TransformTerrainCoordinates(coordinates, sourceProjection, mapProjection, projectToSource);
    }

    /// <summary>Transforms terrain polygon coordinates using the real source CRS pair.</summary>
    internal static TerrainMapCoordinate[] TransformTerrainCoordinates(TerrainMapCoordinate[] coordinates,
        Projection? sourceProjection, Projection? mapProjection, bool projectToSource)
    {
        if (coordinates.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("The terrain polygon contains invalid map coordinates.");
        if ((sourceProjection is null) != (mapProjection is null))
            throw new InvalidDataException("The terrain input and map projections do not share a valid transformation.");
        if (coordinates.Length == 0 || sourceProjection is null || mapProjection is null ||
            sourceProjection.IsEqual(mapProjection)) return coordinates.ToArray();

        // ReprojectionTransformer follows the existing ISpatialTransformer
        // contract, whose GDAL adapter can leave the input buffer unchanged
        // after OSR reports a failed point transformation. Validate the exact
        // direction and points through OSR first so an unsupported transform
        // cannot be mistaken for valid identity coordinates.
        Projection fromProjection = projectToSource ? mapProjection : sourceProjection;
        Projection toProjection = projectToSource ? sourceProjection : mapProjection;
        double[] checkedX = coordinates.Select(point => point.X).ToArray();
        double[] checkedY = coordinates.Select(point => point.Y).ToArray();
        using var fromReference = new SpatialReference(fromProjection.GetWKT());
        using var toReference = new SpatialReference(toProjection.GetWKT());
        fromReference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        toReference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using (CoordinateTransformation nativeTransform = new(fromReference, toReference))
        {
            try { nativeTransform.TransformPoints(checkedX.Length, checkedX, checkedY, null); }
            catch (ApplicationException error)
            {
                throw new InvalidDataException(
                    "The terrain polygon could not be transformed between the source and map projections.", error);
            }
        }
        if (checkedX.Any(value => !double.IsFinite(value)) || checkedY.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("The terrain polygon could not be transformed between the source and map projections.");

        ReprojectionTransformer transform = ReprojectionTransformer.GetOrMake(sourceProjection, mapProjection);
        Point[] source = coordinates.Select(point => new Point(point.X, point.Y)).ToArray();
        Point[] transformed = new Point[source.Length];
        if (projectToSource) transform.ReverseTransform(source.AsMemory(), transformed.AsMemory());
        else transform.Transform(source.AsMemory(), transformed.AsMemory());
        if (transformed.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("The terrain polygon could not be transformed between the source and map projections.");
        return transformed.Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray();
    }

    private static TerrainInputMapTinPlotOptionDescriptor[] ApplyTinPlotOptions(
        MultiTinRenderLayer renderer, TerrainInputMapTinRenderOptions values)
    {
        if (values.SurfaceFillMode is not ("none" or "z" or "m") || values.LabelEdges || values.LabelPoints)
            throw new ArgumentException("The Levee TIN plot options are unsupported.");
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options = renderer.GetPlotOptions()
            .ToDictionary(pair => pair.Key.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);

        // PaintZ and PaintM both write SurfaceFillMode. Clear the other alias
        // first, then set the selected one so the source setter cannot leave
        // the requested fill mode disabled.
        if (values.SurfaceFillMode == "z")
        {
            SetBoolean("PaintM", false);
            SetBoolean("PaintZ", true);
        }
        else if (values.SurfaceFillMode == "m")
        {
            SetBoolean("PaintZ", false);
            SetBoolean("PaintM", true);
        }
        else
        {
            SetBoolean("PaintZ", false);
            SetBoolean("PaintM", false);
        }
        SetBoolean("RenderEdges", values.RenderEdges);
        SetBoolean("RenderPoints", values.RenderPoints);
        SetBoolean("RenderDeletedTris", values.RenderDeletedTriangles);
        SetBoolean("LabelTriangles", values.LabelTriangles);

        return options.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            string propertyName = pair.Value.Property.Name;
            bool implemented = propertyName is not ("LabelEdges" or "LabelPoints");
            string value = pair.Value.Property.GetValue(renderer) is bool boolean
                ? boolean ? "true" : "false"
                : throw new InvalidDataException("The MultiTin renderer exposed a non-Boolean PlotOption.");
            string description = pair.Value.Option.Description ?? string.Empty;
            if (!implemented)
                description = string.IsNullOrEmpty(description) ?
                    "The retained MultiTin renderer does not implement this label drawing option." :
                    description + "\nThe retained MultiTin renderer does not implement this label drawing option.";
            return new TerrainInputMapTinPlotOptionDescriptor(propertyName,
                pair.Value.Option.Name, pair.Value.Option.Label, description, value,
                implemented, implemented);
        }).ToArray();

        void SetBoolean(string name, bool value)
        {
            if (!options.TryGetValue(name, out var option) || option.Property.PropertyType != typeof(bool))
                throw new InvalidOperationException("The retained MultiTin PlotOption contract changed.");
            option.Property.SetValue(renderer, value);
        }
    }

    private static TerrainInputMapContextFeature? LineFeature(string inputNodeID, int index,
        Geospatial.Vectors.Polyline? line, ReprojectionTransformer? transform)
    {
        if (line is null || line.Count < 2) return null;
        Geospatial.Vectors.Point[] points = line.GetPointBufferUnsafe().Span.ToArray();
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
        transform?.Transform(points.AsSpan(), points.AsSpan());
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
        return new TerrainInputMapContextFeature(inputNodeID + ":feature:" + index.ToString(CultureInfo.InvariantCulture),
            "line", points.Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray(), false);
    }

    private static TerrainInputMapContextFeature? PolygonFeature(string inputNodeID, int index,
        Geospatial.Vectors.Polygon? polygon, ReprojectionTransformer? transform)
    {
        if (polygon is null || polygon.Count < 3) return null;
        Geospatial.Vectors.Point[] points = polygon.GetPointBufferUnsafe().Span.ToArray();
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
        transform?.Transform(points.AsSpan(), points.AsSpan());
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
        return new TerrainInputMapContextFeature(inputNodeID + ":feature:" + index.ToString(CultureInfo.InvariantCulture),
            "polygon", points.Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray(), true);
    }

    private static CatalogTreeNode BoundaryConditionProjectNode(ObservableBoundaryCondition condition)
    {
        var children = new List<CatalogTreeNode>
        {
            Component("BoundaryConditionLineLayerVC", "Boundary Condition Lines") with { LocalizeName = true },
            Component("PrecipitationLayerVisualContext", "Precipitation") with { LocalizeName = true }
        };
        // Wind is a separate source context below the exact Boundary owner.
        // Result-owned BoundaryCondition rows cannot supply the project
        // WindLayer source used by the retained map sampler.
        if (!condition.IsResult)
            children.Add(Component("WindLayerVisualContext", "Wind Forces") with { LocalizeName = true });
        return Node(condition, children.ToArray());
    }

    private static CatalogTreeNode GeometryCatalogNode(ObservableGeometry geometry,
        CatalogDiagnosticsService.Context diagnostics)
    {
        string parentPath = Path.GetFullPath(geometry.Filename);
        (string? parentCanonicalPath, string? parentRevision) = diagnostics.ReadStableRevision(parentPath);
        (string? projectCanonicalPath, string? projectRevision) = TryReadStableRevision(geometry.Project.Filename);
        var backups = geometry.BackupFiles.Select(filename =>
        {
            string backupPath = Path.GetFullPath(filename);
            (string? canonicalPath, string? revision) = TryReadStableRevision(backupPath);
            bool canRestore = canonicalPath != null && revision != null &&
                parentCanonicalPath != null && parentRevision != null &&
                projectCanonicalPath != null && projectRevision != null;
            return new CatalogTreeNode(
                NodeID("backup", (parentCanonicalPath ?? parentPath) + "/" + (canonicalPath ?? backupPath)),
                "BackupNodeVM", Path.GetFileName(backupPath),
                AssetID: canonicalPath,
                BackupSummary: new CatalogTreeBackupSummary(backupPath, geometry.Geometry.DateCreated ?? "",
                    geometry.Geometry.DateLastModified ?? "", canRestore, revision,
                    parentCanonicalPath, parentRevision, projectRevision));
        }).ToArray();
        CatalogTreeNode[]? children = backups.Length == 0 ? null : [Group("BackupRootNodeVM", "Backups", backups)];
        return Node(geometry, children);
    }

    internal static (string? CanonicalPath, string? Revision) TryReadStableRevision(string path)
    {
        try
        {
            string canonicalBefore = SourceFilePaths.Canonical(path);
            var before = new FileInfo(path);
            long lengthBefore = before.Length;
            DateTime writeBefore = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            before.Refresh();
            string canonicalAfter = SourceFilePaths.Canonical(path);
            if (!string.Equals(canonicalBefore, canonicalAfter, StringComparison.Ordinal) ||
                before.Length != lengthBefore || before.LastWriteTimeUtc != writeBefore)
                return (null, null);
            return (canonicalBefore, revision);
        }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
        catch (ArgumentException) { return (null, null); }
    }

    private static CatalogTreeNode ResultNode(ObservableResult observable)
    {
        Result result = observable.Result;
        Project project = result.Project;
        string? assetPath = AssetPath(project, result.Filename);
        (string? computeSourcePath, string? computeSourceRevision) = assetPath is null
            ? (null, null) : TryReadStableRevision(assetPath);
        Ras.Layers.Results.ComputeSummary? computeSummary = null;
        if (computeSourcePath is not null && computeSourceRevision is not null)
        {
            // The retained metadata reader instantiates default nested objects
            // even when their HDF group is absent. Only an actually stored
            // summary proves that this Result contains a completed run record.
            try
            {
                using (var reader = new H5Reader(computeSourcePath))
                    if (reader.ExistsAsGroup("/Results/" + Result.ComputeSummaryGroupName))
                        computeSummary = result.ComputeSummary;
                (string? currentPath, string? currentRevision) = TryReadStableRevision(assetPath!);
                if (!PathComparer.Equals(currentPath, computeSourcePath) ||
                    !string.Equals(currentRevision, computeSourceRevision, StringComparison.Ordinal))
                    (computeSourcePath, computeSourceRevision) = (null, null);
            }
            catch (Exception)
            {
                // Existing source diagnostics separately retain read failures.
                // An unreadable source cannot establish a compute state/SHA.
                (computeSourcePath, computeSourceRevision) = (null, null);
            }
        }
        bool computeSourceStable = computeSourcePath is not null && computeSourceRevision is not null;
        string? computeState = !computeSourceStable || computeSummary is null
            ? null : computeSummary.Success ? "completed" : "failed";
        string? computeFailureReason = !computeSourceStable || computeSummary?.Success != false
            ? null : observable.FriendlyFailureReason;
        string id = NodeID("asset", assetPath ?? result.Filename);
        var children = new List<CatalogTreeNode>();
        if (result.Plan is not null)
            children.Add(PlanCatalogNode(observable.Plan));
        if (result.Geometry is not null)
            children.Add(FocusedGeometryNode(observable.Geometry, buildTerrain: true,
                buildNValue: true, buildClassification: true));
        if (result.BoundaryConditions is not null)
            children.Add(BoundaryConditionProjectNode(observable.BoundaryCondition));
        return new CatalogTreeNode(id, "ObservableResult", result.Name,
            Children: children.Count == 0 ? null : children.ToArray(), AssetID: assetPath,
            CatalogSummary: new CatalogTreeSummary(result.Name, assetPath), Properties: [
                new CatalogTreeProperty("Name", "Name", result.Name),
                new CatalogTreeProperty("Filename", "Filename", assetPath ?? result.Filename)
            ], ResultComputeState: computeState, ResultComputeFailureReason: computeFailureReason,
            ResultComputeSourcePath: computeSourcePath, ResultComputeSourceRevision: computeSourceRevision)
        {
            ResultEndTime = computeSourceStable ? computeSummary?.EndTime : null,
            ResultPlanName = result.Plan?.Name
        };
    }

    private static CatalogTreeNode Node(ObservableCoreLayer observable, CatalogTreeNode[]? children = null)
    {
        return Node(observable.Layer, children, observable.GetType().Name);
    }

    private static CatalogTreeNode Node(CoreLayerBase layer, CatalogTreeNode[]? children = null,
        string? sourceClass = null)
    {
        string? assetPath = AssetPath(layer.Project!, layer.Filename);
        string id = NodeID("asset", assetPath ?? layer.Filename);
        var summary = new CatalogTreeSummary(layer.Name, assetPath, layer.DateCreated,
            layer.DateLastModified, layer.Description);
        var properties = new[]
        {
            new CatalogTreeProperty("Name", "Name", layer.Name),
            new CatalogTreeProperty("Filename", "Filename", assetPath ?? layer.Filename),
            new CatalogTreeProperty("DateCreated", "Date Created", layer.DateCreated ?? ""),
            new CatalogTreeProperty("DateLastModified", "Date Last Modified", layer.DateLastModified ?? ""),
            new CatalogTreeProperty("Description", "Description", layer.Description ?? "")
        };
        return new CatalogTreeNode(id, sourceClass ?? layer.GetType().Name, layer.Name,
            Children: children is { Length: > 0 } ? children : null, AssetID: assetPath,
            CatalogSummary: summary, Properties: properties);
    }

    private static CatalogTreeNode Group(string sourceClass, string name, CatalogTreeNode[] children) =>
        new("group:" + sourceClass, sourceClass, name, true, children.Length == 0 ? null : children);

    private static CatalogTreeNode Component(string sourceClass, string name, CatalogTreeNode[]? children = null) =>
        new("component:" + sourceClass + ":" + name, sourceClass, name,
            Children: children is { Length: > 0 } ? children : null);

    private static string NodeID(string type, string value) => type + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..24];
    // RasTreeBase.TryUpdateMatch ignores null associations and marks a group
    // matched when all non-null values refer to the same source model.
    private static bool MatchSourceAssociations<T>(IEnumerable<T?> values) where T : class
    {
        T? first = null;
        bool matches = true;
        foreach (T? value in values)
        {
            if (value == null) continue;
            if (matches && first != null && !ReferenceEquals(first, value)) matches = false;
            else if (first == null) first = value;
        }
        return matches;
    }

    private static string? AssetPath(Project project, string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return null;
        string candidate = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return Path.GetFullPath(candidate);
    }

    private static string FullProjectPath(Project project) => Path.GetFullPath(project.Filename);

    private sealed class CatalogProjectContext : IDisposable
    {
        public Project Project { get; }
        public ObservableProject Observables { get; }
        public CatalogDiagnosticsService.Context Diagnostics { get; }

        public CatalogProjectContext(CatalogTreeRequest request, string[]? protectedReadPaths = null)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath)) throw new ArgumentException("The project path is required.");
            string path = Path.GetFullPath(request.ProjectPath);
            if (!File.Exists(path)) throw new FileNotFoundException("The project file was not found.", path);
            HashSet<string> protectedSources = (protectedReadPaths ?? [])
                .Select(SourceFilePaths.Canonical)
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Diagnostics = new CatalogDiagnosticsService.Context(path, protectedSources);
            if (request.CoreLayerContext is not null)
            {
                if (request.ProjectSessionID is not null &&
                    !string.Equals(request.ProjectSessionID, request.CoreLayerContext.ProjectSessionID,
                        StringComparison.Ordinal)) throw new SourceFileChangedException();
                if (request.ExternalCoreLayerReferences is { Length: > 0 } legacyReferences &&
                    !legacyReferences.SequenceEqual(request.CoreLayerContext.References))
                    throw new SourceFileChangedException();
            }
            else if (request.ExternalCoreLayerReferences is { Length: > 0 })
            {
                throw new SourceFileChangedException();
            }
            Project = CoreLayerImportService.OpenProject(path, request.ProjectXml,
                request.CoreLayerContext,
                sourcePath => !protectedSources.Contains(SourceFilePaths.Canonical(sourcePath)));
            try
            {
                Diagnostics.BindProject(Project);
                Observables = new ObservableProject(Project, Utility.SynchronizationContext.None);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (CoreLayerBase layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries)
                .Concat(Project.Terrains).Concat(Project.BoundaryConditions).Concat(Project.NValues)
                .Concat(Project.ClassificationLayers)) layer.Dispose();
            foreach (Result result in Project.Results) result.Dispose();
        }
    }
}
