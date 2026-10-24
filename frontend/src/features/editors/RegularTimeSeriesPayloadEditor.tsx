import { isEditorFieldReadOnly, type EditorPageProps, type ModelRow, type ModelSample } from './model';
import { ModelDataTable } from './ModelDataTable';

export type BoundarySeries = NonNullable<NonNullable<EditorPageProps['snapshot']['boundary']>['series'][string]>;

/** Scalar time-series editor. It also renders irregular scalar payload rows using the same source table shape. */
export function RegularTimeSeriesPayloadEditor({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  const isRegular = series.schemaId === 'RegularTimeSeriesPayloadEditor';
  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '时序载荷编辑器' : 'Time-series payload editor'}>
    {isRegular && <TimeSeriesDefinitionFields {...props} series={series} />}
    <BoundarySeriesValueView {...props} series={series} />
  </section>;
}

export function TimeSeriesDefinitionFields({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  const schemaID = series.schemaId;
  const fieldID = (name: string) => `${series.scope}/${schemaID}/${name}`;
  const read = (name: string) => props.draft.fields[fieldID(name)] ?? props.snapshot.fields[fieldID(name)] ?? '';
  const useFixedID = fieldID('TimeSeriesDefinition_UseFixedStartTime');
  const fixedStartID = fieldID('TimeSeriesDefinition_FixedStartTime');
  const intervalID = fieldID('TimeSeriesDefinition_Interval');
  const startID = fieldID('BoundaryConditionLine_BoundaryCondition_StartTime');
  const endID = fieldID('BoundaryConditionLine_BoundaryCondition_EndTime');
  const actualStartID = fieldID('BoundaryConditionLine_ActualStartTime');
  const actualEndID = fieldID('BoundaryConditionLine_ActualEndTime');
  const hasRegularDefinition = useFixedID in props.snapshot.fields || useFixedID in props.draft.fields
    || intervalID in props.snapshot.fields || intervalID in props.draft.fields;
  if (!hasRegularDefinition) return null;
  const useFixed = read('TimeSeriesDefinition_UseFixedStartTime') === 'true';
  const interval = read('TimeSeriesDefinition_Interval');
  const intervalNumber = Number(interval);
  const intervalInvalid = interval.trim() === '' || !Number.isFinite(intervalNumber) || intervalNumber <= 0;
  const fixedStart = read('TimeSeriesDefinition_FixedStartTime');
  const boundaryStart = read('BoundaryConditionLine_BoundaryCondition_StartTime');
  const boundaryEnd = read('BoundaryConditionLine_BoundaryCondition_EndTime');
  const fixedAfterWindow = useFixed && Date.parse(fixedStart) > Date.parse(boundaryStart);
  const updateBoundaryTime = (fieldID: string, aliasID: 'BoundaryStartTime' | 'BoundaryEndTime', value: string): void => {
    const grouped = props.draft.boundaryFieldGroups?.[aliasID] ?? props.snapshot.fieldGroups[aliasID] ?? [];
    for (const id of grouped) props.onFieldChange(id, value);
    if (!grouped.includes(fieldID)) props.onFieldChange(fieldID, value);
  };
  const timeValue = (value: string) => toDateTimeLocalValue(value);

  return <details className="editor-field-group" open>
    <summary>{props.language === 'zh' ? '时序定义' : 'Time-series definition'}</summary>
    <div className="editor-field-grid">
      <div className="editor-field">
        <label htmlFor={`series-${series.pageId}-interval`}>{props.language === 'zh' ? '时间间隔（秒）' : 'Interval (seconds)'}</label>
        <input id={`series-${series.pageId}-interval`} type="number" min="0" step="any" inputMode="decimal" value={interval} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, intervalID)} aria-invalid={intervalInvalid || undefined} onChange={(event) => props.onFieldChange(intervalID, event.currentTarget.value)} />
        {intervalInvalid && <small className="editor-field-error">{props.language === 'zh' ? '时间间隔必须大于零。' : 'The interval must be greater than zero.'}</small>}
      </div>
      <label className="editor-toggle" htmlFor={`series-${series.pageId}-fixed-start`}>
        <input id={`series-${series.pageId}-fixed-start`} type="checkbox" checked={useFixed} disabled={!props.snapshot.canEdit || isEditorFieldReadOnly(props, useFixedID)} onChange={(event) => props.onFieldChange(useFixedID, event.currentTarget.checked ? 'true' : 'false')} />
        {props.language === 'zh' ? '使用固定开始时间' : 'Use fixed start time'}
      </label>
      {useFixed && <div className="editor-field">
        <label htmlFor={`series-${series.pageId}-fixed-start-value`}>{props.language === 'zh' ? '固定开始时间' : 'Fixed start time'}</label>
        <input id={`series-${series.pageId}-fixed-start-value`} type="datetime-local" value={timeValue(fixedStart)} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, fixedStartID)} aria-invalid={fixedAfterWindow || undefined} onChange={(event) => props.onFieldChange(fixedStartID, event.currentTarget.value)} />
        {fixedAfterWindow && <small className="editor-field-error">{props.language === 'zh' ? '固定开始时间不能晚于边界条件开始时间。' : 'The fixed start time cannot follow the boundary condition start.'}</small>}
      </div>}
      <div className="editor-field">
        <label htmlFor={`series-${series.pageId}-window-start`}>{props.language === 'zh' ? '边界条件开始时间' : 'Boundary condition start time'}</label>
        <input id={`series-${series.pageId}-window-start`} type="datetime-local" value={timeValue(boundaryStart)} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, startID)} onChange={(event) => updateBoundaryTime(startID, 'BoundaryStartTime', event.currentTarget.value)} />
      </div>
      <div className="editor-field">
        <label htmlFor={`series-${series.pageId}-window-end`}>{props.language === 'zh' ? '边界条件结束时间' : 'Boundary condition end time'}</label>
        <input id={`series-${series.pageId}-window-end`} type="datetime-local" value={timeValue(boundaryEnd)} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, endID)} onChange={(event) => updateBoundaryTime(endID, 'BoundaryEndTime', event.currentTarget.value)} />
      </div>
    </div>
    {(actualStartID in props.snapshot.fields || actualEndID in props.snapshot.fields) && <p className="editor-field-muted">
      {props.language === 'zh' ? `后端快照的实际范围：${read('BoundaryConditionLine_ActualStartTime')} — ${read('BoundaryConditionLine_ActualEndTime')}${boundaryStart !== (props.snapshot.fields[startID] ?? '') || boundaryEnd !== (props.snapshot.fields[endID] ?? '') || interval !== (props.snapshot.fields[intervalID] ?? '') || fixedStart !== (props.snapshot.fields[fixedStartID] ?? '') ? '；保存后由后端重新计算。' : ''}` : `Actual range from the backend snapshot: ${read('BoundaryConditionLine_ActualStartTime')} — ${read('BoundaryConditionLine_ActualEndTime')}${boundaryStart !== (props.snapshot.fields[startID] ?? '') || boundaryEnd !== (props.snapshot.fields[endID] ?? '') || interval !== (props.snapshot.fields[intervalID] ?? '') || fixedStart !== (props.snapshot.fields[fixedStartID] ?? '') ? '; recalculated by the backend after save.' : ''}`}
    </p>}
  </details>;
}

export function BoundarySeriesValueView({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  const samples = props.draft.samples[series.pageId] ?? series.samples;
  const rows = series.tableId ? props.draft.rows[series.tableId] ?? series.rows : [];
  const stations = series.tableId ? props.draft.rows[`${series.pageId}/Stations`] ?? series.stations : [];
  const appendSupported = series.operations.includes('appendRow');
  const removeSupported = series.operations.includes('removeLastRow');
  const transformUnavailable = (appendSupported || removeSupported) && !props.onSeriesTransform;
  return <>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '时序数据' : 'Time-series data'}</h3>
      <span>{samples.length} {props.language === 'zh' ? '个样本' : 'samples'}</span>
    </div>
    <BoundarySeriesChart series={series} rows={rows} samples={samples} language={props.language} />
    {series.tableId ? <>
      <ModelDataTable tableID={series.tableId} rows={rows} columns={series.columns} canEdit={props.snapshot.canEdit} onCellChange={props.onRowCellChange} language={props.language} />
      {stations.length > 0 && <ModelDataTable tableID={`${series.pageId}/Stations`} rows={stations} columns={[{ id: 'Station', title: props.language === 'zh' ? '测站位置' : 'Station location', sourceType: 'double' }]} canEdit={props.snapshot.canEdit} onCellChange={props.onRowCellChange} language={props.language} />}
    </> : <ScalarSamplesTable series={series} samples={samples} {...props} />}
    {(appendSupported || removeSupported) && <div className="editor-workbench-actions">
      {appendSupported && <button type="button" className="editor-button" disabled={!props.snapshot.canEdit || !props.canTransformSeries || !props.onSeriesTransform} onClick={() => { void props.onSeriesTransform?.(series.pageId, 'appendRow'); }}>{props.language === 'zh' ? '追加时序行' : 'Append time-series row'}</button>}
      {removeSupported && <button type="button" className="editor-button" disabled={!props.snapshot.canEdit || !props.canTransformSeries || !props.onSeriesTransform || samples.length === 0} onClick={() => { void props.onSeriesTransform?.(series.pageId, 'removeLastRow'); }}>{props.language === 'zh' ? '移除末行' : 'Remove last row'}</button>}
    </div>}
    {transformUnavailable && <p className="editor-field-muted">{props.language === 'zh' ? '此服务端尚未提供时序行调整回调。' : 'This server has not exposed a time-series row transform callback.'}</p>}
    <p className="editor-field-muted">{props.language === 'zh' ? '行号、样本编号和表格列沿用后端快照身份；更改只进入父级草稿，保存回执后才确认。' : 'Row, sample, and column identities come from the backend snapshot. Edits stay in the parent draft until a save receipt confirms them.'}</p>
  </>;
}

function ScalarSamplesTable({ series, samples, ...props }: EditorPageProps & { series: BoundarySeries; samples: ModelSample[] }) {
  const canEdit = props.snapshot.canEdit;
  return <div className="editor-table-scroll">
    <table className="editor-data-table">
      <thead><tr><th>#</th><th>{series.xIsDate ? (props.language === 'zh' ? '时间' : 'Time') : series.xTitle}</th><th>{series.yTitle}</th></tr></thead>
      <tbody>{samples.map((sample) => {
        const text = props.draft.sampleText[series.pageId]?.[sample.id];
        const x = text?.x ?? (series.xIsDate ? toDateTimeLocalEpoch(sample.x) : String(sample.x));
        const y = text?.y ?? (sample.y == null ? '' : String(sample.y));
        return <tr key={sample.id}>
          <th scope="row">{Number(sample.id) + 1}</th>
          <td><input type={series.xIsDate ? 'datetime-local' : 'text'} inputMode={series.xIsDate ? undefined : 'decimal'} value={x} readOnly={!canEdit || !series.canEditX} aria-label={`${series.xTitle} ${sample.id}`} onChange={(event) => props.onSampleChange?.(series.pageId, sample.id, 'x', event.currentTarget.value)} /></td>
          <td><input type="text" inputMode="decimal" value={y} readOnly={!canEdit} aria-label={`${series.yTitle} ${sample.id}`} onChange={(event) => props.onSampleChange?.(series.pageId, sample.id, 'y', event.currentTarget.value)} /></td>
        </tr>;
      })}</tbody>
    </table>
  </div>;
}

export function BoundarySeriesChart({ series, rows, samples, language }: {
  series: BoundarySeries;
  rows: ModelRow[];
  samples: ModelSample[];
  language: EditorPageProps['language'];
}) {
  const palette = ['#2878a8', '#d28a2d', '#4f8a61', '#8c68a7', '#bd5e62', '#4e92a0'];
  const grouped = new Map<string, { label: string; points: Array<{ x: number; y: number }> }>();
  if (series.tableId) {
    for (const column of series.columns.filter((item) => item.id !== 'Time')) {
      const points = rows.flatMap((row) => {
        const xRaw = row.values.Time;
        const yRaw = row.values[column.id];
        const x = series.xIsDate ? Date.parse(xRaw ?? '') / 1000 : Number(xRaw);
        const y = yRaw?.trim() ? Number(yRaw) : Number.NaN;
        return Number.isFinite(x) && Number.isFinite(y) ? [{ x, y }] : [];
      });
      if (points.length) grouped.set(column.id, { label: column.title, points });
    }
  } else {
    for (const sample of samples) {
      if (sample.y == null || !Number.isFinite(sample.x) || !Number.isFinite(sample.y)) continue;
      const key = sample.datasetId ?? sample.series;
      const group = grouped.get(key) ?? { label: sample.series, points: [] };
      group.points.push({ x: sample.x, y: sample.y });
      grouped.set(key, group);
    }
  }
  const groups = [...grouped.values()].map((group) => ({ ...group, points: group.points.sort((a, b) => a.x - b.x) }));
  const pointCount = groups.reduce((count, group) => count + group.points.length, 0);
  if (!pointCount) return <div className="editor-empty-state">{language === 'zh' ? '当前没有可绘制的数值。' : 'There are no numeric values to plot.'}</div>;
  let minX = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (const group of groups) for (const point of group.points) {
    minX = Math.min(minX, point.x); maxX = Math.max(maxX, point.x);
    minY = Math.min(minY, point.y); maxY = Math.max(maxY, point.y);
  }
  const chartGroups = groups.map((group) => ({ ...group, points: downsample(group.points, 1600) }));
  if (minX === maxX) { minX -= 1; maxX += 1; }
  if (minY === maxY) { const spread = Math.max(Math.abs(minY) * 0.05, 1); minY -= spread; maxY += spread; }
  const xScale = (value: number) => 42 + ((value - minX) / (maxX - minX)) * 482;
  const yScale = (value: number) => 150 - ((value - minY) / (maxY - minY)) * 124;
  return <figure className="editor-table-card" aria-label={language === 'zh' ? '时序数值图' : 'Time-series value chart'} style={{ margin: 0 }}>
    <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '数值图' : 'Value chart'}</h3><span>{series.yTitle}</span></div>
    <div className="editor-table-scroll">
      <svg viewBox="0 0 540 190" role="img" aria-label={`${series.xTitle} / ${series.yTitle}`} style={{ display: 'block', width: '100%', minWidth: 340, height: 190 }}>
        {[0, 1, 2, 3].map((step) => {
          const y = 26 + step * 41;
          const value = maxY - ((maxY - minY) * step) / 3;
          return <g key={step}><line x1="42" x2="524" y1={y} y2={y} stroke="#e3e9ed" strokeWidth="1" /><text x="37" y={y + 4} fill="#778592" fontSize="10" textAnchor="end">{formatNumber(value)}</text></g>;
        })}
        <line x1="42" x2="42" y1="26" y2="150" stroke="#aab6c0" strokeWidth="1" />
        <line x1="42" x2="524" y1="150" y2="150" stroke="#aab6c0" strokeWidth="1" />
        {chartGroups.map((group, index) => {
          const color = palette[index % palette.length];
          const path = group.points.map((point, pointIndex) => `${pointIndex ? 'L' : 'M'}${xScale(point.x).toFixed(2)},${yScale(point.y).toFixed(2)}`).join(' ');
          return <g key={`${group.label}-${index}`}>
            {group.points.length > 1 && <path d={path} fill="none" stroke={color} strokeWidth="2" />}
            {group.points.map((point, pointIndex) => <circle key={`${group.label}-${pointIndex}`} cx={xScale(point.x)} cy={yScale(point.y)} r="2.5" fill={color} />)}
          </g>;
        })}
      </svg>
    </div>
    <figcaption style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginTop: 5, color: '#637182', fontSize: 11 }}>
      {groups.map((group, index) => <span key={`${group.label}-${index}`}><i aria-hidden="true" style={{ display: 'inline-block', width: 8, height: 8, marginRight: 5, borderRadius: '50%', background: palette[index % palette.length] }} />{group.label}</span>)}
      <span>{series.xIsDate ? dateLabel(minX, language) : formatNumber(minX)} — {series.xIsDate ? dateLabel(maxX, language) : formatNumber(maxX)}</span>
    </figcaption>
  </figure>;
}

function toDateTimeLocalEpoch(epochSeconds: number): string {
  const date = new Date(epochSeconds * 1000);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function toDateTimeLocalValue(raw: string): string {
  const date = new Date(raw);
  if (!Number.isFinite(date.getTime())) return raw.length >= 16 ? raw.slice(0, 16) : '';
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function downsample<T extends { x: number }>(points: T[], maximum: number): T[] {
  if (points.length <= maximum) return points;
  const result: T[] = [];
  for (let index = 0; index < maximum; index++) result.push(points[Math.round(index * (points.length - 1) / (maximum - 1))]);
  return result;
}

function formatNumber(value: number): string {
  return Number.isFinite(value) ? String(Number(value.toPrecision(5))) : '';
}

function dateLabel(epochSeconds: number, language: EditorPageProps['language']): string {
  return new Date(epochSeconds * 1000).toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US', { dateStyle: 'short', timeStyle: 'short' });
}
