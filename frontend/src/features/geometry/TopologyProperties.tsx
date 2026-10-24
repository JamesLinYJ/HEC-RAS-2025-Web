import { useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryTopologySnapshot } from './geometryTypes';

export interface TopologyPropertiesProps {
  language: Language;
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onApplyFactor?: (factor: number) => Promise<void>;
}

/** Factor controls for the topology fields supported by the current workspace. */
export function TopologyProperties({ language, snapshot, canEdit, busy, onApplyFactor }: TopologyPropertiesProps) {
  const [factorText, setFactorText] = useState('1');
  const [error, setError] = useState('');
  const apply = async (factor: number): Promise<void> => {
    if (!Number.isFinite(factor) || factor <= 0 || !onApplyFactor) return;
    setError('');
    try { await onApplyFactor(factor); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };
  const customFactor = Number(factorText);
  const disabled = !snapshot || !canEdit || busy || !onApplyFactor;
  return <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑属性' : 'Topology properties'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '拓扑属性缩放' : 'Topology property scaling'}</h3>
      <p>{language === 'zh'
        ? '调整全部控制点的常量尺寸和次级常量尺寸，以及全部弧线的常量尺寸；弧线点数按相反比例调整，且至少保留 1 个点。修改暂存于草稿，保存后生效。'
        : 'Adjusts constant and secondary constant sizes for all control points and constant sizes for all arcs. Arc point counts change by the inverse factor and remain at least 1. Changes stay in the draft until saved.'}</p>
    </div></header>
    {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
    <div className="geometry-panel-body geometry-toolbar-group">
      {[0.25, 0.5, 2, 4].map((factor) => <button key={factor} className="geometry-secondary" type="button" disabled={disabled}
        onClick={() => void apply(factor)}>{factor < 1 ? `${factor}× · ${language === 'zh' ? '细化' : 'Refine'}` : `${factor}× · ${language === 'zh' ? '粗化' : 'Coarsen'}`}</button>)}
      <label className="geometry-form-field">{language === 'zh' ? '自定义比例' : 'Custom factor'}
        <input type="number" min="0.000001" step="any" value={factorText} disabled={disabled} onChange={(event) => setFactorText(event.target.value)} />
      </label>
      <button className="geometry-secondary" type="button" disabled={disabled || !Number.isFinite(customFactor) || customFactor <= 0}
        onClick={() => void apply(customFactor)}>{language === 'zh' ? '应用到全部拓扑' : 'Apply to all topology'}</button>
    </div>
  </section>;
}

export default TopologyProperties;
