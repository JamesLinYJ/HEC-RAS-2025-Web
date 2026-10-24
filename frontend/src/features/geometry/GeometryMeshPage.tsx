import { useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import { GeometryError } from './GeometryError';
import { FlowAreaLayerActiveFeatures } from './FlowAreaLayerActiveFeatures';
import { GeometryPrepareSummaryControl } from './GeometryPrepareSummaryControl';
import { MeshDiagnostics } from './MeshDiagnostics';
import { PatchEditor } from './PatchEditor';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryField, GeometryMeshDegreeScan, GeometryMeshPatchEditContext, GeometryMeshPatchMutation, GeometryMeshSnapshot, GeometryRow, GeometryTable } from './geometryTypes';

export interface GeometryMeshPageProps {
  language: Language;
  snapshot?: GeometryMeshSnapshot;
  localRegionRows: GeometryRow[];
  busy: boolean;
  canEdit: boolean;
  canGenerate: boolean;
  canPrepare: boolean;
  canDegreeScan: boolean;
  canPropertyTables: boolean;
  onFlowAreaChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onRegionSelection: (ids: number[]) => void;
  onPatchMutation: (context: GeometryMeshPatchEditContext, mutation: GeometryMeshPatchMutation) => Promise<void>;
  onPrepare: () => void;
  onGenerate: (progress: (progress: unknown) => void) => Promise<void>;
  onDegreeScan: (maxFacesPerNode: number) => Promise<GeometryMeshDegreeScan>;
  onPropertyTables: () => Promise<unknown>;
}

function flowAreaTable(snapshot: GeometryMeshSnapshot | undefined, language: Language): GeometryTable | undefined {
  if (!snapshot) return undefined;
  return {
    featureKind: 'flowArea',
    columns: [
      { id: 'Name', title: gt(language, 'name'), sourceType: 'string', readOnly: false },
      { id: 'TargetedCellSpacing', title: gt(language, 'targetSpacing'), sourceType: 'double', readOnly: false },
      { id: 'HasMesh', title: gt(language, 'hasMesh'), sourceType: 'bool' },
      { id: 'CellCount', title: gt(language, 'cells'), sourceType: 'int' },
      { id: 'FaceCount', title: gt(language, 'faces'), sourceType: 'int' },
      { id: 'NodeCount', title: gt(language, 'meshNodes'), sourceType: 'int' },
      { id: 'HasMeshErrors', title: language === 'zh' ? '网格错误' : 'Mesh errors', sourceType: 'bool' },
      { id: 'HasPropertyTables', title: language === 'zh' ? '属性表' : 'Property tables', sourceType: 'bool' },
    ],
    editableFields: [
      { id: 'Name', title: gt(language, 'name'), sourceType: 'string' },
      { id: 'TargetedCellSpacing', title: gt(language, 'targetSpacing'), sourceType: 'double', minimum: 0 },
    ],
    rows: snapshot.flowAreas.map((area) => ({
      id: area.id,
      fid: area.rowIndex,
      values: {
        Name: area.name,
        TargetedCellSpacing: area.targetedCellSpacing == null || Number.isNaN(area.targetedCellSpacing) ? '' : String(area.targetedCellSpacing),
        HasMesh: String(area.hasMesh),
        CellCount: String(area.cellCount),
        FaceCount: String(area.faceCount),
        NodeCount: String(area.nodeCount),
        HasMeshErrors: String(area.hasMeshErrors),
        HasPropertyTables: String(area.hasPropertyTables),
      },
    })),
  };
}

function pretty(value: unknown): string {
  if (value == null) return '';
  return typeof value === 'string' ? value : JSON.stringify(value, null, 2);
}

export function GeometryMeshPage({
  language, snapshot, localRegionRows, busy, canEdit, canGenerate, canPrepare, canDegreeScan, canPropertyTables,
  onFlowAreaChange, onRegionSelection, onPatchMutation, onPrepare, onGenerate, onDegreeScan, onPropertyTables,
}: GeometryMeshPageProps) {
  const [maximumFaces, setMaximumFaces] = useState('8');
  const [progress, setProgress] = useState('');
  const [taskError, setTaskError] = useState('');
  const [degreeOutput, setDegreeOutput] = useState('');
  const [degreeScanResult, setDegreeScanResult] = useState<GeometryMeshDegreeScan>();
  const [propertyOutput, setPropertyOutput] = useState('');
  const flowAreas = useMemo(() => flowAreaTable(snapshot, language), [language, snapshot]);

  const generate = async (): Promise<void> => {
    setTaskError(''); setProgress('');
    try {
      await onGenerate((value) => setProgress(pretty(value)));
    } catch (error) {
      setTaskError(geometryDiagnosticMessage(error));
    }
  };
  const scan = async (): Promise<void> => {
    const limit = Number(maximumFaces);
    if (!Number.isInteger(limit) || limit < 1) { setTaskError(language === 'zh' ? '最大面数必须是正整数。' : 'Maximum faces must be a positive integer.'); return; }
    setTaskError('');
    setDegreeScanResult(undefined); setDegreeOutput('');
    try { const result = await onDegreeScan(limit); setDegreeScanResult(result); setDegreeOutput(pretty(result)); }
    catch (error) { setTaskError(geometryDiagnosticMessage(error)); }
  };
  const computeTables = async (): Promise<void> => {
    setTaskError('');
    try { setPropertyOutput(pretty(await onPropertyTables())); }
    catch (error) { setTaskError(geometryDiagnosticMessage(error)); }
  };
  const handleAreaEdit = (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean): void => {
    if (field.id === 'TargetedCellSpacing' && value !== '') {
      const numeric = Number(value);
      if (!Number.isFinite(numeric) || numeric <= 0) { setTaskError(gt(language, 'positive')); return; }
    }
    setTaskError('');
    onFlowAreaChange(row, field, value, isLocal);
  };
  return (
    <div className="geometry-grid">
      <FlowAreaLayerActiveFeatures language={language} table={flowAreas} canEdit={canEdit && !!snapshot?.canEdit} busy={busy} onChange={handleAreaEdit} headerActions={<>
        <button className="geometry-secondary" disabled={!canPrepare || busy || !canEdit} onClick={onPrepare}>{gt(language, 'prepare')}</button>
        <button className="geometry-primary" disabled={!canGenerate || busy || !canEdit || !snapshot} onClick={() => void generate()}>{busy ? gt(language, 'generating') : gt(language, 'generate')}</button>
      </>}>
        {!canGenerate && <div className="geometry-inline-help" style={{ padding: '0 16px 12px' }}>{gt(language, 'unavailable')}</div>}
        {snapshot?.flowAreas.some((area) => area.hasMeshErrors) && <div className="geometry-validation-list"><div className="geometry-validation-item">{language === 'zh' ? '至少一个流动区域包含网格错误。' : 'At least one flow area contains mesh errors.'}</div></div>}
        {progress && <details className="geometry-panel-body"><summary>{gt(language, 'serverProgress')}</summary><pre className="geometry-report-item">{progress}</pre></details>}
        {taskError && <GeometryError language={language} error={taskError} style={{ margin: 13 }} />}
      </FlowAreaLayerActiveFeatures>
      <GeometryPrepareSummaryControl language={language} summary={snapshot?.prepareSummary} pending={!!snapshot?.preparePending} />
      <PatchEditor language={language} snapshot={snapshot} localRows={localRegionRows} canEdit={canEdit} busy={busy}
        onRegionSelection={onRegionSelection} onMutation={onPatchMutation} />
      <section className="geometry-panel">
        <header className="geometry-panel-heading"><div><h2>{gt(language, 'degreeScan')} / {gt(language, 'propertyTables')}</h2><p>{gt(language, 'validationNote')}</p></div></header>
        <div className="geometry-panel-body geometry-mesh-controls">
          <label className="geometry-form-field">{gt(language, 'maxFaces')}<input type="number" min={1} step={1} value={maximumFaces} onChange={(event) => setMaximumFaces(event.target.value)} /></label>
          <button className="geometry-secondary" disabled={!canDegreeScan || busy || !snapshot} onClick={() => void scan()}>{gt(language, 'degreeScan')}</button>
          <button className="geometry-secondary" disabled={!canPropertyTables || busy || !snapshot} onClick={() => void computeTables()}>{gt(language, 'propertyTables')}</button>
        </div>
        {propertyOutput && <details className="geometry-report-item" style={{ margin: '0 14px 14px' }}><summary>{gt(language, 'propertyOutput')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{propertyOutput}</pre></details>}
      </section>
      <MeshDiagnostics language={language} snapshot={snapshot} scan={degreeScanResult} rawOutput={degreeOutput} />
    </div>
  );
}

export default GeometryMeshPage;
