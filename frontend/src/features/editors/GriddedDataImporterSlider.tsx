import type { Language } from '../../shared/workbench';

/** Normalized time scrubber from GriddedDataImporterSlider, bound to real model frames. */
export function GriddedDataImporterSlider(props: {
  language: Language;
  times: string[];
  components: string[];
  selectedTimeId: string;
  selectedComponent: string;
  disabled?: boolean;
  onChange: (timeId: string, component: string) => void;
}) {
  const { language, times, components } = props;
  const index = Math.max(0, times.indexOf(props.selectedTimeId));
  const normalized = times.length <= 1 ? 0 : index / (times.length - 1);
  const component = components.includes(props.selectedComponent)
    ? props.selectedComponent
    : components[0] ?? 'Value';
  const selectIndex = (next: number) => {
    if (times.length === 0) return;
    const clamped = Math.max(0, Math.min(times.length - 1, Math.round(next)));
    props.onChange(times[clamped], component);
  };
  const selectComponent = (next: string) => {
    if (times[index]) props.onChange(times[index], next);
  };

  if (times.length === 0) return null;
  return <section className="editor-grid-frame-slider" aria-label={language === 'zh' ? '格网时刻与分量' : 'Grid time and component'}>
    <div className="editor-field-grid">
      <label className="editor-field">
        <span>{language === 'zh' ? '时刻' : 'Time'}</span>
        <input
          type="range"
          min={0}
          max={1}
          step={times.length <= 1 ? 1 : 1 / (times.length - 1)}
          value={normalized}
          disabled={props.disabled || times.length <= 1}
          aria-label={language === 'zh' ? '选择格网时刻' : 'Select grid time'}
          aria-valuetext={`${index + 1} / ${times.length}: ${times[index]}`}
          onChange={(event) => selectIndex(Number(event.currentTarget.value) * (times.length - 1))}
        />
        <small>{times[index]} · {index + 1} / {times.length}</small>
      </label>
      {components.length > 1
        ? <label className="editor-field">
          <span>{language === 'zh' ? '分量' : 'Component'}</span>
          <select value={component} disabled={props.disabled} onChange={(event) => selectComponent(event.currentTarget.value)}>
            {components.map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        : <div className="editor-field"><span>{language === 'zh' ? '分量' : 'Component'}</span><strong>{component}</strong></div>}
    </div>
    <div className="editor-workbench-actions" aria-label={language === 'zh' ? '相邻时刻' : 'Adjacent timesteps'}>
      <button type="button" className="editor-button" disabled={props.disabled || index <= 0} onClick={() => selectIndex(index - 1)}>
        {language === 'zh' ? '上一个时刻' : 'Previous timestep'}
      </button>
      <button type="button" className="editor-button" disabled={props.disabled || index >= times.length - 1} onClick={() => selectIndex(index + 1)}>
        {language === 'zh' ? '下一个时刻' : 'Next timestep'}
      </button>
    </div>
  </section>;
}
