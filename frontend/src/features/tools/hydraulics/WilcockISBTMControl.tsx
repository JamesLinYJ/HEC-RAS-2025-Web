import { NumericArrayEditor, ScalarInput } from './WilcockFields';
import type { WilcockIsbtmDraft } from './wilcockTypes';

export function WilcockISBTMControl({ draft, onScalarChange, onArrayChange }: {
  draft: WilcockIsbtmDraft;
  onScalarChange: (key: 'totalTransportRate', value: string) => void;
  onArrayChange: (key: 'grainDiameter' | 'transportCdf', index: number, value: string) => void;
}) {
  return <section className="hydraulics-section" aria-label="ISBTM 输入">
    <h3>级配输沙 · ISBTM</h3>
    <div className="hydraulics-field-grid">
      <ScalarInput label="总输沙率" sourceName="TotalTransportRate" value={draft.totalTransportRate} onChange={value => onScalarChange('totalTransportRate', value)} />
    </div>
    <NumericArrayEditor label="粒径" sourceName="GrainDiameter" values={draft.grainDiameter} onChange={(index, value) => onArrayChange('grainDiameter', index, value)} />
    <NumericArrayEditor label="输沙累积百分含量" sourceName="TransportCdf" values={draft.transportCdf} onChange={(index, value) => onArrayChange('transportCdf', index, value)} />
  </section>;
}
