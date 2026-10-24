import type { EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { TimeSeriesDefinitionFields } from './RegularTimeSeriesPayloadEditor';
import { SpatiallyVariableTimesSeriesPayloadTable } from './SpatiallyVariableTimesSeriesPayloadTable';

/** Spatially-variable payload editor with its source station and time/value tables. */
export function RegularSpatiallyVariableTimeSeriesPayloadEditor({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '空间变化时序载荷编辑器' : 'Spatially variable time-series payload editor'}>
    <TimeSeriesDefinitionFields {...props} series={series} />
    <SpatiallyVariableTimesSeriesPayloadTable {...props} series={series} />
  </section>;
}
