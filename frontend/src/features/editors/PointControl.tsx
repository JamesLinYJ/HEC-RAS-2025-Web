import { isEditorFieldReadOnly, type EditorPageProps, type SeriesTransformOperation, type SpatialPointData } from './model';
import { ModelDataTable } from './ModelDataTable';
import { editorText } from './editorLocalization';

type BoundarySeries = NonNullable<NonNullable<EditorPageProps['snapshot']['boundary']>['series'][string]>;

/** Point-mode view bound to the source ObservableLayer through the boundary snapshot. */
export function PointControl(props: EditorPageProps & {
  layerId: string;
  layerTitle: string;
  spatial?: SpatialPointData;
  series: BoundarySeries[];
}) {
  const spatial = props.spatial;
  const stationCount = spatial?.stations.length ?? 0;
  const inputCount = spatial?.inputs.length ?? 0;
  return <section className="editor-meteorology-point-control" style={{ display: 'flex', minWidth: 0, flexDirection: 'column', gap: 10 }} aria-label={props.language === 'zh' ? `${props.layerTitle}测站输入` : `${props.layerTitle} point inputs`} data-layer-id={props.layerId} data-testid={`point-control-${props.layerId}`}>
    <header className="editor-meteorology-layer-header">
      <div>
        <p className="editor-eyebrow">{props.language === 'zh' ? '点测站输入' : 'Point-station inputs'}</p>
        <h4>{props.language === 'zh' ? '测站与时序值' : 'Stations and time-series values'}</h4>
      </div>
      <div className="editor-meteorology-source-summary">
        <span>{props.language === 'zh' ? `测站：${stationCount}` : `Stations: ${stationCount}`}</span>
        <span>{props.language === 'zh' ? `输入：${inputCount}` : `Inputs: ${inputCount}`}</span>
        {spatial?.unit && <span>{props.language === 'zh' ? `源单位：${spatial.unit}` : `Source unit: ${spatial.unit}`}</span>}
        {spatial?.isVector && <span>{props.language === 'zh' ? '矢量数据' : 'Vector data'}</span>}
      </div>
    </header>
    <p className="editor-meteorology-note">
      {props.language === 'zh'
        ? '时序表按当前测站、输入范围和行标识编辑。测站坐标在此只读；请在“空间数据与输入管理”页面查看或调整点位配置。'
        : 'Time-series tables follow the current station, input scope, and row identities. Station coordinates are read-only here; review or adjust point configuration on the Spatial data and inputs page.'}
    </p>
    <SpatialInputs {...props} spatial={spatial} series={props.series} />
  </section>;
}

/** Shared view for the real point/gridded spatial series and spatial-management panel. */
export function SpatialInputs({ spatial, series, language, snapshot, draft, onFieldChange, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }: EditorPageProps & { spatial?: SpatialPointData; series: BoundarySeries[] }) {
  const inputs = spatial?.inputs ?? [];
  const stations = spatial?.stations ?? [];
  if (inputs.length === 0 && series.length === 0 && stations.length === 0) {
    return <p className="editor-meteorology-note">{language === 'zh' ? '当前没有此类空间输入；请在“空间数据与输入管理”页面检查可用测站与输入。' : 'There are no spatial inputs of this type. Review available stations and inputs on the Spatial data and inputs page.'}</p>;
  }
  return <div className="editor-meteorology-spatial">
    {stations.length > 0 && <details className="editor-series-panel" open>
      <summary>{language === 'zh' ? '测站坐标' : 'Station coordinates'} <span>{stations.length}</span></summary>
      <div className="editor-table-scroll"><table className="editor-data-table">
        <thead><tr><th>{language === 'zh' ? '测站' : 'Station'}</th><th>X</th><th>Y</th></tr></thead>
        <tbody>{stations.map((station) => <tr key={station.id} data-station-id={station.id}><th scope="row">{station.name || station.id}</th><td>{station.x}</td><td>{station.y}</td></tr>)}</tbody>
      </table></div>
    </details>}
    {inputs.map((input, index) => {
      const expectedPage = input.pageId ?? input.scope + '/Time Series';
      const match = series.find((item) => item.scope === input.scope && item.pageId === expectedPage);
      const stationKey = `${input.scope}/Station`;
      const stationId = draft.fields[stationKey] ?? snapshot.fields[stationKey] ?? input.stationId ?? '';
      const station = stations.find((item) => item.id === stationId);
      const title = station?.name ?? (language === 'zh' ? `空间数据 ${index + 1}` : `Spatial input ${index + 1}`);
      const localDataType = draft.fields[`${input.scope}/LocalDataType`] ?? snapshot.fields[`${input.scope}/LocalDataType`] ?? '';
      return <article className="editor-meteorology-input" key={input.id} data-input-id={input.id} data-input-scope={input.scope}>
        <h4>{title}</h4>
        <details className="editor-source-details"><summary>{language === 'zh' ? '数据身份' : 'Data identity'}</summary><code>{input.pageId ?? input.scope}</code></details>
        {(localDataType === '' || localDataType === 'Constant') && <div className="editor-field-grid">
          <SpatialGaugeConstantField {...{ language, snapshot, draft, onFieldChange }} scope={input.scope} property="Constant" unit={spatial?.unit} />
          {spatial?.isVector && localDataType === 'Constant' && <SpatialGaugeConstantField {...{ language, snapshot, draft, onFieldChange }} scope={input.scope} property="Constant2" unit={spatial.unit} />}
        </div>}
        {match ? <SpatialSeries {...{ series: match, language, snapshot, draft, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }} /> : <p className="editor-meteorology-note">{localDataType === '' || localDataType === 'Constant'
          ? language === 'zh' ? '此输入使用常量值，不包含时序表。' : 'This input uses a constant value and has no time-series table.'
          : language === 'zh' ? `“${localDataType}”输入当前没有时序表。` : `A time-series table is not available for the “${localDataType}” input.`}</p>}
      </article>;
    })}
    {inputs.length === 0 && series.map((item) => <SpatialSeries key={item.pageId} series={item} language={language} snapshot={snapshot} draft={draft} onRowCellChange={onRowCellChange} onSampleChange={onSampleChange} onSeriesTransform={onSeriesTransform} canTransformSeries={canTransformSeries} />)}
  </div>;
}

function SpatialSeries({ series, language, snapshot, draft, onRowCellChange, onSampleChange, onSeriesTransform, canTransformSeries }: Pick<EditorPageProps, 'language' | 'snapshot' | 'draft' | 'onRowCellChange' | 'onSampleChange' | 'onSeriesTransform' | 'canTransformSeries'> & { series: BoundarySeries }) {
  const rows = series.tableId ? draft.rows[series.tableId] ?? series.rows : [];
  const samples = draft.samples[series.pageId] ?? series.samples;
  if (series.tableId && series.tableId !== series.pageId + '/Values') return <p className="editor-meteorology-note">{language === 'zh' ? '时序表身份与空间输入不匹配，已停用编辑。' : 'The time-series table identity does not match this spatial input, so editing is disabled.'}</p>;
  return <div className="editor-meteorology-series-editor" data-series-page-id={series.pageId}>
    <SeriesTransformActions {...{ series, rows, samples, language, snapshot, canTransformSeries, onSeriesTransform }} />
    {series.tableId
      ? <ModelDataTable tableID={series.tableId} rows={rows} columns={series.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
      : samples.length === 0
        ? <p className="editor-empty-state">{language === 'zh' ? '该输入没有时序行。' : 'This input has no time-series rows.'}</p>
        : <div className="editor-table-scroll"><table className="editor-data-table">
          <thead><tr><th>#</th><th>{editorText(language, series.xTitle)}</th><th>{editorText(language, series.yTitle)}</th></tr></thead>
          <tbody>{samples.map((sample, index) => {
            const raw = draft.sampleText[series.pageId]?.[sample.id];
            const x = raw?.x ?? (series.xIsDate ? toSafeDateTimeLocal(sample.x) : String(sample.x));
            const y = raw?.y ?? (sample.y == null ? '' : String(sample.y));
            const dateInput = series.xIsDate && isDateTimeLocalValue(x);
            return <tr key={sample.id} data-sample-id={sample.id}>
              <th scope="row">{index + 1}</th>
              <td><input type={dateInput ? 'datetime-local' : 'text'} inputMode={dateInput ? undefined : series.xIsDate ? 'text' : 'decimal'} value={x} readOnly={!snapshot.canEdit || !onSampleChange || !series.canEditX} onChange={(event) => onSampleChange?.(series.pageId, sample.id, 'x', event.currentTarget.value)} /></td>
              <td><input type="text" inputMode="decimal" value={y} readOnly={!snapshot.canEdit || !onSampleChange} onChange={(event) => onSampleChange?.(series.pageId, sample.id, 'y', event.currentTarget.value)} /></td>
            </tr>;
          })}</tbody>
        </table></div>}
  </div>;
}

function SeriesTransformActions({ series, rows, samples, language, snapshot, canTransformSeries, onSeriesTransform }: Pick<EditorPageProps, 'language' | 'snapshot' | 'canTransformSeries' | 'onSeriesTransform'> & {
  series: BoundarySeries;
  rows: NonNullable<EditorPageProps['draft']['rows'][string]>;
  samples: NonNullable<EditorPageProps['draft']['samples'][string]>;
}) {
  const canClear = series.operations.includes('clear');
  const canAppend = series.operations.includes('appendRow');
  const canRemove = series.operations.includes('removeLastRow');
  if (!canClear && !canAppend && !canRemove) return null;
  const rowCount = series.tableId ? rows.length : samples.length;
  const disabled = !snapshot.canEdit || !canTransformSeries;
  const run = (operation: SeriesTransformOperation) => { void onSeriesTransform?.(series.pageId, operation); };
  return <div className="editor-meteorology-series-actions" aria-label={language === 'zh' ? '时序行操作' : 'Time-series row actions'}>
    {canClear && <button type="button" className="editor-button" disabled={disabled || !onSeriesTransform || rowCount === 0} data-testid={`series-transform-${encodeURIComponent(series.pageId)}-clear`} data-page-id={series.pageId} data-operation="clear" onClick={() => run('clear')}>{language === 'zh' ? '清空时序' : 'Clear series'}</button>}
    {canAppend && <button type="button" className="editor-button" disabled={disabled || !onSeriesTransform} data-testid={`series-transform-${encodeURIComponent(series.pageId)}-appendRow`} data-page-id={series.pageId} data-operation="appendRow" onClick={() => run('appendRow')}>{language === 'zh' ? '添加末行' : 'Append row'}</button>}
    {canRemove && <button type="button" className="editor-button" disabled={disabled || !onSeriesTransform || rowCount === 0} data-testid={`series-transform-${encodeURIComponent(series.pageId)}-removeLastRow`} data-page-id={series.pageId} data-operation="removeLastRow" onClick={() => run('removeLastRow')}>{language === 'zh' ? '移除末行' : 'Remove last row'}</button>}
    {!onSeriesTransform && <span className="editor-field-muted">{language === 'zh' ? '当前会话未提供时序结构修改操作。' : 'Time-series structure editing is unavailable in this session.'}</span>}
    {onSeriesTransform && !canTransformSeries && <span className="editor-field-muted">{language === 'zh' ? '请先修正当前草稿的无效值，再修改时序结构。' : 'Correct invalid draft values before changing the time-series structure.'}</span>}
  </div>;
}

export function SpatialGaugeConstantField(props: Pick<EditorPageProps, 'language' | 'snapshot' | 'draft' | 'onFieldChange'> & { scope: string; property: 'Constant' | 'Constant2'; unit?: string }) {
  const fieldID = `${props.scope}/${props.property}`;
  const value = props.draft.fields[fieldID] ?? props.snapshot.fields[fieldID] ?? '';
  const label = props.language === 'zh' ? (props.property === 'Constant2' ? '常数 Y' : '常数值') : props.property;
  return <label className="editor-field" data-field-id={fieldID}>
    <span>{props.unit ? `${label}（${props.unit}）` : label}</span>
    <input type="text" inputMode="decimal" value={value} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, fieldID)} onChange={(event) => props.onFieldChange(fieldID, event.currentTarget.value)} />
  </label>;
}

function toSafeDateTimeLocal(epochSeconds: number): string {
  const date = new Date(epochSeconds * 1000);
  if (!Number.isFinite(epochSeconds) || !Number.isFinite(date.getTime())) return String(epochSeconds);
  const pad = (value: number) => String(value).padStart(2, '0');
  return String(date.getFullYear()).padStart(4, '0') + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes());
}

function isDateTimeLocalValue(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return false;
  const [, year, month, day, hour, minute] = match;
  const date = new Date(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute));
  return date.getFullYear() === Number(year) && date.getMonth() === Number(month) - 1 && date.getDate() === Number(day)
    && date.getHours() === Number(hour) && date.getMinutes() === Number(minute);
}
