import { useId } from 'react';
import { text } from '../../../shared/workbench';
import type { FeatureQueryControlProps } from './featureQueryTypes';

/** The four choices correspond to NLDQuerierControl.QueryLayerOptions. */
export function NLDQuerierControl({ language, disabled, value, onChange }: FeatureQueryControlProps) {
  const layerId = useId();

  return <div className="feature-query-source-control">
    <p className="feature-query-source-title">
      {text(language, 'USACE 国家堤防数据库', 'USACE National Levee Database')}
    </p>
    <p className="feature-query-source-description">
      {text(language, '查询当前地图范围内的 NLD 要素。', 'Query NLD features for the current map extent.')}
    </p>
    <label className="feature-query-field" htmlFor={layerId}>
      <span className="feature-query-field-label">{text(language, '要素类型', 'Feature Type')}</span>
      <select id={layerId} className="feature-query-select" data-testid="feature-query-nld-layer"
        disabled={disabled} value={value.nldLayerId}
        onChange={(event) => onChange({ nldLayerId: event.currentTarget.value })}>
        <option value="AlignmentLines">{text(language, '堤防轴线', 'Alignment Lines')}</option>
        <option value="Embankments">{text(language, '堤坝', 'Embankments')}</option>
        <option value="Floodwalls">{text(language, '防洪墙', 'Floodwalls')}</option>
        <option value="SystemRoutes">{text(language, 'RAS 合并轴线', 'RAS Merged Alignments')}</option>
      </select>
    </label>
  </div>;
}
