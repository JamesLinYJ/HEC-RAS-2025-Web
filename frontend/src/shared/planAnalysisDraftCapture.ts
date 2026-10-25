import type { PlanAnalysisPlanDraft } from './planAnalysisDrafts';
import { planAnalysisValueKey } from './planAnalysisDrafts';

interface PlanValues {
  sourceState: string | null;
  fields: Record<string, string>;
  images: unknown;
  rows: unknown;
  samples: unknown;
  sampleText: unknown;
}
interface PlanSnapshot {
  revision: string;
  fields: Record<string, string>;
  sourceState?: string | null;
  readOnlyFields?: readonly string[] | null;
  planTimeWindowFieldIDs?: { mode: string; start: string; end: string } | null;
}

/** Mirrors the existing model.save field/time-window envelope without replaying derived fields. */
export function capturePlanEditorDraft(input: {
  editorID: string; ownerContextID: string; assetPath: string;
  sourceStateFallback?: string | null;
  snapshot: PlanSnapshot; baseline: PlanValues; draft: PlanValues;
}): { planDraft?: PlanAnalysisPlanDraft; unsupportedReason?: string } {
  const { snapshot, baseline, draft } = input;
  for (const key of ['images', 'rows', 'samples', 'sampleText'] as const) {
    if (planAnalysisValueKey(draft[key]) !== planAnalysisValueKey(baseline[key]))
      return { unsupportedReason: '当前计算计划还有图像、表格或时序数据更改，分析接口尚不能完整捕获，请先保存。' };
  }
  if (Object.keys(snapshot.fields).some(id => !(id in draft.fields)))
    return { unsupportedReason: '当前计算计划草稿缺少已载入字段，请重新载入后再分析。' };
  const readonly = new Set(snapshot.readOnlyFields ?? []);
  if (Object.entries(draft.fields).some(([id, value]) => readonly.has(id) && value !== snapshot.fields[id]))
    return { unsupportedReason: '当前草稿包含只读字段的变化，无法安全捕获。' };
  const time = snapshot.planTimeWindowFieldIDs;
  const timeIDs = time ? [time.mode, time.start, time.end] : [];
  const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) =>
    !timeIDs.includes(id) && value !== snapshot.fields[id] && !readonly.has(id)));
  const planTimeWindowEdits = time ? timeIDs.flatMap((fieldID, index) => {
    const value = draft.fields[fieldID];
    return value === undefined || value === snapshot.fields[fieldID] ? [] : [{
      // Stable within this editor snapshot: repeated publication must not manufacture a new draft.
      id: `${input.editorID}:analysis-time:${index}`, fieldID, value,
    }];
  }) : null;
  return { planDraft: {
    assetPath: input.assetPath, revision: snapshot.revision, fields,
    sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
      ? draft.sourceState : snapshot.sourceState ?? input.sourceStateFallback ?? null,
    planTimeWindowEdits, editorID: input.editorID, ownerContextID: input.ownerContextID,
  } };
}
