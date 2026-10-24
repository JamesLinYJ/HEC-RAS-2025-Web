using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Geospatial.Features;
using Geospatial.IO;
using Geospatial.IO.Vectors;
using Ras.Layers;
using Utility.Logging;
using Utility.Files;

namespace Ras.Backend;

public sealed record MapDocumentRequest(
    string ProjectPath,
    string? ProjectXml,
    string? ProjectXmlSHA,
    string? ExpectedVisualizationRevision,
    string Kind,
    string? Name = null,
    string? MapID = null,
    int? MapIndex = null,
    string? MapName = null,
    string? MapType = null,
    string? NewName = null,
    string? InitialGeometryPath = null,
    string? InitialGeometryRevision = null,
    string? InitialPlanPath = null,
    string? InitialPlanRevision = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record MapDocumentMutationReceipt(
    int MapIndex,
    string MapName,
    string MapType,
    string? PreviousVisualizationRevision,
    string VisualizationRevision);

public sealed record MapDocumentIndexChange(
    int PreviousMapIndex,
    int MapIndex,
    string MapName,
    string MapType);

public sealed record MapDocumentDeleteReceipt(
    int RemovedMapIndex,
    string RemovedMapName,
    string RemovedMapType,
    string? PreviousVisualizationRevision,
    string VisualizationRevision,
    MapDocumentIndexChange[] MapIndexChanges);

public sealed record MapVectorImportRequest(
    MapDocumentRequest Map,
    string SourcePath,
    string SourceRevision,
    string ExpectedProjectRevision,
    string ExpectedSourceCanonicalPath);

public sealed record MapTreeRequest(
    string ProjectPath,
    string? ProjectXml,
    string? ProjectXmlSHA,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string? ExpectedVisualizationRevision,
    MapTreeResultSelectionOverride[]? ResultSelectionOverrides = null,
    MapResultComputeReadLease? ComputeReadLease = null,
    string? ProjectSessionID = null,
    CoreLayerReferenceCapture[]? ExternalCoreLayerReferences = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record MapTreeResultSelectionOverride(
    string PlanAssetID,
    string PlanName,
    string SelectedResultPath,
    string SelectedResultRevision,
    SessionHdfResultOwner? SessionHdfResultOwner = null,
    bool FollowLatest = false);

public sealed class SessionHdfMapOwnershipConflictException : Exception
{
    public SessionHdfMapOwnershipConflictException(string message) : base(message) { }
}

public sealed record MapTreeActionRequest(
    MapTreeRequest Map,
    string Action,
    CatalogTreeNodeLocator? Node = null,
    CatalogTreeNodeLocator?[]? Nodes = null,
    CatalogTreeNodeLocator? Target = null,
    string? Name = null,
    bool? Checked = null,
    string? SourcePath = null,
    string? SourceRevision = null,
    int? ProfileIndex = null,
    int? Opacity = null);

public sealed record MapTreeSnapshot(
    CatalogTreeNode[] Roots,
    string? PreviousVisualizationRevision,
    string VisualizationRevision,
    string[] ExpandedNodeIDs,
    string? SelectedNodeID,
    string[] HiddenLayerIDs);

public sealed record MapTreeMutationResult(
    CatalogTreeNode[] Roots,
    string? PreviousVisualizationRevision,
    string VisualizationRevision,
    string[] ExpandedNodeIDs,
    string? SelectedNodeID,
    string[] HiddenLayerIDs);

/// <summary>
/// Persists the source RasMap identity and RasTreeBase sparse tree state in the
/// current project's .vis file. Project XML is consumed as a read-only draft;
/// commits replace only the .vis document and preserve unknown XML nodes.
/// </summary>
public static partial class MapTreeService
{
    private const long MaximumVisualizationBytes = 32L * 1024 * 1024;
    private const long MaximumProjectXmlBytes = 8L * 1024 * 1024;
    private static readonly HashSet<string> CheckableNodeTypes = new(StringComparer.Ordinal)
    {
        "Geometry", "Result", "MultiResult", "Terrain", "BoundaryCondition", "WindLayer", "PrecipitationLayer",
        "NValue", "Classification", "Feature", "MapLayer", "GdbDataset", "GdbVectorLayer", "ConceptualMesh",
        "FlowAreaLayer", "CulvertBarrelLayer"
    };
    private static readonly HashSet<string> TransientTerrainInputSourceClasses = new(StringComparer.Ordinal)
    {
        "ChannelModTerrainInputParentVC", "LeveeModTerrainInputVC",
        "PolygonRasterModTerrainInputVisualContext"
    };
    private static readonly HashSet<string> GeometryMapChildTypes = new(StringComparer.Ordinal)
    {
        "ConceptualMesh", "FlowAreaLayer", "CulvertBarrelLayer"
    };
    private static readonly HashSet<string> MovableNodeTypes = new(StringComparer.Ordinal)
    {
        "Geometry", "Plan", "Result", "MultiResult", "Terrain", "BoundaryCondition",
        "NValue", "Classification", "Feature", "MapLayer", "Folder"
    };
    private static readonly HashSet<string> SupportedMapLayerTypes = new(StringComparer.Ordinal)
    {
        "DiscreteRasterMapLayer", "ImageRasterMapLayer", "SingleBandRasterMapLayer",
        "PointsVector", "PolylinesVector", "PolygonsVector", "GdbDataset", "GdbVectorLayer"
    };
    private static readonly HashSet<string> SupportedGeoJsonVectorTypes = new(StringComparer.Ordinal)
    {
        "PointsVector", "PolylinesVector", "PolygonsVector"
    };

    public static MapDocumentMutationReceipt CreateMap(MapDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("The requested map type is not supported by the source map workflow.");
        bool hasInitialGeometryPath = !string.IsNullOrWhiteSpace(request.InitialGeometryPath);
        bool hasInitialGeometryRevision = !string.IsNullOrWhiteSpace(request.InitialGeometryRevision);
        if (hasInitialGeometryPath != hasInitialGeometryRevision ||
            (hasInitialGeometryPath && request.Kind != "Geometry"))
            throw new ArgumentException("An initial Geometry selection requires both its source path and revision and is available only for Geometry maps.");
        bool hasInitialPlanPath = !string.IsNullOrWhiteSpace(request.InitialPlanPath);
        bool hasInitialPlanRevision = !string.IsNullOrWhiteSpace(request.InitialPlanRevision);
        if (hasInitialPlanPath != hasInitialPlanRevision ||
            (hasInitialPlanPath && request.Kind != "Plan"))
            throw new ArgumentException("An initial Plan selection requires both its source path and revision and is available only for Plan maps.");
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: true,
            projectSessionID: request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        XElement maps = EnsureMaps(context.Visualization.Root!);
        var currentMaps = maps.Elements("Map").ToArray();
        string name = request.Name is null ? NextMapName(currentMaps) : ValidateName(request.Name, "map");
        if (currentMaps.Any(map => string.Equals((string?)map.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A map with that name already exists.");

        int mapIndex = currentMaps.Length;
        var map = new XElement("Map", new XAttribute("Name", name), new XAttribute("Type", request.Kind));
        if (request.Kind == "Geometry")
        {
            GeometryMapRootElements roots = EnsureGeometryMapRoots(map);
            if (hasInitialGeometryPath)
            {
                ProjectGeometryIdentity geometry = ResolveProjectGeometry(context,
                    request.InitialGeometryPath, request.InitialGeometryRevision);
                CatalogTreeNode defaultNode = ProjectGeometryDefault(context, "Geometry", geometry);
                roots.Focused!.Add(SerializeSourceDefault(defaultNode, existing: null, associationStates: null));
                _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
            }
        }
        else if (request.Kind == "Plan" && hasInitialPlanPath)
        {
            ProjectPlanIdentity plan = ResolveProjectPlan(context, request.InitialPlanPath, request.InitialPlanRevision);
            PlanMapRootElements roots = EnsurePlanMapRoots(map);
            CatalogTreeNode defaultNode = ProjectPlanDefault(context, plan);
            roots.Focused!.Add(SerializeSourceDefault(defaultNode, existing: null, associationStates: null));
            _ = ResolveProjectPlan(context, plan.AssetPath, plan.Revision);
        }
        maps.Add(map);
        maps.SetAttributeValue("Maps", name);
        string revision = Commit(context);
        return new(mapIndex, name, request.Kind, context.PreviousVisualizationRevision, revision);
    }

    public static MapDocumentMutationReceipt RenameMap(MapDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MapIndex is not int mapIndex || request.MapName is null || request.MapType is null)
            throw new ArgumentException("The saved map identity is incomplete.");
        string name = ValidateName(request.NewName ?? "", "map");
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        XElement map = ResolveMap(context.Visualization, mapIndex, request.MapName, request.MapType);
        XElement[] maps = context.Visualization.Root!.Element("Maps")!.Elements("Map").ToArray();
        string previousName = request.MapName;
        if (string.Equals(previousName, name, StringComparison.Ordinal))
            return new(mapIndex, previousName, request.MapType, context.PreviousVisualizationRevision,
                context.PreviousVisualizationRevision!);
        if (maps.Where((_, index) => index != mapIndex).Any(item =>
            string.Equals((string?)item.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A map with that name already exists.");
        bool previousNameIsUnique = maps.Count(item => string.Equals((string?)item.Attribute("Name"), previousName,
            StringComparison.Ordinal)) == 1;
        map.SetAttributeValue("Name", name);
        XElement mapList = context.Visualization.Root!.Element("Maps")!;
        if (previousNameIsUnique && string.Equals((string?)mapList.Attribute("Maps"), previousName, StringComparison.Ordinal))
            mapList.SetAttributeValue("Maps", name);
        string revision = Commit(context);
        return new(mapIndex, name, request.MapType, context.PreviousVisualizationRevision, revision);
    }

    public static MapDocumentDeleteReceipt DeleteMap(MapDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MapIndex is not int mapIndex || request.MapName is null || request.MapType is null)
            throw new ArgumentException("The saved map identity is incomplete.");
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        XElement mapsElement = context.Visualization.Root!.Element("Maps") ?? throw new SourceFileChangedException();
        XElement[] maps = mapsElement.Elements("Map").ToArray();
        XElement map = ResolveMap(context.Visualization, mapIndex, request.MapName, request.MapType);
        map.Remove();

        XElement[] remainingMaps = mapsElement.Elements("Map").ToArray();
        string? activeMapName = (string?)mapsElement.Attribute("Maps");
        if (activeMapName == request.MapName &&
            !remainingMaps.Any(item => string.Equals((string?)item.Attribute("Name"), activeMapName, StringComparison.Ordinal)))
        {
            if (remainingMaps.Length == 0) mapsElement.Attribute("Maps")?.Remove();
            else mapsElement.SetAttributeValue("Maps", (string?)remainingMaps[0].Attribute("Name"));
        }

        string revision = Commit(context);
        var remap = remainingMaps.Select((item, index) => new MapDocumentIndexChange(
            PreviousMapIndex: index < mapIndex ? index : index + 1,
            MapIndex: index,
            MapName: (string?)item.Attribute("Name") ?? "",
            MapType: (string?)item.Attribute("Type") ?? "")).ToArray();
        return new(mapIndex, request.MapName, request.MapType, context.PreviousVisualizationRevision,
            revision, remap);
    }

    public static MapDocumentMutationReceipt ImportGeoJSONVector(MapVectorImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        MapDocumentRequest owner = request.Map;
        using var context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null,
            projectSessionID: owner.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: owner.CoreLayerContext?.References,
            coreLayerContext: owner.CoreLayerContext);
        if (!context.ProjectRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        VerifyImportSourceIdentity(request);
        XElement map;
        int index;
        if (owner.MapIndex is int savedIndex)
        {
            map = ResolveMap(context.Visualization, savedIndex,
                owner.MapName ?? throw new ArgumentException("The saved map name is missing."),
                owner.MapType ?? throw new ArgumentException("The saved map type is missing."));
            index = savedIndex;
        }
        else
        {
            // A new project's first layer and first map are published together.
            // Invalid/empty input must not leave behind an empty saved map.
            XElement maps = EnsureMaps(context.Visualization.Root!);
            if (owner.Kind != "Project" || maps.Elements("Map").Any())
                throw new SourceFileChangedException();
            map = new XElement("Map", new XAttribute("Name", NextMapName([])), new XAttribute("Type", "Project"));
            maps.Add(map);
            maps.SetAttributeValue("Maps", (string?)map.Attribute("Name"));
            index = 0;
        }
        AddGeoJSONVector(context, map, request.SourcePath, request.SourceRevision);
        // Recheck the exact input after the retained reader has consumed it.
        _ = ResolveGeoJSONSource(context, request.SourcePath, request.SourceRevision);
        VerifyImportSourceIdentity(request);
        string revision = Commit(context);
        return new(index, (string)map.Attribute("Name")!, (string)map.Attribute("Type")!,
            context.PreviousVisualizationRevision, revision);
    }

    /// <summary>Captures the exact four-file identity used by a Shapefile map layer.</summary>
    public static MapSourceFamilyCapture CaptureShapefileFamily(string sourcePath) =>
        MapShapefileImportService.CaptureFamily(sourcePath);

    /// <summary>
    /// Reads one selected Shapefile with its retained typed reader and persists
    /// a checked MapLayer row in the same saved map as GeoJSON import.
    /// </summary>
    public static MapDocumentMutationReceipt ImportShapefileVector(MapShapefileVectorImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.ExpectedFamily);
        MapDocumentRequest owner = request.Map;
        using var context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null,
            projectSessionID: owner.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: owner.CoreLayerContext?.References,
            coreLayerContext: owner.CoreLayerContext);
        if (!context.ProjectRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string sourcePath = ResolveShapefileSource(context, request.SourcePath, request.ExpectedFamily);
        MapShapefileSourceReadResult source = MapShapefileImportService.Read(sourcePath);
        // Recheck all four physical identities after the actual typed source
        // reader has consumed geometry, dimensions and DBF attributes.
        MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);

        XElement map;
        int index;
        if (owner.MapIndex is int savedIndex)
        {
            map = ResolveMap(context.Visualization, savedIndex,
                owner.MapName ?? throw new ArgumentException("The saved map name is missing."),
                owner.MapType ?? throw new ArgumentException("The saved map type is missing."));
            index = savedIndex;
        }
        else
        {
            XElement maps = EnsureMaps(context.Visualization.Root!);
            if (owner.Kind != "Project" || maps.Elements("Map").Any())
                throw new SourceFileChangedException();
            map = new XElement("Map", new XAttribute("Name", NextMapName([])), new XAttribute("Type", "Project"));
            maps.Add(map);
            maps.SetAttributeValue("Maps", (string?)map.Attribute("Name"));
            index = 0;
        }

        AddShapefileVector(context, map, sourcePath, source.MapType);
        MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);
        string revision = Commit(context);
        return new(index, (string)map.Attribute("Name")!, (string)map.Attribute("Type")!,
            context.PreviousVisualizationRevision, revision);
    }

    private static void VerifyImportSourceIdentity(MapVectorImportRequest request)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.IsNullOrWhiteSpace(request.ExpectedSourceCanonicalPath) ||
            !Path.IsPathFullyQualified(request.ExpectedSourceCanonicalPath) ||
            !CanonicalPath(request.SourcePath).Equals(request.ExpectedSourceCanonicalPath, comparison))
            throw new SourceFileChangedException();
    }

    public static MapTreeSnapshot Load(MapTreeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(request,
            SourceFilePaths.Canonical(request.ProjectPath));
        MapTreeRequest scopedRequest = request with { ComputeReadLease = readLease?.Lease };
        using var context = Open(scopedRequest.ProjectPath, scopedRequest.ProjectXml, scopedRequest.ProjectXmlSHA,
            scopedRequest.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: scopedRequest.ProjectSessionID,
            externalCoreLayerReferences: scopedRequest.ExternalCoreLayerReferences,
            coreLayerContext: scopedRequest.CoreLayerContext);
        XElement map = ResolveMap(context.Visualization, scopedRequest.MapIndex,
            scopedRequest.MapName, scopedRequest.MapType);
        var tree = BuildTree(scopedRequest, map);
        return new(tree.Roots, context.PreviousVisualizationRevision, context.PreviousVisualizationRevision!,
            tree.ExpandedNodeIDs, tree.SelectedNodeID, tree.HiddenLayerIDs);
    }

    public static MapTreeMutationResult Perform(MapTreeActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Map.ComputeReadLease is not null)
            throw new SourceFileChangedException();
        using var context = Open(request.Map.ProjectPath, request.Map.ProjectXml, request.Map.ProjectXmlSHA,
            request.Map.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.Map.ProjectSessionID,
            externalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
            coreLayerContext: request.Map.CoreLayerContext);
        XElement map = ResolveMap(context.Visualization, request.Map.MapIndex, request.Map.MapName, request.Map.MapType);
        if (request.Map.ResultSelectionOverrides is { Length: > 0 })
            _ = BuildTree(request.Map, map);
        bool changed = request.Action switch
        {
            "newFolder" => AddFolder(map, request.Target ?? throw new ArgumentException("The folder destination is missing."), request.Name ?? ""),
            "rename" => RenameFolder(map, request.Node ?? throw new ArgumentException("The tree node is missing."), request.Name ?? ""),
            "visibility" => SetVisibility(request.Map, map, request.Node ?? throw new ArgumentException("The tree node is missing."),
                request.Checked ?? throw new ArgumentException("The visibility value is missing.")),
            "move" => MoveNodes(map, request.Nodes ?? [], request.Target ?? throw new ArgumentException("The folder destination is missing.")),
            "focusGeometry" => FocusGeometry(context, map, request.SourcePath, request.SourceRevision),
            "focusPlan" => FocusPlan(context, request.Map, map, request.SourcePath, request.SourceRevision),
            "addGeometry" => AddGeometry(context, map, request.SourcePath, request.SourceRevision),
            "addBoundaryCondition" => AddBoundaryCondition(context, request.Map, map,
                request.SourcePath, request.SourceRevision),
            "addResult" => AddResult(context, request.Map, map, request.SourcePath, request.SourceRevision),
            "addSessionHdf" => AddSessionHdf(context, request.Map, map,
                request.SourcePath, request.SourceRevision),
            "addGeoJSONVector" => AddGeoJSONVector(context, map, request.SourcePath, request.SourceRevision),
            "setResultMapType" => SetResultMapType(map, request.Name ?? ""),
            "setResultViewState" => SetResultViewState(request.Map, map,
                request.Node ?? throw new ArgumentException("The result node is missing."),
                request.Name, request.ProfileIndex),
            "setClassificationEditModeOpacity" => SetClassificationEditModeOpacity(request.Map, map,
                request.Node ?? throw new ArgumentException("The Classification node is missing."),
                request.Opacity ?? throw new ArgumentException("The Classification edit opacity is missing."),
                request.SourcePath, request.SourceRevision),
            "removeSourceNode" => RemoveSourceNode(context, request.Map, map, request.Node ?? throw new ArgumentException("The tree node is missing."),
                request.SourcePath, request.SourceRevision),
            "pin" => throw new NotSupportedException("The source map serializer reads IsPinned but does not save it."),
            _ => throw new NotSupportedException("This map tree action is not supported by the source map serializer.")
        };

        VerifyActionSourceRevision(request);
        MapTreeResultSelectionOverride[]? refreshedSelections = request.Map.ResultSelectionOverrides;
        if ((request.Action is "addResult" or "addSessionHdf") && request.SourcePath is { Length: > 0 } resultPath &&
            request.SourceRevision is { Length: > 0 } resultRevision)
        {
            SessionHdfReferenceCapture? sessionResult = request.Action == "addSessionHdf"
                ? request.Map.CoreLayerContext?.SessionHdfReferences.SingleOrDefault(reference =>
                    reference.Kind == "result" && PathEquals(reference.SourcePath, resultPath) &&
                    reference.SourceRevision.Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
                : null;
            if (sessionResult is { ResultPlanName: { Length: > 0 } })
            {
                SessionHdfResultOwner? previousOwner = refreshedSelections?.SingleOrDefault(selection =>
                    string.Equals(selection.PlanName, sessionResult.ResultPlanName, StringComparison.Ordinal))
                    ?.SessionHdfResultOwner;
                SessionHdfResultOwner owner = CreateSessionHdfResultOwner(context, sessionResult,
                    previousOwner?.ReferenceID);
                refreshedSelections = SelectSessionHdfResultForMap(refreshedSelections, context,
                    sessionResult, owner);
            }
            else if (request.Action == "addResult" || sessionResult is not null ||
                     request.Action == "addSessionHdf" &&
                     request.Map.CoreLayerContext?.SessionHdfReferences.Any(reference =>
                         reference.Kind == "result" && PathEquals(reference.SourcePath, resultPath) &&
                         reference.SourceRevision.Equals(resultRevision, StringComparison.OrdinalIgnoreCase)) == true)
            {
                ProjectResultIdentity result = ReadProjectResult(context, resultPath, resultRevision);
                if (result.PlanName is null || result.PlanAssetPath is not null)
                    refreshedSelections = SelectResultForMap(refreshedSelections, context, resultPath, resultRevision);
            }
        }
        var refreshedRequest = request.Map with
        {
            ExpectedVisualizationRevision = context.PreviousVisualizationRevision,
            ResultSelectionOverrides = refreshedSelections
        };
        // Resolve the complete response before publishing the .vis mutation.
        // Opening the committed file or rebuilding this tree after the move
        // could fail after persistence and hide the successful receipt.
        var tree = BuildTree(refreshedRequest, map);
        // Building the tree may read source data. Revalidate it immediately
        // before the commit so a changed HDF cannot be published as current.
        VerifyActionSourceRevision(request);
        string newRevision = changed ? Commit(context) : context.PreviousVisualizationRevision!;
        return new(tree.Roots, context.PreviousVisualizationRevision, newRevision,
            tree.ExpandedNodeIDs, tree.SelectedNodeID, tree.HiddenLayerIDs);
    }

    private static void VerifyActionSourceRevision(MapTreeActionRequest request)
    {
        if (request.SourcePath is not { Length: > 0 } sourcePath ||
            request.SourceRevision is not { Length: > 0 } sourceRevision) return;

        string canonicalSource = CanonicalPath(sourcePath);
        bool isRasterMapLayerRemoval = request.Action == "removeSourceNode" &&
            request.Node is { NodeType: "MapLayer", MapType: { } mapLayerType } &&
            MapRasterLayerRendering.SupportedTypes.Contains(mapLayerType, StringComparer.Ordinal);
        if (Directory.Exists(canonicalSource) &&
            string.Equals(Path.GetExtension(canonicalSource), ".gdb", StringComparison.OrdinalIgnoreCase))
        {
            GdbMapSourceIdentity gdb = GdbMapSourceService.CaptureSource(canonicalSource);
            if (!string.Equals(gdb.Revision, sourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
        else if (!File.Exists(canonicalSource) || (isRasterMapLayerRemoval
                     ? !MapRasterLayerSourceManifest.Matches(canonicalSource, sourceRevision)
                     : !RevisionFile(canonicalSource).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SourceFileChangedException();
        }
    }

    internal static MapTreeMutationResult MutateNode(MapTreeRequest request,
        CatalogTreeNodeLocator locator, Func<XElement, XElement, XElement, bool> update,
        bool materializeIfImplicit)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(update);
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.ProjectSessionID,
            externalCoreLayerReferences: request.ExternalCoreLayerReferences,
            coreLayerContext: request.CoreLayerContext);
        XElement map = ResolveMap(context.Visualization, request.MapIndex, request.MapName, request.MapType);
        var beforeTree = BuildTree(request, map);
        CatalogTreeNode target = FindExactNode(beforeTree.Roots, locator);
        XElement? serialized = locator.NodePath is { Length: > 0 }
            ? ResolveNode(map, locator, required: true)
            : null;
        if (serialized is null && !materializeIfImplicit)
            throw new NotSupportedException("The selected source map node has no persisted XML element.");

        XElement candidate = serialized is null
            ? SerializeSourceDefault(target, existing: null, associationStates: null)
            : new XElement(serialized);
        XElement rootBefore = new(context.Visualization.Root!);
        XElement candidateBefore = new(candidate);
        bool changed = update(map, candidate, context.Visualization.Root!);
        bool rootChanged = !XNode.DeepEquals(rootBefore, context.Visualization.Root);
        bool nodeChanged = !XNode.DeepEquals(candidateBefore, candidate);
        if (!changed && (rootChanged || nodeChanged))
            throw new InvalidOperationException("A source map mutation changed XML without reporting a change.");
        if (changed && !rootChanged && !nodeChanged)
            changed = false;
        string newRevision = context.PreviousVisualizationRevision!;
        if (changed)
        {
            if (nodeChanged)
            {
                MaterializedGeometryOwner? materializedGeometryOwner = null;
                if (serialized is null)
                    materializedGeometryOwner = InsertMaterializedNode(map, locator, candidate, beforeTree.Roots);
                else
                    serialized.ReplaceWith(candidate);
                VerifyNodeSourceIdentity(request, locator, map, materializedGeometryOwner);
            }
        }

        // Resolve settings and implicit-node materialization before publishing,
        // just as source actions do. A failed source/tree read must leave the
        // existing .vis untouched rather than hide an already committed write.
        var tree = BuildTree(request, map);
        if (changed) newRevision = Commit(context);
        return new(tree.Roots, context.PreviousVisualizationRevision, newRevision,
            tree.ExpandedNodeIDs, tree.SelectedNodeID, tree.HiddenLayerIDs);
    }

    private static CatalogTreeNode FindExactNode(IEnumerable<CatalogTreeNode> roots,
        CatalogTreeNodeLocator requested)
    {
        CatalogTreeNode[] matches = Descendants(roots).Where(node => node.Locator is { } candidate &&
            LocatorIdentityEquals(candidate, requested)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static bool LocatorIdentityEquals(CatalogTreeNodeLocator candidate,
        CatalogTreeNodeLocator requested, bool ignoreNodePath = false,
        bool allowMaterializedGeometryPath = false)
    {
        if (!string.Equals(candidate.RootName, requested.RootName, StringComparison.Ordinal) ||
            !string.Equals(candidate.NodeType, requested.NodeType, StringComparison.Ordinal) ||
            !string.Equals(candidate.NodeName, requested.NodeName, StringComparison.Ordinal) ||
            !OptionalPathEquals(candidate.AssetID, requested.AssetID) ||
            !OptionalPathEquals(candidate.Filename, requested.Filename) ||
            !string.Equals(candidate.MapType, requested.MapType, StringComparison.Ordinal) ||
            !string.Equals(candidate.FilenameRelative, requested.FilenameRelative, StringComparison.Ordinal) ||
            !OptionalPathEquals(candidate.ParentGeometryAssetID, requested.ParentGeometryAssetID) ||
            !string.Equals(candidate.ParentGeometryNodeName, requested.ParentGeometryNodeName, StringComparison.Ordinal) ||
            !OptionalPathEquals(candidate.AssociationAssetID, requested.AssociationAssetID) ||
            !string.Equals(candidate.AssociationKind, requested.AssociationKind, StringComparison.Ordinal) ||
            !OptionalPathEquals(candidate.ParentPlanAssetID, requested.ParentPlanAssetID) ||
            !string.Equals(candidate.ParentPlanNodeName, requested.ParentPlanNodeName, StringComparison.Ordinal) ||
            !string.Equals(candidate.ParentResultNodeType, requested.ParentResultNodeType, StringComparison.Ordinal) ||
            !OptionalPathEquals(candidate.ParentResultAssetID, requested.ParentResultAssetID) ||
            !string.Equals(candidate.ParentResultNodeName, requested.ParentResultNodeName, StringComparison.Ordinal) ||
            !string.Equals(candidate.ParentResultRevision, requested.ParentResultRevision, StringComparison.OrdinalIgnoreCase) ||
            !SessionHdfResultOwnerEquals(candidate.SessionHdfResultOwner, requested.SessionHdfResultOwner) ||
            !OptionalPathEquals(candidate.ParentBoundaryConditionAssetID, requested.ParentBoundaryConditionAssetID) ||
            !string.Equals(candidate.ParentBoundaryConditionNodeName,
                requested.ParentBoundaryConditionNodeName, StringComparison.Ordinal))
            return false;
        bool geometryPathEqual = OptionalIndicesEqual(candidate.ParentGeometryNodePath,
            requested.ParentGeometryNodePath) ||
            (allowMaterializedGeometryPath && requested.ParentGeometryNodePath is null &&
             candidate.ParentGeometryNodePath is { Length: > 0 });
        return (ignoreNodePath || OptionalIndicesEqual(candidate.NodePath, requested.NodePath)) &&
            geometryPathEqual &&
            OptionalIndicesEqual(candidate.ParentPlanNodePath, requested.ParentPlanNodePath) &&
            OptionalIndicesEqual(candidate.ParentResultNodePath, requested.ParentResultNodePath) &&
            OptionalIndicesEqual(candidate.ParentBoundaryConditionNodePath,
                requested.ParentBoundaryConditionNodePath);
    }

    private static bool OptionalPathEquals(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return PathEquals(left, right);
    }

    private static bool SessionHdfResultOwnerEquals(SessionHdfResultOwner? left,
        SessionHdfResultOwner? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return string.Equals(left.ReferenceID, right.ReferenceID, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ProjectSessionID, right.ProjectSessionID, StringComparison.OrdinalIgnoreCase) &&
            PathEquals(left.ProjectPath, right.ProjectPath) &&
            PathEquals(left.ResultPath, right.ResultPath) &&
            string.Equals(left.ResultRevision, right.ResultRevision, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.EmbeddedPlanName, right.EmbeddedPlanName, StringComparison.Ordinal) &&
            string.Equals(left.CoreLayerContextIdentityToken, right.CoreLayerContextIdentityToken,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool OptionalIndicesEqual(int[]? left, int[]? right) =>
        left is null || right is null ? left is null && right is null : left.SequenceEqual(right);

    private sealed record MaterializedGeometryOwner(XElement Element, XElement ParentElement,
        CatalogTreeNodeLocator Locator, CatalogTreeNodeLocator ParentLocator);

    private static MaterializedGeometryOwner? InsertMaterializedNode(XElement map, CatalogTreeNodeLocator locator,
        XElement candidate, CatalogTreeNode[] sourceRoots)
    {
        if (locator.NodePath is not null) throw new SourceFileChangedException();
        CatalogTreeNode[] chain = FindNodeChain(sourceRoots, locator) ?? throw new SourceFileChangedException();
        if (chain.Length < 2 || chain[0].Locator is not { NodeType: "Root" } rootLocator ||
            !string.Equals(rootLocator.RootName, locator.RootName, StringComparison.Ordinal) ||
            !string.Equals(rootLocator.NodeName, locator.RootName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        XElement? root = FindUniqueRoot(map, locator.RootName);
        root ??= AddRootNode(EnsureTreeNodes(map), locator.RootName);
        var generated = new List<XElement>();
        bool WasGenerated(XElement element) => generated.Any(item => ReferenceEquals(item, element));
        void RecordGeneratedSubtree(XElement element) =>
            generated.AddRange(element.DescendantsAndSelf("Node"));

        XElement parent = root;
        MaterializedGeometryOwner? materializedGeometryOwner = null;
        for (int index = 1; index < chain.Length - 1; index++)
        {
            CatalogTreeNode sourceNode = chain[index];
            CatalogTreeNodeLocator sourceLocator = sourceNode.Locator ?? throw new SourceFileChangedException();
            XElement[] sameIdentity = parent.Elements("Node").Where(element =>
                string.Equals((string?)element.Attribute("Type"), sourceLocator.NodeType, StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute("Name"), sourceLocator.NodeName, StringComparison.Ordinal))
                .ToArray();
            if (sameIdentity.Length > 1) throw new SourceFileChangedException();

            XElement? child = sameIdentity.SingleOrDefault();
            if (sourceLocator.NodePath is { Length: > 0 })
            {
                XElement? exact = ResolveNode(map, sourceLocator, required: false);
                if (exact is null || !ReferenceEquals(exact.Parent, parent) ||
                    child is not null && !ReferenceEquals(child, exact))
                    throw new SourceFileChangedException();
                child = exact;
            }
            else if (child is not null && !WasGenerated(child))
            {
                // A node which the source tree considers implicit cannot
                // already occupy this serialized parent slot. Treat that as
                // an ambiguous or stale identity instead of adopting it.
                throw new SourceFileChangedException();
            }

            if (child is null)
            {
                if (!IsExactGeometryOwner(locator, sourceLocator) || locator.ParentGeometryNodePath is not null ||
                    sourceLocator.NodePath is not null ||
                    SourceNodeType(sourceNode.SourceClass) != "Geometry")
                    throw new SourceFileChangedException();
                CatalogTreeNodeLocator parentLocator = chain[index - 1].Locator ?? throw new SourceFileChangedException();
                if (parentLocator.NodeType != "Root" && parentLocator.NodePath is not { Length: > 0 })
                    throw new SourceFileChangedException();
                XElement exactParent = parentLocator.NodeType == "Root"
                    ? FindUniqueRoot(map, parentLocator.NodeName) ?? throw new SourceFileChangedException()
                    : ResolveNode(map, parentLocator, required: true)!;
                if (!ReferenceEquals(exactParent, parent)) throw new SourceFileChangedException();
                child = SerializeSourceDefault(sourceNode, existing: null, associationStates: null);
                parent.Add(child);
                RecordGeneratedSubtree(child);
                materializedGeometryOwner = new MaterializedGeometryOwner(child, parent,
                    sourceLocator, parentLocator);
            }

            if (string.Equals(sourceLocator.NodeType, "Geometry", StringComparison.Ordinal) &&
                IsExactGeometryOwner(locator, sourceLocator) && WasGenerated(child) &&
                materializedGeometryOwner is null)
            {
                CatalogTreeNodeLocator parentLocator = chain[index - 1].Locator ?? throw new SourceFileChangedException();
                materializedGeometryOwner = new MaterializedGeometryOwner(child, parent, sourceLocator, parentLocator);
            }
            parent = child;
        }

        CatalogTreeNodeLocator targetLocator = chain[^1].Locator ?? throw new SourceFileChangedException();
        XElement[] targetRows = parent.Elements("Node").Where(element =>
            string.Equals((string?)element.Attribute("Type"), targetLocator.NodeType, StringComparison.Ordinal) &&
            string.Equals((string?)element.Attribute("Name"), targetLocator.NodeName, StringComparison.Ordinal))
            .ToArray();
        if (targetRows.Length > 1) throw new SourceFileChangedException();
        if (targetRows.SingleOrDefault() is { } generatedTarget)
        {
            if (!WasGenerated(generatedTarget)) throw new SourceFileChangedException();
            generatedTarget.ReplaceWith(candidate);
        }
        else
        {
            // Once an implicit Geometry owner has been materialized, its
            // source-default subtree must already contain this exact child.
            // Appending a missing row here would invent a hierarchy that the
            // retained source tree did not produce.
            if (materializedGeometryOwner is not null) throw new SourceFileChangedException();
            parent.Add(candidate);
        }

        if (locator.ParentGeometryNodeName is { Length: > 0 } && locator.ParentGeometryNodePath is null &&
            materializedGeometryOwner is null)
            throw new SourceFileChangedException();
        return materializedGeometryOwner;
    }

    private static void VerifyNodeSourceIdentity(MapTreeRequest request,
        CatalogTreeNodeLocator locator, XElement map, MaterializedGeometryOwner? materializedGeometryOwner)
    {
        CatalogTreeNode[] roots = BuildTree(request, map).Roots;
        CatalogTreeNode[] matches = Descendants(roots).Where(node => node.Locator is { } candidate &&
            LocatorIdentityEquals(candidate, locator, ignoreNodePath: true,
                allowMaterializedGeometryPath: materializedGeometryOwner is not null)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();

        if (materializedGeometryOwner is null) return;
        int[]? targetGeometryPath = matches[0].Locator?.ParentGeometryNodePath;
        if (locator.ParentGeometryNodePath is not null ||
            targetGeometryPath is not { Length: > 0 } ||
            string.IsNullOrWhiteSpace(locator.ParentGeometryNodeName) ||
            string.IsNullOrWhiteSpace(locator.ParentGeometryAssetID) ||
            !string.Equals(materializedGeometryOwner.Locator.NodeType, "Geometry", StringComparison.Ordinal) ||
            !IsExactGeometryOwner(locator, materializedGeometryOwner.Locator))
            throw new SourceFileChangedException();

        CatalogTreeNodeLocator refreshedLocator = matches[0].Locator!;
        if (refreshedLocator.NodePath is not { Length: > 0 }) throw new SourceFileChangedException();
        CatalogTreeNode[] geometryMatches = Descendants(roots).Where(node => node.Locator is { } candidate &&
            LocatorIdentityEquals(candidate, materializedGeometryOwner.Locator, ignoreNodePath: true)).ToArray();
        if (geometryMatches.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNodeLocator geometryLocator = geometryMatches[0].Locator!;
        int[]? insertedGeometryPath = geometryLocator.NodePath;
        if (insertedGeometryPath is null || insertedGeometryPath.Length == 0 ||
            !insertedGeometryPath.SequenceEqual(targetGeometryPath)) throw new SourceFileChangedException();
        XElement exactGeometry = ResolveNode(map, geometryLocator, required: true)!;
        XElement exactOwnerParent = materializedGeometryOwner.ParentLocator.NodeType == "Root"
            ? FindUniqueRoot(map, materializedGeometryOwner.ParentLocator.NodeName) ?? throw new SourceFileChangedException()
            : ResolveNode(map, materializedGeometryOwner.ParentLocator, required: true)!;
        XElement exactTarget = ResolveNode(map, refreshedLocator, required: true)!;
        if (!ReferenceEquals(exactGeometry, materializedGeometryOwner.Element) ||
            !ReferenceEquals(exactGeometry.Parent, exactOwnerParent) ||
            !ReferenceEquals(exactOwnerParent, materializedGeometryOwner.ParentElement) ||
            !ReferenceEquals(exactTarget.Parent, exactGeometry))
            throw new SourceFileChangedException();

        if (locator.ParentResultNodeType is not null || locator.ParentResultAssetID is not null ||
            locator.ParentResultNodePath is not null || locator.ParentResultNodeName is not null ||
            locator.ParentResultRevision is not null)
        {
            if (locator.ParentResultNodeType is not { Length: > 0 } resultType ||
                locator.ParentResultNodeName is not { Length: > 0 } resultName ||
                locator.ParentResultNodePath is not { Length: > 0 } resultPath ||
                locator.ParentResultAssetID is not { Length: > 0 } ||
                locator.ParentResultRevision is not { Length: > 0 } ||
                !string.Equals(materializedGeometryOwner.ParentLocator.NodeType, resultType, StringComparison.Ordinal) ||
                !string.Equals(materializedGeometryOwner.ParentLocator.NodeName, resultName, StringComparison.Ordinal) ||
                !OptionalIndicesEqual(materializedGeometryOwner.ParentLocator.NodePath, resultPath))
                throw new SourceFileChangedException();
            if (resultType == "Result")
            {
                if (!OptionalPathEquals(materializedGeometryOwner.ParentLocator.AssetID,
                        locator.ParentResultAssetID))
                    throw new SourceFileChangedException();
            }
            else
            {
                if (locator.SessionHdfResultOwner is { } sessionOwner)
                {
                    if (!SessionHdfResultOwnerEquals(sessionOwner,
                            materializedGeometryOwner.ParentLocator.SessionHdfResultOwner) ||
                        !string.Equals(sessionOwner.EmbeddedPlanName, locator.ParentResultNodeName,
                            StringComparison.Ordinal))
                        throw new SourceFileChangedException();
                    ValidateSessionHdfResultSelection(request, sessionOwner,
                        locator.ParentResultAssetID, locator.ParentResultRevision);
                }
                else
                {
                    if (materializedGeometryOwner.ParentLocator.AssetID is not { Length: > 0 } groupPlanAsset ||
                        locator.ParentPlanAssetID is { Length: > 0 } planAsset &&
                        !PathEquals(groupPlanAsset, planAsset))
                        throw new SourceFileChangedException();

                    if (locator.ParentPlanAssetID is not { Length: > 0 })
                    {
                        using var resultContext = Open(request.ProjectPath, request.ProjectXml,
                            request.ProjectXmlSHA, request.ExpectedVisualizationRevision, allowMissing: false,
                            projectSessionID: request.CoreLayerContext?.ProjectSessionID,
                            externalCoreLayerReferences: request.CoreLayerContext?.References,
                            coreLayerContext: request.CoreLayerContext);
                        ProjectResultIdentity selectedResult = ReadProjectResult(resultContext,
                            locator.ParentResultAssetID, locator.ParentResultRevision);
                        if (selectedResult.PlanAssetPath is null ||
                            !PathEquals(selectedResult.PlanAssetPath, groupPlanAsset))
                            throw new SourceFileChangedException();
                    }
                }
            }
            // The immutable identity includes the selected Result path and
            // HDF revision. Revalidate it before accepting the one permitted
            // nil-to-exact-parent-path transition.
            ValidateResultOwner(request, refreshedLocator);
        }
        else if (locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                 locator.ParentPlanNodeName is not null)
        {
            if (locator.ParentPlanAssetID is not { Length: > 0 } planAssetID ||
                locator.ParentPlanNodePath is not { Length: > 0 } planPath ||
                locator.ParentPlanNodeName is not { Length: > 0 } planName ||
                !string.Equals(materializedGeometryOwner.ParentLocator.NodeType, "Plan", StringComparison.Ordinal) ||
                !string.Equals(materializedGeometryOwner.ParentLocator.NodeName, planName, StringComparison.Ordinal) ||
                !OptionalPathEquals(materializedGeometryOwner.ParentLocator.AssetID, planAssetID) ||
                !OptionalIndicesEqual(materializedGeometryOwner.ParentLocator.NodePath, planPath))
                throw new SourceFileChangedException();
        }
    }

    private static bool IsExactGeometryOwner(CatalogTreeNodeLocator target,
        CatalogTreeNodeLocator candidate)
    {
        return string.Equals(candidate.RootName, target.RootName, StringComparison.Ordinal) &&
            string.Equals(candidate.NodeType, "Geometry", StringComparison.Ordinal) &&
            string.Equals(target.ParentGeometryNodeName, candidate.NodeName, StringComparison.Ordinal) &&
            OptionalPathEquals(target.ParentGeometryAssetID, candidate.AssetID) &&
            string.Equals(target.ParentPlanNodeName, candidate.ParentPlanNodeName, StringComparison.Ordinal) &&
            OptionalPathEquals(target.ParentPlanAssetID, candidate.ParentPlanAssetID) &&
            OptionalIndicesEqual(target.ParentPlanNodePath, candidate.ParentPlanNodePath) &&
            string.Equals(target.ParentResultNodeType, candidate.ParentResultNodeType, StringComparison.Ordinal) &&
            string.Equals(target.ParentResultNodeName, candidate.ParentResultNodeName, StringComparison.Ordinal) &&
            OptionalPathEquals(target.ParentResultAssetID, candidate.ParentResultAssetID) &&
            string.Equals(target.ParentResultRevision, candidate.ParentResultRevision, StringComparison.OrdinalIgnoreCase) &&
            OptionalIndicesEqual(target.ParentResultNodePath, candidate.ParentResultNodePath) &&
            SessionHdfResultOwnerEquals(target.SessionHdfResultOwner, candidate.SessionHdfResultOwner);
    }

    internal static MapTreeMutationResult MutateNode(MapTreeRequest request,
        CatalogTreeNodeLocator locator, Func<XElement, bool> update, bool materializeIfImplicit) =>
        MutateNode(request, locator, (_, node, _) => update(node), materializeIfImplicit);

    private static bool AddResult(OperationContext context, MapTreeRequest mapRequest, XElement map,
        string? sourcePath, string? sourceRevision)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType == "Plan")
        {
            ProjectResultIdentity planResult = ReadProjectResult(context, sourcePath, sourceRevision);
            if (planResult.PlanName is null || planResult.PlanAssetPath is null || planResult.SamePlanNameCount != 1)
                return false;
            string[] focusedPlanIDs = SerializedPlanAssetIDs(context.ProjectAliasPath, context.ProjectXml,
                map, "Focused Plans", context.CoreLayerContext);
            bool belongsToFocusedPlan = focusedPlanIDs.Any(assetID => PathEquals(assetID, planResult.PlanAssetPath));
            if (!belongsToFocusedPlan) return false;

            ProjectPlanIdentity plan = ResolveProjectPlanByName(context, planResult.PlanName);
            if (!PathEquals(plan.AssetPath, planResult.PlanAssetPath)) throw new SourceFileChangedException();
            XElement before = new(map);
            MapTreeResultSelectionOverride[] selectionOverrides =
                SelectResultForMap(mapRequest.ResultSelectionOverrides, context, sourcePath!, sourceRevision!) ?? [];
            // Preserve the currently displayed common-layer states before
            // changing which Result supplies the MultiResult children.
            Dictionary<string, XElement> associationStates =
                CapturePlanAssociationStates(context, map, mapRequest);
            CatalogTreeNode planDefault = ProjectPlanDefault(context, plan, selectionOverrides);
            XElement? focusedRoot = FindUniqueRoot(map, "Focused Plans");
            if (focusedRoot is null) throw new SourceFileChangedException();
            XElement[] planRows = TreeRows(focusedRoot).Where(node =>
                (string?)node.Attribute("Type") == "Plan" &&
                string.Equals((string?)node.Attribute("Name"), plan.Name, StringComparison.Ordinal)).ToArray();
            if (planRows.Length != 1) throw new SourceFileChangedException();
            XElement currentPlan = planRows[0];
            if (!PathEquals(plan.AssetPath, planResult.PlanAssetPath)) throw new SourceFileChangedException();

            XElement updatedPlan = SerializeSourceDefault(planDefault, currentPlan, associationStates);
            XElement[] groups = updatedPlan.Elements("Node").Where(node =>
                (string?)node.Attribute("Type") == "MultiResult" &&
                string.Equals((string?)node.Attribute("Name"), plan.Name, StringComparison.Ordinal)).ToArray();
            if (groups.Length != 1) throw new SourceFileChangedException();
            groups[0].SetAttributeValue("IsChecked", true);
            currentPlan.ReplaceWith(updatedPlan);
            _ = ReadProjectResult(context, sourcePath, sourceRevision);
            return !XNode.DeepEquals(before, map);
        }
        if (mapType is not ("Buildable" or "Geometry"))
            throw new NotSupportedException("Only Buildable and Geometry maps accept source result updates.");
        ProjectResultIdentity result = ReadProjectResult(context, sourcePath, sourceRevision);
        string nodeType = result.PlanName is null ? "Result" : "MultiResult";
        string nodeName = result.PlanName ?? result.Name;

        // RasProjectTreeBase.AddResult creates a single MultiResult per Plan.Name.
        // The serialized source tree stores no per-history Result membership in
        // that group; adding another history for the same Plan therefore only
        // makes the existing group visible.
        if (nodeType == "Result" && result.SameNameCount != 1)
            throw new NotSupportedException("The source map serializer identifies individual results by name; this result name is ambiguous.");
        if (nodeType == "MultiResult" && result.SamePlanNameCount != 1)
            throw new NotSupportedException("The source map serializer identifies result groups by Plan name; this Plan name is ambiguous.");

        XElement root = EnsureRootNode(map, "Layers");
        XElement[] matches = root.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == nodeType && (string?)node.Attribute("Name") == nodeName).ToArray();
        if (matches.Length > 1) throw new NotSupportedException("The source map contains duplicate result nodes that cannot be distinguished safely.");
        if (matches.Length == 1)
        {
            if (ReadBool(matches[0], "IsChecked")) return false;
            matches[0].SetAttributeValue("IsChecked", true);
            return true;
        }

        root.Add(new XElement("Node", new XAttribute("Type", nodeType), new XAttribute("Name", nodeName),
            new XAttribute("IsChecked", true)));
        return true;
    }

    private static bool AddSessionHdf(OperationContext context, MapTreeRequest request, XElement map,
        string? sourcePath, string? sourceRevision)
    {
        SessionHdfReferenceCapture reference = ResolveSessionHdfReference(context, sourcePath,
            sourceRevision, expectedKind: null);
        switch (reference.Kind)
        {
            case "plan":
            {
                ProjectPlanIdentity plan = ResolveProjectPlan(context, reference.SourcePath, reference.SourceRevision);
                if (!string.Equals(plan.Name, reference.Name, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                if ((string?)map.Attribute("Type") == "Plan")
                    return FocusPlan(context, request, map, reference.SourcePath, reference.SourceRevision);
                if ((string?)map.Attribute("Type") is not ("Project" or "Geometry"))
                    throw new NotSupportedException("The selected map does not show imported Plan assets.");
                if (!Descendants(BuildTree(request, map).Roots).Any(node =>
                        node.SourceClass == "ObservablePlan" && node.AssetID is { } asset &&
                        PathEquals(asset, plan.AssetPath) && node.Name == plan.Name))
                    throw new SourceFileChangedException();
                return false;
            }
            case "result":
            {
                ProjectResultIdentity result = ReadProjectResult(context, reference.SourcePath, reference.SourceRevision);
                if (!string.Equals(result.Name, reference.Name, StringComparison.Ordinal) ||
                    !string.Equals(result.PlanName, reference.ResultPlanName, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                if (result.PlanName is { Length: > 0 })
                {
                    if (result.SamePlanNameCount != 0)
                        throw new SessionHdfMapOwnershipConflictException(
                            "The Result was added to this project session, but the saved map cannot distinguish its embedded Plan group from the same-named project Plan. The map was left unchanged.");
                    return AddSessionHdfResult(context, map, reference, result);
                }
                string mapType = (string?)map.Attribute("Type") ?? "";
                if (mapType == "Project")
                {
                    if (!Descendants(BuildTree(request, map).Roots).Any(node =>
                            node.SourceClass == "ObservableResult" && node.AssetID is { } asset &&
                            PathEquals(asset, reference.CanonicalSourcePath) && node.Name == reference.Name))
                        throw new SourceFileChangedException();
                    return false;
                }
                return AddResult(context, request, map, reference.SourcePath, reference.SourceRevision);
            }
            case "terrain":
            {
                ProjectTerrainIdentity terrain = ResolveProjectTerrain(context, reference.SourcePath, reference.SourceRevision);
                if (!string.Equals(terrain.Name, reference.Name, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                if ((string?)map.Attribute("Type") == "Project")
                {
                    if (!Descendants(BuildTree(request, map).Roots).Any(node =>
                            node.SourceClass == "ObservableTerrain" && node.AssetID is { } asset &&
                            PathEquals(asset, terrain.AssetPath) && node.Name == terrain.Name))
                        throw new SourceFileChangedException();
                    return false;
                }
                if ((string?)map.Attribute("Type") is not ("Geometry" or "Plan"))
                    throw new NotSupportedException("The selected map does not show imported Terrain assets.");
                CatalogTreeNode[] candidates = Descendants(BuildTree(request, map).Roots)
                    .Where(node => node.SourceClass == "ObservableTerrain" && node.AssetID is { } asset &&
                        PathEquals(asset, terrain.AssetPath) && node.Name == terrain.Name &&
                        node.Locator is { NodeType: "Terrain", AssociationKind: null,
                            ParentGeometryAssetID: null, ParentPlanAssetID: null, ParentResultAssetID: null })
                    .ToArray();
                if (candidates.Length != 1 || candidates[0].Locator is not { } locator)
                    throw new SourceFileChangedException();
                return SetVisibility(request, map, locator, checkedValue: true);
            }
            default:
                throw new SourceFileChangedException();
        }
    }

    private static bool AddSessionHdfResult(OperationContext context, XElement map,
        SessionHdfReferenceCapture reference, ProjectResultIdentity result)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType == "Plan")
            throw new NotSupportedException("An imported Result with an embedded Plan cannot be assigned to a different saved Plan map.");
        if (mapType == "Project") return false;
        if (mapType is not ("Buildable" or "Geometry"))
            throw new NotSupportedException("The selected map does not accept embedded-Plan Result groups.");
        if (string.IsNullOrWhiteSpace(result.PlanName) ||
            !string.Equals(result.PlanName, reference.ResultPlanName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        XElement root = EnsureRootNode(map, "Layers");
        XElement[] matches = root.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == "MultiResult" &&
            string.Equals((string?)node.Attribute("Name"), result.PlanName, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1)
            throw new SessionHdfMapOwnershipConflictException(
                "The Result was added to this project session, but the saved map contains duplicate embedded-Plan groups that cannot be distinguished safely. The map was left unchanged.");
        if (matches.SingleOrDefault() is { } existing)
        {
            if (ReadBool(existing, "IsChecked")) return false;
            existing.SetAttributeValue("IsChecked", true);
            return true;
        }

        // The retained serializer groups Results by their embedded Plan name.
        // The name remains a group label only; the session owner is carried in
        // the typed request and is never serialized as Project.Plans membership.
        root.Add(new XElement("Node", new XAttribute("Type", "MultiResult"),
            new XAttribute("Name", result.PlanName), new XAttribute("IsChecked", true)));
        return true;
    }

    private static SessionHdfResultOwner CreateSessionHdfResultOwner(OperationContext context,
        SessionHdfReferenceCapture reference, string? preferredReferenceID = null)
    {
        CoreLayerRequestContext coreContext = context.CoreLayerContext ?? throw new SourceFileChangedException();
        if (reference.Kind != "result" || !reference.SessionOnly ||
            string.IsNullOrWhiteSpace(reference.ResultPlanName))
            throw new SourceFileChangedException();
        // The source .vis serializer keys the MultiResult group by embedded
        // Plan name. Its typed group owner is the deterministic first exact
        // external Result capture for that group; the selected result path is
        // carried separately by MapTreeResultSelectionOverride. This keeps
        // imports stable when another Result with the same embedded Plan is
        // added later, without claiming a same-named Project Plan.
        SessionHdfReferenceCapture[] group = (coreContext.SessionHdfReferences ?? [])
            .Where(candidate => candidate.Kind == "result" &&
                string.Equals(candidate.ResultPlanName, reference.ResultPlanName, StringComparison.Ordinal))
            .OrderBy(candidate => SourceFilePaths.Canonical(candidate.SourcePath), PathStringComparer)
            .ToArray();
        if (group.Length == 0 || !group.Any(candidate => candidate.ReferenceID == reference.ReferenceID))
            throw new SourceFileChangedException();
        SessionHdfReferenceCapture selectedAnchor = preferredReferenceID is null
            ? group[0]
            : group.SingleOrDefault(candidate => string.Equals(candidate.ReferenceID,
                preferredReferenceID, StringComparison.OrdinalIgnoreCase))
              ?? throw new SourceFileChangedException();
        SessionHdfReferenceCapture anchor = ResolveSessionHdfReference(context,
            selectedAnchor.SourcePath, selectedAnchor.SourceRevision, expectedKind: "result");
        ProjectResultIdentity result = ReadProjectResult(context, anchor.SourcePath, anchor.SourceRevision);
        if (!string.Equals(result.Name, anchor.Name, StringComparison.Ordinal) ||
            !string.Equals(result.PlanName, anchor.ResultPlanName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return new SessionHdfResultOwner(anchor.ReferenceID, anchor.ProjectSessionID,
            anchor.ProjectPath, SourceFilePaths.Canonical(anchor.SourcePath),
            anchor.SourceRevision.ToUpperInvariant(), anchor.ResultPlanName!,
            coreContext.IdentityToken);
    }

    private static MapTreeResultSelectionOverride[] SelectSessionHdfResultForMap(
        MapTreeResultSelectionOverride[]? existing, OperationContext context,
        SessionHdfReferenceCapture importedReference, SessionHdfResultOwner owner)
    {
        if (importedReference.Kind != "result" ||
            !string.Equals(importedReference.ResultPlanName, owner.EmbeddedPlanName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        MapTreeResultSelectionOverride[] current = existing ?? [];
        if (current.Any(selection => selection is null)) throw new SourceFileChangedException();
        MapTreeResultSelectionOverride[] sameGroup = current.Where(selection =>
            string.Equals(selection.PlanName, owner.EmbeddedPlanName, StringComparison.Ordinal)).ToArray();
        if (sameGroup.Length > 1) throw new SourceFileChangedException();
        if (sameGroup.SingleOrDefault() is { } previous)
        {
            if (previous.SessionHdfResultOwner is null)
                throw new SessionHdfMapOwnershipConflictException(
                    "The Result was added to this project session, but the saved map already selects a same-named project Plan group. The map was left unchanged.");
            if (!string.Equals(previous.SessionHdfResultOwner.EmbeddedPlanName, owner.EmbeddedPlanName,
                    StringComparison.Ordinal) ||
                !string.Equals(previous.SessionHdfResultOwner.ProjectSessionID, owner.ProjectSessionID,
                    StringComparison.OrdinalIgnoreCase) ||
                !PathEquals(previous.SessionHdfResultOwner.ProjectPath, owner.ProjectPath))
                throw new SourceFileChangedException();

            if (!previous.FollowLatest)
            {
                SessionHdfReferenceCapture[] selectedCapture = (context.CoreLayerContext?.SessionHdfReferences ?? [])
                    .Where(candidate => candidate.Kind == "result" &&
                        PathEquals(candidate.SourcePath, previous.SelectedResultPath) &&
                        candidate.SourceRevision.Equals(previous.SelectedResultRevision, StringComparison.OrdinalIgnoreCase) &&
                        candidate.ResultPlanName == owner.EmbeddedPlanName).ToArray();
                if (selectedCapture.Length != 1) throw new SourceFileChangedException();
            }

            MapTreeResultSelectionOverride retained = previous with
            {
                SelectedResultPath = previous.FollowLatest ? "" : previous.SelectedResultPath,
                SelectedResultRevision = previous.FollowLatest ? "" : previous.SelectedResultRevision,
                SessionHdfResultOwner = owner
            };
            return current.Where(selection => !string.Equals(selection.PlanName,
                    owner.EmbeddedPlanName, StringComparison.Ordinal))
                .Append(retained).ToArray();
        }

        return current.Append(new MapTreeResultSelectionOverride("", owner.EmbeddedPlanName,
            "", "", owner, FollowLatest: true)).ToArray();
    }

    private static SessionHdfReferenceCapture ResolveSessionHdfReference(OperationContext context,
        string? sourcePath, string? sourceRevision, string? expectedKind)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath) ||
            string.IsNullOrWhiteSpace(sourceRevision)) throw new ArgumentException("The selected session HDF source is incomplete.");
        SessionHdfReferenceCapture[] matches = (context.CoreLayerContext?.SessionHdfReferences ?? [])
            .Where(reference => PathEquals(reference.SourcePath, sourcePath) &&
                reference.SourceRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase) &&
                (expectedKind is null || reference.Kind == expectedKind)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        SessionHdfReferenceCapture capture = matches[0];
        if (!capture.SessionOnly || capture.Kind is not ("plan" or "result" or "terrain") ||
            !PathEquals(capture.ProjectPath, context.ProjectPath) ||
            !PathEquals(capture.CanonicalSourcePath, SourceFilePaths.Canonical(sourcePath)) ||
            !CoreLayerImportService.SessionHdfReferenceID(capture.ProjectSessionID,
                context.ProjectPath, capture.Kind, capture.CanonicalSourcePath, capture.SourceRevision)
                .Equals(capture.ReferenceID, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return capture;
    }

    private static MapTreeResultSelectionOverride[]? SelectResultForMap(
        MapTreeResultSelectionOverride[]? existing, OperationContext context, string sourcePath,
        string sourceRevision)
    {
        ProjectResultIdentity result = ReadProjectResult(context, sourcePath, sourceRevision);
        if (result.PlanName is null) return existing;
        if (result.PlanAssetPath is null || result.SamePlanNameCount != 1)
            throw new SourceFileChangedException();
        string planAsset = result.PlanAssetPath;
        MapTreeResultSelectionOverride[] current = existing ?? [];
        if (current.Any(selection => selection is null)) throw new SourceFileChangedException();
        return current.Where(selection =>
                !PathEquals(selection.PlanAssetID, planAsset) ||
                !string.Equals(selection.PlanName, result.PlanName, StringComparison.Ordinal))
            .Append(new MapTreeResultSelectionOverride(planAsset, result.PlanName,
                CanonicalPath(sourcePath), sourceRevision)).ToArray();
    }

    private static bool FocusGeometry(OperationContext context, XElement map, string? sourcePath,
        string? sourceRevision)
    {
        if ((string?)map.Attribute("Type") != "Geometry")
            throw new NotSupportedException("Only a saved Geometry map accepts focused Geometry entries.");
        ProjectGeometryIdentity geometry = ResolveProjectGeometry(context, sourcePath, sourceRevision);
        Dictionary<string, XElement> associationStates = CaptureGeometryAssociationStates(context, map);
        GeometryMapRootElements roots = EnsureGeometryMapRoots(map);
        XElement focusedRoot = roots.Focused ?? throw new SourceFileChangedException();
        XElement before = new(map);
        XElement[] matches = TreeRows(focusedRoot)
            .Where(node => (string?)node.Attribute("Type") == "Geometry" &&
                string.Equals((string?)node.Attribute("Name"), geometry.Name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        if (matches.Length == 1)
        {
            SynchronizeGeometryMapRoots(context, map, associationStates);
            _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
            return !XNode.DeepEquals(before, map);
        }

        CatalogTreeNode defaultNode = ProjectGeometryDefault(context, "Geometry", geometry);
        focusedRoot.Add(SerializeSourceDefault(defaultNode, existing: null, associationStates));
        SynchronizeGeometryMapRoots(context, map, associationStates);
        _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
        return true;
    }

    private static bool FocusPlan(OperationContext context, MapTreeRequest request, XElement map, string? sourcePath,
        string? sourceRevision)
    {
        if ((string?)map.Attribute("Type") != "Plan")
            throw new NotSupportedException("Only a saved Plan map accepts focused Plan entries.");
        ProjectPlanIdentity plan = ResolveProjectPlan(context, sourcePath, sourceRevision);
        Dictionary<string, XElement> associationStates = CapturePlanAssociationStates(context, map, request);
        PlanMapRootElements roots = EnsurePlanMapRoots(map);
        XElement before = new(map);
        XElement[] existing = TreeRows(roots.Focused).Where(node =>
            (string?)node.Attribute("Type") == "Plan" &&
            string.Equals((string?)node.Attribute("Name"), plan.Name, StringComparison.Ordinal)).ToArray();
        if (existing.Length > 1) throw new SourceFileChangedException();
        if (existing.Length == 0)
        {
            CatalogTreeNode sourceDefault = ProjectPlanDefault(context, plan, request.ResultSelectionOverrides);
            roots.Focused.Add(SerializeSourceDefault(sourceDefault, existing: null, associationStates));
        }
        SynchronizePlanMapRoots(context, map, associationStates, request.ResultSelectionOverrides);
        _ = ResolveProjectPlan(context, plan.AssetPath, plan.Revision);
        return !XNode.DeepEquals(before, map);
    }

    private static CatalogTreeNode ProjectPlanDefault(OperationContext context, ProjectPlanIdentity plan,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        CatalogTreeNode[] defaults = PreparedPlanDefaults(context, [plan.AssetPath], resultSelectionOverrides);
        CatalogTreeNode[] matches = Descendants(defaults.Single(root => root.Name == "Focused Plans").Children ?? [])
            .Where(node => SourceNodeType(node.SourceClass) == "Plan" && node.AssetID is { Length: > 0 } assetID &&
                PathEquals(assetID, plan.AssetPath) && string.Equals(node.Name, plan.Name, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static CatalogTreeNode[] PreparedPlanDefaults(OperationContext context, string[] focusedPlanIDs,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, FocusedPlanIds: focusedPlanIDs,
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), "Plan");
        CatalogTreeNode[] projectDefaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, "Project",
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), "Project");
        CatalogTreeNode[] projectResults = Descendants(projectDefaults
            .Where(root => root.Name == "Results").SelectMany(root => root.Children ?? [])).ToArray();
        CatalogTreeNode[] resultCandidates = projectResults
            .Where(node => node.SourceClass == "ObservableResult" && node.AssetID is not null).ToArray();
        CatalogTreeNode[] groupCandidates = projectResults
            .Where(node => node.SourceClass == "MultiResultNodeVM").ToArray();
        return PrepareResultContexts(context, defaults, groupCandidates, resultCandidates, resultSelectionOverrides)[0];
    }

    private static bool AddGeometry(OperationContext context, XElement map, string? sourcePath,
        string? sourceRevision)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("The current map tree does not accept added Geometry entries.");
        ProjectGeometryIdentity geometry = ResolveProjectGeometry(context, sourcePath, sourceRevision);
        Dictionary<string, XElement>? associationStates = mapType == "Geometry"
            ? CaptureGeometryAssociationStates(context, map) : null;
        XElement root = mapType switch
        {
            "Project" => EnsureUniqueRoot(EnsureTreeNodes(map), "Geometries"),
            "Buildable" => EnsureGeometryMapRoots(map).Layers,
            "Geometry" => EnsureGeometryMapRoots(map).Focused ?? throw new SourceFileChangedException(),
            "Plan" => EnsurePlanMapRoots(map).Layers,
            _ => throw new NotSupportedException("The current map tree does not accept added Geometry entries.")
        };
        XElement before = new(map);
        XElement[] matches = root.Descendants("Node")
            .Where(node => (string?)node.Attribute("Type") == "Geometry" &&
                string.Equals((string?)node.Attribute("Name"), geometry.Name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        if (matches.Length == 1)
        {
            if (!ReadBool(matches[0], "IsChecked")) matches[0].SetAttributeValue("IsChecked", true);
            if (mapType == "Geometry") SynchronizeGeometryMapRoots(context, map, associationStates);
            _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
            return !XNode.DeepEquals(before, map);
        }

        CatalogTreeNode defaultNode = ProjectGeometryDefault(context, mapType, geometry);
        XElement element = SerializeSourceDefault(defaultNode, existing: null, associationStates);
        // GenericImports.TryImportH5FileAsync explicitly checks the returned
        // Geometry node for every map type after RasTreeVMV2.AddGeometry.
        element.SetAttributeValue("IsChecked", true);
        root.Add(element);
        if (mapType == "Geometry") SynchronizeGeometryMapRoots(context, map, associationStates);
        _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
        return true;
    }

    private static CatalogTreeNode ProjectGeometryDefault(OperationContext context, string mapType,
        ProjectGeometryIdentity geometry)
    {
        string[] geometryIDs = [geometry.AssetPath];
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, FocusedGeometryIds: geometryIDs,
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), mapType);
        string rootName = mapType switch
        {
            "Project" => "Geometries",
            "Geometry" => "Focused Geometries",
            "Buildable" or "Plan" => "Layers",
            _ => throw new NotSupportedException("The requested map type does not own Geometry rows.")
        };
        CatalogTreeNode[] matches = Descendants(defaults.Single(root => root.Name == rootName).Children ?? [])
            .Where(node => SourceNodeType(node.SourceClass) == "Geometry" &&
                node.AssetID is { } path && PathEquals(path, geometry.AssetPath))
            .ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static bool AddBoundaryCondition(OperationContext context, MapTreeRequest request, XElement map,
        string? sourcePath, string? sourceRevision)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("The current map tree does not accept added Boundary Condition entries.");

        ProjectBoundaryConditionIdentity boundary = ResolveProjectBoundaryCondition(context, sourcePath,
            sourceRevision);
        string rootName = mapType == "Project" ? "Boundary Conditions" : "Layers";
        if (mapType == "Geometry") _ = EnsureGeometryMapRoots(map);
        else if (mapType == "Plan") _ = EnsurePlanMapRoots(map);
        XElement root = EnsureUniqueRoot(EnsureTreeNodes(map), rootName);
        XElement before = new(map);
        XElement[] matches = root.Descendants("Node")
            .Where(node => (string?)node.Attribute("Type") == "BoundaryCondition" &&
                string.Equals((string?)node.Attribute("Name"), boundary.Name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        if (matches.Length == 0)
        {
            CatalogTreeNode sourceDefault = ProjectBoundaryConditionDefault(context, mapType, boundary);
            root.Add(SerializeSourceDefault(sourceDefault, existing: null, associationStates: null));
        }

        // The retained AddReference call does not set BoundaryCondition.IsChecked;
        // preserve its default state instead of forcing Geometry's visibility rule.
        _ = ResolveProjectBoundaryCondition(context, boundary.AssetPath, boundary.Revision);
        return !XNode.DeepEquals(before, map);
    }

    private static CatalogTreeNode ProjectBoundaryConditionDefault(OperationContext context, string mapType,
        ProjectBoundaryConditionIdentity boundary)
    {
        string[] boundaryIDs = mapType == "Project" ? [] : [boundary.AssetPath];
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, FocusedBoundaryConditionIds: boundaryIDs,
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), mapType);
        string rootName = mapType == "Project" ? "Boundary Conditions" : "Layers";
        CatalogTreeNode[] matches = Descendants(defaults.Single(root => root.Name == rootName).Children ?? [])
            .Where(node => SourceNodeType(node.SourceClass) == "BoundaryCondition" &&
                node.AssetID is { } path && PathEquals(path, boundary.AssetPath) &&
                string.Equals(node.Name, boundary.Name, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static XElement SerializeSourceDefault(CatalogTreeNode source, XElement? existing,
        IReadOnlyDictionary<string, XElement>? associationStates)
    {
        string type = SourceNodeType(source.SourceClass) ??
            throw new NotSupportedException("A source map node cannot be persisted by the retained tree serializer.");
        XElement? previous = existing;
        if (source.Locator is { AssociationKind: { Length: > 0 } kind,
            AssociationAssetID: { Length: > 0 } assetID })
        {
            previous = null;
            if (associationStates is not null &&
                associationStates.TryGetValue(AssociationStateKey(kind, assetID), out XElement? matchedState))
                previous = matchedState;
        }
        var element = previous is null ? new XElement("Node") : new XElement(previous);
        element.SetAttributeValue("Type", type);
        element.SetAttributeValue("Name", source.Name);

        XElement[] oldChildren = element.Elements("Node").ToArray();
        // MultiResultVisualContext serializes selected map/profile state on the
        // group row. The child rows below are the current key-frame Result's
        // embedded Plan/Geometry/BoundaryCondition contexts, not the history
        // Result options that feed the group's timeline.
        var used = new HashSet<XElement>();
        var children = new List<XElement>();
        foreach (CatalogTreeNode childSource in source.Children ?? [])
        {
            string? childType = SourceNodeType(childSource.SourceClass);
            if (childType is null) continue;
            XElement[] matches = oldChildren.Where(child => !used.Contains(child) &&
                (string?)child.Attribute("Type") == childType &&
                (string?)child.Attribute("Name") == childSource.Name).ToArray();
            if (matches.Length > 1) throw new SourceFileChangedException();
            XElement? previousChild = matches.SingleOrDefault();
            if (previousChild is not null) used.Add(previousChild);
            children.Add(SerializeSourceDefault(childSource, previousChild, associationStates));
        }
        // Retained Plan nodes are deliberately non-checkable. The result
        // group and its embedded key-frame children own visibility state.
        if (type == "Plan") element.Attribute("IsChecked")?.Remove();
        foreach (XElement oldChild in oldChildren)
        {
            if (used.Contains(oldChild)) continue;
            string? oldType = (string?)oldChild.Attribute("Type");
            // These are source-owned contexts whose membership is recalculated
            // from Geometry associations. Preserve unrelated/unknown XML, but
            // do not leave a stale association after focus/membership changes.
            if (SourceNodeType(source.SourceClass) == "Plan" &&
                oldType is "Geometry" or "Terrain" or "NValue" or "Classification" or "BoundaryCondition" or "MultiResult")
                continue;
            if (type == "MultiResult" && oldType is "Plan" or "Geometry" or "BoundaryCondition")
                continue;
            if (oldType is "Terrain" or "NValue" or "Classification") continue;
            children.Add(new XElement(oldChild));
        }
        element.Elements("Node").Remove();
        element.Add(children);
        return element;
    }

    private static bool SetResultMapType(XElement map, string rawMapType)
    {
        string sourceMapType = rawMapType.Trim() switch
        {
            "WaterSurfaceElevation" or "Water Surface" => "Water Surface",
            "Depth" => "Depth",
            "Velocity" => "Velocity",
            "DepthTimesVelocity" or "D*V" => "D*V",
            "DepthTimesVelocitySquared" or "D*V^2" => "D*V^2",
            _ => throw new NotSupportedException("The selected Result map type is not supported by the current map renderer.")
        };

        XElement[] treeRoots = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        var activeResultNodes = new List<XElement>();
        foreach (XElement root in treeRoots)
            CollectActiveResultNodes(root, ancestorsEnabled: true, activeResultNodes);

        bool changed = false;
        foreach (XElement node in activeResultNodes)
        {
            if (string.Equals((string?)node.Attribute("SelectedMapType"), sourceMapType, StringComparison.Ordinal)) continue;
            node.SetAttributeValue("SelectedMapType", sourceMapType);
            changed = true;
        }
        return changed;
    }

    private static bool SetResultViewState(MapTreeRequest request, XElement map,
        CatalogTreeNodeLocator locator, string? rawMapType, int? profileIndex)
    {
        if (locator.NodeType is not ("Result" or "MultiResult"))
            throw new NotSupportedException("Result view state is available only on Result and MultiResult source nodes.");
        _ = FindExactNode(BuildTree(request, map).Roots, locator);
        if (locator.NodePath is not { Length: > 0 })
            throw new SourceFileChangedException();
        XElement node = ResolveNode(map, locator, required: true)!;
        if (profileIndex is < 0)
            throw new ArgumentOutOfRangeException(nameof(profileIndex), "The Result profile index cannot be negative.");

        string? sourceMapType = rawMapType is null ? null : NormalizeResultMapType(rawMapType);
        bool changed = false;
        if (sourceMapType is not null && !string.Equals((string?)node.Attribute("SelectedMapType"), sourceMapType,
                StringComparison.Ordinal))
        {
            node.SetAttributeValue("SelectedMapType", sourceMapType);
            changed = true;
        }
        if (profileIndex is int index)
        {
            XAttribute? current = node.Attribute("ProfileIndex");
            if (index == 0)
            {
                if (current is not null)
                {
                    current.Remove();
                    changed = true;
                }
            }
            else if (!int.TryParse(current?.Value, out int currentIndex) || currentIndex != index)
            {
                node.SetAttributeValue("ProfileIndex", index);
                changed = true;
            }
        }
        return changed;
    }

    private static bool SetClassificationEditModeOpacity(MapTreeRequest request, XElement map,
        CatalogTreeNodeLocator locator, int opacity, string? sourcePath, string? sourceRevision)
    {
        if (locator.NodeType != "Classification" || locator.AssociationKind != "Classification" ||
            string.IsNullOrWhiteSpace(locator.AssociationAssetID))
            throw new NotSupportedException("Edit-mode opacity is available only for an exact Classification map layer.");
        if (opacity is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(opacity), "Classification edit-mode opacity must be from 0 to 100.");
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(sourceRevision) ||
            sourceRevision.Length != 64 || !sourceRevision.All(Uri.IsHexDigit) ||
            !PathEquals(sourcePath, locator.AssociationAssetID))
            throw new SourceFileChangedException();

        CatalogTreeNode target = FindExactNode(BuildTree(request, map).Roots, locator);
        if (target.Checked != "checked")
            throw new NotSupportedException("Edit-mode opacity is available only for a visible Classification map layer.");
        ValidateResultOwner(request, locator);
        XElement? node = ResolveNode(map, locator, required: false);
        if (node is null)
        {
            // Only a visible row can be edited through the selected-component
            // inspector. Materialize its exact source chain without changing
            // any existing visibility choice.
            _ = SetSourceVisibility(request, map, locator, checkedValue: true);
            node = ResolveNode(map, locator, required: true) ?? throw new SourceFileChangedException();
        }

        string? savedRaw = (string?)node.Attribute("RenderInEditMode");
        bool savedIsValid = savedRaw is null ||
            (int.TryParse(savedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
             parsed is >= 0 and <= 100);
        int savedOpacity = savedRaw is null ? 50 :
            int.TryParse(savedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
                ? parsedValue : -1;
        if (savedIsValid && savedOpacity == opacity)
            return false;
        if (opacity == 50)
            node.Attribute("RenderInEditMode")?.Remove();
        else
            node.SetAttributeValue("RenderInEditMode", opacity.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static int? ReadClassificationEditModeOpacity(XElement node)
    {
        string? raw = (string?)node.Attribute("RenderInEditMode");
        if (raw is null) return 50;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
            value is >= 0 and <= 100 ? value : null;
    }

    private static string NormalizeResultMapType(string rawMapType) => rawMapType.Trim() switch
    {
        "WaterSurfaceElevation" or "Water Surface" => "Water Surface",
        "Depth" => "Depth",
        "Velocity" => "Velocity",
        "DepthTimesVelocity" or "D*V" => "D*V",
        "DepthTimesVelocitySquared" or "D*V^2" => "D*V^2",
        _ => throw new NotSupportedException("The selected Result map type is not supported by the current map renderer.")
    };

    private static void CollectActiveResultNodes(XElement node, bool ancestorsEnabled, List<XElement> results)
    {
        string type = (string?)node.Attribute("Type") ?? "";
        bool checkable = CheckableNodeTypes.Contains(type);
        bool enabled = ancestorsEnabled && (!checkable || ReadBool(node, "IsChecked"));
        if (!enabled) return;
        if (type is "Result" or "MultiResult") results.Add(node);
        foreach (XElement child in node.Elements("Node"))
            CollectActiveResultNodes(child, enabled, results);
    }

    private static bool AddGeoJSONVector(OperationContext context, XElement map, string? sourcePath,
        string? sourceRevision)
    {
        string path = ResolveGeoJSONSource(context, sourcePath, sourceRevision);
        string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
        var operation = GeoJsonReader.TrySort(path, out var buckets);
        if (!operation.Result || buckets is null)
            throw new ArgumentException("The source GeoJSON reader could not read this file.");

        var layerTypes = new List<(string Type, string DefaultName)>();
        if (buckets.Points.Count > 0) layerTypes.Add(("PointsVector", "Point Layer"));
        if (buckets.Polylines.Count > 0) layerTypes.Add(("PolylinesVector", "Polyline Layer"));
        if (buckets.Polygons.Count > 0) layerTypes.Add(("PolygonsVector", "Polygon Layer"));
        if (layerTypes.Count == 0) throw new ArgumentException("The source GeoJSON reader found no point, line, or polygon features.");

        string rootName = (string?)map.Attribute("Type") switch
        {
            "Project" => "Map Layers",
            "Buildable" or "Geometry" or "Plan" => "Layers",
            _ => throw new NotSupportedException("GeoJSON layers can be added only to Project, Buildable, Geometry and Plan maps.")
        };
        XElement root = EnsureRootNode(map, rootName);
        XElement parent = root;
        if (rootName == "Map Layers")
        {
            parent = root.Elements("Node").FirstOrDefault(node =>
                (string?)node.Attribute("Type") == "Folder" && (string?)node.Attribute("Name") == "Vectors")!;
            if (parent is null)
            {
                parent = new XElement("Node", new XAttribute("Type", "Folder"), new XAttribute("Name", "Vectors"));
                root.AddFirst(parent);
            }
        }

        string baseName = Path.GetFileNameWithoutExtension(path);
        var definitions = layerTypes.Count == 1
            ? layerTypes.Select(layer => (layer.Type, Name: baseName)).ToArray()
            : layerTypes.Select(layer => (layer.Type, Name: baseName + " (" + layer.DefaultName + ")")).ToArray();
        foreach ((string type, string name) in definitions)
        {
            bool duplicate = root.DescendantsAndSelf().Any(node =>
                (string?)node.Attribute("Type") == "MapLayer" &&
                (string?)node.Attribute("MapType") == type &&
                PathEquals(ResolveMapLayerPath(projectDirectory, (string?)node.Attribute("FilenameRelative"),
                    (string?)node.Attribute("Filename")) ?? "", path));
            if (duplicate)
                throw new ArgumentException("This GeoJSON layer is already present in the selected map.");
        }

        string relative = Path.GetRelativePath(projectDirectory, path);
        foreach ((string type, string name) in definitions)
        {
            parent.Add(new XElement("Node", new XAttribute("Type", "MapLayer"),
                new XAttribute("Name", name), new XAttribute("Filename", path),
                new XAttribute("FilenameRelative", relative), new XAttribute("MapType", type),
                new XAttribute("IsChecked", true)));
        }
        return true;
    }

    private static bool AddShapefileVector(OperationContext context, XElement map, string sourcePath,
        string mapType)
    {
        string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
        string rootName = (string?)map.Attribute("Type") switch
        {
            "Project" => "Map Layers",
            "Buildable" or "Geometry" or "Plan" => "Layers",
            _ => throw new NotSupportedException("Shapefile layers can be added only to Project, Buildable, Geometry and Plan maps.")
        };
        XElement root = EnsureRootNode(map, rootName);
        XElement parent = root;
        if (rootName == "Map Layers")
        {
            parent = root.Elements("Node").FirstOrDefault(node =>
                (string?)node.Attribute("Type") == "Folder" && (string?)node.Attribute("Name") == "Vectors")!;
            if (parent is null)
            {
                parent = new XElement("Node", new XAttribute("Type", "Folder"), new XAttribute("Name", "Vectors"));
                root.AddFirst(parent);
            }
        }

        bool duplicate = root.DescendantsAndSelf().Any(node =>
            (string?)node.Attribute("Type") == "MapLayer" &&
            (string?)node.Attribute("MapType") == mapType &&
            PathEquals(ResolveMapLayerPath(projectDirectory, (string?)node.Attribute("FilenameRelative"),
                (string?)node.Attribute("Filename")) ?? "", sourcePath));
        if (duplicate)
            throw new ArgumentException("This Shapefile layer is already present in the selected map.");

        string relative = Path.GetRelativePath(projectDirectory, sourcePath);
        parent.Add(new XElement("Node", new XAttribute("Type", "MapLayer"),
            new XAttribute("Name", Path.GetFileNameWithoutExtension(sourcePath)),
            new XAttribute("Filename", sourcePath), new XAttribute("FilenameRelative", relative),
            new XAttribute("MapType", mapType), new XAttribute("IsChecked", true)));
        return true;
    }

    private static bool RemoveSourceNode(OperationContext context, MapTreeRequest mapRequest, XElement map,
        CatalogTreeNodeLocator locator,
        string? sourcePath, string? sourceRevision)
    {
        if (locator.NodePath is not { Length: > 0 })
            throw new SourceFileChangedException();
        XElement node = ResolveNode(map, locator, required: true)!;
        string type = (string?)map.Attribute("Type") ?? "";
        switch (locator.NodeType)
        {
            case "Geometry":
            {
                if (type is not ("Buildable" or "Geometry"))
                    throw new NotSupportedException("Project map Geometry rows are included automatically and cannot be removed from the map tree.");
                string expectedRoot = GeometryRootNameForMap(type);
                if (locator.NodePath is not { Length: >= 2 } ||
                    !string.Equals(locator.RootName, expectedRoot, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                ProjectGeometryIdentity geometry = ResolveProjectGeometry(context, sourcePath, sourceRevision);
                if (locator.AssetID is not { Length: > 0 } assetID ||
                    !PathEquals(assetID, geometry.AssetPath) ||
                    !string.Equals(locator.NodeName, geometry.Name, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                XElement? root = FindUniqueRoot(map, expectedRoot);
                if (root is null || !node.AncestorsAndSelf("Node").Any(ancestor => ReferenceEquals(ancestor, root)))
                    throw new SourceFileChangedException();
                Dictionary<string, XElement>? associationStates = type == "Geometry"
                    ? CaptureGeometryAssociationStates(context, map) : null;
                node.Remove();
                if (type == "Geometry") SynchronizeGeometryMapRoots(context, map, associationStates);
                _ = ResolveProjectGeometry(context, geometry.AssetPath, geometry.Revision);
                return true;
            }
            case "Plan":
            {
                if (type != "Plan" || locator.ParentResultNodeType is not null ||
                    locator.ParentResultAssetID is not null || locator.ParentResultNodePath is not null ||
                    locator.ParentResultNodeName is not null || locator.ParentResultRevision is not null ||
                    locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                    locator.ParentPlanNodeName is not null ||
                    !string.Equals(locator.RootName, "Focused Plans", StringComparison.Ordinal) ||
                    locator.NodePath is not { Length: >= 2 })
                    throw new SourceFileChangedException();
                ProjectPlanIdentity plan = ResolveProjectPlan(context, sourcePath, sourceRevision);
                if (locator.AssetID is not { Length: > 0 } assetID ||
                    !PathEquals(assetID, plan.AssetPath) ||
                    !string.Equals(locator.NodeName, plan.Name, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                XElement? root = FindUniqueRoot(map, "Focused Plans");
                if (root is null || !node.AncestorsAndSelf("Node").Any(ancestor => ReferenceEquals(ancestor, root)))
                    throw new SourceFileChangedException();
                Dictionary<string, XElement> associationStates = CapturePlanAssociationStates(context, map, mapRequest);
                node.Remove();
                SynchronizePlanMapRoots(context, map, associationStates, mapRequest.ResultSelectionOverrides);
                _ = ResolveProjectPlan(context, plan.AssetPath, plan.Revision);
                return true;
            }
            case "MapLayer":
            {
                string mapType = (string?)node.Attribute("MapType") ?? "";
                bool isRaster = MapRasterLayerRendering.SupportedTypes.Contains(mapType, StringComparer.Ordinal);
                if (!isRaster && !SupportedGeoJsonVectorTypes.Contains(mapType))
                    throw new NotSupportedException("This source map layer type does not support removal through this action.");
                if (isRaster && node.Parent is { } rasterParent &&
                    (string?)rasterParent.Attribute("Type") == "GdbDataset")
                {
                    VerifyGdbRasterTreeNode(mapRequest, locator, node, rasterParent,
                        sourcePath, sourceRevision);
                    node.Remove();
                    return true;
                }
                string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
                string expectedPath = isRaster
                    ? ResolveRasterMapLayerSource(context, sourcePath, sourceRevision)
                    : ResolveGeoJSONSource(context, sourcePath, sourceRevision);
                string? serializedPath = ResolveMapLayerPath(projectDirectory,
                    (string?)node.Attribute("FilenameRelative"), (string?)node.Attribute("Filename"));
                if (serializedPath is null || !PathEquals(serializedPath, expectedPath) ||
                    !PathEquals(locator.AssetID ?? expectedPath, expectedPath) ||
                    locator.MapType != mapType)
                    throw new SourceFileChangedException();
                break;
            }
            case "GdbDataset":
            case "GdbVectorLayer":
            {
                if (locator.MapType != locator.NodeType ||
                    locator.NodeType is not ("GdbDataset" or "GdbVectorLayer"))
                    throw new SourceFileChangedException();
                GdbMapSourceIdentity source = GdbMapSourceService.CaptureSource(
                    sourcePath ?? throw new ArgumentException("The selected GDB source path is missing."));
                if (sourceRevision is null ||
                    !string.Equals(source.Revision, sourceRevision, StringComparison.OrdinalIgnoreCase) ||
                    locator.AssetID is not { Length: > 0 } assetID || !PathEquals(assetID, source.SourcePath) ||
                    !PathEquals(ResolveGdbMapLayerPath(Path.GetDirectoryName(context.ProjectAliasPath)!,
                            (string?)node.Attribute("FilenameRelative"), (string?)node.Attribute("Filename")) ?? "",
                        source.SourcePath))
                    throw new SourceFileChangedException();
                if (locator.NodeType == "GdbVectorLayer")
                {
                    MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedGdbNode(mapRequest, locator);
                    if (!PathEquals(location.SourcePath, source.SourcePath) ||
                        !string.Equals(location.GdbVectorType, (string?)node.Attribute("GdbVectorType"), StringComparison.Ordinal))
                        throw new SourceFileChangedException();

                    GdbMapCatalogResponse catalog = GdbMapSourceService.Catalog(
                        new(source.SourcePath, source.Revision));
                    GdbMapLayerDescriptor[] layers = catalog.Layers.Where(layer =>
                        string.Equals(layer.Name, location.GdbLayerName, StringComparison.Ordinal)).ToArray();
                    if (!SameGdbSource(source, catalog.Source) || layers.Length != 1 ||
                        !string.Equals(layers[0].VectorType, location.GdbVectorType, StringComparison.Ordinal))
                        throw new SourceFileChangedException();
                }
                else
                {
                    GdbMapCatalogResponse catalog = GdbMapSourceService.Catalog(
                        new(source.SourcePath, source.Revision));
                    if (!SameGdbSource(source, catalog.Source)) throw new SourceFileChangedException();
                    VerifyGdbDatasetChildren(node, source, catalog);
                }
                break;
            }
            case "Result":
            {
                if (type is not ("Buildable" or "Geometry") ||
                    !string.Equals(locator.RootName, "Layers", StringComparison.Ordinal) ||
                    locator.NodePath is not { Length: >= 2 })
                    throw new NotSupportedException("Project map results are included automatically and cannot be removed from the map tree.");
                ProjectResultIdentity result = ReadProjectResult(context, sourcePath, sourceRevision);
                if (result.PlanName is not null || result.SameNameCount != 1 || result.Name != locator.NodeName ||
                    locator.AssetID is not { Length: > 0 } resultAsset || !PathEquals(resultAsset, sourcePath!))
                    throw new SourceFileChangedException();
                break;
            }
            case "MultiResult":
                if (type is not ("Buildable" or "Geometry") ||
                    !string.Equals(locator.RootName, "Layers", StringComparison.Ordinal) ||
                    locator.NodePath is not { Length: >= 2 })
                    throw new NotSupportedException("Project map result groups are included automatically and cannot be removed from the map tree.");
                // The source .vis schema keys a MultiResult by Plan.Name and
                // cannot express membership of one timeline Result within it.
                // Removing this exact node therefore removes the whole group.
                ProjectPlanIdentity resultPlan = ResolveProjectPlan(context, sourcePath, sourceRevision);
                if (!string.Equals(resultPlan.Name, locator.NodeName, StringComparison.Ordinal) ||
                    locator.AssetID is not { Length: > 0 } planAsset || !PathEquals(planAsset, resultPlan.AssetPath))
                    throw new SourceFileChangedException();
                break;
            default:
                throw new NotSupportedException("This source map node does not support removal through the native map layer action.");
        }
        node.Remove();
        return true;
    }

    private static ProjectResultIdentity ReadProjectResult(OperationContext context, string? sourcePath,
        string? sourceRevision)
    {
        string path;
        if (sourcePath is not null && sourceRevision is not null &&
            (context.CoreLayerContext?.SessionHdfReferences ?? []).Any(reference =>
                reference.Kind == "result" && PathEquals(reference.SourcePath, sourcePath) &&
                reference.SourceRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase)))
        {
            SessionHdfReferenceCapture capture = ResolveSessionHdfReference(context, sourcePath,
                sourceRevision, expectedKind: "result");
            path = SourceFilePaths.Canonical(capture.SourcePath);
        }
        else
        {
            string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
            path = ResolveProjectOwnedFile(context, sourcePath, sourceRevision,
                Path.Combine(projectDirectory, "Results"), allowedExtensions: [".h5", ".hdf"]);
        }
        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            Result[] matches = project.Results.Where(result =>
                PathEquals(ProjectAssetPath(project, result.Filename), path)).ToArray();
            if (matches.Length != 1) throw new ArgumentException("The selected HDF file is not one unique Result owned by the current project.");
            Result selected = matches[0];
            string? planName = selected.Plan?.Name;
            Plan[] sourcePlans = planName is null ? [] : project.Plans.Where(plan =>
                string.Equals(plan.Name, planName, StringComparison.Ordinal)).ToArray();
            string? planAssetPath = sourcePlans.Length == 1
                ? ProjectPlanAssetPath(project, sourcePlans[0].Filename)
                : null;
            return new ProjectResultIdentity(selected.Name, planName, planAssetPath,
                project.Results.Count(result => string.Equals(result.Name, selected.Name, StringComparison.Ordinal)),
                sourcePlans.Length);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string ResolveProjectOwnedFile(OperationContext context, string? sourcePath,
        string? sourceRevision, string ownerDirectory, string[] allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected source file must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected source file revision is required.");
        string canonicalPath = CanonicalPath(sourcePath);
        string canonicalOwner = CanonicalPath(ownerDirectory);
        if (!IsWithinDirectory(canonicalPath, canonicalOwner))
            throw new ArgumentException("The selected source file is outside the current project's supported source directory.");
        if (!allowedExtensions.Contains(Path.GetExtension(canonicalPath), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("The selected source file format is not supported by this map action.");
        if (!File.Exists(canonicalPath)) throw new FileNotFoundException("The selected source file was not found.", canonicalPath);
        if (!RevisionFile(canonicalPath).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return canonicalPath;
    }

    private static string ResolveGeoJSONSource(OperationContext context, string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected source file must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected source file revision is required.");

        string sourceDisplayPath = Path.GetFullPath(sourcePath);
        string canonicalPath = CanonicalPath(sourceDisplayPath);
        if (!new[] { ".json", ".geojson" }.Contains(Path.GetExtension(sourceDisplayPath), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("The selected source file format is not supported by this map action.");
        if (!File.Exists(sourceDisplayPath)) throw new FileNotFoundException("The selected source file was not found.", sourceDisplayPath);

        string projectDirectory = CanonicalPath(Path.GetDirectoryName(context.ProjectAliasPath)!);
        bool projectOwned = IsWithinDirectory(canonicalPath, projectDirectory);
        if (!projectOwned && !IsExternalConnectionFile(context, canonicalPath))
            throw new ArgumentException("The selected source file is outside the current project's supported source directory.");
        if (!RevisionFile(canonicalPath).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return sourceDisplayPath;
    }

    private static string ResolveShapefileSource(OperationContext context, string? sourcePath,
        MapSourceFamilyCapture expectedFamily)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Shapefile must use an absolute path.");
        string sourceDisplayPath = Path.GetFullPath(sourcePath);
        string canonicalPath = CanonicalPath(sourceDisplayPath);
        if (!string.Equals(Path.GetExtension(sourceDisplayPath), ".shp", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Choose a Shapefile (.shp) source file.");
        if (!File.Exists(sourceDisplayPath)) throw new FileNotFoundException("The selected Shapefile was not found.", sourceDisplayPath);

        string projectDirectory = CanonicalPath(Path.GetDirectoryName(context.ProjectAliasPath)!);
        if (!IsWithinDirectory(canonicalPath, projectDirectory) && !IsExternalConnectionFile(context, canonicalPath))
            throw new ArgumentException("The selected Shapefile is outside the current project's supported source directory.");
        MapShapefileImportService.VerifyExpectedFamily(expectedFamily, sourceDisplayPath);
        return sourceDisplayPath;
    }

    private static bool IsExternalConnectionFile(OperationContext context, string canonicalPath)
    {
        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {

            foreach (ExternalConnectionBase connection in project.ConnectionSource.Connections)
            {
                switch (connection)
                {
                    case FileExternalConnection file when file.TryGetFile(out string connectedFile) &&
                        PathEquals(connectedFile, canonicalPath):
                        return true;
                    case DirectoryExternalConnection directory when directory.TryGetFiles(out string[] files) &&
                        files.Any(path => PathEquals(path, canonicalPath)):
                        return true;
                }
            }
            return false;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static bool IsWithinDirectory(string candidate, string directory)
    {
        string relative = Path.GetRelativePath(directory, candidate);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ProjectAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return CanonicalPath(path);
    }

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
            .Concat(project.ClassificationLayers)) layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private sealed record ProjectResultIdentity(string Name, string? PlanName, string? PlanAssetPath,
        int SameNameCount, int SamePlanNameCount);

    private sealed record ProjectPlanIdentity(string Name, string AssetPath, string Revision);

    private sealed record ProjectTerrainIdentity(string Name, string AssetPath, string Revision);

    private sealed record ProjectGeometryIdentity(string Name, string AssetPath, string Revision);
    private sealed record ProjectBoundaryConditionIdentity(string Name, string AssetPath, string Revision);

    private sealed record GeometryMapRootElements(XElement? Focused, XElement? Matching, XElement Layers);
    private sealed record PlanMapRootElements(XElement Focused, XElement Matching, XElement Layers);

    private static GeometryMapRootElements EnsureGeometryMapRoots(XElement map)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType == "Buildable")
            return new(null, null, EnsureUniqueRoot(EnsureTreeNodes(map), "Layers"));
        if (mapType != "Geometry")
            throw new NotSupportedException("Geometry source roots are available only on Buildable and Geometry maps.");

        XElement nodes = EnsureTreeNodes(map);
        XElement focused = EnsureUniqueRoot(nodes, "Focused Geometries");
        XElement matching = EnsureUniqueRoot(nodes, "Matching Layers");
        XElement layers = EnsureUniqueRoot(nodes, "Layers");
        // Retained GeometryTreeModeVM presents these roots in this order.
        // Existing unknown roots and their relative order remain untouched.
        return new(focused, matching, layers);
    }

    private static PlanMapRootElements EnsurePlanMapRoots(XElement map)
    {
        if ((string?)map.Attribute("Type") != "Plan")
            throw new NotSupportedException("Plan source roots are available only on Plan maps.");
        XElement nodes = EnsureTreeNodes(map);
        XElement focused = EnsureUniqueRoot(nodes, "Focused Plans");
        XElement matching = EnsureUniqueRoot(nodes, "Matching Layers");
        XElement layers = EnsureUniqueRoot(nodes, "Layers");

        // PlanTreeModeVM prepends Focused Plans and Matching Layers to the
        // Buildable base's Layers root. Keep these owned roots in source order;
        // unrelated root elements retain their relative order behind them.
        foreach (XElement root in new[] { layers, matching, focused })
        {
            root.Remove();
            nodes.AddFirst(root);
        }
        return new(focused, matching, layers);
    }

    private static XElement EnsureUniqueRoot(XElement nodes, string name)
    {
        XElement[] matches = nodes.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == "Root" &&
            string.Equals((string?)node.Attribute("Name"), name, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        return matches.SingleOrDefault() ?? AddRootNode(nodes, name);
    }

    private static ProjectGeometryIdentity ResolveProjectGeometry(OperationContext context,
        string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Geometry source must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected Geometry source revision is required.");

        string displayPath = Path.GetFullPath(sourcePath);
        string canonicalPath = CanonicalPath(displayPath);
        if (!File.Exists(canonicalPath)) throw new FileNotFoundException("The selected Geometry source was not found.", displayPath);
        string actualRevision = RevisionFile(canonicalPath);
        if (!actualRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            Geometry[] matches = project.Geometries.Where(geometry =>
                PathEquals(ProjectGeometryAssetPath(project, geometry.Filename), displayPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected source file is not one unique Geometry owned by the current project.");

            Geometry selected = matches[0];
            if (project.Geometries.Count(geometry =>
                    string.Equals(geometry.Name, selected.Name, StringComparison.Ordinal)) != 1)
                throw new NotSupportedException("The current project has Geometry sources with the same serialized name; the saved map cannot distinguish them safely.");
            string assetPath = ProjectGeometryAssetPath(project, selected.Filename);
            if (!PathEquals(assetPath, displayPath) ||
                !RevisionFile(CanonicalPath(assetPath)).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return new(selected.Name, assetPath, sourceRevision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static ProjectBoundaryConditionIdentity ResolveProjectBoundaryCondition(OperationContext context,
        string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Boundary Condition source must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected Boundary Condition source revision is required.");

        string displayPath = Path.GetFullPath(sourcePath);
        string canonicalPath = CanonicalPath(displayPath);
        if (!File.Exists(canonicalPath))
            throw new FileNotFoundException("The selected Boundary Condition source was not found.", displayPath);
        if (!RevisionFile(canonicalPath).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            BoundaryCondition[] matches = project.BoundaryConditions.Where(condition =>
                PathEquals(ProjectAssetPath(project, condition.Filename), displayPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected source file is not one unique Boundary Condition owned by the current project session.");

            BoundaryCondition selected = matches[0];
            if (project.BoundaryConditions.Count(condition =>
                    string.Equals(condition.Name, selected.Name, StringComparison.Ordinal)) != 1)
                throw new NotSupportedException("The current project has Boundary Conditions with the same serialized name; the saved map cannot distinguish them safely.");
            string assetPath = ProjectAssetPath(project, selected.Filename);
            if (!PathEquals(assetPath, displayPath) ||
                !RevisionFile(CanonicalPath(assetPath)).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return new(selected.Name, assetPath, sourceRevision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static ProjectPlanIdentity ResolveProjectPlan(OperationContext context,
        string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Plan source must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected Plan source revision is required.");

        string displayPath = Path.GetFullPath(sourcePath);
        string canonicalPath = CanonicalPath(displayPath);
        if (!File.Exists(canonicalPath)) throw new FileNotFoundException("The selected Plan source was not found.", displayPath);
        if (!RevisionFile(canonicalPath).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            Plan[] matches = project.Plans.Where(plan =>
                PathEquals(ProjectPlanAssetPath(project, plan.Filename), displayPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected source file is not one unique Plan owned by the current project.");

            Plan selected = matches[0];
            if (project.Plans.Count(plan => string.Equals(plan.Name, selected.Name, StringComparison.Ordinal)) != 1)
                throw new NotSupportedException("The current project has Plans with the same serialized name; the saved map cannot distinguish them safely.");
            string assetPath = ProjectPlanAssetPath(project, selected.Filename);
            if (!PathEquals(assetPath, displayPath) ||
                !RevisionFile(CanonicalPath(assetPath)).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return new(selected.Name, assetPath, sourceRevision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static ProjectTerrainIdentity ResolveProjectTerrain(OperationContext context,
        string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Terrain source must use an absolute path.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The selected Terrain source revision is required.");
        SessionHdfReferenceCapture capture = ResolveSessionHdfReference(context, sourcePath,
            sourceRevision, expectedKind: "terrain");
        string displayPath = Path.GetFullPath(capture.SourcePath);
        string canonicalPath = SourceFilePaths.Canonical(displayPath);
        if (!RevisionFile(canonicalPath).Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            Terrain[] matches = project.Terrains.Where(terrain =>
                PathEquals(ProjectAssetPath(project, terrain.Filename), displayPath)).ToArray();
            if (matches.Length != 1 || !string.Equals(matches[0].Name, capture.Name, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            if (project.Terrains.Count(terrain =>
                    string.Equals(terrain.Name, matches[0].Name, StringComparison.Ordinal)) != 1)
                throw new NotSupportedException("The current project has Terrains with the same serialized name; the saved map cannot distinguish them safely.");
            string assetPath = ProjectAssetPath(project, matches[0].Filename);
            if (!PathEquals(assetPath, canonicalPath) ||
                !RevisionFile(canonicalPath).Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return new(matches[0].Name, assetPath, capture.SourceRevision);
        }
        finally { DisposeProject(project); }
    }

    private static ProjectPlanIdentity ResolveProjectPlanByName(OperationContext context, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new SourceFileChangedException();
        var project = CoreLayerImportService.OpenProject(context.ProjectAliasPath, context.ProjectXml,
            context.CoreLayerContext);
        try
        {
            Plan[] matches = project.Plans.Where(plan => string.Equals(plan.Name, name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            string assetPath = ProjectPlanAssetPath(project, matches[0].Filename);
            string revision = RevisionFile(CanonicalPath(assetPath));
            return new(matches[0].Name, assetPath, revision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string ProjectPlanAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return Path.GetFullPath(path);
    }

    private static string[] SerializedPlanAssetIDs(string projectPath, string? projectXml,
        XElement map, string rootName, CoreLayerRequestContext? coreLayerContext = null)
    {
        XElement nodes = map.Element("Tree")?.Element("Nodes") ?? new XElement("Nodes");
        XElement[] roots = nodes.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == "Root" &&
            string.Equals((string?)node.Attribute("Name"), rootName, StringComparison.Ordinal)).ToArray();
        if (roots.Length > 1) throw new SourceFileChangedException();
        if (roots.Length == 0) return [];
        XElement[] serializedPlans = TreeRows(roots[0]).Where(node => (string?)node.Attribute("Type") == "Plan").ToArray();
        string[] names = serializedPlans.Select(node => (string?)node.Attribute("Name") ?? "").ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml,
            coreLayerContext);
        try
        {
            var result = new List<string>(names.Length);
            foreach (string name in names)
            {
                Plan[] matches = project.Plans.Where(plan => string.Equals(plan.Name, name, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                result.Add(ProjectPlanAssetPath(project, matches[0].Filename));
            }
            return result.ToArray();
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string ProjectGeometryAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return Path.GetFullPath(path);
    }

    private static string[] SerializedGeometryAssetIDs(MapTreeRequest request, XElement map, string rootName)
        => SerializedGeometryAssetIDs(request.ProjectPath, request.ProjectXml, map, rootName,
            request.ProjectSessionID, request.ExternalCoreLayerReferences, request.CoreLayerContext);

    private static string[] SerializedGeometryAssetIDs(OperationContext context, XElement map, string rootName)
        => SerializedGeometryAssetIDs(context.ProjectAliasPath, context.ProjectXml, map, rootName,
            context.ProjectSessionID, context.ExternalCoreLayerReferences, context.CoreLayerContext);

    private static string[] SerializedBoundaryConditionAssetIDs(MapTreeRequest request, XElement map,
        string rootName)
    {
        XElement nodes = map.Element("Tree")?.Element("Nodes") ?? new XElement("Nodes");
        XElement[] roots = nodes.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == "Root" &&
            string.Equals((string?)node.Attribute("Name"), rootName, StringComparison.Ordinal)).ToArray();
        if (roots.Length > 1) throw new SourceFileChangedException();
        if (roots.Length == 0) return [];
        XElement[] serialized = TreeRows(roots[0]).Where(node =>
            (string?)node.Attribute("Type") == "BoundaryCondition").ToArray();
        if (serialized.Length == 0) return [];
        string[] names = serialized.Select(node => (string?)node.Attribute("Name") ?? "").ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(request.ProjectPath, request.ProjectXml,
            request.CoreLayerContext);
        try
        {
            var result = new List<string>(names.Length);
            foreach (string name in names)
            {
                BoundaryCondition[] matches = project.BoundaryConditions.Where(condition =>
                    string.Equals(condition.Name, name, StringComparison.Ordinal)).ToArray();
                if (matches.Length > 1) throw new SourceFileChangedException();
                if (matches.Length == 1)
                    result.Add(ProjectAssetPath(project, matches[0].Filename));
            }
            return result.ToArray();
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string[] SerializedGeometryAssetIDs(string projectPath, string? projectXml,
        XElement map, string rootName, string? projectSessionID = null,
        CoreLayerReferenceCapture[]? externalCoreLayerReferences = null,
        CoreLayerRequestContext? coreLayerContext = null)
    {
        XElement nodes = map.Element("Tree")?.Element("Nodes") ?? new XElement("Nodes");
        XElement[] roots = nodes.Elements("Node").Where(node =>
            (string?)node.Attribute("Type") == "Root" &&
            string.Equals((string?)node.Attribute("Name"), rootName, StringComparison.Ordinal)).ToArray();
        if (roots.Length > 1) throw new SourceFileChangedException();
        if (roots.Length == 0) return [];

        XElement[] serializedGeometry = TreeRows(roots[0]).Where(node =>
            (string?)node.Attribute("Type") == "Geometry").ToArray();
        if (serializedGeometry.Length == 0) return [];
        string[] names = serializedGeometry.Select(node => (string?)node.Attribute("Name") ?? "").ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new SourceFileChangedException();

        var project = CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml,
            coreLayerContext);
        try
        {
            var result = new List<string>(names.Length);
            foreach (string name in names)
            {
                Geometry[] matches = project.Geometries.Where(geometry =>
                    string.Equals(geometry.Name, name, StringComparison.Ordinal)).ToArray();
                if (matches.Length > 1) throw new SourceFileChangedException();
                if (matches.Length == 1)
                    result.Add(ProjectGeometryAssetPath(project, matches[0].Filename));
            }
            return result.ToArray();
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static IEnumerable<XElement> TreeRows(XElement root)
    {
        foreach (XElement child in root.Elements("Node"))
        {
            yield return child;
            if ((string?)child.Attribute("Type") == "Folder")
                foreach (XElement descendant in TreeRows(child)) yield return descendant;
        }
    }

    private static Dictionary<string, XElement> CaptureGeometryAssociationStates(OperationContext context, XElement map)
    {
        string[] focusedIDs = SerializedGeometryAssetIDs(context, map, "Focused Geometries");
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, FocusedGeometryIds: focusedIDs,
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), "Geometry");
        var states = new Dictionary<string, XElement>(StringComparer.Ordinal);
        CatalogTreeNode focusedRoot = defaults.Single(node => node.Name == "Focused Geometries");
        XElement? focusedElement = FindUniqueRoot(map, "Focused Geometries");
        if (focusedElement is not null)
        {
            foreach (CatalogTreeNode geometry in focusedRoot.Children ?? [])
            {
                XElement[] geometryMatches = TreeRows(focusedElement).Where(node =>
                    (string?)node.Attribute("Type") == "Geometry" &&
                    string.Equals((string?)node.Attribute("Name"), geometry.Name, StringComparison.Ordinal)).ToArray();
                if (geometryMatches.Length > 1) throw new SourceFileChangedException();
                XElement? geometryElement = geometryMatches.SingleOrDefault();
                if (geometryElement is null) continue;
                foreach (CatalogTreeNode association in geometry.Children ?? [])
                {
                    CatalogTreeNodeLocator? locator = association.Locator;
                    if (locator?.AssociationKind is not { Length: > 0 } kind ||
                        locator.AssociationAssetID is not { Length: > 0 } assetID) continue;
                    XElement[] saved = geometryElement.Elements("Node").Where(node =>
                        (string?)node.Attribute("Type") == kind &&
                        string.Equals((string?)node.Attribute("Name"), association.Name, StringComparison.Ordinal)).ToArray();
                    AddAssociationState(states, kind, assetID, saved.SingleOrDefault(), saved.Length);
                }
            }
        }

        CatalogTreeNode matchingRoot = defaults.Single(node => node.Name == "Matching Layers");
        XElement? matchingElement = FindUniqueRoot(map, "Matching Layers");
        if (matchingElement is not null)
        {
            foreach (CatalogTreeNode association in matchingRoot.Children ?? [])
            {
                CatalogTreeNodeLocator? locator = association.Locator;
                if (locator?.AssociationKind is not { Length: > 0 } kind ||
                    locator.AssociationAssetID is not { Length: > 0 } assetID) continue;
                XElement[] saved = TreeRows(matchingElement).Where(node =>
                    (string?)node.Attribute("Type") == kind &&
                    string.Equals((string?)node.Attribute("Name"), association.Name, StringComparison.Ordinal)).ToArray();
                AddAssociationState(states, kind, assetID, saved.SingleOrDefault(), saved.Length);
            }
        }
        return states;
    }

    private static Dictionary<string, XElement> CapturePlanAssociationStates(OperationContext context, XElement map,
        MapTreeRequest? request = null)
    {
        string[] focusedIDs = SerializedPlanAssetIDs(context.ProjectAliasPath, context.ProjectXml,
            map, "Focused Plans", context.CoreLayerContext);
        CatalogTreeNode[] defaults = PreparedPlanDefaults(context, focusedIDs, request?.ResultSelectionOverrides);
        var states = new Dictionary<string, XElement>(StringComparer.Ordinal);
        CatalogTreeNode focusedRoot = defaults.Single(node => node.Name == "Focused Plans");
        XElement? focusedElement = FindUniqueRoot(map, "Focused Plans");
        if (focusedElement is not null)
        {
            foreach (CatalogTreeNode plan in focusedRoot.Children ?? [])
            {
                XElement[] planRows = TreeRows(focusedElement).Where(node =>
                    (string?)node.Attribute("Type") == "Plan" &&
                    string.Equals((string?)node.Attribute("Name"), plan.Name, StringComparison.Ordinal)).ToArray();
                if (planRows.Length != 1) throw new SourceFileChangedException();
                XElement planElement = planRows[0];
                foreach (CatalogTreeNode association in plan.Children ?? [])
                {
                    CatalogTreeNodeLocator? locator = association.Locator;
                    if (locator?.AssociationKind is not { Length: > 0 } kind ||
                        locator.AssociationAssetID is not { Length: > 0 } assetID) continue;
                    XElement[] saved = planElement.Elements("Node").Where(node =>
                        (string?)node.Attribute("Type") == kind &&
                        string.Equals((string?)node.Attribute("Name"), association.Name, StringComparison.Ordinal)).ToArray();
                    AddAssociationState(states, kind, assetID, saved.SingleOrDefault(), saved.Length);
                }
            }
        }

        CatalogTreeNode matchingRoot = defaults.Single(node => node.Name == "Matching Layers");
        XElement? matchingElement = FindUniqueRoot(map, "Matching Layers");
        if (matchingElement is not null)
        {
            foreach (CatalogTreeNode association in matchingRoot.Children ?? [])
            {
                CatalogTreeNodeLocator? locator = association.Locator;
                if (locator?.AssociationKind is not { Length: > 0 } kind ||
                    locator.AssociationAssetID is not { Length: > 0 } assetID) continue;
                XElement[] saved = TreeRows(matchingElement).Where(node =>
                    (string?)node.Attribute("Type") == kind &&
                    string.Equals((string?)node.Attribute("Name"), association.Name, StringComparison.Ordinal)).ToArray();
                AddAssociationState(states, kind, assetID, saved.SingleOrDefault(), saved.Length);
            }
        }
        return states;
    }

    private static void SynchronizePlanMapRoots(OperationContext context, XElement map,
        IReadOnlyDictionary<string, XElement>? associationStates,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        PlanMapRootElements roots = EnsurePlanMapRoots(map);
        string[] focusedIDs = SerializedPlanAssetIDs(context.ProjectAliasPath, context.ProjectXml,
            map, "Focused Plans", context.CoreLayerContext);
        CatalogTreeNode[] defaults = PreparedPlanDefaults(context, focusedIDs, resultSelectionOverrides);
        CatalogTreeNode focusedDefault = defaults.Single(node => node.Name == "Focused Plans");
        foreach (CatalogTreeNode plan in focusedDefault.Children ?? [])
        {
            XElement[] matches = TreeRows(roots.Focused).Where(node =>
                (string?)node.Attribute("Type") == "Plan" &&
                string.Equals((string?)node.Attribute("Name"), plan.Name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            XElement old = matches[0];
            XElement updated = SerializeSourceDefault(plan, old, associationStates);
            old.ReplaceWith(updated);
        }

        RemovePlanAssociationRows(roots.Matching);
        CatalogTreeNode matchingDefault = defaults.Single(node => node.Name == "Matching Layers");
        foreach (CatalogTreeNode association in matchingDefault.Children ?? [])
            roots.Matching.Add(SerializeSourceDefault(association, existing: null, associationStates));
    }

    private static void RemovePlanAssociationRows(XElement parent)
    {
        foreach (XElement child in parent.Elements("Node").ToArray())
        {
            string? type = (string?)child.Attribute("Type");
            if (type is "Geometry" or "Terrain" or "NValue" or "Classification" or "BoundaryCondition")
            {
                child.Remove();
                continue;
            }
            if (type == "Folder") RemovePlanAssociationRows(child);
        }
    }

    private static void AddAssociationState(Dictionary<string, XElement> states, string kind,
        string assetID, XElement? saved, int matchCount)
    {
        if (matchCount > 1) throw new SourceFileChangedException();
        if (saved is null) return;
        string key = AssociationStateKey(kind, assetID);
        if (!states.TryAdd(key, new XElement(saved))) throw new SourceFileChangedException();
    }

    private static string AssociationStateKey(string kind, string assetID)
    {
        string path = CanonicalPath(assetID);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return kind + "\n" + path;
    }

    private static void SynchronizeGeometryMapRoots(OperationContext context, XElement map,
        IReadOnlyDictionary<string, XElement>? associationStates)
    {
        GeometryMapRootElements roots = EnsureGeometryMapRoots(map);
        XElement focused = roots.Focused ?? throw new SourceFileChangedException();
        XElement matching = roots.Matching ?? throw new SourceFileChangedException();
        string[] focusedIDs = SerializedGeometryAssetIDs(context, map, "Focused Geometries");
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            context.ProjectAliasPath, context.ProjectXml, FocusedGeometryIds: focusedIDs,
            ProjectSessionID: context.ProjectSessionID,
            ExternalCoreLayerReferences: context.ExternalCoreLayerReferences,
            CoreLayerContext: context.CoreLayerContext), "Geometry");
        CatalogTreeNode defaultFocused = defaults.Single(node => node.Name == "Focused Geometries");
        foreach (CatalogTreeNode geometry in defaultFocused.Children ?? [])
        {
            XElement[] serialized = TreeRows(focused).Where(node =>
                (string?)node.Attribute("Type") == "Geometry" &&
                string.Equals((string?)node.Attribute("Name"), geometry.Name, StringComparison.Ordinal)).ToArray();
            if (serialized.Length > 1) throw new SourceFileChangedException();
            XElement? old = serialized.SingleOrDefault();
            if (old is null) continue;
            XElement updated = SerializeSourceDefault(geometry, old, associationStates);
            old.ReplaceWith(updated);
        }

        // Matching Layers is derived from the exact Geometry associations.
        // Remove its prior source-owned rows, retaining folders and unknown XML;
        // then rebuild in source order and reapply only identity-matched states.
        RemoveAssociationRows(matching);
        CatalogTreeNode defaultMatching = defaults.Single(node => node.Name == "Matching Layers");
        foreach (CatalogTreeNode association in defaultMatching.Children ?? [])
            matching.Add(SerializeSourceDefault(association, existing: null, associationStates));
    }

    private static void RemoveAssociationRows(XElement parent)
    {
        foreach (XElement child in parent.Elements("Node").ToArray())
        {
            string? type = (string?)child.Attribute("Type");
            if (type is "Terrain" or "NValue" or "Classification")
            {
                child.Remove();
                continue;
            }
            if (type == "Folder") RemoveAssociationRows(child);
        }
    }

    private static XElement? FindUniqueRoot(XElement map, string name)
    {
        XElement[] matches = (map.Element("Tree")?.Element("Nodes")?.Elements("Node") ?? [])
            .Where(node => (string?)node.Attribute("Type") == "Root" &&
                string.Equals((string?)node.Attribute("Name"), name, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        return matches.SingleOrDefault();
    }

    private static (CatalogTreeNode[] Roots, string[] ExpandedNodeIDs, string? SelectedNodeID, string[] HiddenLayerIDs) BuildTree(
        MapTreeRequest request, XElement map)
    {
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.ProjectSessionID,
            externalCoreLayerReferences: request.ExternalCoreLayerReferences,
            coreLayerContext: request.CoreLayerContext);
        var gdbRasterCatalogs = new MapTreeGdbCatalogs();
        string[] focusedGeometryIDs = request.MapType switch
        {
            "Buildable" => SerializedGeometryAssetIDs(request, map, "Layers"),
            "Geometry" => SerializedGeometryAssetIDs(request, map, "Focused Geometries"),
            "Plan" => SerializedGeometryAssetIDs(request, map, "Layers"),
            _ => []
        };
        string[] focusedPlanIDs = request.MapType == "Plan"
            ? SerializedPlanAssetIDs(request.ProjectPath, request.ProjectXml, map, "Focused Plans",
                request.CoreLayerContext)
            : [];
        string[] focusedBoundaryConditionIDs = request.MapType is "Buildable" or "Geometry" or "Plan"
            ? SerializedBoundaryConditionAssetIDs(request, map, "Layers")
            : [];
        var defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            request.ProjectPath, request.ProjectXml, FocusedGeometryIds: focusedGeometryIDs,
            FocusedPlanIds: focusedPlanIDs, FocusedBoundaryConditionIds: focusedBoundaryConditionIDs,
            ProjectSessionID: request.ProjectSessionID,
            ExternalCoreLayerReferences: request.ExternalCoreLayerReferences,
            CoreLayerContext: request.CoreLayerContext), request.MapType,
            request.ComputeReadLease?.ProtectedSourcePaths);
        CatalogTreeNode[] resultSourceRoots = request.MapType == "Project" ? defaults :
            CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
                request.ProjectPath, request.ProjectXml, "Project",
                ProjectSessionID: request.ProjectSessionID,
                ExternalCoreLayerReferences: request.ExternalCoreLayerReferences,
            CoreLayerContext: request.CoreLayerContext), "Project",
                request.ComputeReadLease?.ProtectedSourcePaths);
        CatalogTreeNode[] projectResultNodes = Descendants(resultSourceRoots
            .Where(root => root.Name == "Results").SelectMany(root => root.Children ?? [])).ToArray();
        CatalogTreeNode[] resultCandidates = projectResultNodes
            .Where(node => node.SourceClass == "ObservableResult" && node.AssetID is not null).ToArray();
        string? protectedCallbackResultPath = ProtectedCallbackResultPath(request.ComputeReadLease, map);
        var resultGroupCandidates = projectResultNodes
            .Where(node => node.SourceClass == "MultiResultNodeVM").ToList();
        // A saved MultiResult can outlive all stored outputs, or await its
        // Plan's first compute. Retain its exact owning Plan without inventing
        // a Result/HDF identity, so the real computeStarted run can supply it.
        string[] serializedResultPlans = map.Element("Tree")?.Descendants("Node")
            .Where(node => (string?)node.Attribute("Type") == "MultiResult")
            .Select(node => (string?)node.Attribute("Name"))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray() ?? [];
        CatalogTreeNode[] knownPlans = resultSourceRoots.Where(root => root.Name == "Plans")
            .SelectMany(root => root.Children ?? [])
            .Where(node => node.SourceClass == "ObservablePlan" && node.AssetID is { Length: > 0 })
            .ToArray();
        foreach (string planName in serializedResultPlans)
        {
            if (resultGroupCandidates.Any(node => node.Name == planName)) continue;
            CatalogTreeNode[] owners = knownPlans.Where(node => node.Name == planName).ToArray();
            if (owners.Length > 1) throw new SourceFileChangedException();
            if (owners.Length != 1 || owners[0].AssetID is not { Length: > 0 } assetID) continue;
            string id = "multi-result:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetID)))[..24];
            resultGroupCandidates.Add(new CatalogTreeNode(id, "MultiResultNodeVM", planName, AssetID: assetID));
        }
        CatalogTreeNode[][] prepared = PrepareResultContexts(context, defaults,
            resultGroupCandidates.ToArray(), resultCandidates, request.ResultSelectionOverrides);
        defaults = prepared[0];
        resultGroupCandidates = prepared[1].ToList();
        XElement[] serializedRoots = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        string? selected = null;
        var result = new List<CatalogTreeNode>(defaults.Length);

        foreach (CatalogTreeNode root in defaults)
        {
            string rootName = root.Name;
            int rootIndex = Array.FindIndex(serializedRoots, node =>
                (string?)node.Attribute("Type") == "Root" && (string?)node.Attribute("Name") == rootName);
            CatalogTreeNodeLocator rootLocator = new(rootName, "Root", rootName,
                rootIndex < 0 ? null : [rootIndex]);
            CatalogTreeNode[] children = PreferResultGroupCandidates(
                AnnotateDefaults(root.Children ?? [], rootName, request.MapIndex),
                request.ResultSelectionOverrides);
            expanded.Add(root.Id);
            if (rootIndex >= 0)
            {
                XElement rootElement = serializedRoots[rootIndex];
                var available = children.ToList();
                var placed = ApplySerializedChildren(rootElement.Elements("Node").ToArray(), available,
                    rootName, [rootIndex], request.MapIndex, Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!,
                    gdbRasterCatalogs, expanded, resultCandidates, ref selected,
                    resultGroupCandidates: resultGroupCandidates.ToList(),
                    protectedCallbackResultPath: protectedCallbackResultPath,
                    resultSelectionOverrides: request.ResultSelectionOverrides);
                children = placed.Concat(available).ToArray();
                AddState(rootElement, root.Id, defaultExpanded: true, expanded, ref selected);
            }
            else
            {
                expanded.Add(root.Id);
            }
            result.Add(root with
            {
                Children = children.Length == 0 ? null : children,
                CanCreateFolder = true,
                Locator = rootLocator
            });
        }

        foreach ((XElement extra, int index) in serializedRoots.Select((node, index) => (node, index)))
        {
            if ((string?)extra.Attribute("Type") != "Root" || result.Any(root => root.Name == (string?)extra.Attribute("Name"))) continue;
            string name = (string?)extra.Attribute("Name") ?? "";
            var locator = new CatalogTreeNodeLocator(name, "Root", name, [index]);
            string id = NodeID(request.MapIndex, [index]);
            var available = new List<CatalogTreeNode>();
            var placed = ApplySerializedChildren(extra.Elements("Node").ToArray(), available, name, [index],
                request.MapIndex, Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!, gdbRasterCatalogs, expanded,
                resultCandidates, ref selected, resultGroupCandidates: resultGroupCandidates.ToList(),
                protectedCallbackResultPath: protectedCallbackResultPath,
                resultSelectionOverrides: request.ResultSelectionOverrides);
            result.Add(new CatalogTreeNode(id, "MapTreeRootNode", name, Children: placed.Length == 0 ? null : placed,
                CanCreateFolder: true, Locator: locator));
            AddState(extra, id, defaultExpanded: true, expanded, ref selected);
        }

        CatalogTreeNode[] roots = result.ToArray();
        gdbRasterCatalogs.VerifySuccessfulSources();
        return (roots, expanded.ToArray(), selected, CollectHiddenLayerIDs(roots, request.MapIndex));
    }

    private static string? ProtectedCallbackResultPath(MapResultComputeReadLease? lease, XElement map)
    {
        if (lease?.CallbackResultPath is not { Length: > 0 } callbackResultPath) return null;
        string canonical = SourceFilePaths.Canonical(callbackResultPath);
        if (!(lease.ProtectedSourcePaths ?? []).Any(path =>
                SourceFilePaths.SameSourceFile(path, canonical)))
            throw new SourceFileChangedException();
        string callbackName = Path.GetFileNameWithoutExtension(canonical);
        IEnumerable<XElement> serializedNodes = map.Element("Tree")?.Descendants("Node")
            ?? Enumerable.Empty<XElement>();
        int serializedRows = serializedNodes
            .Count(node => (string?)node.Attribute("Type") == "Result" &&
                string.Equals((string?)node.Attribute("Name"), callbackName, StringComparison.Ordinal));
        if (serializedRows > 1) throw new SourceFileChangedException();
        return canonical;
    }

    private static CatalogTreeNode[][] PrepareResultContexts(OperationContext context, CatalogTreeNode[] defaults,
        CatalogTreeNode[] resultGroupCandidates, CatalogTreeNode[] resultCandidates,
        MapTreeResultSelectionOverride[]? requestedSelections)
    {
        var groups = Descendants(defaults).Concat(resultGroupCandidates)
            .Where(node => SourceNodeType(node.SourceClass) == "MultiResult")
            .GroupBy(ResultGroupIdentity, StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        var overrides = new Dictionary<string, MapTreeResultSelectionOverride>(StringComparer.Ordinal);
        foreach (MapTreeResultSelectionOverride selection in requestedSelections ?? [])
        {
            if (selection is null || string.IsNullOrWhiteSpace(selection.PlanName) ||
                (selection.FollowLatest
                    ? selection.SessionHdfResultOwner is null ||
                      !string.IsNullOrEmpty(selection.SelectedResultPath) ||
                      !string.IsNullOrEmpty(selection.SelectedResultRevision)
                    : string.IsNullOrWhiteSpace(selection.SelectedResultPath) ||
                      string.IsNullOrWhiteSpace(selection.SelectedResultRevision) ||
                      !Path.IsPathFullyQualified(selection.SelectedResultPath)))
                throw new ArgumentException("A selected MultiResult context requires exact Result identity.");
            string key;
            if (selection.SessionHdfResultOwner is { } owner)
            {
                ValidateSessionHdfResultOwner(context, owner);
            CatalogTreeNode[] ownerGroups = groups.Where(group =>
                    string.Equals(group.Name, owner.EmbeddedPlanName, StringComparison.Ordinal) &&
                    group.AssetID is null &&
                    (group.SelectedSessionHdfResultOwner is not null ||
                     group.Locator?.SessionHdfResultOwner is not null))
                .ToArray();
                if (!string.IsNullOrWhiteSpace(selection.PlanAssetID) ||
                    !string.Equals(selection.PlanName, owner.EmbeddedPlanName, StringComparison.Ordinal) ||
                    ownerGroups.Length != 1)
                    throw new SourceFileChangedException();
                key = SessionHdfResultGroupKey(owner);
                CatalogTreeNode[] exactResults = resultCandidates.Where(candidate =>
                    candidate.SourceClass == "ObservableResult" && candidate.AssetID is { } path &&
                    PathEquals(path, owner.ResultPath) && candidate.ResultPlanName == owner.EmbeddedPlanName)
                    .ToArray();
                if (exactResults.Length != 1 ||
                    !RevisionFile(exactResults[0].AssetID!).Equals(owner.ResultRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(selection.PlanAssetID) ||
                    !Path.IsPathFullyQualified(selection.PlanAssetID))
                    throw new ArgumentException("A project-owned MultiResult context requires an exact Plan identity.");
                key = ResultGroupKey(selection.PlanAssetID, selection.PlanName);
                if (!groups.Any(group => group.AssetID is { Length: > 0 } asset &&
                        ResultGroupKey(asset, group.Name) == key))
                    throw new SourceFileChangedException();
            }
            if (!overrides.TryAdd(key, selection)) throw new SourceFileChangedException();
        }
        if (overrides.Values.GroupBy(selection => selection.PlanName, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            throw new SourceFileChangedException();

        CatalogTreeNode[] Prepare(IEnumerable<CatalogTreeNode> nodes) => nodes.Select(node =>
        {
            CatalogTreeNode[]? children = node.Children is { Length: > 0 } nested
                ? Prepare(nested)
                : null;
            if (SourceNodeType(node.SourceClass) != "MultiResult")
                return node with { Children = children };

            string? planAsset = node.AssetID;
            string? groupKey = planAsset is { Length: > 0 }
                ? ResultGroupKey(planAsset, node.Name) : null;
            SessionHdfResultOwner? nodeSessionOwner = node.SelectedSessionHdfResultOwner ??
                node.Locator?.SessionHdfResultOwner;
            MapTreeResultSelectionOverride? sessionRequest = nodeSessionOwner is null ? null :
                overrides.Values.SingleOrDefault(selection =>
                    selection.SessionHdfResultOwner is { } owner &&
                    string.Equals(owner.EmbeddedPlanName, node.Name, StringComparison.Ordinal));
            CatalogTreeNode[] options = resultCandidates.Where(candidate =>
                    string.Equals(candidate.ResultPlanName, node.Name, StringComparison.Ordinal) &&
                    candidate.AssetID is { Length: > 0 } resultPath && File.Exists(CanonicalPath(resultPath)))
                .OrderByDescending(candidate => candidate.ResultEndTime ?? DateTime.MinValue)
                .ToArray();
            if (sessionRequest?.SessionHdfResultOwner is { } sessionOwner)
            {
                // A session-owned embedded-Plan group contains only explicitly
                // captured external Results for that exact label. A same-named
                // project Plan or its Results never become its owner.
                ValidateSessionHdfResultOwner(context, sessionOwner);
                options = options.Where(candidate => candidate.AssetID is { Length: > 0 } resultPath &&
                    CaptureSessionHdfResultOwner(context, candidate, CanonicalPath(resultPath),
                        RevisionFile(CanonicalPath(resultPath))) is { } candidateOwner &&
                    string.Equals(candidateOwner.EmbeddedPlanName, sessionOwner.EmbeddedPlanName,
                        StringComparison.Ordinal)).ToArray();
            }
            else if (groupKey is not null)
            {
                options = options.Where(candidate => candidate.AssetID is { Length: > 0 } resultPath &&
                    CaptureSessionHdfResultOwner(context, candidate, CanonicalPath(resultPath),
                        RevisionFile(CanonicalPath(resultPath))) is null).ToArray();
            }
            else
                options = options.Where(candidate => candidate.AssetID is { Length: > 0 } resultPath &&
                    CaptureSessionHdfResultOwner(context, candidate, CanonicalPath(resultPath),
                        RevisionFile(CanonicalPath(resultPath))) is not null).ToArray();
            if (options.Select(candidate => candidate.AssetID).Where(path => path is not null)
                .Distinct(PathStringComparer).Count() != options.Length)
                throw new SourceFileChangedException();

            if (options.Length == 0 && sessionRequest is not null)
                throw new SourceFileChangedException();
            if (options.Length == 0)
                return node with
                {
                    Children = null, SelectedResultAssetID = null, SelectedResultRevision = null,
                    SelectedSessionHdfResultOwner = null,
                    LayerErrors = null, DiagnosticStatus = null, DiagnosticSourceIdentity = null,
                    DiagnosticSourceRevision = null, DiagnosticReadError = null,
                    ResultComputeState = null, ResultComputeFailureReason = null,
                    ResultComputeSourcePath = null, ResultComputeSourceRevision = null
                };

            CatalogTreeNode? selected = null;
            if (sessionRequest?.SessionHdfResultOwner is { } owner)
            {
                ValidateSessionHdfResultOwner(context, owner);
                if (sessionRequest.FollowLatest)
                {
                    // `options` is already ordered by the retained Result
                    // ComputeSummary.EndTime, matching GetLatestResultOption.
                    selected = options.FirstOrDefault();
                }
                else
                {
                    CatalogTreeNode[] matches = options.Where(candidate => candidate.AssetID is { } path &&
                        PathEquals(path, sessionRequest.SelectedResultPath) &&
                        candidate.ResultPlanName == owner.EmbeddedPlanName).ToArray();
                    if (matches.Length != 1) throw new SourceFileChangedException();
                    selected = matches[0];
                    if (!RevisionFile(selected.AssetID!).Equals(sessionRequest.SelectedResultRevision,
                            StringComparison.OrdinalIgnoreCase))
                        throw new SourceFileChangedException();
                    ValidateSessionHdfResultSelection(context, owner,
                        sessionRequest.SelectedResultPath, sessionRequest.SelectedResultRevision);
                }
            }
            else if (groupKey is { } exactGroupKey &&
                     overrides.TryGetValue(exactGroupKey, out MapTreeResultSelectionOverride? requested))
            {
                CatalogTreeNode[] matches = options.Where(candidate => candidate.AssetID is { } path &&
                    PathEquals(path, requested.SelectedResultPath)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                selected = matches[0];
                string revision = RevisionFile(selected.AssetID!);
                if (!string.Equals(revision, requested.SelectedResultRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
            }
            else
            {
                selected = options.FirstOrDefault();
            }

            string? selectedPath = selected?.AssetID is { Length: > 0 } path
                ? CanonicalPath(path) : null;
            string? selectedRevision = selectedPath is null ? null : RevisionFile(selectedPath);
            SessionHdfResultOwner? selectedResultOwner = selected is null ? null :
                CaptureSessionHdfResultOwner(context, selected, selectedPath!, selectedRevision!);
            SessionHdfResultOwner? groupOwner = sessionRequest?.SessionHdfResultOwner ?? selectedResultOwner;
            if (groupKey is null && groupOwner is null)
                return node with
                {
                    Children = null, SelectedResultAssetID = null, SelectedResultRevision = null,
                    SelectedSessionHdfResultOwner = null,
                    LayerErrors = null, DiagnosticStatus = null, DiagnosticSourceIdentity = null,
                    DiagnosticSourceRevision = null, DiagnosticReadError = null,
                    ResultComputeState = null, ResultComputeFailureReason = null,
                    ResultComputeSourcePath = null, ResultComputeSourceRevision = null
                };
            bool matchingDiagnostics = selectedPath is not null &&
                selected?.DiagnosticSourceIdentity is { } diagnosticPath &&
                PathEquals(diagnosticPath, selectedPath) &&
                string.Equals(selected.DiagnosticSourceRevision, selectedRevision,
                    StringComparison.OrdinalIgnoreCase);
            bool matchingComputeState = selectedPath is not null &&
                selected?.ResultComputeSourcePath is { } computeSourcePath &&
                PathEquals(computeSourcePath, selectedPath) &&
                string.Equals(selected.ResultComputeSourceRevision, selectedRevision,
                    StringComparison.OrdinalIgnoreCase);
            return node with
            {
                AssetID = groupOwner is null ? node.AssetID : null,
                Children = selected?.Children,
                SelectedResultAssetID = selectedPath,
                SelectedResultRevision = selectedRevision,
                SelectedSessionHdfResultOwner = groupOwner,
                LayerErrors = matchingDiagnostics ? selected?.LayerErrors : null,
                DiagnosticStatus = matchingDiagnostics ? selected?.DiagnosticStatus :
                    selected is null ? null : selected.DiagnosticStatus is "loaded" or "sourceChanged"
                        ? "sourceChanged" : "unavailable",
                DiagnosticSourceIdentity = selectedPath,
                DiagnosticSourceRevision = matchingDiagnostics ? selected?.DiagnosticSourceRevision : null,
                DiagnosticReadError = matchingDiagnostics ? selected?.DiagnosticReadError : null,
                ResultComputeState = matchingComputeState ? selected?.ResultComputeState : null,
                ResultComputeFailureReason = matchingComputeState ? selected?.ResultComputeFailureReason : null,
                ResultComputeSourcePath = matchingComputeState ? selected?.ResultComputeSourcePath : null,
                ResultComputeSourceRevision = matchingComputeState ? selected?.ResultComputeSourceRevision : null
            };
        }).ToArray();

        CatalogTreeNode[] preparedDefaults = Prepare(defaults);
        CatalogTreeNode[] preparedGroups = Prepare(resultGroupCandidates);
        foreach (CatalogTreeNode group in Descendants(preparedDefaults).Concat(preparedGroups)
                     .Where(node => SourceNodeType(node.SourceClass) == "MultiResult" &&
                                    node.SelectedResultAssetID is not null && node.SelectedResultRevision is not null))
        {
            if (!string.Equals(RevisionFile(group.SelectedResultAssetID!), group.SelectedResultRevision,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
        return [preparedDefaults, preparedGroups];
    }

    private static string ResultGroupIdentity(CatalogTreeNode node) => node.AssetID is { Length: > 0 } asset
        ? ResultGroupKey(asset, node.Name)
        : node.SelectedSessionHdfResultOwner is { } selectedOwner
            ? SessionHdfResultGroupKey(selectedOwner)
            : node.Locator?.SessionHdfResultOwner is { } locatorOwner
                ? SessionHdfResultGroupKey(locatorOwner)
                : "embedded-plan-name\n" + node.Name;

    private static string SessionHdfResultGroupKey(SessionHdfResultOwner owner) =>
        "session-hdf-result\n" + owner.ReferenceID + "\n" + owner.EmbeddedPlanName;

    private static SessionHdfResultOwner? CaptureSessionHdfResultOwner(OperationContext context,
        CatalogTreeNode selected, string resultPath, string resultRevision)
    {
        if (context.CoreLayerContext is not { } coreContext || selected.ResultPlanName is not { Length: > 0 } planName)
            return null;
        SessionHdfReferenceCapture[] matches = (coreContext.SessionHdfReferences ?? []).Where(reference =>
            reference.Kind == "result" && PathEquals(reference.SourcePath, resultPath) &&
            reference.SourceRevision.Equals(resultRevision, StringComparison.OrdinalIgnoreCase) &&
            reference.ResultPlanName == planName && reference.Name == selected.Name).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new SourceFileChangedException();
        SessionHdfReferenceCapture capture = matches[0];
        return new SessionHdfResultOwner(capture.ReferenceID, capture.ProjectSessionID,
            capture.ProjectPath, resultPath, resultRevision.ToUpperInvariant(), planName,
            coreContext.IdentityToken);
    }

    private static void ValidateSessionHdfResultOwner(OperationContext context,
        SessionHdfResultOwner owner)
    {
        CoreLayerRequestContext coreContext = context.CoreLayerContext ?? throw new SourceFileChangedException();
        if (!string.Equals(owner.CoreLayerContextIdentityToken, coreContext.IdentityToken, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(owner.ProjectSessionID, coreContext.ProjectSessionID, StringComparison.Ordinal) ||
            !PathEquals(owner.ProjectPath, coreContext.ProjectPath) ||
            !Path.IsPathFullyQualified(owner.ResultPath) ||
            string.IsNullOrWhiteSpace(owner.EmbeddedPlanName) ||
            owner.ResultRevision.Length != 64 || !owner.ResultRevision.All(character => Uri.IsHexDigit(character)))
            throw new SourceFileChangedException();
        SessionHdfReferenceCapture capture = ResolveSessionHdfReference(context, owner.ResultPath,
            owner.ResultRevision, expectedKind: "result");
        if (!string.Equals(owner.ReferenceID, capture.ReferenceID, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(owner.EmbeddedPlanName, capture.ResultPlanName, StringComparison.Ordinal) ||
            !PathEquals(owner.ProjectPath, capture.ProjectPath))
            throw new SourceFileChangedException();
        ProjectResultIdentity result = ReadProjectResult(context, owner.ResultPath, owner.ResultRevision);
        if (!string.Equals(result.PlanName, owner.EmbeddedPlanName, StringComparison.Ordinal) ||
            !string.Equals(result.Name, capture.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void ValidateSessionHdfResultSelection(OperationContext context,
        SessionHdfResultOwner owner, string selectedResultPath, string selectedResultRevision)
    {
        SessionHdfReferenceCapture selectedReference = ResolveSessionHdfReference(context,
            selectedResultPath, selectedResultRevision, expectedKind: "result");
        ProjectResultIdentity selected = ReadProjectResult(context, selectedResultPath,
            selectedResultRevision);
        if (!string.Equals(selected.PlanName, owner.EmbeddedPlanName, StringComparison.Ordinal) ||
            !string.Equals(selected.Name, selectedReference.Name, StringComparison.Ordinal) ||
            !string.Equals(selectedReference.ResultPlanName, owner.EmbeddedPlanName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static string ResultGroupKey(string planAssetID, string planName) =>
        CanonicalPath(planAssetID) + "\n" + planName;

    private static StringComparer PathStringComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static IEnumerable<CatalogTreeNode> Descendants(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode descendant in Descendants(children)) yield return descendant;
        }
    }

    private static string[] CollectHiddenLayerIDs(IEnumerable<CatalogTreeNode> nodes, int mapIndex)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatalogTreeNode node in nodes)
        {
            if (node.Locator is { NodeType: "MapLayer", NodePath: { Length: > 0 } path } locator &&
                node.Checked == "unchecked")
            {
                if (MapRasterLayerRendering.SupportedTypes.Contains(locator.MapType ?? "", StringComparer.Ordinal))
                    hidden.Add($"vis-map-{mapIndex}-node-{string.Join('.', path)}");
                else if (locator.MapType is "PointsVector" or "PolylinesVector" or "PolygonsVector" &&
                         node.AssetID is { Length: > 0 } assetID)
                    hidden.Add(assetID);
            }
            if (node.Children is { Length: > 0 } children)
                hidden.UnionWith(CollectHiddenLayerIDs(children, mapIndex));
        }
        return hidden.ToArray();
    }

    internal static bool IsEffectivelyVisible(IEnumerable<CatalogTreeNode> roots, CatalogTreeNode target)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(target);
        bool found = false;
        bool visible = false;
        foreach (CatalogTreeNode root in roots)
            _ = Visit(root, parentVisible: true);
        if (!found) throw new SourceFileChangedException();
        return visible;

        bool Visit(CatalogTreeNode node, bool parentVisible)
        {
            bool isCheckable = node.Locator is { NodeType: { } type } && CheckableNodeTypes.Contains(type);
            bool effective = parentVisible && (!isCheckable || node.Checked == "checked");
            if (ReferenceEquals(node, target))
            {
                found = true;
                visible = effective;
                return true;
            }
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode child in children)
                    if (Visit(child, effective)) return true;
            return false;
        }
    }

    private static CatalogTreeNode[] AnnotateDefaults(IEnumerable<CatalogTreeNode> nodes, string rootName,
        int mapIndex, GeometryOwner? parentGeometry = null, PlanOwner? parentPlan = null,
        ResultOwner? parentResult = null, BoundaryConditionOwner? parentBoundaryCondition = null)
    {
        return nodes.Select(node =>
        {
            string? type = SourceNodeType(node.SourceClass);
            GeometryOwner? childOwner = type == "Geometry" && node.AssetID is { Length: > 0 } geometryAsset
                ? new GeometryOwner(rootName, node.Name, geometryAsset, null)
                : parentGeometry;
            PlanOwner? childPlan = type == "Plan" && parentPlan is null && node.AssetID is { Length: > 0 } planAsset
                ? new PlanOwner(rootName, node.Name, planAsset, null)
                : parentPlan;
            ResultOwner? childResult = type switch
            {
                "Result" when node.AssetID is { Length: > 0 } resultAsset =>
                    new ResultOwner("Result", rootName, node.Name, CanonicalPath(resultAsset), null,
                        RevisionFile(CanonicalPath(resultAsset))),
                "MultiResult" when node.SelectedResultAssetID is { Length: > 0 } selectedAsset &&
                                    node.SelectedResultRevision is { Length: > 0 } selectedRevision =>
                    new ResultOwner("MultiResult", rootName, node.Name, selectedAsset, null, selectedRevision,
                        node.SelectedSessionHdfResultOwner),
                _ => parentResult
            };
            BoundaryConditionOwner? childBoundaryCondition = type == "BoundaryCondition" &&
                node.AssetID is { Length: > 0 } boundaryAsset
                ? new BoundaryConditionOwner(rootName, node.Name, boundaryAsset, null)
                : parentBoundaryCondition;
            bool isGeometryChild = type is not null && parentGeometry is not null &&
                (GeometryMapChildTypes.Contains(type) || node.Locator?.AssociationKind is { Length: > 0 });
            bool isPlanChild = type is not null && parentPlan is not null;
            bool isResultChild = type is not null && type is not ("Result" or "MultiResult") && parentResult is not null;
            bool isBoundaryConditionChild = type == "WindLayer" && parentBoundaryCondition is not null &&
                parentResult is null;
            bool isPrecipitationChild = type == "PrecipitationLayer" && parentBoundaryCondition is not null;
            bool isCheckable = type is not null && CheckableNodeTypes.Contains(type) &&
                (type != "PrecipitationLayer" || isPrecipitationChild);
            var children = node.Children is { Length: > 0 } nested
                ? AnnotateDefaults(nested, rootName, mapIndex, childOwner, childPlan, childResult,
                    childBoundaryCondition)
                : null;
            CatalogTreeNodeLocator? locator = type is null ? null : new(rootName, type, node.Name,
                AssetID: isBoundaryConditionChild || isPrecipitationChild ? parentBoundaryCondition!.AssetID : node.AssetID,
                MapType: type == "MapLayer" ? Property(node, "MapType") : null,
                Filename: type == "MapLayer" ? Property(node, "Filename") : null,
                FilenameRelative: type == "MapLayer" ? Property(node, "FilenameRelative") : null,
                ParentGeometryAssetID: isGeometryChild ? parentGeometry!.AssetID : null,
                ParentGeometryNodePath: isGeometryChild ? parentGeometry!.NodePath : null,
                ParentGeometryNodeName: isGeometryChild ? parentGeometry!.Name : null,
                AssociationAssetID: node.Locator?.AssociationAssetID,
                AssociationKind: node.Locator?.AssociationKind,
                ParentPlanAssetID: isPlanChild ? parentPlan!.AssetID : null,
                ParentPlanNodePath: isPlanChild ? parentPlan!.NodePath : null,
                ParentPlanNodeName: isPlanChild ? parentPlan!.Name : null,
                ParentResultNodeType: isResultChild ? parentResult!.NodeType : null,
                ParentResultAssetID: isResultChild ? parentResult!.AssetID : null,
                ParentResultNodePath: isResultChild ? parentResult!.NodePath : null,
                ParentResultNodeName: isResultChild ? parentResult!.Name : null,
                ParentResultRevision: isResultChild ? parentResult!.Revision : null,
                SessionHdfResultOwner: isResultChild ? parentResult!.SessionHdfResultOwner :
                    type == "MultiResult" ? node.SelectedSessionHdfResultOwner : null,
                ParentBoundaryConditionAssetID: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.AssetID : null,
                ParentBoundaryConditionNodePath: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.NodePath : null,
                ParentBoundaryConditionNodeName: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.Name : null);
            return node with
            {
                Id = isResultChild
                    ? ResultChildNodeID(mapIndex, parentResult!, type!, node.Name)
                    : isGeometryChild ? GeometryChildNodeID(mapIndex, parentGeometry!, type!, node.Name)
                    : isBoundaryConditionChild || isPrecipitationChild
                        ? BoundaryConditionChildNodeID(mapIndex, parentBoundaryCondition!, type!, node.Name)
                        : node.Id,
                AssetID = isBoundaryConditionChild || isPrecipitationChild ? parentBoundaryCondition!.AssetID : node.AssetID,
                Children = children,
                Checked = isCheckable || type == "TerrainInput" &&
                    TransientTerrainInputSourceClasses.Contains(node.SourceClass)
                    ? "unchecked" : null,
                CanMove = type is not null && MovableNodeTypes.Contains(type),
                CanPin = false,
                CanCreateFolder = false,
                CanAcceptChildren = false,
                Locator = locator
            };
        }).ToArray();
    }

    private static CatalogTreeNode[] ReScopeGeometryChildren(IEnumerable<CatalogTreeNode> nodes,
        int mapIndex, GeometryOwner owner)
    {
        return nodes.Select(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            bool isGeometryChild = locator is not null &&
                (GeometryMapChildTypes.Contains(locator.NodeType) || locator.AssociationKind is { Length: > 0 });
            CatalogTreeNode[]? children = node.Children is { Length: > 0 } nested
                ? ReScopeGeometryChildren(nested, mapIndex, owner)
                : null;
            return isGeometryChild
                ? node with
                {
                    Id = GeometryChildNodeID(mapIndex, owner, locator!.NodeType, node.Name),
                    Children = children,
                    Locator = locator! with
                    {
                        RootName = owner.RootName,
                        ParentGeometryAssetID = owner.AssetID,
                        ParentGeometryNodePath = owner.NodePath,
                        ParentGeometryNodeName = owner.Name
                    }
                }
                : node with { Children = children };
        }).ToArray();
    }

    private sealed record GeometryOwner(string RootName, string Name, string AssetID, int[]? NodePath);
    private sealed record PlanOwner(string RootName, string Name, string AssetID, int[]? NodePath);
    private sealed record BoundaryConditionOwner(string RootName, string Name, string AssetID, int[]? NodePath);
    private sealed record ResultOwner(string NodeType, string RootName, string Name, string AssetID,
        int[]? NodePath, string Revision, SessionHdfResultOwner? SessionHdfResultOwner = null);

    private static string GeometryChildNodeID(int mapIndex, GeometryOwner owner, string nodeType, string nodeName)
    {
        string path = owner.NodePath is null ? "default" : string.Join('.', owner.NodePath);
        string identity = string.Join("\n", mapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            owner.RootName, CanonicalPath(owner.AssetID), path, owner.Name, nodeType, nodeName);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return $"vis-map-{mapIndex}-geometry-child-{digest}";
    }

    private static string BoundaryConditionChildNodeID(int mapIndex, BoundaryConditionOwner owner,
        string nodeType, string nodeName)
    {
        string path = owner.NodePath is null ? "default" : string.Join('.', owner.NodePath);
        string identity = string.Join("\n", mapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            owner.RootName, CanonicalPath(owner.AssetID), path, owner.Name, nodeType, nodeName);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return $"vis-map-{mapIndex}-boundary-child-{digest}";
    }

    private static string ResultChildNodeID(int mapIndex, ResultOwner owner, string nodeType, string nodeName)
    {
        string path = owner.NodePath is null ? "default" : string.Join('.', owner.NodePath);
        string identity = string.Join("\n", mapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            owner.RootName, CanonicalPath(owner.AssetID), path, owner.Name, nodeType, nodeName);
        if (owner.SessionHdfResultOwner is { } sessionOwner)
            identity = string.Join("\n", identity, sessionOwner.ReferenceID,
                sessionOwner.CoreLayerContextIdentityToken);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return $"vis-map-{mapIndex}-result-child-{digest}";
    }

    private static CatalogTreeNode[] ApplySerializedChildren(XElement[] serialized, List<CatalogTreeNode> available,
        string rootName, int[] parentPath, int mapIndex, string projectDirectory, MapTreeGdbCatalogs gdbRasterCatalogs,
        HashSet<string> expanded,
        CatalogTreeNode[] resultCandidates, ref string? selected, GeometryOwner? parentGeometry = null,
        PlanOwner? parentPlan = null, List<CatalogTreeNode>? resultGroupCandidates = null,
        ResultOwner? parentResult = null, BoundaryConditionOwner? parentBoundaryCondition = null,
        string? protectedCallbackResultPath = null,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        var placed = new List<CatalogTreeNode>(serialized.Length);
        for (int index = 0; index < serialized.Length; index++)
        {
            XElement element = serialized[index];
            int[] path = [.. parentPath, index];
            string type = (string?)element.Attribute("Type") ?? "Unknown";
            string name = (string?)element.Attribute("Name") ?? "";
            string id = NodeID(mapIndex, path);
            if (type == "Root") continue;
            if (type == "Folder")
            {
                CatalogTreeNode[] folderChildren = ApplySerializedChildren(element.Elements("Node").ToArray(), available,
                    rootName, path, mapIndex, projectDirectory, gdbRasterCatalogs, expanded, resultCandidates, ref selected,
                    parentGeometry, parentPlan, resultGroupCandidates, parentResult, parentBoundaryCondition,
                    protectedCallbackResultPath, resultSelectionOverrides);
                AddState(element, id, defaultExpanded: true, expanded, ref selected);
                placed.Add(new CatalogTreeNode(id, "FolderNode", name, Children: folderChildren.Length == 0 ? null : folderChildren,
                    IsPinned: ReadBool(element, "IsPinned"), CanRename: true, CanMove: true,
                    CanAcceptChildren: true, CanCreateFolder: true,
                    Locator: new CatalogTreeNodeLocator(rootName, type, name, path)));
                continue;
            }

            CatalogTreeNode? node;
            if (type == "MapLayer")
            {
                node = (string?)element.Parent?.Attribute("Type") == "GdbDataset"
                    ? BuildGdbRasterMapNode(element, id, rootName, path, projectDirectory, gdbRasterCatalogs)
                    : BuildMapLayerNode(element, id, rootName, path, projectDirectory);
            }
            else if (type is "GdbDataset" or "GdbVectorLayer")
            {
                node = BuildGdbMapNode(element, id, rootName, path, projectDirectory);
            }
            else
            {
                node = TakeDefault(available, type, name);
                if (node is null && type == "MultiResult" && resultGroupCandidates is not null)
                {
                    node = TakeResultGroupDefault(resultGroupCandidates, name, parentPlan,
                        resultSelectionOverrides);
                    if (node is not null && node.AssetID is null &&
                        node.SelectedSessionHdfResultOwner is null)
                        throw new SourceFileChangedException();
                }
                if (node is null && type == "Result")
                {
                    CatalogTreeNode[] matches = resultCandidates.Where(candidate =>
                        string.Equals(candidate.Name, name, StringComparison.Ordinal)).ToArray();
                    if (matches.Length > 1) throw new SourceFileChangedException();
                    node = matches.SingleOrDefault();
                }

            if (type == "Result" && node is not null)
            {
                CatalogTreeNode[] exactCandidates = resultCandidates.Where(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal)).ToArray();
                if (exactCandidates.Length > 1 || exactCandidates.Length == 1 &&
                    (node.AssetID is not { Length: > 0 } resultAsset ||
                     exactCandidates[0].AssetID is not { Length: > 0 } candidateAsset ||
                     !PathEquals(resultAsset, candidateAsset)))
                    throw new SourceFileChangedException();
            }
            if (type == "MultiResult" && node is not null && node.AssetID is null &&
                node.SelectedSessionHdfResultOwner is null && ReadBool(element, "IsChecked"))
                throw new SourceFileChangedException();
            if (type == "MultiResult" && node is not null && node.AssetID is null &&
                node.SelectedSessionHdfResultOwner is null)
                node = node with { Children = null, SelectedResultAssetID = null, SelectedResultRevision = null };
            }

            string? protectedResultPath = ProtectedSerializedResultPath(type, name, node,
                resultCandidates, protectedCallbackResultPath);

            if (node is null)
            {
                CatalogTreeNode unknown = BuildSerializedUnknownNode(element, id, rootName, path, mapIndex, projectDirectory,
                    gdbRasterCatalogs, expanded, resultCandidates, ref selected, parentGeometry, parentPlan, parentResult,
                    resultGroupCandidates, parentBoundaryCondition, protectedCallbackResultPath,
                    resultSelectionOverrides);
                if (protectedResultPath is not null)
                    unknown = unknown with { ProtectedResultSourcePath = protectedResultPath };
                placed.Add(unknown);
                continue;
            }

            if (protectedResultPath is not null)
                node = node with { ProtectedResultSourcePath = protectedResultPath };

            var childPool = (node.Children ?? []).ToList();
            GeometryOwner? childOwner = parentGeometry;
            if (type == "Geometry" && node.AssetID is { Length: > 0 } geometryAsset)
            {
                childOwner = new GeometryOwner(rootName, name, geometryAsset, path);
                childPool = ReScopeGeometryChildren(childPool, mapIndex, childOwner).ToList();
            }
            PlanOwner? childPlan = type == "Plan" && parentPlan is null && node.AssetID is { Length: > 0 } planAsset
                ? new PlanOwner(rootName, name, planAsset, path)
                : parentPlan;
            ResultOwner? childResult = type switch
            {
                "Result" when node.AssetID is { Length: > 0 } resultAsset =>
                    new ResultOwner("Result", rootName, name, CanonicalPath(resultAsset), path,
                        RevisionFile(CanonicalPath(resultAsset))),
                "MultiResult" when node.SelectedResultAssetID is { Length: > 0 } selectedAsset &&
                                    node.SelectedResultRevision is { Length: > 0 } selectedRevision =>
                    new ResultOwner("MultiResult", rootName, name, selectedAsset, path, selectedRevision,
                        node.SelectedSessionHdfResultOwner),
                _ => parentResult
            };
            BoundaryConditionOwner? childBoundaryCondition = type == "BoundaryCondition" &&
                node.AssetID is { Length: > 0 } boundaryAsset
                ? new BoundaryConditionOwner(rootName, name, boundaryAsset, path)
                : parentBoundaryCondition;
            childPool = AnnotateDefaults(childPool, rootName, mapIndex, childOwner, childPlan, childResult,
                childBoundaryCondition).ToList();
            CatalogTreeNode[] children = ApplySerializedChildren(element.Elements("Node").ToArray(), childPool,
                rootName, path, mapIndex, projectDirectory, gdbRasterCatalogs, expanded, resultCandidates, ref selected,
                childOwner, childPlan, resultGroupCandidates, childResult, childBoundaryCondition,
                protectedCallbackResultPath, resultSelectionOverrides);
            children = children.Concat(childPool).ToArray();
            bool isGeometryChild = parentGeometry is not null &&
                (GeometryMapChildTypes.Contains(type) || node.Locator?.AssociationKind is { Length: > 0 });
            bool isPlanChild = parentPlan is not null;
            bool isResultChild = parentResult is not null && type is not ("Result" or "MultiResult");
            bool isBoundaryConditionChild = type == "WindLayer" && parentBoundaryCondition is not null &&
                parentResult is null;
            bool isPrecipitationChild = type == "PrecipitationLayer" && parentBoundaryCondition is not null;
            string nodeID = isResultChild
                ? ResultChildNodeID(mapIndex, parentResult!, type, name)
                : isGeometryChild ? GeometryChildNodeID(mapIndex, parentGeometry!, type, name)
                : isBoundaryConditionChild || isPrecipitationChild
                    ? BoundaryConditionChildNodeID(mapIndex, parentBoundaryCondition!, type, name)
                    : node.Id;
            AddState(element, nodeID, defaultExpanded: false, expanded, ref selected);
            CatalogTreeNodeLocator locator = new(rootName, type, name, path,
                isBoundaryConditionChild || isPrecipitationChild ? parentBoundaryCondition!.AssetID : node.AssetID,
                type == "MapLayer" ? (string?)element.Attribute("MapType") :
                    type == "GdbVectorLayer" ? "GdbVectorLayer" : type == "GdbDataset" ? "GdbDataset" : null,
                type is "MapLayer" or "GdbDataset" or "GdbVectorLayer" ? (string?)element.Attribute("Filename") : null,
                type is "MapLayer" or "GdbDataset" or "GdbVectorLayer" ? (string?)element.Attribute("FilenameRelative") : null,
                isGeometryChild ? parentGeometry!.AssetID : null,
                isGeometryChild ? parentGeometry!.NodePath : null,
                isGeometryChild ? parentGeometry!.Name : null,
                node.Locator?.AssociationAssetID, node.Locator?.AssociationKind,
                isPlanChild ? parentPlan!.AssetID : null,
                isPlanChild ? parentPlan!.NodePath : null,
                isPlanChild ? parentPlan!.Name : null,
                ParentResultNodeType: isResultChild ? parentResult!.NodeType : null,
                ParentResultAssetID: isResultChild ? parentResult!.AssetID : null,
                ParentResultNodePath: isResultChild ? parentResult!.NodePath : null,
                ParentResultNodeName: isResultChild ? parentResult!.Name : null,
                ParentResultRevision: isResultChild ? parentResult!.Revision : null,
                SessionHdfResultOwner: isResultChild ? parentResult!.SessionHdfResultOwner :
                    type == "MultiResult" ? node.SelectedSessionHdfResultOwner : null,
                ParentBoundaryConditionAssetID: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.AssetID : null,
                ParentBoundaryConditionNodePath: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.NodePath : null,
                ParentBoundaryConditionNodeName: isBoundaryConditionChild || isPrecipitationChild
                    ? parentBoundaryCondition!.Name : null);
            bool supported = CheckableNodeTypes.Contains(type) &&
                (type != "PrecipitationLayer" || parentBoundaryCondition is not null) &&
                (type != "MapLayer" || (string?)element.Attribute("MapType") is string mapType && SupportedMapLayerTypes.Contains(mapType)) &&
                (type is not ("GdbDataset" or "GdbVectorLayer") || node.Checked is not null);
            CatalogTreeProperty[] serializedProperties = element.Attributes()
                .Where(attribute => attribute.Name != "Type" && attribute.Name != "Name")
                .Select(attribute => new CatalogTreeProperty("Source/" + attribute.Name.LocalName,
                    attribute.Name.LocalName, attribute.Value)).ToArray();
            var serializedPropertyIDs = serializedProperties.Select(property => property.Id)
                .ToHashSet(StringComparer.Ordinal);
            placed.Add(node with
            {
                Id = nodeID,
                AssetID = isBoundaryConditionChild || isPrecipitationChild ? parentBoundaryCondition!.AssetID : node.AssetID,
                Children = children.Length == 0 ? null : children,
                Properties = serializedProperties.Concat((node.Properties ?? [])
                    .Where(property => !serializedPropertyIDs.Contains(property.Id))).ToArray(),
                // BuildMapLayerNode deliberately removes the check state
                // when its source cannot be used. Do not restore visibility
                // from XML for a missing or unsupported map-layer file.
                Checked = supported && (type != "MapLayer" || node.Checked is not null)
                    ? (ReadBool(element, "IsChecked") ? "checked" : "unchecked") : null,
                IsPinned = ReadBool(element, "IsPinned"),
                CanPin = false,
                CanMove = MovableNodeTypes.Contains(type) &&
                    !(type == "MapLayer" && (string?)element.Parent?.Attribute("Type") == "GdbDataset"),
                CanRename = false,
                Locator = locator,
                ClassificationEditModeOpacity = type == "Classification"
                    ? ReadClassificationEditModeOpacity(element) : null
            });
        }
        return placed.ToArray();
    }

    private static string? ProtectedSerializedResultPath(string type, string name, CatalogTreeNode? node,
        CatalogTreeNode[] resultCandidates, string? protectedCallbackResultPath)
    {
        if (type != "Result" || protectedCallbackResultPath is null ||
            !string.Equals(Path.GetFileNameWithoutExtension(protectedCallbackResultPath), name,
                StringComparison.Ordinal))
            return null;

        // The source .vis schema has no Result path field. Its source adapter
        // accepts only unique Result names, so bind this callback path only to
        // its exact serialized display row when no other Result with that name
        // or AssetID can compete for the same row.
        if (resultCandidates.Any(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal)) ||
            node?.AssetID is not null || node?.Locator?.AssetID is not null)
            throw new SourceFileChangedException();
        return protectedCallbackResultPath;
    }

    private static CatalogTreeNode? TakeDefault(List<CatalogTreeNode> pool, string nodeType, string name)
    {
        for (int index = 0; index < pool.Count; index++)
        {
            CatalogTreeNode candidate = pool[index];
            if (candidate.Locator is { } locator && locator.NodeType == nodeType && locator.NodeName == name)
            {
                pool.RemoveAt(index);
                return candidate;
            }
            if (candidate.Children is not { Length: > 0 } children) continue;
            var nested = children.ToList();
            CatalogTreeNode? found = TakeDefault(nested, nodeType, name);
            if (found is null) continue;
            pool[index] = candidate with { Children = nested.Count == 0 ? null : nested.ToArray() };
            return found;
        }
        return null;
    }

    private static CatalogTreeNode? TakeResultGroupDefault(List<CatalogTreeNode> candidates,
        string name, PlanOwner? parentPlan,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        CatalogTreeNode[] matches = candidates.Where(candidate =>
            SourceNodeType(candidate.SourceClass) == "MultiResult" &&
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            (parentPlan is null || candidate.AssetID is { Length: > 0 } assetID &&
                PathEquals(assetID, parentPlan.AssetID))).ToArray();
        CatalogTreeNode? selected = SelectResultGroupDefault(matches, name, resultSelectionOverrides);
        if (selected is null) return null;
        int index = candidates.FindIndex(candidate => ReferenceEquals(candidate, selected));
        if (index < 0) throw new SourceFileChangedException();
        candidates.RemoveAt(index);
        return selected;
    }

    private static CatalogTreeNode? FindResultGroupDefault(IEnumerable<CatalogTreeNode>? candidates,
        string name, PlanOwner? parentPlan,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        CatalogTreeNode[] matches = (candidates ?? []).Where(candidate =>
            SourceNodeType(candidate.SourceClass) == "MultiResult" &&
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            (parentPlan is null || candidate.AssetID is { Length: > 0 } assetID &&
                PathEquals(assetID, parentPlan.AssetID))).ToArray();
        return SelectResultGroupDefault(matches, name, resultSelectionOverrides);
    }

    private static CatalogTreeNode? SelectResultGroupDefault(CatalogTreeNode[] matches, string name,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides)
    {
        if (matches.Length == 0) return null;
        if (matches.Length == 1) return matches[0];

        MapTreeResultSelectionOverride[] requested = (resultSelectionOverrides ?? [])
            .Where(selection => string.Equals(selection.PlanName, name, StringComparison.Ordinal)).ToArray();
        if (requested.Length > 1) throw new SourceFileChangedException();
        if (requested.SingleOrDefault() is { } selection)
        {
            CatalogTreeNode[] exact = selection.SessionHdfResultOwner is { } sessionOwner
                ? matches.Where(candidate => SessionHdfResultOwnerEquals(
                    candidate.SelectedSessionHdfResultOwner ?? candidate.Locator?.SessionHdfResultOwner,
                    sessionOwner)).ToArray()
                : matches.Where(candidate => candidate.SelectedSessionHdfResultOwner is null &&
                    candidate.Locator?.SessionHdfResultOwner is null &&
                    candidate.AssetID is { Length: > 0 } asset &&
                    PathEquals(asset, selection.PlanAssetID)).ToArray();
            return exact.Length == 1 ? exact[0] : throw new SourceFileChangedException();
        }

        CatalogTreeNode[] projectOwned = matches.Where(candidate => candidate.AssetID is { Length: > 0 } &&
            candidate.SelectedSessionHdfResultOwner is null &&
            candidate.Locator?.SessionHdfResultOwner is null).ToArray();
        if (projectOwned.Length == 1) return projectOwned[0];
        CatalogTreeNode[] sessionOwned = matches.Where(candidate =>
            candidate.SelectedSessionHdfResultOwner is not null ||
            candidate.Locator?.SessionHdfResultOwner is not null).ToArray();
        if (projectOwned.Length == 0 && sessionOwned.Length == 1) return sessionOwned[0];
        throw new SourceFileChangedException();
    }

    private static CatalogTreeNode[] PreferResultGroupCandidates(CatalogTreeNode[] nodes,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides)
    {
        CatalogTreeNode[] recursivelyPrepared = nodes.Select(node => node with
        {
            Children = node.Children is { Length: > 0 } children
                ? PreferResultGroupCandidates(children, resultSelectionOverrides)
                : node.Children
        }).ToArray();
        foreach (IGrouping<string, int> indices in recursivelyPrepared.Select((node, index) => (node, index))
                     .Where(item => SourceNodeType(item.node.SourceClass) == "MultiResult")
                     .GroupBy(item => item.node.Name, item => item.index, StringComparer.Ordinal))
        {
            int[] slots = indices.ToArray();
            if (slots.Length < 2) continue;
            CatalogTreeNode[] reordered = slots.Select(index => recursivelyPrepared[index])
                .OrderBy(node => ResultGroupCandidatePriority(node, resultSelectionOverrides))
                .ToArray();
            for (int index = 0; index < slots.Length; index++)
                recursivelyPrepared[slots[index]] = reordered[index];
        }
        return recursivelyPrepared;
    }

    private static int ResultGroupCandidatePriority(CatalogTreeNode node,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides)
    {
        MapTreeResultSelectionOverride[] requests = (resultSelectionOverrides ?? [])
            .Where(selection => string.Equals(selection.PlanName, node.Name, StringComparison.Ordinal)).ToArray();
        if (requests.Length > 1) throw new SourceFileChangedException();
        if (requests.SingleOrDefault() is { } request)
        {
            bool matches = request.SessionHdfResultOwner is { } owner
                ? SessionHdfResultOwnerEquals(node.SelectedSessionHdfResultOwner ?? node.Locator?.SessionHdfResultOwner, owner)
                : node.SelectedSessionHdfResultOwner is null && node.Locator?.SessionHdfResultOwner is null &&
                    node.AssetID is { Length: > 0 } asset && PathEquals(asset, request.PlanAssetID);
            return matches ? 0 : 2;
        }
        if (node.AssetID is { Length: > 0 } && node.SelectedSessionHdfResultOwner is null &&
            node.Locator?.SessionHdfResultOwner is null) return 0;
        return node.SelectedSessionHdfResultOwner is not null || node.Locator?.SessionHdfResultOwner is not null
            ? 1 : 2;
    }

    private static CatalogTreeNode BuildMapLayerNode(XElement element, string id, string rootName, int[] path,
        string projectDirectory)
    {
        string name = (string?)element.Attribute("Name") ?? "";
        string mapType = (string?)element.Attribute("MapType") ?? "";
        string? filename = (string?)element.Attribute("Filename");
        string? relative = (string?)element.Attribute("FilenameRelative");
        string? resolved = ResolveMapLayerPath(projectDirectory, relative, filename);
        bool usable = SupportedMapLayerTypes.Contains(mapType) && resolved is not null && File.Exists(resolved);
        MapLayerSourceDiagnostics? diagnostics = resolved is not null && IsVectorMapLayerType(mapType)
            ? ReadVectorMapLayerDiagnostics(mapType, resolved)
            : null;
        string sourceClass = mapType switch
        {
            "DiscreteRasterMapLayer" => "DiscreteRasterMapLayerVisualContext",
            "ImageRasterMapLayer" => "ImageMapLayerVisualContext",
            "SingleBandRasterMapLayer" => "RasterMapLayerVisualContext",
            "PointsVector" => "PointMapLayerVisualContext",
            "PolylinesVector" => "PolylineMapLayerVisualContext",
            "PolygonsVector" => "PolygonMapLayerVisualContext",
            "GdbDataset" => "GdbDatasetVisualContext",
            "GdbVectorLayer" => "GdbVectorLayerVisualContext",
            _ => "UnsupportedMapLayerNode"
        };
        var properties = element.Attributes().Where(attribute => attribute.Name != "Type" && attribute.Name != "Name")
            .Select(attribute => new CatalogTreeProperty("Source/" + attribute.Name.LocalName,
                attribute.Name.LocalName, attribute.Value)).ToList();
        return new(id, sourceClass, name, AssetID: resolved,
            Properties: properties.ToArray(), Checked: usable ? (ReadBool(element, "IsChecked") ? "checked" : "unchecked") : null,
            IsPinned: ReadBool(element, "IsPinned"), CanMove: usable,
            Locator: new CatalogTreeNodeLocator(rootName, "MapLayer", name, path,
                MapType: mapType, Filename: filename, FilenameRelative: relative),
            LayerErrors: diagnostics?.LayerErrors,
            DiagnosticStatus: diagnostics?.Status,
            DiagnosticSourceIdentity: diagnostics?.SourceIdentity,
            DiagnosticSourceRevision: diagnostics?.SourceRevision,
            DiagnosticReadError: diagnostics?.ReadError);
    }

    private static CatalogTreeNode BuildGdbMapNode(XElement element, string id, string rootName, int[] path,
        string projectDirectory)
    {
        string type = (string?)element.Attribute("Type") ?? "";
        string name = (string?)element.Attribute("Name") ?? "";
        string? filename = (string?)element.Attribute("Filename");
        string? relative = (string?)element.Attribute("FilenameRelative");
        string? resolved = ResolveGdbMapLayerPath(projectDirectory, relative, filename);
        string? assetID = resolved is null ? null : CanonicalPath(resolved);
        bool isDataset = type == "GdbDataset";
        bool valid = assetID is not null &&
            string.Equals(Path.GetExtension(assetID), ".gdb", StringComparison.OrdinalIgnoreCase);
        string? layerName = (string?)element.Attribute("GdbLayerName");
        string? vectorType = (string?)element.Attribute("GdbVectorType");
        if (type == "GdbVectorLayer")
        {
            XElement? parent = element.Parent;
            valid &= parent is not null && (string?)parent.Attribute("Type") == "GdbDataset" &&
                string.Equals((string?)parent.Attribute("Filename"), filename, StringComparison.Ordinal) &&
                string.Equals((string?)parent.Attribute("FilenameRelative"), relative, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(layerName) &&
                Enum.TryParse(vectorType, ignoreCase: false, out Geospatial.GDALAssist.VectorType parsedType) &&
                parsedType is not (Geospatial.GDALAssist.VectorType.None or Geospatial.GDALAssist.VectorType.Unknown);
        }
        else if (!isDataset)
        {
            valid = false;
        }

        var properties = element.Attributes().Where(attribute => attribute.Name != "Type" && attribute.Name != "Name")
            .Select(attribute => new CatalogTreeProperty("Source/" + attribute.Name.LocalName,
                attribute.Name.LocalName, attribute.Value)).ToArray();
        return new CatalogTreeNode(id,
            isDataset ? "GdbDatasetVisualContext" : "GdbVectorLayerVisualContext", name,
            AssetID: assetID, Properties: properties,
            Checked: valid ? (ReadBool(element, "IsChecked") ? "checked" : "unchecked") : null,
            IsPinned: ReadBool(element, "IsPinned"), CanMove: false,
            Locator: new CatalogTreeNodeLocator(rootName, type, name, path, assetID,
                MapType: isDataset ? "GdbDataset" : "GdbVectorLayer",
                Filename: filename, FilenameRelative: relative));
    }

    private static string? ResolveGdbMapLayerPath(string projectDirectory, string? relative, string? filename)
    {
        string? candidate = !string.IsNullOrWhiteSpace(relative)
            ? ResolvePath(projectDirectory, relative) : null;
        if (candidate is not null && Directory.Exists(candidate)) return candidate;
        candidate = !string.IsNullOrWhiteSpace(filename) ? ResolvePath(projectDirectory, filename) : null;
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }

    private static bool IsVectorMapLayerType(string mapType) =>
        mapType is "PointsVector" or "PolylinesVector" or "PolygonsVector";

    private static MapLayerSourceDiagnostics ReadVectorMapLayerDiagnostics(string mapType, string sourcePath)
    {
        string? sourceIdentity = null;
        try { sourceIdentity = CanonicalPath(sourcePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(null, "readFailed", null, null, error.Message);
        }

        MapLayerSourceRevision before;
        try { before = CaptureMapLayerSourceRevision(sourcePath); }
        catch (SourceFileChangedException)
        {
            return new(null, "sourceChanged", sourceIdentity, null, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(null, "readFailed", sourceIdentity, null, error.Message);
        }

        OperationResult? operation = null;
        string? readError = null;
        try { operation = ReadTypedVectorMapLayer(mapType, sourcePath); }
        catch (Exception error)
        {
            readError = error.Message;
        }

        MapLayerSourceRevision after;
        try { after = CaptureMapLayerSourceRevision(sourcePath); }
        catch (Exception)
        {
            return new(null, "sourceChanged", before.CanonicalPath, null, null);
        }

        if (!SameSourceRevision(before, after))
            return new(null, "sourceChanged", before.CanonicalPath, null, null);

        CatalogTreeLayerError[]? layerErrors = operation is null ? null : MaterializeMapLayerErrors(operation.Messages);
        if (operation is null || !operation.Result)
        {
            if (string.IsNullOrWhiteSpace(readError) && layerErrors is { Length: > 0 })
                readError = string.Join(Environment.NewLine,
                    layerErrors.Select(item => item.EvaluatedMessage).Where(message => !string.IsNullOrWhiteSpace(message)));
            return new(layerErrors, "readFailed", before.CanonicalPath, before.Revision, readError);
        }

        return new(layerErrors, "loaded", before.CanonicalPath, before.Revision, null);
    }

    private static OperationResult ReadTypedVectorMapLayer(string mapType, string path)
    {
        if (path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
        {
            return mapType switch
            {
                "PointsVector" => ShapefileIO.TryRead(path, out PointFeatureCollection _),
                "PolylinesVector" => ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection _),
                "PolygonsVector" => ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection _),
                _ => throw new ArgumentOutOfRangeException(nameof(mapType))
            };
        }

        if (!GeoJsonReader.HasJsonFileExtension(path))
            throw new NotImplementedException("Unsupported/unrecognized file format.");

        return mapType switch
        {
            "PointsVector" => GeoJsonReader.TryRead(path, out PointFeatureCollection _),
            "PolylinesVector" => GeoJsonReader.TryRead(path, out PolylineFeatureCollection _),
            "PolygonsVector" => GeoJsonReader.TryRead(path, out PolygonFeatureCollection _),
            _ => throw new ArgumentOutOfRangeException(nameof(mapType))
        };
    }

    private static CatalogTreeLayerError[] MaterializeMapLayerErrors(IReadOnlyList<Message>? messages) =>
        (messages ?? []).Select(message => new CatalogTreeLayerError(
            message.MessageType?.Code ?? string.Empty,
            message.MessageType?.DefaultSeverity.ToString() ?? "Unknown",
            message.EvaluatedMessage ?? string.Empty,
            message.LogData.Select(pair => new CatalogTreeLogDatum(pair.Key, pair.Value switch
            {
                null => null,
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => pair.Value.ToString()
            })).ToArray())).ToArray();

    private static MapLayerSourceRevision CaptureMapLayerSourceRevision(string path)
    {
        string[] consumedPaths = path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)
            ? [path, ShapefileIO.SHXFilename(path), ShapefileIO.DBFFilename(path)]
            : [path];
        MapLayerSourceFileRevision[] files = consumedPaths.Select(CaptureMapLayerSourceFileRevision).ToArray();
        MapLayerSourceFileRevision primary = files[0];
        if (!primary.Exists) throw new SourceFileChangedException();
        string primaryRevision = primary.Revision ?? throw new SourceFileChangedException();
        return new(primary.CanonicalPath, primaryRevision, files);
    }

    private static bool SameSourceRevision(MapLayerSourceRevision first, MapLayerSourceRevision second) =>
        SameCanonicalPath(first.CanonicalPath, second.CanonicalPath) && first.Files.Length == second.Files.Length &&
        first.Files.Zip(second.Files).All(pair => SameCanonicalPath(pair.First.CanonicalPath, pair.Second.CanonicalPath) &&
            pair.First.Exists == pair.Second.Exists &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));

    private static MapLayerSourceFileRevision CaptureMapLayerSourceFileRevision(string path)
    {
        string canonicalBefore = CanonicalPath(path);
        var source = new FileInfo(path);
        source.Refresh();
        if (!source.Exists) return new(canonicalBefore, false, null);
        long lengthBefore = source.Length;
        DateTime writeBefore = source.LastWriteTimeUtc;
        string revision = RevisionFile(path);
        source.Refresh();
        string canonicalAfter = CanonicalPath(path);
        if (!SameCanonicalPath(canonicalBefore, canonicalAfter) || !source.Exists ||
            source.Length != lengthBefore || source.LastWriteTimeUtc != writeBefore)
            throw new SourceFileChangedException();
        return new(canonicalBefore, true, revision);
    }

    private static bool SameCanonicalPath(string first, string second) =>
        string.Equals(first, second, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record MapLayerSourceRevision(string CanonicalPath, string Revision,
        MapLayerSourceFileRevision[] Files);
    private sealed record MapLayerSourceFileRevision(string CanonicalPath, bool Exists, string? Revision);
    private sealed record MapLayerSourceDiagnostics(CatalogTreeLayerError[]? LayerErrors, string Status,
        string? SourceIdentity, string? SourceRevision, string? ReadError);

    private static CatalogTreeNode BuildSerializedUnknownNode(XElement element, string id, string rootName,
        int[] path, int mapIndex, string projectDirectory, MapTreeGdbCatalogs gdbRasterCatalogs,
        HashSet<string> expanded,
        CatalogTreeNode[] resultCandidates, ref string? selected, GeometryOwner? parentGeometry,
        PlanOwner? parentPlan, ResultOwner? parentResult,
        List<CatalogTreeNode>? resultGroupCandidates,
        BoundaryConditionOwner? parentBoundaryCondition, string? protectedCallbackResultPath,
        MapTreeResultSelectionOverride[]? resultSelectionOverrides = null)
    {
        string type = (string?)element.Attribute("Type") ?? "Unknown";
        string name = (string?)element.Attribute("Name") ?? type;
        CatalogTreeNode[] matchingResults = type == "Result"
            ? resultCandidates.Where(candidate => candidate.Name == name).ToArray()
            : [];
        if (matchingResults.Length > 1) throw new SourceFileChangedException();
        CatalogTreeNode? resultCandidate = matchingResults.SingleOrDefault();
        CatalogTreeNode? groupCandidate = type == "MultiResult"
            ? FindResultGroupDefault(resultGroupCandidates, name, parentPlan, resultSelectionOverrides)
            : null;
        ResultOwner? childResult = type switch
        {
            "Result" when resultCandidate?.AssetID is { Length: > 0 } resultAsset =>
                new ResultOwner("Result", rootName, name, CanonicalPath(resultAsset), path,
                    RevisionFile(CanonicalPath(resultAsset))),
            "MultiResult" when groupCandidate?.SelectedResultAssetID is { Length: > 0 } selectedAsset &&
                                groupCandidate.SelectedResultRevision is { Length: > 0 } selectedRevision =>
                new ResultOwner("MultiResult", rootName, name, selectedAsset, path, selectedRevision,
                    groupCandidate.SelectedSessionHdfResultOwner),
            _ => parentResult
        };
        BoundaryConditionOwner? childBoundaryCondition = type == "BoundaryCondition"
            ? null : parentBoundaryCondition;
        bool isPrecipitationChild = type == "PrecipitationLayer" && parentBoundaryCondition is not null;
        CatalogTreeNode[] children = ApplySerializedChildren(element.Elements("Node").ToArray(), [], rootName,
            path, mapIndex, projectDirectory, gdbRasterCatalogs, expanded, resultCandidates, ref selected,
            parentGeometry, parentPlan, resultGroupCandidates, childResult, childBoundaryCondition,
            protectedCallbackResultPath, resultSelectionOverrides);
        AddState(element, id, defaultExpanded: type is "Root" or "Folder", expanded, ref selected);
        bool checkable = CheckableNodeTypes.Contains(type) && (type != "WindLayer" ||
            parentBoundaryCondition is not null && parentResult is null) &&
            (type != "PrecipitationLayer" || isPrecipitationChild);
        string sourceClass = resultCandidate?.SourceClass ?? (type == "MultiResult"
            ? "MultiResultNodeVM"
            : type == "WindLayer" && checkable ? "WindLayerVisualContext"
            : type == "PrecipitationLayer" && checkable ? "PrecipitationLayerVisualContext"
            : "Serialized" + type + "Node");
        return new CatalogTreeNode(id, sourceClass, name,
            LocalizeName: (type is "WindLayer" or "PrecipitationLayer") && checkable,
            Children: children.Length == 0 ? null : children,
            AssetID: (type == "WindLayer" && checkable) || isPrecipitationChild
                ? parentBoundaryCondition!.AssetID : type == "MultiResult"
                    ? groupCandidate?.AssetID : resultCandidate?.AssetID,
            CatalogSummary: resultCandidate?.CatalogSummary,
            Properties: element.Attributes().Where(attribute => attribute.Name != "Type" && attribute.Name != "Name")
                .Select(attribute => new CatalogTreeProperty("Source/" + attribute.Name.LocalName,
                    attribute.Name.LocalName, attribute.Value)).Concat(resultCandidate?.Properties ?? []).ToArray(),
            Checked: checkable ? (ReadBool(element, "IsChecked") ? "checked" : "unchecked") : null,
            IsPinned: ReadBool(element, "IsPinned"),
            CanMove: MovableNodeTypes.Contains(type),
            Locator: new CatalogTreeNodeLocator(rootName, type, name, path,
                (type == "WindLayer" && checkable) || isPrecipitationChild
                    ? parentBoundaryCondition!.AssetID : type == "MultiResult"
                        ? groupCandidate?.AssetID : resultCandidate?.AssetID,
                type == "MapLayer" ? (string?)element.Attribute("MapType") : null,
                type == "MapLayer" ? (string?)element.Attribute("Filename") : null,
                type == "MapLayer" ? (string?)element.Attribute("FilenameRelative") : null,
                ParentPlanAssetID: parentPlan?.AssetID,
                ParentPlanNodePath: parentPlan?.NodePath,
                ParentPlanNodeName: parentPlan?.Name,
                ParentResultNodeType: type is not ("Result" or "MultiResult") ? parentResult?.NodeType : null,
                ParentResultAssetID: type is not ("Result" or "MultiResult") ? parentResult?.AssetID : null,
                ParentResultNodePath: type is not ("Result" or "MultiResult") ? parentResult?.NodePath : null,
                ParentResultNodeName: type is not ("Result" or "MultiResult") ? parentResult?.Name : null,
                ParentResultRevision: type is not ("Result" or "MultiResult") ? parentResult?.Revision : null,
                SessionHdfResultOwner: type == "MultiResult" ? childResult?.SessionHdfResultOwner :
                    parentResult?.SessionHdfResultOwner,
                ParentBoundaryConditionAssetID: (type == "WindLayer" && parentResult is null) || isPrecipitationChild
                    ? parentBoundaryCondition?.AssetID : null,
                ParentBoundaryConditionNodePath: (type == "WindLayer" && parentResult is null) || isPrecipitationChild
                    ? parentBoundaryCondition?.NodePath : null,
                ParentBoundaryConditionNodeName: (type == "WindLayer" && parentResult is null) || isPrecipitationChild
                    ? parentBoundaryCondition?.Name : null));
    }

    private static void AddState(XElement element, string id, bool defaultExpanded, HashSet<string> expanded,
        ref string? selected)
    {
        bool hasExpanded = bool.TryParse((string?)element.Attribute("IsExpanded"), out bool explicitExpanded);
        if (hasExpanded ? explicitExpanded : defaultExpanded)
            expanded.Add(id);
        if (ReadBool(element, "IsIsSelected") && selected is null) selected = id;
    }

    private static bool AddFolder(XElement map, CatalogTreeNodeLocator target, string rawName)
    {
        string name = ValidateName(rawName, "folder");
        XElement parent = ResolveFolderTarget(map, target, createRoot: true);
        if (parent.Elements("Node").Any(node => string.Equals((string?)node.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A folder with that name already exists in this tree section.");
        parent.Add(new XElement("Node", new XAttribute("Type", "Folder"), new XAttribute("Name", name)));
        return true;
    }

    private static bool RenameFolder(XElement map, CatalogTreeNodeLocator locator, string rawName)
    {
        string name = ValidateName(rawName, "folder");
        if (locator.NodeType != "Folder") throw new NotSupportedException("Only source Folder nodes can be renamed in the map tree.");
        XElement folder = ResolveNode(map, locator, required: true)!;
        XElement? parent = folder.Parent;
        if (parent is null || parent.Name != "Node") throw new SourceFileChangedException();
        if (parent.Elements("Node").Any(node => !ReferenceEquals(node, folder) &&
            string.Equals((string?)node.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A folder with that name already exists in this tree section.");
        if (string.Equals((string?)folder.Attribute("Name"), name, StringComparison.Ordinal)) return false;
        folder.SetAttributeValue("Name", name);
        return true;
    }

    private static bool SetVisibility(MapTreeRequest mapRequest, XElement map,
        CatalogTreeNodeLocator locator, bool checkedValue)
    {
        if (!CheckableNodeTypes.Contains(locator.NodeType))
            throw new NotSupportedException("Visibility is unavailable for this source tree node.");
        if (locator.NodeType == "MapLayer" && (locator.MapType is null || !SupportedMapLayerTypes.Contains(locator.MapType)))
            throw new NotSupportedException("Visibility is unavailable for this unsupported map layer type.");
        if (checkedValue && locator.NodeType == "MapLayer" &&
            MapRasterLayerRendering.SupportedTypes.Contains(locator.MapType ?? "", StringComparer.Ordinal) &&
            locator.NodePath is { Length: >= 2 })
        {
            XElement? serialized = ResolveNode(map, locator, required: false);
            if ((string?)serialized?.Parent?.Attribute("Type") == "GdbDataset")
            {
                CatalogTreeNode exact = FindExactNode(BuildTree(mapRequest, map).Roots, locator);
                if (exact.Checked is null)
                    throw new NotSupportedException(exact.DiagnosticReadError ??
                        "The selected GDB raster subdataset is not available for map rendering.");
            }
        }
        if (locator.NodeType != "MapLayer")
            return SetSourceVisibility(mapRequest, map, locator, checkedValue);
        XElement? node = ResolveNode(map, locator, required: false);
        if (node is null)
        {
            if (!checkedValue) return false;
            XElement root = EnsureRootNode(map, locator.RootName);
            node = new XElement("Node", new XAttribute("Type", locator.NodeType), new XAttribute("Name", locator.NodeName));
            ApplyLocatorAttributes(node, locator);
            node.SetAttributeValue("IsChecked", true);
            root.Add(node);
            return true;
        }
        if (ReadBool(node, "IsChecked") == checkedValue && (!checkedValue || node.Attribute("IsChecked") is not null)) return false;
        if (checkedValue) node.SetAttributeValue("IsChecked", true);
        else node.Attribute("IsChecked")?.Remove();
        if (!checkedValue && node.Elements("Node").Any() == false &&
            node.Attributes().All(attribute => attribute.Name == "Type" || attribute.Name == "Name"))
            node.Remove();
        return true;
    }

    private static bool SetSourceVisibility(MapTreeRequest request, XElement map,
        CatalogTreeNodeLocator locator, bool checkedValue)
    {
        CatalogTreeNode[] currentRoots = BuildTree(request, map).Roots;
        _ = FindExactNode(currentRoots, locator);
        ValidateResultOwner(request, locator);
        XElement? savedTarget = ResolveNode(map, locator, required: false);
        if (!checkedValue)
        {
            if (savedTarget is null || !ReadBool(savedTarget, "IsChecked")) return false;
            savedTarget.Attribute("IsChecked")?.Remove();
            // Source membership can be explicit even when all display options
            // are defaults. RasTreeBase.SerializeDefault keeps Type/Name for
            // unchecked Result, MultiResult, Terrain, NValue and Classification
            // rows; deleting one here removes it from the map instead of hiding it.
            if (locator.NodeType is not ("Result" or "MultiResult" or "Terrain" or "NValue" or "Classification") &&
                !savedTarget.Elements("Node").Any() &&
                savedTarget.Attributes().All(attribute => attribute.Name == "Type" || attribute.Name == "Name"))
                savedTarget.Remove();
            return true;
        }

        CatalogTreeNode[] chain = FindNodeChain(currentRoots, locator) ?? throw new SourceFileChangedException();
        XElement before = new(map);
        string rootName = locator.RootName;
        XElement parent = EnsureRootNode(map, rootName);
        for (int index = 1; index < chain.Length; index++)
        {
            CatalogTreeNode sourceNode = chain[index];
            CatalogTreeNodeLocator sourceLocator = sourceNode.Locator ?? throw new SourceFileChangedException();
            string sourceType = sourceLocator.NodeType;
            XElement[] siblings = parent.Elements("Node").Where(node =>
                string.Equals((string?)node.Attribute("Type"), sourceType, StringComparison.Ordinal) &&
                string.Equals((string?)node.Attribute("Name"), sourceLocator.NodeName, StringComparison.Ordinal)).ToArray();
            if (siblings.Length > 1) throw new SourceFileChangedException();
            XElement? child = siblings.SingleOrDefault();
            if (sourceLocator.NodePath is { Length: > 0 })
            {
                XElement? exact = ResolveNode(map, sourceLocator, required: false);
                if (exact is not null && !ReferenceEquals(exact.Parent, parent))
                    throw new SourceFileChangedException();
                if (child is not null && exact is not null && !ReferenceEquals(child, exact))
                    throw new SourceFileChangedException();
                child ??= exact;
            }

            if (sourceType == "Folder")
            {
                child ??= new XElement("Node", new XAttribute("Type", "Folder"),
                    new XAttribute("Name", sourceLocator.NodeName));
                if (child.Parent is null) parent.Add(child);
            }
            else if (child is null)
            {
                if (SourceNodeType(sourceNode.SourceClass) is null)
                    throw new NotSupportedException("The selected source map node cannot be materialized safely.");
                child = SerializeSourceDefault(sourceNode, existing: null, associationStates: null);
                parent.Add(child);
            }

            if (CheckableNodeTypes.Contains(sourceType) &&
                (!ReadBool(child!, "IsChecked") || child!.Attribute("IsChecked") is null))
                child!.SetAttributeValue("IsChecked", true);
            parent = child!;
        }

        ValidateResultOwner(request, locator);
        return !XNode.DeepEquals(before, map);
    }

    private static CatalogTreeNode[]? FindNodeChain(IEnumerable<CatalogTreeNode> nodes,
        CatalogTreeNodeLocator requested)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            if (node.Locator is { } candidate && LocatorIdentityEquals(candidate, requested))
                return [node];
            if (node.Children is not { Length: > 0 } children) continue;
            CatalogTreeNode[]? descendant = FindNodeChain(children, requested);
            if (descendant is not null) return [node, .. descendant];
        }
        return null;
    }

    private static void ValidateResultOwner(MapTreeRequest request, CatalogTreeNodeLocator locator)
    {
        if (locator.SessionHdfResultOwner is { } sessionOwner)
        {
            if (locator.NodeType == "MultiResult" && locator.AssetID is null &&
                string.Equals(locator.NodeName, sessionOwner.EmbeddedPlanName, StringComparison.Ordinal) &&
                locator.ParentResultNodeType is null && locator.ParentResultAssetID is null &&
                locator.ParentResultNodePath is null && locator.ParentResultNodeName is null &&
                locator.ParentResultRevision is null)
            {
                ValidateSessionHdfResultOwner(request, sessionOwner);
                return;
            }
            if (locator.ParentResultNodeType != "MultiResult" ||
                locator.ParentResultAssetID is not { Length: > 0 } parentResultPath ||
                locator.ParentResultRevision is not { Length: > 0 } parentResultRevision ||
                !string.Equals(locator.ParentResultNodeName, sessionOwner.EmbeddedPlanName,
                    StringComparison.Ordinal))
                throw new SourceFileChangedException();
            ValidateSessionHdfResultSelection(request, sessionOwner, parentResultPath,
                parentResultRevision);
            return;
        }
        if (locator.ParentResultNodeType is null && locator.ParentResultAssetID is null &&
            locator.ParentResultNodePath is null && locator.ParentResultNodeName is null &&
            locator.ParentResultRevision is null) return;
        if (locator.ParentResultNodeType is not ("Result" or "MultiResult") ||
            string.IsNullOrWhiteSpace(locator.ParentResultAssetID) ||
            string.IsNullOrWhiteSpace(locator.ParentResultNodeName) ||
            string.IsNullOrWhiteSpace(locator.ParentResultRevision))
            throw new SourceFileChangedException();
        OperationContext context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        try
        {
            ProjectResultIdentity result = ReadProjectResult(context, locator.ParentResultAssetID,
                locator.ParentResultRevision);
            if (locator.ParentResultNodeType == "Result")
            {
                if (!string.Equals(result.Name, locator.ParentResultNodeName, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                if (result.PlanName is not null)
                    ValidateSerializedPlanAssociatedDirectResultOwner(context, request, locator, result);
            }
            else if (!string.Equals(result.PlanName, locator.ParentResultNodeName, StringComparison.Ordinal) ||
                     locator.ParentPlanAssetID is { Length: > 0 } planAsset &&
                     (result.PlanAssetPath is null || !PathEquals(result.PlanAssetPath, planAsset)))
            {
                throw new SourceFileChangedException();
            }
        }
        finally
        {
            context.Dispose();
        }
    }

    internal static void ValidateSessionHdfResultOwner(MapTreeRequest request,
        SessionHdfResultOwner owner)
    {
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.ProjectSessionID ?? request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.ExternalCoreLayerReferences ?? request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        ValidateSessionHdfResultOwner(context, owner);
    }

    private static void ValidateSessionHdfResultSelection(MapTreeRequest request,
        SessionHdfResultOwner owner, string selectedResultPath, string selectedResultRevision)
    {
        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.ProjectSessionID ?? request.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: request.ExternalCoreLayerReferences ?? request.CoreLayerContext?.References,
            coreLayerContext: request.CoreLayerContext);
        ValidateSessionHdfResultSelection(context, owner, selectedResultPath, selectedResultRevision);
    }

    private static void ValidateSerializedPlanAssociatedDirectResultOwner(OperationContext context,
        MapTreeRequest request, CatalogTreeNodeLocator locator, ProjectResultIdentity result)
    {
        if (result.SameNameCount != 1 ||
            locator.ParentResultNodePath is not { Length: >= 2 } resultPath ||
            string.IsNullOrWhiteSpace(locator.ParentResultAssetID) ||
            !string.Equals(locator.ParentResultNodeName, result.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string resultPathOnDisk = CanonicalPath(locator.ParentResultAssetID);
        if (!RevisionFile(resultPathOnDisk).Equals(locator.ParentResultRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        XElement map = ResolveMap(context.Visualization, request.MapIndex, request.MapName, request.MapType);
        var resultLocator = new CatalogTreeNodeLocator(locator.RootName, "Result", result.Name,
            NodePath: resultPath, AssetID: resultPathOnDisk);
        CatalogTreeNode exactOwner = FindExactNode(BuildTree(request, map).Roots, resultLocator);
        if (exactOwner.Locator is not { NodeType: "Result", NodePath: { } actualPath,
                AssetID: { Length: > 0 } actualAssetPath } ||
            !actualPath.SequenceEqual(resultPath) || !PathEquals(actualAssetPath, resultPathOnDisk))
            throw new SourceFileChangedException();

        XElement serializedOwner = ResolveNode(map, resultLocator, required: true)!;
        if ((string?)serializedOwner.Attribute("Type") != "Result" ||
            !string.Equals((string?)serializedOwner.Attribute("Name"), result.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static bool SetGeometryChildVisibility(MapTreeRequest mapRequest, XElement map,
        CatalogTreeNodeLocator locator, bool checkedValue)
    {
        string rootName = GeometryRootNameForMap(mapRequest.MapType);
        if (!string.Equals(locator.RootName, rootName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(locator.ParentGeometryAssetID) ||
            string.IsNullOrWhiteSpace(locator.ParentGeometryNodeName))
            throw new ArgumentException("The Geometry child locator is missing its exact parent identity.");

        string[] focusedIDs = mapRequest.MapType == "Project" ? [] :
            SerializedGeometryAssetIDs(mapRequest, map, rootName);
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            mapRequest.ProjectPath, mapRequest.ProjectXml, FocusedGeometryIds: focusedIDs,
            ProjectSessionID: mapRequest.ProjectSessionID,
            ExternalCoreLayerReferences: mapRequest.ExternalCoreLayerReferences,
            CoreLayerContext: mapRequest.CoreLayerContext), mapRequest.MapType);
        CatalogTreeNode[] owners = Descendants(defaults)
            .Where(node => SourceNodeType(node.SourceClass) == "Geometry" &&
                node.Name == locator.ParentGeometryNodeName &&
                node.AssetID is { Length: > 0 } assetID &&
                PathEquals(assetID, locator.ParentGeometryAssetID))
            .ToArray();
        if (owners.Length != 1)
            throw new SourceFileChangedException();
        CatalogTreeNode geometryDefault = owners[0];
        if (geometryDefault.AssetID is not { Length: > 0 } ownerAssetID ||
            !PathEquals(ownerAssetID, locator.ParentGeometryAssetID))
            throw new SourceFileChangedException();
        CatalogTreeNode[] sourceChildren = (geometryDefault.Children ?? [])
            .Where(node => SourceNodeType(node.SourceClass) == locator.NodeType && node.Name == locator.NodeName)
            .ToArray();
        if (sourceChildren.Length != 1)
            throw new SourceFileChangedException();

        XElement? geometryElement = ResolveGeometryOwnerElement(map, locator);
        if (geometryElement is null)
        {
            if (!checkedValue) return false;
            if (mapRequest.MapType != "Project") throw new SourceFileChangedException();
            XElement root = EnsureRootNode(map, rootName);
            geometryElement = new XElement("Node",
                new XAttribute("Type", "Geometry"),
                new XAttribute("Name", geometryDefault.Name),
                new XAttribute("IsChecked", true));
            root.Add(geometryElement);
        }

        XElement[] matchingChildren = geometryElement.Elements("Node")
            .Where(node => (string?)node.Attribute("Type") == locator.NodeType &&
                           (string?)node.Attribute("Name") == locator.NodeName)
            .ToArray();
        if (matchingChildren.Length > 1) throw new SourceFileChangedException();
        XElement? child = matchingChildren.SingleOrDefault();
        if (locator.NodePath is { Length: > 0 })
        {
            XElement? exact = ResolveNode(map, locator, required: false);
            if (exact is null || !ReferenceEquals(exact.Parent, geometryElement) ||
                (child is not null && !ReferenceEquals(child, exact)))
                throw new SourceFileChangedException();
            child = exact;
        }

        if (child is null)
        {
            if (!checkedValue) return false;
            child = new XElement("Node",
                new XAttribute("Type", locator.NodeType),
                new XAttribute("Name", locator.NodeName));
            geometryElement.Add(child);
        }

        bool changed = false;
        if (checkedValue)
        {
            foreach (XElement ancestor in child.AncestorsAndSelf("Node"))
            {
                string? type = (string?)ancestor.Attribute("Type");
                if (type is not null && CheckableNodeTypes.Contains(type) &&
                    (!ReadBool(ancestor, "IsChecked") || ancestor.Attribute("IsChecked") is null))
                {
                    ancestor.SetAttributeValue("IsChecked", true);
                    changed = true;
                }
            }
        }
        else if (child.Attribute("IsChecked") is not null)
        {
            child.Attribute("IsChecked")!.Remove();
            changed = true;
        }

        if (!checkedValue && child.Elements("Node").Any() == false &&
            child.Attributes().All(attribute => attribute.Name == "Type" || attribute.Name == "Name"))
        {
            child.Remove();
            changed = true;
        }
        return changed;
    }

    private static string GeometryRootNameForMap(string mapType) => mapType switch
    {
        "Project" => "Geometries",
        "Buildable" => "Layers",
        "Geometry" => "Focused Geometries",
        _ => throw new NotSupportedException("Geometry children are not available for the selected map type.")
    };

    private static XElement? ResolveGeometryOwnerElement(XElement map, CatalogTreeNodeLocator childLocator)
    {
        XElement nodes = EnsureTreeNodes(map);
        XElement? root = nodes.Elements("Node").FirstOrDefault(node =>
            (string?)node.Attribute("Type") == "Root" &&
            (string?)node.Attribute("Name") == childLocator.RootName);
        if (childLocator.ParentGeometryNodePath is { Length: > 0 } path)
        {
            var locator = new CatalogTreeNodeLocator(childLocator.RootName, "Geometry",
                childLocator.ParentGeometryNodeName!, path, childLocator.ParentGeometryAssetID);
            XElement? exact = ResolveNode(map, locator, required: false);
            if (exact is null || root is null ||
                !exact.AncestorsAndSelf("Node").Any(ancestor => ReferenceEquals(ancestor, root)))
                throw new SourceFileChangedException();
            return exact;
        }

        if (root is null) return null;
        XElement[] matches = root.Descendants("Node").Where(node =>
            (string?)node.Attribute("Type") == "Geometry" &&
            (string?)node.Attribute("Name") == childLocator.ParentGeometryNodeName).ToArray();
        if (matches.Length > 1) throw new SourceFileChangedException();
        return matches.SingleOrDefault();
    }

    private static bool MoveNodes(XElement map, CatalogTreeNodeLocator?[] requestedNodes,
        CatalogTreeNodeLocator targetLocator)
    {
        CatalogTreeNodeLocator[] locators = requestedNodes.Where(locator => locator is not null).Cast<CatalogTreeNodeLocator>()
            .Distinct().ToArray();
        if (locators.Length == 0 || locators.Any(locator => !MovableNodeTypes.Contains(locator.NodeType)))
            throw new NotSupportedException("One or more selected source tree nodes cannot be moved.");
        if (locators.Any(locator => locator.RootName != targetLocator.RootName))
            throw new ArgumentException("Source tree nodes can move only within the same root section.");
        if (targetLocator.NodeType != "Folder")
            throw new ArgumentException("Moved nodes require a source Folder destination.");
        XElement target = ResolveFolderTarget(map, targetLocator, createRoot: false);
        var sourceElements = new List<(CatalogTreeNodeLocator Locator, XElement? Element)>();
        foreach (CatalogTreeNodeLocator locator in locators)
        {
            if (locator.NodeType == "Root") throw new ArgumentException("A source tree root cannot be moved into a folder.");
            XElement? element = ResolveNode(map, locator, required: false);
            if (element is null && locator.NodePath is not null) throw new SourceFileChangedException();
            if (element is not null && (ReferenceEquals(element, target) ||
                target.Ancestors("Node").Any(ancestor => ReferenceEquals(ancestor, element))))
                throw new ArgumentException("A folder cannot be moved into itself or one of its descendants.");
            sourceElements.Add((locator, element));
        }
        for (int left = 0; left < sourceElements.Count; left++)
        for (int right = 0; right < sourceElements.Count; right++)
        {
            if (left == right || sourceElements[left].Element is not { } ancestor || sourceElements[right].Element is not { } child) continue;
            if (child.Ancestors("Node").Any(candidate => ReferenceEquals(candidate, ancestor)))
                throw new ArgumentException("A selected folder cannot contain another selected source node.");
        }

        bool changed = false;
        foreach ((CatalogTreeNodeLocator locator, XElement? element) in sourceElements)
        {
            if (element is null)
            {
                var created = new XElement("Node", new XAttribute("Type", locator.NodeType),
                    new XAttribute("Name", locator.NodeName));
                ApplyLocatorAttributes(created, locator);
                target.Add(created);
                changed = true;
                continue;
            }
            if (ReferenceEquals(element.Parent, target)) continue;
            element.Remove();
            target.Add(element);
            changed = true;
        }
        return changed;
    }

    private static XElement ResolveFolderTarget(XElement map, CatalogTreeNodeLocator target, bool createRoot)
    {
        if (target.NodeType is not ("Root" or "Folder"))
            throw new ArgumentException("New folders and moved nodes require a source tree root or folder destination.");
        XElement? destination = ResolveNode(map, target, required: false);
        if (destination is null && target.NodeType == "Root" && createRoot)
            destination = EnsureRootNode(map, target.RootName);
        if (destination is null || (string?)destination.Attribute("Type") != target.NodeType ||
            (string?)destination.Attribute("Name") != target.NodeName)
            throw new SourceFileChangedException();
        return destination;
    }

    private static XElement? ResolveNode(XElement map, CatalogTreeNodeLocator locator, bool required)
    {
        XElement nodes = EnsureTreeNodes(map);
        XElement? found = null;
        if (locator.NodePath is { Length: > 0 } path)
        {
            XElement[] siblings = nodes.Elements("Node").ToArray();
            foreach (int index in path)
            {
                if (index < 0 || index >= siblings.Length) throw new SourceFileChangedException();
                found = siblings[index];
                siblings = found.Elements("Node").ToArray();
            }
            if ((string?)found?.Attribute("Type") != locator.NodeType ||
                (string?)found?.Attribute("Name") != locator.NodeName) throw new SourceFileChangedException();
        }
        else
        {
            XElement? root = nodes.Elements("Node").FirstOrDefault(node =>
                (string?)node.Attribute("Type") == "Root" && (string?)node.Attribute("Name") == locator.RootName);
            if (locator.NodeType == "Root") found = root;
            else if (root is not null)
            {
                XElement[] matches = root.Descendants("Node").Where(node =>
                    (string?)node.Attribute("Type") == locator.NodeType && (string?)node.Attribute("Name") == locator.NodeName).ToArray();
                if (matches.Length > 1) throw new NotSupportedException("The source map contains duplicate tree nodes that cannot be distinguished safely.");
                found = matches.SingleOrDefault();
            }
        }
        if (found is null && required) throw new SourceFileChangedException();
        return found;
    }

    private static XElement EnsureRootNode(XElement map, string rootName)
    {
        XElement nodes = EnsureTreeNodes(map);
        return nodes.Elements("Node").FirstOrDefault(node =>
            (string?)node.Attribute("Type") == "Root" && (string?)node.Attribute("Name") == rootName)
            ?? AddRootNode(nodes, rootName);
    }

    private static XElement AddRootNode(XElement nodes, string name)
    {
        var root = new XElement("Node", new XAttribute("Type", "Root"), new XAttribute("Name", name));
        nodes.Add(root);
        return root;
    }

    private static XElement EnsureTreeNodes(XElement map)
    {
        XElement tree = map.Element("Tree") ?? new XElement("Tree");
        if (tree.Parent is null) map.Add(tree);
        XElement nodes = tree.Element("Nodes") ?? new XElement("Nodes");
        if (nodes.Parent is null) tree.Add(nodes);
        return nodes;
    }

    private static void ApplyLocatorAttributes(XElement node, CatalogTreeNodeLocator locator)
    {
        if (locator.MapType is not null) node.SetAttributeValue("MapType", locator.MapType);
        if (locator.Filename is not null) node.SetAttributeValue("Filename", locator.Filename);
        if (locator.FilenameRelative is not null) node.SetAttributeValue("FilenameRelative", locator.FilenameRelative);
    }

    private static string? ResolveMapLayerPath(string projectDirectory, string? relative, string? filename)
    {
        string? candidate = !string.IsNullOrWhiteSpace(relative) ? ResolvePath(projectDirectory, relative) : null;
        if (candidate is not null && File.Exists(candidate)) return candidate;
        candidate = !string.IsNullOrWhiteSpace(filename) ? ResolvePath(projectDirectory, filename) : null;
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    private static string? Property(CatalogTreeNode node, string name) =>
        node.Properties?.FirstOrDefault(property => property.Id == name)?.Value;

    private static string? SourceNodeType(string sourceClass) => sourceClass switch
    {
        "ObservableGeometry" or "GeometryNode" or "GeometryNodeVM" => "Geometry",
        "ObservablePlan" or "PlanNode" or "PlanNodeVM" => "Plan",
        "ObservableResult" or "ResultNode" or "ResultNodeVM" => "Result",
        "MultiResultNodeVM" or "MultiResultNode" => "MultiResult",
        "ObservableTerrain" or "TerrainNode" or "TerrainNodeVM" => "Terrain",
        "ChannelModTerrainInputParentVC" or "LeveeModTerrainInputVC" or
            "PolygonRasterModTerrainInputVisualContext" or "EllipseRasterModTerrainInputVisualContext" => "TerrainInput",
        "ObservableBoundaryCondition" or "BoundaryConditionNode" or "BoundaryConditionNodeVM" => "BoundaryCondition",
        "ObservableNValueLayer" or "NValueNode" or "NValueNodeVM" => "NValue",
        "ObservableClassificationLayer" or "ClassificationNode" or "ClassificationNodeVM" => "Classification",
        "FlowAreaLayerVisualContext" => "FlowAreaLayer",
        "ConceptualMeshLayerVisualContext" or "TopologyLayerVisualContext" => "ConceptualMesh",
        "CulvertBarrelLayerVisualContext" => "CulvertBarrelLayer",
        "BoundaryConditionLineLayerVC" => "BoundaryConditionLineLayer",
        "PrecipitationLayerVisualContext" => "PrecipitationLayer",
        "WindLayerVisualContext" => "WindLayer",
        _ => null
    };

    private static string NodeID(int mapIndex, int[] path) =>
        $"vis-map-{mapIndex}-tree-{string.Join('.', path)}";

    private static bool ReadBool(XElement element, string attribute) =>
        bool.TryParse((string?)element.Attribute(attribute), out bool value) && value;

    private static string ValidateName(string value, string kind)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 128 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a valid " + kind + " name.");
        return name;
    }

    private static string NextMapName(XElement[] maps)
    {
        var names = maps.Select(map => (string?)map.Attribute("Name") ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 100_000; index++)
        {
            string candidate = index == 0 ? "Map" : "Map " + index;
            if (!names.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not find a unique map name.");
    }

    private static XElement EnsureMaps(XElement root)
    {
        if (root.Name != "Ras") throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        XElement? maps = root.Element("Maps");
        if (maps is not null) return maps;
        maps = new XElement("Maps");
        root.Add(maps);
        return maps;
    }

    private static XElement ResolveMap(XDocument visualization, int mapIndex, string mapName, string mapType)
    {
        XElement[] maps = visualization.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (mapIndex < 0 || mapIndex >= maps.Length) throw new SourceFileChangedException();
        XElement map = maps[mapIndex];
        if ((string?)map.Attribute("Name") != mapName || (string?)map.Attribute("Type") != mapType)
            throw new SourceFileChangedException();
        if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("Persistent map-tree actions are supported only for Project, Buildable, Geometry and Plan maps.");
        return map;
    }

    private static OperationContext Open(string projectPath, string? projectXml, string? projectXmlSHA,
        string? expectedVisualizationRevision, bool allowMissing, string? projectSessionID = null,
        CoreLayerReferenceCapture[]? externalCoreLayerReferences = null,
        CoreLayerRequestContext? coreLayerContext = null, bool discoverVisualizationRevision = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("The project path is required.");
        string projectAliasPath = Path.GetFullPath(projectPath);
        string canonicalProject = CanonicalPath(projectAliasPath);
        if (!Path.GetExtension(projectAliasPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The map tree requires a saved HEC-RAS project file.");
        if (!File.Exists(canonicalProject)) throw new FileNotFoundException("The current project file was not found.", canonicalProject);
        if (coreLayerContext is not null)
        {
            CoreLayerImportService.ValidateContext(coreLayerContext, canonicalProject);
            if (projectSessionID is not null &&
                !string.Equals(projectSessionID, coreLayerContext.ProjectSessionID, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            if (externalCoreLayerReferences is { Length: > 0 } legacyReferences &&
                !legacyReferences.SequenceEqual(coreLayerContext.References))
                throw new SourceFileChangedException();
            projectSessionID = coreLayerContext.ProjectSessionID;
            externalCoreLayerReferences = coreLayerContext.References;
        }
        else if (externalCoreLayerReferences is { Length: > 0 })
        {
            throw new SourceFileChangedException();
        }
        ValidateProjectXml(projectXml, projectXmlSHA);
        string diskProjectRevision = RevisionFile(canonicalProject);
        string visualizationAliasPath = Path.ChangeExtension(projectAliasPath, ".vis");
        string visualizationPath = CanonicalPath(visualizationAliasPath);
        bool exists = File.Exists(visualizationAliasPath);
        string? previousRevision = exists ? RevisionFile(visualizationPath) : null;
        if (!discoverVisualizationRevision && !SameRevision(previousRevision, expectedVisualizationRevision))
            throw new SourceFileChangedException();
        if (!exists && !allowMissing) throw new FileNotFoundException("The current project's visualization file was not found.", visualizationAliasPath);
        XDocument visualization;
        if (!exists)
        {
            visualization = new XDocument(new XElement("Ras", new XElement("Maps")));
        }
        else
        {
            var info = new FileInfo(visualizationPath);
            if (info.Length > MaximumVisualizationBytes) throw new NotSupportedException("The visualization file is too large to update.");
            visualization = ParseXml(File.ReadAllBytes(visualizationPath), MaximumVisualizationBytes);
            if (visualization.Root?.Name != "Ras") throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        }
        return new OperationContext(projectAliasPath, canonicalProject, visualizationAliasPath,
            visualizationPath, diskProjectRevision, previousRevision, projectXml, visualization,
            projectSessionID, externalCoreLayerReferences, coreLayerContext);
    }

    private static void ValidateProjectXml(string? projectXml, string? projectXmlSHA)
    {
        if (projectXml is null)
        {
            if (!string.IsNullOrEmpty(projectXmlSHA)) throw new ArgumentException("The project XML revision has no project XML snapshot.");
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(projectXml);
        if (bytes.LongLength > MaximumProjectXmlBytes) throw new NotSupportedException("The current project XML snapshot is too large.");
        if (string.IsNullOrWhiteSpace(projectXmlSHA) || !Revision(bytes).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var document = ParseXml(bytes, MaximumProjectXmlBytes);
        if (document.Root?.Name != "Project") throw new InvalidDataException("The current project XML snapshot has an invalid Project root element.");
    }

    private static XDocument ParseXml(byte[] bytes, long maximumBytes)
    {
        if (bytes.LongLength > maximumBytes) throw new NotSupportedException("The XML document is too large.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
            IgnoreWhitespace = false
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string Commit(OperationContext context)
    {
        string tempPath = context.VisualizationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                context.Visualization.Save(stream, SaveOptions.DisableFormatting);
                stream.Flush(flushToDisk: true);
            }
            // Prepare the receipt while every remaining failure still leaves
            // the original visualization untouched. Once the move succeeds,
            // returning this value performs no further file IO.
            string committedRevision = RevisionFile(tempPath);
            VerifyBeforeCommit(context);
            if (context.PreviousVisualizationRevision is null)
            {
                if (File.Exists(context.VisualizationPath)) throw new SourceFileChangedException();
                File.Move(tempPath, context.VisualizationPath);
            }
            else
            {
                if (!File.Exists(context.VisualizationPath) ||
                    !RevisionFile(context.VisualizationPath).Equals(context.PreviousVisualizationRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
                File.Move(tempPath, context.VisualizationPath, overwrite: true);
            }
            return committedRevision;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void VerifyBeforeCommit(OperationContext context)
    {
        if (!File.Exists(context.ProjectAliasPath) || !PathEquals(context.ProjectAliasPath, context.ProjectPath) ||
            !File.Exists(context.ProjectPath) || !RevisionFile(context.ProjectPath).Equals(context.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!PathEquals(context.VisualizationAliasPath, context.VisualizationPath)) throw new SourceFileChangedException();
        if (context.PreviousVisualizationRevision is null)
        {
            if (File.Exists(context.VisualizationAliasPath)) throw new SourceFileChangedException();
        }
        else if (!File.Exists(context.VisualizationPath) ||
                 !RevisionFile(context.VisualizationPath).Equals(context.PreviousVisualizationRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceFileChangedException();
        }
    }

    private static string ResolvePath(string directory, string value)
    {
        string normalized = value.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(directory, normalized));
    }

    // Resolve aliases in a symlink's target as well as in its presentation
    // path, using the same identity as the other retained source services.
    private static string CanonicalPath(string path) => SourceFilePaths.Canonical(path);

    private static bool PathEquals(string first, string second) =>
        string.Equals(CanonicalPath(first), CanonicalPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SameRevision(string? first, string? second) =>
        first is null ? second is null : second is not null && first.Equals(second, StringComparison.OrdinalIgnoreCase);

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string RevisionFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class OperationContext(string projectAliasPath, string projectPath, string visualizationAliasPath,
        string visualizationPath, string projectRevision, string? previousVisualizationRevision, string? projectXml,
        XDocument visualization, string? projectSessionID,
        CoreLayerReferenceCapture[]? externalCoreLayerReferences,
        CoreLayerRequestContext? coreLayerContext) : IDisposable
    {
        public string ProjectAliasPath { get; } = projectAliasPath;
        public string ProjectPath { get; } = projectPath;
        public string VisualizationAliasPath { get; } = visualizationAliasPath;
        public string VisualizationPath { get; } = visualizationPath;
        public string ProjectRevision { get; } = projectRevision;
        public string? PreviousVisualizationRevision { get; } = previousVisualizationRevision;
        public string? ProjectXml { get; } = projectXml;
        public XDocument Visualization { get; } = visualization;
        public string? ProjectSessionID { get; } = projectSessionID;
        public CoreLayerReferenceCapture[]? ExternalCoreLayerReferences { get; } = externalCoreLayerReferences;
        public CoreLayerRequestContext? CoreLayerContext { get; } = coreLayerContext;
        public void Dispose() { }
    }
}
