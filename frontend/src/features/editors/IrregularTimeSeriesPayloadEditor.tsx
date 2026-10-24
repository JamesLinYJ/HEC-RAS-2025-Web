import type { EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { BoundarySeriesValueView } from './RegularTimeSeriesPayloadEditor';

/** Source IrregularTimeSeriesPayloadEditor: editable time/value pairs and its payload chart. */
export function IrregularTimeSeriesPayloadEditor({ series, ...props }: EditorPageProps & { series: BoundarySeries }) {
  return <section className="editor-series-panel" aria-label={props.language === 'zh' ? '不规则时序载荷编辑器' : 'Irregular time-series payload editor'}>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '不规则时序' : 'Irregular time series'}</h3>
      <span>{props.language === 'zh' ? '每行时间可单独编辑' : 'Edit the time in each row'}</span>
    </div>
    <BoundarySeriesValueView {...props} series={series} />
  </section>;
}
