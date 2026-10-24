import type { ReactNode } from 'react';
import { EmptySelectionOverlay } from '../../controls/EmptySelectionOverlay';
import { text } from '../../shared/workbench';
import { localizeProjectTreeSourceClass } from '../project/ProjectTreeModeSectionTitles';
import { mapCRS, mapError, mapGeometryType } from './mapText';
import type { GeoFeature, GeoJsonLayer } from './mapTypes';

export interface MapBackendSelection {
  nodeID: string;
  name: string;
  sourceClass: string;
  diagnostic?: string;
}

/** Context-sensitive selection and property content from RasLayerPropertiesPane.cs. */
export function RasLayerPropertiesPane({ language, featureSelection, backendSelection,
  canZoomBackend, onZoomFeature, onZoomBackend, children }: {
  language: 'zh' | 'en';
  featureSelection: { layer: GeoJsonLayer; feature: GeoFeature } | null;
  backendSelection: MapBackendSelection | null;
  canZoomBackend: boolean;
  onZoomFeature: (layer: GeoJsonLayer, feature: GeoFeature) => void;
  onZoomBackend: (nodeID: string) => void;
  children?: ReactNode;
}) {
  const heading = text(language, '属性', 'Properties');
  return <section className="map-inspector-section" data-testid="ras-layer-properties-pane">
    <div className="map-pane-heading">
      <div><span className="map-pane-kicker">02 / {heading}</span><h2>{heading}</h2></div>
      {featureSelection && <span className="map-selection-pill">{text(language, '已选择', 'Selected')}</span>}
    </div>
    <EmptySelectionOverlay isEmpty={!featureSelection && !backendSelection}
      text={text(language, '在地图或图层树中选择对象以查看属性。', 'Select an object on the map or in the layer tree to inspect its properties.')}>
      {featureSelection ? <>
        <div className="map-property-feature">
          <span className="map-property-type">{mapGeometryType(language, featureSelection.feature.geometry.type)}</span>
          <strong>{featureSelection.feature.name}</strong><span>{featureSelection.layer.name}</span>
        </div>
        <div className="map-feature-properties">
          <div className="map-property-row"><span>{text(language, '坐标系', 'CRS')}</span><code>{mapCRS(language, featureSelection.layer.crs)}</code></div>
          {Object.entries(featureSelection.feature.properties).map(([key, value]) =>
            <div className="map-property-row" key={key}><span>{key}</span><code>{typeof value === 'string' ? value : JSON.stringify(value)}</code></div>)}
          {Object.keys(featureSelection.feature.properties).length === 0 && <p className="map-inspector-empty">{text(language, '该要素没有附加属性。', 'This feature has no additional properties.')}</p>}
          <button type="button" className="map-zoom-feature" onClick={() => onZoomFeature(featureSelection.layer, featureSelection.feature)}>{text(language, '缩放到要素', 'Zoom to feature')} ↗</button>
        </div>
      </> : backendSelection && <div className="map-backend-inspector">
        <span className="map-property-type">{localizeProjectTreeSourceClass(language, backendSelection.sourceClass)}</span>
        <strong>{backendSelection.name}</strong>
        <p>{text(language, '属性与可用操作由当前工程对象提供。', 'Properties and available actions come from the selected project object.')}</p>
        {canZoomBackend && <button type="button" className="map-zoom-feature" onClick={() => onZoomBackend(backendSelection.nodeID)}>{text(language, '缩放到图层', 'Zoom to layer')} ↗</button>}
        {backendSelection.diagnostic && <details><summary>{text(language, '查看诊断详情', 'View diagnostic details')}</summary><pre>{mapError(language, backendSelection.diagnostic)}</pre></details>}
      </div>}
    </EmptySelectionOverlay>
    {children}
  </section>;
}
