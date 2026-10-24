using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ras.Backend.ServerChecks;

internal sealed record ProjectAssociationAcceptanceReport(
    string State,
    string ManifestPath,
    string ProjectPath,
    Guid ProjectSessionID,
    string InitialProjectRevision,
    string FinalProjectRevision,
    int CommitCount,
    bool StaleSourceReferencesRejected,
    bool StaleProjectRevisionRejected,
    bool ProjectOpenReadback,
    bool ProjectTreeReadback,
    bool OriginalHdfBytesUnchanged,
    bool CopiedHdfBytesUnchanged);

internal static partial class ServerAcceptance
{
    private sealed record AssociationAcceptanceProjectState(
        string ProjectPath,
        string ProjectXml,
        string ProjectRevision,
        string ProjectXmlSHA,
        Guid SessionID);

    public static async Task<ProjectAssociationAcceptanceReport> RunProjectAssociationsAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Project-association acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(resultHoverManifestPath);
        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(sourceManifestPath), JsonOptions)
            ?? throw new InvalidDataException("The source Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || !File.Exists(source.ProjectPath) || source.Sources.Length == 0)
            throw new InvalidDataException("Project-association acceptance requires the completed Result-hover fixture.");

        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source.ProjectPath))!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Project-association acceptance requires a new output directory; existing data is never overwritten.");
        string outputRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        CopyDirectory(sourceRoot, outputRoot);
        string Rebase(string path) => RebasePath(path, sourceRoot, outputRoot);
        string projectPath = Rebase(source.ProjectPath);
        string planPath = Rebase(source.PlanPath);
        string manifestPath = Path.Combine(outputDirectory, "project-association-http-e2e-manifest.json");

        string[] sourceHdfPaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> originalHdfHashes = sourceHdfPaths.ToDictionary(
            path => path, FileSha256, StringComparer.Ordinal);
        Dictionary<string, string> copiedHdfHashes = sourceHdfPaths.ToDictionary(
            Rebase, path => FileSha256(Rebase(path)), StringComparer.Ordinal);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        Guid sessionID = Guid.NewGuid();
        JsonElement opened = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        AssociationAcceptanceProjectState state = AssociationStateFromOpen(opened, sessionID);
        string initialProjectRevision = state.ProjectRevision;
        if (!PathEquals(state.ProjectPath, projectPath) ||
            !FileSha256(state.ProjectPath).Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!File.Exists(planPath))
            throw new FileNotFoundException("The fixture Plan path did not rebase into the copied project.", planPath);

        string resultPath = Rebase(source.Sources[0].ResultPath);
        byte[] projectBeforeReadOnlyCheck = await File.ReadAllBytesAsync(state.ProjectPath, cancellation);
        byte[] resultBeforeReadOnlyCheck = await File.ReadAllBytesAsync(resultPath, cancellation);
        bool resultAssociationRejected = await AssociationInspectRejectedAsync(client, state, sessionID,
            "plan", resultPath, cancellation);
        if (!resultAssociationRejected ||
            !File.ReadAllBytes(state.ProjectPath).AsSpan().SequenceEqual(projectBeforeReadOnlyCheck) ||
            !File.ReadAllBytes(resultPath).AsSpan().SequenceEqual(resultBeforeReadOnlyCheck))
            throw new InvalidDataException("The saved Result was accepted as a writable Plan or changed fixture bytes.");

        // The Result-hover fixture has one saved Geometry and Boundary Condition.
        // The first Plan commit creates the standard C# PlanAssociations row.
        JsonElement planInspection = await InspectProjectAssociationAsync(client, state, sessionID,
            "plan", planPath, cancellation);
        JsonElement selectedGeometry = AssociationOption(planInspection, "geometry");
        JsonElement selectedBoundary = AssociationOption(planInspection, "boundaryCondition");
        string geometryPath = StringProperty(selectedGeometry, "path");
        string geometrySHA = StringProperty(selectedGeometry, "sha256");
        string boundaryPath = StringProperty(selectedBoundary, "path");
        string boundarySHA = StringProperty(selectedBoundary, "sha256");
        string planName = StringProperty(Property(planInspection, "target"), "name");
        EnsureAssociationOptionFile(selectedGeometry);
        EnsureAssociationOptionFile(selectedBoundary);

        object stalePlanCommit = AssociationCommitRequest(state, sessionID, "plan", planPath,
            StringProperty(Property(planInspection, "target"), "sha256"),
            planInspection, geometryPath, geometrySHA, boundaryPath, boundarySHA,
            null, null, clearTerrain: false, null, null, clearNValue: false);
        JsonElement planReceipt = (await SubmitOperationAsync(client, "project.association.commit",
            stalePlanCommit, cancellation)).Result;
        AssociationAcceptanceProjectState previousPlanState = state;
        state = ValidateAssociationReceipt(planReceipt, previousPlanState, sessionID,
            "plan", planPath, planInspection);
        VerifyPlanAssociationXml(state.ProjectXml, planName,
            StringProperty(selectedGeometry, "name"), StringProperty(selectedBoundary, "name"));
        await VerifyAssociationProjectReadbackAsync(client, state, "plan", planPath, cancellation);

        byte[] bytesBeforeStaleProjectCommit = await File.ReadAllBytesAsync(state.ProjectPath, cancellation);
        bool staleProjectRejected = await AssociationCommitRejectedAsync(client, stalePlanCommit, cancellation);
        if (!File.ReadAllBytes(state.ProjectPath).AsSpan().SequenceEqual(bytesBeforeStaleProjectCommit) ||
            !staleProjectRejected)
            throw new InvalidDataException("The stale Project-association commit was accepted or changed the committed project bytes.");

        // A deliberately incorrect aggregate dependency SHA exercises stale-source
        // rejection without touching any copied or original HDF bytes.
        JsonElement geometryInspection = await InspectProjectAssociationAsync(client, state, sessionID,
            "geometry", geometryPath, cancellation);
        string targetGeometrySHA = StringProperty(Property(geometryInspection, "target"), "sha256");
        string geometryName = StringProperty(Property(geometryInspection, "target"), "name");
        JsonElement originalTerrain = OptionalProperty(Property(geometryInspection, "before"), "terrain");
        JsonElement originalNValue = OptionalProperty(Property(geometryInspection, "before"), "nValue");
        if (originalTerrain.ValueKind != JsonValueKind.Object || originalNValue.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The actual fixture Geometry must have both Terrain and N-Value associations.");
        string terrainPath = StringProperty(originalTerrain, "path");
        string terrainSHA = StringProperty(originalTerrain, "sha256");
        string nValuePath = StringProperty(originalNValue, "path");
        string nValueSHA = StringProperty(originalNValue, "sha256");
        EnsureAssociationOptionFile(originalTerrain);
        EnsureAssociationOptionFile(originalNValue);

        object staleSourceCommit = AssociationCommitRequest(state, sessionID, "geometry", geometryPath,
            targetGeometrySHA, geometryInspection, null, null, null, null,
            terrainPath, terrainSHA, clearTerrain: false, nValuePath, nValueSHA, clearNValue: false,
            expectedSourceReferencesSHA256: DeliberatelyDifferentSHA(StringProperty(geometryInspection, "sourceReferencesSHA256")));
        bool staleSourceRejected = await AssociationCommitRejectedAsync(client, staleSourceCommit, cancellation);
        if (!staleSourceRejected || !FileSha256(state.ProjectPath).Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A stale source-reference fingerprint was accepted or changed the project.");

        // Clear both nullable Geometry dependencies in the disposable copy, read
        // the C# XML back, then restore the exact original model selections.
        object clearGeometryCommit = AssociationCommitRequest(state, sessionID, "geometry", geometryPath,
            targetGeometrySHA, geometryInspection, null, null, null, null,
            null, null, clearTerrain: true, null, null, clearNValue: true);
        JsonElement clearedReceipt = (await SubmitOperationAsync(client, "project.association.commit",
            clearGeometryCommit, cancellation)).Result;
        AssociationAcceptanceProjectState beforeClear = state;
        state = ValidateAssociationReceipt(clearedReceipt, beforeClear, sessionID,
            "geometry", geometryPath, geometryInspection);
        VerifyGeometryAssociationXml(state.ProjectXml, geometryName, null, null);
        await VerifyAssociationProjectReadbackAsync(client, state, "geometry", geometryPath, cancellation);

        JsonElement restoreInspection = await InspectProjectAssociationAsync(client, state, sessionID,
            "geometry", geometryPath, cancellation);
        JsonElement terrainChoice = FindAssociationOption(restoreInspection, "terrain", terrainPath, terrainSHA);
        JsonElement nValueChoice = FindAssociationOption(restoreInspection, "nValue", nValuePath, nValueSHA);
        object restoreCommit = AssociationCommitRequest(state, sessionID, "geometry", geometryPath,
            targetGeometrySHA, restoreInspection, null, null, null, null,
            StringProperty(terrainChoice, "path"), StringProperty(terrainChoice, "sha256"), clearTerrain: false,
            StringProperty(nValueChoice, "path"), StringProperty(nValueChoice, "sha256"), clearNValue: false);
        JsonElement restoredReceipt = (await SubmitOperationAsync(client, "project.association.commit",
            restoreCommit, cancellation)).Result;
        AssociationAcceptanceProjectState beforeRestore = state;
        state = ValidateAssociationReceipt(restoredReceipt, beforeRestore, sessionID,
            "geometry", geometryPath, restoreInspection);
        VerifyGeometryAssociationXml(state.ProjectXml, geometryName,
            StringProperty(terrainChoice, "name"), StringProperty(nValueChoice, "name"));
        await VerifyAssociationProjectReadbackAsync(client, state, "geometry", geometryPath, cancellation);

        bool originalHdfUnchanged = originalHdfHashes.All(pair => File.Exists(pair.Key) &&
            FileSha256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        bool copiedHdfUnchanged = copiedHdfHashes.All(pair => File.Exists(pair.Key) &&
            FileSha256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        if (!originalHdfUnchanged || !copiedHdfUnchanged)
            throw new IOException("An original or copied model/Result HDF changed during association acceptance.");

        const int commitCount = 3;
        var manifest = new
        {
            schema = "hecras-project-association-http-e2e/v1",
            sourceManifestPath,
            projectPath = state.ProjectPath,
            projectSessionID = sessionID,
            initialProjectRevision,
            finalProjectRevision = state.ProjectRevision,
            targets = new { plan = planPath, geometry = geometryPath },
            plan = new
            {
                geometry = new { path = geometryPath, sha256 = geometrySHA },
                boundaryCondition = new { path = boundaryPath, sha256 = boundarySHA },
                associationRowReadback = true
            },
            geometry = new
            {
                terrain = new { path = terrainPath, sha256 = terrainSHA },
                nValue = new { path = nValuePath, sha256 = nValueSHA },
                clearedBothAndRestored = true
            },
            staleSourceReferencesRejected = staleSourceRejected,
            staleProjectRevisionRejected = staleProjectRejected,
            resultAssociationRejected = resultAssociationRejected,
            projectOpenReadback = true,
            projectTreeReadback = true,
            originalHdfBytesUnchanged = originalHdfUnchanged,
            copiedHdfBytesUnchanged = copiedHdfUnchanged,
            originalHdfSHA256 = originalHdfHashes,
            copiedHdfSHA256 = copiedHdfHashes
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);
        return new("passed", manifestPath, state.ProjectPath, sessionID, initialProjectRevision,
            state.ProjectRevision, commitCount, staleSourceRejected, staleProjectRejected,
            true, true, originalHdfUnchanged, copiedHdfUnchanged);
    }

    private static AssociationAcceptanceProjectState AssociationStateFromOpen(JsonElement opened, Guid sessionID)
    {
        string xml = StringProperty(opened, "projectXml");
        return new(StringProperty(opened, "projectPath"), xml, StringProperty(opened, "revision"),
            Sha256(Encoding.UTF8.GetBytes(xml)), sessionID);
    }

    private static async Task<JsonElement> InspectProjectAssociationAsync(HttpClient client,
        AssociationAcceptanceProjectState state, Guid sessionID, string targetKind,
        string targetPath, CancellationToken cancellation)
    {
        string targetRevision = FileSha256(targetPath);
        JsonElement inspection = (await SubmitOperationAsync(client, "project.association.inspect", new
        {
            targetKind,
            projectPath = state.ProjectPath,
            projectXml = state.ProjectXml,
            projectXmlSHA = state.ProjectXmlSHA,
            projectRevision = state.ProjectRevision,
            projectSessionID = sessionID,
            targetAssetPath = targetPath,
            targetAssetRevision = targetRevision
        }, cancellation)).Result;
        if (!BooleanProperty(inspection, "canCommit") ||
            !StringProperty(inspection, "projectRevision").Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(inspection, "projectXmlSHA").Equals(state.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(inspection, "projectSessionID").Equals(sessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(inspection, "targetKind").Equals(targetKind, StringComparison.Ordinal) ||
            !PathEquals(StringProperty(Property(inspection, "target"), "path"), targetPath) ||
            !StringProperty(Property(inspection, "target"), "sha256").Equals(targetRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsAssociationSHA(StringProperty(inspection, "sourceReferencesSHA256")))
            throw new InvalidDataException("project.association.inspect returned a different Project, target, or dependency identity.");
        return inspection;
    }

    private static object AssociationCommitRequest(AssociationAcceptanceProjectState state, Guid sessionID,
        string targetKind, string targetPath, string targetSHA, JsonElement inspection,
        string? geometryPath, string? geometrySHA, string? boundaryPath, string? boundarySHA,
        string? terrainPath, string? terrainSHA, bool clearTerrain,
        string? nValuePath, string? nValueSHA, bool clearNValue,
        string? expectedSourceReferencesSHA256 = null) => new
    {
        targetKind,
        projectPath = state.ProjectPath,
        projectXml = state.ProjectXml,
        projectXmlSHA = state.ProjectXmlSHA,
        projectRevision = state.ProjectRevision,
        projectSessionID = sessionID,
        targetAssetPath = targetPath,
        targetAssetRevision = targetSHA,
        expectedTargetSHA256 = targetSHA,
        expectedAssociationSHA256 = StringProperty(inspection, "associationSHA256"),
        expectedOptionsSHA256 = StringProperty(inspection, "optionsSHA256"),
        expectedSourceReferencesSHA256 = expectedSourceReferencesSHA256 ??
            StringProperty(inspection, "sourceReferencesSHA256"),
        geometryPath,
        geometrySHA256 = geometrySHA,
        boundaryConditionPath = boundaryPath,
        boundaryConditionSHA256 = boundarySHA,
        terrainPath,
        terrainSHA256 = terrainSHA,
        clearTerrain,
        nValuePath,
        nValueSHA256 = nValueSHA,
        clearNValue
    };

    private static AssociationAcceptanceProjectState ValidateAssociationReceipt(JsonElement receipt,
        AssociationAcceptanceProjectState previous, Guid sessionID, string targetKind,
        string targetPath, JsonElement inspection)
    {
        if (!BooleanProperty(receipt, "success") ||
            !StringProperty(receipt, "targetKind").Equals(targetKind, StringComparison.Ordinal) ||
            !StringProperty(receipt, "previousProjectRevision").Equals(previous.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "previousSourceReferencesSHA256").Equals(
                StringProperty(inspection, "sourceReferencesSHA256"), StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "sourceReferencesSHA256").Equals(
                StringProperty(inspection, "sourceReferencesSHA256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The association receipt does not match its inspected Project or dependency capture.");

        JsonElement target = Property(receipt, "target");
        if (!PathEquals(StringProperty(target, "path"), targetPath) ||
            !StringProperty(target, "sha256").Equals(StringProperty(Property(inspection, "target"), "sha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The association receipt target identity differs from the exact inspected asset.");

        JsonElement project = Property(receipt, "project");
        string xml = StringProperty(project, "projectXml");
        string revision = StringProperty(project, "revision");
        if (!BooleanProperty(project, "success") ||
            !PathEquals(StringProperty(project, "projectPath"), previous.ProjectPath) ||
            !StringProperty(project, "projectSessionID").Equals(sessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !Sha256(Encoding.UTF8.GetBytes(xml)).Equals(StringProperty(project, "projectXmlSHA"), StringComparison.OrdinalIgnoreCase) ||
            !FileSha256(previous.ProjectPath).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The association receipt does not confirm the persisted Project bytes and session.");

        JsonElement sourceCapture = Property(receipt, "sourceCapture");
        if (!StringProperty(sourceCapture, "sourceReferencesSHA256").Equals(
                StringProperty(inspection, "sourceReferencesSHA256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The association receipt dependency capture differs from inspection.");
        return new(previous.ProjectPath, xml, revision, StringProperty(project, "projectXmlSHA"), sessionID);
    }

    private static async Task VerifyAssociationProjectReadbackAsync(HttpClient client,
        AssociationAcceptanceProjectState state, string targetKind, string targetPath,
        CancellationToken cancellation)
    {
        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = state.ProjectPath }, cancellation)).Result;
        if (!PathEquals(StringProperty(opened, "projectPath"), state.ProjectPath) ||
            !StringProperty(opened, "revision").Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(StringProperty(opened, "projectXml"), state.ProjectXml, StringComparison.Ordinal))
            throw new InvalidDataException("project.open did not read back the exact committed association Project.");

        JsonElement tree = (await SubmitOperationAsync(client, "project.tree.load", new
        {
            projectPath = state.ProjectPath,
            projectXml = state.ProjectXml,
            mode = "Project",
            projectSessionID = state.SessionID
        }, cancellation)).Result;
        CatalogTreeNode[] roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        string expectedSourceClass = targetKind == "plan" ? "ObservablePlan" : "ObservableGeometry";
        CatalogTreeNode[] matches = WalkAssociationAcceptanceTree(roots).Where(node =>
            node.SourceClass == expectedSourceClass && node.AssetID is { } asset && PathEquals(asset, targetPath)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("project.tree.load did not expose the exact saved association target.");

        JsonElement inspection = await InspectProjectAssociationAsync(client, state, state.SessionID,
            targetKind, targetPath, cancellation);
        if (!BooleanProperty(inspection, "canCommit"))
            throw new InvalidDataException("The reopened association target could not be inspected.");
    }

    private static async Task<bool> AssociationCommitRejectedAsync(HttpClient client,
        object request, CancellationToken cancellation)
    {
        try
        {
            _ = await SubmitOperationAsync(client, "project.association.commit", request, cancellation);
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static async Task<bool> AssociationInspectRejectedAsync(HttpClient client,
        AssociationAcceptanceProjectState state, Guid sessionID, string targetKind,
        string targetPath, CancellationToken cancellation)
    {
        try
        {
            _ = await SubmitOperationAsync(client, "project.association.inspect", new
            {
                targetKind,
                projectPath = state.ProjectPath,
                projectXml = state.ProjectXml,
                projectXmlSHA = state.ProjectXmlSHA,
                projectRevision = state.ProjectRevision,
                projectSessionID = sessionID,
                targetAssetPath = targetPath,
                targetAssetRevision = FileSha256(targetPath)
            }, cancellation);
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static JsonElement AssociationOption(JsonElement inspection, string kind)
    {
        JsonElement array = Property(Property(inspection, "options"), kind);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 1)
            throw new InvalidDataException($"The actual Result-hover fixture must expose exactly one {kind} option.");
        JsonElement option = array[0];
        EnsureAssociationOptionFile(option);
        return option;
    }

    private static JsonElement FindAssociationOption(JsonElement inspection, string kind,
        string expectedPath, string expectedSHA)
    {
        JsonElement array = Property(Property(inspection, "options"), kind);
        JsonElement[] matches = array.EnumerateArray().Where(option =>
            PathEquals(StringProperty(option, "path"), expectedPath) &&
            StringProperty(option, "sha256").Equals(expectedSHA, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The original {kind} selection was not uniquely present after reopening the project.");
        EnsureAssociationOptionFile(matches[0]);
        return matches[0];
    }

    private static void EnsureAssociationOptionFile(JsonElement option)
    {
        string path = StringProperty(option, "path");
        string sha = StringProperty(option, "sha256");
        if (!BooleanProperty(option, "exists") || !File.Exists(path) ||
            !FileSha256(path).Equals(sha, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(option, "revision").Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void VerifyPlanAssociationXml(string xml, string planName,
        string geometryName, string boundaryConditionName)
    {
        XElement root = XElement.Parse(xml, LoadOptions.None);
        XElement section = root.Elements().Single(element => element.Name.LocalName == "PlanAssociations");
        XElement row = section.Elements().Single(element => element.Name.LocalName == "PlanAssociation" &&
            (string?)element.Attribute("Plan") == planName);
        if ((string?)row.Attribute("Geometry") != geometryName ||
            (string?)row.Attribute("BoundaryCondition") != boundaryConditionName)
            throw new InvalidDataException("The saved PlanAssociations row does not match the selected C# Geometry and Boundary Condition.");
    }

    private static void VerifyGeometryAssociationXml(string xml, string geometryName,
        string? terrainName, string? nValueName)
    {
        XElement root = XElement.Parse(xml, LoadOptions.None);
        XElement[] sections = root.Elements().Where(element => element.Name.LocalName == "GeometryAssociations").ToArray();
        if (sections.Length > 1)
            throw new InvalidDataException("The saved project contains duplicate GeometryAssociations sections.");

        XElement[] rows = sections.SingleOrDefault()?.Elements().Where(element =>
            element.Name.LocalName == "GeometryAssociation" &&
            (string?)element.Attribute("Geometry") == geometryName).ToArray() ?? [];
        if (terrainName is null && nValueName is null)
        {
            // The retained C# Project.Save omits rows where GeometryAssociation.IsDefault()
            // is true. The transaction may preserve an empty group/unknown XML nodes, but
            // this selected Geometry must have no persisted association row after clearing.
            if (rows.Length != 0)
                throw new InvalidDataException("The cleared default Geometry association row was not omitted from the saved project.");
            return;
        }

        if (sections.Length != 1 || rows.Length != 1)
            throw new InvalidDataException("The restored Geometry association row is missing or duplicated.");
        XElement row = rows[0];
        if ((string?)row.Attribute("Terrain") != terrainName || (string?)row.Attribute("N_Values") != nValueName)
            throw new InvalidDataException("The saved GeometryAssociations row does not match the selected Terrain and N-Value state.");
    }

    private static IEnumerable<CatalogTreeNode> WalkAssociationAcceptanceTree(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            if (node.Children is not null)
                foreach (CatalogTreeNode child in WalkAssociationAcceptanceTree(node.Children)) yield return child;
        }
    }

    private static JsonElement OptionalProperty(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? default : property;
    }

    private static string DeliberatelyDifferentSHA(string actual) =>
        new string(actual.All(character => character == '0') ? 'F' : '0', 64);

    private static bool IsAssociationSHA(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
