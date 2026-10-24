namespace Ras.Backend;

public sealed record ProjectLibraryCatalogRequest(bool DebugMode);

public sealed record ProjectLibraryProjectRow(
    string Id,
    string Title,
    string Description,
    DateTime? PublishDate,
    string[] Tags,
    string? ThumbnailUrl,
    string Kind);

public sealed record ProjectLibraryCatalogResponse(
    ProjectLibraryProjectRow[] Examples,
    ProjectLibraryProjectRow[] Synthetic);

public sealed record ProjectLibraryOpenRequest(
    string ProjectId,
    bool Synthetic,
    bool DebugMode,
    string DestinationDirectory);

public sealed record ProjectLibraryOpenResponse(string ProjectPath);
