import { useEffect } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { GeometryTopologyCanvas } from './GeometryTopologyCanvas';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';
import { TopologyMapArcTools } from './TopologyMapArcTools';
import { TopologySmartAddControls } from './TopologySmartAddControls';
import { TopologyDeepConnectControls } from './TopologyDeepConnectControls';
import { TopologyDeepConnectVectorControls } from './TopologyDeepConnectVectorControls';

export interface GeometryTopologyMapPanelProps {
  language: Language;
  workspace: GeometryTopologyMapWorkspace;
  showArcTools?: boolean;
  allowMapEditing?: boolean;
}

export function GeometryTopologyMapPanel({ language, workspace, showArcTools = false, allowMapEditing = true }: GeometryTopologyMapPanelProps) {
  useEffect(() => {
    if (!showArcTools && workspace.activeTool !== 'select') workspace.onCancelDeepConnect();
  }, [showArcTools, workspace.activeTool, workspace.onCancelDeepConnect]);

  const canvasTool = showArcTools ? workspace.activeTool : 'select';
  const text = language === 'zh'
    ? {
      title: '拓扑地图', subtitle: '空间选集和地图编辑使用当前几何拓扑草稿；地图来源必须与所选几何文件精确匹配。',
      source: '保存地图 / 几何来源', loading: '正在读取保存地图目录…', capturing: '正在确认地图几何来源…',
      scope: '框选范围', mode: '选集方式', hit: '框选命中', all: '全部要素', arcs: '弧线', nodes: '节点', regions: '区域',
      replace: '替换选集', append: '追加到选集', ensure: '确保包含', toggle: '切换选中', remove: '从选集中移除',
      intersects: '与框相交', contained: '完全包含', noTarget: '没有可用于此几何文件的精确地图来源。地图编辑已停用。',
    }
    : {
      title: 'Topology map', subtitle: 'Spatial selection and map edits use the current geometry topology draft. The saved map must reference this exact geometry file.',
      source: 'Saved map / Geometry source', loading: 'Reading saved map catalog…', capturing: 'Verifying map Geometry source…',
      scope: 'Extent scope', mode: 'Selection mode', hit: 'Extent hit mode', all: 'All features', arcs: 'Arcs', nodes: 'Nodes', regions: 'Regions',
      replace: 'Replace selection', append: 'Append selection', ensure: 'Ensure included', toggle: 'Toggle selection', remove: 'Remove from selection',
      intersects: 'Intersects', contained: 'Contained', noTarget: 'No saved map has an exact source locator for this geometry. Map edits are disabled.',
    };

  return <section className="geometry-panel geometry-topology-map-panel" data-testid="geometry-topology-map-workspace">
    <header className="geometry-panel-heading">
      <div><h2>{text.title}</h2><p>{text.subtitle}</p></div>
      {workspace.targets.length > 0 && <label className="geometry-form-field">
        {text.source}
        <select aria-label={text.source} value={workspace.selectedTargetKey}
          onChange={(event) => workspace.onTargetChange(event.currentTarget.value)}>
          {workspace.targets.map((target) => <option key={target.key} value={target.key}>{target.label}</option>)}
        </select>
      </label>}
    </header>
    <div className="geometry-panel-body">
      {workspace.loading && <span className="geometry-loading">{text.loading}</span>}
      {!workspace.loading && workspace.targets.length === 0 && <div className="geometry-inline-help">{text.noTarget}</div>}
      {workspace.capturing && <span className="geometry-loading">{text.capturing}</span>}
      {workspace.error && <GeometryError language={language} error={workspace.error}
        title={language === 'zh' ? '地图交互不可用' : 'Map interaction unavailable'} />}
      <div className="geometry-topology-map-controls">
        <button type="button" className="geometry-secondary" data-testid="geometry-topology-zoom-selection"
          disabled={!workspace.canZoomToSelection || workspace.canvasProps.busy}
          onClick={workspace.onZoomToSelection}>
          {language === 'zh' ? '缩放至选中要素' : 'Zoom to selected features'} ({workspace.selectedFeatureCount})
        </button>
        <label className="geometry-form-field">{text.scope}<select value={workspace.selectionScope}
          onChange={(event) => workspace.onSelectionScopeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['selectionScope'])}>
          <option value="All">{text.all}</option><option value="Arcs">{text.arcs}</option>
          <option value="Nodes">{text.nodes}</option><option value="Regions">{text.regions}</option>
        </select></label>
        <label className="geometry-form-field">{text.mode}<select value={workspace.selectionMode}
          onChange={(event) => workspace.onSelectionModeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['selectionMode'])}>
          <option value="Replace">{text.replace}</option><option value="Append">{text.append}</option>
          <option value="EnsureIncluded">{text.ensure}</option><option value="Toggle">{text.toggle}</option>
          <option value="Remove">{text.remove}</option>
        </select></label>
        <label className="geometry-form-field">{text.hit}<select value={workspace.marqueeHitMode}
          onChange={(event) => workspace.onMarqueeHitModeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['marqueeHitMode'])}>
          <option value="Intersects">{text.intersects}</option><option value="Contained">{text.contained}</option>
        </select></label>
      </div>
      {showArcTools && <TopologyMapArcTools language={language} mapCaptureReady={workspace.canvasProps.mapCaptureReady}
        canSelect={workspace.canvasProps.canSelect} canEdit={workspace.canvasProps.canMoveNodes} busy={workspace.canvasProps.busy}
        canSmartAdd={workspace.canSmartAdd}
        draftReady={workspace.draftReady} draftDirty={workspace.draftDirty}
        mapCellSize={workspace.mapCellSize} activeTool={workspace.activeTool}
        onActiveToolChange={workspace.onActiveToolChange} selectedArcFIDs={workspace.selectedArcFIDs}
        splitMode={workspace.splitMode} onSplitModeChange={workspace.onSplitModeChange}
        onRunArcCommand={(command) => { void workspace.onMapCommand(command).catch(() => undefined); }} />}
      {showArcTools && workspace.activeTool === 'smartAdd' && <TopologySmartAddControls language={language}
        canApply={workspace.canSmartAdd && workspace.draftReady} busy={workspace.interactionBusy}
        previewAvailable={workspace.previewAvailable} previewState={workspace.previewState}
        terrainCapture={workspace.terrainCapture} measuredWindow={workspace.measuredWindow}
        mapCellSize={workspace.mapCellSize} initialOptions={workspace.smartAddOptions}
        onApply={workspace.onApplySmartAddOptions} />}
      {showArcTools && workspace.activeTool === 'deepConnect' && <TopologyDeepConnectControls language={language}
        canApply={workspace.draftReady && workspace.canvasProps.mapCaptureReady} busy={workspace.interactionBusy}
        previewAvailable={workspace.previewAvailable} previewState={workspace.previewState}
        sourceKind={workspace.deepConnectSourceKind} sourceLabel={workspace.deepConnectSourceLabel}
        sourceFID={workspace.deepConnectSourceFID} anchorCount={workspace.deepConnectAnchorCount}
        onCancel={workspace.onCancelDeepConnect} />}
      {showArcTools && workspace.activeTool === 'deepConnect' && <TopologyDeepConnectVectorControls language={language}
        candidates={workspace.vectorCandidates} inspection={workspace.vectorInspection}
        selectedComponentKey={workspace.vectorSelectedComponentKey} busy={workspace.vectorBusy || workspace.interactionBusy}
        canInspect={workspace.vectorCanInspect} error={workspace.vectorError}
        onInspect={workspace.onInspectVectorSources} onSelectComponent={workspace.onSelectVectorComponent}
        onCancel={workspace.onCancelVectorSource} />}
      <GeometryTopologyCanvas language={language} {...workspace.canvasProps} tool={canvasTool}
        canMoveNodes={allowMapEditing && workspace.canvasProps.canMoveNodes} />
    </div>
  </section>;
}

export default GeometryTopologyMapPanel;
