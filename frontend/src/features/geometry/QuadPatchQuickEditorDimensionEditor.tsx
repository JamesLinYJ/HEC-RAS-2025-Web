import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { QuickRatioCellSizeButton } from './QuickRatioCellSizeButton';
import type { GeometryMeshPatchDimension, GeometryMeshPatchMutation } from './geometryTypes';

export interface QuadPatchQuickEditorDimensionEditorProps {
  language: Language;
  dimension: GeometryMeshPatchDimension;
  selectedRegionIDs: number[];
  canEdit: boolean;
  busy: boolean;
  sideArcSelectionIsCurrent: boolean;
  onMutation: (mutation: GeometryMeshPatchMutation) => Promise<void>;
}

function modeLabel(language: Language, mode: string): string {
  if (language !== 'zh') return mode;
  if (mode === 'Constant') return '固定尺寸';
  if (mode === 'CountWithBias') return '点数与偏差';
  return mode;
}

export function QuadPatchQuickEditorDimensionEditor({
  language, dimension, selectedRegionIDs, canEdit, busy, sideArcSelectionIsCurrent, onMutation,
}: QuadPatchQuickEditorDimensionEditorProps) {
  const mode = dimension.sizeFunction ?? '';
  const [value, setValue] = useState(dimension.liveValueHasMismatch || dimension.liveValue == null ? '' : String(dimension.liveValue));
  const [dirty, setDirty] = useState(false);
  const supportedMode = mode === 'Constant' || mode === 'CountWithBias';
  const axis = dimension.id === 'NorthSouth' || dimension.id === 'EastWest' ? dimension.id : undefined;
  const issues = dimension.sideArcIssues ?? [];
  const sideArcIDs = dimension.sideArcSelection?.arcFeatureIDs ?? [];
  const canTarget = !!axis && sideArcSelectionIsCurrent && issues.length === 0 && sideArcIDs.length > 0;
  const dimensionTitle = dimension.id === 'NorthSouth'
    ? (language === 'zh' ? '南北向（北侧与南侧）' : 'North–South (north and south sides)')
    : dimension.id === 'EastWest'
      ? (language === 'zh' ? '东西向（东侧与西侧）' : 'East–West (east and west sides)')
      : dimension.title;

  useEffect(() => {
    if (dirty) return;
    setValue(dimension.liveValueHasMismatch || dimension.liveValue == null ? '' : String(dimension.liveValue));
  }, [dimension.liveValue, dimension.liveValueHasMismatch, dirty]);

  const setMode = (next: string): void => {
    if (!axis || (next !== 'Constant' && next !== 'CountWithBias')) return;
    setDirty(false);
    void onMutation({ operation: 'setPatchDimensionMode', featureIDs: selectedRegionIDs, axis, value: next })
      .catch(() => undefined);
  };

  const applyValue = (): void => {
    const parsed = Number(value.trim());
    if (!axis || !supportedMode || !Number.isFinite(parsed)) return;
    if (mode === 'Constant' ? parsed <= 0 : !Number.isInteger(parsed) || parsed < 1) return;
    void onMutation({ operation: 'applyPatchDimension', featureIDs: selectedRegionIDs, axis, value: value.trim() })
      .then(() => setDirty(false))
      .catch(() => undefined);
  };

  const resize = (factor: 0.5 | 2): void => {
    if (!axis) return;
    void onMutation({ operation: 'resizePatch', featureIDs: selectedRegionIDs, axis, value: factor === 0.5 ? '0.5' : '2' })
      .catch(() => undefined);
  };

  return <section className="geometry-panel" aria-label={`${dimensionTitle} ${language === 'zh' ? '尺寸设置' : 'sizing settings'}`}>
    <header className="geometry-panel-heading"><div>
      <h4>{dimensionTitle}</h4>
      <p>{dimension.arcCount} {language === 'zh' ? '条源弧段' : 'source arcs'} · {dimension.sizeFunctionHasMismatch
        ? (language === 'zh' ? '尺寸模式不一致' : 'Mixed sizing modes')
        : (mode ? modeLabel(language, mode) : (language === 'zh' ? '尚未设置尺寸模式' : 'No sizing mode set'))}</p>
    </div></header>
    <div className="geometry-panel-body geometry-grid two">
      <label className="geometry-form-field">{language === 'zh' ? '尺寸模式' : 'Sizing mode'}
        <select value={mode} disabled={!canEdit || busy || !dimension.canChangeMode || !canTarget} onChange={(event) => setMode(event.target.value)}>
          <option value="">{dimension.sizeFunctionHasMismatch ? (language === 'zh' ? '多个模式' : 'Mixed modes') : '—'}</option>
          {mode && mode !== 'Constant' && mode !== 'CountWithBias' ? <option value={mode}>{mode}</option> : null}
          <option value="Constant">{modeLabel(language, 'Constant')}</option>
          <option value="CountWithBias">{modeLabel(language, 'CountWithBias')}</option>
        </select>
      </label>
      <label className="geometry-form-field">{mode === 'CountWithBias' ? (language === 'zh' ? '当前点数' : 'Current point count') : (language === 'zh' ? '当前固定尺寸' : 'Current cell size')}
        <input type="number" step={mode === 'CountWithBias' ? 1 : 'any'} value={value}
          placeholder={dimension.liveValueHasMismatch ? (language === 'zh' ? '所选弧段数值不一致' : 'Selected arcs have different values') : undefined}
          disabled={!canEdit || busy || !dimension.canApply || !canTarget || !supportedMode}
          onChange={(event) => { setValue(event.target.value); setDirty(true); }}
          onBlur={() => { if (!value.trim()) setDirty(false); }} />
      </label>
    </div>
    <div className="geometry-panel-body geometry-toolbar-group">
      <button className="geometry-primary" type="button" disabled={!canEdit || busy || !dimension.canApply || !canTarget || !supportedMode
        || !Number.isFinite(Number(value.trim())) || (mode === 'Constant' ? Number(value) <= 0 : !Number.isInteger(Number(value)) || Number(value) < 1)} onClick={applyValue}>
        {language === 'zh' ? '应用尺寸' : 'Apply sizing'}
      </button>
      <QuickRatioCellSizeButton language={language} factor={0.5} axis={axis}
        disabled={!canEdit || busy || !dimension.canResize || !canTarget} onResize={resize} />
      <QuickRatioCellSizeButton language={language} factor={2} axis={axis}
        disabled={!canEdit || busy || !dimension.canResize || !canTarget} onResize={resize} />
    </div>
    {!sideArcSelectionIsCurrent && <div className="geometry-validation-item">{language === 'zh'
      ? '弧段选择与当前源修订、拓扑指纹或所选区域不一致；请重新读取尺寸信息。'
      : 'The arc selection does not match the current source revision, topology fingerprint, or selected regions. Reload sizing information.'}</div>}
    {issues.map((issue, index) => <div className="geometry-validation-item" key={`${issue.regionFID}-${issue.axis}-${issue.code}-${index}`}>
      <strong>{issue.code}</strong><pre style={{ whiteSpace: 'pre-wrap' }}>{issue.message}</pre>
    </div>)}
    <details className="geometry-inline-help"><summary>{language === 'zh' ? `查看源弧段 FID（${sideArcIDs.length}）` : `Show source arc FIDs (${sideArcIDs.length})`}</summary>
      {sideArcIDs.length ? <ul>{sideArcIDs.map((fid) => <li key={fid}>{fid}</li>)}</ul> : <p>{language === 'zh' ? '当前快照没有可用弧段。' : 'The current snapshot contains no usable arcs.'}</p>}
    </details>
  </section>;
}

export default QuadPatchQuickEditorDimensionEditor;
