import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTopologyFactorSelection } from './geometryTypes';

export interface ConceptualMeshContextualControlProps {
  language: Language;
  featureKind: 'arc' | 'region';
  rows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onApplyFactor?: (factor: number, selection?: GeometryTopologyFactorSelection) => Promise<void>;
}

const factors = [0.25, 0.5, 2, 4];

/** Applies source TopologyProperties factors to the active arc or control-point selection. */
export function ConceptualMeshContextualControl({ language, featureKind, rows, canEdit, busy, onApplyFactor }: ConceptualMeshContextualControlProps) {
  const [error, setError] = useState('');
  const [factorText, setFactorText] = useState('1');
  const ids = rows.map((row) => row.fid);
  const selectionIsValid = rows.length > 0 && ids.every((fid) => fid != null && Number.isInteger(fid) && fid >= 0)
    && new Set(ids).size === ids.length;
  const selectionKey = ids.join(',');
  const customFactor = Number(factorText);
  const disabled = !selectionIsValid || !canEdit || busy || !onApplyFactor;
  const featureName = featureKind === 'arc'
    ? (language === 'zh' ? '弧线' : 'arcs')
    : (language === 'zh' ? '区域控制点' : 'region control points');
  const factorDescription = featureKind === 'arc'
    ? (language === 'zh' ? '调整常量尺寸，并按反向比例调整点数。' : 'Changes constant size and adjusts point count by the inverse factor.')
    : (language === 'zh' ? '调整常量尺寸和次级常量尺寸。' : 'Changes constant and secondary constant sizes.');

  const apply = async (factor: number): Promise<void> => {
    if (disabled || !onApplyFactor) return;
    setError('');
    try {
      await onApplyFactor(factor, { featureKind, featureIDs: ids as number[] });
    } catch (reason) {
      setError(geometryDiagnosticMessage(reason));
    }
  };

  useEffect(() => setError(''), [selectionKey]);

  return <section className="geometry-panel" aria-label={language === 'zh' ? '概念网格选择操作' : 'Conceptual mesh selection actions'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '所选拓扑比例调整' : 'Scale selected topology features'}</h3>
      <p>{selectionIsValid
        ? `${rows.length} ${featureName} · ${factorDescription} ${language === 'zh' ? '改动暂存于草稿，可使用工作区保存或撤销。' : 'Changes stay in the draft and use workspace save or undo.'}`
        : (language === 'zh' ? '先从当前表格选择一个或多个源要素。' : 'Select one or more source features from the current table first.')}</p>
    </div><span className="geometry-count-pill">{rows.length} {language === 'zh' ? '项已选' : 'selected'}</span></header>
    {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
    {selectionIsValid && <div className="geometry-panel-body geometry-toolbar-group">
      {factors.map((factor) => <button key={factor} className="geometry-secondary" type="button" disabled={disabled}
        onClick={() => void apply(factor)}>{factor < 1 ? `${factor}× · ${language === 'zh' ? '细化' : 'Refine'}` : `${factor}× · ${language === 'zh' ? '粗化' : 'Coarsen'}`}</button>)}
      <label className="geometry-form-field">{language === 'zh' ? '自定义比例' : 'Custom factor'}
        <input type="number" min="0.000001" step="any" value={factorText} disabled={disabled} onChange={(event) => setFactorText(event.target.value)} />
      </label>
      <button className="geometry-secondary" type="button" disabled={disabled || !Number.isFinite(customFactor) || customFactor <= 0}
        onClick={() => void apply(customFactor)}>{language === 'zh' ? '应用自定义比例' : 'Apply custom factor'}</button>
    </div>}
  </section>;
}

export default ConceptualMeshContextualControl;
