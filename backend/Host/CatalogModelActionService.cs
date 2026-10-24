using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Observables;

namespace Ras.Backend;

public sealed record CatalogModelActionRequest(
    string ProjectPath,
    string ProjectXml,
    string AssetPath,
    string Kind,
    string ExpectedAssetSHA,
    string Action,
    string? Name = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record CatalogModelActionDependency(string? AssetPath, string Kind, string Name,
    string? Revision, string Reason);
public sealed record CatalogModelActionTrashPath(string Path, string Revision, bool OriginalAsset);
public sealed record CatalogModelActionStagedTarget(string StagePath, string TargetPath, string StageSHA,
    string? ExpectedTargetSHA);
public sealed record CatalogModelActionStagingDirectory(string Path, string Token);
public sealed record CatalogModelActionMetadataMutation(string Section, string RowName,
    string IdentityAttribute, string IdentityValue, string Attribute, string? OldValue,
    string? NewValue, bool RemoveRow = false);
public sealed record CatalogModelActionInspection(string ProjectPath, string AssetPath,
    string CanonicalAssetPath, string Kind, string Name, string Revision,
    string[] AvailableActions, CatalogModelActionDependency[] DeleteDependencies,
    CatalogModelActionTrashPath[] TrashPaths);
public sealed record CatalogModelActionReceipt(string Action, string Kind,
    string OriginalAssetPath, string CanonicalOriginalAssetPath, string OriginalName, string OriginalSHA,
    string? NewName, string? NewAssetPath, bool ActualClone,
    CatalogModelActionStagedTarget[] StagedTargets,
    CatalogModelActionMetadataMutation[] MetadataMutations,
    CatalogModelActionDependency[] DeleteDependencies,
    CatalogModelActionStagingDirectory[] StagingDirectories,
    string ProjectXmlFingerprint,
    CatalogModelActionTrashPath[]? TrashPaths = null);
public sealed record CatalogModelActionCleanupRequest(string StagingDirectory, string Token);

/// <summary>
/// Source-backed catalog actions. The retained rename behavior copies the HDF5
/// file and then changes the in-memory source identity; publication and moving
/// the old file to macOS Trash are left to the workspace transaction. Delete is
/// validation only: this service never deletes or saves the project or assets.
/// </summary>
public static class CatalogModelActionService
{
    private static readonly HashSet<string> SupportedKinds = [
        "Geometry", "Plan", "Terrain", "BoundaryCondition", "Classification", "NValue"];

    public static CatalogModelActionInspection Inspect(CatalogModelActionRequest request)
    {
        using var context = new Context(request);
        CoreLayerBase layer = context.Layer;
        CatalogModelActionDependency[] dependencies = DeleteDependencies(context.Project, layer);
        CatalogModelActionTrashPath[] trashPaths = DeleteTrashPaths(context.Project, layer, request.AssetPath);
        string[] actions = layer switch
        {
            BoundaryCondition { IsResult: true } => ["clone", "duplicate", "delete"],
            _ => ["rename", "clone", "duplicate", "delete"]
        };
        return new(context.ProjectPath, request.AssetPath, context.AssetPath, request.Kind,
            layer.Name, context.Revision, actions, dependencies, trashPaths);
    }

    public static CatalogModelActionReceipt Perform(CatalogModelActionRequest request)
    {
        using var context = new Context(request);
        string action = request.Action.Trim().ToLowerInvariant();
        if (action is not ("rename" or "clone" or "duplicate" or "delete"))
            throw new NotSupportedException("Choose a supported catalog model action.");
        if (action == "rename" && context.Layer is BoundaryCondition { IsResult: true })
            throw new NotSupportedException("Result boundary conditions cannot be renamed.");

        string originalName = context.Layer.Name;
        string originalPath = request.AssetPath;
        string canonicalOriginal = context.AssetPath;
        string fingerprint = HashText(request.ProjectXml);
        CatalogModelActionDependency[] dependencies = action == "delete"
            ? DeleteDependencies(context.Project, context.Layer)
            : [];
        CatalogModelActionTrashPath[] trashPaths = action == "delete"
            ? DeleteTrashPaths(context.Project, context.Layer, request.AssetPath)
            : [];

        if (action == "delete")
        {
            context.VerifyAssetRevision();
            return new(action, request.Kind, originalPath, canonicalOriginal, originalName, context.Revision,
                null, null, false, [], DeleteMetadataMutations(context, context.Layer),
                dependencies, [], fingerprint, trashPaths);
        }

        if (action == "duplicate")
            return PerformSourceDuplicate(context, originalPath, canonicalOriginal, originalName, fingerprint);

        string target;
        bool clone = action == "clone";
        if (clone)
        {
            target = ClonePath(context.Layer);
        }
        else
        {
            string newName = request.Name?.Trim() ?? "";
            if (!CoreLayerBase.IsLegalName(newName, out string error)) throw new ArgumentException(error);
            if (string.Equals(newName, originalName, StringComparison.Ordinal))
                throw new ArgumentException("Enter a different model name.");
            target = Project.MakeFilename(context.Layer.GetType(), context.Project.ProjectDirectory,
                newName, Path.GetExtension(context.AssetPath));
            if (SameOnPlatform(target, context.AssetPath))
                throw new ArgumentException("The destination file is the selected model file.");
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException("A model file already exists with that name.");
        }
        EnsureProjectOwnedTarget(context, target);

        context.VerifyAssetRevision();
        string token = Guid.NewGuid().ToString("N");
        string stagingDirectory = MakeStagingDirectory(target, token);
        Directory.CreateDirectory(stagingDirectory);
        stagingDirectory = SourceFilePaths.Canonical(stagingDirectory);
        string marker = Path.Combine(stagingDirectory, ".catalog-action-owner");
        File.WriteAllText(marker, token, System.Text.Encoding.UTF8);
        List<CatalogModelActionStagedTarget> staged = [];
        bool completed = false;
        try
        {
            if (clone)
            {
                string stagePath = Path.Combine(stagingDirectory, "clone.h5");
                context.Layer.SaveAs(stagePath);
                staged.Add(Staged(stagePath, target, expectedTargetSHA: null));
            }
            else
            {
                string stagePath = Path.Combine(stagingDirectory, "renamed-model.h5");
                NValueLayer[] classificationDependents = context.Layer is ClassificationLayer sourceClassification
                    ? context.Project.NValues.Where(item =>
                        ReferenceEquals(item.AssociatedClassificationLayer, sourceClassification)).ToArray()
                    : [];
                File.Copy(context.AssetPath, stagePath, overwrite: false);
                context.Layer.OverrideName(request.Name!.Trim());
                staged.Add(Staged(stagePath, target, expectedTargetSHA: null));
                if (context.Layer is ClassificationLayer classification)
                    StageClassificationDependents(classificationDependents, classification,
                        request.Name!.Trim(), stagingDirectory, staged);
            }

            context.VerifyAssetRevision();
            VerifyStagedTargets(staged);
            CatalogModelActionMetadataMutation[] metadata = clone ? [] :
                RenameMetadataMutations(context, context.Layer, originalName, request.Name!.Trim());
            string finalName = clone ? Path.GetFileNameWithoutExtension(target) : request.Name!.Trim();
            completed = true;
            CatalogModelActionTrashPath[] renameTrash = clone ? [] :
                [new(Path.GetFullPath(originalPath), context.Revision, true)];
            return new(action, request.Kind, originalPath, canonicalOriginal, originalName, context.Revision,
                finalName, target, clone, staged.ToArray(), metadata, [],
                [new CatalogModelActionStagingDirectory(stagingDirectory, token)], fingerprint, renameTrash);
        }
        finally
        {
            if (!completed) TryRemoveOwnedStagingDirectory(stagingDirectory, token);
        }
    }

    /// <summary>
    /// Runs the retained ObservableCoreLayer duplicate action while redirecting
    /// its SaveAs write into this operation's token-owned staging directory.
    /// Geometry and Plan use their specialized source menu actions to copy their
    /// project associations into the receipt; other core layers retain the
    /// generic CoreLayerBehaviors duplicate semantics.
    /// </summary>
    private static CatalogModelActionReceipt PerformSourceDuplicate(Context context,
        string originalPath, string canonicalOriginal, string originalName, string fingerprint)
    {
        context.VerifyAssetRevision();
        string? stagingDirectory = null;
        string? stagePath = null;
        string targetPath = "";
        string token = Guid.NewGuid().ToString("N");
        bool stagingDirectoryCreated = false;
        bool stagingOwnerMarkerWritten = false;
        bool redirectInvoked = false;
        bool completed = false;
        try
        {
            bool duplicated = context.ObservableLayer.DuplicateBySaveAsH5(out string generatedPath, proposedTarget =>
            {
                redirectInvoked = true;
                targetPath = SourceFilePaths.Canonical(proposedTarget);
                if (SameOnPlatform(targetPath, context.AssetPath))
                    throw new IOException("The source duplicate action selected the original model as its destination.");
                EnsureProjectOwnedTarget(context, targetPath);
                EnsureDuplicateNameAvailable(context, Path.GetFileNameWithoutExtension(proposedTarget));
                if (HasFilesystemEntry(proposedTarget))
                    throw new IOException("A model file appeared at the destination during the action.");

                stagingDirectory = MakeStagingDirectory(targetPath, token);
                if (Directory.Exists(stagingDirectory) || File.Exists(stagingDirectory))
                    throw new IOException("A catalog action staging directory already exists.");
                Directory.CreateDirectory(stagingDirectory);
                stagingDirectoryCreated = true;
                stagingDirectory = SourceFilePaths.Canonical(stagingDirectory);
                string markerPath = Path.Combine(stagingDirectory, ".catalog-action-owner");
                byte[] markerContents = System.Text.Encoding.UTF8.GetBytes(token);
                using (var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    marker.Write(markerContents);
                stagingOwnerMarkerWritten = true;

                string selectedStagePath = Path.Combine(stagingDirectory, Path.GetFileName(proposedTarget));
                stagePath = selectedStagePath;
                return selectedStagePath;
            });
            if (!duplicated || !redirectInvoked || string.IsNullOrWhiteSpace(generatedPath) ||
                string.IsNullOrWhiteSpace(stagePath) || !File.Exists(stagePath))
                throw new IOException("The retained source duplicate action did not produce an HDF5 model.");
            if (!string.Equals(SourceFilePaths.Canonical(generatedPath), targetPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("The retained source duplicate action changed its selected destination name.");
            if (HasFilesystemEntry(generatedPath) || HasFilesystemEntry(targetPath))
                throw new IOException("A model file appeared at the destination during the action.");
            string ownedStagePath = stagePath ?? throw new IOException("The catalog staging path is unavailable.");
            string ownedStagingDirectory = stagingDirectory ??
                throw new IOException("The catalog staging directory is unavailable.");

            // The staged file keeps the source-selected basename, so loading it
            // through the retained Geometry/Plan project APIs produces the same
            // association rows as the original source duplicate action.
            CatalogModelActionMetadataMutation[] metadata = AddSourceDuplicateAssociations(context, ownedStagePath);
            context.VerifyAssetRevision();
            CatalogModelActionStagedTarget staged = Staged(ownedStagePath, targetPath, expectedTargetSHA: null);
            VerifyStagedTargets([staged]);

            completed = true;
            return new("duplicate", context.Layer switch
                {
                    Plan => "Plan",
                    Geometry => "Geometry",
                    Terrain => "Terrain",
                    BoundaryCondition => "BoundaryCondition",
                    ClassificationLayer => "Classification",
                    NValueLayer => "NValue",
                    _ => throw new NotSupportedException("This source model cannot be duplicated from its catalog context menu.")
                }, originalPath, canonicalOriginal, originalName, context.Revision,
                Path.GetFileNameWithoutExtension(targetPath), targetPath, true, [staged], metadata, [],
                [new CatalogModelActionStagingDirectory(ownedStagingDirectory, token)], fingerprint, []);
        }
        finally
        {
            if (!completed && stagingDirectory is not null)
            {
                if (stagingOwnerMarkerWritten) context.DeferStagingCleanup(stagingDirectory, token);
                else if (stagingDirectoryCreated && Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static void EnsureDuplicateNameAvailable(Context context, string duplicateName)
    {
        if (SourceLayers(context.Project, context.Kind).Any(layer =>
                string.Equals(layer.Name, duplicateName, StringComparison.Ordinal)))
            throw new ArgumentException("The source duplicate name is already used by a project model.");
    }

    private static CatalogModelActionMetadataMutation[] AddSourceDuplicateAssociations(
        Context context, string sourceDuplicatePath)
    {
        switch (context.Layer)
        {
            case Geometry:
            {
                ObservableGeometry source = (ObservableGeometry)context.ObservableLayer;
                ObservableGeometry duplicate = context.SourceProject.TryAddExistingGeometry(sourceDuplicatePath)
                    ?? throw new IOException("The source project could not open the duplicated geometry.");
                if (duplicate.HasLoadErrors)
                    throw new IOException(string.Join("\n", duplicate.LayerErrors.Select(error => error.EvaluatedMessage)));
                if (source.AssociatedTerrain is { } terrain) duplicate.AssociatedTerrain = terrain;
                if (source.AssociatedNValueLayer is { } nvalues) duplicate.AssociatedNValueLayer = nvalues;
                if (context.Project.Geometries.Count(item => item.Name == duplicate.Name) != 1)
                    throw new ArgumentException("The duplicated Geometry name is already used by another project Geometry.");

                GeometryAssociation association = context.Project.GetGeometryAssociation(duplicate.Geometry);
                if (association.IsDefault()) return [];
                return CreateAssociationRow(context.Metadata, "GeometryAssociations", "GeometryAssociation",
                    "Geometry", duplicate.Name,
                    ("Terrain", association.TerrainName), ("N_Values", association.NValueLayerName));
            }
            case Plan:
            {
                ObservablePlan source = (ObservablePlan)context.ObservableLayer;
                ObservablePlan duplicate = context.SourceProject.TryAddExistingPlan(sourceDuplicatePath)
                    ?? throw new IOException("The source project could not open the duplicated plan.");
                if (duplicate.HasLoadErrors)
                    throw new IOException(string.Join("\n", duplicate.LayerErrors.Select(error => error.EvaluatedMessage)));
                if (source.AssociatedGeometry is { } geometry) duplicate.AssociatedGeometry = geometry;
                if (source.AssociatedBoundaryCondition is { } boundary) duplicate.AssociatedBoundaryCondition = boundary;
                if (context.Project.Plans.Count(item => item.Name == duplicate.Name) != 1)
                    throw new ArgumentException("The duplicated Plan name is already used by another project Plan.");

                PlanAssociation association = context.Project.GetPlanAssociation(duplicate.Plan);
                if (association.IsDefault()) return [];
                return CreateAssociationRow(context.Metadata, "PlanAssociations", "PlanAssociation",
                    "Plan", duplicate.Name,
                    ("Geometry", association.GeometryName), ("BoundaryCondition", association.BoundaryConditionName));
            }
            default:
                return [];
        }
    }

    private static CatalogModelActionMetadataMutation[] CreateAssociationRow(XElement metadata,
        string section, string rowName, string identityAttribute, string identityValue,
        params (string Attribute, string Value)[] values)
    {
        if (FindRow(metadata, section, rowName, identityAttribute, identityValue) is not null)
            throw new ArgumentException("The project XML already contains an association row for the duplicated model name.");

        var mutations = new List<CatalogModelActionMetadataMutation>
        {
            new(section, rowName, identityAttribute, identityValue, identityAttribute, null, identityValue)
        };
        foreach ((string attribute, string value) in values)
        {
            if (rowName == "GeometryAssociation" && string.IsNullOrEmpty(value)) continue;
            mutations.Add(new(section, rowName, identityAttribute, identityValue, attribute, null, value));
        }
        return mutations.ToArray();
    }

    /// <summary>Removes only the single service-created staging directory named by its random token.</summary>
    public static bool Cleanup(CatalogModelActionCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = Path.GetFullPath(request.StagingDirectory);
        if (!Guid.TryParseExact(request.Token, "N", out _)) return false;
        return TryRemoveOwnedStagingDirectory(path, request.Token);
    }

    private static void StageClassificationDependents(IEnumerable<NValueLayer> dependents,
        ClassificationLayer classification, string newName, string stagingDirectory,
        ICollection<CatalogModelActionStagedTarget> staged)
    {
        foreach (NValueLayer layer in dependents)
        {
            string path = SourceFilePaths.Canonical(layer.Filename);
            string expected = Hash(path);
            // The retained ObservableClassificationLayer alerts each linked N-Value
            // classification handle after the file rename. Persist that same linked
            // identity in the dependent HDF5 before replacing its original file.
            layer.AssociatedClassificationLayer = classification;
            layer.ClassificationLayerName = newName;
            string stagePath = Path.Combine(stagingDirectory,
                "dependent-nvalue-" + staged.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".h5");
            layer.SaveAs(stagePath);
            staged.Add(Staged(stagePath, path, expected));
        }
    }

    private static CatalogModelActionMetadataMutation[] RenameMetadataMutations(Context context,
        CoreLayerBase layer, string oldName, string newName)
    {
        var result = new List<CatalogModelActionMetadataMutation>();
        XElement root = context.Metadata;
        switch (layer)
        {
            case Geometry geometry:
                foreach (Plan plan in context.Project.Plans)
                {
                    PlanAssociation association = context.Project.GetPlanAssociation(plan);
                    if (ReferenceEquals(association.Geometry, geometry))
                        AddAssociationChange(root, result, "PlanAssociations", "PlanAssociation", "Plan",
                            plan.Name, "Geometry", oldName, newName);
                }
                AddRowIdentityChange(root, result, "GeometryAssociations", "GeometryAssociation",
                    "Geometry", oldName, newName);
                break;
            case BoundaryCondition boundary:
                foreach (Plan plan in context.Project.Plans)
                {
                    PlanAssociation association = context.Project.GetPlanAssociation(plan);
                    if (ReferenceEquals(association.BoundaryCondition, boundary))
                        AddAssociationChange(root, result, "PlanAssociations", "PlanAssociation", "Plan",
                            plan.Name, "BoundaryCondition", oldName, newName);
                }
                break;
            case Terrain terrain:
                foreach (Geometry geometry in context.Project.Geometries)
                {
                    GeometryAssociation association = context.Project.GetGeometryAssociation(geometry);
                    if (ReferenceEquals(association.Terrain, terrain))
                        AddAssociationChange(root, result, "GeometryAssociations", "GeometryAssociation", "Geometry",
                            geometry.Name, "Terrain", oldName, newName);
                }
                XElement? defaultTerrain = root.Elements("DefaultTerrain").SingleOrDefault();
                if (defaultTerrain?.Value == oldName)
                    result.Add(new("Project", "DefaultTerrain", "", "", "#text", oldName, newName));
                break;
            case NValueLayer nvalue:
                foreach (Geometry geometry in context.Project.Geometries)
                {
                    GeometryAssociation association = context.Project.GetGeometryAssociation(geometry);
                    if (ReferenceEquals(association.NValueLayer, nvalue))
                        AddAssociationChange(root, result, "GeometryAssociations", "GeometryAssociation", "Geometry",
                            geometry.Name, "N_Values", oldName, newName);
                }
                break;
            case Plan:
                AddRowIdentityChange(root, result, "PlanAssociations", "PlanAssociation",
                    "Plan", oldName, newName);
                break;
            case ClassificationLayer:
                break;
            default:
                throw new NotSupportedException("This source model cannot be renamed from the catalog.");
        }
        return result.ToArray();
    }

    private static void AddRowIdentityChange(XElement root,
        ICollection<CatalogModelActionMetadataMutation> mutations, string section,
        string rowName, string identityAttribute, string oldName, string newName)
    {
        XElement? row = FindRow(root, section, rowName, identityAttribute, oldName);
        if (row is null) return;
        string? persisted = (string?)row.Attribute(identityAttribute);
        if (!string.Equals(persisted, oldName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        mutations.Add(new(section, rowName, identityAttribute, oldName,
            identityAttribute, oldName, newName));
    }

    private static CatalogModelActionMetadataMutation[] DeleteMetadataMutations(Context context, CoreLayerBase layer)
    {
        var result = new List<CatalogModelActionMetadataMutation>();
        XElement root = context.Metadata;
        switch (layer)
        {
            case Plan plan:
                AddRemoveOrPreserveUnknownRow(root, result, "PlanAssociations", "PlanAssociation", "Plan",
                    plan.Name, "Plan");
                break;
            case Geometry geometry:
                AddRemoveOrPreserveUnknownRow(root, result, "GeometryAssociations", "GeometryAssociation", "Geometry",
                    geometry.Name, "Geometry");
                break;
            case Terrain terrain:
                foreach (Geometry geometry in context.Project.Geometries)
                {
                    GeometryAssociation association = context.Project.GetGeometryAssociation(geometry);
                    if (ReferenceEquals(association.Terrain, terrain))
                        AddClearAssociation(root, result, "GeometryAssociations", "GeometryAssociation", "Geometry",
                            geometry.Name, "Terrain", terrain.Name, association.NValueLayerName);
                }
                foreach (Result item in context.Project.Results)
                {
                    ResultAssociation association = context.Project.GetResultAssociation(item);
                    if (ReferenceEquals(association.OverrideTerrain, terrain))
                        AddClearAssociation(root, result, "ResultAssociations", "ResultAssociation", "Result",
                            item.Name, "OverrideTerrain", terrain.Name, null);
                }
                break;
            case BoundaryCondition:
            case ClassificationLayer:
            case NValueLayer:
                break;
            default:
                throw new NotSupportedException("This source model cannot be deleted from the catalog.");
        }
        return result.ToArray();
    }

    private static void AddAssociationChange(XElement root, ICollection<CatalogModelActionMetadataMutation> mutations,
        string section, string rowName, string identityAttribute, string identityValue, string attribute,
        string oldName, string newName)
    {
        XElement? row = FindRow(root, section, rowName, identityAttribute, identityValue);
        if (row is null)
        {
            mutations.Add(new(section, rowName, identityAttribute, identityValue,
                identityAttribute, null, identityValue));
            mutations.Add(new(section, rowName, identityAttribute, identityValue, attribute, null, newName));
            return;
        }
        string? persisted = (string?)row.Attribute(attribute);
        if (!string.Equals(persisted, oldName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        mutations.Add(new(section, rowName, identityAttribute, identityValue, attribute, persisted, newName));
    }

    private static void AddClearAssociation(XElement root, ICollection<CatalogModelActionMetadataMutation> mutations,
        string section, string rowName, string identityAttribute, string identityValue,
        string attribute, string oldValue, string? remainingSourceAssociation)
    {
        XElement? row = FindRow(root, section, rowName, identityAttribute, identityValue);
        if (row is null) return;
        string? persisted = (string?)row.Attribute(attribute);
        if (!string.Equals(persisted, oldValue, StringComparison.Ordinal)) throw new SourceFileChangedException();
        bool hasUnknownContent = HasUnknownAssociationContent(row, rowName, identityAttribute);
        bool noOtherKnownReference = string.IsNullOrEmpty(remainingSourceAssociation);
        bool hasOtherKnownAttributes = row.Attributes().Any(item =>
            item.Name.LocalName != identityAttribute && item.Name.LocalName != attribute &&
            IsKnownAssociationAttribute(section, item.Name.LocalName));
        bool remove = noOtherKnownReference && !hasOtherKnownAttributes && !hasUnknownContent;
        mutations.Add(new(section, rowName, identityAttribute, identityValue, attribute,
            persisted, null, remove));
    }

    private static void AddRemoveOrPreserveUnknownRow(XElement root,
        ICollection<CatalogModelActionMetadataMutation> mutations, string section, string rowName,
        string identityAttribute, string identityValue, string removalAttribute)
    {
        XElement? row = FindRow(root, section, rowName, identityAttribute, identityValue);
        if (row is null) return;
        string? oldValue = (string?)row.Attribute(removalAttribute);
        if (HasUnknownAssociationContent(row, rowName, identityAttribute))
        {
            mutations.Add(new(section, rowName, identityAttribute, identityValue,
                identityAttribute, identityValue, ""));
        }
        else
        {
            mutations.Add(new(section, rowName, identityAttribute, identityValue,
                removalAttribute, oldValue, null, true));
        }
    }

    private static bool HasUnknownAssociationContent(XElement row, string rowName, string identityAttribute)
    {
        HashSet<string> known = rowName switch
        {
            "PlanAssociation" => [identityAttribute, "Geometry", "BoundaryCondition"],
            "GeometryAssociation" => [identityAttribute, "Terrain", "N_Values"],
            "ResultAssociation" => [identityAttribute, "OverrideTerrain"],
            _ => [identityAttribute]
        };
        return row.Attributes().Any(attribute => !known.Contains(attribute.Name.LocalName)) || row.Nodes().Any();
    }

    private static bool IsKnownAssociationAttribute(string section, string name) => section switch
    {
        "GeometryAssociations" => name is "Terrain" or "N_Values",
        "PlanAssociations" => name is "Geometry" or "BoundaryCondition",
        "ResultAssociations" => name == "OverrideTerrain",
        _ => false
    };

    private static XElement? FindRow(XElement root, string section, string rowName,
        string identityAttribute, string identityValue)
    {
        XElement[] groups = root.Elements(section).ToArray();
        if (groups.Length > 1) throw new ArgumentException("The project has ambiguous association groups.");
        if (groups.Length == 0) return null;
        XElement[] rows = groups[0].Elements(rowName)
            .Where(item => (string?)item.Attribute(identityAttribute) == identityValue).ToArray();
        if (rows.Length > 1) throw new ArgumentException("The project has ambiguous model association rows.");
        return rows.SingleOrDefault();
    }

    private static CatalogModelActionDependency[] DeleteDependencies(Project project, CoreLayerBase layer)
    {
        var items = new List<CatalogModelActionDependency>();
        void Add(CoreLayerBase dependency, string kind, string reason)
        {
            if (dependency.IsInMemory || string.IsNullOrWhiteSpace(dependency.Filename) || !File.Exists(dependency.Filename)) return;
            string path = SourceFilePaths.Canonical(dependency.Filename);
            if (items.Any(item => item.AssetPath is { } existing && SourceFilePaths.SameSourceFile(existing, path))) return;
            items.Add(new(path, kind, dependency.Name, Hash(path), reason));
        }
        switch (layer)
        {
            case Geometry geometry:
                foreach (Plan plan in project.Plans)
                    if (ReferenceEquals(project.GetPlanAssociation(plan).Geometry, geometry))
                        Add(plan, "Plan", "This plan references the geometry being deleted.");
                break;
            case BoundaryCondition condition:
                foreach (Plan plan in project.Plans)
                    if (ReferenceEquals(project.GetPlanAssociation(plan).BoundaryCondition, condition))
                        Add(plan, "Plan", "This plan references the boundary condition being deleted.");
                break;
            case Terrain terrain:
                if (project.DefaultTerrain is { } defaultTerrain && ReferenceEquals(defaultTerrain, terrain))
                    items.Add(new(null, "Project", "Default Terrain", null, "This is the project's default terrain."));
                foreach (Geometry geometry in project.Geometries)
                    if (ReferenceEquals(project.GetGeometryAssociation(geometry).Terrain, terrain))
                        Add(geometry, "Geometry", "This geometry uses the terrain being deleted.");
                foreach (Result result in project.Results)
                    if (ReferenceEquals(project.GetResultAssociation(result).OverrideTerrain, terrain))
                    {
                        string path = string.IsNullOrWhiteSpace(result.Filename) ? "" : SourceFilePaths.Canonical(result.Filename);
                        if (path.Length > 0 && File.Exists(path))
                            items.Add(new(path, "Result", result.Name, Hash(path), "This result overrides to the terrain being deleted."));
                    }
                break;
            case ClassificationLayer classification:
                foreach (NValueLayer nvalue in project.NValues)
                    if (ReferenceEquals(nvalue.AssociatedClassificationLayer, classification))
                        Add(nvalue, "NValue", "This N-value layer uses the classification being deleted.");
                break;
            case NValueLayer nvalue:
                foreach (Geometry geometry in project.Geometries)
                    if (ReferenceEquals(project.GetGeometryAssociation(geometry).NValueLayer, nvalue))
                        Add(geometry, "Geometry", "This geometry uses the N-value layer being deleted.");
                break;
        }
        foreach (string sharedPath in SharedRasterInputs(project, layer))
            items.Add(new(sharedPath, "Raster", Path.GetFileName(sharedPath), Hash(sharedPath),
                "This raster is also referenced by another project model and will be retained."));
        return items.ToArray();
    }

    private static CatalogModelActionTrashPath[] DeleteTrashPaths(Project project, CoreLayerBase layer, string originalEntryPath)
    {
        var paths = new List<CatalogModelActionTrashPath>
        {
            new(Path.GetFullPath(originalEntryPath), Hash(originalEntryPath), true)
        };
        IEnumerable<string> rasterPaths = layer switch
        {
            Terrain terrain => terrain.AllRasterInputs().Select(item => item.Filename),
            ClassificationLayer classification => classification.AllRasterInputs().Select(item => item.Filename),
            _ => Enumerable.Empty<string>()
        };
        foreach (string path in rasterPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            string canonical = SourceFilePaths.Canonical(path);
            if (paths.Any(item => SourceFilePaths.SameSourceFile(item.Path, canonical))) continue;
            if (IsRasterReferencedByAnotherModel(project, layer, canonical)) continue;
            paths.Add(new(canonical, Hash(canonical), false));
        }
        return paths.ToArray();
    }

    private static IEnumerable<string> SharedRasterInputs(Project project, CoreLayerBase owner)
    {
        IEnumerable<string> owned = RasterInputs(owner).Where(File.Exists).Select(SourceFilePaths.Canonical).Distinct(StringComparer.Ordinal);
        foreach (string path in owned)
            if (IsRasterReferencedByAnotherModel(project, owner, path)) yield return path;
    }

    private static bool IsRasterReferencedByAnotherModel(Project project, CoreLayerBase owner, string path) =>
        SourceLayers(project, "all").Where(layer => !ReferenceEquals(layer, owner))
            .SelectMany(RasterInputs)
            .Where(File.Exists)
            .Any(candidate => SourceFilePaths.SameSourceFile(candidate, path));

    private static IEnumerable<string> RasterInputs(CoreLayerBase layer) => layer switch
    {
        Terrain terrain => terrain.AllRasterInputs().Select(item => item.Filename),
        ClassificationLayer classification => classification.AllRasterInputs().Select(item => item.Filename),
        _ => Enumerable.Empty<string>()
    };

    private static string ClonePath(CoreLayerBase layer)
    {
        string directory = layer.SourceDirectory;
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Only saved project models can be cloned.");
        for (int index = 1; index < int.MaxValue; index++)
        {
            string path = Path.Combine(directory, $"{layer.Name} ({index}).h5");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
        throw new IOException("A free clone filename could not be allocated.");
    }

    private static void EnsureProjectOwnedTarget(Context context, string target)
    {
        string projectDirectory = Path.TrimEndingDirectorySeparator(
            SourceFilePaths.Canonical(context.Project.ProjectDirectory));
        string canonicalTarget = SourceFilePaths.Canonical(target);
        string relative = Path.GetRelativePath(projectDirectory, canonicalTarget);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison))
            throw new ArgumentException("The model destination is outside the current project directory.");
    }

    private static CatalogModelActionStagedTarget Staged(string stagePath, string targetPath,
        string? expectedTargetSHA)
    {
        return new(SourceFilePaths.Canonical(stagePath), SourceFilePaths.Canonical(targetPath),
            Hash(stagePath), expectedTargetSHA);
    }

    private static bool HasFilesystemEntry(string path) =>
        File.Exists(path) || Directory.Exists(path) ||
        new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;

    private static void VerifyStagedTargets(IEnumerable<CatalogModelActionStagedTarget> targets)
    {
        foreach (CatalogModelActionStagedTarget target in targets)
        {
            if (!File.Exists(target.StagePath) || Hash(target.StagePath) != target.StageSHA)
                throw new SourceFileChangedException();
            if (target.ExpectedTargetSHA is null)
            {
                if (HasFilesystemEntry(target.TargetPath))
                    throw new IOException("A model file appeared at the destination during the action.");
            }
            else if (!File.Exists(target.TargetPath) || Hash(target.TargetPath) != target.ExpectedTargetSHA)
                throw new SourceFileChangedException();
        }
    }

    private static string MakeStagingDirectory(string assetPath, string token) =>
        Path.Combine(Path.GetDirectoryName(assetPath)!, ".catalog-action-staging", token);

    private static bool TryRemoveOwnedStagingDirectory(string path, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return false;
        string fullPath = Path.GetFullPath(path);
        string expectedParent = Path.GetDirectoryName(fullPath)!;
        if (Path.GetFileName(fullPath) != token || Path.GetFileName(expectedParent) != ".catalog-action-staging") return false;
        string marker = Path.Combine(fullPath, ".catalog-action-owner");
        if (!Directory.Exists(fullPath) || !File.Exists(marker) || File.ReadAllText(marker).Trim() != token) return false;
        Directory.Delete(fullPath, recursive: true);
        if (Directory.Exists(expectedParent) && !Directory.EnumerateFileSystemEntries(expectedParent).Any())
            Directory.Delete(expectedParent);
        return true;
    }

    private static string Hash(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static bool SameOnPlatform(string left, string right) =>
        (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private sealed class Context : IDisposable
    {
        private readonly Project project;
        private ObservableProject? sourceProject;
        private CatalogModelActionStagingDirectory? deferredStagingCleanup;
        public Project Project => project;
        public CoreLayerBase Layer { get; }
        public string Kind { get; }
        public XElement Metadata { get; }
        public string ProjectPath { get; }
        public string AssetPath { get; }
        public string Revision { get; }
        public ObservableProject SourceProject => sourceProject ??= new ObservableProject(project,
            Utility.SynchronizationContext.None);
        public ObservableCoreLayer ObservableLayer => Layer switch
        {
            Geometry geometry => SourceProject.Geometries.Single(item => ReferenceEquals(item.Geometry, geometry)),
            Plan plan => SourceProject.Plans.Single(item => ReferenceEquals(item.Plan, plan)),
            Terrain terrain => SourceProject.Terrains.Single(item => ReferenceEquals(item.Terrain, terrain)),
            BoundaryCondition boundary => SourceProject.BoundaryConditions.Single(item => ReferenceEquals(item.BoundaryCondition, boundary)),
            ClassificationLayer classification => SourceProject.Classifications.Single(item => ReferenceEquals(item.Layer, classification)),
            NValueLayer nvalue => SourceProject.NValues.Single(item => ReferenceEquals(item.Layer, nvalue)),
            _ => throw new NotSupportedException("This source model has no observable duplicate action.")
        };

        public Context(CatalogModelActionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
                throw new ArgumentException("Open a saved project before managing catalog models.");
            if (string.IsNullOrWhiteSpace(request.ProjectXml))
                throw new ArgumentException("The current project XML context is unavailable.");
            if (string.IsNullOrWhiteSpace(request.AssetPath) || !Path.IsPathFullyQualified(request.AssetPath))
                throw new ArgumentException("Select a saved model file.");
            if (!SupportedKinds.Contains(request.Kind)) throw new NotSupportedException("This catalog model type is unsupported.");
            Kind = request.Kind;
            if (string.IsNullOrWhiteSpace(request.ExpectedAssetSHA)) throw new ArgumentException("Refresh the model before managing it.");

            ProjectPath = SourceFilePaths.Canonical(request.ProjectPath);
            AssetPath = SourceFilePaths.Canonical(request.AssetPath);
            if (!File.Exists(ProjectPath)) throw new FileNotFoundException("The project file was not found.", ProjectPath);
            if (!File.Exists(AssetPath)) throw new FileNotFoundException("The selected model file was not found.", AssetPath);
            string actualRevision = Hash(AssetPath);
            if (!string.Equals(request.ExpectedAssetSHA, actualRevision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            using var reader = XmlReader.Create(new StringReader(request.ProjectXml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 8 * 1024 * 1024
            });
            Metadata = XElement.Load(reader, LoadOptions.None);
            Revision = actualRevision;

            project = CoreLayerImportService.OpenProject(ProjectPath, request.ProjectXml,
                request.CoreLayerContext);
            try
            {
                CoreLayerBase[] matches = SourceLayers(project, request.Kind)
                    .Where(item => !item.IsInMemory && !string.IsNullOrWhiteSpace(item.Filename)
                        && SourceFilePaths.SameSourceFile(item.Filename, AssetPath)).ToArray();
                if (matches.Length != 1)
                    throw new ArgumentException("The selected file is not exactly one saved model of this type in the current project.");
                Layer = matches[0];
                if (Layer.HasLoadErrors)
                    throw new IOException(string.Join("\n", Layer.LayerErrors.Select(error => error.EvaluatedMessage)));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void VerifyAssetRevision()
        {
            if (!File.Exists(AssetPath) || Hash(AssetPath) != Revision) throw new SourceFileChangedException();
        }

        public void DeferStagingCleanup(string path, string token) =>
            deferredStagingCleanup = new CatalogModelActionStagingDirectory(path, token);

        public void Dispose()
        {
            foreach (CoreLayerBase layer in SourceLayers(project, "all")) layer.Dispose();
            foreach (Result result in project.Results) result.Dispose();
            if (deferredStagingCleanup is { } staging)
                TryRemoveOwnedStagingDirectory(staging.Path, staging.Token);
        }
    }

    private static IEnumerable<CoreLayerBase> SourceLayers(Project project, string kind) => kind switch
    {
        "Geometry" => project.Geometries,
        "Plan" => project.Plans,
        "Terrain" => project.Terrains,
        "BoundaryCondition" => project.BoundaryConditions,
        "Classification" => project.ClassificationLayers,
        "NValue" => project.NValues,
        "all" => project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries).Concat(project.Terrains)
            .Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers),
        _ => []
    };
}
