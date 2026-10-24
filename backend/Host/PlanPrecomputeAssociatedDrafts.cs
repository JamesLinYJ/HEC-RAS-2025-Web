namespace Ras.Backend;

/// <summary>
/// A transient capture of the exact Plan draft and any dirty, currently
/// associated child editors. The backend evaluates these models in one
/// in-memory Project context and never saves them.
/// </summary>
public sealed record PlanPrecomputeDraftRequest(ModelSaveRequest Plan,
    ModelSaveRequest? AssociatedGeometry, ModelSaveRequest? AssociatedBoundaryCondition,
    string CaptureID);

/// <summary>Echoes the caller's draft capture so late or mismatched replies are rejected.</summary>
public sealed record PlanPrecomputeDraftResponse(string CaptureID, PlanPrecomputeResult Result);
