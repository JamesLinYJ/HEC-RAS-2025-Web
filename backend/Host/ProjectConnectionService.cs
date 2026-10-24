using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Files;

namespace Ras.Backend;

public sealed record ProjectConnectionInspectRequest(
    string Action,
    string Kind,
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string? ConnectionName,
    string? Name,
    string? Path,
    string? Description,
    string? ResultName,
    string? SearchPattern = null,
    bool Recurse = false,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectConnectionCommitRequest(
    string Action,
    string Kind,
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string? ConnectionName,
    string? Name,
    string? Path,
    string? Description,
    string? ResultName,
    string? ExpectedConnectionSHA256,
    string? ExpectedSourceSHA256,
    string ExpectedSourceReferencesSHA256,
    string? ExpectedNormalizedName = null,
    string? SearchPattern = null,
    bool Recurse = false,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectConnectionIdentity(
    string Name,
    string Type,
    string? Path,
    string Description,
    string ConnectionSHA256,
    string? SourcePath,
    string? SourceSHA256,
    bool SourceExists,
    string? SearchPattern = null,
    bool? Recurse = null);

public sealed record ProjectConnectionInspection(
    bool CanCommit,
    string? ValidationMessage,
    string Action,
    string Kind,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    Guid ProjectSessionID,
    string? ConnectionName,
    string? Name,
    string? CanonicalPath,
    string? ConnectionSHA256,
    string? SourceSHA256,
    bool SourceExists,
    int SourceFileCount,
    string SourceReferencesSHA256,
    string? NormalizedName,
    ProjectConnectionIdentity? Before,
    ProjectConnectionIdentity? After);

public sealed record ProjectConnectionSnapshot(
    bool Success,
    string ProjectPath,
    string ProjectXml,
    string Revision,
    string ProjectXmlSHA,
    Guid ProjectSessionID,
    CoreLayerRequestContext? CoreLayerContext);

public sealed record ProjectConnectionSourceCapture(
    string? Path,
    [property: JsonPropertyName("sha256")] string? SHA256,
    bool Exists,
    int FileCount);

public sealed record ProjectConnectionCommitReceipt(
    bool Success,
    string Action,
    string Kind,
    ProjectConnectionSnapshot Project,
    string PreviousProjectRevision,
    ProjectConnectionIdentity? Before,
    ProjectConnectionIdentity? After,
    ProjectConnectionSourceCapture SourceCapture,
    string PreviousSourceReferencesSHA256,
    string SourceReferencesSHA256);

/// <summary>
/// Writes one C# Project connection as a revision-checked XML transaction.
/// The original Project model defines File and Directory external connections;
/// selecting a Result is an ownership-checked File connection to its HDF.
/// </summary>
public static class ProjectConnectionService
{
    private const int MaximumProjectXmlBytes = 8 * 1024 * 1024;
    private const int MaximumNameLength = 256;
    private const int MaximumDescriptionBytes = 4096;

    private sealed record ProjectSnapshot(string Path, string Xml, byte[] Bytes,
        string Revision, string XmlSHA);

    private sealed record SourceCapture(string? Path, string? Revision, bool Exists, int FileCount);

    private sealed record ConnectionRow(XElement Xml, string Name, string Type,
        ExternalConnectionBase? Model, ProjectConnectionIdentity Identity);

    public static ProjectConnectionInspection Inspect(ProjectConnectionInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProjectRequest(request);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(request.ProjectPath);
        using ProjectState state = OpenState(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ProjectRevision);
        ValidateSessionAndContext(request.ProjectPath, request.ProjectRevision,
            request.ProjectSessionID, request.CoreLayerContext);
        return InspectCore(request, state);
    }

    public static ProjectConnectionCommitReceipt Commit(ProjectConnectionCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProjectRequest(request);
        ValidateCommitCapture(request);
        string projectPath = Path.GetFullPath(request.ProjectPath);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(projectPath);
        using ProjectState state = OpenState(projectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ProjectRevision);
        ValidateSessionAndContext(projectPath, request.ProjectRevision,
            request.ProjectSessionID, request.CoreLayerContext);

        ProjectConnectionInspection inspection = InspectCore(ToInspectRequest(request), state);
        if (!inspection.CanCommit)
            throw new ArgumentException(inspection.ValidationMessage ?? "The connection change is invalid.");
        if (!string.Equals(inspection.SourceReferencesSHA256,
                request.ExpectedSourceReferencesSHA256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inspection.ConnectionSHA256, request.ExpectedConnectionSHA256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inspection.SourceSHA256, request.ExpectedSourceSHA256, StringComparison.OrdinalIgnoreCase) ||
            (request.Action == "add" &&
             !string.Equals(inspection.NormalizedName, request.ExpectedNormalizedName, StringComparison.Ordinal)))
            throw new SourceFileChangedException();

        ProjectConnectionIdentity? before = inspection.Before;
        EnsureSourceReferencesUnchanged(state);
        EnsureTargetSourceUnchanged(request, state, inspection);
        string committedXml = ApplyChange(state, request, inspection.NormalizedName!);
        byte[] committedBytes = new UTF8Encoding(false, true).GetBytes(committedXml);
        string committedRevision = HashBytes(committedBytes);
        string committedXmlSHA = HashText(committedXml);
        if (!committedXmlSHA.Equals(committedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved project bytes do not match the committed XML.");

        string? stagedProjectPath = null;
        bool projectPublished = false;
        try
        {
            EnsureSnapshotUnchanged(state.Snapshot);
            EnsureSourceReferencesUnchanged(state);
            EnsureTargetSourceUnchanged(request, state, inspection);
            stagedProjectPath = WriteProjectTemp(state.Snapshot.Path, committedBytes);
            EnsureSnapshotUnchanged(state.Snapshot);
            EnsureSourceReferencesUnchanged(state);
            EnsureTargetSourceUnchanged(request, state, inspection);
            File.Move(stagedProjectPath, state.Snapshot.Path, overwrite: true);
            stagedProjectPath = null;
            projectPublished = true;

            EnsureCommittedSnapshot(state.Snapshot.Path, committedXml, committedRevision, committedXmlSHA);
            ProjectWorkspaceResponse reopened = ProjectWorkspaceService.Open(
                new ProjectWorkspaceOpenRequest(state.Snapshot.Path));
            if (!reopened.Success || !SamePath(reopened.ProjectPath, state.Snapshot.Path) ||
                !reopened.Revision.Equals(committedRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(reopened.ProjectXml, committedXml, StringComparison.Ordinal))
                throw new IOException("The saved connection project could not be reopened with the committed revision.");

            Project readbackProject = OpenProjectWithoutSaving(state.Snapshot.Path);
            try
            {
                XElement readbackRoot = ReadProjectRoot(reopened.ProjectXml);
                ConnectionRow[] readbackRows = ReadRows(readbackRoot, readbackProject);
                ProjectConnectionIdentity? after = FindAfterIdentity(readbackRows, request.Action,
                    inspection.NormalizedName!);
                VerifyAfterIdentity(request, before, after, inspection);
                VerifyCatalogReadback(reopened, request, inspection.NormalizedName!);
                EnsureCommittedSnapshot(state.Snapshot.Path, committedXml, committedRevision, committedXmlSHA);

                ProjectConnectionSourceCapture changedSource = BuildSourceCapture(request.Kind,
                    inspection.CanonicalPath, inspection.SourceSHA256, inspection.SourceExists,
                    inspection.SourceFileCount);
                CoreLayerRequestContext? rebasedContext = request.CoreLayerContext is null
                    ? null
                    : RebaseCoreLayerContext(request.CoreLayerContext, committedRevision);
                var projectReceipt = new ProjectConnectionSnapshot(true, reopened.ProjectPath,
                    reopened.ProjectXml, reopened.Revision, committedXmlSHA,
                    request.ProjectSessionID, rebasedContext);
                string afterReferencesSHA = CaptureSourceReferences(readbackProject,
                    readbackRoot, readbackRows);
                return new(true, request.Action, request.Kind, projectReceipt,
                    state.Snapshot.Revision, before, after, changedSource,
                    state.SourceReferencesSHA256, afterReferencesSHA);
            }
            finally { DisposeProject(readbackProject); }
        }
        catch (Exception error)
        {
            if (projectPublished)
            {
                string rollback = RollbackProject(state.Snapshot, committedRevision, committedBytes);
                if (rollback.Length > 0)
                    throw new IOException("The connection transaction failed after publication. " + rollback, error);
            }
            throw;
        }
        finally
        {
            if (stagedProjectPath is not null) TryDeleteFile(stagedProjectPath);
        }
    }

    private static ProjectConnectionInspection InspectCore(ProjectConnectionInspectRequest request,
        ProjectState state)
    {
        ConnectionRow? before = null;
        SourceCapture targetSource = new(null, null, false, 0);
        string? canonicalPath = null;
        string? normalizedName = null;
        try
        {
            ValidateActionAndKind(request.Action, request.Kind);
            if (request.Action is "update" or "remove")
            {
                string existingName = RequiredName(request.ConnectionName, "Select an existing connection.");
                ConnectionRow[] matches = state.Rows.Where(row =>
                    string.Equals(row.Name, existingName, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 || matches[0].Model is null)
                    throw new ArgumentException("The selected connection is missing, ambiguous, or unsupported by the retained Project model.");
                before = matches[0];
                if (request.Action == "remove")
                {
                    ValidateExistingKind(request.Kind, before, state.Project);
                    targetSource = CaptureConnectionSource(before.Model!);
                    canonicalPath = targetSource.Path;
                    normalizedName = before.Name;
                }
                else if (before.Model is not FileExternalConnection && before.Model is not DirectoryExternalConnection)
                    throw new ArgumentException("The selected connection type cannot be updated by this workflow.");
                else
                    ValidateExistingKind(request.Kind, before, state.Project);
            }

            if (request.Action is "add" or "update")
            {
                string name = RequiredName(request.Name, "Enter a connection name.");
                string path = RequiredPath(request.Path);
                canonicalPath = CanonicalPath(path);
                targetSource = CaptureRequestedSource(request.Kind, state.Project,
                    canonicalPath, request.ResultName, request.SearchPattern, request.Recurse);
                if (!targetSource.Exists)
                    throw new ArgumentException("The connected source must exist and be readable.");
                if (request.Action == "add")
                {
                    normalizedName = state.Project.ConnectionSource.UniqueFileConnectionName(name);
                }
                else
                {
                    if (state.Rows.Any(row => !ReferenceEquals(row, before) &&
                            string.Equals(row.Name, name, StringComparison.Ordinal)))
                        throw new ArgumentException("Another connection already has this name.");
                    normalizedName = name;
                }
                ValidateConnectionFields(name, request.Description, request.Kind,
                    request.SearchPattern, request.Recurse);
            }

            if (request.Action == "remove")
                ValidateConnectionFields(before!.Name, before.Identity.Description, request.Kind,
                    request.SearchPattern, request.Recurse);

            if (request.Kind == "result" && request.Action is "add" or "update")
                EnsureResultOwnership(state.Project, canonicalPath!, request.ResultName);
            return new(true, null, request.Action, request.Kind, state.Snapshot.Path,
                state.Snapshot.Revision, state.Snapshot.XmlSHA, request.ProjectSessionID,
                before?.Name, request.Name, canonicalPath, before?.Identity.ConnectionSHA256,
                targetSource.Revision, targetSource.Exists, targetSource.FileCount, state.SourceReferencesSHA256,
                normalizedName, before?.Identity, PreviewAfter(request, normalizedName!, canonicalPath!, targetSource));
        }
        catch (SourceFileChangedException) { throw; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(false, error.Message, request.Action, request.Kind, state.Snapshot.Path,
                state.Snapshot.Revision, state.Snapshot.XmlSHA, request.ProjectSessionID,
                before?.Name, request.Name, canonicalPath, before?.Identity.ConnectionSHA256,
                targetSource.Revision, targetSource.Exists, targetSource.FileCount, state.SourceReferencesSHA256,
                normalizedName, before?.Identity, null);
        }
    }

    private static ProjectConnectionIdentity? PreviewAfter(ProjectConnectionInspectRequest request,
        string normalizedName, string path, SourceCapture source)
    {
        if (request.Action == "remove") return null;
        string type = request.Kind == "directory" ? DirectoryExternalConnection.TypeName : FileExternalConnection.TypeName;
        XElement xml = CreateConnection(request.ProjectPath, request.Kind, normalizedName, path,
            request.Description, request.SearchPattern, request.Recurse, type).SaveXML();
        return new(normalizedName, type, path, request.Description ?? "",
            HashText(xml.ToString(SaveOptions.DisableFormatting)), source.Path,
            source.Revision, source.Exists);
    }

    private static string ApplyChange(ProjectState state, ProjectConnectionCommitRequest request,
        string normalizedName)
    {
        XDocument document = new(state.Document);
        XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
        XName connectionsName = root.Name.Namespace + Project.Keys.ConnectionGroup;
        XElement[] sections = root.Elements(connectionsName).ToArray();
        if (sections.Length > 1) throw new InvalidDataException("The project XML has duplicate Connections sections.");
        XElement? connections = sections.SingleOrDefault();
        if (request.Action == "add" && connections is null)
        {
            connections = new XElement(connectionsName);
            AppendPreservingWhitespace(root, connections);
        }

        if (request.Action is "update" or "remove")
        {
            if (connections is null) throw new SourceFileChangedException();
            XElement[] matches = connections.Elements().Where(element =>
                element.Name.LocalName == ExternalConnectionBase.XmlElementName &&
                string.Equals((string?)element.Attribute("Name"), request.ConnectionName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !HashText(matches[0].ToString(SaveOptions.DisableFormatting))
                    .Equals(request.ExpectedConnectionSHA256, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            if (request.Action == "remove") matches[0].Remove();
            else PatchConnection(matches[0], CreateConnection(request.ProjectPath, request.Kind,
                normalizedName, CanonicalPath(request.Path!), request.Description, request.SearchPattern,
                request.Recurse, request.Kind == "directory" ? DirectoryExternalConnection.TypeName :
                    FileExternalConnection.TypeName).SaveXML());
        }
        else
        {
            if (connections is null) throw new InvalidOperationException("The Connections section was not created.");
            if (connections.Elements().Any(element => element.Name.LocalName == ExternalConnectionBase.XmlElementName &&
                    string.Equals((string?)element.Attribute("Name"), normalizedName, StringComparison.Ordinal)))
                throw new SourceFileChangedException();
            connections.Add(CreateConnection(request.ProjectPath, request.Kind, normalizedName,
                CanonicalPath(request.Path!), request.Description, request.SearchPattern, request.Recurse,
                request.Kind == "directory" ? DirectoryExternalConnection.TypeName :
                    FileExternalConnection.TypeName).SaveXML());
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

    private static ExternalConnectionBase CreateConnection(string projectPath, string kind,
        string name, string path, string? description, string? searchPattern, bool recurse, string type)
    {
        if (type == DirectoryExternalConnection.TypeName)
        {
            var directory = new DirectoryExternalConnection(projectPath)
            {
                Name = name,
                Description = description ?? "",
                SearchPattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern,
                Recurse = recurse
            };
            directory.SetConnectedPathname(path);
            return directory;
        }

        FileExternalConnection file = FileExternalConnection.CreateAbsoluteFileConnection(name, path, projectPath);
        file.Description = description ?? "";
        return file;
    }

    private static void PatchConnection(XElement current, XElement desired)
    {
        XNamespace ns = current.Name.Namespace;
        current.SetAttributeValue("Name", (string?)desired.Attribute("Name"));
        current.SetAttributeValue("Type", (string?)desired.Attribute("Type"));
        string[] recognizedChildren = ["Description", "RelativePath", "AbsolutePath", "SearchPattern", "Recurse"];
        foreach (string childName in recognizedChildren)
        {
            XElement[] desiredChildren = desired.Elements().Where(child => child.Name.LocalName == childName).ToArray();
            XElement[] currentChildren = current.Elements().Where(child => child.Name.LocalName == childName).ToArray();
            if (desiredChildren.Length == 0)
            {
                foreach (XElement extra in currentChildren) extra.Remove();
                continue;
            }

            XElement? keep = currentChildren.FirstOrDefault();
            if (keep is null)
            {
                AppendPreservingWhitespace(current, new XElement(ns + childName, desiredChildren[0].Value));
                continue;
            }

            keep.Value = desiredChildren[0].Value;
            foreach (XElement duplicate in currentChildren.Skip(1)) duplicate.Remove();
        }
    }

    private static void AppendPreservingWhitespace(XContainer parent, XElement element)
    {
        XNode? last = parent.Nodes().LastOrDefault();
        if (last is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
        {
            string? indent = parent.Elements().LastOrDefault()?.PreviousNode is XText before &&
                string.IsNullOrWhiteSpace(before.Value) ? before.Value : null;
            trailing.AddBeforeSelf(new XText(indent ?? (trailing.Value + "  ")), element);
            return;
        }
        parent.Add(element);
    }

    private static ProjectState OpenState(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision)
    {
        ProjectSnapshot snapshot = ReadProjectSnapshot(projectPath, projectXml, projectXmlSHA, projectRevision);
        Project project = OpenProjectWithoutSaving(snapshot.Path);
        try
        {
            EnsureSnapshotUnchanged(snapshot);
            XDocument document = XDocument.Parse(snapshot.Xml, LoadOptions.PreserveWhitespace);
            XElement root = document.Root ?? throw new InvalidDataException("The project XML has no root element.");
            if (root.Name.LocalName != Project.Keys.Project)
                throw new InvalidDataException("The project XML root is not Project.");
            XElement[] sections = root.Elements().Where(element =>
                element.Name.LocalName == Project.Keys.ConnectionGroup).ToArray();
            if (sections.Length > 1)
                throw new InvalidDataException("The project XML contains duplicate Connections sections.");
            XElement? connections = sections.SingleOrDefault();
            ConnectionRow[] rows = ReadRows(root, project);
            string sourceReferencesSHA = CaptureSourceReferences(project, root, rows);
            return new(snapshot, project, document, root, connections, rows, sourceReferencesSHA);
        }
        catch
        {
            DisposeProject(project);
            throw;
        }
    }

    private static ProjectSnapshot ReadProjectSnapshot(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision)
    {
        string canonicalPath = Path.GetFullPath(projectPath);
        if (!Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(canonicalPath).Equals(Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved .ras project before changing connections.");
        if (!IsSHA256(projectRevision) || !IsSHA256(projectXmlSHA) || projectXml is null ||
            Encoding.UTF8.GetByteCount(projectXml) > MaximumProjectXmlBytes ||
            !HashText(projectXml).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
        if (beforePath is null || beforeRevision is null) throw new FileNotFoundException("The project file was not found.", canonicalPath);
        byte[] bytes = File.ReadAllBytes(canonicalPath);
        string xml = File.ReadAllText(canonicalPath);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(canonicalPath);
        if (afterPath is null || afterRevision is null || !SamePath(beforePath, afterPath) ||
            !beforeRevision.Equals(afterRevision, StringComparison.Ordinal) ||
            !SamePath(beforePath, canonicalPath) ||
            !beforeRevision.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(xml, projectXml, StringComparison.Ordinal) ||
            !HashText(xml).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        XElement root = ReadProjectRoot(xml);
        if (root.Name.LocalName != Project.Keys.Project)
            throw new InvalidDataException("The project XML root is not Project.");
        return new(beforePath, xml, bytes, beforeRevision, HashText(xml));
    }

    private static Project OpenProjectWithoutSaving(string path)
    {
        var project = new Project(path);
        if (!SamePath(project.Filename, path))
        {
            DisposeProject(project);
            throw new InvalidDataException("The Project model resolved a different .ras path.");
        }
        return project;
    }

    private static ConnectionRow[] ReadRows(XElement root, Project project)
    {
        XElement[] rawRows = root.Elements().FirstOrDefault(element =>
                element.Name.LocalName == Project.Keys.ConnectionGroup)?.Elements().Where(element =>
                element.Name.LocalName == ExternalConnectionBase.XmlElementName).ToArray() ?? [];
        var available = project.ConnectionSource.Connections.ToList();
        var rows = new List<ConnectionRow>(rawRows.Length);
        foreach (XElement raw in rawRows)
        {
            string name = (string?)raw.Attribute("Name") ?? "";
            string type = (string?)raw.Attribute("Type") ?? "";
            ExternalConnectionBase? model = available.FirstOrDefault(connection =>
                string.Equals(connection.Name, name, StringComparison.Ordinal) &&
                string.Equals(connection.SaveXML().Attribute("Type")?.Value, type, StringComparison.Ordinal));
            if (model is not null) available.Remove(model);
            SourceCapture source = model is null
                ? CaptureUnknownConnectionSource(raw, project.Filename)
                : CaptureConnectionSource(model);
            string? sourceSHA = source.Revision;
            var identity = new ProjectConnectionIdentity(name, type, source.Path,
                model?.Description ?? raw.Elements().FirstOrDefault(element => element.Name.LocalName == "Description")?.Value ?? "",
                HashText(raw.ToString(SaveOptions.DisableFormatting)), source.Path, sourceSHA, source.Exists,
                (model as DirectoryExternalConnection)?.SearchPattern,
                model is DirectoryExternalConnection directory ? directory.Recurse : null);
            rows.Add(new(raw, name, type, model, identity));
        }
        return rows.ToArray();
    }

    private static string CaptureSourceReferences(Project project, XElement root, ConnectionRow[] rows)
    {
        var captured = new List<object>(rows.Length);
        foreach (ConnectionRow row in rows.OrderBy(item => item.Name, StringComparer.Ordinal)
                     .ThenBy(item => item.Type, StringComparer.Ordinal)
                     .ThenBy(item => item.Identity.ConnectionSHA256, StringComparer.Ordinal))
        {
            SourceCapture source = row.Model is null
                ? CaptureUnknownConnectionSource(row.Xml, project.Filename)
                : CaptureConnectionSource(row.Model);
            captured.Add(new
            {
                row.Name,
                row.Type,
                connectionSHA256 = row.Identity.ConnectionSHA256,
                source.Path,
                sourceSHA256 = source.Revision,
                source.Exists,
                source.FileCount
            });
        }
        return HashText(JsonSerializer.Serialize(captured, BackendJson.Options));
    }

    private static SourceCapture CaptureUnknownConnectionSource(XElement row, string projectPath)
    {
        string? relative = row.Descendants().FirstOrDefault(element => element.Name.LocalName == "RelativePath")?.Value;
        string? absolute = row.Descendants().FirstOrDefault(element => element.Name.LocalName == "AbsolutePath")?.Value;
        string? path = null;
        if (!string.IsNullOrWhiteSpace(relative))
        {
            try
            {
                path = Path.IsPathFullyQualified(relative) ? relative :
                    Path.GetFullPath(relative, Path.GetDirectoryName(projectPath)!);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        }
        if (path is null && !string.IsNullOrWhiteSpace(absolute))
        {
            try { path = Path.GetFullPath(absolute); }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        }
        if (path is not null && File.Exists(path)) return CaptureFile(path);
        if (path is not null && Directory.Exists(path)) return CaptureDirectory(path, "*", recurse: false);
        return new(path, null, false, 0);
    }

    private static SourceCapture CaptureConnectionSource(ExternalConnectionBase connection)
    {
        switch (connection)
        {
            case FileExternalConnection file:
                if (file.TryGetFile(out string filePath)) return CaptureFile(filePath);
                return new(ResolveUnfoundPath(file), null, false, 0);
            case DirectoryExternalConnection directory:
                if (directory.TryGetDirectory(out string directoryPath))
                    return CaptureDirectory(directoryPath, directory.SearchPattern, directory.Recurse);
                return new(ResolveUnfoundPath(directory), null, false, 0);
            default:
                return new(null, null, false, 0);
        }
    }

    private static SourceCapture CaptureRequestedSource(string kind, Project project, string path,
        string? resultName, string? searchPattern, bool recurse)
    {
        if (kind == "directory") return CaptureDirectory(path, searchPattern, recurse);
        if (kind == "result") EnsureResultOwnership(project, path, resultName);
        return CaptureFile(path);
    }

    private static SourceCapture CaptureFile(string path)
    {
        string canonical = Path.GetFullPath(path);
        if (!File.Exists(canonical)) return new(canonical, null, false, 0);
        return new(canonical, HashFile(canonical), true, 1);
    }

    private static SourceCapture CaptureDirectory(string path, string? searchPattern, bool recurse)
    {
        string canonical = Path.GetFullPath(path);
        if (!Directory.Exists(canonical)) return new(canonical, null, false, 0);
        string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern;
        if (pattern.Contains('/') || pattern.Contains('\\'))
            throw new ArgumentException("Directory filename pattern cannot include path separators.");
        string[] files = Directory.EnumerateFiles(canonical, pattern, new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        }).OrderBy(file => Path.GetRelativePath(canonical, file), StringComparer.Ordinal).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            byte[] relative = Encoding.UTF8.GetBytes(Path.GetRelativePath(canonical, file)
                .Replace(Path.DirectorySeparatorChar, '/'));
            hash.AppendData(BitConverter.GetBytes(relative.Length));
            hash.AppendData(relative);
            using FileStream stream = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(stream));
        }
        return new(canonical, Convert.ToHexString(hash.GetHashAndReset()), true, files.Length);
    }

    private static string? ResolveUnfoundPath(PathExternalConnection connection)
    {
        try
        {
            string relative = connection.RelativePath ?? "";
            if (!string.IsNullOrWhiteSpace(relative))
                return Path.GetFullPath(relative, connection.PathRelativeSourceDirectory);
            if (!string.IsNullOrWhiteSpace(connection.AbsolutePath))
                return Path.GetFullPath(connection.AbsolutePath);
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        return null;
    }

    private static ProjectConnectionIdentity? FindAfterIdentity(ConnectionRow[] rows, string action,
        string normalizedName) => action == "remove"
        ? null
        : rows.SingleOrDefault(row => string.Equals(row.Name, normalizedName, StringComparison.Ordinal))?.Identity;

    private static CoreLayerRequestContext RebaseCoreLayerContext(CoreLayerRequestContext context,
        string projectRevision) => context with
    {
        ProjectRevision = projectRevision,
        IdentityToken = CoreLayerImportService.ContextIdentityToken(context.ProjectSessionID,
            context.ProjectPath, projectRevision, context.References, context.SessionHdfReferences)
    };

    private static void VerifyAfterIdentity(ProjectConnectionCommitRequest request,
        ProjectConnectionIdentity? before, ProjectConnectionIdentity? after,
        ProjectConnectionInspection inspection)
    {
        if (request.Action == "remove")
        {
            if (after is not null || before is null) throw new InvalidDataException("The removed connection remained in the reopened project.");
            return;
        }
        if (after is null || after.Name != inspection.NormalizedName ||
            after.Type != (request.Kind == "directory" ? DirectoryExternalConnection.TypeName : FileExternalConnection.TypeName) ||
            !SamePath(after.Path ?? "", inspection.CanonicalPath ?? "") ||
            !string.Equals(after.Description, request.Description ?? "", StringComparison.Ordinal) ||
            !string.Equals(after.SourceSHA256, inspection.SourceSHA256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The committed connection identity did not read back exactly.");
    }

    private static void VerifyCatalogReadback(ProjectWorkspaceResponse project,
        ProjectConnectionCommitRequest request, string normalizedName)
    {
        CatalogTreeSnapshot snapshot = CatalogTreeService.LoadCatalog(new CatalogTreeRequest(
            project.ProjectPath, project.ProjectXml, Mode: "Catalog",
            ProjectSessionID: request.ProjectSessionID.ToString("D"),
            CoreLayerContext: request.CoreLayerContext is null ? null :
                RebaseCoreLayerContext(request.CoreLayerContext, project.Revision)));
        CatalogTreeNode[] matches = Walk(snapshot.Roots).Where(node =>
            node.Name == normalizedName && node.SourceClass is "ExternalFileConnectionCatalogNode" or "ExternalConnectionCatalogNode").ToArray();
        int expectedCount = request.Action == "remove" ? 0 : 1;
        if (matches.Length != expectedCount)
            throw new InvalidDataException("The committed external connection was not confirmed by catalog.tree.load.");
        if (request.Action == "remove")
        {
            if (matches.Length != 0) throw new InvalidDataException("The removed external connection remained in catalog.tree.load.");
            if (request.ConnectionName is null || Walk(snapshot.Roots).Any(node =>
                    node.Name == request.ConnectionName && node.SourceClass is "ExternalFileConnectionCatalogNode" or "ExternalConnectionCatalogNode"))
                throw new InvalidDataException("The removed connection identity remained in the project catalog.");
        }
    }

    private static IEnumerable<CatalogTreeNode> Walk(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 })
                foreach (CatalogTreeNode child in Walk(node.Children)) yield return child;
        }
    }

    private static void EnsureResultOwnership(Project project, string path, string? resultName)
    {
        string expected = Path.GetFullPath(path);
        Result[] matches = project.Results.Where(result => SamePath(result.Filename, expected) &&
            string.Equals(result.Name, resultName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new ArgumentException("Choose exactly one Result HDF that belongs to the current project.");
        if (!File.Exists(expected)) throw new SourceFileChangedException();
    }

    private static void ValidateExistingKind(string kind, ConnectionRow row, Project project)
    {
        bool valid = kind switch
        {
            "file" => row.Model is FileExternalConnection,
            "directory" => row.Model is DirectoryExternalConnection,
            "result" => row.Model is FileExternalConnection && row.Identity.Path is { } path &&
                project.Results.Any(result => SamePath(result.Filename, path)),
            _ => false
        };
        if (!valid) throw new ArgumentException("The selected connection does not match the requested connection kind.");
    }

    private static void ValidateSessionAndContext(string projectPath, string projectRevision,
        Guid sessionID, CoreLayerRequestContext? context)
    {
        if (sessionID == Guid.Empty) throw new ArgumentException("A non-empty project session ID is required.");
        if (context is null) return;
        if (!Guid.TryParse(context.ProjectSessionID, out Guid contextSession) || contextSession != sessionID ||
            !SamePath(context.ProjectPath, projectPath) ||
            !string.Equals(context.ProjectRevision, projectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        CoreLayerImportService.ValidateContext(context, projectPath);
    }

    private static void ValidateProjectRequest(ProjectConnectionInspectRequest request)
    {
        ValidateProjectFields(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID);
    }

    private static void ValidateProjectRequest(ProjectConnectionCommitRequest request)
    {
        ValidateProjectFields(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID);
    }

    private static ProjectConnectionInspectRequest ToInspectRequest(ProjectConnectionCommitRequest request) => new(
        request.Action, request.Kind, request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
        request.ProjectRevision, request.ProjectSessionID, request.ConnectionName, request.Name,
        request.Path, request.Description, request.ResultName, request.SearchPattern,
        request.Recurse, request.CoreLayerContext);

    private static void ValidateProjectFields(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision, Guid sessionID)
    {
        if (!Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(projectPath).Equals(Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved .ras project before changing connections.");
        if (sessionID == Guid.Empty) throw new ArgumentException("A non-empty project session ID is required.");
        if (projectXml is null || Encoding.UTF8.GetByteCount(projectXml) > MaximumProjectXmlBytes ||
            !IsSHA256(projectRevision) || !IsSHA256(projectXmlSHA) ||
            !HashText(projectXml).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void ValidateCommitCapture(ProjectConnectionCommitRequest request)
    {
        ValidateActionAndKind(request.Action, request.Kind);
        if (!IsSHA256(request.ExpectedSourceReferencesSHA256))
            throw new ArgumentException("Inspect the project connections before committing.");
        if (request.Action is "update" or "remove" && !IsSHA256(request.ExpectedConnectionSHA256))
            throw new ArgumentException("Inspect the selected connection before changing it.");
        if (request.Action != "remove" && !IsSHA256(request.ExpectedSourceSHA256))
            throw new ArgumentException("Inspect the target source file or directory before committing.");
        if (request.ExpectedSourceSHA256 is { Length: > 0 } revision && !IsSHA256(revision))
            throw new ArgumentException("The inspected source revision is invalid.");
        if (request.Action == "add" && string.IsNullOrWhiteSpace(request.ExpectedNormalizedName))
            throw new ArgumentException("Use the normalized connection name returned by inspection.");
    }

    private static void ValidateActionAndKind(string action, string kind)
    {
        if (action is not ("add" or "update" or "remove"))
            throw new ArgumentException("Choose add, update, or remove.");
        if (kind is not ("file" or "directory" or "result"))
            throw new ArgumentException("Choose a File, Directory, or Result connection.");
    }

    private static void ValidateConnectionFields(string name, string? description,
        string kind, string? searchPattern, bool recurse)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength ||
            name.IndexOfAny(['\0', '\r', '\n', '/', '\\']) >= 0)
            throw new ArgumentException("Connection names must be non-empty, at most 256 characters, and cannot contain path separators.");
        if (description is not null && (Encoding.UTF8.GetByteCount(description) > MaximumDescriptionBytes ||
            description.IndexOf('\0') >= 0))
            throw new ArgumentException("The connection description is too large or contains invalid characters.");
        if (kind == "directory")
        {
            string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern;
            if (pattern.IndexOfAny(['\0', '\r', '\n', '/', '\\']) >= 0)
                throw new ArgumentException("Directory filename patterns cannot include path separators.");
            _ = recurse;
        }
        else if (kind == "result" && searchPattern is not null)
            throw new ArgumentException("Result connections do not accept directory search options.");
    }

    private static string RequiredName(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();

    private static string RequiredPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Select a source file or directory.") : value;

    private static string CanonicalPath(string path) => Path.GetFullPath(path);

    private static ProjectConnectionSourceCapture BuildSourceCapture(string kind,
        string? path, string? revision, bool exists, int fileCount) => new(path, revision, exists,
        kind == "directory" && exists ? fileCount : exists ? 1 : 0);

    private static XElement ReadProjectRoot(string projectXml)
    {
        XDocument document = XDocument.Parse(projectXml, LoadOptions.PreserveWhitespace);
        return document.Root ?? throw new InvalidDataException("The project XML has no root element.");
    }

    private static void EnsureSnapshotUnchanged(ProjectSnapshot snapshot)
    {
        (string? firstPath, string? firstRevision) = CatalogTreeService.TryReadStableRevision(snapshot.Path);
        if (firstPath is null || firstRevision is null || !SamePath(firstPath, snapshot.Path) ||
            !firstRevision.Equals(snapshot.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        byte[] bytes = File.ReadAllBytes(snapshot.Path);
        string xml = File.ReadAllText(snapshot.Path);
        (string? lastPath, string? lastRevision) = CatalogTreeService.TryReadStableRevision(snapshot.Path);
        if (lastPath is null || lastRevision is null || !SamePath(lastPath, snapshot.Path) ||
            !lastRevision.Equals(firstRevision, StringComparison.Ordinal) ||
            !bytes.AsSpan().SequenceEqual(snapshot.Bytes) ||
            !string.Equals(xml, snapshot.Xml, StringComparison.Ordinal) ||
            !HashText(xml).Equals(snapshot.XmlSHA, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void EnsureSourceReferencesUnchanged(ProjectState state)
    {
        string current = CaptureSourceReferences(state.Project, state.Root, state.Rows);
        if (!current.Equals(state.SourceReferencesSHA256, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void EnsureTargetSourceUnchanged(ProjectConnectionCommitRequest request,
        ProjectState state, ProjectConnectionInspection inspection)
    {
        SourceCapture current;
        if (request.Action == "remove")
        {
            ConnectionRow? selected = state.Rows.SingleOrDefault(row =>
                string.Equals(row.Name, request.ConnectionName, StringComparison.Ordinal));
            if (selected?.Model is null) throw new SourceFileChangedException();
            current = CaptureConnectionSource(selected.Model);
        }
        else
        {
            current = CaptureRequestedSource(request.Kind, state.Project,
                inspection.CanonicalPath ?? throw new SourceFileChangedException(),
                request.ResultName, request.SearchPattern, request.Recurse);
        }
        if (!SameOptionalPath(current.Path, inspection.CanonicalPath) ||
            current.Exists != inspection.SourceExists ||
            !string.Equals(current.Revision, inspection.SourceSHA256, StringComparison.OrdinalIgnoreCase) ||
            current.FileCount != inspection.SourceFileCount)
            throw new SourceFileChangedException();
    }

    private static void EnsureCommittedSnapshot(string path, string expectedXml,
        string expectedRevision, string expectedXmlSHA)
    {
        (string? firstPath, string? firstRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (firstPath is null || firstRevision is null || !SamePath(firstPath, path) ||
            !firstRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string xml = File.ReadAllText(path);
        (string? lastPath, string? lastRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (lastPath is null || lastRevision is null || !SamePath(lastPath, path) ||
            !lastRevision.Equals(firstRevision, StringComparison.Ordinal) ||
            !string.Equals(xml, expectedXml, StringComparison.Ordinal) ||
            !HashText(xml).Equals(expectedXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string RollbackProject(ProjectSnapshot source, string committedRevision,
        byte[] committedBytes)
    {
        try
        {
            (string? path, string? revision) = CatalogTreeService.TryReadStableRevision(source.Path);
            if (path is null || revision is null || !SamePath(path, source.Path) ||
                !revision.Equals(committedRevision, StringComparison.OrdinalIgnoreCase) ||
                !File.ReadAllBytes(source.Path).AsSpan().SequenceEqual(committedBytes))
                return "The project changed after publication and was left untouched.";
            string rollbackPath = WriteProjectTemp(source.Path, source.Bytes);
            try { File.Move(rollbackPath, source.Path, overwrite: true); }
            finally { TryDeleteFile(rollbackPath); }
            EnsureSnapshotUnchanged(source);
            return "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "The project rollback could not be confirmed: " + error.Message;
        }
    }

    private static string WriteProjectTemp(string path, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path)!, ".project-connection-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temp;
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool SamePath(string left, string right) => SourceFilePaths.SameSourceFile(left, right);
    private static bool SameOptionalPath(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) ? string.IsNullOrWhiteSpace(right) :
        !string.IsNullOrWhiteSpace(right) && SamePath(left, right);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ProjectState : IDisposable
    {
        private readonly Project project;
        public ProjectSnapshot Snapshot { get; }
        public XDocument Document { get; }
        public XElement Root { get; }
        public XElement? Connections { get; }
        public ConnectionRow[] Rows { get; }
        public string SourceReferencesSHA256 { get; }
        public Project Project => project;

        public ProjectState(ProjectSnapshot snapshot, Project project, XDocument document,
            XElement root, XElement? connections, ConnectionRow[] rows, string sourceReferencesSHA256)
        {
            Snapshot = snapshot;
            this.project = project;
            Document = document;
            Root = root;
            Connections = connections;
            Rows = rows;
            SourceReferencesSHA256 = sourceReferencesSHA256;
        }

        public void Dispose() => DisposeProject(project);
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false, true);
    }
}
