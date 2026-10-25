/** Host projection of the original PlanDiffOption; IDs are opaque source identities. */
export interface PlanDiffOption {
  id: string;
  planPath: string;
  planRevision: string;
  /** The Host can keep an unavailable Result as a separate associated-Plan choice. */
  resultPath: string | null;
  /** The current Host returns null: it compares the associated Plan, not Result contents. */
  resultRevision: string | null;
  displayName: string;
  headerName: string;
  /** A Result choice compares its associated Plan, as in the original C# UI. */
  isResult: boolean;
}

export function planDiffOptionKey(option: PlanDiffOption): string {
  return JSON.stringify([option.id, option.planPath, option.planRevision, option.resultPath,
    option.resultRevision, option.displayName, option.headerName, option.isResult]);
}
