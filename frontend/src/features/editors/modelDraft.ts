import type { ModelDraft, ModelSnapshot } from './model';
import { snapshotRows } from './model';

export function createModelDraft(snapshot: ModelSnapshot): ModelDraft {
  const samples: ModelDraft['samples'] = {};
  const sampleText: ModelDraft['sampleText'] = {};
  for (const [pageID, series] of Object.entries(snapshot.boundary?.series ?? {})) {
    samples[pageID] = series.samples.map((sample) => ({ ...sample }));
    sampleText[pageID] = Object.fromEntries(series.samples.map((sample) => [sample.id, {
      x: series.xIsDate ? toDateTimeLocal(sample.x) : String(sample.x),
      y: sample.y == null ? '' : String(sample.y),
    }]));
  }
  return {
    sourceState: snapshot.sourceState ?? null,
    fields: { ...snapshot.fields },
    images: cloneValue(snapshot.images),
    rows: snapshotRows(snapshot),
    samples,
    sampleText,
    changedCells: {},
  };
}

/**
 * Table-backed series expose samples as a projection of their editable source
 * rows. Keep those samples in the UI draft, but omit them from write requests;
 * rows are authoritative for these pages, while independent payload samples
 * remain writable.
 */
export function samplesForModelRequest(snapshot: ModelSnapshot, draft: ModelDraft): ModelDraft['samples'] {
  const sourceTablePages = new Set(Object.values(snapshot.boundary?.series ?? {})
    .filter((series) => !!series.tableId)
    .map((series) => series.pageId));
  return Object.fromEntries(Object.entries(draft.samples).filter(([pageID]) => !sourceTablePages.has(pageID)));
}

export function isModelDraftDirty(snapshot: ModelSnapshot | null, draft: ModelDraft): boolean {
  if (!snapshot) return false;
  const baseline = createModelDraft(snapshot);
  return draftFingerprint(draft) !== draftFingerprint(baseline);
}

export function changedModelCells(draft: ModelDraft, tables: Record<string, Array<{ id: string; values: Record<string, string> }>>): ModelDraft['changedCells'] {
  const changes: ModelDraft['changedCells'] = {};
  for (const [tableID, rows] of Object.entries(draft.rows)) {
    const original = tables[tableID];
    if (!original) continue;
    const originalByID = new Map(original.map((row) => [row.id, row]));
    for (const row of rows) {
      const initial = originalByID.get(row.id);
      if (!initial) continue;
      for (const [columnID, value] of Object.entries(row.values)) {
        if (value !== initial.values[columnID]) (changes[tableID] ??= []).push({ rowID: row.id, columnID });
      }
    }
  }
  return changes;
}

export function updateModelSample(draft: ModelDraft, snapshot: ModelSnapshot, pageID: string, sampleID: string, column: 'x' | 'y', raw: string): ModelDraft {
  const series = snapshot.boundary?.series[pageID];
  if (!series) return draft;
  const sample = (draft.samples[pageID] ?? series.samples).find((candidate) => candidate.id === sampleID);
  if (!sample) return draft;
  const samples = { ...draft.samples, [pageID]: (draft.samples[pageID] ?? series.samples).map((candidate) => candidate.id !== sampleID ? candidate : {
    ...candidate,
    ...(column === 'x' ? { x: series.xIsDate ? Date.parse(raw) / 1000 : Number(raw) } : { y: raw.trim() === '' ? null : Number(raw) }),
  }) };
  const textForPage = { ...(draft.sampleText[pageID] ?? {}) };
  const currentText = textForPage[sampleID] ?? { x: series.xIsDate ? toDateTimeLocal(sample.x) : String(sample.x), y: sample.y == null ? '' : String(sample.y) };
  textForPage[sampleID] = { ...currentText, [column]: raw };
  return { ...draft, samples, sampleText: { ...draft.sampleText, [pageID]: textForPage } };
}

export function validateChangedSamples(snapshot: ModelSnapshot, draft: ModelDraft, language: 'zh' | 'en'): Array<{ fieldID: string; title: string; message: string }> {
  const errors: Array<{ fieldID: string; title: string; message: string }> = [];
  const invalid = language === 'zh' ? '请输入有效值。' : 'Enter a valid value.';
  for (const [pageID, rawSamples] of Object.entries(draft.sampleText)) {
    const series = snapshot.boundary?.series[pageID];
    if (!series) continue;
    for (const [sampleID, raw] of Object.entries(rawSamples)) {
      const initial = series.samples.find((sample) => sample.id === sampleID);
      if (!draft.samples[pageID]?.some((sample) => sample.id === sampleID)) continue;
      const initialX = initial ? (series.xIsDate ? toDateTimeLocal(initial.x) : String(initial.x)) : undefined;
      const initialY = initial ? (initial.y == null ? '' : String(initial.y)) : undefined;
      if ((initialX === undefined || raw.x !== initialX) && (raw.x.trim() === '' || !Number.isFinite(series.xIsDate ? Date.parse(raw.x) : Number(raw.x))))
        errors.push({ fieldID: `${pageID}/${sampleID}/x`, title: `${series.xTitle} ${Number(sampleID) + 1}`, message: invalid });
      if ((initialY === undefined || raw.y !== initialY) && raw.y.trim() !== '' && !Number.isFinite(Number(raw.y)))
        errors.push({ fieldID: `${pageID}/${sampleID}/y`, title: `${series.yTitle} ${Number(sampleID) + 1}`, message: invalid });
    }
  }
  return errors;
}

function draftFingerprint(draft: ModelDraft): string {
  return JSON.stringify({ sourceState: draft.sourceState, fields: draft.fields, images: draft.images, rows: draft.rows, samples: draft.samples, sampleText: draft.sampleText });
}

function cloneValue<T>(value: T): T {
  if (typeof structuredClone === 'function') return structuredClone(value);
  return JSON.parse(JSON.stringify(value)) as T;
}

function toDateTimeLocal(epochSeconds: number): string {
  const date = new Date(epochSeconds * 1000);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
