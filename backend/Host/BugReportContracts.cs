namespace Ras.Backend;

public sealed record BugReportComponentsRequest();
public sealed record BugReportComponent(string Id, string Name, bool AttachesGeometry);
public sealed record BugReportComponentsResult(string RasVersion, string KnownIssuesUrl,
    BugReportComponent[] Components);

/// <summary>
/// Identifies one Geometry from the current saved Project and the exact project
/// XML draft shown to the user. The optional Geometry revision is supplied on
/// refresh/submit to reject a changed HDF; it is absent for the first capture.
/// </summary>
public sealed record BugReportGeometryCaptureRequest(
    string ProjectPath,
    string ExpectedProjectRevision,
    string ProjectXml,
    string ProjectXmlFingerprint,
    string GeometryAssetID,
    string GeometryName,
    string GeometryPath,
    string? ExpectedGeometryRevision = null);

public sealed record BugReportGeometryCapture(
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlFingerprint,
    string GeometryAssetID,
    string GeometryName,
    string GeometryPath,
    string GeometryRevision);

public sealed record BugReportImage(string Name, string ContentType, byte[] Data);

public sealed record BugReportSubmitRequest(
    string Uid,
    string RasVersion,
    string Os,
    string Cores,
    string Timezone,
    string Email,
    string Summary,
    string Description,
    string ErrorLog,
    string[] ComponentIDs,
    BugReportImage[] Images,
    string? CrashContext,
    BugReportGeometryCaptureRequest? Geometry);

public sealed record BugReportSubmitReceipt(string IssueKey, string[] AttachmentWarnings);
