import type { EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { BoundarySeriesValueView, TimeSeriesDefinitionFields } from './RegularTimeSeriesPayloadEditor';

/** Spatially-variable payload editor with its source station and time/value tables. */
export function RegularSpatiallyVariableTimeSeriesPayloadEditor({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '空间变化时序载荷编辑器' : 'Spatially variable time-series payload editor'}>
    <TimeSeriesDefinitionFields {...props} series={series} />
    <BoundarySeriesValueView {...props} series={series} />
    {series.operations.includes('addStation') && <p className="editor-field-muted">
      {props.language === 'zh' ? '源控件支持添加测站；当前父级草稿回调尚未接入测站结构变换。' : 'The source control can add stations; the parent draft callback does not expose station structure transforms yet.'}
    </p>}
  </section>;
}
