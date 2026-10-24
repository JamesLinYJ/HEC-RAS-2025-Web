import type { Language } from '../../shared/workbench';

export interface GriddedDataTimelineItem {
  id: string;
  time: string;
  issueTime?: string | null;
  detail?: string;
  included?: boolean;
}

/** Timeline selection/detail view corresponding to the source GriddedDataImporterTimeline. */
export function GriddedDataImporterTimeline(props: {
  language: Language;
  items: GriddedDataTimelineItem[];
  selectedId: string;
  loading?: boolean;
  disabled?: boolean;
  onSelect: (item: GriddedDataTimelineItem) => void;
}) {
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  if (props.items.length === 0) return <p className="editor-field-muted">{label('暂无可显示的格网时刻。', 'No grid timesteps are available.')}</p>;
  const selected = props.items.find((item) => item.id === props.selectedId);
  return <section className="editor-grid-timeline" aria-label={label('格网时序', 'Grid timeline')}>
    <header className="editor-meteorology-layer-header">
      <div>
        <h4>{label('时间轴', 'Timeline')}</h4>
        <small>{selected ? `${selected.time}${selected.issueTime ? ` · ${label('起报', 'issued')} ${selected.issueTime}` : ''}` : label('选择时刻', 'Select a timestep')}</small>
      </div>
      {props.loading && <span role="status">{label('正在读取帧…', 'Reading frame…')}</span>}
    </header>
    <div className="editor-grid-timeline-track" role="list" style={{ display: 'flex', gap: 8, overflowX: 'auto', padding: '4px 0' }}>
      {props.items.map((item, index) => {
        const active = item.id === props.selectedId;
        return <div key={item.id} role="listitem" style={{ display: 'flex', flex: '0 0 auto' }}>
          <button
            type="button"
            className={`editor-button${active ? ' editor-button-primary' : ''}`}
            aria-current={active ? 'time' : undefined}
            aria-label={`${index + 1}. ${item.time}${item.issueTime ? `, ${label('起报时间', 'issue time')} ${item.issueTime}` : ''}${item.included === false ? `, ${label('不导入', 'excluded')}` : ''}`}
            title={item.detail ?? item.time}
            disabled={props.disabled || props.loading}
            onClick={() => props.onSelect(item)}
          >
            <span>{index + 1}</span>
            <small>{item.time || label('未设置时刻', 'Time unset')}</small>
            {item.included === false && <small>{label('已排除', 'Excluded')}</small>}
          </button>
        </div>;
      })}
    </div>
    {selected?.detail && <p className="editor-field-muted">{selected.detail}</p>}
  </section>;
}
