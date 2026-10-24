using Utility.Files;

namespace Ras.Backend;

public sealed record SessionReferenceComposeRequest(
    string ProjectPath, string ProjectSessionID, string ProjectRevision,
    string ProjectXml, string ProjectXmlRevision,
    CoreLayerRequestContext? CoreLayerContext = null,
    CoreLayerReferenceCapture[]? AddCoreReferences = null,
    SessionHdfReferenceCapture[]? AddSessionHdfReferences = null,
    string[]? RemoveReferenceIDs = null);

public sealed record SessionReferenceComposeReceipt(
    string ProjectPath, string ProjectSessionID, string ProjectRevision,
    string? PreviousContextIdentityToken, CoreLayerRequestContext NextContext);

/// <summary>Creates a verified next capability without registering it or changing project/map files.</summary>
public static class SessionReferenceContextService
{
    public static SessionReferenceComposeReceipt Compose(SessionReferenceComposeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(request.ProjectSessionID, out _) || !IsRevision(request.ProjectRevision) ||
            !IsRevision(request.ProjectXmlRevision) || string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("A saved project session, XML snapshot and exact revisions are required.");
        if (!Path.IsPathFullyQualified(request.ProjectPath) ||
            !Path.GetExtension(request.ProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an absolute saved project path.");
        string projectPath = Path.GetFullPath(request.ProjectPath);
        CoreLayerRequestContext? previous = request.CoreLayerContext;
        if (previous is not null)
        {
            // Shape validation deliberately allows removing an inaccessible stale source.
            CoreLayerImportService.ValidateContextShape(previous, projectPath);
            if (previous.ProjectSessionID != request.ProjectSessionID ||
                !string.Equals(previous.ProjectRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
        string[] removals = request.RemoveReferenceIDs ?? [];
        var known = new HashSet<string>((previous?.References ?? []).Select(item => item.ReferenceID)
            .Concat((previous?.SessionHdfReferences ?? []).Select(item => item.ReferenceID)), StringComparer.OrdinalIgnoreCase);
        if (removals.Length > 512 || removals.Any(id => string.IsNullOrWhiteSpace(id) || !known.Contains(id)) ||
            removals.Distinct(StringComparer.OrdinalIgnoreCase).Count() != removals.Length)
            throw new ArgumentException("Choose unique reference IDs from the current session.");
        var removed = new HashSet<string>(removals, StringComparer.OrdinalIgnoreCase);
        CoreLayerReferenceCapture[] coreAdditions = request.AddCoreReferences ?? [];
        SessionHdfReferenceCapture[] hdfAdditions = request.AddSessionHdfReferences ?? [];
        if (coreAdditions.Any(item => item is null) || hdfAdditions.Any(item => item is null))
            throw new ArgumentException("A reference capture is missing.");
        CoreLayerReferenceCapture[] core = Merge((previous?.References ?? []).Where(item => !removed.Contains(item.ReferenceID)),
            coreAdditions, item => item.ReferenceID);
        SessionHdfReferenceCapture[] hdf = Merge((previous?.SessionHdfReferences ?? []).Where(item => !removed.Contains(item.ReferenceID)),
            hdfAdditions, item => item.ReferenceID);
        if (core.Length + hdf.Length > 512)
            throw new NotSupportedException("This project session has too many external references.");
        var sourcePaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string path in core.Select(item => item.CanonicalSourcePath).Concat(hdf.Select(item => item.CanonicalSourcePath)))
            if (string.IsNullOrWhiteSpace(path) || !sourcePaths.Add(SourceFilePaths.Canonical(path)))
                throw new ArgumentException("The same source cannot be registered more than once in a project session.");
        string revision = request.ProjectRevision.ToUpperInvariant();
        string identity = CoreLayerImportService.ContextIdentityToken(request.ProjectSessionID, projectPath, revision, core, hdf);
        var next = new CoreLayerRequestContext(request.ProjectSessionID, projectPath, revision, core, identity)
        { SessionHdfReferences = hdf };
        // Re-open with this exact capability to validate source SHA, type, names,
        // project ownership and XML. No files or global session state are written.
        var project = CoreLayerImportService.OpenSessionProject(projectPath, request.ProjectXml,
            request.ProjectSessionID, revision, request.ProjectXmlRevision, next);
        try { CoreLayerImportService.ValidateContext(next, projectPath); }
        finally { CoreLayerImportService.DisposeProject(project); }
        return new(projectPath, request.ProjectSessionID, revision, previous?.IdentityToken, next);
    }

    private static T[] Merge<T>(IEnumerable<T> previous, T[] additions, Func<T, string> identity) where T : class
    {
        var values = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (T item in previous.Concat(additions))
        {
            string key = identity(item);
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A reference identity is missing.");
            if (values.TryGetValue(key, out T? existing) && !Equals(existing, item))
                throw new ArgumentException("Conflicting captures carry the same reference identity.");
            values[key] = item;
        }
        return values.Values.ToArray();
    }

    private static bool IsRevision(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
