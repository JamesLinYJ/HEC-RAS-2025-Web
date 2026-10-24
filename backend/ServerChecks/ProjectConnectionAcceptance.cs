using System.Text;
using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal sealed record ProjectConnectionAcceptanceReport(
    string State,
    string ManifestPath,
    string ProjectPath,
    Guid ProjectSessionID,
    string InitialProjectRevision,
    string FinalProjectRevision,
    int AddCount,
    int UpdateCount,
    int RemoveCount,
    bool StaleSourceRevisionRejected,
    bool StaleProjectRevisionRejected,
    bool ProjectOpenReadback,
    bool CatalogReadback,
    bool OriginalSourceHdfBytesUnchanged);

internal static partial class ServerAcceptance
{
    private sealed record ProjectConnectionState(
        string ProjectPath, string ProjectXml, string ProjectRevision, string ProjectXmlSHA, Guid SessionID);

    private sealed record ConnectionInspectRequest(
        string Action, string Kind, string ProjectPath, string ProjectXml, string ProjectXmlSHA,
        string ProjectRevision, Guid ProjectSessionID, string? ConnectionName = null, string? Name = null,
        string? Path = null, string? Description = null, string? ResultName = null,
        string? SearchPattern = null, bool Recurse = false);

    private sealed record ConnectionCommitRequest(
        string Action, string Kind, string ProjectPath, string ProjectXml, string ProjectXmlSHA,
        string ProjectRevision, Guid ProjectSessionID, string? ConnectionName, string? Name,
        string? Path, string? Description, string? ResultName, string? ExpectedConnectionSHA256,
        string? ExpectedSourceSHA256, string ExpectedSourceReferencesSHA256,
        string? ExpectedNormalizedName, string? SearchPattern, bool Recurse);

    public static async Task<ProjectConnectionAcceptanceReport> RunProjectConnectionsAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Project-connection acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(resultHoverManifestPath);
        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(sourceManifestPath), JsonOptions)
            ?? throw new InvalidDataException("The source Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || !File.Exists(source.ProjectPath) ||
            source.Sources.Length < 2)
            throw new InvalidDataException("Acceptance requires the completed two-Result project fixture.");

        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source.ProjectPath))!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Acceptance requires a new output directory; existing data is never overwritten.");
        string outputRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        CopyDirectory(sourceRoot, outputRoot);
        string Rebase(string path) => RebasePath(path, sourceRoot, outputRoot);
        string projectPath = Rebase(source.ProjectPath);
        string manifestPath = Path.Combine(outputDirectory, "project-connection-acceptance-manifest.json");

        string[] hdfPaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".h5" or ".hdf").Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> originalHdf = hdfPaths.ToDictionary(path => path, FileSha256, StringComparer.Ordinal);
        Dictionary<string, string> copiedHdf = hdfPaths.ToDictionary(Rebase, path => FileSha256(Rebase(path)), StringComparer.Ordinal);

        string sourceDirectory = Path.Combine(outputRoot, "Connection Acceptance Sources");
        Directory.CreateDirectory(sourceDirectory);
        string sourceA = Path.Combine(sourceDirectory, "source-a.txt");
        string sourceB = Path.Combine(sourceDirectory, "source-b.txt");
        string staleSource = Path.Combine(sourceDirectory, "stale-source.txt");
        await File.WriteAllTextAsync(sourceA, "External file fixture A\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(sourceB, "External file fixture B\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(staleSource, "Stale-source baseline\n", new UTF8Encoding(false));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        Guid sessionID = Guid.NewGuid();
        JsonElement opened = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        var state = StateFromOpen(opened, sessionID);
        string initialProjectRevision = state.ProjectRevision;
        if (!PathEquals(state.ProjectPath, projectPath) ||
            !FileSha256(state.ProjectPath).Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var operations = new List<object>();
        int addCount = 0, updateCount = 0, removeCount = 0;

        // A changed target source must reject the stale SHA without changing project XML.
        var staleSourceValues = Values(state, sessionID, "add", "file", name: "Stale source", path: staleSource);
        JsonElement staleSourceInspection = await InspectConnectionAsync(client, staleSourceValues, cancellation);
        string staleSourceSHA = RequiredString(staleSourceInspection, "sourceSHA256");
        await File.AppendAllTextAsync(staleSource, "changed after inspection\n", new UTF8Encoding(false), cancellation);
        byte[] beforeStaleSourceCommit = await File.ReadAllBytesAsync(state.ProjectPath, cancellation);
        bool staleSourceRejected = await CommitRejectedAsync(client,
            CommitFromInspection(staleSourceValues, staleSourceInspection), cancellation);
        if (!File.ReadAllBytes(state.ProjectPath).AsSpan().SequenceEqual(beforeStaleSourceCommit) ||
            FileSha256(staleSource).Equals(staleSourceSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stale-source rejection did not preserve the Project or the changed fixture source.");
        await File.WriteAllTextAsync(staleSource, "Stale-source baseline\n", new UTF8Encoding(false), cancellation);
        if (!staleSourceRejected)
            throw new InvalidDataException("The stale source SHA was accepted by project.connection.commit.");

        // Keep one valid request based on the old Project revision for a stale-revision check.
        var staleProjectValues = Values(state, sessionID, "add", "file",
            name: "Stale project", path: staleSource, description: "must not publish");
        JsonElement staleProjectInspection = await InspectConnectionAsync(client, staleProjectValues, cancellation);

        var fileAddValues = Values(state, sessionID, "add", "file", name: "外部文件连接",
            path: Path.Combine(sourceDirectory, ".", "source-a.txt"), description: "中文文件连接");
        JsonElement fileAddInspection = await InspectConnectionAsync(client, fileAddValues, cancellation);
        if (!PathEquals(RequiredString(fileAddInspection, "canonicalPath"), Path.GetFullPath(sourceA)) ||
            !RequiredString(fileAddInspection, "sourceSHA256").Equals(FileSha256(sourceA), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File inspection did not return its canonical path and actual source SHA.");
        JsonElement fileAdded = await CommitConnectionAsync(client, fileAddValues, fileAddInspection, cancellation);
        state = StateFromReceipt(fileAdded, state);
        addCount++;
        await VerifyProjectAndCatalogAsync(client, state, "外部文件连接", true, cancellation);
        operations.Add(Evidence("add-file", fileAdded));

        bool staleProjectRejected = await CommitRejectedAsync(client,
            CommitFromInspection(staleProjectValues, staleProjectInspection), cancellation);
        if (!FileSha256(state.ProjectPath).Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A stale-project request changed the committed Project.");
        await VerifyProjectAndCatalogAsync(client, state, "Stale project", false, cancellation);
        if (!staleProjectRejected)
            throw new InvalidDataException("The stale Project revision was accepted by project.connection.commit.");

        var fileUpdateValues = Values(state, sessionID, "update", "file", connectionName: "外部文件连接",
            name: "外部文件连接更新", path: sourceB, description: "更新连接");
        JsonElement fileUpdateInspection = await InspectConnectionAsync(client, fileUpdateValues, cancellation);
        JsonElement fileUpdated = await CommitConnectionAsync(client, fileUpdateValues, fileUpdateInspection, cancellation);
        state = StateFromReceipt(fileUpdated, state);
        updateCount++;
        await VerifyProjectAndCatalogAsync(client, state, "外部文件连接更新", true, cancellation);
        operations.Add(Evidence("update-file", fileUpdated));

        var fileRemoveValues = Values(state, sessionID, "remove", "file", connectionName: "外部文件连接更新");
        JsonElement fileRemoveInspection = await InspectConnectionAsync(client, fileRemoveValues, cancellation);
        JsonElement fileRemoved = await CommitConnectionAsync(client, fileRemoveValues, fileRemoveInspection, cancellation);
        state = StateFromReceipt(fileRemoved, state);
        removeCount++;
        await VerifyProjectAndCatalogAsync(client, state, "外部文件连接更新", false, cancellation);
        operations.Add(Evidence("remove-file", fileRemoved));

        bool originalHdfUnchanged = originalHdf.All(pair => File.Exists(pair.Key) &&
            FileSha256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        bool copiedHdfUnchanged = copiedHdf.All(pair => File.Exists(pair.Key) &&
            FileSha256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        if (!originalHdfUnchanged || !copiedHdfUnchanged)
            throw new IOException("An original or copied Result fixture HDF changed during connection acceptance.");

        var manifest = new
        {
            schema = "hecras-project-connection-http-e2e/v1",
            sourceManifestPath,
            projectPath = state.ProjectPath,
            projectSessionID = sessionID,
            initialProjectRevision,
            finalProjectRevision = state.ProjectRevision,
            addCount,
            updateCount,
            removeCount,
            staleSourceRevisionRejected = staleSourceRejected,
            staleProjectRevisionRejected = staleProjectRejected,
            projectOpenReadback = true,
            catalogReadback = true,
            originalSourceHdfBytesUnchanged = originalHdfUnchanged,
            copiedSourceHdfBytesUnchanged = copiedHdfUnchanged,
            originalHdfSHA256 = originalHdf,
            copiedHdfSHA256 = copiedHdf,
            operations
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);
        return new("passed", manifestPath, state.ProjectPath, sessionID, initialProjectRevision,
            state.ProjectRevision, addCount, updateCount, removeCount, staleSourceRejected,
            staleProjectRejected, true, true, originalHdfUnchanged && copiedHdfUnchanged);
    }

    private static ConnectionInspectRequest Values(ProjectConnectionState state, Guid sessionID,
        string action, string kind, string? connectionName = null, string? name = null,
        string? path = null, string? description = null, string? resultName = null,
        string? searchPattern = null) =>
        new(action, kind, state.ProjectPath, state.ProjectXml, state.ProjectXmlSHA,
            state.ProjectRevision, sessionID, connectionName, name, path, description,
            resultName, searchPattern, Recurse: false);

    private static ProjectConnectionState StateFromOpen(JsonElement opened, Guid sessionID)
    {
        string xml = StringProperty(opened, "projectXml");
        return new(StringProperty(opened, "projectPath"), xml, StringProperty(opened, "revision"),
            Sha256(Encoding.UTF8.GetBytes(xml)), sessionID);
    }

    private static async Task<JsonElement> InspectConnectionAsync(HttpClient client,
        ConnectionInspectRequest request, CancellationToken cancellation)
    {
        JsonElement result = (await SubmitOperationAsync(client, "project.connection.inspect",
            request, cancellation)).Result;
        if (!BooleanProperty(result, "canCommit"))
            throw new InvalidDataException("project.connection.inspect rejected fixture operation: " +
                OptionalString(result, "validationMessage"));
        if (!StringProperty(result, "projectRevision").Equals(request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(result, "projectXmlSHA").Equals(request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(result, "projectSessionID").Equals(request.ProjectSessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(StringProperty(result, "sourceReferencesSHA256")))
            throw new InvalidDataException("project.connection.inspect returned a different Project or source identity.");
        return result;
    }

    private static ConnectionCommitRequest CommitFromInspection(ConnectionInspectRequest request,
        JsonElement inspection) => new(request.Action, request.Kind, request.ProjectPath,
        request.ProjectXml, request.ProjectXmlSHA, request.ProjectRevision, request.ProjectSessionID,
        request.ConnectionName, request.Name, OptionalString(inspection, "canonicalPath") ?? request.Path,
        request.Description, request.ResultName, OptionalString(inspection, "connectionSHA256"),
        OptionalString(inspection, "sourceSHA256"), StringProperty(inspection, "sourceReferencesSHA256"),
        OptionalString(inspection, "normalizedName"), request.SearchPattern, request.Recurse);

    private static async Task<JsonElement> CommitConnectionAsync(HttpClient client,
        ConnectionInspectRequest request, JsonElement inspection, CancellationToken cancellation)
    {
        ConnectionCommitRequest commit = CommitFromInspection(request, inspection);
        JsonElement receipt = (await SubmitOperationAsync(client, "project.connection.commit",
            commit, cancellation)).Result;
        ValidateConnectionReceipt(receipt, request, inspection);
        return receipt;
    }

    private static void ValidateConnectionReceipt(JsonElement receipt,
        ConnectionInspectRequest request, JsonElement inspection)
    {
        if (!BooleanProperty(receipt, "success") || StringProperty(receipt, "action") != request.Action ||
            StringProperty(receipt, "kind") != request.Kind ||
            !StringProperty(receipt, "previousProjectRevision").Equals(request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(receipt, "previousSourceReferencesSHA256").Equals(
                StringProperty(inspection, "sourceReferencesSHA256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The connection receipt does not match its inspected Project/source revision.");
        JsonElement project = Property(receipt, "project");
        string projectXml = StringProperty(project, "projectXml");
        string projectRevision = StringProperty(project, "revision");
        if (!BooleanProperty(project, "success") ||
            !StringProperty(project, "projectSessionID").Equals(request.ProjectSessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !PathEquals(StringProperty(project, "projectPath"), request.ProjectPath) ||
            !Sha256(Encoding.UTF8.GetBytes(projectXml)).Equals(
                StringProperty(project, "projectXmlSHA"), StringComparison.OrdinalIgnoreCase) ||
            !FileSha256(request.ProjectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(StringProperty(receipt, "sourceReferencesSHA256")))
            throw new InvalidDataException("The connection receipt does not confirm the exact persisted Project snapshot.");
        JsonElement capture = Property(receipt, "sourceCapture");
        if (BooleanProperty(capture, "exists") != BooleanProperty(inspection, "sourceExists") ||
            OptionalString(capture, "sha256") != OptionalString(inspection, "sourceSHA256") ||
            IntProperty(capture, "fileCount") != IntProperty(inspection, "sourceFileCount"))
            throw new InvalidDataException("The connection receipt source capture differs from inspection.");
        if (request.Action == "remove")
        {
            if (PropertyOrNull(receipt, "after").ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidDataException("A removed connection remained in the receipt.");
        }
        else
        {
            JsonElement after = Property(receipt, "after");
            string expectedName = request.Action == "add" ? StringProperty(inspection, "normalizedName") : request.Name!;
            if (StringProperty(after, "name") != expectedName ||
                StringProperty(after, "type") != (request.Kind == "directory" ? "Directory" : "File") ||
                !PathEquals(StringProperty(after, "path"), OptionalString(inspection, "canonicalPath")) ||
                OptionalString(after, "sourceSHA256") != OptionalString(inspection, "sourceSHA256"))
                throw new InvalidDataException("The connection receipt changed the inspected identity, path, or source.");
        }
    }

    private static ProjectConnectionState StateFromReceipt(JsonElement receipt,
        ProjectConnectionState previous)
    {
        JsonElement project = Property(receipt, "project");
        if (!StringProperty(receipt, "previousProjectRevision").Equals(
                previous.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The connection commit began from an outdated Project revision.");
        return new(StringProperty(project, "projectPath"), StringProperty(project, "projectXml"),
            StringProperty(project, "revision"), StringProperty(project, "projectXmlSHA"),
            previous.SessionID);
    }

    private static async Task VerifyProjectAndCatalogAsync(HttpClient client,
        ProjectConnectionState state, string name, bool expected, CancellationToken cancellation)
    {
        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = state.ProjectPath }, cancellation)).Result;
        if (!PathEquals(StringProperty(opened, "projectPath"), state.ProjectPath) ||
            !StringProperty(opened, "revision").Equals(state.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(StringProperty(opened, "projectXml"), state.ProjectXml, StringComparison.Ordinal))
            throw new InvalidDataException("project.open did not read back the exact connection transaction.");
        JsonElement tree = (await SubmitOperationAsync(client, "catalog.tree.load", new
        {
            projectPath = state.ProjectPath,
            projectXml = state.ProjectXml,
            mode = "Catalog",
            projectSessionID = state.SessionID
        }, cancellation)).Result;
        CatalogTreeNode[] roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        int matchCount = FlattenMapNodes(roots).Count(node => node.Name == name &&
            node.SourceClass is "ExternalFileConnectionCatalogNode" or "ExternalConnectionCatalogNode");
        if (matchCount != (expected ? 1 : 0))
            throw new InvalidDataException($"catalog.tree.load did not confirm expected connection presence for '{name}'.");
    }

    private static async Task<bool> CommitRejectedAsync(HttpClient client,
        ConnectionCommitRequest request, CancellationToken cancellation)
    {
        try
        {
            _ = await SubmitOperationAsync(client, "project.connection.commit", request, cancellation);
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static object Evidence(string operation, JsonElement receipt) => new
    {
        operation,
        action = StringProperty(receipt, "action"),
        kind = StringProperty(receipt, "kind"),
        previousProjectRevision = StringProperty(receipt, "previousProjectRevision"),
        projectRevision = StringProperty(Property(receipt, "project"), "revision"),
        sourceReferencesSHA256 = StringProperty(receipt, "sourceReferencesSHA256"),
        sourceCapture = Property(receipt, "sourceCapture").Clone(),
        before = PropertyOrNull(receipt, "before").Clone(),
        after = PropertyOrNull(receipt, "after").Clone()
    };

    private static string RequiredString(JsonElement value, string name) =>
        OptionalString(value, name) ?? throw new InvalidDataException($"JSON property '{name}' is null.");

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
