namespace Ras.Backend;

public sealed record StableChannelDebugSource(
    string SourceID,
    string? ProjectPath,
    string? ProjectRevision,
    string? DesignPath,
    string? DesignRevision,
    bool InMemory);

public sealed record StableChannelDebugPartState(
    double? D,
    double? AngleOfRepose,
    double? Roughness,
    double? SpecificGravity,
    double? ShieldsParameter,
    string Equation);

public sealed record StableChannelDebugState(
    double? Depth,
    double? Discharge,
    double? Slope,
    double? LeftBankSideSlope,
    double? RightBankSideSlope,
    double? Temperature,
    double? Width,
    string SolutionMethod,
    StableChannelDebugPartState? LeftBank,
    StableChannelDebugPartState? Bed,
    StableChannelDebugPartState? RightBank);

public sealed record StableChannelDebugOpenRequest(
    string RequestID,
    string? ProjectPath,
    string? InMemorySourceID = null,
    StableChannelDebugState? InMemoryState = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record StableChannelDebugOpenResponse(
    string RequestID,
    StableChannelDebugSource Source,
    StableChannelDebugState State);

public sealed record StableChannelDebugComputeRequest(
    string RequestID,
    StableChannelDebugSource Source,
    StableChannelDebugState State,
    string[] SolveFor,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record StableChannelDebugComputeResponse(
    string RequestID,
    string SourceID,
    StableChannelDebugState State,
    string[] SolveFor,
    bool Computed,
    string? ErrorCode);

public sealed record StableChannelDebugSaveRequest(
    string RequestID,
    StableChannelDebugSource Source,
    StableChannelDebugState State,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record StableChannelDebugSaveResponse(
    string RequestID,
    string SourceID,
    string DesignPath,
    string DesignRevision,
    StableChannelDebugState State);
