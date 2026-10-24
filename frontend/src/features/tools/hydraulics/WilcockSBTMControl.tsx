import { NumericArrayEditor, ScalarInput } from './WilcockFields';
import type { WilcockSbtmDraft } from './wilcockTypes';

export function WilcockSBTMControl({ draft, onScalarChange, onArrayChange }: {
  draft: WilcockSbtmDraft;
  onScalarChange: (key: 'shearVelocity' | 'waterDensity' | 'sedimentDensity', value: string) => void;
  onArrayChange: (key: 'grainDiameter' | 'surfaceCdf', index: number, value: string) => void;
}) {
  return <section className="hydraulics-section" aria-label="SBTM 输入">
    <h3>表面床载输沙 · SBTM</h3>
    <div className="hydraulics-field-grid">
      <ScalarInput label="床面剪切流速" sourceName="ShearVelocity" value={draft.shearVelocity} onChange={value => onScalarChange('shearVelocity', value)} />
      <ScalarInput label="水体密度" sourceName="WaterDensity" value={draft.waterDensity} onChange={value => onScalarChange('waterDensity', value)} />
      <ScalarInput label="泥沙密度" sourceName="SedimentDensity" value={draft.sedimentDensity} onChange={value => onScalarChange('sedimentDensity', value)} />
    </div>
    <NumericArrayEditor label="粒径" sourceName="GrainDiameter" values={draft.grainDiameter} onChange={(index, value) => onArrayChange('grainDiameter', index, value)} />
    <NumericArrayEditor label="表面累积百分含量" sourceName="SurfaceCdf" values={draft.surfaceCdf} onChange={(index, value) => onArrayChange('surfaceCdf', index, value)} />
  </section>;
}
