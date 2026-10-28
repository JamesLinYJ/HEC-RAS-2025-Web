using Ras.Layers;

namespace Ras.Backend;

// Per-call friend-assembly checks only. Public requests cannot supply hooks.
internal sealed record TerrainSaveCommitTestHooks(
    Action<string>? StagingClosed = null,
    Action<string, ModelSnapshot>? ReceiptPrepared = null,
    Action? Published = null);

public static partial class ModelEditorAdapter
{
    private static async Task<ModelSnapshot> PrepareTerrainCommittedSnapshotAsync(
        ModelSaveRequest request, string staging, int? selectedIndex)
    {
        string revision = Revision(staging);
        ModelSnapshot snapshot;
        // A fresh loader reads only the closed saved HDF. The editor uses the
        // final asset path so input IDs and opaque state never retain staging.
        // Terrain inherits NameMustMatchFilename: ModelContext also restores
        // the owning source's filename-derived name, exactly as a final-path
        // load does (including its captured session alias), never the random
        // staging basename or an untrusted draft display value.
        using (var context = new ModelContext(request with { SourceState = null }, staging))
        {
            var editor = TerrainInputs(context, request.AssetPath, revision, null)
                ?? throw new InvalidDataException("The saved HDF is not a Terrain.");
            if (selectedIndex is { } index)
            {
                TerrainInputData[] inputs = editor.Snapshot().Inputs;
                if (index < 0 || index >= inputs.Length)
                    throw new InvalidDataException("The saved Terrain lost its selected input.");
                editor.CaptureSelectedInput(new() { [TerrainEditor.SourceField] = inputs[index].Id });
            }
            snapshot = await Snapshot(context, revision, true, null, terrain: editor);
            // The public filename is the same original layer/alias chosen by
            // the owning project loader. All data came from staging, but that
            // private physical filename is never a saved asset identity.
            snapshot.Fields["TerrainCatalogSummary/Context_Filename"] = context.SourceFilename;
            snapshot = snapshot with { CoreLayerContext = PrepareTerrainCommittedCoreContext(
                request, (Terrain)context.Layer, revision) };
        }
        // Closing the reader is part of verification. A changed staging file
        // cannot inherit the receipt even when a caller injects a test fault.
        if (Revision(staging) != revision) throw new SourceFileChangedException();
        return snapshot;
    }

    private static CoreLayerRequestContext? PrepareTerrainCommittedCoreContext(
        ModelSaveRequest request, Terrain savedTerrain, string savedRevision)
    {
        if (request.CoreLayerContext is not { } captured) return null;
        CoreLayerImportService.ValidateContext(captured, request.ProjectPath!);
        string canonicalAsset = SourceFilePaths.Canonical(request.AssetPath);
        bool changed = false;
        SessionHdfReferenceCapture[] references = captured.SessionHdfReferences.Select(reference =>
        {
            if (!SourceFilePaths.SameSourceFile(reference.CanonicalSourcePath, canonicalAsset)) return reference;
            if (changed || reference.Kind != "terrain" ||
                !SourceFilePaths.SameSourceFile(reference.SourcePath, canonicalAsset) ||
                !string.Equals(reference.SourceRevision, request.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            changed = true;
            // The original capability was just validated. Its revision comes
            // from the staged HDF; its name follows the retained Terrain
            // loader's filename rule under this same owning source context.
            return reference with
            {
                SourceRevision = savedRevision,
                Name = savedTerrain.Name,
                ResultPlanName = null,
                ReferenceID = CoreLayerImportService.SessionHdfReferenceID(captured.ProjectSessionID,
                    captured.ProjectPath, "terrain", canonicalAsset, savedRevision)
            };
        }).ToArray();
        CoreLayerRequestContext rebased = captured with
        {
            SessionHdfReferences = references,
            IdentityToken = CoreLayerImportService.ContextIdentityToken(captured.ProjectSessionID,
                captured.ProjectPath, captured.ProjectRevision, captured.References, references)
        };
        // This is a prospective capability: the new source hash becomes live
        // only at File.Move. All other captures retain their verified identity.
        CoreLayerImportService.ValidateContextShape(rebased, request.ProjectPath!);
        return rebased;
    }

    private static ModelSnapshot WithTerrainSaveCleanupWarning(ModelSnapshot snapshot,
        string? token, string? warning) => warning is null ? snapshot : snapshot with
        {
            Terrain = snapshot.Terrain! with { ImportCleanupWarning = warning, ImportCleanupToken = token }
        };
}
