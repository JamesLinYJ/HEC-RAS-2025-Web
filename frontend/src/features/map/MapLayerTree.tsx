import type { CatalogTreeNode, GeoJsonLayer, MapDocumentTab } from './mapTypes';
import { mapCRS, mapError } from './mapText';
import { RasTreeV2 } from './tree/RasTreeV2';

export interface MapLayerTreeProps {
  document: MapDocumentTab | null;
  localLayers: GeoJsonLayer[];
  selectedLocalLayerID: string | null;
  selectedBackendNodeID: string | null;
  busyNodeID: string | null;
  onSelectLocalLayer: (id: string) => void;
  onSelectBackendNode: (node: CatalogTreeNode) => void;
  onZoomBackendNode: (node: CatalogTreeNode) => void;
  onToggleLocalLayer: (id: string, visible: boolean) => void;
  onChangeOpacity: (id: string, opacity: number) => void;
  onMoveLocalLayer: (id: string, direction: -1 | 1) => void;
  onRemoveLocalLayer: (id: string) => void;
  onToggleBackendNode: (node: CatalogTreeNode, checked: boolean) => void;
  language: 'zh' | 'en';
}

export function MapLayerTree({
  document,
  localLayers,
  selectedLocalLayerID,
  selectedBackendNodeID,
  busyNodeID,
  onSelectLocalLayer,
  onSelectBackendNode,
  onZoomBackendNode,
  onToggleLocalLayer,
  onChangeOpacity,
  onMoveLocalLayer,
  onRemoveLocalLayer,
  onToggleBackendNode,
  language,
}: MapLayerTreeProps) {
  const copy = language === 'zh'
    ? {
      title: '图层', local: '本地 GeoJSON', sources: '工程图层树',
      empty: '尚无图层。导入 GeoJSON 可在地图中预览真实要素。',
      localOnly: '本地预览在浏览器会话中有效；使用“导入工程图层”可将源文件保存到工程。',
      noMap: '本地预览未连接工程地图树。',
      treeError: '无法读取地图图层树',
      renderNote: '勾选图层可读取已保存的矢量、栅格和结果；双击已读取的图层可缩放到范围。结果时刻由下方时间轴控制，读取失败会显示具体说明。',
      up: '上移', down: '下移', remove: '移除', opacity: '透明度',
    }
    : {
      title: 'Layers', local: 'Local GeoJSON', sources: 'Project layer tree',
      empty: 'No layers yet. Import GeoJSON to preview its actual features.',
      localOnly: 'Local previews live in this browser session. Use Import project layers to save source files into the project.',
      noMap: 'Local preview is not connected to a project map tree.',
      treeError: 'Could not load the map layer tree',
      renderNote: 'Checked layers read saved vectors, rasters and results; double-click a loaded layer to zoom to its extent. The timeline controls Result times; reading failures display a specific explanation.',
      up: 'Move up', down: 'Move down', remove: 'Remove', opacity: 'Opacity',
    };
  return (
    <aside className="map-layer-pane" aria-label={copy.title}>
      <div className="map-pane-heading">
        <div>
          <span className="map-pane-kicker">01 / {copy.title}</span>
          <h2>{copy.title}</h2>
        </div>
        <span className="map-layer-count">{localLayers.length + (document?.roots.length ?? 0)}</span>
      </div>
      <section className="map-tree-section">
        <div className="map-tree-section-title">
          <span className="map-section-caret" aria-hidden="true">⌄</span>
          <strong>{copy.local}</strong>
          <span className="map-section-count">{localLayers.length}</span>
        </div>
        {localLayers.length === 0 ? <p className="map-tree-empty">{copy.empty}</p> : (
          <ul className="map-layer-list" aria-label={copy.local}>
            {[...localLayers].reverse().map((layer, reversedIndex) => {
              const position = localLayers.length - reversedIndex - 1;
              return (
                <li key={layer.id} className={'map-local-layer' + (selectedLocalLayerID === layer.id ? ' is-selected' : '')}>
                  <div className="map-local-layer-main">
                    <input type="checkbox" checked={layer.visible} onChange={(event) => onToggleLocalLayer(layer.id, event.currentTarget.checked)} aria-label={(language === 'zh' ? '显示 ' : 'Show ') + layer.name} />
                    <button type="button" className="map-local-layer-name" onClick={() => onSelectLocalLayer(layer.id)} title={layer.sourceName}>
                      <span className="map-layer-color" aria-hidden="true" />
                      <span>{layer.name}</span>
                    </button>
                    <span className="map-tree-kind">{layer.features.length}</span>
                  </div>
                  <div className="map-local-layer-detail">
                    <span>{mapCRS(language, layer.crs)}</span>
                    <div className="map-local-layer-actions">
                      <button type="button" onClick={() => onMoveLocalLayer(layer.id, 1)} disabled={position === localLayers.length - 1} title={copy.up} aria-label={copy.up}>↑</button>
                      <button type="button" onClick={() => onMoveLocalLayer(layer.id, -1)} disabled={position === 0} title={copy.down} aria-label={copy.down}>↓</button>
                      <button type="button" onClick={() => onRemoveLocalLayer(layer.id)} title={copy.remove} aria-label={copy.remove}>×</button>
                    </div>
                  </div>
                  <label className="map-opacity">
                    <span>{copy.opacity}</span>
                    <input type="range" min="0" max="1" step="0.05" value={layer.opacity} onChange={(event) => onChangeOpacity(layer.id, Number(event.currentTarget.value))} aria-label={copy.opacity + ' ' + layer.name} />
                    <output>{Math.round(layer.opacity * 100)}%</output>
                  </label>
                </li>
              );
            })}
          </ul>
        )}
        {localLayers.length > 0 && <p className="map-tree-note">{copy.localOnly}</p>}
      </section>

      <section className="map-tree-section map-project-tree-section">
        <div className="map-tree-section-title">
          <span className="map-section-caret" aria-hidden="true">⌄</span>
          <strong>{copy.sources}</strong>
          <span className="map-section-count">{document?.roots.length ?? 0}</span>
        </div>
        {!document ? <p className="map-tree-empty">{copy.noMap}</p> : document.treeError ? <p className="map-tree-empty map-tree-load-error">{copy.treeError}: {mapError(language, document.treeError)}</p> : document.roots.length === 0 ? <p className="map-tree-empty">{copy.empty}</p> : (
          <>
            <RasTreeV2
              roots={document.roots}
              label={copy.sources}
              selectedID={selectedBackendNodeID}
              busyNodeID={busyNodeID}
              onSelect={onSelectBackendNode}
              onZoom={onZoomBackendNode}
              onToggle={onToggleBackendNode}
              language={language}
            />
            <p className="map-tree-note">{copy.renderNote}</p>
          </>
        )}
      </section>
    </aside>
  );
}
