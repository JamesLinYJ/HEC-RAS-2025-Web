import { equations, partNames } from './stableChannelWorkflow';
import type { Equation, ScalarName } from './stableChannelWorkflow';
import type { TractiveForceFormProps } from './TractiveForceSolveFor';
import { channelPartLabels } from './TractiveForceSolveFor';

const requiredFields: Array<{ field: ScalarName; label: string }> = [
  { field: 'discharge', label: '流量（ft³/s）' }, { field: 'temperature', label: '水温（°F，40–120）' },
  { field: 'leftBankSideSlope', label: '左岸边坡（水平 : 垂直）' }, { field: 'rightBankSideSlope', label: '右岸边坡（水平 : 垂直）' },
];
/** Source role: flow/temperature, bank slopes and separate material/roughness inputs for all three parts. */
export function TractiveForceRequiredInput({ draft, disabled, onChange }: TractiveForceFormProps) {
  return <section className="stable-card">
    <h3>已知水力与材料参数</h3>
    <div className="stable-grid stable-four">
      {requiredFields.map(({ field, label }) => <label className="stable-field" key={field}><span>{label}</span>
        <input inputMode="decimal" type="text" data-testid={`stable-${field}`} disabled={disabled} value={draft[field]}
          onChange={event => onChange(current => ({ ...current, [field]: event.target.value }))} />
      </label>)}
    </div>
    <div className="stable-grid">
      {partNames.map(part => <fieldset className="stable-material" key={part} disabled={disabled}><legend>{channelPartLabels[part]}</legend>
        <label className="stable-field"><span>颗粒比重（无量纲）</span>
          <input inputMode="decimal" type="text" data-testid={`stable-${part}-specificGravity`} value={draft[part].specificGravity}
            onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], specificGravity: event.target.value } }))} /></label>
        <label className="stable-field"><span>休止角（°）</span>
          <input inputMode="decimal" type="text" data-testid={`stable-${part}-angleOfRepose`} value={draft[part].angleOfRepose}
            onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], angleOfRepose: event.target.value } }))} /></label>
        <label className="stable-field"><span>糙率公式</span><select data-testid={`stable-${part}-equation`} value={draft[part].equation}
          onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], equation: event.target.value as Equation } }))}>
          {equations.map(equation => <option key={equation} value={equation}>{equation}</option>)}</select></label>
        <label className="stable-field"><span>{draft[part].equation === 'Manning' ? 'Manning 糙率 n' : 'Strickler 粗糙粒径（ft）'}</span>
          <input inputMode="decimal" type="text" data-testid={`stable-${part}-roughness`} value={draft[part].roughness}
            onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], roughness: event.target.value } }))} /></label>
        {draft[part].equation === 'Strickler' && <small>按原公式 n = 0.039 × 粗糙粒径¹⁄⁶ 计算。</small>}
      </fieldset>)}
    </div>
  </section>;
}
