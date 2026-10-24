using System.Text.Json.Serialization;

namespace Ras.Backend;

public sealed record WorkspaceUpdateCredentials(string AccessKey, string SecretKey);

public sealed record WorkspaceUpdateCatalogRequest(string Channel, string Branch,
    string InstalledVersion, bool DebugMode, WorkspaceUpdateCredentials? Credentials);

public sealed record WorkspaceUpdateRelease(
    string ID,
    string Version,
    DateTimeOffset? PublicationDate,
    string Notes,
    string? NotesUrl,
    string Platform,
    string Architecture,
    string? PackageFilename,
    [property: JsonPropertyName("packageSha256")] string? PackageSHA256,
    long? PackageLength,
    string? PackageManifestKey,
    string PackageStatus);

public sealed record WorkspaceUpdateCatalogResponse(
    SourceEditorChoice[] Channels,
    string[] Branches,
    string SelectedChannel,
    string SelectedBranch,
    bool CanChangeChannel,
    bool HasDeveloperCredentials,
    string CredentialStatus,
    WorkspaceUpdateRelease[] Releases,
    bool? IsUpToDate);

public sealed record WorkspaceUpdatePackagePrepareRequest(
    string Channel,
    string Branch,
    bool DebugMode,
    string ReleaseID,
    string PackageManifestKey,
    string PackageFilename,
    string PackageSHA256,
    WorkspaceUpdateCredentials? Credentials);

public sealed record WorkspaceUpdatePackageReceipt(
    string Path,
    string Filename,
    [property: JsonPropertyName("sha256")] string SHA256,
    long Length,
    string Platform,
    string Architecture);
