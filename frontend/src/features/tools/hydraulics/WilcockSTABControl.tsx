import { BooleanInput, NumericArrayEditor, NumericMatrixEditor, ScalarInput, sourceText } from './WilcockFields';
import type { WilcockCaseChoice, WilcockStabDraft } from './wilcockTypes';

export function WilcockSTABControl({
  draft, cases, selectedCaseID, canAddCase, canRemoveCase, importAvailable,
  onScalarChange, onBooleanChange, onArrayChange, onMatrixChange, onCaseSelect, onCaseAction, onImportCase,
}: {
  draft: WilcockStabDraft;
  cases: WilcockCaseChoice[];
  selectedCaseID: string | null;
  canAddCase: boolean;
  canRemoveCase: boolean;
  importAvailable: boolean;
  onScalarChange: (key: 'bMin' | 'bMax' | 'z' | 'ns' | 'nSpec' | 'tcTauStarC' | 'tcD50' | 'tcD84' | 'tcM', value: string) => void;
  onBooleanChange: (key: 'useThresholdSlope', value: boolean) => void;
  onArrayChange: (key: 'waterDischarges' | 'sedimentSupplyRates' | 'grainDiameter', index: number, value: string) => void;
  onMatrixChange: (row: number, column: number, value: string) => void;
  onCaseSelect: (caseID: string) => void;
  onCaseAction: (action: 'addCase' | 'removeCase', caseID?: string) => void;
  onImportCase: () => void;
}) {
  const water = Array.isArray(draft.waterDischarges) ? draft.waterDischarges : [];
  const supply = Array.isArray(draft.sedimentSupplyRates) ? draft.sedimentSupplyRates : [];
  const selected = cases.find(item => item.id === selectedCaseID) ?? cases[0];

  return <section className="hydraulics-section" aria-label="STAB 输入">
    <div className="hydraulics-section-title"><div><h3>输沙与断面设计 · STAB</h3><p>输入和案例结构由后端 Wilcock 工作区快照提供。</p></div>
      <div className="hydraulics-actions">
        <button type="button" className="hydraulics-button" disabled={!canAddCase} onClick={() => onCaseAction('addCase')}>添加工况</button>
        <button type="button" className="hydraulics-button" disabled={!canRemoveCase || !selected} onClick={() => selected && onCaseAction('removeCase', selected.id)}>删除选中工况</button>
      </div>
    </div>
    <div className="hydraulics-table-scroll"><table className="hydraulics-table hydraulics-case-table">
      <thead><tr><th>工况 ID</th><th>水流量 <code>WaterDischarges</code></th><th>泥沙供给率 <code>SedimentSupplyRates</code></th><th>操作</th></tr></thead>
      <tbody>{cases.map(item => <tr key={item.id} className={selected?.id === item.id ? 'is-selected' : ''}>
        <th><button type="button" className="hydraulics-row-select" aria-pressed={selected?.id === item.id} onClick={() => onCaseSelect(item.id)}>{item.id}</button></th>
        <td><input aria-label={`WaterDischarges ${item.index + 1}`} inputMode="decimal" value={sourceText(water[item.index])} disabled={water[item.index] === undefined} onChange={event => onArrayChange('waterDischarges', item.index, event.currentTarget.value)} /></td>
        <td><input aria-label={`SedimentSupplyRates ${item.index + 1}`} inputMode="decimal" value={sourceText(supply[item.index])} disabled={supply[item.index] === undefined} onChange={event => onArrayChange('sedimentSupplyRates', item.index, event.currentTarget.value)} /></td>
        <td><button type="button" className="hydraulics-link-button" disabled={!canRemoveCase} onClick={() => onCaseAction('removeCase', item.id)}>删除</button></td>
      </tr>)}</tbody>
    </table></div>
    <div className="hydraulics-field-grid">
      <ScalarInput label="最小底宽" sourceName="BMin" value={draft.bMin} onChange={value => onScalarChange('bMin', value)} />
      <ScalarInput label="最大底宽" sourceName="BMax" value={draft.bMax} onChange={value => onScalarChange('bMax', value)} />
      <ScalarInput label="边坡系数" sourceName="Z" value={draft.z} onChange={value => onScalarChange('z', value)} />
      <ScalarInput label="边坡糙率" sourceName="Ns" value={draft.ns} onChange={value => onScalarChange('ns', value)} />
      <ScalarInput label="指定糙率 n" sourceName="Nspec" value={draft.nSpec} onChange={value => onScalarChange('nSpec', value)} />
    </div>
    <BooleanInput label="启用阈值坡度" sourceName="UseThresholdSlope" value={draft.useThresholdSlope} onChange={value => onBooleanChange('useThresholdSlope', value)} />
    {draft.useThresholdSlope === true && <div className="hydraulics-field-grid">
      <ScalarInput label="阈值 τ*" sourceName="TcTauStarC" value={draft.tcTauStarC} onChange={value => onScalarChange('tcTauStarC', value)} />
      <ScalarInput label="阈值 D50" sourceName="TcD50" value={draft.tcD50} onChange={value => onScalarChange('tcD50', value)} />
      <ScalarInput label="阈值 D84" sourceName="TcD84" value={draft.tcD84} onChange={value => onScalarChange('tcD84', value)} />
      <ScalarInput label="阈值指数 M" sourceName="TcM" value={draft.tcM} onChange={value => onScalarChange('tcM', value)} />
    </div>}
    <NumericArrayEditor label="粒径" sourceName="GrainDiameter" values={draft.grainDiameter} onChange={(index, value) => onArrayChange('grainDiameter', index, value)} />
    <NumericMatrixEditor label="各工况输沙粒径分布" sourceName="TransportGrainSizes" values={draft.transportGrainSizes} columnLabels={draft.grainDiameter} onChange={onMatrixChange} />
    <div className="hydraulics-import-case">
      <p>从当前 {draft.btmMode === 'ISBTM' ? 'ISBTM' : 'SBTM'} 床载输沙结果导入 STAB 工况；导入前将检查兼容性与替换范围。</p>
      <button type="button" className="hydraulics-button hydraulics-button-primary" disabled={!importAvailable} onClick={onImportCase}>从 BTM 导入工况…</button>
    </div>
  </section>;
}
