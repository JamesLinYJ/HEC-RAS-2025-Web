using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ras.Backend.ServerChecks;

internal sealed record ModelCreationAcceptanceReport(
    string State,
    string ManifestPath,
    string ProjectPath,
    Guid ProjectSessionID,
    string InitialProjectRevision,
    string FinalProjectRevision,
    string GeometryPath,
    string GeometryRevision,
    string BoundaryConditionPath,
    string BoundaryConditionRevision,
    string PlanPath,
    string PlanRevision,
    bool ProjectOpenReadback,
    bool CatalogReadback,
    bool StaleRevisionRejected,
    bool StaleRevisionCreatedNoFile,
    bool OriginalSourceHdfBytesUnchanged);

internal static partial class ServerAcceptance
{
    public static async Task<ModelCreationAcceptanceReport> RunModelCreationAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Model-creation acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));
        string referenceManifestPath = Path.GetFullPath(resultHoverManifestPath);
        ResultHoverManifest reference = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(referenceManifestPath), JsonOptions)
            ?? throw new InvalidDataException("The source Result-hover manifest is empty.");
        if (reference.Schema != "hecras-result-hover-e2e/v1" || !File.Exists(reference.ProjectPath))
            throw new InvalidDataException("Model creation requires a completed Result-hover project fixture.");

        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(reference.ProjectPath))!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Model-creation acceptance requires a new output directory; existing data is never overwritten.");
        string outputRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        CopyDirectory(sourceRoot, outputRoot);
        string projectPath = RebasePath(reference.ProjectPath, sourceRoot, outputRoot);
        string outputManifestPath = Path.Combine(outputDirectory, "model-creation-commit-manifest.json");

        string[] sourceHdfPaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".h5" or ".hdf")
            .Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> sourceHdfHashes = sourceHdfPaths.ToDictionary(path => path,
            FileSha256, StringComparer.Ordinal);
        Dictionary<string, string> copiedHdfHashes = sourceHdfPaths.ToDictionary(
            path => RebasePath(path, sourceRoot, outputRoot), path => FileSha256(RebasePath(path, sourceRoot, outputRoot)),
            StringComparer.Ordinal);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        Guid sessionID = Guid.NewGuid();
        JsonElement opened = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        string currentProjectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        if (!PathEquals(currentProjectPath, projectPath)) throw new SourceFileChangedException();
        string currentXml = StringProperty(opened, "projectXml");
        string initialProjectRevision = StringProperty(opened, "revision");
        string initialProjectXmlSHA = Sha256(Encoding.UTF8.GetBytes(currentXml));
        if (!FileSha256(currentProjectPath).Equals(initialProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string geometryName = "提交验收几何";
        Dictionary<string, string> geometryFields = new(StringComparer.Ordinal)
        {
            ["NewGeometryControl/GeometryName"] = geometryName,
            ["NewGeometryControl/SelectedTerrain"] = "",
            ["NewGeometryControl/SelectedNValue"] = ""
        };
        JsonElement geometryChoicesInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newGeometry", projectPath = currentProjectPath, projectXml = currentXml,
                fields = geometryFields, projectSessionID = sessionID }, cancellation)).Result;
        string terrainPath = ChoiceID(geometryChoicesInspection, "NewGeometryControl/SelectedTerrain");
        string terrainName = ChoiceTitle(geometryChoicesInspection, "NewGeometryControl/SelectedTerrain", terrainPath);
        geometryFields["NewGeometryControl/SelectedTerrain"] = terrainPath;

        Dictionary<string, string> staleFields = new(geometryFields, StringComparer.Ordinal)
        {
            ["NewGeometryControl/GeometryName"] = "拒绝旧修订几何"
        };
        JsonElement staleInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newGeometry", projectPath = currentProjectPath, projectXml = currentXml,
                fields = staleFields, projectSessionID = sessionID }, cancellation)).Result;
        string staleInspectionRevision = StringProperty(staleInspection, "revision");
        string staleAssetPath = Path.Combine(Path.GetDirectoryName(currentProjectPath)!, "Geometries", "拒绝旧修订几何.h5");

        JsonElement geometryInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newGeometry", projectPath = currentProjectPath, projectXml = currentXml,
                fields = geometryFields, projectSessionID = sessionID }, cancellation)).Result;
        EnsureCanExecute(geometryInspection, "Geometry");
        JsonElement geometryReceipt = (await SubmitOperationAsync(client, "modelCreation.commit",
            CommitRequest("newGeometry", currentProjectPath, currentXml, geometryFields,
                StringProperty(geometryInspection, "revision"), sessionID, initialProjectRevision,
                initialProjectXmlSHA), cancellation)).Result;
        CreatedCommit geometry = ValidateCommitReceipt(geometryReceipt, "Geometry", geometryName,
            sessionID, currentProjectPath, currentXml, initialProjectRevision, geometryFields);
        currentProjectPath = geometry.ProjectPath;
        currentXml = geometry.ProjectXml;
        string geometryProjectRevision = geometry.ProjectRevision;
        bool projectOpenReadback = await VerifyProjectOpenAndCatalogAsync(client, cancellation,
            currentProjectPath, currentXml, geometryProjectRevision, sessionID,
            geometry.AssetPath, "ObservableGeometry");

        bool staleRevisionRejected = false;
        try
        {
            await SubmitOperationAsync(client, "modelCreation.commit",
                CommitRequest("newGeometry", projectPath, StringProperty(opened, "projectXml"), staleFields,
                    staleInspectionRevision, sessionID, initialProjectRevision, initialProjectXmlSHA), cancellation);
        }
        catch (InvalidOperationException) { staleRevisionRejected = true; }
        if (!staleRevisionRejected) throw new InvalidDataException("A commit with a stale project revision was not rejected.");
        if (File.Exists(staleAssetPath)) throw new InvalidDataException("The stale project revision created an HDF file before rejection.");
        _ = await SubmitOperationAsync(client, "backend.info", new { }, cancellation);

        Dictionary<string, string> boundaryFields = new(StringComparer.Ordinal)
        {
            ["NewBoundaryConditionControl/BoundaryConditionName"] = "提交验收边界",
            ["NewBoundaryConditionControl/StartTime"] = "",
            ["NewBoundaryConditionControl/EndTime"] = ""
        };
        JsonElement boundaryDefaultsInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newBoundary", projectPath = currentProjectPath, projectXml = currentXml,
                fields = boundaryFields, projectSessionID = sessionID }, cancellation)).Result;
        JsonElement boundaryDefaults = Property(boundaryDefaultsInspection, "boundaryDefaults");
        boundaryFields["NewBoundaryConditionControl/StartTime"] = StringProperty(boundaryDefaults, "start");
        boundaryFields["NewBoundaryConditionControl/EndTime"] = StringProperty(boundaryDefaults, "end");
        JsonElement boundaryInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newBoundary", projectPath = currentProjectPath, projectXml = currentXml,
                fields = boundaryFields, projectSessionID = sessionID }, cancellation)).Result;
        EnsureCanExecute(boundaryInspection, "BoundaryCondition");
        string boundaryPreviousRevision = geometryProjectRevision;
        string currentProjectXmlSHA = Sha256(Encoding.UTF8.GetBytes(currentXml));
        JsonElement boundaryReceipt = (await SubmitOperationAsync(client, "modelCreation.commit",
            CommitRequest("newBoundary", currentProjectPath, currentXml, boundaryFields,
                StringProperty(boundaryInspection, "revision"), sessionID, boundaryPreviousRevision,
                currentProjectXmlSHA), cancellation)).Result;
        CreatedCommit boundary = ValidateCommitReceipt(boundaryReceipt, "BoundaryCondition",
            boundaryFields["NewBoundaryConditionControl/BoundaryConditionName"], sessionID,
            currentProjectPath, currentXml, boundaryPreviousRevision, boundaryFields);
        if (!boundary.ProjectRevision.Equals(boundaryPreviousRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(boundary.ProjectXml, currentXml, StringComparison.Ordinal))
            throw new InvalidDataException("A BoundaryCondition commit changed a project XML section that the retained C# format does not define.");
        currentProjectPath = boundary.ProjectPath;
        currentXml = boundary.ProjectXml;
        string boundaryProjectRevision = boundary.ProjectRevision;
        projectOpenReadback &= await VerifyProjectOpenAndCatalogAsync(client, cancellation,
            currentProjectPath, currentXml, boundaryProjectRevision, sessionID,
            boundary.AssetPath, "ObservableBoundaryCondition");

        Dictionary<string, string> planFields = new(StringComparer.Ordinal)
        {
            ["NewPlanControl/PlanName"] = "提交验收计划",
            ["NewPlanControl/SelectedGeometry"] = geometry.AssetPath,
            ["NewPlanControl/SelectedBoundaryCondition"] = boundary.AssetPath,
            ["NewPlanControl/TimeWindowMode"] = "BoundaryCondition",
            ["NewPlanControl/StartTime"] = "",
            ["NewPlanControl/EndTime"] = ""
        };
        JsonElement planInspection = (await SubmitOperationAsync(client, "modelCreation.inspect",
            new { kind = "newPlan", projectPath = currentProjectPath, projectXml = currentXml,
                fields = planFields, projectSessionID = sessionID }, cancellation)).Result;
        EnsureCanExecute(planInspection, "Plan");
        JsonElement planReceipt = (await SubmitOperationAsync(client, "modelCreation.commit",
            CommitRequest("newPlan", currentProjectPath, currentXml, planFields,
                StringProperty(planInspection, "revision"), sessionID, boundaryProjectRevision,
                Sha256(Encoding.UTF8.GetBytes(currentXml))), cancellation)).Result;
        CreatedCommit plan = ValidateCommitReceipt(planReceipt, "Plan", planFields["NewPlanControl/PlanName"],
            sessionID, currentProjectPath, currentXml, boundaryProjectRevision, planFields);
        currentProjectPath = plan.ProjectPath;
        currentXml = plan.ProjectXml;
        string finalProjectRevision = plan.ProjectRevision;
        projectOpenReadback &= await VerifyProjectOpenAndCatalogAsync(client, cancellation,
            currentProjectPath, currentXml, finalProjectRevision, sessionID,
            plan.AssetPath, "ObservablePlan");

        VerifyGeometryAssociation(currentXml, geometryName, terrainName);
        VerifyPlanAssociation(currentXml, planFields["NewPlanControl/PlanName"], geometryName,
            boundaryFields["NewBoundaryConditionControl/BoundaryConditionName"]);
        bool copiedSourcesUnchanged = copiedHdfHashes.All(item =>
            File.Exists(item.Key) && FileSha256(item.Key).Equals(item.Value, StringComparison.OrdinalIgnoreCase));
        bool originalSourcesUnchanged = sourceHdfHashes.All(item =>
            File.Exists(item.Key) && FileSha256(item.Key).Equals(item.Value, StringComparison.OrdinalIgnoreCase));
        if (!copiedSourcesUnchanged || !originalSourcesUnchanged)
            throw new IOException("An existing source HDF changed during model creation acceptance.");

        var manifest = new
        {
            schema = "hecras-model-creation-commit-e2e/v1",
            sourceManifestPath = referenceManifestPath,
            projectPath = currentProjectPath,
            projectSessionID = sessionID,
            initialProjectRevision,
            initialProjectXmlSHA,
            finalProjectRevision,
            finalProjectXmlSHA = Sha256(Encoding.UTF8.GetBytes(currentXml)),
            geometry = new { path = geometry.AssetPath, revision = geometry.AssetRevision,
                name = geometryName, selectedTerrainPath = terrainPath, selectedTerrainName = terrainName,
                previousProjectRevision = geometry.PreviousProjectRevision, projectRevision = geometry.ProjectRevision },
            staleRevision = new { rejected = staleRevisionRejected, assetPath = staleAssetPath,
                fileWasNotCreated = !File.Exists(staleAssetPath), inspectionRevision = staleInspectionRevision },
            boundaryCondition = new { path = boundary.AssetPath, revision = boundary.AssetRevision,
                name = boundaryFields["NewBoundaryConditionControl/BoundaryConditionName"],
                previousProjectRevision = boundary.PreviousProjectRevision, projectRevision = boundary.ProjectRevision,
                projectXMLChanged = !string.Equals(boundary.ProjectXml, geometry.ProjectXml, StringComparison.Ordinal) },
            plan = new { path = plan.AssetPath, revision = plan.AssetRevision,
                name = planFields["NewPlanControl/PlanName"], geometryPath = geometry.AssetPath,
                boundaryConditionPath = boundary.AssetPath, previousProjectRevision = plan.PreviousProjectRevision,
                projectRevision = plan.ProjectRevision },
            originalSourceHdfSHA256 = sourceHdfHashes.ToDictionary(item => item.Key, item => item.Value),
            copiedSourceHdfSHA256 = copiedHdfHashes.ToDictionary(item => item.Key, item => item.Value),
            originalSourceHdfBytesUnchanged = originalSourcesUnchanged,
            copiedSourceHdfBytesUnchanged = copiedSourcesUnchanged,
            projectOpenReadback,
            catalogReadback = true
        };
        await File.WriteAllTextAsync(outputManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        return new("passed", outputManifestPath, currentProjectPath, sessionID,
            initialProjectRevision, finalProjectRevision, geometry.AssetPath, geometry.AssetRevision,
            boundary.AssetPath, boundary.AssetRevision, plan.AssetPath, plan.AssetRevision,
            projectOpenReadback, true, staleRevisionRejected, !File.Exists(staleAssetPath),
            originalSourcesUnchanged && copiedSourcesUnchanged);
    }

    private static object CommitRequest(string kind, string projectPath, string projectXml,
        Dictionary<string, string> fields, string inspectionRevision, Guid sessionID,
        string projectRevision, string projectXmlSHA) => new
    {
        kind,
        projectPath,
        projectXml,
        fields,
        revision = inspectionRevision,
        projectSessionID = sessionID,
        expectedProjectRevision = projectRevision,
        projectXmlSHA
    };

    private sealed record CreatedCommit(string AssetPath, string AssetRevision, string Name,
        string? PrimaryKey, string ProjectPath, string ProjectXml, string ProjectRevision,
        string PreviousProjectRevision);

    private static CreatedCommit ValidateCommitReceipt(JsonElement receipt, string kind, string name,
        Guid sessionID, string projectPath, string previousXml, string previousRevision,
        Dictionary<string, string> expectedAssociations)
    {
        JsonElement created = Property(receipt, "created");
        JsonElement project = Property(receipt, "project");
        string assetPath = StringProperty(created, "assetPath");
        string assetRevision = StringProperty(receipt, "assetRevision");
        string projectXml = StringProperty(project, "projectXml");
        string committedProjectPath = StringProperty(project, "projectPath");
        string committedProjectRevision = StringProperty(project, "revision");
        string projectXmlSHA = StringProperty(project, "projectXmlSHA");
        string previousProjectRevision = StringProperty(receipt, "previousProjectRevision");
        if (!PathEquals(committedProjectPath, projectPath) ||
            !committedProjectRevision.Equals(StringProperty(receipt, "previousProjectRevision"), StringComparison.OrdinalIgnoreCase) && kind == "BoundaryCondition" ||
            !previousProjectRevision.Equals(previousRevision, StringComparison.OrdinalIgnoreCase) ||
            StringProperty(project, "projectSessionID") != sessionID.ToString("D") ||
            !Sha256(Encoding.UTF8.GetBytes(projectXml)).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !assetRevision.Equals(FileSha256(assetPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(assetPath) || StringProperty(created, "name") != name)
            throw new InvalidDataException($"The {kind} commit receipt failed persistent-file verification.");
        if (kind == "BoundaryCondition" && !string.Equals(projectXml, previousXml, StringComparison.Ordinal))
            throw new InvalidDataException("The BoundaryCondition receipt changed the project XML.");
        if (kind == "Geometry" && StringProperty(created, "primaryKey") != "Geometry")
            throw new InvalidDataException("The Geometry receipt omitted its C# association key.");
        if (kind == "Plan" && StringProperty(created, "primaryKey") != "Plan")
            throw new InvalidDataException("The Plan receipt omitted its C# association key.");
        JsonElement associations = Property(created, "associations");
        if (kind == "Geometry" &&
            (StringProperty(associations, "Terrain") != ChoiceTitleFromPath(expectedAssociations["NewGeometryControl/SelectedTerrain"]) ||
             Property(associations, "N_Values").GetString() != ""))
            throw new InvalidDataException("The Geometry receipt did not preserve the inspected association sources.");
        if (kind == "Plan" &&
            (StringProperty(associations, "Geometry") != Path.GetFileNameWithoutExtension(expectedAssociations["NewPlanControl/SelectedGeometry"]) ||
             StringProperty(associations, "BoundaryCondition") != Path.GetFileNameWithoutExtension(expectedAssociations["NewPlanControl/SelectedBoundaryCondition"])))
            throw new InvalidDataException("The Plan receipt did not preserve the inspected association sources.");
        return new(assetPath, assetRevision, StringProperty(created, "name"), OptionalString(created, "primaryKey"),
            committedProjectPath, projectXml, committedProjectRevision, previousProjectRevision);
    }

    private static string ChoiceTitleFromPath(string path) => Path.GetFileNameWithoutExtension(path);

    private static string ChoiceID(JsonElement inspection, string choicesKey)
    {
        JsonElement choice = Property(Property(inspection, "choices"), choicesKey).EnumerateArray().FirstOrDefault();
        if (choice.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Model inspection returned no choices for {choicesKey}.");
        return StringProperty(choice, "id");
    }

    private static string ChoiceTitle(JsonElement inspection, string choicesKey, string id)
    {
        JsonElement choice = Property(Property(inspection, "choices"), choicesKey).EnumerateArray()
            .Single(item => StringProperty(item, "id") == id);
        return StringProperty(choice, "title");
    }

    private static void EnsureCanExecute(JsonElement inspection, string kind)
    {
        if (!BooleanProperty(inspection, "canExecute"))
            throw new InvalidDataException($"The {kind} inspection refused the known-valid acceptance fields: {OptionalString(inspection, "validationMessage")}");
    }

    private static async Task<bool> VerifyProjectOpenAndCatalogAsync(HttpClient client,
        CancellationToken cancellation, string projectPath, string expectedXml, string expectedRevision,
        Guid sessionID, string assetPath, string expectedSourceClass)
    {
        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        string readbackPath = StringProperty(opened, "projectPath");
        string readbackXml = StringProperty(opened, "projectXml");
        string readbackRevision = StringProperty(opened, "revision");
        if (!PathEquals(readbackPath, projectPath) || readbackRevision != expectedRevision ||
            !string.Equals(readbackXml, expectedXml, StringComparison.Ordinal) ||
            !FileSha256(readbackPath).Equals(readbackRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.open did not read back the exact committed project snapshot.");

        JsonElement tree = (await SubmitOperationAsync(client, "project.tree.load", new
        {
            projectPath = readbackPath,
            projectXml = readbackXml,
            mode = "Project",
            projectSessionID = sessionID
        }, cancellation)).Result;
        CatalogTreeNode[] roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNode[] matches = FlattenMapNodes(roots).Where(node => node.SourceClass == expectedSourceClass &&
            node.AssetID is { } nodePath && PathEquals(nodePath, assetPath)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The project tree did not expose the exact committed model HDF.");
        return true;
    }

    private static void VerifyGeometryAssociation(string projectXml, string geometryName, string terrainName)
    {
        XElement root = XElement.Parse(projectXml, LoadOptions.None);
        XElement section = root.Elements().Single(element => element.Name.LocalName == "GeometryAssociations");
        XElement row = section.Elements().Single(element => element.Name.LocalName == "GeometryAssociation" &&
            (string?)element.Attribute("Geometry") == geometryName);
        if ((string?)row.Attribute("Terrain") != terrainName)
            throw new InvalidDataException("The saved C# GeometryAssociations row omitted the exact Terrain name.");
    }

    private static void VerifyPlanAssociation(string projectXml, string planName, string geometryName,
        string boundaryConditionName)
    {
        XElement root = XElement.Parse(projectXml, LoadOptions.None);
        XElement section = root.Elements().Single(element => element.Name.LocalName == "PlanAssociations");
        XElement row = section.Elements().Single(element => element.Name.LocalName == "PlanAssociation" &&
            (string?)element.Attribute("Plan") == planName);
        if ((string?)row.Attribute("Geometry") != geometryName ||
            (string?)row.Attribute("BoundaryCondition") != boundaryConditionName)
            throw new InvalidDataException("The saved C# PlanAssociations row omitted its exact Geometry or BoundaryCondition.");
    }
}
