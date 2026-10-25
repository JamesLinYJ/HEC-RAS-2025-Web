using System.Text.Json;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record ProjectionExportFixtureManifest(string Schema, string ProjectPath, string LocalProjectPath,
    int EpsgCode, string DestinationPath);

internal static partial class ServerAcceptance
{
    public static ProjectionExportFixtureManifest CreateProjectionExportFixture(string requestedDirectory)
    {
        string root = Path.GetFullPath(requestedDirectory);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Projection fixture requires a new directory.");
        Directory.CreateDirectory(root);
        RasRuntimeBootstrap.Configure();
        string projectedPath = Path.Combine(root, "projected", "Projected Acceptance.ras");
        string localPath = Path.Combine(root, "local", "Local Acceptance.ras");
        Directory.CreateDirectory(Path.GetDirectoryName(projectedPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        Project projected = Project.CreateNew(projectedPath, ProjectionDefinition.FromEPSG(32610));
        try { projected.Save(); }
        finally { StableDisposeProject(projected); }
        Project local = Project.CreateNew(localPath, UnitSystem.USCustomary);
        try { local.Save(); }
        finally { StableDisposeProject(local); }
        var manifest = new ProjectionExportFixtureManifest("hecras-projection-export-fixture/v1", projectedPath, localPath,
            32610, Path.Combine(root, "exports", "Projection.prj"));
        File.WriteAllText(Path.Combine(root, "fixture.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return manifest;
    }

    public static async Task<object> RunProjectProjectionExportAsync(Uri baseAddress, string requestedDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Projection export acceptance requires a loopback HTTP(S) server.");
        string root = Path.GetFullPath(requestedDirectory);
        ProjectionExportFixtureManifest fixture = CreateProjectionExportFixture(root);
        Dictionary<string, string> sourceHashes = StableTreeHashes(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        CancellationToken ct = timeout.Token;
        JsonElement opened = (await SubmitOperationAsync(client, "project.open", new { projectPath = fixture.ProjectPath }, ct)).Result;
        string projectXml = StringProperty(opened, "projectXml");
        string revision = StringProperty(opened, "revision");
        async Task<ProjectProjectionExportReceipt> Export(string xml, string destination, string? expected = null,
            string? source = null) =>
            (await SubmitOperationAsync(client, "project.projection.export", new ProjectProjectionExportRequest(
                source ?? fixture.ProjectPath, xml, expected ?? revision, destination), ct)).Result
                .Deserialize<ProjectProjectionExportReceipt>(JsonOptions) ?? throw new InvalidDataException("Export receipt missing.");

        Directory.CreateDirectory(Path.GetDirectoryName(fixture.DestinationPath)!);
        await File.WriteAllTextAsync(fixture.DestinationPath, "An existing user file must never be overwritten.\n", ct);
        string originalDestinationSHA = FileSha256(fixture.DestinationPath);
        var receipts = new List<ProjectProjectionExportReceipt>();
        for (int index = 1; index <= 2; index++)
        {
            ProjectProjectionExportReceipt receipt = await Export(projectXml, fixture.DestinationPath);
            string expectedPath = Path.Combine(Path.GetDirectoryName(fixture.DestinationPath)!, $"Projection ({index}).prj");
            StableRequire(PathEquals(receipt.OutputPath, expectedPath) && receipt.ProjectRevision == revision &&
                PathEquals(receipt.ProjectPath, fixture.ProjectPath), "Projection export no-clobber receipt differs from persisted output.");
            VerifyProjectionExport(receipt.OutputPath, fixture.EpsgCode);
            receipts.Add(receipt);
        }
        StableRequire(FileSha256(fixture.DestinationPath) == originalDestinationSHA, "Projection export overwrote existing destination.");
        ProjectProjectionExportReceipt extension = await Export(projectXml, Path.Combine(root, "exports", "WithoutExtension"));
        StableRequire(extension.OutputPath.EndsWith("WithoutExtension.prj", StringComparison.Ordinal), "Missing .prj extension was not appended.");
        VerifyProjectionExport(extension.OutputPath, fixture.EpsgCode);
        receipts.Add(extension);

        // The action exports the visible project metadata, including an unsaved projection selection.
        XElement draft = XElement.Parse(projectXml);
        XElement projection = draft.Element("Projection") ?? throw new InvalidDataException("Fixture has no projection XML.");
        ProjectionDefinition.FromEPSG(32611).Serialize(projection);
        ProjectProjectionExportReceipt draftReceipt = await Export(draft.ToString(), Path.Combine(root, "exports", "Draft.prj"));
        VerifyProjectionExport(draftReceipt.OutputPath, 32611);
        receipts.Add(draftReceipt);

        string[] filesBeforeReject = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray();
        await StableExpectFailureAsync(() => Export(projectXml, Path.Combine(root, "exports", "Stale.prj"), new string('0', 64)));
        await StableExpectFailureAsync(() => Export(projectXml, Path.Combine(root, "exports", "Invalid.txt")));
        await StableExpectFailureAsync(() => Export(projectXml, "relative.prj"));
        JsonElement local = (await SubmitOperationAsync(client, "project.open", new { projectPath = fixture.LocalProjectPath }, ct)).Result;
        await StableExpectFailureAsync(() => Export(StringProperty(local, "projectXml"), Path.Combine(root, "exports", "Local.prj"),
            StringProperty(local, "revision"), fixture.LocalProjectPath));
        StableRequire(filesBeforeReject.SequenceEqual(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order()),
            "Rejected export created a file.");
        StableRequire(sourceHashes.All(item => FileSha256(item.Key) == item.Value), "Export changed a source project or fixture file.");
        StableRequire(!Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "Projection export leaked temporary files.");
        var report = new { state = "passed", fixture, receipts, originalDestinationSHA,
            originalSourceFilesUnchanged = true, destinationNotOverwritten = true, gdalProjectionReadback = true,
            draftProjectionReadback = true, staleRevisionRejected = true, localProjectionRejected = true,
            invalidDestinationRejected = true, noRejectedOutputsOrPartialFiles = true, sourceHashes };
        await File.WriteAllTextAsync(Path.Combine(root, "projection-export-report.json"), JsonSerializer.Serialize(report, JsonOptions), ct);
        return report;
    }

    private static void VerifyProjectionExport(string path, int epsg)
    {
        StableRequire(File.Exists(path) && new FileInfo(path).Length > 0, "Projection output is empty.");
        var actual = new ESRIProjection(path);
        var expected = new EPSGProjection(epsg);
        StableRequire(actual.IsProjected() && actual.IsEqual(expected) && actual.TryParseUnitSystem(out UnitSystem units) &&
            units == UnitSystem.SI && !string.IsNullOrWhiteSpace(actual.GetWKT()),
            "GDAL failed to read exported ESRI projection as EPSG:" + epsg);
    }
}
