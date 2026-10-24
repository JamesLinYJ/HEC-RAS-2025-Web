using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Files;

namespace Ras.Backend;

public sealed record CreationRequest(string Kind, string ProjectPath, string ProjectXml,
    Dictionary<string, string> Fields, string? Revision = null,
    CoreLayerRequestContext? CoreLayerContext = null, Guid? ProjectSessionID = null,
    string? ExpectedProjectRevision = null, string? ProjectXmlSHA = null);
public sealed record CreationChoice(string Id, string Title);
public sealed record CreationWindow(DateTimeOffset Start, DateTimeOffset End);
public sealed record BoundaryCreationDefaults(string Name, DateTimeOffset Start, DateTimeOffset End);
public sealed record CreationInspection(string Revision, bool CanExecute, string? ValidationMessage,
    string? Warning, Dictionary<string, CreationChoice[]> Choices, Dictionary<string, CreationWindow> BoundaryTimeWindows,
    BoundaryCreationDefaults? BoundaryDefaults, Guid? ProjectSessionID, string ProjectPath);
public sealed record CreatedModel(string AssetPath, string Section, string? Group, string? Element,
    string? PrimaryKey, string Name, Dictionary<string, string> Associations);
public sealed record ModelCreationProjectSnapshot(bool Success, string ProjectPath, string ProjectXml,
    string Revision, string ProjectXmlSHA, Guid ProjectSessionID,
    CoreLayerRequestContext? CoreLayerContext);
public sealed record ModelCreationCommitReceipt(CreatedModel Created, ModelCreationProjectSnapshot Project,
    string AssetRevision, string PreviousProjectRevision);

// Both paths use the same CoreLayerBase constructors, associations and HDF
// writers as Project.AddNew*. Legacy Create publishes only the new HDF file;
// Commit also persists its project association in a revision-checked transaction.
public static class ModelCreationService
{
    public static CreationInspection Inspect(CreationRequest request)
    {
        using var context = new Context(request);
        string? validation = null;
        string? warning = null;
        try { context.Validate(out warning); }
        catch (ArgumentException error) { validation = error.Message; }
        string revisionBeforeDefaults = context.Revision();
        BoundaryCreationDefaults? boundaryDefaults = request.Kind == "newBoundary" ? context.BoundaryDefaults() : null;
        string revision = context.Revision();
        if (revision != revisionBeforeDefaults) throw new SourceFileChangedException();
        return new(revision, validation == null, validation, warning,
            context.Choices(), context.Project.BoundaryConditions.ToDictionary(
                item => Path.GetFullPath(item.Filename), item => new CreationWindow(ToOffset(item.StartTime), ToOffset(item.EndTime))),
            boundaryDefaults, request.ProjectSessionID, request.ProjectPath);
    }

    public static CreatedModel Create(CreationRequest request)
    {
        using var context = new Context(request);
        context.Validate(out _);
        string revision = context.Revision();
        if (request.Revision == null || revision != request.Revision) throw new SourceFileChangedException();
        string directory = Path.GetDirectoryName(context.Destination)!;
        Directory.CreateDirectory(directory);
        string stagingDirectory = Path.Combine(directory, ".model-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            string staging = Path.Combine(stagingDirectory, context.Name + ".h5");
            CreatedModel created = CreateAndSave(context, staging);
            // Recheck selected source bytes and the project before publishing.
            // File.Move without overwrite is the final collision guard.
            if (context.Revision() != revision) throw new SourceFileChangedException();
            context.ValidateDestination();
            File.Move(staging, context.Destination, overwrite: false);
            return created;
        }
        finally { Directory.Delete(stagingDirectory, recursive: true); }
    }

    public static ModelCreationCommitReceipt Commit(CreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommitRequest(request);
        string requestedProjectPath = Path.GetFullPath(request.ProjectPath);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(requestedProjectPath);
        ProjectSnapshot source = ReadProjectSnapshot(request);
        Guid sessionID = request.ProjectSessionID!.Value;
        ValidateCoreLayerContext(request, source);

        using var context = new Context(request);
        context.Validate(out _);
        string inspectionRevision = context.Revision();
        if (!inspectionRevision.Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        Dictionary<string, string> dependencyRevisions = context.DependencyRevisions();

        string destinationDirectory = Path.GetDirectoryName(context.Destination)!;
        Directory.CreateDirectory(destinationDirectory);
        context.ValidateDestination();
        string stagingDirectory = Path.Combine(destinationDirectory, ".model-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        string stagedAsset = Path.Combine(stagingDirectory, context.Name + ".h5");
        string? stagedProject = null;
        bool assetPublished = false;
        bool projectPublished = false;
        string? assetRevision = null;
        string? committedProjectRevision = null;
        bool metadataChanged = false;
        string committedXml = source.Xml;
        byte[] committedProjectBytes = source.FileBytes;
        CreatedModel created;

        try
        {
            created = CreateAndSave(context, stagedAsset);
            assetRevision = FileSha256(stagedAsset);
            context.EnsureDependencyRevisions(dependencyRevisions);
            if (!context.Revision().Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            committedXml = ApplyCreationMetadata(source.Xml, created);
            metadataChanged = !string.Equals(committedXml, source.Xml, StringComparison.Ordinal);
            committedProjectBytes = metadataChanged
                ? Encoding.UTF8.GetBytes(committedXml)
                : source.FileBytes;
            committedProjectRevision = metadataChanged ? HashBytes(committedProjectBytes) : source.Revision;
            if (metadataChanged)
            {
                string expectedXmlSHA = HashText(committedXml);
                if (!expectedXmlSHA.Equals(HashBytes(committedProjectBytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The staged project XML bytes do not match their revision.");
                stagedProject = WriteProjectTemp(source.Path, committedProjectBytes);
            }

            context.EnsureDependencyRevisions(dependencyRevisions);
            EnsureSnapshotUnchanged(source);
            context.ValidateDestination();
            File.Move(stagedAsset, context.Destination, overwrite: false);
            assetPublished = true;
            if (!FileSha256(context.Destination).Equals(assetRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            // The C# project tree must resolve the new HDF and the exact standard
            // association rows before the saved project is replaced.
            VerifyCreatedAssetFile(context.Destination, request.Kind);
            EnsureSnapshotUnchanged(source);
            if (metadataChanged)
            {
                if (stagedProject is null) throw new InvalidOperationException("The project XML staging file is missing.");
                File.Move(stagedProject, source.Path, overwrite: true);
                stagedProject = null;
                projectPublished = true;
            }

            ProjectWorkspaceResponse opened = ProjectWorkspaceService.Open(new ProjectWorkspaceOpenRequest(source.Path));
            if (!opened.Success || !SourceFilePaths.SameSourceFile(opened.ProjectPath, source.Path) ||
                !opened.Revision.Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(opened.ProjectXml, committedXml, StringComparison.Ordinal))
                throw new IOException("The saved project could not be reopened with the committed XML and revision.");

            string projectXmlSHA = HashText(opened.ProjectXml);
            CoreLayerRequestContext? committedContext = CoreLayerImportService.RebaseContext(
                request.CoreLayerContext, opened.Revision, context.Destination, assetRevision);
            Project reopenedProject = CoreLayerImportService.OpenProjectWithMetadata(
                opened.ProjectPath, XElement.Parse(opened.ProjectXml, LoadOptions.None), committedContext);
            try { VerifyProjectReadback(reopenedProject, context, created, request.Kind); }
            finally { DisposeProjectResources(reopenedProject); }

            CatalogTreeSnapshot catalog = CatalogTreeService.LoadProject(new CatalogTreeRequest(
                opened.ProjectPath, opened.ProjectXml, Mode: "Project", ProjectSessionID: sessionID.ToString("D"),
                CoreLayerContext: committedContext));
            VerifyCatalogReadback(catalog, created, request.Kind);
            context.EnsureDependencyRevisions(dependencyRevisions);
            if (!FileSha256(context.Destination).Equals(assetRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            EnsureCommittedSnapshot(source.Path, committedXml, committedProjectRevision, projectXmlSHA);

            var projectSnapshot = new ModelCreationProjectSnapshot(true, opened.ProjectPath, opened.ProjectXml,
                opened.Revision, projectXmlSHA, sessionID, committedContext);
            return new(created, projectSnapshot, assetRevision, source.Revision);
        }
        catch (Exception error)
        {
            string rollback = RollbackCommit(source, context.Destination, assetRevision,
                assetPublished, projectPublished, committedProjectRevision, committedProjectBytes);
            if (rollback.Length > 0)
                throw new IOException("Model creation did not produce a confirmed receipt. " + rollback, error);
            throw;
        }
        finally
        {
            if (stagedProject is not null) TryDeleteFile(stagedProject);
            if (Directory.Exists(stagingDirectory)) TryDeleteDirectory(stagingDirectory);
        }
    }

    private static CreatedModel CreateAndSave(Context context, string stagingPath)
    {
        var associations = new Dictionary<string, string>(StringComparer.Ordinal);
        string? group = null, element = null, primaryKey = null;
        CoreLayerBase created = context.Request.Kind switch
        {
            "newGeometry" => CreateGeometry(context, stagingPath, associations,
                out group, out element, out primaryKey),
            "newBoundary" => CreateBoundary(context, stagingPath),
            "newPlan" => CreatePlan(context, stagingPath, associations,
                out group, out element, out primaryKey),
            _ => throw new NotSupportedException("Unsupported model creation kind.")
        };
        try { created.Save(); }
        finally { created.Dispose(); }
        return new(context.Destination, context.Section, group, element, primaryKey, context.Name, associations);
    }

    private static Geometry CreateGeometry(Context context, string path, Dictionary<string, string> associations,
        out string? group, out string? element, out string? primaryKey)
    {
        var geometry = Geometry.CreateNew(path);
        context.Project.AddGeometry(geometry);
        if (context.Terrain is Terrain terrain) geometry.GeometryAssociation.Terrain = terrain;
        if (context.NValues is NValueLayer nvalues) geometry.GeometryAssociation.NValueLayer = nvalues;
        associations["Terrain"] = context.Terrain?.Name ?? "";
        associations["N_Values"] = context.NValues?.Name ?? "";
        group = "GeometryAssociations"; element = "GeometryAssociation"; primaryKey = "Geometry";
        return geometry;
    }

    private static BoundaryCondition CreateBoundary(Context context, string path)
    {
        var boundary = BoundaryCondition.CreateNew(path);
        context.Project.AddBoundaryCondition(boundary);
        boundary.StartTime = context.Start;
        boundary.EndTime = context.End;
        return boundary;
    }

    private static Plan CreatePlan(Context context, string path, Dictionary<string, string> associations,
        out string? group, out string? element, out string? primaryKey)
    {
        var plan = Plan.CreateNew(path);
        context.Project.AddPlan(plan);
        var association = plan.AssertGetPlanAssocation();
        association.Geometry = context.Geometry ?? throw new ArgumentException("Select an existing project model for this association.");
        association.BoundaryCondition = context.Boundary ?? throw new ArgumentException("Select an existing project model for this association.");
        plan.TimeWindowMode = context.InheritWindow ? PlanTimeWindowMode.BoundaryCondition : PlanTimeWindowMode.Plan;
        plan.InternalStartTime = context.Start;
        plan.InternalComputeDurationSeconds = (context.End - context.Start).TotalSeconds;
        associations["Geometry"] = context.Geometry.Name;
        associations["BoundaryCondition"] = context.Boundary.Name;
        group = "PlanAssociations"; element = "PlanAssociation"; primaryKey = "Plan";
        return plan;
    }

    private static string ApplyCreationMetadata(string projectXml, CreatedModel created)
    {
        XDocument document = XDocument.Parse(projectXml, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
        if (root.Name.LocalName != "Project") throw new InvalidDataException("The project XML root is not Project.");
        XNamespace ns = root.Name.Namespace;
        switch (created.PrimaryKey)
        {
            case "Geometry":
            {
                string terrainName = created.Associations.GetValueOrDefault("Terrain", "");
                string nvalueName = created.Associations.GetValueOrDefault("N_Values", "");
                XElement[] existingSections = root.Elements()
                    .Where(element => element.Name.LocalName == "GeometryAssociations").ToArray();
                if (existingSections.Length > 1)
                    throw new InvalidDataException("The project XML contains duplicate GeometryAssociations sections.");
                if (existingSections.SingleOrDefault() is { } existingSection)
                    EnsureNoDuplicateAssociation(existingSection, "GeometryAssociation", "Geometry", created.Name);
                // Project.BuildXML omits default GeometryAssociation rows. Preserve
                // that exact convention when neither optional source is selected.
                if (terrainName.Length == 0 && nvalueName.Length == 0) return projectXml;
                XElement section = GetOrAddProjectSection(root, ns, "GeometryAssociations");
                EnsureNoDuplicateAssociation(section, "GeometryAssociation", "Geometry", created.Name);
                var row = new XElement(ns + "GeometryAssociation");
                row.SetAttributeValue("Geometry", created.Name);
                if (terrainName.Length > 0) row.SetAttributeValue("Terrain", terrainName);
                if (nvalueName.Length > 0) row.SetAttributeValue("N_Values", nvalueName);
                section.Add(row);
                break;
            }
            case "Plan":
            {
                string geometry = RequiredAssociation(created, "Geometry");
                string boundary = RequiredAssociation(created, "BoundaryCondition");
                XElement section = GetOrAddProjectSection(root, ns, "PlanAssociations");
                EnsureNoDuplicateAssociation(section, "PlanAssociation", "Plan", created.Name);
                var row = new XElement(ns + "PlanAssociation");
                row.SetAttributeValue("Plan", created.Name);
                row.SetAttributeValue("Geometry", geometry);
                row.SetAttributeValue("BoundaryCondition", boundary);
                section.Add(row);
                break;
            }
            case null when created.Section == "Boundary Conditions":
                // Project.LoadAllEventsInProject discovers BoundaryCondition HDFs
                // from this exact folder; the C# Project format has no BC XML section.
                return projectXml;
            default:
                throw new InvalidDataException("The created model has no supported C# project association format.");
        }
        using var stream = new MemoryStream();
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            Indent = true,
            NewLineHandling = System.Xml.NewLineHandling.Entitize
        };
        using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(stream, settings)) document.Save(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static XElement GetOrAddProjectSection(XElement root, XNamespace ns, string name)
    {
        XElement[] sections = root.Elements().Where(element => element.Name.LocalName == name).ToArray();
        if (sections.Length > 1) throw new InvalidDataException($"The project XML contains duplicate {name} sections.");
        if (sections.Length == 1) return sections[0];
        string[] order = ["Projection", "Description", "DefaultTerrain", "PlanAssociations",
            "GeometryAssociations", "ResultAssociations", "Connections", "CleanupPolicy"];
        int rank = Array.IndexOf(order, name);
        XElement? before = root.Elements().FirstOrDefault(element =>
            Array.IndexOf(order, element.Name.LocalName) is int childRank && childRank > rank);
        var section = new XElement(ns + name);
        if (before is null) root.Add(section); else before.AddBeforeSelf(section);
        return section;
    }

    private static void EnsureNoDuplicateAssociation(XElement section, string rowName,
        string identityAttribute, string name)
    {
        if (section.Elements().Any(element => element.Name.LocalName == rowName &&
                string.Equals((string?)element.Attribute(identityAttribute), name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The project XML already contains a {rowName} row for '{name}'.");
    }

    private static string RequiredAssociation(CreatedModel created, string name)
    {
        string? value = created.Associations.GetValueOrDefault(name);
        return !string.IsNullOrWhiteSpace(value) ? value :
            throw new InvalidDataException($"The new model's {name} association is missing.");
    }

    private static void ValidateCommitRequest(CreationRequest request)
    {
        if (request.Kind is not ("newGeometry" or "newPlan" or "newBoundary"))
            throw new NotSupportedException("This tool is not connected to model creation.");
        if (!Path.IsPathFullyQualified(request.ProjectPath) ||
            !Path.GetExtension(request.ProjectPath).Equals(Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved .ras project before creating a model.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml) ||
            Encoding.UTF8.GetByteCount(request.ProjectXml) > 8L * 1024 * 1024 ||
            !IsSHA256(request.ProjectXmlSHA) ||
            !HashText(request.ProjectXml).Equals(request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!IsSHA256(request.ExpectedProjectRevision) || !IsSHA256(request.Revision))
            throw new ArgumentException("The project and model inspection revisions are required.");
        if (request.ProjectSessionID is null || request.ProjectSessionID == Guid.Empty)
            throw new ArgumentException("A non-empty project session ID is required.");
        if (request.Fields is null) throw new ArgumentException("Model creation fields are required.");
        if (request.CoreLayerContext is { } coreContext &&
            (!Guid.TryParse(coreContext.ProjectSessionID, out Guid contextSession) || contextSession != request.ProjectSessionID.Value ||
             !SourceFilePaths.SameSourceFile(coreContext.ProjectPath, request.ProjectPath) ||
             !string.Equals(coreContext.ProjectRevision, request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();
    }

    private static ProjectSnapshot ReadProjectSnapshot(CreationRequest request)
    {
        string canonicalPath = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(canonicalPath)) throw new FileNotFoundException("The project file was not found.", canonicalPath);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
            if (beforePath is null || beforeRevision is null) continue;
            byte[] fileBytes;
            string xml;
            try
            {
                fileBytes = File.ReadAllBytes(canonicalPath);
                xml = File.ReadAllText(canonicalPath);
            }
            catch (IOException) when (attempt < 2) { continue; }
            (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
            if (afterPath is null || afterRevision is null ||
                !SourceFilePaths.SameSourceFile(beforePath, afterPath) ||
                !beforeRevision.Equals(afterRevision, StringComparison.Ordinal)) continue;
            if (!SourceFilePaths.SameSourceFile(beforePath, request.ProjectPath) ||
                !beforeRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
                !HashText(xml).Equals(request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(xml, request.ProjectXml, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            XElement root;
            try { root = XElement.Parse(xml, LoadOptions.None); }
            catch (System.Xml.XmlException error) { throw new InvalidDataException("The project XML is invalid.", error); }
            if (root.Name.LocalName != "Project") throw new InvalidDataException("The project XML root is not Project.");
            return new(beforePath, xml, fileBytes, beforeRevision, HashText(xml));
        }
        throw new SourceFileChangedException();
    }

    private static void ValidateCoreLayerContext(CreationRequest request, ProjectSnapshot source)
    {
        if (request.CoreLayerContext is not { } context) return;
        if (!string.Equals(context.ProjectSessionID, request.ProjectSessionID!.Value.ToString("D"), StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(context.ProjectPath, source.Path) ||
            !string.Equals(context.ProjectRevision, source.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void EnsureSnapshotUnchanged(ProjectSnapshot source)
    {
        (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (beforePath is null || beforeRevision is null || !SourceFilePaths.SameSourceFile(beforePath, source.Path) ||
            !beforeRevision.Equals(source.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        byte[] bytes = File.ReadAllBytes(source.Path);
        string xml = File.ReadAllText(source.Path);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (afterPath is null || afterRevision is null || !SourceFilePaths.SameSourceFile(afterPath, source.Path) ||
            !afterRevision.Equals(beforeRevision, StringComparison.Ordinal) ||
            !bytes.AsSpan().SequenceEqual(source.FileBytes) ||
            !string.Equals(xml, source.Xml, StringComparison.Ordinal) ||
            !HashText(xml).Equals(source.XmlSHA, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void EnsureCommittedSnapshot(string projectPath, string expectedXml,
        string expectedRevision, string expectedXmlSHA)
    {
        (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(projectPath);
        if (beforePath is null || beforeRevision is null || !SourceFilePaths.SameSourceFile(beforePath, projectPath) ||
            !beforeRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string xml = File.ReadAllText(projectPath);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(projectPath);
        if (afterPath is null || afterRevision is null || !SourceFilePaths.SameSourceFile(afterPath, projectPath) ||
            !afterRevision.Equals(beforeRevision, StringComparison.Ordinal) ||
            !string.Equals(xml, expectedXml, StringComparison.Ordinal) ||
            !HashText(xml).Equals(expectedXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void VerifyCreatedAssetFile(string path, string kind)
    {
        CoreLayerBase layer = kind switch
        {
            "newGeometry" => new Geometry(path),
            "newPlan" => new Plan(path),
            "newBoundary" => new BoundaryCondition(path),
            _ => throw new NotSupportedException("Unsupported model creation kind.")
        };
        using (layer)
            if (layer.HasLoadErrors) throw new InvalidDataException("The staged model HDF could not be reopened cleanly.");
    }

    private static void VerifyProjectReadback(Project project, Context context, CreatedModel created, string kind)
    {
        static bool Same(string? path, string expected) => path is not null && SourceFilePaths.SameSourceFile(path, expected);
        switch (kind)
        {
            case "newGeometry":
            {
                Geometry geometry = project.Geometries.SingleOrDefault(item => Same(item.Filename, created.AssetPath))
                    ?? throw new InvalidDataException("The reopened project did not load the created Geometry HDF.");
                var association = project.GetGeometryAssociation(geometry);
                if ((association.Terrain is null) != (context.Terrain is null) ||
                    (association.Terrain is not null && !Same(association.Terrain.Filename, context.Terrain!.Filename)) ||
                    (association.NValueLayer is null) != (context.NValues is null) ||
                    (association.NValueLayer is not null && !Same(association.NValueLayer.Filename, context.NValues!.Filename)))
                    throw new InvalidDataException("The reopened Geometry association did not match the exact inspected sources.");
                break;
            }
            case "newPlan":
            {
                Plan plan = project.Plans.SingleOrDefault(item => Same(item.Filename, created.AssetPath))
                    ?? throw new InvalidDataException("The reopened project did not load the created Plan HDF.");
                var association = project.GetPlanAssociation(plan);
                if (association is null || context.Geometry is null || context.Boundary is null ||
                    association.Geometry is null || association.BoundaryCondition is null ||
                    !Same(association.Geometry.Filename, context.Geometry.Filename) ||
                    !Same(association.BoundaryCondition.Filename, context.Boundary.Filename))
                    throw new InvalidDataException("The reopened Plan association did not match the exact inspected sources.");
                break;
            }
            case "newBoundary":
                if (project.BoundaryConditions.Count(item => Same(item.Filename, created.AssetPath)) != 1)
                    throw new InvalidDataException("The reopened project did not discover the created Boundary Condition HDF.");
                break;
            default:
                throw new NotSupportedException("Unsupported model creation kind.");
        }
    }

    private static void VerifyCatalogReadback(CatalogTreeSnapshot snapshot, CreatedModel created, string kind)
    {
        string sourceClass = kind switch
        {
            "newGeometry" => "ObservableGeometry",
            "newPlan" => "ObservablePlan",
            "newBoundary" => "ObservableBoundaryCondition",
            _ => throw new NotSupportedException("Unsupported model creation kind.")
        };
        CatalogTreeNode[] matches = WalkCatalogNodes(snapshot.Roots).Where(node =>
            node.SourceClass == sourceClass && node.Name == created.Name && node.AssetID is { } path &&
            SourceFilePaths.SameSourceFile(path, created.AssetPath)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The reopened project tree did not expose one exact {kind} node.");
    }

    private static IEnumerable<CatalogTreeNode> WalkCatalogNodes(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 })
                foreach (CatalogTreeNode child in WalkCatalogNodes(node.Children)) yield return child;
        }
    }

    private static string RollbackCommit(ProjectSnapshot source, string targetPath, string? assetRevision,
        bool assetPublished, bool projectPublished, string? committedProjectRevision, byte[] committedProjectBytes)
    {
        var warnings = new List<string>();
        if (projectPublished && committedProjectRevision is not null)
        {
            try
            {
                (string? path, string? revision) = CatalogTreeService.TryReadStableRevision(source.Path);
                if (path is null || revision is null || !SourceFilePaths.SameSourceFile(path, source.Path) ||
                    !revision.Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
                    !File.ReadAllBytes(source.Path).AsSpan().SequenceEqual(committedProjectBytes))
                    warnings.Add("The project changed after publication; it was left untouched.");
                else
                {
                    string rollbackPath = WriteProjectTemp(source.Path, source.FileBytes);
                    try { File.Move(rollbackPath, source.Path, overwrite: true); }
                    finally { TryDeleteFile(rollbackPath); }
                }
            }
            catch (Exception rollbackError)
            {
                warnings.Add("The project rollback could not be confirmed: " + rollbackError.Message);
            }
        }

        if (assetPublished && assetRevision is not null)
        {
            try
            {
                bool projectIsOriginal = IsProjectSnapshot(source);
                if (projectIsOriginal && File.Exists(targetPath) &&
                    FileSha256(targetPath).Equals(assetRevision, StringComparison.OrdinalIgnoreCase))
                    File.Delete(targetPath);
                else if (File.Exists(targetPath))
                    warnings.Add("The published model or project changed; the model file was left untouched.");
            }
            catch (Exception rollbackError)
            {
                warnings.Add("The model cleanup could not be confirmed: " + rollbackError.Message);
            }
        }
        return string.Join(" ", warnings);
    }

    private static bool IsProjectSnapshot(ProjectSnapshot source)
    {
        (string? path, string? revision) = CatalogTreeService.TryReadStableRevision(source.Path);
        return path is not null && revision is not null && SourceFilePaths.SameSourceFile(path, source.Path) &&
            revision.Equals(source.Revision, StringComparison.Ordinal) &&
            File.ReadAllBytes(source.Path).AsSpan().SequenceEqual(source.FileBytes);
    }

    private static string WriteProjectTemp(string projectPath, byte[] bytes)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(projectPath)!, ".model-create-project-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return tempPath;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value));
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record ProjectSnapshot(string Path, string Xml, byte[] FileBytes,
        string Revision, string XmlSHA);

    private static void DisposeProjectResources(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private static DateTimeOffset ToOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Local));

    private sealed class Context : IDisposable
    {
        private readonly CreationRequest request;
        private readonly string[] boundaryConditionSourcePaths;
        public Project Project { get; }
        public string Name { get; }
        public string Section { get; }
        public string Destination { get; }
        public CreationRequest Request => request;
        public Terrain? Terrain { get; private set; }
        public NValueLayer? NValues { get; private set; }
        public Geometry? Geometry { get; private set; }
        public BoundaryCondition? Boundary { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }
        public bool InheritWindow { get; private set; }
        private string Field(string schema, string field) => request.Fields.GetValueOrDefault(schema + "/" + field, "");
        public Context(CreationRequest request)
        {
            this.request = request;
            Section = request.Kind switch { "newGeometry" => "Geometries", "newPlan" => "Plans", "newBoundary" => "Boundary Conditions", _ => throw new NotSupportedException("This tool is not connected to model creation.") };
            if (!Path.IsPathFullyQualified(request.ProjectPath)) throw new ArgumentException("Open a saved project before creating a model.");
            Project = CoreLayerImportService.OpenProject(Path.GetFullPath(request.ProjectPath),
                request.ProjectXml, request.CoreLayerContext);
            try
            {
                boundaryConditionSourcePaths = request.Kind == "newBoundary"
                    ? Project.BoundaryConditions.Select(item => item.Filename)
                        .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                Name = request.Kind switch { "newGeometry" => Field("NewGeometryControl", "GeometryName"), "newPlan" => Field("NewPlanControl", "PlanName"), _ => Field("NewBoundaryConditionControl", "BoundaryConditionName") };
                string directory = request.Kind switch { "newGeometry" => Project.GeometriesDirectory, "newPlan" => Project.PlansDirectory, _ => Project.BoundaryConditionDirectory };
                // Never combine an unvalidated user filename into a write path.
                Destination = ValidName(Name) ? Path.Combine(directory, Name + ".h5") : "";
            }
            catch { Dispose(); throw; }
        }
        private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value)
            && value == value.Trim() && !value.EndsWith('.') && !value.StartsWith('.')
            && Encoding.UTF8.GetByteCount(value + ".h5") <= 255
            && !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
        public void ValidateDestination()
        {
            if (!ValidName(Name)) throw new ArgumentException("Enter a valid model name without path separators or reserved filename characters.");
            string directory = Path.GetDirectoryName(Destination)!;
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(path => string.Equals(Path.GetFileName(path), Name + ".h5", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A model with this name already exists.");
        }
        private T? Select<T>(IEnumerable<T> items, string key, bool required) where T : CoreLayerBase
        {
            string value = request.Fields.GetValueOrDefault(key, "");
            if (value.Length == 0 && !required) return null;
            var selected = items.FirstOrDefault(item => Path.GetFullPath(item.Filename) == value)
                ?? throw new ArgumentException("Select an existing project model for this association.");
            if (selected.HasLoadErrors) throw new IOException("The selected model has load errors. Resolve them before creating a related model.");
            return selected;
        }
        public void Validate(out string? warning)
        {
            warning = null;
            ValidateDestination();
            if (request.Kind == "newGeometry")
            {
                Terrain = Select(Project.Terrains, "NewGeometryControl/SelectedTerrain", false);
                NValues = Select(Project.NValues, "NewGeometryControl/SelectedNValue", false);
                return;
            }
            string schema = request.Kind == "newBoundary" ? "NewBoundaryConditionControl" : "NewPlanControl";
            if (request.Kind == "newPlan")
            {
                Geometry = Select(Project.Geometries, schema + "/SelectedGeometry", true);
                Boundary = Select(Project.BoundaryConditions, schema + "/SelectedBoundaryCondition", true);
                string mode = Field(schema, "TimeWindowMode");
                if (mode is not ("BoundaryCondition" or "Plan")) throw new ArgumentException("Select a valid time window mode.");
                InheritWindow = mode == "BoundaryCondition";
            }
            if (InheritWindow) { Start = Boundary!.StartTime; End = Boundary.EndTime; }
            else
            {
                Start = ParseDate(Field(schema, "StartTime")); End = ParseDate(Field(schema, "EndTime"));
            }
            if (End <= Start) throw new ArgumentException("End time must be later than start time.");
            if (Boundary != null && (Start < Boundary.StartTime || End > Boundary.EndTime))
                warning = "The custom time window extends beyond the boundary condition time window.";
        }
        public BoundaryCreationDefaults BoundaryDefaults()
        {
            string uniquePath = FileHelpers.GetUniqueFileName(
                Path.Combine(Project.BoundaryConditionDirectory, "Boundary Condition"),
                ".h5", path => File.Exists(path));
            Project.GetReasonableProjectTimeWindow(out var start, out var end);
            return new(Path.GetFileNameWithoutExtension(uniquePath), ToOffset(start), ToOffset(end));
        }
        private static DateTime ParseDate(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.LocalDateTime : throw new ArgumentException("Enter a valid start and end time.");
        public Dictionary<string, CreationChoice[]> Choices()
        {
            static CreationChoice[] ChoicesFor(IEnumerable<CoreLayerBase> layers) => layers.Select(item => new CreationChoice(Path.GetFullPath(item.Filename), item.Name)).ToArray();
            return new() {
                ["NewGeometryControl/SelectedTerrain"] = ChoicesFor(Project.Terrains),
                ["NewGeometryControl/SelectedNValue"] = ChoicesFor(Project.NValues),
                ["NewPlanControl/SelectedGeometry"] = ChoicesFor(Project.Geometries),
                ["NewPlanControl/SelectedBoundaryCondition"] = ChoicesFor(Project.BoundaryConditions)
            };
        }
        public string Revision()
        {
            static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
            IEnumerable<string> sourcePaths = new CoreLayerBase?[] { Terrain, NValues, Geometry, Boundary }
                .Where(item => item != null).Cast<CoreLayerBase>().Select(item => item.Filename);
            if (request.Kind == "newBoundary") sourcePaths = sourcePaths.Concat(boundaryConditionSourcePaths);
            var sources = sourcePaths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new { path, hash = HashFile(path) });
            string payload = JsonSerializer.Serialize(new { request.Kind, request.ProjectXml, fields = request.Fields.OrderBy(item => item.Key, StringComparer.Ordinal), project = HashFile(request.ProjectPath), sources });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }
        public Dictionary<string, string> DependencyRevisions()
        {
            IEnumerable<string> paths = request.Kind == "newBoundary"
                ? boundaryConditionSourcePaths
                : new CoreLayerBase?[] { Terrain, NValues, Geometry, Boundary }
                    .Where(item => item is not null).Cast<CoreLayerBase>().Select(item => item.Filename);
            return paths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(path => path, ModelCreationService.FileSha256, StringComparer.Ordinal);
        }
        public void EnsureDependencyRevisions(IReadOnlyDictionary<string, string> expected)
        {
            Dictionary<string, string> current = DependencyRevisions();
            if (current.Count != expected.Count || expected.Any(item =>
                    !current.TryGetValue(item.Key, out string? revision) ||
                    !revision.Equals(item.Value, StringComparison.OrdinalIgnoreCase)))
                throw new SourceFileChangedException();
        }
        public void Dispose()
        {
            foreach (var layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries).Concat(Project.Terrains)
                .Concat(Project.BoundaryConditions).Concat(Project.NValues).Concat(Project.ClassificationLayers).Cast<IDisposable>().Concat(Project.Results)) layer.Dispose();
        }
    }
}
