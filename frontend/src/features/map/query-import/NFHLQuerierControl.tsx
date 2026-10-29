import { text } from '../../../shared/workbench';
import type { FeatureQueryControlProps } from './featureQueryTypes';

export function NFHLQuerierControl({ language }: FeatureQueryControlProps) {
  return <div className="feature-query-source-control">
    <p className="feature-query-source-title">
      {text(language, 'FEMA 国家洪水危险图层', 'FEMA National Flood Hazard Layer')}
    </p>
    <p className="feature-query-source-description">
      {text(language, '导入当前地图范围内的所有洪水危险区。', 'All flood hazard zones in the current map extent are imported.')}
    </p>
  </div>;
}
