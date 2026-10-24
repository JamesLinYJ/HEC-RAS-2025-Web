import type { EditorPageProps } from './model';
import { PrecipitationControl } from './PrecipitationControl';

/** Keeps the precipitation source control mounted and links it to the shared spatial-data page. */
export function PrecipitationEditor(props: EditorPageProps) {
  return <div className="editor-meteorology-page">
    <div className="editor-schema-heading">
      <div><p className="editor-eyebrow">{props.language === 'zh' ? '空间边界条件' : 'Spatial boundary condition'}</p>
        <h2>{props.language === 'zh' ? '降水' : 'Precipitation'}</h2></div>
      <button type="button" className="editor-button" onClick={() => props.onPageChange('SpatialDataSummaryPage')}>
        {props.language === 'zh' ? '空间数据与输入管理' : 'Spatial data and inputs'}
      </button>
    </div>
    <PrecipitationControl {...props} />
  </div>;
}
