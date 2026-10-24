import { isEditorFieldReadOnly, type EditorPageProps } from './model';

/** React port of the source BoundaryConditionTimeWindow control. */
export function BoundaryConditionTimeWindow(props: EditorPageProps) {
  const { snapshot, draft, language } = props;
  const startID = 'BoundaryConditionTimeWindow/BoundaryCondition_StartTime';
  const endID = 'BoundaryConditionTimeWindow/BoundaryCondition_EndTime';
  const start = draft.fields[startID] ?? snapshot.fields[startID] ?? '';
  const end = draft.fields[endID] ?? snapshot.fields[endID] ?? '';
  if (!(startID in snapshot.fields) || !(endID in snapshot.fields)) return null;

  const startValue = toDateTimeLocal(start);
  const endValue = toDateTimeLocal(end);
  const startEpoch = Date.parse(start);
  const endEpoch = Date.parse(end);
  const invalidStart = !Number.isFinite(startEpoch);
  const invalidEnd = !Number.isFinite(endEpoch);
  const invalidOrder = !invalidStart && !invalidEnd && endEpoch <= startEpoch;
  const update = (side: 'start' | 'end', value: string): void => {
    const fieldID = side === 'start' ? startID : endID;
    const groupID = side === 'start' ? 'BoundaryStartTime' : 'BoundaryEndTime';
    const grouped = draft.boundaryFieldGroups?.[groupID] ?? snapshot.fieldGroups[groupID] ?? [];
    for (const id of grouped) props.onFieldChange(id, value);
    if (!grouped.includes(fieldID)) props.onFieldChange(fieldID, value);
  };

  return <section className="editor-table-card" aria-labelledby="boundary-time-window-title">
    <div className="editor-terrain-table-heading">
      <h3 id="boundary-time-window-title">{language === 'zh' ? '边界条件时段' : 'Boundary condition time window'}</h3>
    </div>
    <div className="editor-field-grid">
      <div className="editor-field">
        <label htmlFor="boundary-time-window-start">{language === 'zh' ? '开始时间' : 'Start time'}</label>
        <input id="boundary-time-window-start" type="datetime-local" value={startValue} readOnly={!snapshot.canEdit || isEditorFieldReadOnly(props, startID)} aria-invalid={invalidStart || invalidOrder ? 'true' : undefined} onChange={(event) => update('start', event.currentTarget.value)} />
        {invalidStart && <small className="editor-field-error">{language === 'zh' ? '请输入有效的开始时间。' : 'Enter a valid start time.'}</small>}
      </div>
      <div className="editor-field">
        <label htmlFor="boundary-time-window-end">{language === 'zh' ? '结束时间' : 'End time'}</label>
        <input id="boundary-time-window-end" type="datetime-local" value={endValue} readOnly={!snapshot.canEdit || isEditorFieldReadOnly(props, endID)} aria-invalid={invalidEnd || invalidOrder ? 'true' : undefined} onChange={(event) => update('end', event.currentTarget.value)} />
        {invalidEnd && <small className="editor-field-error">{language === 'zh' ? '请输入有效的结束时间。' : 'Enter a valid end time.'}</small>}
      </div>
    </div>
    {invalidOrder && <p className="editor-field-error">{language === 'zh' ? '结束时间必须晚于开始时间。' : 'The end time must follow the start time.'}</p>}
  </section>;
}

function toDateTimeLocal(raw: string): string {
  const date = new Date(raw);
  if (!Number.isFinite(date.getTime())) return raw.length >= 16 ? raw.slice(0, 16) : '';
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
