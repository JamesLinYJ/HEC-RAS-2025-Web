import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryTopologyArcPointsSnapshot, GeometryTopologySnapshot } from './geometryTypes';

export interface ArcPointsEditorDialogProps {
  language: Language;
  arcFID: number;
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onLoad: (arcFID: number) => Promise<GeometryTopologyArcPointsSnapshot>;
  onCoordinateChange: (arcFID: number, point: GeometryTopologyArcPointsSnapshot['rows'][number], axis: 'X' | 'Y', value: string,
    expected: { revision: string; draftState: string; draftIdentity: string }) => Promise<void>;
  onClose: () => void;
}

function pointKind(language: Language, kind: string): string {
  if (kind === 'start') return language === 'zh' ? '起点' : 'Start';
  if (kind === 'end') return language === 'zh' ? '终点' : 'End';
  if (kind === 'internal') return language === 'zh' ? '内部点' : 'Internal point';
  return kind;
}

function pointIdentity(point: GeometryTopologyArcPointsSnapshot['rows'][number]): string {
  return `${point.rowIndex}:${point.pointKind}:${point.internalPointIndex ?? ''}:${point.endpointNodeIndex ?? ''}`;
}

export function ArcPointsEditorDialog({ language, arcFID, snapshot, canEdit, busy, onLoad, onCoordinateChange, onClose }: ArcPointsEditorDialogProps) {
  const [points, setPoints] = useState<GeometryTopologyArcPointsSnapshot>();
  const [pointsDraftState, setPointsDraftState] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [drafts, setDrafts] = useState<Record<string, { X?: string; Y?: string }>>({});
  const requestSequence = useRef(0);
  const loadRef = useRef(onLoad); loadRef.current = onLoad;

  useEffect(() => {
    const sequence = ++requestSequence.current;
    let cancelled = false;
    const capturedDraftState = snapshot?.draftState;
    setPoints(undefined); setPointsDraftState(undefined); setError(''); setDrafts({}); setLoading(true);
    void loadRef.current(arcFID).then((result) => {
      if (cancelled || sequence !== requestSequence.current) return;
      if (result.arcFID !== arcFID || (snapshot?.revision && result.revision !== snapshot.revision))
        throw new Error('The arc-point response does not match the selected source arc and revision.');
      if (snapshot?.completion?.draftIdentity && result.draftIdentity !== snapshot.completion.draftIdentity)
        throw new DOMException('The arc-point response belongs to a different topology draft.', 'AbortError');
      if (!capturedDraftState) throw new Error('The topology draft identity is unavailable for this arc-point response.');
      setPoints(result); setPointsDraftState(capturedDraftState);
    }).catch((reason: unknown) => {
      if (!cancelled && sequence === requestSequence.current && !(reason instanceof DOMException && reason.name === 'AbortError'))
        setError(geometryDiagnosticMessage(reason));
    }).finally(() => { if (!cancelled && sequence === requestSequence.current) setLoading(false); });
    return () => { cancelled = true; };
  }, [arcFID, snapshot?.revision, snapshot?.draftState]);

  const commit = async (point: GeometryTopologyArcPointsSnapshot['rows'][number], axis: 'X' | 'Y', value: string): Promise<void> => {
    if (!canEdit || busy || saving) return;
    if (!points || !pointsDraftState || points.revision !== snapshot?.revision || pointsDraftState !== snapshot?.draftState
      || (snapshot.completion?.draftIdentity && points.draftIdentity !== snapshot.completion.draftIdentity)) {
      setError(language === 'zh' ? '拓扑草稿已变化，请重新读取弧段点后再编辑。' : 'The topology draft changed. Reload arc points before editing.');
      return;
    }
    const key = pointIdentity(point);
    const oldValue = String(point[axis.toLowerCase() as 'x' | 'y']);
    if (!value.trim() || value === oldValue) { setDrafts((current) => { const next = { ...current }; delete next[key]; return next; }); return; }
    setSaving(true); setError('');
    try {
      await onCoordinateChange(arcFID, point, axis, value, { revision: points.revision, draftState: pointsDraftState, draftIdentity: points.draftIdentity });
      setDrafts((current) => { const next = { ...current }; delete next[key]; return next; });
    } catch (reason) { setError(geometryDiagnosticMessage(reason)); }
    finally { setSaving(false); }
  };

  return (
    <div className="geometry-modal-scrim" role="presentation">
      <section className="geometry-modal geometry-arc-points-dialog" role="dialog" aria-modal="true" aria-labelledby="geometry-arc-points-title">
        <header className="geometry-panel-heading"><div><h3 id="geometry-arc-points-title">{language === 'zh' ? '弧段点坐标' : 'Arc point coordinates'}</h3><p>FID {arcFID} · {points?.rows.length ?? '—'} {language === 'zh' ? '个点' : 'points'}</p></div>
          <button type="button" className="geometry-secondary" onClick={onClose}>{language === 'zh' ? '关闭' : 'Close'}</button>
        </header>
        {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
        {loading || !points ? <div className="geometry-empty"><strong>{language === 'zh' ? '正在读取当前拓扑草稿中的弧段点…' : 'Loading arc points from the current topology draft…'}</strong></div> : points.arcFID !== arcFID ? (
          <div className="geometry-empty"><strong>{language === 'zh' ? '返回点序列属于其他弧段。' : 'The point sequence belongs to another arc.'}</strong></div>
        ) : (
          <div className="geometry-table-wrap"><table className="geometry-table">
            <thead><tr><th>{language === 'zh' ? '序号' : 'Index'}</th><th>{language === 'zh' ? '类型' : 'Type'}</th><th>{language === 'zh' ? '源索引' : 'Source index'}</th><th>X</th><th>Y</th></tr></thead>
            <tbody>{points.rows.map((point) => {
              const key = pointIdentity(point);
              return <tr key={key}>
                <td>{point.rowIndex}</td><td>{pointKind(language, point.pointKind)}</td>
                <td>{point.pointKind === 'internal' ? point.internalPointIndex ?? '—' : point.endpointNodeIndex ?? '—'}</td>
                {(['X', 'Y'] as const).map((axis) => {
                  const sourceValue = String(point[axis.toLowerCase() as 'x' | 'y']);
                  const value = drafts[key]?.[axis] ?? sourceValue;
                  return <td key={axis}><input aria-label={`${axis} · ${pointKind(language, point.pointKind)} ${point.rowIndex}`} type="number" step="any" value={value}
                    disabled={!canEdit || busy || saving} onChange={(event) => setDrafts((current) => ({ ...current, [key]: { ...current[key], [axis]: event.target.value } }))}
                    onBlur={() => void commit(point, axis, value)} onKeyDown={(event) => { if (event.key === 'Enter') { event.currentTarget.blur(); } }} /></td>;
                })}
              </tr>;
            })}</tbody>
          </table></div>
        )}
        <footer className="geometry-table-footer"><span>{language === 'zh' ? 'X/Y 更改立即进入当前可撤销拓扑草稿；父级保存后才写回 Geometry。' : 'X/Y edits enter the current undoable topology draft; the parent save writes them back to Geometry.'}</span>
          <span>{snapshot?.revision ?? points?.revision ?? '—'}</span></footer>
      </section>
    </div>
  );
}

export default ArcPointsEditorDialog;
