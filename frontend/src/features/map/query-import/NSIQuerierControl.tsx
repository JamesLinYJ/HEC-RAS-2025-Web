import { useId } from 'react';
import { text } from '../../../shared/workbench';
import type { FeatureQueryControlProps } from './featureQueryTypes';

export function NSIQuerierControl({ language, disabled, value, onChange, polygonSources }: FeatureQueryControlProps) {
  const groupId = useId();
  const sourceId = useId();
  const hintId = useId();
  const selectedSourceExists = polygonSources.some((source) => source.id === value.polygonSourceId);
  const missingSelection = value.polygonSourceId !== '' && !selectedSourceExists;

  return <div className="feature-query-source-control">
    <fieldset className="feature-query-fieldset" disabled={disabled}>
      <legend>{text(language, '查询方式', 'Query Method')}</legend>
      <label className="feature-query-radio">
        <input type="radio" name={groupId} data-testid="feature-query-nsi-map-extent"
          checked={!value.useBoundaryPolygon}
          onChange={() => onChange({ useBoundaryPolygon: false })} />
        <span>{text(language, '当前地图范围', 'Current Map Extent')}</span>
      </label>
      <label className="feature-query-radio">
        <input type="radio" name={groupId} data-testid="feature-query-nsi-polygon"
          checked={value.useBoundaryPolygon}
          onChange={() => onChange({ useBoundaryPolygon: true })} />
        <span>{text(language, '多边形', 'Polygon')}</span>
      </label>
      {value.useBoundaryPolygon && <label className="feature-query-field" htmlFor={sourceId}>
        <span className="feature-query-field-label">{text(language, '多边形来源', 'Polygon Source')}</span>
        <select id={sourceId} className="feature-query-select" data-testid="feature-query-nsi-polygon-source"
          value={selectedSourceExists ? value.polygonSourceId : ''}
          aria-describedby={polygonSources.length === 0 || missingSelection ? hintId : undefined}
          aria-invalid={missingSelection || undefined}
          onChange={(event) => onChange({ polygonSourceId: event.currentTarget.value })}>
          <option value="">{text(language, '选择多边形来源', 'Select a polygon source')}</option>
          {polygonSources.map((source) => <option key={source.id} value={source.id}>{source.name}</option>)}
        </select>
        {polygonSources.length === 0 && <span id={hintId} className="feature-query-field-hint">
          {text(language, '当前地图没有可用的多边形来源。', 'No polygon sources are available for the current map.')}
        </span>}
        {polygonSources.length > 0 && missingSelection && <span id={hintId} className="feature-query-field-hint">
          {text(language, '先前选择的多边形来源已不可用，请重新选择。', 'The previously selected polygon source is unavailable. Select another source.')}
        </span>}
      </label>}
    </fieldset>
  </div>;
}
