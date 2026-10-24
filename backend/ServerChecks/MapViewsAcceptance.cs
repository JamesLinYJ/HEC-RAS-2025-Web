using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ras.Backend.ServerChecks;

internal sealed record MapViewsAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string ProjectPath,
    Guid ProjectSessionID,
    string InitialVisualizationRevision,
    string FinalVisualizationRevision,
    int CreatedViews,
    int RenamedViews,
    int ResetExtents,
    int ShortcutChanges,
    int DeletedViews,
    bool StaleVisualizationRevisionRejected,
    bool StaleViewsCollectionRejected,
    bool MapCollectionPreserved,
    bool UnknownVisualizationNodePreserved,
    bool ProjectOpenReadback,
    bool MapTreeReadback,
    bool OriginalHdfBytesUnchanged,
    bool CopiedHdfBytesUnchanged);

internal static partial class ServerAcceptance
{
    private sealed record MapViewsRequestValues(
        string ProjectPath,
        string ProjectXml,
        string ProjectXmlSHA,
        string ProjectRevision,
        Guid ProjectSessionID,
        string Action,
        string? TargetName = null,
        string? Name = null,
        object? Extent = null,
        string? Shortcut = null);

    private sealed record MapViewsCommitValues(
        string ProjectPath,
        string ProjectXml,
        string ProjectXmlSHA,
        string ProjectRevision,
        Guid ProjectSessionID,
        string Action,
        string? ExpectedVisualizationRevision,
        string ExpectedViewsSHA256,
        string? TargetName = null,
        string? ExpectedEntrySHA256 = null,
        string? Name = null,
        object? Extent = null,
        string? Shortcut = null);

    private sealed record MapViewsAcceptanceEvidence(
        string Action,
        string PreviousVisualizationRevision,
        string VisualizationRevision,
        string PreviousViewsSHA256,
        string ViewsSHA256,
        string? TargetName,
        string? BeforeEntrySHA256,
        string? AfterEntrySHA256);

    public static async Task<MapViewsAcceptanceReport> RunMapViewsAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Saved-view acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(resultHoverManifestPath);
        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(sourceManifestPath), JsonOptions)
            ?? throw new InvalidDataException("The source Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || !File.Exists(source.ProjectPath) ||
            !File.Exists(source.VisualizationPath))
            throw new InvalidDataException("Saved-view acceptance requires the completed Result-hover project and .vis fixture.");

        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source.ProjectPath))!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Saved-view acceptance requires a new output directory; existing data is never overwritten.");
        string outputRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        CopyDirectory(sourceRoot, outputRoot);
        string Rebase(string path) => RebasePath(path, sourceRoot, outputRoot);
        string projectPath = Rebase(source.ProjectPath);
        string visualizationPath = Rebase(source.VisualizationPath);
        string manifestPath = Path.Combine(outputDirectory, "map-views-http-e2e-manifest.json");
        Directory.CreateDirectory(outputDirectory);

        string[] originalHdfPaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> originalHdfHashes = originalHdfPaths.ToDictionary(
            path => path, MapViewsFileSHA256, StringComparer.Ordinal);
        Dictionary<string, string> copiedHdfHashes = originalHdfPaths.ToDictionary(
            Rebase, path => MapViewsFileSHA256(Rebase(path)), StringComparer.Ordinal);

        // Make one opaque non-map node in the isolated copy so the round trip
        // proves that edits to <Views> preserve unrelated .vis XML.
        XDocument copiedVisualization = XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace);
        XElement root = copiedVisualization.Root
            ?? throw new InvalidDataException("The copied visualization file has no root element.");
        if (root.Name != "Ras" || root.Element("AcceptanceUnknown") is not null)
            throw new InvalidDataException("The copied visualization root is unexpected or already contains the acceptance marker.");
        var unknownMarker = new XElement("AcceptanceUnknown", new XAttribute("Marker", "saved-view-roundtrip"),
            new XElement("Nested", "preserve this XML"));
        root.Add(unknownMarker);
        copiedVisualization.Save(visualizationPath, SaveOptions.DisableFormatting);
        string expectedMapsSHA = MapViewsElementSHA(root.Element("Maps"));
        string expectedUnknownSHA = MapViewsElementSHA(unknownMarker);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        Guid sessionID = Guid.NewGuid();
        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = MapViewsSHA256(Encoding.UTF8.GetBytes(projectXml));
        if (!MapViewsPathEquals(StringProperty(opened, "projectPath"), projectPath) ||
            !MapViewsFileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        MapViewsRequestValues Values(string action, string? targetName = null, string? name = null,
            object? extent = null, string? shortcut = null) => new(projectPath, projectXml,
            projectXmlSHA, projectRevision, sessionID, action, targetName, name, extent, shortcut);

        var evidence = new List<MapViewsAcceptanceEvidence>();
        JsonElement initialInspection = await InspectMapViewsAsync(client,
            Values("list"), cancellation);
        string initialVisualizationRevision = OptionalString(initialInspection, "visualizationRevision")
            ?? throw new InvalidDataException("The fixture .vis must have a captured SHA revision.");
        AssertMapViewsDiskRevision(visualizationPath, initialVisualizationRevision,
            "The saved-view inspection did not return the copied .vis SHA.");
        string currentVisualizationRevision = initialVisualizationRevision;

        const string createdName = "保存视图验收-新视图";
        const string renamedName = "保存视图验收-重命名";
        JsonElement[] initialViews = Property(initialInspection, "views").EnumerateArray().ToArray();
        string[] availableShortcuts = Enumerable.Range(1, 24).Reverse()
            .Select(key => $"Ctrl+F{key}")
            .Where(candidate => !initialViews.Any(view =>
                string.Equals(StringProperty(view, "shortcut"), candidate, StringComparison.OrdinalIgnoreCase)))
            .Take(4).ToArray();
        if (availableShortcuts.Length != 4)
            throw new InvalidDataException("The copied fixture has no four unused function-key shortcuts for acceptance.");
        string createdShortcut = availableShortcuts[0];
        string firstShortcutChange = availableShortcuts[1];
        string staleCollectionShortcut = availableShortcuts[2];
        string currentShortcut = availableShortcuts[3];
        var firstExtent = new { left = 100d, top = 200d, right = 300d, bottom = 0d };
        var secondExtent = new { left = -25.5d, top = 70d, right = 80.25d, bottom = -10d };

        JsonElement createInspection = await InspectMapViewsAsync(client,
            Values("create", name: createdName, extent: firstExtent, shortcut: createdShortcut), cancellation);
        JsonElement created = await CommitMapViewAsync(client,
            Values("create", name: createdName, extent: firstExtent, shortcut: createdShortcut),
            createInspection, expectedVisualizationRevision: currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(created, "visualizationRevision");
        AssertMapViewExtent(Property(created, "after"), firstExtent.left, firstExtent.top,
            firstExtent.right, firstExtent.bottom);
        if (StringProperty(Property(created, "after"), "shortcut") != createdShortcut)
            throw new InvalidDataException("Creating a saved view did not persist its original Bind shortcut text.");
        evidence.Add(MapViewEvidence(created));

        JsonElement renameInspection = await InspectMapViewsAsync(client,
            Values("rename", targetName: createdName, name: renamedName), cancellation);
        JsonElement renamed = await CommitMapViewAsync(client,
            Values("rename", targetName: createdName, name: renamedName), renameInspection,
            currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(renamed, "visualizationRevision");
        AssertMapViewExtent(Property(renamed, "after"), firstExtent.left, firstExtent.top,
            firstExtent.right, firstExtent.bottom);
        evidence.Add(MapViewEvidence(renamed));

        JsonElement extentInspection = await InspectMapViewsAsync(client,
            Values("resetExtent", targetName: renamedName, extent: secondExtent), cancellation);
        JsonElement reset = await CommitMapViewAsync(client,
            Values("resetExtent", targetName: renamedName, extent: secondExtent), extentInspection,
            currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(reset, "visualizationRevision");
        evidence.Add(MapViewEvidence(reset));

        JsonElement shortcutInspection = await InspectMapViewsAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: firstShortcutChange), cancellation);
        JsonElement shortcutReceipt = await CommitMapViewAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: firstShortcutChange), shortcutInspection,
            currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(shortcutReceipt, "visualizationRevision");
        if (StringProperty(Property(shortcutReceipt, "after"), "shortcut") != firstShortcutChange)
            throw new InvalidDataException("The shortcut update did not persist its captured VirtualKey string.");
        evidence.Add(MapViewEvidence(shortcutReceipt));

        // Stale collection identity with a current .vis revision must still be
        // rejected, proving the collection and target-row captures are used.
        JsonElement oldCollectionInspection = await InspectMapViewsAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: staleCollectionShortcut), cancellation);
        string oldViewsSHA = StringProperty(oldCollectionInspection, "viewsSHA256");
        string oldEntrySHA = StringProperty(Property(oldCollectionInspection, "target"), "entrySHA256");
        JsonElement updateBeforeStale = await InspectMapViewsAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: currentShortcut), cancellation);
        JsonElement updateReceipt = await CommitMapViewAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: currentShortcut), updateBeforeStale,
            currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(updateReceipt, "visualizationRevision");
        evidence.Add(MapViewEvidence(updateReceipt));
        byte[] beforeStaleViewsCommit = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        bool staleViewsRejected = await MapViewCommitRejectedAsync(client,
            new MapViewsCommitValues(projectPath, projectXml, projectXmlSHA, projectRevision, sessionID,
                "shortcut", currentVisualizationRevision, oldViewsSHA, renamedName, oldEntrySHA,
                Shortcut: staleCollectionShortcut), cancellation);
        if (!staleViewsRejected || !File.ReadAllBytes(visualizationPath).AsSpan().SequenceEqual(beforeStaleViewsCommit))
            throw new InvalidDataException("A stale views collection capture was accepted or changed the copied .vis file.");

        // A current list/row capture paired with an old complete .vis revision
        // must be rejected before the transaction publishes any bytes.
        JsonElement staleRevisionInspection = await InspectMapViewsAsync(client,
            Values("shortcut", targetName: renamedName, shortcut: firstShortcutChange), cancellation);
        byte[] beforeStaleRevisionCommit = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        bool staleRevisionRejected = await MapViewCommitRejectedAsync(client,
            new MapViewsCommitValues(projectPath, projectXml, projectXmlSHA, projectRevision, sessionID,
                "shortcut", initialVisualizationRevision, StringProperty(staleRevisionInspection, "viewsSHA256"),
                renamedName, StringProperty(Property(staleRevisionInspection, "target"), "entrySHA256"),
                Shortcut: firstShortcutChange), cancellation);
        if (!staleRevisionRejected || !File.ReadAllBytes(visualizationPath).AsSpan().SequenceEqual(beforeStaleRevisionCommit))
            throw new InvalidDataException("A stale .vis revision was accepted or changed the copied .vis file.");

        JsonElement deleteInspection = await InspectMapViewsAsync(client,
            Values("delete", targetName: renamedName), cancellation);
        JsonElement deleted = await CommitMapViewAsync(client,
            Values("delete", targetName: renamedName), deleteInspection,
            currentVisualizationRevision, cancellation);
        currentVisualizationRevision = StringProperty(deleted, "visualizationRevision");
        evidence.Add(MapViewEvidence(deleted));

        JsonElement finalList = await InspectMapViewsAsync(client, Values("list"), cancellation);
        if (Property(finalList, "views").EnumerateArray().Any(view =>
                StringProperty(view, "name") is createdName or renamedName))
            throw new InvalidDataException("The deleted test view remained in the reloaded .vis collection.");
        AssertMapViewsDiskRevision(visualizationPath, currentVisualizationRevision,
            "The final view receipt does not match the persisted copied .vis bytes.");

        XDocument finalVisualization = XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace);
        XElement finalRoot = finalVisualization.Root ?? throw new InvalidDataException("The final .vis has no root element.");
        bool mapsPreserved = MapViewsElementSHA(finalRoot.Element("Maps")) == expectedMapsSHA;
        bool unknownPreserved = finalRoot.Element("AcceptanceUnknown") is { } finalMarker &&
            MapViewsElementSHA(finalMarker) == expectedUnknownSHA;
        if (!mapsPreserved || !unknownPreserved)
            throw new InvalidDataException("The saved-view transaction changed the copied map collection or an unrelated .vis node.");

        JsonElement reopened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        bool projectReadback = StringProperty(reopened, "revision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            StringProperty(reopened, "projectXml").Equals(projectXml, StringComparison.Ordinal) &&
            MapViewsFileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase);
        if (!projectReadback)
            throw new InvalidDataException("Saved-view editing unexpectedly changed the .ras project or its readback.");

        JsonElement mapList = (await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedProjectRevision = projectRevision,
        }, cancellation)).Result;
        JsonElement[] maps = Property(mapList, "maps").EnumerateArray().ToArray();
        bool mapListReadback = maps.Any(map => IntProperty(map, "mapIndex") == source.Map.MapIndex &&
            StringProperty(map, "mapName") == source.Map.MapName && StringProperty(map, "mapType") == source.Map.MapType) &&
            StringProperty(mapList, "visualizationRevision").Equals(currentVisualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!mapListReadback)
            throw new InvalidDataException("The map catalog did not read back the unchanged map collection at the final .vis revision.");

        JsonElement mapTree = (await SubmitOperationAsync(client, "map.tree.load", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            mapID = source.Map.MapID,
            mapIndex = source.Map.MapIndex,
            mapName = source.Map.MapName,
            mapType = source.Map.MapType,
            expectedVisualizationRevision = currentVisualizationRevision,
            projectSessionID = sessionID,
        }, cancellation)).Result;
        bool mapTreeReadback = StringProperty(mapTree, "visualizationRevision").Equals(
            currentVisualizationRevision, StringComparison.OrdinalIgnoreCase) &&
            Property(mapTree, "roots").ValueKind == JsonValueKind.Array;
        if (!mapTreeReadback)
            throw new InvalidDataException("The original saved map tree did not reload after saved-view edits.");

        bool originalHdfUnchanged = originalHdfHashes.All(pair =>
            MapViewsFileSHA256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        bool copiedHdfUnchanged = copiedHdfHashes.All(pair =>
            MapViewsFileSHA256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        if (!originalHdfUnchanged || !copiedHdfUnchanged)
            throw new InvalidDataException("Saved-view changes modified an original or copied HDF source.");

        var report = new MapViewsAcceptanceReport("passed", manifestPath, sourceManifestPath,
            projectPath, sessionID, initialVisualizationRevision, currentVisualizationRevision,
            1, 1, 1, 2, 1, staleRevisionRejected, staleViewsRejected,
            mapsPreserved, unknownPreserved, projectReadback, mapTreeReadback,
            originalHdfUnchanged, copiedHdfUnchanged);
        var manifest = new
        {
            schema = "hecras-map-views-http-e2e/v1",
            sourceManifestPath,
            outputRoot,
            projectPath,
            visualizationPath,
            projectRevision,
            projectXmlSHA,
            projectSessionID = sessionID,
            map = source.Map,
            initialVisualizationRevision,
            finalVisualizationRevision = currentVisualizationRevision,
            preservedMapsSHA256 = expectedMapsSHA,
            preservedUnknownNodeSHA256 = expectedUnknownSHA,
            operations = evidence,
            staleVisualizationRevisionRejected = staleRevisionRejected,
            staleViewsCollectionRejected = staleViewsRejected,
            hdfSources = copiedHdfHashes.Select(pair => new { path = pair.Key, sha256 = pair.Value }).ToArray(),
            originalHdfUnchanged,
            copiedHdfUnchanged,
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellation);
        return report;
    }

    private static async Task<JsonElement> InspectMapViewsAsync(HttpClient client,
        MapViewsRequestValues request, CancellationToken cancellation)
    {
        JsonElement result = (await SubmitOperationAsync(client, "map.views.inspect", request, cancellation)).Result;
        if (!StringProperty(result, "action").Equals(request.Action, StringComparison.Ordinal) ||
            !StringProperty(result, "projectRevision").Equals(request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(result, "projectXmlSHA").Equals(request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(result, "projectSessionID").Equals(request.ProjectSessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(result, "projectXml").Equals(request.ProjectXml, StringComparison.Ordinal) ||
            !IsMapViewsSha256(StringProperty(result, "viewsSHA256")))
            throw new InvalidDataException("map.views.inspect returned a different project, session, or views capture.");
        if (request.Action != "list" && !BooleanProperty(result, "canCommit"))
            throw new InvalidDataException("map.views.inspect rejected a valid fixture change: " + OptionalString(result, "validationMessage"));
        return result;
    }

    private static async Task<JsonElement> CommitMapViewAsync(HttpClient client,
        MapViewsRequestValues request, JsonElement inspection, string? expectedVisualizationRevision,
        CancellationToken cancellation)
    {
        var commit = new MapViewsCommitValues(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID, request.Action, expectedVisualizationRevision,
            StringProperty(inspection, "viewsSHA256"), request.TargetName,
            request.TargetName is null ? null : StringProperty(Property(inspection, "target"), "entrySHA256"),
            request.Name, request.Extent, request.Shortcut);
        JsonElement receipt = (await SubmitOperationAsync(client, "map.views.commit", commit, cancellation)).Result;
        ValidateMapViewReceipt(receipt, request, inspection, expectedVisualizationRevision);
        AssertMapViewsDiskRevision(StringProperty(receipt, "projectPath") is { } receiptProject
                ? Path.ChangeExtension(receiptProject, ".vis") : throw new InvalidDataException("The view receipt omitted projectPath."),
            StringProperty(receipt, "visualizationRevision"),
            "A saved-view commit receipt does not match the copied .vis file.");
        return receipt;
    }

    private static void ValidateMapViewReceipt(JsonElement receipt, MapViewsRequestValues request,
        JsonElement inspection, string? expectedVisualizationRevision)
    {
        if (!BooleanProperty(receipt, "success") || StringProperty(receipt, "action") != request.Action ||
            !StringProperty(receipt, "projectRevision").Equals(request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "projectXmlSHA").Equals(request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "projectSessionID").Equals(request.ProjectSessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "projectXml").Equals(request.ProjectXml, StringComparison.Ordinal) ||
            !StringProperty(receipt, "previousViewsSHA256").Equals(StringProperty(inspection, "viewsSHA256"), StringComparison.OrdinalIgnoreCase) ||
            OptionalString(receipt, "previousVisualizationRevision") != expectedVisualizationRevision ||
            !IsMapViewsSha256(StringProperty(receipt, "viewsSHA256")) ||
            !IsMapViewsSha256(StringProperty(receipt, "visualizationRevision")))
            throw new InvalidDataException("The map.views.commit receipt does not match its inspected project and .vis capture.");

        JsonElement before = PropertyOrNull(receipt, "before");
        if (request.TargetName is null)
        {
            if (before.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidDataException("Creating a saved view returned an unexpected before entry.");
        }
        else if (StringProperty(before, "name") != request.TargetName ||
                 !StringProperty(before, "entrySHA256").Equals(
                     StringProperty(Property(inspection, "target"), "entrySHA256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved-view transaction changed the captured before entry.");

        JsonElement after = PropertyOrNull(receipt, "after");
        if (request.Action == "delete")
        {
            if (after.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidDataException("A deleted saved view remained in the receipt.");
        }
        else
        {
            string expectedName = request.Action == "rename" ? request.Name! : request.TargetName ?? request.Name!;
            if (StringProperty(after, "name") != expectedName ||
                !IsMapViewsSha256(StringProperty(after, "entrySHA256")))
                throw new InvalidDataException("The saved-view receipt did not return its expected after entry.");
        }
    }

    private static async Task<bool> MapViewCommitRejectedAsync(HttpClient client,
        MapViewsCommitValues request, CancellationToken cancellation)
    {
        try
        {
            _ = await SubmitOperationAsync(client, "map.views.commit", request, cancellation);
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static MapViewsAcceptanceEvidence MapViewEvidence(JsonElement receipt) => new(
        StringProperty(receipt, "action"),
        OptionalString(receipt, "previousVisualizationRevision") ?? "(missing)",
        StringProperty(receipt, "visualizationRevision"),
        StringProperty(receipt, "previousViewsSHA256"),
        StringProperty(receipt, "viewsSHA256"),
        OptionalString(PropertyOrNull(receipt, "before"), "name"),
        OptionalString(PropertyOrNull(receipt, "before"), "entrySHA256"),
        OptionalString(PropertyOrNull(receipt, "after"), "entrySHA256"));

    private static string MapViewsFileSHA256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertMapViewsDiskRevision(string path, string revision, string message)
    {
        if (!MapViewsFileSHA256(path).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static void AssertMapViewExtent(JsonElement savedView, double left, double top,
        double right, double bottom)
    {
        JsonElement extent = Property(savedView, "extent");
        if (Property(extent, "left").GetDouble() != left || Property(extent, "top").GetDouble() != top ||
            Property(extent, "right").GetDouble() != right || Property(extent, "bottom").GetDouble() != bottom)
            throw new InvalidDataException("The saved-view extent did not round-trip in Left/Top/Right/Bottom order.");
    }

    private static string MapViewsSHA256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static string MapViewsElementSHA(XElement? element) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            element is null ? "hecras-map-views-acceptance:absent" : element.ToString(SaveOptions.DisableFormatting))));

    private static bool MapViewsPathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsMapViewsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
