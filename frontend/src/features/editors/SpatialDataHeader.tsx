import type { EditorPageProps } from './model';

export const SPATIAL_BOUNDARY_LAYERS = [
  ['precipitation', '降水', 'Precipitation'],
  ['evapotranspiration', '蒸散发', 'Evapotranspiration'],
  ['wind', '风', 'Wind'],
  ['airPressure', '气压', 'Air pressure'],
  ['airDensity', '空气密度', 'Air density'],
] as const;

export type SpatialBoundaryLayerID = typeof SPATIAL_BOUNDARY_LAYERS[number][0];

export function SpatialDataHeader(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onLayerChange: (layerId: SpatialBoundaryLayerID) => void;
}) {
  const title = SPATIAL_BOUNDARY_LAYERS.find(([id]) => id === props.layerId);
  const spatial = props.draft.boundaryView?.spatial ?? props.snapshot.boundary?.spatial;
  const layer = spatial?.[props.layerId];
  return <header className="editor-meteorology-layer-header">
    <div>
      <p className="editor-eyebrow">{props.language === 'zh' ? '空间边界数据' : 'Spatial boundary data'}</p>
      <h2>{props.language === 'zh' ? title?.[1] : title?.[2]}</h2>
      <div className="editor-meteorology-source-summary">
        <span>{(props.language === 'zh' ? '测站' : 'Stations') + ': ' + (layer?.stations.length ?? 0)}</span>
        <span>{(props.language === 'zh' ? '输入' : 'Inputs') + ': ' + (layer?.inputs.length ?? 0)}</span>
        {layer?.unit && <span>{(props.language === 'zh' ? '源单位' : 'Source units') + ': ' + layer.unit}</span>}
      </div>
    </div>
    <div className="editor-workbench-actions">
      <label className="editor-field">
        <span>{props.language === 'zh' ? '边界类型' : 'Boundary layer'}</span>
        <select value={props.layerId} onChange={(event) => props.onLayerChange(event.currentTarget.value as SpatialBoundaryLayerID)}>
          {SPATIAL_BOUNDARY_LAYERS.map(([id, zh, en]) => <option key={id} value={id}>{props.language === 'zh' ? zh : en}</option>)}
        </select>
      </label>
    </div>
  </header>;
}
