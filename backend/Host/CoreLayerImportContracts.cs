namespace Ras.Backend;

/// <summary>
/// An explicitly authorized, session-scoped external core-layer reference.
/// The original project format does not persist this path; callers must carry
/// the capture on every backend request that rebuilds the current Project.
/// </summary>
public sealed record CoreLayerReferenceCapture(
    string ReferenceID,
    string ProjectSessionID,
    string ProjectPath,
    string Kind,
    string SourcePath,
    string CanonicalSourcePath,
    string SourceRevision,
    string Name,
    bool SessionOnly = true);

/// <summary>
/// An explicitly selected Plan, Result, or Terrain HDF attached to one open
/// project session. This is separate from CoreLayerReferenceCapture so the
/// Geometry/Boundary Condition whitelist and recovery contract stay unchanged.
/// </summary>
public sealed record SessionHdfReferenceCapture(
    string ReferenceID,
    string ProjectSessionID,
    string ProjectPath,
    string Kind,
    string SourcePath,
    string CanonicalSourcePath,
    string SourceRevision,
    string Name,
    string? ResultPlanName = null,
    bool SessionOnly = true);

/// <summary>
/// Exact owner of one embedded-Plan Result in the original name-grouped
/// MultiResult tree. The embedded Plan name is a grouping label only; the
/// reference ID, Result path, source SHA, and containing Core context identify
/// the owner. This never promotes the embedded Plan into Project.Plans.
/// </summary>
public sealed record SessionHdfResultOwner(
    string ReferenceID,
    string ProjectSessionID,
    string ProjectPath,
    string ResultPath,
    string ResultRevision,
    string EmbeddedPlanName,
    string CoreLayerContextIdentityToken);

/// <summary>
/// Immutable capability context carried by every request that rebuilds the
/// current in-memory Project. The identity token prevents capture fields from
/// being mixed across maps or project sessions; it is not a persisted owner
/// marker and must be supplied explicitly by the caller.
/// </summary>
public sealed record CoreLayerRequestContext(
    string ProjectSessionID,
    string ProjectPath,
    string? ProjectRevision,
    CoreLayerReferenceCapture[] References,
    string IdentityToken)
{
    public SessionHdfReferenceCapture[] SessionHdfReferences { get; init; } = [];
}

public sealed record SessionHdfImportRequest(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXml,
    string ProjectXmlRevision,
    string SourcePath,
    string SourceRevision,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record SessionHdfImportInspection(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXmlRevision,
    string SourcePath,
    string CanonicalSourcePath,
    string SourceRevision,
    string Kind,
    string Name,
    string? ResultPlanName,
    string Description,
    long FileSize,
    bool SessionOnly,
    SessionHdfReferenceCapture Reference);

public sealed record CoreLayerImportRequest(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXml,
    string ProjectXmlRevision,
    string SourcePath,
    string SourceRevision,
    string? Action = null,
    CoreLayerRequestContext? CoreLayerContext = null,
    string? ExpectedSourceCanonicalPath = null);

public sealed record CoreLayerImportInspection(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXmlRevision,
    string SourcePath,
    string CanonicalSourcePath,
    string SourceRevision,
    string Kind,
    string Name,
    string Description,
    long FileSize,
    string[] AvailableActions,
    bool ReferenceIsSessionOnly);

public sealed record CoreLayerImportReceipt(
    string Action,
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXmlRevision,
    string SourcePath,
    string CanonicalSourcePath,
    string SourceRevision,
    string Kind,
    string Name,
    CoreLayerReferenceCapture? Reference,
    string? ImportedAssetPath,
    string? ImportedAssetRevision,
    bool ReferenceIsSessionOnly);

/// <summary>
/// Requests an explicit recovery read for one existing session-only reference.
/// The target source path is taken from that exact captured reference; callers
/// cannot redirect a reload to a different file.
/// </summary>
public sealed record CoreLayerReferenceReloadRequest(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    CoreLayerRequestContext CoreLayerContext,
    string TargetReferenceID);

public sealed record CoreLayerReferenceReloadReceipt(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string PreviousContextIdentityToken,
    string PreviousReferenceID,
    CoreLayerReferenceCapture Reference,
    CoreLayerRequestContext NextContext);
