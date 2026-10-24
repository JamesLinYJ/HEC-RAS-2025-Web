import { useCallback, useEffect, useRef, useState } from 'react';
import type { EditorPageProps, ManningTransformOperation, ModelRow } from './model';

const floatText = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/;

/** Match the Host's invariant-culture float parsing, including its float32 range. */
export function isFiniteManningFloat(value: string): boolean {
  const trimmed = value.trim();
  if (!trimmed || !floatText.test(trimmed)) return false;
  const number = Number(trimmed);
  return Number.isFinite(number) && Number.isFinite(Math.fround(number));
}

/** Manning constants are nonnegative in ModelEditorAdapter; interpretation values retain Host float semantics. */
export function isValidManningConstant(value: string): boolean {
  return isFiniteManningFloat(value) && Number(value) >= 0;
}

export interface ManningRowsAssessment {
  identityIsSafe: boolean;
  invalidValueRowIDs: string[];
}

/** Keep Host row IDs and classification keys intact; only Value is an editable cell. */
export function assessManningRows(rows: readonly ModelRow[], projection: readonly ModelRow[]): ManningRowsAssessment {
  const expected = new Map<string, string>();
  let identityIsSafe = rows.length === projection.length;
  for (const row of projection) {
    const key = row.values.Key;
    if (!row.id || expected.has(row.id) || typeof key !== 'string') identityIsSafe = false;
    else expected.set(row.id, key);
  }

  const seen = new Set<string>();
  const invalidValueRowIDs: string[] = [];
  for (const row of rows) {
    const keyNames = Object.keys(row.values).sort();
    const expectedKey = expected.get(row.id);
    if (!row.id || seen.has(row.id) || expectedKey === undefined || row.values.Key !== expectedKey
      || keyNames.length !== 2 || keyNames[0] !== 'Key' || keyNames[1] !== 'Value') {
      identityIsSafe = false;
    }
    seen.add(row.id);
    if (typeof row.values.Value !== 'string' || !isFiniteManningFloat(row.values.Value)) invalidValueRowIDs.push(row.id);
  }
  if (seen.size !== expected.size) identityIsSafe = false;
  return { identityIsSafe, invalidValueRowIDs };
}

/** A UI operation gate; Host callbacks still own source revision and workbench owner checks. */
export function useManningTransform(identity: string, onTransform?: (operation: ManningTransformOperation) => Promise<boolean>) {
  const [busy, setBusy] = useState(false);
  const active = useRef(false);
  const generation = useRef(0);
  const mounted = useRef(true);
  const currentIdentity = useRef(identity);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      generation.current += 1;
      active.current = false;
    };
  }, []);

  useEffect(() => {
    currentIdentity.current = identity;
    generation.current += 1;
    active.current = false;
    setBusy(false);
  }, [identity]);

  const run = useCallback(async (operation: ManningTransformOperation): Promise<boolean> => {
    if (!onTransform || active.current || currentIdentity.current !== identity) return false;
    active.current = true;
    const requestGeneration = generation.current;
    setBusy(true);
    try {
      return await onTransform(operation);
    } catch {
      return false;
    } finally {
      if (mounted.current && generation.current === requestGeneration && currentIdentity.current === identity) {
        active.current = false;
        setBusy(false);
      }
    }
  }, [identity, onTransform]);

  return { busy, run };
}

/** Local UI key for associating asynchronous feedback with the displayed source snapshot. */
export function manningEditorIdentity(props: Pick<EditorPageProps, 'asset' | 'snapshot'>): string {
  let locator = '';
  try {
    locator = JSON.stringify(props.asset.locator ?? null) ?? '';
  } catch {
    // The parent callback has the authoritative project/session owner check.
  }
  return [props.asset.kind, props.asset.path, props.asset.revision ?? '', props.snapshot.revision,
    props.snapshot.sourceState ?? '', locator].join('\u001f');
}
