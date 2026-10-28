using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record TerrainSaveCommitFaultEvidence(string Point, string ErrorType,
    bool OriginalAndPayloadPreserved);
internal sealed record TerrainSaveCommitAcceptanceReport(string State, string ReportPath,
    string SavedRevision, bool ClosedStagingReadback, bool ExactPublishedBytes,
    bool CleanFinalAssetIdentity, bool SelectedSavedInputTables, bool ExactRebasedCoreContext,
    bool PostPublicationReadFailurePreservedCommit, bool CleanupDeferredWithExactToken,
    bool IndependentSavedProfilesReadback, bool SubsequentSaveSucceeded, bool SourceSymlinkPreserved,
    TerrainSaveCommitFaultEvidence[] Faults);

internal static partial class ServerAcceptance
{
    public static async Task<TerrainSaveCommitAcceptanceReport> RunTerrainSaveCommitAsync(string newOutputDirectory)
    {
        string root = ChannelV6CreateNewDirectory(newOutputDirectory);
        ChannelV6ImportFixture fixture = CreateChannelV6ImportFixture(Path.Combine(root, "source"));
        string physicalTerrainPath = fixture.TerrainPath;
        string sourceAlias = Path.Combine(Path.GetDirectoryName(physicalTerrainPath)!, "Terrain source alias.h5");
        File.CreateSymbolicLink(sourceAlias, Path.GetFileName(physicalTerrainPath));
        fixture = fixture with { TerrainPath = sourceAlias,
            InputID = ChannelV6InputID(sourceAlias, fixture.TerrainRevision) };
        string ownerDirectory = ChannelV6CreateNewDirectory(Path.Combine(root, "session-owner"));
        string ownerPath = Path.Combine(ownerDirectory, "Terrain save session.ras");
        Project owner = Project.CreateNew(ownerPath, UnitSystem.SI);
        try { owner.Save(); }
        finally { ChannelV6DisposeProject(owner); }
        fixture = fixture with { ProjectPath = ownerPath };
        string projectXml = File.ReadAllText(ownerPath);
        string session = Guid.NewGuid().ToString("N");
        string projectRevision = ChannelV6FileSHA(ownerPath);
        SessionHdfImportInspection inspection = CoreLayerImportService.InspectSessionHdf(new(
            ownerPath, session, projectRevision, projectXml,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectXml))),
            fixture.TerrainPath, fixture.TerrainRevision));
        SessionHdfReferenceCapture[] references = [inspection.Reference];
        var core = new CoreLayerRequestContext(session, inspection.ProjectPath, projectRevision, [],
            CoreLayerImportService.ContextIdentityToken(session, inspection.ProjectPath, projectRevision, [], references))
            { SessionHdfReferences = references };
        CoreLayerImportService.ValidateContext(core, ownerPath);
        ModelSaveRequest Model(ModelSnapshot snapshot, CoreLayerRequestContext? context = null) =>
            ChannelV6Model(snapshot, fixture with { InputID = snapshot.Terrain!.SelectedId ?? fixture.InputID }) with
            { CoreLayerContext = context ?? core, ProjectXml = projectXml };

        ModelSnapshot loaded = await ModelEditorAdapter.LoadAsync(new(fixture.TerrainPath, "Terrain", ownerPath,
            projectXml, CoreLayerContext: core));
        File.WriteAllText(Path.Combine(root, "source-context.json"), JsonSerializer.Serialize(new
        {
            inspection.Reference, loaded.Fields, physicalTerrainPath, sourceAlias
        }, JsonOptions));
        ModelSnapshot selected = await ModelEditorAdapter.TransformTerrainAsync(new(Model(loaded), "selectInput",
            ItemId: fixture.InputID));
        await using var controls = new BackendDuplexControlReader("terrain-save-commit", new StringReader(""));
        ChannelV6GeometryImportResponse import = await ModelEditorAdapter.ImportChannelV6GeometryDuplexAsync(new(
            Model(selected), fixture.InputID, fixture.SourcePath, fixture.SourceSHA256,
            ModelEditorAdapter.DraftIdentity(fixture.InputID, selected.Revision, selected.SourceState)), null, controls);
        ChannelV6Require(import.State == "completed" && import.Snapshot is not null && import.Cleanup is not null,
            "The real retained Channel conversion did not produce a draft and cleanup receipt.");
        ModelSnapshot draft = import.Snapshot!;
        ChannelV6GeometryImportCleanup cleanup = import.Cleanup!;
        string payloadPath = ChannelV6PayloadPath(fixture.TerrainPath, cleanup);
        string tokenDirectory = ChannelV6DraftDirectory(fixture.TerrainPath, cleanup.DraftToken);
        string payloadRevision = ChannelV6FileSHA(payloadPath);
        ModelRow firstFeature = ChannelV6Rows(draft, ChannelV6FeatureTable).First();
        draft = await ModelEditorAdapter.TransformTerrainAsync(new(Model(draft), "selectRows",
            TableId: ChannelV6FeatureTable, RowIDs: [firstFeature.Id]));
        ChannelV6Require(ChannelV6Rows(draft, ChannelV6ProfileTable).Length == 3,
            "The imported parent draft did not expose the selected source profile.");
        ModelSaveRequest save = Model(draft);
        var faults = new List<TerrainSaveCommitFaultEvidence>();
        void AssertUnpublished()
        {
            ChannelV6Require(ChannelV6FileSHA(fixture.TerrainPath) == fixture.TerrainRevision &&
                ChannelV6FileSHA(payloadPath) == payloadRevision && Directory.Exists(tokenDirectory) &&
                ChannelV6FileSHA(ownerPath) == projectRevision,
                "A rejected pre-publication save changed the source/project or consumed the import token.");
            ChannelV6Require(new FileInfo(sourceAlias).LinkTarget is not null &&
                SourceFilePaths.SameSourceFile(sourceAlias, physicalTerrainPath),
                "A rejected save replaced or retargeted the source symlink.");
            ChannelV6Require(!Directory.EnumerateFiles(Path.GetDirectoryName(fixture.TerrainPath)!, ".model-edit-*.pending").Any(),
                "A rejected save retained its staging file.");
        }
        async Task Reject(string point, TerrainSaveCommitTestHooks hooks, Action? restore = null)
        {
            Exception? failure = null;
            try { _ = await ModelEditorAdapter.SaveModelCoreAsync(save, terrainSaveTestHooks: hooks); }
            catch (Exception error) { failure = error; }
            finally { restore?.Invoke(); }
            ChannelV6Require(failure is not null, "The " + point + " fault published a Terrain save.");
            AssertUnpublished();
            faults.Add(new(point, failure!.GetType().Name, true));
        }

        // Exercise the actual fresh HDF loader, not just a synthetic throw.
        await Reject("closed-staging-unreadable", new(StagingClosed: path =>
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not an HDF file"))));
        await Reject("staging-mutated-after-receipt", new(ReceiptPrepared: (path, _) =>
            File.AppendAllText(path, "changed after receipt")));
        byte[] ownerBytes = File.ReadAllBytes(ownerPath);
        await Reject("project-mutated-after-receipt", new(ReceiptPrepared: (_, _) =>
            File.AppendAllText(ownerPath, "\n")), () => File.WriteAllBytes(ownerPath, ownerBytes));
        byte[] originalBytes = File.ReadAllBytes(fixture.TerrainPath);
        await Reject("source-mutated-after-receipt", new(ReceiptPrepared: (_, _) =>
            File.AppendAllText(fixture.TerrainPath, "source changed")),
            () => File.WriteAllBytes(fixture.TerrainPath, originalBytes));
        string alternateTerrainPath = Path.Combine(Path.GetDirectoryName(physicalTerrainPath)!, "Other terrain.h5");
        File.Copy(physicalTerrainPath, alternateTerrainPath);
        await Reject("source-alias-retargeted-after-receipt", new(ReceiptPrepared: (_, _) =>
        {
            File.Delete(sourceAlias);
            File.CreateSymbolicLink(sourceAlias, Path.GetFileName(alternateTerrainPath));
        }), () =>
        {
            File.Delete(sourceAlias);
            File.CreateSymbolicLink(sourceAlias, Path.GetFileName(physicalTerrainPath));
        });

        ModelSnapshot? verifiedReceipt = null;
        string? closedStagingRevision = null;
        bool publishedBytesMatched = false, failedReadback = false;
        string heldProject = ownerPath + ".unavailable";
        ModelSnapshot committed;
        try
        {
            committed = await ModelEditorAdapter.SaveModelCoreAsync(save, terrainSaveTestHooks: new(
                StagingClosed: path => closedStagingRevision = ChannelV6FileSHA(path),
                ReceiptPrepared: (path, receipt) =>
                {
                    ChannelV6Require(ChannelV6FileSHA(path) == receipt.Revision &&
                        receipt.Revision == closedStagingRevision, "The prepared receipt did not describe the closed saved bytes.");
                    verifiedReceipt = receipt;
                    File.WriteAllText(Path.Combine(root, "prepared-snapshot.json"), JsonSerializer.Serialize(receipt, JsonOptions));
                },
                Published: () =>
                {
                    publishedBytesMatched = verifiedReceipt is not null &&
                        ChannelV6FileSHA(fixture.TerrainPath) == verifiedReceipt.Revision;
                    ChannelV6Require(publishedBytesMatched && File.Exists(payloadPath),
                        "The import payload was released before the verified committed receipt existed.");
                    // Reproduce the old post-Move RebaseContext failure using
                    // a real missing project. The committed HDF stays intact.
                    File.Move(ownerPath, heldProject);
                    try
                    {
                        _ = CoreLayerImportService.RebaseContext(core, projectRevision,
                            fixture.TerrainPath, verifiedReceipt!.Revision);
                    }
                    catch (FileNotFoundException)
                    {
                        failedReadback = true;
                        throw;
                    }
                    throw new InvalidDataException("The injected unavailable-project readback did not fail.");
                }));
        }
        finally { if (File.Exists(heldProject)) File.Move(heldProject, ownerPath); }
        File.WriteAllText(Path.Combine(root, "committed-snapshot.json"), JsonSerializer.Serialize(committed, JsonOptions));
        ChannelV6Require(failedReadback && publishedBytesMatched && verifiedReceipt is not null &&
            committed.Revision == verifiedReceipt.Revision && committed.Revision == ChannelV6FileSHA(fixture.TerrainPath),
            "A post-publication read failure lost the verified committed result.");
        ChannelV6Require(new FileInfo(sourceAlias).LinkTarget is not null &&
            SourceFilePaths.SameSourceFile(sourceAlias, physicalTerrainPath) &&
            ChannelV6FileSHA(physicalTerrainPath) == committed.Revision &&
            ChannelV6FileSHA(alternateTerrainPath) == fixture.TerrainRevision,
            "The committed save replaced its symlink or published to another canonical source.");
        ChannelV6Require(committed.Terrain?.ImportCleanupToken == cleanup.DraftToken &&
            !string.IsNullOrWhiteSpace(committed.Terrain.ImportCleanupWarning) &&
            ChannelV6FileSHA(payloadPath) == payloadRevision,
            "Post-publication cleanup failure lost its exact recoverable token.");
        using (JsonDocument state = JsonDocument.Parse(committed.SourceState!))
        {
            JsonElement value = state.RootElement;
            ChannelV6Require(value.GetProperty("asset").GetString() == Path.GetFullPath(fixture.TerrainPath) &&
                value.GetProperty("revision").GetString() == committed.Revision &&
                value.GetProperty("importDraftToken").ValueKind == JsonValueKind.Null &&
                value.GetProperty("pendingImports").GetArrayLength() == 0 &&
                value.GetProperty("pendingInputs").GetArrayLength() == 0 &&
                value.GetProperty("topologyDrafts").ValueKind == JsonValueKind.Null &&
                !committed.SourceState!.Contains(cleanup.PayloadId, StringComparison.Ordinal) &&
                !committed.SourceState.Contains(".model-edit-", StringComparison.Ordinal),
                "The committed state retained temporary paths, import payloads or undo mutations.");
        }
        string savedInput = ChannelV6InputID(fixture.TerrainPath, committed.Revision);
        ChannelV6Require(committed.Terrain!.SelectedId == savedInput &&
            committed.Fields[ChannelV6SelectedInputField] == savedInput,
            "The committed snapshot did not select the final saved input identity.");
        ChannelV6Require(ChannelV6Rows(committed, ChannelV6FeatureTable).Length == fixture.Profiles.Length,
            "The committed snapshot did not project the saved feature table.");
        ChannelV6Require(committed.Fields["TerrainProperties/Terrain_Name"] ==
            loaded.Fields["TerrainProperties/Terrain_Name"] &&
            committed.Fields["TerrainCatalogSummary/Context_Filename"] ==
            loaded.Fields["TerrainCatalogSummary/Context_Filename"],
            "The committed name/path differs from the owning source loader.");
        ChannelV6Require(!JsonSerializer.Serialize(committed, JsonOptions).Contains(".model-edit-", StringComparison.Ordinal),
            "The committed snapshot exposed its private staging path or name.");
        CoreLayerRequestContext expectedCore = CoreLayerImportService.RebaseContext(core, projectRevision,
            fixture.TerrainPath, committed.Revision)!;
        ChannelV6Require(JsonSerializer.Serialize(committed.CoreLayerContext, JsonOptions) ==
            JsonSerializer.Serialize(expectedCore, JsonOptions), "The staged Core rebase differs from actual committed HDF rebase.");
        CoreLayerImportService.ValidateContext(committed.CoreLayerContext!, ownerPath);
        using (Terrain reopened = new(fixture.TerrainPath))
            ChannelV6ProfilesEqual(ChannelV6Profiles(reopened.Layers.OfType<ChannelModTerrainInput>().Single()),
                fixture.Profiles, "Independent committed HDF");
        ChannelV6GeometryImportDiscardResponse discarded = ModelEditorAdapter.DiscardChannelV6GeometryImport(new(
            Model(committed, committed.CoreLayerContext), cleanup));
        ChannelV6Require(discarded.Discarded && discarded.DraftDirectoryRemoved,
            "The committed clean state could not release its deferred exact import token.");
        // Round-trip the clean receipt through normal parent Save to prove it
        // no longer depends on the consumed imported payload or stale Core.
        ModelSnapshot resaved = await ModelEditorAdapter.SaveAsync(Model(committed, committed.CoreLayerContext));
        ChannelV6Require(resaved.Revision == ChannelV6FileSHA(fixture.TerrainPath) &&
            resaved.Terrain?.ImportDraftToken is null && resaved.Terrain?.ImportCleanupWarning is null,
            "The clean committed receipt was not usable by the next ordinary save.");
        string reportPath = Path.Combine(root, "terrain-save-commit-report.json");
        var report = new TerrainSaveCommitAcceptanceReport("passed", reportPath, committed.Revision,
            true, true, true, true, true, true, true, true, true, true, faults.ToArray());
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }
}
