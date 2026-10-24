import { useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { GeometryFeatureTable } from './GeometryFeatureTable';
import { CartesianPatchQuickEditor } from './CartesianPatchQuickEditor';
import { QuadPatchQuickEditor } from './QuadPatchQuickEditor';
import { TrianglePatchQuickEditor } from './TrianglePatchQuickEditor';
import { geometryDiagnosticMessage } from './geometryErrors';
import { gt } from './geometryText';
import type { GeometryMeshPatchEditContext, GeometryMeshPatchMutation, GeometryMeshSnapshot, GeometryRow } from './geometryTypes';

export interface PatchEditorProps {
  language: Language;
  snapshot?: GeometryMeshSnapshot;
  localRows?: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onRegionSelection: (ids: number[]) => void;
  onMutation: (context: GeometryMeshPatchEditContext, mutation: GeometryMeshPatchMutation) => Promise<void>;
}

function sameIDs(left: number[], right: number[]): boolean {
  return left.length === right.length && left.every((id, index) => id === right[index]);
}

function patchTypeLabel(language: Language, kind: string): string {
  if (language !== 'zh') return kind;
  const labels: Record<string, string> = {
    Quad: '四边形', TrianglePaver: '三角网格', Cartesian: '笛卡尔网格', OneD: '一维', Empty: '空区域',
  };
  return labels[kind] ?? kind;
}

function idsMatchRows(rows: GeometryRow[], ids: number[]): boolean {
  const validIDs = new Set(rows.flatMap((row) => row.fid == null || !Number.isInteger(row.fid) || row.fid < 0 ? [] : [row.fid]));
  return ids.every((id) => validIDs.has(id)) && new Set(ids).size === ids.length;
}

export function PatchEditor({
  language, snapshot, localRows = [], canEdit, busy, onRegionSelection, onMutation,
}: PatchEditorProps) {
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const selectedIDs = snapshot?.selectedRegionIDs ?? [];
  const selectedSet = useMemo(() => new Set(selectedIDs), [selectedIDs.join(',')]);
  const selectedRows = useMemo(() => selectedIDs.flatMap((fid) => {
    const row = snapshot?.regions.rows.find((candidate) => candidate.fid === fid);
    return row ? [row] : [];
  }), [snapshot?.regions.rows, selectedIDs.join(',')]);
  const firstRow = selectedRows[0];
  const selectedTypes = [...new Set(selectedRows.map((row) => row.values.PatchType ?? '').filter(Boolean))];
  const patchSizing = snapshot?.patchSizing;
  const currentKind = patchSizing?.state === 'ready' && patchSizing.selectedRegionIDs.length === selectedIDs.length
    && sameIDs(patchSizing.selectedRegionIDs, selectedIDs) ? patchSizing.kind ?? '' : '';
  const context: GeometryMeshPatchEditContext | undefined = snapshot ? {
    revision: snapshot.revision,
    draftState: snapshot.draftState,
    topologyFingerprint: snapshot.topologyFingerprint,
    selectedRegionIDs: [...snapshot.selectedRegionIDs],
  } : undefined;
  const selectionIsUsable = !!snapshot && selectedIDs.length > 0 && idsMatchRows(snapshot.regions.rows, selectedIDs)
    && !!patchSizing && sameIDs(patchSizing.selectedRegionIDs, selectedIDs);
  const targetKind = selectedTypes.length === 1 ? selectedTypes[0] : currentKind;
  const canMutate = canEdit && !!snapshot?.canEdit && !busy && !pending && selectionIsUsable && patchSizing?.state === 'ready';

  useEffect(() => setError(''), [snapshot?.draftState, selectedIDs.join(',')]);

  const runMutation = async (mutation: GeometryMeshPatchMutation): Promise<void> => {
    if (!context) throw new Error(language === 'zh' ? '网格快照尚未加载。' : 'The mesh snapshot is not loaded.');
    if (pending || busy) throw new Error(language === 'zh' ? '网格操作正在进行。' : 'A mesh operation is already in progress.');
    setPending(true);
    setError('');
    try {
      await onMutation(context, mutation);
    } catch (reason) {
      setError(geometryDiagnosticMessage(reason));
      throw reason;
    } finally {
      setPending(false);
    }
  };

  const handleSelection = (rowIDs: string[]): void => {
    const selectedRowIDs = new Set(rowIDs);
    const selectedFIDs = (snapshot?.regions.rows ?? [])
      .filter((row) => selectedRowIDs.has(row.id) && row.fid != null && Number.isInteger(row.fid) && row.fid >= 0)
      .map((row) => row.fid!);
    onRegionSelection(selectedFIDs);
  };

  const dimensionEditor = (): ReactNode => {
    if (!snapshot || !patchSizing || !selectionIsUsable || patchSizing.state !== 'ready' || !currentKind || targetKind !== currentKind) return null;
    const firstSourceRow = firstRow;
    if (!firstSourceRow) return null;
    if (targetKind === 'Quad') return <QuadPatchQuickEditor language={language} snapshot={snapshot} sizing={patchSizing}
      canEdit={canMutate} busy={busy || pending} onMutation={runMutation} />;
    if (targetKind === 'TrianglePaver') return <TrianglePatchQuickEditor language={language} row={firstSourceRow}
      selectedCount={selectedIDs.length} canEdit={canMutate} busy={busy || pending} onMutation={runMutation} />;
    if (targetKind === 'Cartesian') return <CartesianPatchQuickEditor language={language} row={firstSourceRow}
      selectedCount={selectedIDs.length} canEdit={canMutate} busy={busy || pending}
      onResize={(factor) => runMutation({ operation: 'resizePatch', featureIDs: [selectedIDs[0]], value: factor === 0.5 ? '0.5' : '2' })}
      onSetCombinedCellSize={(value) => runMutation({ operation: 'setCartesianCombinedCellSize', featureIDs: [selectedIDs[0]], value })} />;
    return null;
  };

  return <section className="geometry-panel">
    <header className="geometry-panel-heading"><div>
      <h2>{language === 'zh' ? '网格分区' : 'Patch editor'}</h2>
      <p>{selectedIDs.length} {gt(language, 'selectedRegions')} · {currentKind ? patchTypeLabel(language, currentKind) : (language === 'zh' ? '尚无统一分区类型' : 'No common patch type')}</p>
    </div><span className="geometry-count-pill">{language === 'zh' ? '源区域 FID' : 'Source region FIDs'}</span></header>
    <GeometryFeatureTable language={language} kind="region" table={snapshot?.regions} localRows={localRows} editable={false}
      allowLocalAdd={false} busy={busy || pending}
      selectedRowIDs={(snapshot?.regions.rows ?? []).filter((row) => row.fid != null && selectedSet.has(row.fid)).map((row) => row.id)}
      onSelect={handleSelection} />

    {!snapshot && <div className="geometry-empty">{gt(language, 'meshNoData')}</div>}
    {snapshot && selectedIDs.length > 0 && !selectionIsUsable && <div className="geometry-validation-item">
      {language === 'zh' ? '所选区域 FID 与当前源表或尺寸快照不一致；请重新选择区域。' : 'Selected region FIDs do not match the current source table or sizing snapshot. Select the regions again.'}
    </div>}
    {patchSizing?.state === 'mixedTypes' && <div className="geometry-inline-help">
      {language === 'zh' ? '所选源区域包含不同分区类型。尺寸编辑需要先逐个选择同类型区域。' : 'The selected source regions have mixed patch types. Select regions with one common type before editing dimensions.'}
    </div>}

    {dimensionEditor()}
    {patchSizing?.message && <details className="geometry-inline-help"><summary>{gt(language, 'diagnosticDetails')}</summary>
      <pre style={{ whiteSpace: 'pre-wrap' }}>{patchSizing.message}</pre>
    </details>}
    {error && <GeometryError language={language} error={error} style={{ margin: 13 }} />}
    {selectedRows.length > 1 && targetKind !== 'Quad' && <div className="geometry-inline-help">
      {language === 'zh' ? '三角与笛卡尔快速尺寸命令作用于首个选中源区域；四边形尺寸按所选区域合并侧弧段。' : 'Triangle and Cartesian quick sizing targets the first selected source region; Quad sizing combines side arcs across the selection.'}
    </div>}
  </section>;
}

export default PatchEditor;
