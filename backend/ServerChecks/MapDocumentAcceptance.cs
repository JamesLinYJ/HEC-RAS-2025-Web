using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal sealed record MapDocumentAcceptanceReport(
    string State, string WorkspaceId, string ProjectPath, string VisualizationRevision,
    string[] MapNames, bool MissingVisualizationStayedMissing,
    bool ListingPreservedVisualizationBytes, bool StaleProjectRejected,
    bool StaleVisualizationMutationRejected);

internal static partial class ServerAcceptance
{
    public static async Task<MapDocumentAcceptanceReport> RunMapDocumentsAsync(Uri baseAddress, FixtureManifest fixture)
    {
        if (!baseAddress.IsLoopback || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Map acceptance only connects to a loopback HTTP(S) URL.", nameof(baseAddress));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        JsonElement upload = await UploadFixtureAsync(client, fixture, cancellation);
        string workspaceId = StringProperty(upload, "workspaceId");
        string projectPath = StringProperty(Property(upload, "projects").EnumerateArray().Single(), "path");
        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string xmlSHA = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectXml)));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (File.Exists(visualizationPath)) throw new InvalidDataException("Map fixture unexpectedly has an existing .vis file.");

        object ListRequest(string revision) => new { projectPath, projectXml, projectXmlSHA = xmlSHA, expectedProjectRevision = revision };
        object CreateRequest(string name, string? revision) => new
        {
            projectPath, projectXml, projectXmlSHA = xmlSHA,
            expectedVisualizationRevision = revision, kind = "Project", name,
        };
        OperationReceipt empty = await SubmitOperationAsync(client, "map.document.list", ListRequest(projectRevision), cancellation);
        if (Property(empty.Result, "maps").GetArrayLength() != 0 ||
            OptionalString(empty.Result, "visualizationRevision") is not null || File.Exists(visualizationPath))
            throw new InvalidDataException("Listing an empty project created or invented a map visualization.");

        OperationReceipt first = await SubmitOperationAsync(client, "map.document.create", CreateRequest("中文工程地图", null), cancellation);
        string firstRevision = StringProperty(first.Result, "visualizationRevision");
        OperationReceipt second = await SubmitOperationAsync(client, "map.document.create", CreateRequest("第二张地图", firstRevision), cancellation);
        string finalRevision = StringProperty(second.Result, "visualizationRevision");
        byte[] beforeListing = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        OperationReceipt listed = await SubmitOperationAsync(client, "map.document.list", ListRequest(projectRevision), cancellation);
        JsonElement[] maps = Property(listed.Result, "maps").EnumerateArray().ToArray();
        string[] names = maps.Select(map => StringProperty(map, "mapName")).ToArray();
        if (!names.SequenceEqual(new[] { "中文工程地图", "第二张地图" }) ||
            maps.Where((map, index) => IntProperty(map, "mapIndex") != index || StringProperty(map, "mapType") != "Project").Any() ||
            IntProperty(listed.Result, "activeMapIndex") != 1 ||
            StringProperty(listed.Result, "visualizationRevision") != finalRevision ||
            StringProperty(listed.Result, "projectRevision") != projectRevision)
            throw new InvalidDataException("Saved map discovery lost map order, names, active identity or revisions.");

        foreach (JsonElement map in maps)
        {
            _ = await SubmitOperationAsync(client, "map.tree.load", new
            {
                projectPath, projectXml, projectXmlSHA = xmlSHA, projectSessionID = "map-acceptance",
                mapID = "map-" + IntProperty(map, "mapIndex"),
                mapIndex = IntProperty(map, "mapIndex"), mapName = StringProperty(map, "mapName"),
                mapType = StringProperty(map, "mapType"), expectedVisualizationRevision = finalRevision,
            }, cancellation);
        }
        byte[] afterListing = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        if (!beforeListing.SequenceEqual(afterListing))
            throw new InvalidDataException("Read-only discovery or tree loading modified the visualization file.");

        await ExpectMapSourceChangedAsync(client, "map.document.list", ListRequest(new string('0', 64)), cancellation);
        await ExpectMapSourceChangedAsync(client, "map.document.create", CreateRequest("不能用旧修订创建", firstRevision), cancellation);
        byte[] afterRejectedRequests = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        if (!beforeListing.SequenceEqual(afterRejectedRequests))
            throw new InvalidDataException("A rejected stale request changed the visualization.");
        _ = await SubmitOperationAsync(client, "backend.info", new { }, cancellation);
        return new("completed", workspaceId, projectPath, finalRevision, names,
            MissingVisualizationStayedMissing: true, ListingPreservedVisualizationBytes: true,
            StaleProjectRejected: true, StaleVisualizationMutationRejected: true);
    }

    private static async Task ExpectMapSourceChangedAsync(HttpClient client, string method, object parameters, CancellationToken cancellation)
    {
        using HttpResponseMessage accepted = await client.PostAsJsonAsync("api/operations", new { method, parameters }, JsonOptions, cancellation);
        if (accepted.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidDataException("The stale-source check was not accepted as a worker task.");
        using JsonDocument submission = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(cancellation));
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument poll = await GetJsonAsync(client, $"api/operations/{id}?after={cursor}", cancellation);
            JsonElement root = poll.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                if (state != "failed" || StringProperty(Property(root, "error"), "code") != "sourceChanged")
                    throw new InvalidDataException("The stale map source was not rejected with sourceChanged: " + root);
                return;
            }
            await Task.Delay(50, cancellation);
        }
    }
}
