namespace Ras.Backend;

/// <summary>Immutable workspace identity used by read-only plan analysis and R2R generation.</summary>
public sealed record PlanAnalysisProjectCapture(string ProjectPath, string ProjectRevision,
    string ProjectXml, string ProjectXmlFingerprint, CoreLayerRequestContext? CoreLayerContext = null);

/// <summary>
/// Exact in-memory Plan editor snapshot. This is applied to a private source Plan
/// instance only; no analysis endpoint saves the Plan or Project.
/// </summary>
public sealed record PlanAnalysisPlanDraft(string AssetPath, string Revision,
    Dictionary<string, string> Fields, string? SourceState = null,
    PlanTimeWindowEdit[]? PlanTimeWindowEdits = null,
    Guid? EditorID = null, string? OwnerContextID = null);

public sealed record PlanAnalysisDiffOptionsRequest(PlanAnalysisProjectCapture Capture);
public sealed record PlanAnalysisDiffOption(string Id, string PlanPath, string PlanRevision,
    string? ResultPath, string? ResultRevision, string DisplayName, string HeaderName, bool IsResult);
public sealed record PlanAnalysisDiffOptionsResponse(string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, PlanAnalysisDiffOption[] Options);
public sealed record PlanAnalysisDiffRequest(PlanAnalysisProjectCapture Capture,
    string LeftOptionID, string RightOptionID, PlanAnalysisPlanDraft[]? PlanDrafts = null);
public sealed record PlanAnalysisDiffRow(string Key, string LeftValue, string RightValue, bool Differs);
public sealed record PlanAnalysisDiffResponse(string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, PlanAnalysisDiffOption Left, PlanAnalysisDiffOption Right,
    PlanAnalysisDiffRow[] Rows);

public sealed record PlanMatrixOptionsRequest(PlanAnalysisProjectCapture Capture,
    string? SelectedPlanPath = null, PlanAnalysisPlanDraft? PlanDraft = null);
public sealed record PlanMatrixPlanChoice(string PlanPath, string Revision, string Name);
public sealed record PlanMatrixValueChoice(string Value, string Label, bool Selected, bool Removable);
public sealed record PlanMatrixProperty(string PropertyName, string DisplayName, string ValueType,
    string InputHint, bool IsClosedSet, PlanMatrixValueChoice[] InitialValues);
public sealed record PlanMatrixRule(string Name, string Reason, bool Enabled = true);
public sealed record PlanMatrixOptionsResponse(string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, PlanMatrixPlanChoice[] Plans, string? SelectedPlanPath,
    string? OutputDirectory, PlanMatrixProperty[] Properties, PlanMatrixRule[] Rules);
public sealed record PlanMatrixAxisSelection(string PropertyName, string[] Values);
public sealed record PlanMatrixPreviewRequest(PlanAnalysisProjectCapture Capture,
    string PlanPath, string PlanRevision, PlanAnalysisPlanDraft? PlanDraft,
    PlanMatrixAxisSelection[] Axes, string[] EnabledRuleNames, bool CollapseIrrelevant,
    string? OutputDirectory = null);
public sealed record PlanMatrixAssignment(string PropertyName, string DisplayName, string Value,
    bool IsRelevant, string? IrrelevantReason);
public sealed record PlanMatrixCombination(int Index, string Label, string Filename,
    bool IsExcluded, bool IsCollapsed, string? ExclusionReason, PlanMatrixAssignment[] Assignments);
public sealed record PlanMatrixPreviewResponse(string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, string PlanPath, string PlanRevision, string InspectionID,
    string OutputDirectory, int WriteCount, string Summary, string Hint,
    PlanMatrixCombination[] Combinations);
public sealed record PlanMatrixEmitRequest(PlanMatrixPreviewRequest Preview,
    string InspectionID, bool Overwrite, bool NameEmbeddedPlan, bool WriteManifest);
public sealed record PlanMatrixEmitFile(string Filename, string Status, string? FilePath,
    long FileSizeBytes, string? Message);
public sealed record PlanMatrixEmitResponse(string ProjectPath, string ProjectRevision,
    string ProjectXmlFingerprint, string PlanPath, string PlanRevision, string InspectionID,
    string OutputDirectory, string State, int Written, int Skipped, int Failed, int Excluded,
    bool WasCancelled, bool WasAborted, string? AbortReason, string? ManifestPath,
    bool SourcesStillCurrent, PlanMatrixEmitFile[] Files);
