import { partNames, solutionMethods, solveTargets } from './stableChannelWorkflow';
import type { ChannelDraft, SolutionMethod, SolveTarget } from './stableChannelWorkflow';

export const channelTargetLabels: Record<SolveTarget, string> = { Depth: '水深', Width: '底宽', Slope: '纵坡', ParticleSize: '粒径' };
export const channelPartLabels = { leftBank: '左岸', bed: '河床', rightBank: '右岸' } as const;
const methodLabels: Record<SolutionMethod, string> = { Lane: 'Lane', Shields: 'Shields', UserDefinedMobility: '用户指定临界起动参数' };
export interface TractiveForceFormProps { draft: ChannelDraft; disabled: boolean; onChange: (update: (draft: ChannelDraft) => ChannelDraft) => void }

/** Source role: method, exactly-two solve targets, target values and user mobility inputs. */
export function TractiveForceSolveFor({ draft, disabled, onChange }: TractiveForceFormProps) {
  return <section className="stable-card">
    <h3>求解方法与目标</h3>
    <label className="stable-field"><span>求解方法</span><select data-testid="stable-solution-method" value={draft.solutionMethod} disabled={disabled}
      onChange={event => onChange(current => ({ ...current, solutionMethod: event.target.value as SolutionMethod }))}>
      {solutionMethods.map(method => <option key={method} value={method}>{methodLabels[method]}</option>)}
    </select></label>
    <fieldset className="stable-targets" disabled={disabled}><legend>选择两个未知量</legend>
      {solveTargets.map(target => <label key={target} className={draft.solveFor.includes(target) ? 'stable-target selected' : 'stable-target'}>
        <input type="checkbox" data-testid={`stable-target-${target}`} checked={draft.solveFor.includes(target)}
          onChange={event => onChange(current => ({ ...current, solveFor: event.target.checked
            ? solveTargets.filter(item => item === target || current.solveFor.includes(item))
            : current.solveFor.filter(item => item !== target) }))} />{channelTargetLabels[target]}
      </label>)}
    </fieldset>
    <p className="stable-help" data-testid="stable-target-count">已选择 {draft.solveFor.length} / 2 项。输入完整时，修改参数会自动更新计算。</p>
    <div className="stable-grid">
      {(['Depth', 'Width', 'Slope'] as const).map(target => {
        const field = target.toLowerCase() as 'depth' | 'width' | 'slope';
        return <label className="stable-field" key={target}><span>{channelTargetLabels[target]}{target === 'Slope' ? '（ft/ft）' : '（ft）'}{draft.solveFor.includes(target) && <em>求解值</em>}</span>
          <input inputMode="decimal" type="text" data-testid={`stable-${field}`} value={draft[field]} disabled={disabled} readOnly={draft.solveFor.includes(target)}
            onChange={event => onChange(current => ({ ...current, [field]: event.target.value }))} />
        </label>;
      })}
    </div>
    <div className="stable-grid">
      {partNames.map(part => <label className="stable-field" key={part}><span>{channelPartLabels[part]}粒径 D（mm）{draft.solveFor.includes('ParticleSize') && <em>求解值</em>}</span>
        <input inputMode="decimal" type="text" data-testid={`stable-${part}-d`} value={draft[part].d} disabled={disabled} readOnly={draft.solveFor.includes('ParticleSize')}
          onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], d: event.target.value } }))} />
      </label>)}
    </div>
    {draft.solutionMethod === 'UserDefinedMobility' && <div className="stable-grid" data-testid="stable-user-mobility">
      {partNames.map(part => <label className="stable-field" key={part}><span>{channelPartLabels[part]} Shields 参数（无量纲）</span>
        <input inputMode="decimal" type="text" data-testid={`stable-${part}-shieldsParameter`} value={draft[part].shieldsParameter} disabled={disabled}
          onChange={event => onChange(current => ({ ...current, [part]: { ...current[part], shieldsParameter: event.target.value } }))} />
      </label>)}
    </div>}
  </section>;
}
