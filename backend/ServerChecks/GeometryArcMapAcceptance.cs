using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryArcMapRejectedRequest(string Action, string State, string? Code, string Message);

internal sealed record GeometryArcMapAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string SourceProjectPath,
    string SourceGeometryPath,
    string ProjectPath,
    string GeometryPath,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string InitialContextToken,
    string ReopenedContextToken,
    string SourceRevision,
    string SavedRevision,
    int AddedArcFID,
    int[] SplitArcFIDs,
    int[] SplitCreatedArcFIDs,
    int InsertedArcPointCount,
    object[] Operations,
    bool MapCatalogReadback,
    bool MapGeometryContextAndSourceReadback,
    bool OriginalSourceFeaturesPreserved,
    bool AddArcDraftReadback,
    bool InvalidPointIndexRejected,
    bool InvalidSegmentRejected,
    bool NonFiniteCoordinateRejected,
    bool FilterArcDraftReadback,
    bool SmoothArcDraftReadback,
    bool InsertArcPointDraftReadback,
    bool MoveArcPointDraftReadback,
    bool DeleteArcPointDraftReadback,
    bool SplitArcFidsReadBack,
    bool SplitArcAttributesPreserved,
    bool ReverseArcDraftReadback,
    bool DraftUndoReadback,
    bool DraftRedoReadback,
    bool SourceHdfUnchangedBeforeSave,
    bool ParentSaveChangedHdfRevision,
    bool ReopenedArcAndAttributeReadback,
    bool OriginalFixtureUnchanged,
    GeometryArcMapRejectedRequest[] RejectedRequests);

/// <summary>
/// Exercises source-backed Geometry arc commands through the real loopback
/// Host, on a copied Geometry-topology acceptance project. Writes stay inside
/// the new output directory until the explicit parent model.save operation.
/// </summary>
internal static partial class ServerAcceptance
{
    private static readonly TimeSpan GeometryArcMapTimeout = TimeSpan.FromMinutes(8);

    public static async Task<GeometryArcMapAcceptanceReport> RunGeometryArcMapAsync(
        Uri baseAddress, string topologyMapManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry arc-map acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(topologyMapManifestPath);
        if (!File.Exists(sourceManifestPath))
            throw new FileNotFoundException("The completed Geometry-topology map manifest was not found.", sourceManifestPath);
        using JsonDocument sourceManifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(sourceManifestPath));
        JsonElement sourceManifest = sourceManifestDocument.RootElement;
        string sourceProjectPath = Path.GetFullPath(ArcMapManifestString(sourceManifest, "projectPath", "ProjectPath"));
        string sourceGeometryPath = Path.GetFullPath(ArcMapManifestString(sourceManifest, "geometryPath", "GeometryPath"));
        if (!File.Exists(sourceProjectPath) || !Path.GetExtension(sourceProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The topology manifest must identify an existing saved .ras project.", sourceProjectPath);
        if (!File.Exists(sourceGeometryPath) || !Path.GetExtension(sourceGeometryPath).Equals(".h5", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The topology manifest must identify the saved Geometry HDF.", sourceGeometryPath);

        string sourceDirectory = Path.GetDirectoryName(sourceProjectPath)
            ?? throw new IOException("The source Geometry project has no containing directory.");
        string sourceVisualizationPath = Path.ChangeExtension(sourceProjectPath, ".vis");
        if (!File.Exists(sourceVisualizationPath))
            throw new FileNotFoundException("The completed topology fixture must contain its saved map catalog.", sourceVisualizationPath);
        if (!ArcMapPathIsInside(sourceGeometryPath, sourceDirectory))
            throw new InvalidDataException("The input Geometry HDF must be inside the copied project fixture directory.");

        string sourceProjectSHA = FileSHA256(sourceProjectPath);
        string sourceVisualizationSHA = FileSHA256(sourceVisualizationPath);
        string sourceGeometrySHA = FileSHA256(sourceGeometryPath);
        SortedDictionary<string, string> sourceFixtureSnapshot = ArcMapFixtureSnapshot(sourceDirectory);
        string? manifestSavedGeometrySHA = ArcMapOptionalString(sourceManifest, "savedGeometrySHA256");
        if (manifestSavedGeometrySHA is not null &&
            !manifestSavedGeometrySHA.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry arc-map acceptance requires a new output directory; existing files are never overwritten.");
        if (ArcMapPathsOverlap(outputDirectory, sourceDirectory))
            throw new IOException("The isolated Geometry arc-map output must not overlap the input fixture directory.");
        Directory.CreateDirectory(outputDirectory);

        string fixtureDirectory = Path.Combine(outputDirectory, "fixture");
        string projectPath = CopyProjectDirectory(sourceDirectory, sourceProjectPath, fixtureDirectory);
        string geometryPath = Path.GetFullPath(Path.Combine(fixtureDirectory,
            Path.GetRelativePath(sourceDirectory, sourceGeometryPath)));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(geometryPath) || !File.Exists(visualizationPath) ||
            !FileSHA256(projectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(geometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The copied project, Geometry HDF, or .vis bytes differ from the accepted source fixture.");

        using var timeout = new CancellationTokenSource(GeometryArcMapTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string projectXmlSHA = TextSHA256(projectXml);
        if (!IsSHA256(projectRevision) ||
            !projectRevision.Equals(FileSHA256(projectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.open did not bind the copied .ras bytes to its revision.");
        JsonElement coreContextElement = PropertyOrNull(opened.Result, "coreLayerContext");
        object? coreLayerContext = coreContextElement.ValueKind == JsonValueKind.Object
            ? coreContextElement.Clone() : null;
        string projectSessionID = Guid.NewGuid().ToString("D");

        OperationReceipt listed = await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedProjectRevision = projectRevision,
            coreLayerContext,
        }, cancellation);
        string visualizationRevision = StringProperty(listed.Result, "visualizationRevision");
        if (!IsSHA256(visualizationRevision) ||
            !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved map catalog revision does not match the copied .vis bytes.");

        var geometryOwners = new List<MapGeometryOwner>();
        foreach (JsonElement map in Property(listed.Result, "maps").EnumerateArray())
        {
            int mapIndex = IntProperty(map, "mapIndex");
            string mapName = StringProperty(map, "mapName");
            string mapType = StringProperty(map, "mapType");
            if (!string.Equals(mapType, "Plan", StringComparison.Ordinal)) continue;
            string mapID = Guid.NewGuid().ToString("D");
            object mapRequest = new
            {
                projectPath,
                projectXml,
                projectXmlSHA,
                mapID,
                mapIndex,
                mapName,
                mapType,
                expectedVisualizationRevision = visualizationRevision,
                projectSessionID,
                coreLayerContext,
            };
            OperationReceipt tree = await SubmitOperationAsync(client, "map.tree.load", mapRequest, cancellation);
            MapTreeSnapshot treeSnapshot = JsonSerializer.Deserialize<MapTreeSnapshot>(tree.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The copied saved map tree response was empty.");
            if (!treeSnapshot.VisualizationRevision.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            MapGeometryOwner? owner = FindGeometryOwner(mapID, mapIndex, mapName, mapType,
                visualizationRevision, treeSnapshot.Roots, mapRequest, TopologyMapFixturePlanName,
                TopologyMapFixtureGeometryName, geometryPath);
            if (owner is not null) geometryOwners.Add(owner);
        }
        if (geometryOwners.Count != 1)
            throw new InvalidDataException("The actual saved map catalog must resolve exactly one Plan Geometry owner for the copied Geometry HDF.");
        MapGeometryOwner mapOwner = geometryOwners[0];

        OperationReceipt mapGeometryOperation = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = mapOwner.MapRequest,
            geometryNode = mapOwner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = mapOwner.PlanNodes,
        }, cancellation);
        JsonElement mapGeometry = mapGeometryOperation.Result;
        string geometryRevision = StringProperty(mapGeometry, "geometryRevision");
        string contextToken = StringProperty(mapGeometry, "contextToken");
        if (!PathEquals(StringProperty(mapGeometry, "geometryCanonicalPath"), geometryPath) ||
            !geometryRevision.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(contextToken) || StringProperty(Property(mapGeometry, "topologyState"), "status") != "available")
            throw new InvalidDataException("map.geometry.load did not retain the exact saved Geometry source and context.");
        JsonElement mapTopologyPage = Property(mapGeometry, "topology");
        if (!BooleanProperty(mapTopologyPage, "complete") ||
            IntProperty(mapTopologyPage, "omittedSourceFeatureCount") != 0)
            throw new InvalidDataException("map.geometry.load did not return the complete saved Geometry topology page.");

        object geometryModel = new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            projectXml,
            coreLayerContext,
        };
        OperationReceipt modelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
        if (!StringProperty(modelLoad.Result, "revision").Equals(geometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        OperationReceipt topologyLoad = await SubmitOperationAsync(client, "topology.load", new
        {
            model = geometryModel,
            revision = geometryRevision,
        }, cancellation);
        GeometryTopologySnapshot initial = ArcMapReadTopology(topologyLoad.Result);
        RequireTopologyOwner(topologyLoad.Result, geometryRevision);
        if (!initial.CanEdit || initial.Map?.Extent is null || initial.Completion is null ||
            initial.Map.Features.Count(feature => feature.FeatureKind == "arc" && feature.FID is not null) < 4)
            throw new InvalidDataException("The copied Geometry must expose its saved editable topology and source arc FIDs.");

        GeometryTopologyFeatureRow[] originalArcRows = initial.Arcs.Rows.ToArray();
        GeometryTopologyFeatureRow[] originalNodeRows = initial.Nodes.Rows.ToArray();
        GeometryTopologyFeatureRow[] originalRegionRows = initial.Regions.Rows.ToArray();
        GeometryTopologyMapFeature[] originalSourceFeatures = (initial.Map?.Features ?? [])
            .Where(feature => feature.FID is not null).ToArray();
        bool mapGeometrySourceReadback = ArcMapMapFeaturesEqual(originalSourceFeatures,
            JsonSerializer.Deserialize<GeometryTopologyMapFeature[]>(Property(mapTopologyPage, "features").GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("map.geometry.load omitted its source Geometry features."));
        if (!mapGeometrySourceReadback)
            throw new InvalidDataException("map.geometry.load and topology.load disagree on source FIDs, feature identities, or exact coordinates.");
        double mapCellSize = MapCellSize(topologyLoad.Result.GetProperty("map"));

        GeometryTopologySnapshot current = initial;
        var operations = new List<object>();
        var rejected = new List<GeometryArcMapRejectedRequest>();
        async Task<GeometryTopologySnapshot> ApplyMapCommandAsync(string action, object command)
        {
            string draftIdentity = current.Completion?.DraftIdentity
                ?? throw new InvalidDataException("The current Geometry topology draft has no identity.");
            string requestID = Guid.NewGuid().ToString("D");
            var mapRequestContext = new
            {
                requestID,
                mapID = mapOwner.MapID,
                contextToken,
                geometryRevision,
                expectedDraftIdentity = draftIdentity,
            };
            OperationReceipt receipt = await SubmitOperationAsync(client, "topology.edit", new
            {
                model = geometryModel,
                revision = current.Revision,
                draftState = current.DraftState,
                mutation = new
                {
                    operation = "mapCommand",
                    featureKind = "map",
                    featureIDs = Array.Empty<int>(),
                    mapCommand = command,
                },
                mapRequestContext,
            }, cancellation);
            GeometryTopologySnapshot next = ArcMapReadTopology(receipt.Result);
            GeometryTopologyCompletionResult nextCompletion = next.Completion
                ?? throw new InvalidDataException(action + " receipt omitted its completed source draft.");
            GeometryTopologyMapRequestContext echoed = next.MapRequestContext
                ?? throw new InvalidDataException(action + " receipt omitted its captured map-owner context.");
            if (!next.Revision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                nextCompletion.SourceRevision != geometryRevision ||
                !IsSHA256(nextCompletion.DraftIdentity) || echoed.RequestID != requestID ||
                echoed.MapID != mapOwner.MapID || echoed.ContextToken != contextToken ||
                echoed.GeometryRevision != geometryRevision || echoed.ExpectedDraftIdentity != draftIdentity)
                throw new InvalidDataException(action + " receipt lost its exact source, map, or prior draft identity.");
            current = next;
            operations.Add(new { action, id = receipt.Id, state = receipt.State, draftIdentity = nextCompletion.DraftIdentity });
            return next;
        }

        async Task<GeometryArcMapRejectedRequest> ExpectRejectedMapCommandAsync(string action,
            object command, bool allowNamedNonFinite = false)
        {
            string draftIdentity = current.Completion?.DraftIdentity
                ?? throw new InvalidDataException("The current Geometry topology draft has no identity.");
            string requestID = Guid.NewGuid().ToString("D");
            object parameters = new
            {
                model = geometryModel,
                revision = current.Revision,
                draftState = current.DraftState,
                mutation = new
                {
                    operation = "mapCommand",
                    featureKind = "map",
                    featureIDs = Array.Empty<int>(),
                    mapCommand = command,
                },
                mapRequestContext = new
                {
                    requestID,
                    mapID = mapOwner.MapID,
                    contextToken,
                    geometryRevision,
                    expectedDraftIdentity = draftIdentity,
                },
            };
            JsonSerializerOptions requestOptions = JsonOptions;
            if (allowNamedNonFinite)
            {
                requestOptions = new JsonSerializerOptions(JsonOptions)
                {
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                };
            }
            using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
                new { method = "topology.edit", parameters }, requestOptions, cancellation);
            string body = await response.Content.ReadAsStringAsync(cancellation);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    var rejectedRequest = new GeometryArcMapRejectedRequest(action, "rejected",
                        response.StatusCode.ToString(), body);
                    rejected.Add(rejectedRequest);
                    return rejectedRequest;
                }
                throw new HttpRequestException($"POST api/operations (topology.edit/{action}) returned {(int)response.StatusCode}: {body}");
            }
            using JsonDocument submission = JsonDocument.Parse(body);
            string operationID = StringProperty(submission.RootElement, "id");
            long cursor = 0;
            while (true)
            {
                using JsonDocument poll = await GetJsonAsync(client,
                    $"api/operations/{Uri.EscapeDataString(operationID)}?after={cursor}", cancellation);
                JsonElement root = poll.RootElement;
                cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
                if (BooleanProperty(root, "hasMore")) continue;
                string state = StringProperty(root, "state");
                if (IsTerminal(state))
                {
                    JsonElement error = PropertyOrNull(root, "error");
                    string? code = OptionalString(error, "code");
                    string message = OptionalString(error, "message") ?? string.Empty;
                    if (!state.Equals("failed", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(message))
                        throw new InvalidDataException($"Invalid {action} input did not reject at the Host: state={state}; {JsonSerializer.Serialize(error, JsonOptions)}");
                    var rejectedRequest = new GeometryArcMapRejectedRequest(action, state, code, message);
                    rejected.Add(rejectedRequest);
                    operations.Add(new { action = "reject:" + action, id = operationID, state, code });
                    return rejectedRequest;
                }
                await Task.Delay(100, cancellation);
            }
        }

        bool invalidPointIndexRejected = true;
        bool invalidSegmentRejected = true;
        bool nonFiniteCoordinateRejected = true;
        GeometryTopologyMapFeature[] startingArcs = ArcMapFeatures(current, "arc");
        int addedArcFID = -1;
        GeometryTopologyCoordinate[] initialAddedArcPath = [];

        GeometryTopologyCoordinate[] addArcCoordinates =
        [
            // The midpoint shoulders lie on the endpoint-to-shoulder segments,
            // while the two shoulder vertices sit about 0.57 map units from
            // their endpoint-to-apex segments. Epsilon below therefore removes
            // the midpoints but keeps a nontrivial bend for the smooth check.
            new(0.5, 1.0), new(1.0, 1.1), new(1.5, 1.2),
            new(2.0, 2.5), new(2.5, 1.2), new(3.0, 1.1), new(3.5, 1.0),
        ];
        GeometryTopologySnapshot added = await ApplyMapCommandAsync("addArc", new
        {
            action = "addArc",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            coordinates = addArcCoordinates,
            mapCellSize,
        });
        GeometryTopologyMapFeature[] addedArcs = ArcMapFeatures(added, "arc");
        int[] createdByAdd = addedArcs.Select(feature => feature.FID!.Value).Except(
            startingArcs.Select(feature => feature.FID!.Value)).ToArray();
        if (addedArcs.Length != startingArcs.Length + 1 || createdByAdd.Length != 1)
            throw new InvalidDataException("addArc did not publish exactly one newly read-back source arc FID.");
        addedArcFID = createdByAdd[0];
        GeometryTopologyMapFeature addedArc = addedArcs.Single(feature => feature.FID == addedArcFID);
        initialAddedArcPath = ArcMapPath(addedArc);
        if (initialAddedArcPath.Length != addArcCoordinates.Length ||
            !initialAddedArcPath.SequenceEqual(addArcCoordinates))
            throw new InvalidDataException("addArc did not preserve its exact ordered map coordinates.");
        bool addArcDraftReadback = added.IsDirty && added.CanUndo &&
            ArcMapFeatureRow(added, addedArcFID) is not null &&
            ArcMapBaselineRowsPreserved(originalArcRows, added.Arcs.Rows) &&
            ArcMapBaselineRowsPreserved(originalNodeRows, added.Nodes.Rows) &&
            ArcMapBaselineRowsPreserved(originalRegionRows, added.Regions.Rows);
        if (!addArcDraftReadback)
            throw new InvalidDataException("addArc did not retain its source FID/table row while preserving original Geometry rows.");

        GeometryTopologyMapFeature[] addedPathFeatures = ArcMapFeatures(added, "arc");
        GeometryTopologyMapFeature targetArc = addedPathFeatures.Single(feature => feature.FID == addedArcFID);
        int internalPointCount = ArcMapPath(targetArc).Length - 2;
        GeometryArcMapRejectedRequest badPointIndex = await ExpectRejectedMapCommandAsync("moveArcPoint.invalidPointIndex", new
        {
            action = "moveArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            pointIndex = internalPointCount,
            coordinates = new[] { new GeometryTopologyCoordinate(1.25, 2.25) },
            mapCellSize,
        });
        invalidPointIndexRejected = badPointIndex.State is "failed" or "rejected";

        int invalidSegmentIndex = ArcMapPath(targetArc).Length - 1;
        GeometryArcMapRejectedRequest badSegment = await ExpectRejectedMapCommandAsync("insertArcPoint.invalidSegment", new
        {
            action = "insertArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            sourceSegmentIndex = invalidSegmentIndex,
            coordinates = new[] { new GeometryTopologyCoordinate(2.0, 2.0) },
            mapCellSize,
        });
        invalidSegmentRejected = badSegment.State is "failed" or "rejected";

        GeometryArcMapRejectedRequest badCoordinate = await ExpectRejectedMapCommandAsync("moveArcPoint.nonFiniteCoordinate", new
        {
            action = "moveArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            pointIndex = 0,
            coordinates = new[] { new GeometryTopologyCoordinate(double.NaN, 2.25) },
            mapCellSize,
        }, allowNamedNonFinite: true);
        nonFiniteCoordinateRejected = badCoordinate.State is "failed" or "rejected";
        if (!invalidPointIndexRejected || !invalidSegmentRejected || !nonFiniteCoordinateRejected)
            throw new InvalidDataException("An invalid arc point, segment, or non-finite coordinate was not rejected.");

        GeometryTopologyFeatureRow addedArcRowBeforeSplit = ArcMapFeatureRow(current, addedArcFID)
            ?? throw new InvalidDataException("The newly added arc has no actual source property-table row.");
        GeometryTopologyCoordinate[] pathBeforeFilter = ArcMapPath(ArcMapFeatures(current, "arc")
            .Single(feature => feature.FID == addedArcFID));
        int internalPointsBeforeFilter = pathBeforeFilter.Length - 2;
        GeometryTopologySnapshot filtered = await ApplyMapCommandAsync("filterArc", new
        {
            action = "filterArc",
            featureKind = "arc",
            featureIDs = new[] { addedArcFID },
            mapCellSize,
            epsilon = 0.25d,
        });
        GeometryTopologyMapFeature filteredFeature = ArcMapFeatures(filtered, "arc")
            .Single(feature => feature.FID == addedArcFID);
        GeometryTopologyCoordinate[] pathAfterFilter = ArcMapPath(filteredFeature);
        int internalPointsAfterFilter = pathAfterFilter.Length - 2;
        GeometryTopologyFeatureRow? filteredRow = ArcMapFeatureRow(filtered, addedArcFID);
        bool filteredAttributesPreserved = filteredRow is not null &&
            ArcMapCopiedArcAttributesEqual(addedArcRowBeforeSplit.Values, filteredRow.Values);
        bool filterArcDraftReadback = filtered.IsDirty && filtered.CanUndo &&
            internalPointsAfterFilter < internalPointsBeforeFilter && internalPointsAfterFilter > 0 &&
            filteredAttributesPreserved;
        if (!filterArcDraftReadback)
        {
            string diagnostic = JsonSerializer.Serialize(new
            {
                pointsBefore = internalPointsBeforeFilter,
                pointsAfter = internalPointsAfterFilter,
                pathBefore = pathBeforeFilter,
                pathAfter = pathAfterFilter,
                filtered.IsDirty,
                filtered.CanUndo,
                pointsReduced = internalPointsAfterFilter < internalPointsBeforeFilter,
                internalPointRemains = internalPointsAfterFilter > 0,
                filteredRowFound = filteredRow is not null,
                filteredAttributesPreserved,
                valueDifferences = filteredRow is null
                    ? new[] { "The filtered arc has no source property-table row." }
                    : ArcMapValueDifferences(addedArcRowBeforeSplit.Values, filteredRow.Values),
            }, JsonOptions);
            throw new InvalidDataException("filterArc readback failed its point-reduction or source-attribute invariant: " + diagnostic);
        }

        GeometryTopologyCoordinate[] beforeSmoothPath = ArcMapPath(filteredFeature);
        GeometryTopologySnapshot smoothed = await ApplyMapCommandAsync("smoothArc", new
        {
            action = "smoothArc",
            featureKind = "arc",
            featureIDs = new[] { addedArcFID },
            mapCellSize,
            smoothWindowSize = 3,
            smoothIterations = 1,
        });
        GeometryTopologyCoordinate[] afterSmoothPath = ArcMapPath(ArcMapFeatures(smoothed, "arc")
            .Single(feature => feature.FID == addedArcFID));
        GeometryTopologyFeatureRow smoothedRow = ArcMapFeatureRow(smoothed, addedArcFID)
            ?? throw new InvalidDataException("smoothArc removed the exact source arc's property row.");
        bool smoothArcDraftReadback = smoothed.IsDirty && smoothed.CanUndo &&
            !beforeSmoothPath.SequenceEqual(afterSmoothPath) &&
            ArcMapBaselineRowsPreserved(originalArcRows, smoothed.Arcs.Rows) &&
            ArcMapCopiedArcAttributesEqual(addedArcRowBeforeSplit.Values, smoothedRow.Values);
        if (!smoothArcDraftReadback)
            throw new InvalidDataException("smoothArc did not return a changed source arc in the current draft.");

        GeometryTopologyCoordinate[] beforeInsertPath = ArcMapPath(ArcMapFeatures(smoothed, "arc")
            .Single(feature => feature.FID == addedArcFID));
        GeometryTopologyCoordinate insertedPoint = new(
            (beforeInsertPath[0].X + beforeInsertPath[1].X) / 2d,
            (beforeInsertPath[0].Y + beforeInsertPath[1].Y) / 2d);
        GeometryTopologySnapshot inserted = await ApplyMapCommandAsync("insertArcPoint", new
        {
            action = "insertArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            sourceSegmentIndex = 0,
            coordinates = new[] { insertedPoint },
            mapCellSize,
        });
        GeometryTopologyCoordinate[] afterInsertPath = ArcMapPath(ArcMapFeatures(inserted, "arc")
            .Single(feature => feature.FID == addedArcFID));
        int insertedArcPointCount = afterInsertPath.Length - beforeInsertPath.Length;
        bool insertArcPointDraftReadback = insertedArcPointCount == 1 && inserted.IsDirty && inserted.CanUndo &&
            afterInsertPath.Contains(insertedPoint);
        if (!insertArcPointDraftReadback)
            throw new InvalidDataException("insertArcPoint did not return exactly one inserted internal source point.");

        GeometryTopologyCoordinate movedPoint = new(insertedPoint.X + 0.075d, insertedPoint.Y + 0.125d);
        int insertedPathIndex = Array.IndexOf(afterInsertPath, insertedPoint);
        if (insertedPathIndex <= 0 || insertedPathIndex >= afterInsertPath.Length - 1)
            throw new InvalidDataException("The inserted coordinate is not an internal point in the returned source path.");
        GeometryTopologySnapshot moved = await ApplyMapCommandAsync("moveArcPoint", new
        {
            action = "moveArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            pointIndex = insertedPathIndex - 1,
            coordinates = new[] { movedPoint },
            mapCellSize,
        });
        GeometryTopologyCoordinate[] afterMovePath = ArcMapPath(ArcMapFeatures(moved, "arc")
            .Single(feature => feature.FID == addedArcFID));
        bool moveArcPointDraftReadback = moved.IsDirty && moved.CanUndo && afterMovePath.Contains(movedPoint) &&
            !afterMovePath.Contains(insertedPoint) && afterMovePath.Length == afterInsertPath.Length;
        if (!moveArcPointDraftReadback)
            throw new InvalidDataException("moveArcPoint did not move the exact inserted source point in the unsaved draft.");

        int internalPointsBeforeDelete = afterMovePath.Length - 2;
        if (internalPointsBeforeDelete <= 0)
            throw new InvalidDataException("The accepted map arc has no internal source point for deleteArcPoint.");
        int deletedPointIndex = internalPointsBeforeDelete - 1;
        GeometryTopologyCoordinate deletedPoint = afterMovePath[deletedPointIndex + 1];
        GeometryTopologySnapshot deleted = await ApplyMapCommandAsync("deleteArcPoint", new
        {
            action = "deleteArcPoint",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            pointIndex = deletedPointIndex,
            mapCellSize,
        });
        GeometryTopologyCoordinate[] afterDeletePath = ArcMapPath(ArcMapFeatures(deleted, "arc")
            .Single(feature => feature.FID == addedArcFID));
        bool deleteArcPointDraftReadback = deleted.IsDirty && deleted.CanUndo &&
            afterDeletePath.Length == afterMovePath.Length - 1 && !afterDeletePath.Contains(deletedPoint);
        if (!deleteArcPointDraftReadback)
            throw new InvalidDataException("deleteArcPoint did not remove exactly the selected internal source point.");

        int[] arcFIDsBeforeSplit = ArcMapFeatures(deleted, "arc").Select(feature => feature.FID!.Value).Order().ToArray();
        GeometryTopologyCoordinate[] splitTargetPath = ArcMapPath(ArcMapFeatures(deleted, "arc")
            .Single(feature => feature.FID == addedArcFID));
        if (splitTargetPath.Length < 2)
            throw new InvalidDataException("The target source arc has no valid segment for splitArc.");
        GeometryTopologyCoordinate splitPoint = new(
            (splitTargetPath[0].X + splitTargetPath[1].X) / 2d,
            (splitTargetPath[0].Y + splitTargetPath[1].Y) / 2d);
        GeometryTopologySnapshot split = await ApplyMapCommandAsync("splitArc", new
        {
            action = "splitArc",
            featureKind = "arc",
            featureIDs = Array.Empty<int>(),
            arcFID = addedArcFID,
            coordinates = new[] { splitPoint },
            splitMode = "Anywhere",
            mapCellSize,
        });
        GeometryTopologyMapFeature[] splitArcs = ArcMapFeatures(split, "arc");
        int[] splitArcFIDs = splitArcs.Select(feature => feature.FID!.Value).Order().ToArray();
        int[] splitCreatedArcFIDs = splitArcFIDs.Except(arcFIDsBeforeSplit).ToArray();
        if (splitArcFIDs.Length != arcFIDsBeforeSplit.Length + 1 || splitCreatedArcFIDs.Length != 1 ||
            !splitArcFIDs.Contains(addedArcFID))
            throw new InvalidDataException("splitArc did not return the preserved source FID plus one newly read-back FID.");
        int createdSplitFID = splitCreatedArcFIDs[0];
        GeometryTopologyCoordinate[][] splitPiecePaths =
        [
            ArcMapPath(splitArcs.Single(feature => feature.FID == addedArcFID)),
            ArcMapPath(splitArcs.Single(feature => feature.FID == createdSplitFID)),
        ];
        GeometryTopologyCoordinate[] commonEndpoints = splitPiecePaths[0]
            .Where(point => point.Equals(splitPiecePaths[0][0]) || point.Equals(splitPiecePaths[0][^1]))
            .Intersect(splitPiecePaths[1].Where(point => point.Equals(splitPiecePaths[1][0]) || point.Equals(splitPiecePaths[1][^1])))
            .Distinct().ToArray();
        bool splitArcFidsReadBack = commonEndpoints.Length == 1 && split.IsDirty && split.CanUndo;
        if (!splitArcFidsReadBack)
            throw new InvalidDataException("splitArc's dynamically read-back source FIDs do not share one exact split endpoint.");

        GeometryTopologyFeatureRow[] splitRows = split.Arcs.Rows.Where(row => row.FID == addedArcFID ||
            row.FID == createdSplitFID).ToArray();
        bool splitArcAttributesPreserved = splitRows.Length == 2 &&
            splitRows.All(row => ArcMapCopiedArcAttributesEqual(addedArcRowBeforeSplit.Values, row.Values));
        if (!splitArcAttributesPreserved)
            throw new InvalidDataException("splitArc did not copy the source arc's exact attribute values to both dynamically identified pieces.");

        Dictionary<int, GeometryTopologyCoordinate[]> beforeReversePaths = splitArcs
            .Where(feature => feature.FID == addedArcFID || feature.FID == createdSplitFID)
            .ToDictionary(feature => feature.FID!.Value, ArcMapPath);
        GeometryTopologySnapshot reversed = await ApplyMapCommandAsync("reverseArc", new
        {
            action = "reverseArc",
            featureKind = "arc",
            featureIDs = new[] { addedArcFID, createdSplitFID },
            mapCellSize,
        });
        bool reverseArcDraftReadback = beforeReversePaths.All(item =>
        {
            GeometryTopologyMapFeature feature = ArcMapFeatures(reversed, "arc")
                .SingleOrDefault(candidate => candidate.FID == item.Key)
                ?? throw new InvalidDataException("reverseArc removed a dynamically captured split FID.");
            return ArcMapPath(feature).SequenceEqual(item.Value.Reverse());
        }) && reversed.IsDirty && reversed.CanUndo;
        if (!reverseArcDraftReadback)
            throw new InvalidDataException("reverseArc did not reverse both exact source split pieces in the current draft.");

        string beforeReverseSignature = ArcMapTopologySignature(split);
        string reversedSignature = ArcMapTopologySignature(reversed);
        OperationReceipt undoReceipt = await SubmitOperationAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = geometryRevision,
            draftState = reversed.DraftState,
            mutation = new { operation = "undo", featureKind = "history", featureIDs = Array.Empty<int>() },
        }, cancellation);
        GeometryTopologySnapshot undone = ArcMapReadTopology(undoReceipt.Result);
        bool draftUndoReadback = ArcMapTopologySignature(undone) == beforeReverseSignature &&
            undone.CanUndo && undone.CanRedo;
        if (!draftUndoReadback)
            throw new InvalidDataException("Undo did not restore the exact pre-reverse arc map and source table rows.");
        OperationReceipt redoReceipt = await SubmitOperationAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = geometryRevision,
            draftState = undone.DraftState,
            mutation = new { operation = "redo", featureKind = "history", featureIDs = Array.Empty<int>() },
        }, cancellation);
        GeometryTopologySnapshot redone = ArcMapReadTopology(redoReceipt.Result);
        bool draftRedoReadback = ArcMapTopologySignature(redone) == reversedSignature &&
            redone.CanUndo && !redone.CanRedo;
        if (!draftRedoReadback)
            throw new InvalidDataException("Redo did not restore the exact accepted reverseArc map and source table rows.");

        bool baselineRowsPreserved = ArcMapBaselineRowsPreserved(originalArcRows, redone.Arcs.Rows) &&
            ArcMapBaselineRowsPreserved(originalNodeRows, redone.Nodes.Rows) &&
            ArcMapBaselineRowsPreserved(originalRegionRows, redone.Regions.Rows);
        bool originalSourceFeaturesPreserved = ArcMapMapFeaturesPreserved(originalSourceFeatures,
            redone.Map?.Features ?? []);
        if (!baselineRowsPreserved || !originalSourceFeaturesPreserved)
            throw new InvalidDataException("Arc editing changed original source Node/Arc/Region FIDs, row identities, or attributes.");
        bool sourceHdfUnchangedBeforeSave = FileSHA256(geometryPath).Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!sourceHdfUnchangedBeforeSave)
            throw new InvalidDataException("Arc commands changed the saved Geometry HDF, .ras, or .vis before parent model.save.");

        OperationReceipt save = await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            revision = geometryRevision,
            fields = new Dictionary<string, string>(),
            images = Property(modelLoad.Result, "images"),
            projectXml,
            sourceState = OptionalString(modelLoad.Result, "sourceState"),
            geometryTopologyState = redone.DraftState,
            coreLayerContext,
        }, cancellation);
        string savedRevision = StringProperty(save.Result, "revision");
        bool parentSaveChangedHdfRevision = IsSHA256(savedRevision) &&
            !savedRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(geometryPath).Equals(savedRevision, StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedHdfRevision)
            throw new InvalidDataException("The parent model.save did not publish the changed Geometry HDF at its returned revision.");

        OperationReceipt reopenedProject = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string reopenedProjectXml = StringProperty(reopenedProject.Result, "projectXml");
        if (!StringProperty(reopenedProject.Result, "revision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || !FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Parent model.save unexpectedly changed the project XML or map catalog.");
        JsonElement reopenedCoreContextElement = PropertyOrNull(reopenedProject.Result, "coreLayerContext");
        object? reopenedCoreLayerContext = reopenedCoreContextElement.ValueKind == JsonValueKind.Object
            ? reopenedCoreContextElement.Clone() : null;
        object reopenedGeometryModel = new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            projectXml = reopenedProjectXml,
            coreLayerContext = reopenedCoreLayerContext,
        };
        OperationReceipt reopenedModel = await SubmitOperationAsync(client, "model.load", reopenedGeometryModel, cancellation);
        if (!StringProperty(reopenedModel.Result, "revision").Equals(savedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("model.load did not reopen the committed Geometry revision.");
        OperationReceipt reopenedTopologyOperation = await SubmitOperationAsync(client, "topology.load", new
        {
            model = reopenedGeometryModel,
            revision = savedRevision,
        }, cancellation);
        GeometryTopologySnapshot reopenedTopology = ArcMapReadTopology(reopenedTopologyOperation.Result);
        RequireTopologyOwner(reopenedTopologyOperation.Result, savedRevision);
        bool reopenedArcAndAttributeReadback = !reopenedTopology.IsDirty && !reopenedTopology.CanUndo &&
            !reopenedTopology.CanRedo && ArcMapTopologySignature(reopenedTopology) == reversedSignature &&
            ArcMapBaselineRowsPreserved(originalArcRows, reopenedTopology.Arcs.Rows) &&
            ArcMapBaselineRowsPreserved(originalNodeRows, reopenedTopology.Nodes.Rows) &&
            ArcMapBaselineRowsPreserved(originalRegionRows, reopenedTopology.Regions.Rows) &&
            ArcMapMapFeaturesPreserved(originalSourceFeatures, reopenedTopology.Map?.Features ?? []) &&
            reopenedTopology.Arcs.Rows.Any(row => row.FID == addedArcFID) &&
            splitCreatedArcFIDs.All(fid => reopenedTopology.Arcs.Rows.Any(row => row.FID == fid));
        if (!reopenedArcAndAttributeReadback)
            throw new InvalidDataException("Reopened model.load/topology.load did not retain source arc/node rows, split FIDs, reversed coordinates, and attributes.");

        OperationReceipt reopenedTreeOperation = await SubmitOperationAsync(client, "map.tree.load", mapOwner.MapRequest, cancellation);
        MapTreeSnapshot reopenedTree = JsonSerializer.Deserialize<MapTreeSnapshot>(reopenedTreeOperation.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The reopened map tree response was empty.");
        MapGeometryOwner? reopenedOwner = FindGeometryOwner(mapOwner.MapID, mapOwner.MapIndex,
            mapOwner.MapName, mapOwner.MapType, visualizationRevision, reopenedTree.Roots,
            mapOwner.MapRequest, TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath);
        if (reopenedOwner is null)
            throw new InvalidDataException("The reopened map catalog did not resolve the same Geometry source path and Plan association.");
        OperationReceipt reopenedMapGeometryOperation = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = reopenedOwner.MapRequest,
            geometryNode = reopenedOwner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = reopenedOwner.PlanNodes,
        }, cancellation);
        string reopenedContextToken = StringProperty(reopenedMapGeometryOperation.Result, "contextToken");
        if (!StringProperty(reopenedMapGeometryOperation.Result, "geometryRevision")
                .Equals(savedRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(reopenedContextToken) || reopenedContextToken.Equals(contextToken, StringComparison.Ordinal))
            throw new InvalidDataException("The reopened map Geometry snapshot did not bind the saved revision to a fresh context token.");

        bool originalFixtureUnchanged = ArcMapFixtureSnapshotEquals(sourceFixtureSnapshot,
                ArcMapFixtureSnapshot(sourceDirectory)) &&
            FileSHA256(sourceProjectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceGeometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceVisualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase);
        if (!originalFixtureUnchanged)
            throw new InvalidDataException("The original topology fixture changed; all edits must stay in the isolated project copy.");

        string manifestPath = Path.Combine(outputDirectory, "geometry-arc-map-http-e2e-manifest.json");
        var report = new GeometryArcMapAcceptanceReport("completed", manifestPath, sourceManifestPath,
            sourceProjectPath, sourceGeometryPath, projectPath, geometryPath, mapOwner.MapID,
            mapOwner.MapIndex, mapOwner.MapName, mapOwner.MapType, contextToken, reopenedContextToken,
            geometryRevision, savedRevision, addedArcFID, splitArcFIDs, splitCreatedArcFIDs,
            insertedArcPointCount, operations.ToArray(), MapCatalogReadback: true,
            MapGeometryContextAndSourceReadback: mapGeometrySourceReadback,
            OriginalSourceFeaturesPreserved: originalSourceFeaturesPreserved,
            AddArcDraftReadback: addArcDraftReadback, InvalidPointIndexRejected: invalidPointIndexRejected,
            InvalidSegmentRejected: invalidSegmentRejected, NonFiniteCoordinateRejected: nonFiniteCoordinateRejected,
            FilterArcDraftReadback: filterArcDraftReadback, SmoothArcDraftReadback: smoothArcDraftReadback,
            InsertArcPointDraftReadback: insertArcPointDraftReadback, MoveArcPointDraftReadback: moveArcPointDraftReadback,
            DeleteArcPointDraftReadback: deleteArcPointDraftReadback, SplitArcFidsReadBack: splitArcFidsReadBack,
            SplitArcAttributesPreserved: splitArcAttributesPreserved, ReverseArcDraftReadback: reverseArcDraftReadback,
            DraftUndoReadback: draftUndoReadback, DraftRedoReadback: draftRedoReadback,
            SourceHdfUnchangedBeforeSave: sourceHdfUnchangedBeforeSave,
            ParentSaveChangedHdfRevision: parentSaveChangedHdfRevision,
            ReopenedArcAndAttributeReadback: reopenedArcAndAttributeReadback,
            OriginalFixtureUnchanged: originalFixtureUnchanged, rejected.ToArray());
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    private static string ArcMapManifestString(JsonElement manifest, string firstName, string secondName)
    {
        JsonElement value = PropertyOrNull(manifest, firstName);
        if (value.ValueKind == JsonValueKind.Undefined) value = PropertyOrNull(manifest, secondName);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"The Geometry topology manifest omitted '{firstName}'.");
    }

    private static string? ArcMapOptionalString(JsonElement manifest, string name)
    {
        JsonElement value = PropertyOrNull(manifest, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ArcMapPathIsInside(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != "." && !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool ArcMapPathsOverlap(string first, string second) =>
        ArcMapPathIsInside(first, second) || ArcMapPathIsInside(second, first) ||
        PathEquals(first, second);

    private static SortedDictionary<string, string> ArcMapFixtureSnapshot(string directory)
    {
        string root = Path.GetFullPath(directory);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The source fixture contains a symbolic or reparse-point file.");
            files.Add(Path.GetRelativePath(root, path), FileSHA256(path));
        }
        return files;
    }

    private static bool ArcMapFixtureSnapshotEquals(SortedDictionary<string, string> expected,
        SortedDictionary<string, string> actual) => expected.Count == actual.Count &&
        expected.All(pair => actual.TryGetValue(pair.Key, out string? sha) &&
            pair.Value.Equals(sha, StringComparison.OrdinalIgnoreCase));

    private static GeometryTopologySnapshot ArcMapReadTopology(JsonElement result) =>
        JsonSerializer.Deserialize<GeometryTopologySnapshot>(result.GetRawText(), JsonOptions)
        ?? throw new InvalidDataException("The topology service returned an empty snapshot.");

    private static GeometryTopologyMapFeature[] ArcMapFeatures(GeometryTopologySnapshot snapshot, string kind) =>
        (snapshot.Map?.Features ?? []).Where(feature => feature.FeatureKind == kind && feature.FID is not null).ToArray();

    private static GeometryTopologyCoordinate[] ArcMapPath(GeometryTopologyMapFeature feature)
    {
        if (feature.Paths.Length != 1 || feature.Paths[0].Length < 2)
            throw new InvalidDataException("A source arc map feature did not return exactly one usable coordinate path.");
        return feature.Paths[0];
    }

    private static GeometryTopologyFeatureRow? ArcMapFeatureRow(GeometryTopologySnapshot snapshot, int fid) =>
        snapshot.Arcs.Rows.SingleOrDefault(row => row.FID == fid);

    private static bool ArcMapBaselineRowsPreserved(IEnumerable<GeometryTopologyFeatureRow> baseline,
        IEnumerable<GeometryTopologyFeatureRow> current)
    {
        GeometryTopologyFeatureRow[] actual = current.ToArray();
        return baseline.All(expected => actual.Count(candidate => candidate.FID == expected.FID &&
            candidate.ID == expected.ID && ArcMapValuesEqual(candidate.Values, expected.Values) &&
            candidate.X == expected.X && candidate.Y == expected.Y &&
            candidate.StartNodeID == expected.StartNodeID && candidate.EndNodeID == expected.EndNodeID) == 1);
    }

    private static bool ArcMapMapFeaturesEqual(IEnumerable<GeometryTopologyMapFeature> left,
        IEnumerable<GeometryTopologyMapFeature> right)
    {
        GeometryTopologyMapFeature[] first = left.OrderBy(feature => feature.FeatureKind, StringComparer.Ordinal)
            .ThenBy(feature => feature.FID).ThenBy(feature => feature.ID, StringComparer.Ordinal).ToArray();
        GeometryTopologyMapFeature[] second = right.Where(feature => feature.FID is not null)
            .OrderBy(feature => feature.FeatureKind, StringComparer.Ordinal)
            .ThenBy(feature => feature.FID).ThenBy(feature => feature.ID, StringComparer.Ordinal).ToArray();
        return first.Length == second.Length && first.Zip(second).All(pair =>
            pair.First.ID == pair.Second.ID && pair.First.FeatureKind == pair.Second.FeatureKind &&
            pair.First.FID == pair.Second.FID && pair.First.GeometryType == pair.Second.GeometryType &&
            pair.First.SelectionIDs.SequenceEqual(pair.Second.SelectionIDs, StringComparer.Ordinal) &&
            pair.First.Paths.Length == pair.Second.Paths.Length &&
            pair.First.Paths.Zip(pair.Second.Paths).All(paths => paths.First.SequenceEqual(paths.Second)));
    }

    private static bool ArcMapMapFeaturesPreserved(IEnumerable<GeometryTopologyMapFeature> baseline,
        IEnumerable<GeometryTopologyMapFeature> current)
    {
        GeometryTopologyMapFeature[] expected = baseline.ToArray();
        GeometryTopologyMapFeature[] actual = current.Where(feature => feature.FID is not null).ToArray();
        return expected.All(feature => actual.Count(candidate =>
            ArcMapMapFeaturesEqual([feature], [candidate])) == 1);
    }

    private static bool ArcMapValuesEqual(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static bool ArcMapCopiedArcAttributesEqual(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        // SplitArcOperation calls Topology.CopyArcAttributes, which copies the
        // source ArcMetadata row. FID/endpoints/length are derived from the
        // resulting geometry; PointCount is ArcMetadata.Count and must match.
        HashSet<string> geometryColumns = ["FID", "StartNodeID", "EndNodeID", "Length"];
        Dictionary<string, string> leftAttributes = left.Where(pair => !geometryColumns.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, string> rightAttributes = right.Where(pair => !geometryColumns.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return ArcMapValuesEqual(leftAttributes, rightAttributes);
    }

    private static string[] ArcMapValueDifferences(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) => left.Keys.Concat(right.Keys)
        .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)
        .Where(key => !left.TryGetValue(key, out string? leftValue) ||
            !right.TryGetValue(key, out string? rightValue) ||
            !string.Equals(leftValue, rightValue, StringComparison.Ordinal))
        .Select(key =>
        {
            string before = left.TryGetValue(key, out string? beforeValue) ? beforeValue : "<missing>";
            string after = right.TryGetValue(key, out string? afterValue) ? afterValue : "<missing>";
            return $"{key}: before={before}; after={after}";
        })
        .ToArray();

    private static string ArcMapTopologySignature(GeometryTopologySnapshot snapshot)
    {
        var features = (snapshot.Map?.Features ?? [])
            .OrderBy(feature => feature.FeatureKind, StringComparer.Ordinal)
            .ThenBy(feature => feature.FID ?? -1)
            .ThenBy(feature => feature.ID, StringComparer.Ordinal)
            .Select(feature => new
            {
                feature.ID,
                feature.FeatureKind,
                feature.FID,
                feature.GeometryType,
                feature.SelectionIDs,
                paths = feature.Paths.Select(path => path.Select(point => new { point.X, point.Y }).ToArray()).ToArray(),
            }).ToArray();
        string[] Rows(IEnumerable<GeometryTopologyFeatureRow> rows) => rows
            .OrderBy(row => row.FID).ThenBy(row => row.ID, StringComparer.Ordinal)
            .Select(row => JsonSerializer.Serialize(new
            {
                row.FID,
                row.ID,
                values = row.Values.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                row.X,
                row.Y,
                row.StartNodeID,
                row.EndNodeID,
            }, JsonOptions)).ToArray();
        return JsonSerializer.Serialize(new
        {
            features,
            arcs = Rows(snapshot.Arcs.Rows),
            nodes = Rows(snapshot.Nodes.Rows),
            regions = Rows(snapshot.Regions.Rows),
        }, JsonOptions);
    }
}
