import type { EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { SpatiallyVariableTimesSeriesPayloadTable } from './SpatiallyVariableTimesSeriesPayloadTable';

/** Source spatial payload editor, mounted for both regular and irregular station series. */
export function SpatiallyVariableTimeSeriesPayloadEditor({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '空间变化时序载荷编辑器' : 'Spatially variable time-series payload editor'}>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '不规则空间时序' : 'Irregular spatial time series'}</h3>
      <span>{props.language === 'zh' ? '时间与空间测站数值分列编辑' : 'Edit each timestamp and station value in the table'}</span>
    </div>
    <SpatiallyVariableTimesSeriesPayloadTable {...props} series={series} />
  </section>;
}
