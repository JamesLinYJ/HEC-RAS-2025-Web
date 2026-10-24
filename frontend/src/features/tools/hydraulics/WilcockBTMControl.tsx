import { WilcockISBTMControl } from './WilcockISBTMControl';
import { WilcockSBTMControl } from './WilcockSBTMControl';
import type { WilcockDraft } from './wilcockTypes';

export function WilcockBTMControl({ draft, mode, onDraftChange }: {
  draft: WilcockDraft;
  mode: string;
  onDraftChange: (draft: WilcockDraft) => void;
}) {
  const activeMode = mode === 'ISBTM' ? 'ISBTM' : 'SBTM';
  const sbtm = draft.sbtm ?? {};
  const isbtm = draft.isbtm ?? {};
  const updateSbtmScalar = (key: 'shearVelocity' | 'waterDensity' | 'sedimentDensity', value: string) => {
    onDraftChange({ ...draft, sbtm: { ...sbtm, [key]: value } });
  };
  const updateSbtmArray = (key: 'grainDiameter' | 'surfaceCdf', index: number, value: string) => {
    const source = sbtm[key];
    const values = Array.isArray(source) ? [...source] : [];
    if (index >= 0 && index < values.length) values[index] = value;
    onDraftChange({ ...draft, sbtm: { ...sbtm, [key]: values } });
  };
  const updateIsbtmScalar = (_key: 'totalTransportRate', value: string) => {
    onDraftChange({ ...draft, isbtm: { ...isbtm, totalTransportRate: value } });
  };
  const updateIsbtmArray = (key: 'grainDiameter' | 'transportCdf', index: number, value: string) => {
    const source = isbtm[key];
    const values = Array.isArray(source) ? [...source] : [];
    if (index >= 0 && index < values.length) values[index] = value;
    onDraftChange({ ...draft, isbtm: { ...isbtm, [key]: values } });
  };

  return <section className="hydraulics-source-control" aria-label="Wilcock bed-load transport model">
    <div className="hydraulics-tabs" role="tablist" aria-label="底床输沙模式">
      {(['SBTM', 'ISBTM'] as const).map(value => <button
        key={value}
        type="button"
        role="tab"
        aria-selected={activeMode === value}
        className={activeMode === value ? 'hydraulics-tab is-active' : 'hydraulics-tab'}
        onClick={() => onDraftChange({ ...draft, btmMode: value })}
      >{value}</button>)}
    </div>
    {activeMode === 'ISBTM'
      ? <WilcockISBTMControl draft={isbtm} onScalarChange={updateIsbtmScalar} onArrayChange={updateIsbtmArray} />
      : <WilcockSBTMControl draft={sbtm} onScalarChange={updateSbtmScalar} onArrayChange={updateSbtmArray} />}
  </section>;
}
