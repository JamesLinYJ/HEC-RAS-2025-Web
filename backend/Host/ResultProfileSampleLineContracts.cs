namespace Ras.Backend;

public sealed record ResultProfileSourcesRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision);

public sealed record ResultProfileSampleLineRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision, ResultProfileSourceIdentity[] Sources,
    GeometryTopologyCoordinate[] SampleLine, string Mode);

public sealed record ResultProfileSourceIdentity(string ResultPath, string Revision);

public sealed record ResultProfileSourceChoice(string ResultPath, string Revision, string ResultName,
    string EmbeddedPlanName, int ProfileCount, string? FirstFlowAreaName,
    int FirstFlowAreaCellCount, int FirstFlowAreaFaceCount);

public sealed record ResultProfileSourcesResult(string ProjectPath, string ResultPath, string Revision,
    string EmbeddedPlanName, string? FirstFlowAreaName, int FirstFlowAreaCellCount,
    int FirstFlowAreaFaceCount, ResultProfileSourceChoice[] Sources);

public sealed record ResultProfileSampleLinePoint(int ProfileIndex, string Time, double? Value);

public sealed record ResultProfileSampleLineSeries(string ResultPath, string Revision, string ResultName,
    string Mode, string Quantity, string Units, bool Available, string? UnavailableReason,
    ResultProfileSampleLinePoint[] Points);

public sealed record ResultProfileSampleLineResult(string ProjectPath, string ResultPath, string Revision,
    string Mode, GeometryTopologyCoordinate[] SampleLine, ResultProfileSampleLineSeries[] Series);
