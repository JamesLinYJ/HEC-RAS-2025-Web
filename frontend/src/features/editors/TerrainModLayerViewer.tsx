import type { TerrainTopologyFeature } from './terrainTopologyTypes';

/** Source TerrainModLayerViewer identity and value list backed by the current Host feature collection. */
export function TerrainModLayerViewer({
  features,
  selectedIDs,
  disabled,
  onSelect,
}: {
  features: TerrainTopologyFeature[];
  selectedIDs: string[];
  disabled?: boolean;
  onSelect: (feature: TerrainTopologyFeature, selected: boolean) => void;
}) {
  const selected = new Set(selectedIDs);
  return <section className="terrain-mod-layer-viewer" aria-label="地形修改图层" data-testid="terrain-mod-layer-viewer">
    <h3>地形修改图层 <span>{features.length}</span></h3>
    {features.length === 0
      ? <p className="editor-field-muted">当前保存输入没有多边形修改。可在地图上放置形状或绘制多边形。</p>
      : <ul className="terrain-mod-feature-list">
        {features.map((feature, index) => {
          const value = feature.metadata?.find((item) => item.id === 'Value')?.value ?? '—';
          const type = feature.metadata?.find((item) => item.id === 'ModificationType')?.value ?? '—';
          const isSelected = selected.has(feature.id);
          return <li key={feature.id} className={isSelected ? 'terrain-mod-feature-selected' : undefined}>
            <button type="button" className="terrain-mod-feature-select" data-feature-id={feature.id} aria-pressed={isSelected} disabled={disabled} onClick={() => onSelect(feature, isSelected)}>
              <span>修改 {index + 1}</span>
              <small>{type} · {value}</small>
            </button>
            <details><summary>来源要素身份</summary><code>{feature.id}</code></details>
          </li>;
        })}
      </ul>}
  </section>;
}
