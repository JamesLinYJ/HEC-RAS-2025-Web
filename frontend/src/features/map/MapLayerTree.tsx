import { useEffect, useState } from 'react';
import type { CatalogTreeNode, GeoJsonLayer, MapDocumentTab } from './mapTypes';
import type { RasterCalculatorSessionLayer } from '../../shared/rasterCalculatorSession';
import { mapCRS, mapError } from './mapText';
import { mapSourceRemovalCheck } from './ProjectTreeListener';
import { RasTreeV2 } from './tree/RasTreeV2';

export interface CalculatorSessionLayerState {
  status: 'loading' | 'ready' | 'error';
  message?: string;
  diagnostic?: string;
}

export interface MapLayerTreeProps {
  interactionDisabled?: boolean;
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
  mapSourceRemovalAvailable?: boolean;
  mapSourceRemovalUnavailableReason?: string;
  mapSourceRemovalBusy?: boolean;
  mapSourceRemovalBlockedReason?: string;
  mapSourceRemovalStatus?: { state: string; message: string; needsReconcile: boolean; diagnostic?: unknown } | null;
  onRemoveBackendNode?: (node: CatalogTreeNode) => void;
  onReconcileBackendNodeRemoval?: () => void;
  calculatorLayers?: readonly RasterCalculatorSessionLayer[];
  selectedCalculatorLayerID?: string | null;
  calculatorStates?: Readonly<Record<string, CalculatorSessionLayerState | undefined>>;
  onSelectCalculatorLayer?: (id: string) => void;
  onZoomCalculatorLayer?: (layer: RasterCalculatorSessionLayer) => void;
  onToggleCalculatorLayer?: (id: string, visible: boolean) => void;
  onChangeCalculatorOpacity?: (id: string, opacity: number) => void;
  onEditCalculatorLayer?: (id: string) => void;
  onRemoveCalculatorLayer?: (id: string) => void;
  language: 'zh' | 'en';
}

export function MapLayerTree({
  interactionDisabled = false,
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
  mapSourceRemovalAvailable = false,
  mapSourceRemovalUnavailableReason,
  mapSourceRemovalBusy = false,
  mapSourceRemovalBlockedReason,
  mapSourceRemovalStatus = null,
  onRemoveBackendNode,
  onReconcileBackendNodeRemoval,
  calculatorLayers = [],
  selectedCalculatorLayerID = null,
  calculatorStates = {},
  onSelectCalculatorLayer,
  onZoomCalculatorLayer,
  onToggleCalculatorLayer,
  onChangeCalculatorOpacity,
  onEditCalculatorLayer,
  onRemoveCalculatorLayer,
  language,
}: MapLayerTreeProps) {
  const [confirmingRemovalNodeID, setConfirmingRemovalNodeID] = useState<string | null>(null);
  useEffect(() => setConfirmingRemovalNodeID(null), [document?.id, document?.visualizationRevision, selectedBackendNodeID]);
  const findSelectedNode = (nodes: CatalogTreeNode[], parent: CatalogTreeNode | null = null): { node: CatalogTreeNode; parent: CatalogTreeNode | null } | null => {
    for (const node of nodes) {
      if (node.id === selectedBackendNodeID) return { node, parent };
      const nested = node.children?.length ? findSelectedNode(node.children, node) : null;
      if (nested) return nested;
    }
    return null;
  };
  const selectedBackendEntry = document && selectedBackendNodeID ? findSelectedNode(document.roots) : null;
  const removalCheck = mapSourceRemovalCheck(selectedBackendEntry?.node ?? null, selectedBackendEntry?.parent ?? null);
  const removalReason = mapSourceRemovalBlockedReason ?? mapSourceRemovalUnavailableReason ?? (
    removalCheck.canRemove ? '' : language === 'zh'
      ? removalCheck.reason === 'missing-source' ? '缺少精确来源路径或读取修订，不能安全移除。'
        : removalCheck.reason === 'stale-source' ? '来源格式或修订与当前保存图层不匹配。'
          : '此节点类型当前不支持从地图中移除。'
      : removalCheck.reason === 'missing-source' ? 'An exact source path or revision is unavailable.'
        : removalCheck.reason === 'stale-source' ? 'The source format or revision does not match this saved layer.'
          : 'This node type cannot be removed from the map here.');
  const confirmingRemoval = confirmingRemovalNodeID !== null && confirmingRemovalNodeID === selectedBackendEntry?.node.id;
  const copy = language === 'zh'
    ? {
      title: '图层', local: '本地 GeoJSON', sources: '工程图层树',
      empty: '尚无图层。导入 GeoJSON 可在地图中预览真实要素。',
      localOnly: '本地预览在浏览器会话中有效；使用“导入工程图层”可将源文件保存到工程。',
      noMap: '本地预览未连接工程地图树。',
      treeError: '无法读取地图图层树',
      renderNote: '勾选图层可读取已保存的矢量、栅格和结果；双击已读取的图层可缩放到范围。结果时刻由下方时间轴控制，读取失败会显示具体说明。',
      up: '上移', down: '下移', remove: '移除', opacity: '透明度',
      calculators: '计算器图层（本次会话）', noCalculators: '当前保存地图没有计算器图层。可在栅格计算器中添加实时图层。',
      select: '选择计算器图层', zoom: '缩放到范围', edit: '编辑配方', hidden: '已隐藏',
      loading: '正在按当前地图视口更新…', ready: '已按当前地图视口更新', failed: '无法读取当前图层帧',
      pending: '等待当前地图图层帧…',
      dynamic: '动态', profiles: '个剖面', static: '静态', sessionOnly: '仅当前工程会话有效，不写入保存地图或 .vis。',
      diagnostics: '原始诊断',
      removeFromMap: '从此地图移除', cancelRemove: '取消', confirmRemove: '确认移除',
      removalScope: '只从当前地图文档移除此来源；不会删除或修改磁盘上的源文件。',
      reconcileRemove: '重新读取并核对',
    }
    : {
      title: 'Layers', local: 'Local GeoJSON', sources: 'Project layer tree',
      empty: 'No layers yet. Import GeoJSON to preview its actual features.',
      localOnly: 'Local previews live in this browser session. Use Import project layers to save source files into the project.',
      noMap: 'Local preview is not connected to a project map tree.',
      treeError: 'Could not load the map layer tree',
      renderNote: 'Checked layers read saved vectors, rasters and results; double-click a loaded layer to zoom to its extent. The timeline controls Result times; reading failures display a specific explanation.',
      up: 'Move up', down: 'Move down', remove: 'Remove', opacity: 'Opacity',
      calculators: 'Calculator layers (this session)', noCalculators: 'No calculator layers are attached to this saved map. Add a live layer from Raster Calculator.',
      select: 'Select calculator layer', zoom: 'Zoom to extent', edit: 'Edit recipe', hidden: 'Hidden',
      loading: 'Updating for the current map viewport…', ready: 'Updated for the current map viewport', failed: 'Could not read the current frame',
      pending: 'Waiting for the current map frame…',
      dynamic: 'Dynamic', profiles: 'profiles', static: 'Static', sessionOnly: 'Only available in this project session; not written to the saved map or .vis.',
      diagnostics: 'Raw diagnostic',
      removeFromMap: 'Remove from this map', cancelRemove: 'Cancel', confirmRemove: 'Confirm removal',
      removalScope: 'This removes the source from the current map document only. The source file on disk is not deleted or changed.',
      reconcileRemove: 'Reread and reconcile',
    };
  return (
    <aside className="map-layer-pane" aria-label={copy.title} inert={interactionDisabled}>
      <div className="map-pane-heading">
        <div>
          <span className="map-pane-kicker">01 / {copy.title}</span>
          <h2>{copy.title}</h2>
        </div>
        <span className="map-layer-count">{localLayers.length + (document?.roots.length ?? 0) + calculatorLayers.length}</span>
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

      <section className="map-tree-section map-calculator-session-section" data-testid="map-calculator-session-section">
        <div className="map-tree-section-title">
          <span className="map-section-caret" aria-hidden="true">⌄</span>
          <strong>{copy.calculators}</strong>
          <span className="map-section-count">{calculatorLayers.length}</span>
        </div>
        {calculatorLayers.length === 0 ? <p className="map-tree-empty">{copy.noCalculators}</p> : (
          <ul className="map-layer-list" aria-label={copy.calculators}>
            {calculatorLayers.map(layer => {
              const state = calculatorStates[layer.id];
              const selected = selectedCalculatorLayerID === layer.id;
              const statusText = !layer.visible ? copy.hidden
                : state?.status === 'loading' ? copy.loading
                  : state?.status === 'error' ? state.message || copy.failed
                    : state?.status === 'ready' ? copy.ready : copy.pending;
              return <li key={layer.id} className={'map-local-layer map-calculator-session-layer' + (selected ? ' is-selected' : '')}
                data-testid={`map-calculator-session-row-${layer.id}`} data-session-layer-id={layer.id}
                data-frame-state={state?.status ?? (layer.visible ? 'pending' : 'hidden')}>
                <div className="map-local-layer-main">
                  <input type="checkbox" checked={layer.visible}
                    aria-label={(language === 'zh' ? '显示计算器图层 ' : 'Show calculator layer ') + layer.name}
                    data-testid={`map-calculator-session-visible-${layer.id}`}
                    onChange={event => onToggleCalculatorLayer?.(layer.id, event.currentTarget.checked)} />
                  <button type="button" className="map-local-layer-name"
                    aria-label={copy.select + ' ' + layer.name}
                    data-testid={`map-calculator-session-select-${layer.id}`}
                    onClick={() => onSelectCalculatorLayer?.(layer.id)}>
                    <span className="map-layer-color map-calculator-layer-color" aria-hidden="true" />
                    <span>{layer.name}</span>
                  </button>
                  <span className="map-tree-kind">{layer.isDynamic ? `${copy.dynamic} · ${layer.profiles.length} ${copy.profiles}` : copy.static}</span>
                </div>
                <div className="map-local-layer-detail">
                  <span role={state?.status === 'error' ? 'alert' : 'status'} data-testid={`map-calculator-session-state-${layer.id}`}>
                    {statusText}
                  </span>
                  <div className="map-local-layer-actions">
                    <button type="button" title={copy.zoom} aria-label={copy.zoom + ' ' + layer.name}
                      data-testid={`map-calculator-session-zoom-${layer.id}`}
                      onClick={() => onZoomCalculatorLayer?.(layer)}>{language === 'zh' ? '⌖' : '↗'}</button>
                    <button type="button" title={copy.edit} aria-label={copy.edit + ' ' + layer.name}
                      data-testid={`map-calculator-session-edit-${layer.id}`}
                      onClick={() => onEditCalculatorLayer?.(layer.id)}>{language === 'zh' ? '配方' : 'Edit'}</button>
                    <button type="button" title={copy.remove} aria-label={copy.remove + ' ' + layer.name}
                      data-testid={`map-calculator-session-remove-${layer.id}`}
                      onClick={() => onRemoveCalculatorLayer?.(layer.id)}>×</button>
                  </div>
                </div>
                <label className="map-opacity">
                  <span>{copy.opacity}</span>
                  <input type="range" min="0" max="1" step="0.05" value={layer.opacity}
                    aria-label={copy.opacity + ' ' + layer.name}
                    data-testid={`map-calculator-session-opacity-${layer.id}`}
                    onChange={event => onChangeCalculatorOpacity?.(layer.id, Number(event.currentTarget.value))} />
                  <output>{Math.round(layer.opacity * 100)}%</output>
                </label>
                {state?.status === 'error' && state.diagnostic && <details className="map-calculator-session-diagnostic">
                  <summary>{copy.diagnostics}</summary><pre>{state.diagnostic}</pre>
                </details>}
              </li>;
            })}
          </ul>
        )}
        {calculatorLayers.length > 0 && <p className="map-tree-note">{copy.sessionOnly}</p>}
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
            {selectedBackendEntry && <section className="map-source-removal" aria-label={copy.removeFromMap}>
              <button type="button" data-testid="map-remove-source-action"
                aria-label={copy.removeFromMap + ' ' + selectedBackendEntry.node.name}
                title={removalReason || copy.removeFromMap}
                disabled={!removalCheck.canRemove || !mapSourceRemovalAvailable || mapSourceRemovalBusy || Boolean(mapSourceRemovalBlockedReason)}
                onClick={() => setConfirmingRemovalNodeID(selectedBackendEntry.node.id)}>
                {copy.removeFromMap}
              </button>
              {removalReason && <small role="status" data-testid="map-remove-source-unavailable">{removalReason}</small>}
              {confirmingRemoval && removalCheck.canRemove && <div className="map-source-removal-confirmation"
                role="alertdialog" aria-label={copy.confirmRemove} data-testid="map-remove-source-confirmation">
                <strong>{selectedBackendEntry.node.name}</strong>
                <p>{copy.removalScope}</p>
                <div className="map-source-removal-actions">
                  <button type="button" data-testid="map-remove-source-cancel" disabled={mapSourceRemovalBusy}
                    onClick={() => setConfirmingRemovalNodeID(null)}>{copy.cancelRemove}</button>
                  <button type="button" data-testid="map-remove-source-confirm" disabled={mapSourceRemovalBusy || !mapSourceRemovalAvailable || Boolean(mapSourceRemovalBlockedReason)}
                    onClick={() => {
                      setConfirmingRemovalNodeID(null);
                      onRemoveBackendNode?.(selectedBackendEntry.node);
                    }}>{mapSourceRemovalBusy ? (language === 'zh' ? '正在处理…' : 'Working…') : copy.confirmRemove}</button>
                </div>
              </div>}
            </section>}
            <p className="map-tree-note">{copy.renderNote}</p>
          </>
        )}
        {mapSourceRemovalStatus && <div className="map-source-removal-status" role={mapSourceRemovalStatus.state === 'uncertain' || mapSourceRemovalStatus.state === 'inconclusive' ? 'alert' : 'status'}
          data-testid="map-remove-source-status" data-state={mapSourceRemovalStatus.state}>
          <span>{mapSourceRemovalStatus.message}</span>
          {mapSourceRemovalStatus.diagnostic !== undefined && <details data-testid="map-remove-source-diagnostic">
            <summary>{copy.treeError}</summary><pre>{JSON.stringify(mapSourceRemovalStatus.diagnostic, null, 2)}</pre>
          </details>}
          {mapSourceRemovalStatus.needsReconcile && <button type="button" data-testid="map-remove-source-reconcile"
            disabled={mapSourceRemovalBusy} onClick={onReconcileBackendNodeRemoval}>{copy.reconcileRemove}</button>}
        </div>}
      </section>
    </aside>
  );
}
