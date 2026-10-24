import type { EditorPageProps, SeriesTransformOperation } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { BoundarySeriesChart } from './RegularTimeSeriesPayloadEditor';
import { ModelDataTable } from './ModelDataTable';

/** The source SpatiallyVariableTimesSeriesPayloadTable: time rows, station columns, and station locations. */
export function SpatiallyVariableTimesSeriesPayloadTable({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  const rows = series.tableId ? props.draft.rows[series.tableId] ?? series.rows : [];
  const stations = props.draft.rows[`${series.pageId}/Stations`] ?? series.stations;
  const samples = props.draft.samples[series.pageId] ?? series.samples;
  const supported = (['clear', 'interpolate', 'addStation', 'appendRow', 'removeLastRow'] as const)
    .filter((operation) => series.operations.includes(operation));
  const labels: Record<SeriesTransformOperation, [string, string]> = {
    clear: ['清空数值', 'Clear values'],
    interpolate: ['插值补齐', 'Interpolate values'],
    addStation: ['添加测站', 'Add station'],
    appendRow: ['追加时序行', 'Append time-series row'],
    removeLastRow: ['移除末行', 'Remove last row'],
  };
  const transform = (operation: SeriesTransformOperation): void => {
    if (!props.snapshot.canEdit || !props.canTransformSeries || !props.onSeriesTransform) return;
    void props.onSeriesTransform(series.pageId, operation);
  };

  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '空间变化时序表' : 'Spatially variable time-series table'}>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '时序数据与测站' : 'Time-series values and stations'}</h3>
      <span>{rows.length} {props.language === 'zh' ? '行' : 'rows'} · {stations.length} {props.language === 'zh' ? '个测站' : 'stations'}</span>
    </div>
    <BoundarySeriesChart series={series} rows={rows} samples={samples} language={props.language} />
    {series.tableId ? <ModelDataTable tableID={series.tableId} rows={rows} columns={series.columns} canEdit={props.snapshot.canEdit} onCellChange={props.onRowCellChange} language={props.language} />
      : <div className="editor-empty-state">{props.language === 'zh' ? '后端快照没有提供空间变化时序表。' : 'The backend snapshot did not provide a spatial time-series table.'}</div>}
    {stations.length > 0 && <>
      <div className="editor-terrain-table-heading"><h3>{props.language === 'zh' ? '测站位置' : 'Station locations'}</h3><span>{stations.length}</span></div>
      <ModelDataTable tableID={`${series.pageId}/Stations`} rows={stations} columns={[{ id: 'Station', title: props.language === 'zh' ? '测站位置' : 'Station location', sourceType: 'double' }]} canEdit={props.snapshot.canEdit} onCellChange={props.onRowCellChange} language={props.language} />
    </>}
    {supported.length > 0 && <div className="editor-workbench-actions">
      {supported.map((operation) => <button key={operation} type="button" className="editor-button" disabled={!props.snapshot.canEdit || !props.canTransformSeries || !props.onSeriesTransform || operation === 'removeLastRow' && rows.length === 0} onClick={() => transform(operation)}>
        {labels[operation][props.language === 'zh' ? 0 : 1]}
      </button>)}
    </div>}
    {supported.length > 0 && !props.onSeriesTransform && <p className="editor-field-muted">{props.language === 'zh' ? '当前工作台未接入这些时序变换，相关操作已停用。' : 'These time-series transforms are disabled because the workbench has not connected them.'}</p>}
    {supported.length > 0 && props.onSeriesTransform && !props.canTransformSeries && <p className="editor-field-muted">{props.language === 'zh' ? '请先修正当前草稿的无效字段，再执行时序变换。' : 'Correct invalid draft fields before transforming this time series.'}</p>}
    <p className="editor-field-muted">{props.language === 'zh' ? '测站与时序行身份沿用服务端快照；变换只更新父级草稿，保存回执后才确认。' : 'Station and time-row identities come from the backend snapshot. Transforms update the parent draft and require a save receipt.'}</p>
  </section>;
}
