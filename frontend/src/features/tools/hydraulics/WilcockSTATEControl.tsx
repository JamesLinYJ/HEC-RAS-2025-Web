import { NumericArrayEditor } from './WilcockFields';
import type { WilcockStateDraft } from './wilcockTypes';

export function WilcockSTATEControl({ draft, onArrayChange }: {
  draft: WilcockStateDraft;
  onArrayChange: (key: 'grainDiameter' | 'firstTransportCdfGrainSize' | 'secondTransportCdfGrainSize' | 'transportRates' | 'slopes', index: number, value: string) => void;
}) {
  return <section className="hydraulics-section" aria-label="STATE 输入">
    <h3>输沙状态分析 · STATE</h3>
    <NumericArrayEditor label="粒径" sourceName="GrainDiameter" values={draft.grainDiameter} onChange={(index, value) => onArrayChange('grainDiameter', index, value)} />
    <NumericArrayEditor label="第一组输沙累积百分含量" sourceName="FirstTransportCdfGrainSize" values={draft.firstTransportCdfGrainSize} onChange={(index, value) => onArrayChange('firstTransportCdfGrainSize', index, value)} />
    <NumericArrayEditor label="第二组输沙累积百分含量" sourceName="SecondTransportCdfGrainSize" values={draft.secondTransportCdfGrainSize} onChange={(index, value) => onArrayChange('secondTransportCdfGrainSize', index, value)} />
    <NumericArrayEditor label="输沙率" sourceName="TransportRates" values={draft.transportRates} onChange={(index, value) => onArrayChange('transportRates', index, value)} />
    <NumericArrayEditor label="坡度" sourceName="Slopes" values={draft.slopes} onChange={(index, value) => onArrayChange('slopes', index, value)} />
  </section>;
}
