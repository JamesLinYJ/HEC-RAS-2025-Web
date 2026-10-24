using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Files;

namespace Ras.Backend;

public sealed record ProjectAssociationInspectRequest(
    string TargetKind,
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string TargetAssetPath,
    string TargetAssetRevision,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectAssociationCommitRequest(
    string TargetKind,
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string TargetAssetPath,
    string TargetAssetRevision,
    string ExpectedTargetSHA256,
    string ExpectedAssociationSHA256,
    string ExpectedOptionsSHA256,
    string ExpectedSourceReferencesSHA256,
    string? GeometryPath,
    string? GeometrySHA256,
    string? BoundaryConditionPath,
    string? BoundaryConditionSHA256,
    string? TerrainPath,
    string? TerrainSHA256,
    bool ClearTerrain,
    string? NValuePath,
    string? NValueSHA256,
    bool ClearNValue,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectAssociationOption(
    string Name,
    string? Path,
    string? Revision,
    [property: JsonPropertyName("sha256")] string? SHA256,
    bool Exists);

public sealed record ProjectAssociationState(
    ProjectAssociationOption? Geometry = null,
    ProjectAssociationOption? BoundaryCondition = null,
    ProjectAssociationOption? Terrain = null,
    ProjectAssociationOption? NValue = null);

public sealed record ProjectAssociationOptions(
    ProjectAssociationOption[] Geometry,
    ProjectAssociationOption[] BoundaryCondition,
    ProjectAssociationOption[] Terrain,
    ProjectAssociationOption[] NValue);

public sealed record ProjectAssociationDependency(
    string Kind,
    string Name,
    string Path,
    string CanonicalPath,
    string? Revision,
    [property: JsonPropertyName("sha256")] string? SHA256,
    bool Exists);

public sealed record ProjectAssociationTarget(
    string Kind,
    string Name,
    string Path,
    string Revision,
    [property: JsonPropertyName("sha256")] string SHA256);

public sealed record ProjectAssociationSourceCapture(
    ProjectAssociationDependency[] Dependencies,
    string SourceReferencesSHA256);

public sealed record ProjectAssociationInspection(
    bool CanCommit,
    string? ValidationMessage,
    string TargetKind,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    Guid ProjectSessionID,
    ProjectAssociationTarget Target,
    ProjectAssociationState Before,
    string AssociationSHA256,
    string OptionsSHA256,
    ProjectAssociationOptions Options,
    ProjectAssociationDependency[] Dependencies,
    string SourceReferencesSHA256);

public sealed record ProjectAssociationProjectSnapshot(
    bool Success,
    string ProjectPath,
    string ProjectXml,
    string Revision,
    string ProjectXmlSHA,
    Guid ProjectSessionID,
    CoreLayerRequestContext? CoreLayerContext);

public sealed record ProjectAssociationCommitReceipt(
    bool Success,
    string TargetKind,
    ProjectAssociationProjectSnapshot Project,
    string PreviousProjectRevision,
    ProjectAssociationTarget Target,
    ProjectAssociationState Before,
    ProjectAssociationState After,
    ProjectAssociationSourceCapture SourceCapture,
    string PreviousSourceReferencesSHA256,
    string SourceReferencesSHA256);

/// <summary>
/// Changes only the standard association rows in a saved .ras project. This
/// service deliberately never calls Project.Save or writes a model HDF.
/// </summary>
public static class ProjectAssociationService
{
    private const int MaximumProjectXmlBytes = 8 * 1024 * 1024;

    private sealed record ProjectSnapshot(string Path, string Xml, byte[] Bytes,
        string Revision, string XmlSHA);

    private sealed record ModelOption(string Kind, CoreLayerBase Layer,
        ProjectAssociationOption Public);

    private sealed class ProjectState : IDisposable
    {
        private readonly Project _project;
        public ProjectSnapshot Snapshot { get; }
        public XDocument Document { get; }
        public XElement Root { get; }
        public Project Project => _project;
        public CoreLayerRequestContext? CoreLayerContext { get; }

        public ProjectState(ProjectSnapshot snapshot, XDocument document, XElement root,
            Project project, CoreLayerRequestContext? coreLayerContext)
        {
            Snapshot = snapshot;
            Document = document;
            Root = root;
            _project = project;
            CoreLayerContext = coreLayerContext;
        }

        public void Dispose() => DisposeProject(_project);
    }

    public static ProjectAssociationInspection Inspect(ProjectAssociationInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInspectRequest(request);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(request.ProjectPath);
        using ProjectState state = OpenState(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ProjectRevision, request.ProjectSessionID,
            request.CoreLayerContext);
        return InspectCore(request, state);
    }

    public static ProjectAssociationCommitReceipt Commit(ProjectAssociationCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectAssociationInspectRequest inspectRequest = ToInspectRequest(request);
        ValidateInspectRequest(inspectRequest);
        ValidateCommitRequest(request);

        string projectPath = Path.GetFullPath(request.ProjectPath);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(projectPath);
        using ProjectState state = OpenState(projectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID, request.CoreLayerContext);

        ProjectAssociationInspection inspection = InspectCore(inspectRequest, state);
        if (!inspection.CanCommit ||
            !SameSHA(inspection.Target.SHA256, request.ExpectedTargetSHA256) ||
            !SameSHA(inspection.AssociationSHA256, request.ExpectedAssociationSHA256) ||
            !SameSHA(inspection.OptionsSHA256, request.ExpectedOptionsSHA256) ||
            !SameSHA(inspection.SourceReferencesSHA256, request.ExpectedSourceReferencesSHA256))
            throw new SourceFileChangedException();

        ModelOption? geometry = request.TargetKind == "plan"
            ? ResolveSelection(state.Project, "geometry", request.GeometryPath,
                request.GeometrySHA256, inspection.Options.Geometry)
            : null;
        ModelOption? boundary = request.TargetKind == "plan"
            ? ResolveSelection(state.Project, "boundaryCondition", request.BoundaryConditionPath,
                request.BoundaryConditionSHA256, inspection.Options.BoundaryCondition)
            : null;
        ModelOption? terrain = request.TargetKind == "geometry"
            ? ResolveSelection(state.Project, "terrain", request.TerrainPath,
                request.TerrainSHA256, inspection.Options.Terrain, request.ClearTerrain)
            : null;
        ModelOption? nValue = request.TargetKind == "geometry"
            ? ResolveSelection(state.Project, "nValue", request.NValuePath,
                request.NValueSHA256, inspection.Options.NValue, request.ClearNValue)
            : null;

        if (request.TargetKind == "plan" && (geometry is null || boundary is null))
            throw new ArgumentException("A Plan must have both a Geometry and a Boundary Condition.");
        if (request.TargetKind == "geometry" && (geometry is not null || boundary is not null))
            throw new ArgumentException("Geometry association requests cannot include Plan choices.");
        if (request.TargetKind == "plan" &&
            (request.ClearTerrain || request.ClearNValue || request.TerrainPath is not null || request.TerrainSHA256 is not null ||
             request.NValuePath is not null || request.NValueSHA256 is not null))
            throw new ArgumentException("Plan association requests cannot include Geometry choices.");
        if (request.TargetKind == "geometry" &&
            (request.GeometryPath is not null || request.GeometrySHA256 is not null ||
             request.BoundaryConditionPath is not null || request.BoundaryConditionSHA256 is not null))
            throw new ArgumentException("Geometry association requests cannot include Plan choices.");

        ProjectAssociationState before = inspection.Before;
        EnsureSourceCaptureUnchanged(inspection.Dependencies, inspection.SourceReferencesSHA256);
        string committedXml = ApplyAssociationChange(state, request, geometry, boundary, terrain, nValue);
        byte[] committedBytes = new UTF8Encoding(false, true).GetBytes(committedXml);
        string committedRevision = HashBytes(committedBytes);
        string committedXmlSHA = HashText(committedXml);
        if (!SameSHA(committedXmlSHA, committedRevision))
            throw new InvalidDataException("The committed .ras XML and byte revision differ.");

        string? stagedPath = null;
        bool projectPublished = false;
        try
        {
            EnsureSnapshotUnchanged(state.Snapshot);
            EnsureSourceCaptureUnchanged(inspection.Dependencies, inspection.SourceReferencesSHA256);
            stagedPath = WriteProjectTemp(state.Snapshot.Path, committedBytes);
            EnsureSnapshotUnchanged(state.Snapshot);
            EnsureSourceCaptureUnchanged(inspection.Dependencies, inspection.SourceReferencesSHA256);
            File.Move(stagedPath, state.Snapshot.Path, overwrite: true);
            stagedPath = null;
            projectPublished = true;

            EnsureCommittedSnapshot(state.Snapshot.Path, committedXml, committedRevision, committedXmlSHA);
            ProjectWorkspaceResponse opened = ProjectWorkspaceService.Open(
                new ProjectWorkspaceOpenRequest(state.Snapshot.Path));
            if (!opened.Success || !SamePath(opened.ProjectPath, state.Snapshot.Path) ||
                !SameSHA(opened.Revision, committedRevision) ||
                !string.Equals(opened.ProjectXml, committedXml, StringComparison.Ordinal))
                throw new IOException("The saved Project could not be reopened with the committed revision.");

            CoreLayerRequestContext? committedContext = CoreLayerImportService.RebaseContext(
                request.CoreLayerContext, committedRevision, inspection.Target.Path, inspection.Target.SHA256);
            XElement readbackRoot = ParseProjectRoot(opened.ProjectXml);
            Project readbackProject = CoreLayerImportService.OpenProjectWithMetadata(
                opened.ProjectPath, readbackRoot, committedContext);
            ProjectAssociationState after;
            ProjectAssociationSourceCapture afterCapture;
            try
            {
                CoreLayerBase readbackTarget = FindTarget(readbackProject, request.TargetKind,
                    request.TargetAssetPath);
                VerifyTargetHash(readbackTarget, inspection.Target.SHA256);
                after = ReadAssociationState(readbackProject, request.TargetKind, readbackTarget);
                VerifyAfter(request.TargetKind, after, geometry, boundary, terrain, nValue);
                string readbackAssociationSHA = ComputeAssociationSHA(readbackRoot, request.TargetKind,
                    readbackTarget.Name);
                string expectedAfterSHA = ComputeAssociationSHA(ParseProjectRoot(committedXml),
                    request.TargetKind, readbackTarget.Name);
                if (!SameSHA(readbackAssociationSHA, expectedAfterSHA))
                    throw new InvalidDataException("The reopened Project association XML did not match the committed association row.");
                afterCapture = CaptureSourceDependencies(readbackProject, request.TargetKind, readbackTarget);
                if (!SameSHA(afterCapture.SourceReferencesSHA256, inspection.SourceReferencesSHA256))
                    throw new SourceFileChangedException();
                EnsureCommittedSnapshot(state.Snapshot.Path, committedXml, committedRevision, committedXmlSHA);
            }
            finally { DisposeProject(readbackProject); }

            CatalogTreeSnapshot catalog = CatalogTreeService.LoadProject(new CatalogTreeRequest(
                opened.ProjectPath, opened.ProjectXml, Mode: "Project",
                ProjectSessionID: request.ProjectSessionID.ToString("D"), CoreLayerContext: committedContext));
            VerifyCatalogTarget(catalog, request.TargetKind, request.TargetAssetPath, inspection.Target.Name);
            EnsureCommittedSnapshot(state.Snapshot.Path, committedXml, committedRevision, committedXmlSHA);

            var projectReceipt = new ProjectAssociationProjectSnapshot(true, opened.ProjectPath,
                opened.ProjectXml, opened.Revision, committedXmlSHA, request.ProjectSessionID, committedContext);
            var targetReceipt = inspection.Target with { Revision = inspection.Target.SHA256 };
            return new(true, request.TargetKind, projectReceipt, state.Snapshot.Revision,
                targetReceipt, before, after, afterCapture, inspection.SourceReferencesSHA256,
                afterCapture.SourceReferencesSHA256);
        }
        catch (Exception error)
        {
            if (projectPublished)
            {
                string rollback = RollbackProject(state.Snapshot, committedRevision, committedBytes);
                if (rollback.Length > 0)
                    throw new IOException("The association transaction failed after publishing XML. " + rollback, error);
            }
            throw;
        }
        finally
        {
            if (stagedPath is not null) TryDeleteFile(stagedPath);
        }
    }

    private static ProjectAssociationInspection InspectCore(ProjectAssociationInspectRequest request,
        ProjectState state)
    {
        if (request.TargetKind is not ("plan" or "geometry"))
            throw new ArgumentException("Choose a Project Plan or Geometry. Results and session-only Result assets are read-only.");
        CoreLayerBase target = FindTarget(state.Project, request.TargetKind, request.TargetAssetPath);
        ProjectAssociationTarget targetIdentity = BuildTarget(request.TargetKind, target);
        if (!SameSHA(targetIdentity.SHA256, request.TargetAssetRevision))
            throw new SourceFileChangedException();

        ProjectAssociationState before = ReadAssociationState(state.Project, request.TargetKind, target);
        string associationSHA = ComputeAssociationSHA(state.Root, request.TargetKind, target.Name);
        ProjectAssociationOption[] geometries = request.TargetKind == "plan"
            ? BuildOptions("geometry", state.Project.Geometries.Cast<CoreLayerBase>()) : [];
        ProjectAssociationOption[] boundaries = request.TargetKind == "plan"
            ? BuildOptions("boundaryCondition", state.Project.BoundaryConditions.Cast<CoreLayerBase>()) : [];
        ProjectAssociationOption[] terrains = request.TargetKind == "geometry"
            ? BuildOptions("terrain", state.Project.Terrains.Cast<CoreLayerBase>()) : [];
        ProjectAssociationOption[] nValues = request.TargetKind == "geometry"
            ? BuildOptions("nValue", state.Project.NValues.Cast<CoreLayerBase>()) : [];

        var options = new ProjectAssociationOptions(geometries, boundaries, terrains, nValues);
        string optionsSHA = HashText(JsonSerializer.Serialize(options, BackendJson.Options));
        ProjectAssociationSourceCapture capture = CaptureSourceDependencies(state.Project,
            request.TargetKind, target);
        EnsureSnapshotUnchanged(state.Snapshot);
        EnsureSourceCaptureUnchanged(capture.Dependencies, capture.SourceReferencesSHA256);
        bool canCommit = request.TargetKind != "plan" || (geometries.Length > 0 && boundaries.Length > 0);
        string? validation = canCommit ? null : "当前工程没有可用的几何或边界条件文件，不能修改计划关联。";
        return new(canCommit, validation, request.TargetKind, state.Snapshot.Path, state.Snapshot.Revision,
            state.Snapshot.XmlSHA, request.ProjectSessionID, targetIdentity, before, associationSHA,
            optionsSHA, options, capture.Dependencies, capture.SourceReferencesSHA256);
    }

    private static ProjectState OpenState(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision, Guid sessionID,
        CoreLayerRequestContext? coreLayerContext)
    {
        ProjectSnapshot snapshot = ReadProjectSnapshot(projectPath, projectXml, projectXmlSHA, projectRevision, sessionID);
        if (coreLayerContext is not null)
        {
            if (!string.Equals(coreLayerContext.ProjectSessionID, sessionID.ToString("D"), StringComparison.Ordinal) ||
                !SamePath(coreLayerContext.ProjectPath, snapshot.Path) ||
                !SameSHA(coreLayerContext.ProjectRevision, snapshot.Revision))
                throw new SourceFileChangedException();
            CoreLayerImportService.ValidateContext(coreLayerContext, snapshot.Path);
        }

        XDocument document = XDocument.Parse(snapshot.Xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
        if (root.Name.LocalName != Project.Keys.Project)
            throw new InvalidDataException("The project XML root is not Project.");
        Project project = CoreLayerImportService.OpenProjectWithMetadata(snapshot.Path, root, coreLayerContext);
        try
        {
            EnsureSnapshotUnchanged(snapshot);
            return new(snapshot, document, root, project, coreLayerContext);
        }
        catch
        {
            DisposeProject(project);
            throw;
        }
    }

    private static ProjectSnapshot ReadProjectSnapshot(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision, Guid sessionID)
    {
        string canonicalPath = Path.GetFullPath(projectPath);
        if (!Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(canonicalPath).Equals(Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved .ras project before editing associations.");
        if (sessionID == Guid.Empty || projectXml is null ||
            Encoding.UTF8.GetByteCount(projectXml) > MaximumProjectXmlBytes ||
            !IsSHA(projectXmlSHA) || !IsSHA(projectRevision) ||
            !SameSHA(HashText(projectXml), projectXmlSHA))
            throw new SourceFileChangedException();

        (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
        if (beforePath is null || beforeRevision is null) throw new FileNotFoundException("The Project file was not found.", canonicalPath);
        byte[] bytes = File.ReadAllBytes(canonicalPath);
        string xml = File.ReadAllText(canonicalPath);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
        if (afterPath is null || afterRevision is null || !SamePath(beforePath, afterPath) ||
            !SameSHA(beforeRevision, afterRevision) || !SamePath(beforePath, canonicalPath) ||
            !SameSHA(beforeRevision, projectRevision) || !string.Equals(xml, projectXml, StringComparison.Ordinal) ||
            !SameSHA(HashText(xml), projectXmlSHA))
            throw new SourceFileChangedException();
        return new(beforePath, xml, bytes, beforeRevision, HashText(xml));
    }

    private static void ValidateInspectRequest(ProjectAssociationInspectRequest request)
    {
        if (request.TargetKind is not ("plan" or "geometry"))
            throw new ArgumentException("Only Project-owned Plan and Geometry associations can be edited.");
        if (string.IsNullOrWhiteSpace(request.TargetAssetPath) || !Path.IsPathFullyQualified(request.TargetAssetPath) ||
            !IsSHA(request.TargetAssetRevision))
            throw new ArgumentException("Choose an exact saved Plan or Geometry asset and inspect its revision.");
    }

    private static void ValidateCommitRequest(ProjectAssociationCommitRequest request)
    {
        if (!IsSHA(request.ExpectedTargetSHA256) || !IsSHA(request.ExpectedAssociationSHA256) ||
            !IsSHA(request.ExpectedOptionsSHA256) || !IsSHA(request.ExpectedSourceReferencesSHA256))
            throw new ArgumentException("Inspect the current Project, association and choices before committing.");
        if (request.TargetKind == "plan")
        {
            if (request.ClearTerrain || request.ClearNValue)
                throw new ArgumentException("Plan associations cannot clear Geometry dependencies.");
            if (string.IsNullOrWhiteSpace(request.GeometryPath) || !IsSHA(request.GeometrySHA256) ||
                string.IsNullOrWhiteSpace(request.BoundaryConditionPath) || !IsSHA(request.BoundaryConditionSHA256))
                throw new ArgumentException("A Plan requires an exact Geometry and Boundary Condition choice.");
        }
        else
        {
            ValidateClearableChoice(request.TerrainPath, request.TerrainSHA256, request.ClearTerrain, "Terrain");
            ValidateClearableChoice(request.NValuePath, request.NValueSHA256, request.ClearNValue, "N-Value");
        }
    }

    private static void ValidateClearableChoice(string? path, string? sha, bool clear, string label)
    {
        if (clear)
        {
            if (path is not null || sha is not null)
                throw new ArgumentException($"Clear {label} must not include a selected path or SHA.");
        }
        else if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !IsSHA(sha))
            throw new ArgumentException($"Select an exact {label} dependency or explicitly clear it.");
    }

    private static ProjectAssociationInspectRequest ToInspectRequest(ProjectAssociationCommitRequest request) =>
        new(request.TargetKind, request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID, request.TargetAssetPath,
            request.TargetAssetRevision, request.CoreLayerContext);

    private static CoreLayerBase FindTarget(Project project, string kind, string path)
    {
        CoreLayerBase[] matches = kind switch
        {
            "plan" => project.Plans.Cast<CoreLayerBase>().Where(item => SamePath(item.Filename, path)).ToArray(),
            "geometry" => project.Geometries.Cast<CoreLayerBase>().Where(item => SamePath(item.Filename, path)).ToArray(),
            _ => []
        };
        if (matches.Length == 1) return matches[0];
        if (project.Results.Any(item => SamePath(item.Filename, path)))
            throw new ArgumentException("Result and embedded Plan assets are read-only; choose a saved Project Plan or Geometry.");
        throw new ArgumentException("The selected asset is not one unique Project-owned Plan or Geometry. Session-only assets are not writable here.");
    }

    private static ProjectAssociationTarget BuildTarget(string kind, CoreLayerBase layer)
    {
        string path = Path.GetFullPath(layer.Filename);
        string sha = HashFile(path);
        return new(kind, layer.Name, path, sha, sha);
    }

    private static ProjectAssociationOption[] BuildOptions(string kind, IEnumerable<CoreLayerBase> layers)
    {
        CoreLayerBase[] source = layers.ToArray();
        HashSet<string> ambiguousNames = source.GroupBy(layer => layer.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        return source.Select(layer => BuildOption(kind, layer)).Where(option => option.Exists && option.Path is not null &&
                option.Revision is not null && option.SHA256 is not null && !ambiguousNames.Contains(option.Name))
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.Path, StringComparer.Ordinal).ToArray();
    }

    private static ProjectAssociationOption BuildOption(string kind, CoreLayerBase layer)
    {
        string path = Path.GetFullPath(layer.Filename);
        bool exists = File.Exists(path);
        string? sha = exists ? HashFile(path) : null;
        return new(layer.Name, path, sha, sha, exists);
    }

    private static ProjectAssociationState ReadAssociationState(Project project, string kind, CoreLayerBase target)
    {
        if (kind == "plan")
        {
            Plan plan = (Plan)target;
            PlanAssociation association = project.GetPlanAssociation(plan);
            return new(
                Geometry: ToCurrentOption("geometry", association.GeometryName, association.Geometry),
                BoundaryCondition: ToCurrentOption("boundaryCondition", association.BoundaryConditionName, association.BoundaryCondition));
        }
        Geometry geometry = (Geometry)target;
        GeometryAssociation geometryAssociation = project.GetGeometryAssociation(geometry);
        return new(
            Terrain: ToCurrentOption("terrain", geometryAssociation.TerrainName, geometryAssociation.Terrain),
            NValue: ToCurrentOption("nValue", geometryAssociation.NValueLayerName, geometryAssociation.NValueLayer));
    }

    private static ProjectAssociationOption? ToCurrentOption(string kind, string? name, CoreLayerBase? layer)
    {
        _ = kind;
        if (layer is null || string.IsNullOrWhiteSpace(name)) return null;
        ProjectAssociationOption option = BuildOption(kind, layer);
        return option.Exists && option.Path is not null && option.Revision is not null && option.SHA256 is not null
            ? option : null;
    }

    private static string ComputeAssociationSHA(XElement root, string kind, string targetName)
    {
        string sectionName = kind == "plan" ? Project.Keys.PlanAssociationGroup : Project.Keys.GeometryAssociationGroup;
        string rowName = kind == "plan" ? Project.Keys.PlanAssociation : Project.Keys.GeometryAssociation;
        string key = kind == "plan" ? Project.Keys.PlanAssociation_Plan : Project.Keys.GeometryAssociation_Geom;
        XElement[] sections = root.Elements().Where(element => element.Name.LocalName == sectionName).ToArray();
        if (sections.Length > 1) throw new InvalidDataException($"The project XML has duplicate {sectionName} sections.");
        XElement[] rows = sections.SingleOrDefault()?.Elements().Where(element =>
            element.Name.LocalName == rowName &&
            string.Equals((string?)element.Attribute(key), targetName, StringComparison.Ordinal)).ToArray() ?? [];
        if (rows.Length > 1) throw new InvalidDataException($"The project has duplicate {rowName} rows for '{targetName}'.");
        string value = rows.Length == 0 ? $"absent:{kind}:{targetName}" : rows[0].ToString(SaveOptions.DisableFormatting);
        return HashText(value);
    }

    private static ProjectAssociationSourceCapture CaptureSourceDependencies(Project project,
        string targetKind, CoreLayerBase target)
    {
        var layers = new List<(string Kind, CoreLayerBase Layer)> { (targetKind, target) };
        if (targetKind == "plan")
        {
            layers.AddRange(project.Geometries.Select(item => ("geometry", (CoreLayerBase)item)));
            layers.AddRange(project.BoundaryConditions.Select(item => ("boundaryCondition", (CoreLayerBase)item)));
            PlanAssociation association = project.GetPlanAssociation((Plan)target);
            if (association.Geometry is not null) layers.Add(("geometry", association.Geometry));
            if (association.BoundaryCondition is not null) layers.Add(("boundaryCondition", association.BoundaryCondition));
        }
        else
        {
            layers.AddRange(project.Terrains.Select(item => ("terrain", (CoreLayerBase)item)));
            layers.AddRange(project.NValues.Select(item => ("nValue", (CoreLayerBase)item)));
            GeometryAssociation association = project.GetGeometryAssociation((Geometry)target);
            if (association.Terrain is not null) layers.Add(("terrain", association.Terrain));
            if (association.NValueLayer is not null) layers.Add(("nValue", association.NValueLayer));
        }

        ProjectAssociationDependency[] dependencies = layers
            .Select(item => BuildDependency(item.Kind, item.Layer))
            .GroupBy(item => (CanonicalPath(item.Path), item.Kind, item.Name), new DependencyKeyComparer())
            .Select(group => group.OrderBy(item => item.Path, StringComparer.Ordinal).First())
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => CanonicalPath(item.Path), StringComparer.Ordinal)
            .ToArray();
        string sha = HashText(JsonSerializer.Serialize(dependencies, BackendJson.Options));
        return new(dependencies, sha);
    }

    private static ProjectAssociationDependency BuildDependency(string kind, CoreLayerBase layer)
    {
        string path = Path.GetFullPath(layer.Filename);
        bool exists = File.Exists(path);
        string? sha = exists ? HashFile(path) : null;
        return new(kind, layer.Name, path, CanonicalPath(path), sha, sha, exists);
    }

    private sealed class DependencyKeyComparer : IEqualityComparer<(string Path, string Kind, string Name)>
    {
        public bool Equals((string Path, string Kind, string Name) left,
            (string Path, string Kind, string Name) right) =>
            string.Equals(left.Path, right.Path, PathComparison) && left.Kind == right.Kind && left.Name == right.Name;
        public int GetHashCode((string Path, string Kind, string Name) value) =>
            HashCode.Combine(PathComparer.GetHashCode(value.Path), StringComparer.Ordinal.GetHashCode(value.Kind),
                StringComparer.Ordinal.GetHashCode(value.Name));
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void EnsureSourceCaptureUnchanged(ProjectAssociationDependency[] dependencies,
        string expectedSHA)
    {
        foreach (ProjectAssociationDependency dependency in dependencies)
        {
            if (!SamePath(CanonicalPath(dependency.Path), dependency.CanonicalPath))
                throw new SourceFileChangedException();
            bool exists = File.Exists(dependency.Path);
            string? revision = exists ? HashFile(dependency.Path) : null;
            if (exists != dependency.Exists || !SameOptionalSHA(revision, dependency.Revision) ||
                !SameOptionalSHA(revision, dependency.SHA256))
                throw new SourceFileChangedException();
        }
        // A checksum over the frozen dependency list binds the exact source set;
        // per-file hashes above recheck each member without recomputing paths.
        string frozenSHA = HashText(JsonSerializer.Serialize(dependencies, BackendJson.Options));
        if (!SameSHA(frozenSHA, expectedSHA)) throw new SourceFileChangedException();
    }

    private static string ApplyAssociationChange(ProjectState state,
        ProjectAssociationCommitRequest request, ModelOption? geometry, ModelOption? boundary,
        ModelOption? terrain, ModelOption? nValue)
    {
        XDocument document = new(state.Document);
        XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
        bool isPlan = request.TargetKind == "plan";
        string sectionName = isPlan ? Project.Keys.PlanAssociationGroup : Project.Keys.GeometryAssociationGroup;
        string rowName = isPlan ? Project.Keys.PlanAssociation : Project.Keys.GeometryAssociation;
        string targetKey = isPlan ? Project.Keys.PlanAssociation_Plan : Project.Keys.GeometryAssociation_Geom;
        string targetName = FindTarget(state.Project, request.TargetKind, request.TargetAssetPath).Name;
        XName sectionXName = root.Name.Namespace + sectionName;
        XElement[] sections = root.Elements(sectionXName).ToArray();
        if (sections.Length > 1) throw new InvalidDataException($"The project XML has duplicate {sectionName} sections.");
        XElement? section = sections.SingleOrDefault();
        if (section is null)
        {
            section = new XElement(sectionXName);
            AppendPreservingWhitespace(root, section);
        }

        XElement[] rows = section.Elements().Where(element => element.Name.LocalName == rowName &&
            string.Equals((string?)element.Attribute(targetKey), targetName, StringComparison.Ordinal)).ToArray();
        if (rows.Length > 1) throw new InvalidDataException($"The project has duplicate association rows for '{targetName}'.");
        XElement? row = rows.SingleOrDefault();
        XName rowXName = root.Name.Namespace + rowName;
        if (row is null)
        {
            row = new XElement(rowXName);
            AppendPreservingWhitespace(section, row);
        }
        row.SetAttributeValue(targetKey, targetName);
        if (isPlan)
        {
            row.SetAttributeValue(Project.Keys.PlanAssociation_Geom, geometry!.Layer.Name);
            row.SetAttributeValue(Project.Keys.PlanAssociation_BC, boundary!.Layer.Name);
        }
        else
        {
            row.SetAttributeValue(Project.Keys.GeometryAssociation_Terrain,
                request.ClearTerrain ? null : terrain!.Layer.Name);
            row.SetAttributeValue(Project.Keys.GeometryAssociation_NValues,
                request.ClearNValue ? null : nValue!.Layer.Name);
        }

        if (!isPlan && request.ClearTerrain && request.ClearNValue &&
            row.Attributes().All(attribute => attribute.Name.LocalName == Project.Keys.GeometryAssociation_Geom) &&
            !row.Nodes().Any(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
        {
            // Match Project.Save's omission of a fully-default association row,
            // while retaining any unknown attributes or extension children.
            row.Remove();
        }

        if (document.Declaration is { } declaration) declaration.Encoding = "utf-8";
        using var text = new Utf8StringWriter();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = document.Declaration is null,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CheckCharacters = true
        };
        using (XmlWriter writer = XmlWriter.Create(text, settings)) document.Save(writer);
        return text.ToString();
    }

    private static ModelOption? ResolveSelection(Project project, string kind, string? path, string? sha,
        ProjectAssociationOption[] options, bool allowClear = false)
    {
        if (allowClear && path is null && sha is null) return null;
        if (path is null || sha is null) throw new ArgumentException("A selected association requires an exact path and SHA.");
        ProjectAssociationOption[] matches = options.Where(option => option.Path is not null &&
            SamePath(option.Path, path)).ToArray();
        if (matches.Length != 1 || !SameSHA(matches[0].SHA256, sha))
            throw new SourceFileChangedException();
        CoreLayerBase[] layers = kind switch
        {
            "geometry" => project.Geometries.Cast<CoreLayerBase>().ToArray(),
            "boundaryCondition" => project.BoundaryConditions.Cast<CoreLayerBase>().ToArray(),
            "terrain" => project.Terrains.Cast<CoreLayerBase>().ToArray(),
            "nValue" => project.NValues.Cast<CoreLayerBase>().ToArray(),
            _ => []
        };
        CoreLayerBase[] layerMatches = layers.Where(layer => SamePath(layer.Filename, path) &&
            string.Equals(layer.Name, matches[0].Name, StringComparison.Ordinal)).ToArray();
        if (layerMatches.Length != 1) throw new SourceFileChangedException();
        return new(kind, layerMatches[0], matches[0]);
    }

    private static void VerifyAfter(string kind, ProjectAssociationState after,
        ModelOption? geometry, ModelOption? boundary, ModelOption? terrain, ModelOption? nValue)
    {
        if (kind == "plan")
        {
            if (!SameOptionalPath(after.Geometry?.Path, geometry?.Layer.Filename) ||
                !SameOptionalPath(after.BoundaryCondition?.Path, boundary?.Layer.Filename))
                throw new InvalidDataException("The reopened Plan association did not match the selected Geometry and Boundary Condition.");
        }
        else if (!SameOptionalPath(after.Terrain?.Path, terrain?.Layer.Filename) ||
                 !SameOptionalPath(after.NValue?.Path, nValue?.Layer.Filename))
            throw new InvalidDataException("The reopened Geometry association did not match the selected Terrain and N-Value layer.");
    }

    private static void VerifyTargetHash(CoreLayerBase target, string expectedSHA)
    {
        if (!SameSHA(HashFile(target.Filename), expectedSHA)) throw new SourceFileChangedException();
    }

    private static void VerifyCatalogTarget(CatalogTreeSnapshot snapshot, string kind,
        string path, string name)
    {
        string sourceClass = kind == "plan" ? "ObservablePlan" : "ObservableGeometry";
        CatalogTreeNode[] matches = WalkNodes(snapshot.Roots).Where(node => node.SourceClass == sourceClass &&
            node.Name == name && node.AssetID is { } assetPath && SamePath(assetPath, path)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("The reopened project catalog did not expose the exact association target.");
    }

    private static IEnumerable<CatalogTreeNode> WalkNodes(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is not null)
                foreach (CatalogTreeNode child in WalkNodes(node.Children)) yield return child;
        }
    }

    private static XElement ParseProjectRoot(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
        if (root.Name.LocalName != Project.Keys.Project) throw new InvalidDataException("The project XML root is not Project.");
        return root;
    }

    private static void EnsureSnapshotUnchanged(ProjectSnapshot source)
    {
        (string? firstPath, string? firstRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (firstPath is null || firstRevision is null || !SamePath(firstPath, source.Path) ||
            !SameSHA(firstRevision, source.Revision)) throw new SourceFileChangedException();
        byte[] bytes = File.ReadAllBytes(source.Path);
        string xml = File.ReadAllText(source.Path);
        (string? lastPath, string? lastRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (lastPath is null || lastRevision is null || !SamePath(lastPath, source.Path) ||
            !SameSHA(lastRevision, firstRevision) || !bytes.AsSpan().SequenceEqual(source.Bytes) ||
            !string.Equals(xml, source.Xml, StringComparison.Ordinal) || !SameSHA(HashText(xml), source.XmlSHA))
            throw new SourceFileChangedException();
    }

    private static void EnsureCommittedSnapshot(string path, string expectedXml,
        string expectedRevision, string expectedXmlSHA)
    {
        (string? firstPath, string? firstRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (firstPath is null || firstRevision is null || !SamePath(firstPath, path) ||
            !SameSHA(firstRevision, expectedRevision)) throw new SourceFileChangedException();
        string xml = File.ReadAllText(path);
        (string? lastPath, string? lastRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (lastPath is null || lastRevision is null || !SamePath(lastPath, path) ||
            !SameSHA(lastRevision, firstRevision) || !string.Equals(xml, expectedXml, StringComparison.Ordinal) ||
            !SameSHA(HashText(xml), expectedXmlSHA)) throw new SourceFileChangedException();
    }

    private static string RollbackProject(ProjectSnapshot source, string committedRevision, byte[] committedBytes)
    {
        try
        {
            (string? path, string? revision) = CatalogTreeService.TryReadStableRevision(source.Path);
            if (path is null || revision is null || !SamePath(path, source.Path) ||
                !SameSHA(revision, committedRevision) ||
                !File.ReadAllBytes(source.Path).AsSpan().SequenceEqual(committedBytes))
                return "The Project changed after the XML commit and was left untouched.";
            string rollbackPath = WriteProjectTemp(source.Path, source.Bytes);
            try { File.Move(rollbackPath, source.Path, overwrite: true); }
            finally { TryDeleteFile(rollbackPath); }
            EnsureSnapshotUnchanged(source);
            return "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "The Project rollback could not be confirmed: " + error.Message;
        }
    }

    private static string WriteProjectTemp(string path, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path)!, ".project-association-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temp;
        }
        catch { TryDeleteFile(temp); throw; }
    }

    private static void AppendPreservingWhitespace(XContainer parent, XElement element)
    {
        XNode? last = parent.Nodes().LastOrDefault();
        if (last is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
        {
            string? indent = parent.Elements().LastOrDefault()?.PreviousNode is XText before &&
                string.IsNullOrWhiteSpace(before.Value) ? before.Value : null;
            trailing.AddBeforeSelf(new XText(indent ?? trailing.Value + "  "), element);
            return;
        }
        parent.Add(element);
    }

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers)) layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(
        new UTF8Encoding(false, true).GetBytes(text)));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsSHA(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool SameSHA(string? left, string? right) => left is not null && right is not null &&
        left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static bool SameOptionalSHA(string? left, string? right) =>
        left is null || right is null ? left is null && right is null : SameSHA(left, right);
    private static bool SamePath(string left, string right) => SourceFilePaths.SameSourceFile(left, right);
    private static bool SameOptionalPath(string? left, string? right) =>
        left is null || right is null ? left is null && right is null : SamePath(left, right);
    private static string CanonicalPath(string path) => SourceFilePaths.Canonical(path);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false, true);
    }
}
