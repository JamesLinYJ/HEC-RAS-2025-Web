import { useEffect, useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { TopologyNodeActiveFeatures } from './TopologyNodeActiveFeatures';
import { NodeEditor } from './NodeEditor';
import { TopologyFeatureErrorControl } from './TopologyFeatureErrorControl';
import type { GeometryRow } from './geometryTypes';
import { GeometryTopologyCanvas } from './GeometryTopologyCanvas';
import { GeometryError } from './GeometryError';
import type { GeometryTopologyMapWorkspace } from './useGeometryTopologyMap';

export type TopologyNodesEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  mapWorkspace?: GeometryTopologyMapWorkspace;
};

function topologyMapPanel(language: 'zh' | 'en', workspace: GeometryTopologyMapWorkspace) {
  const text = language === 'zh'
    ? { title: '拓扑地图', subtitle: '地图空间选集和节点拖动使用当前几何拓扑草稿；地图来源必须与所选几何文件精确匹配。', source: '保存地图 / 几何来源', loading: '正在读取保存地图目录…', capturing: '正在确认地图几何来源…', scope: '框选范围', mode: '选集方式', hit: '框选命中', all: '全部要素', arcs: '弧线', nodes: '节点', regions: '区域', replace: '替换选集', append: '追加到选集', ensure: '确保包含', toggle: '切换选中', remove: '从选集中移除', intersects: '与框相交', contained: '完全包含', noTarget: '没有可用于此几何文件的精确地图来源。地图保持只读。' }
    : { title: 'Topology map', subtitle: 'Map selection and node dragging use the current geometry topology draft. The saved map must reference this exact geometry file.', source: 'Saved map / Geometry source', loading: 'Reading saved map catalog…', capturing: 'Verifying map Geometry source…', scope: 'Extent scope', mode: 'Selection mode', hit: 'Extent hit mode', all: 'All features', arcs: 'Arcs', nodes: 'Nodes', regions: 'Regions', replace: 'Replace selection', append: 'Append selection', ensure: 'Ensure included', toggle: 'Toggle selection', remove: 'Remove from selection', intersects: 'Intersects', contained: 'Contained', noTarget: 'No saved map has an exact source locator for this geometry. The map is read-only.' };
  return <section className="geometry-panel geometry-topology-map-panel">
    <header className="geometry-panel-heading">
      <div><h2>{text.title}</h2><p>{text.subtitle}</p></div>
      {workspace.targets.length > 0 && <label className="geometry-form-field">
        {text.source}
        <select value={workspace.selectedTargetKey} onChange={(event) => workspace.onTargetChange(event.currentTarget.value)}>
          {workspace.targets.map((target) => <option key={target.key} value={target.key}>{target.label}</option>)}
        </select>
      </label>}
    </header>
    <div className="geometry-panel-body">
      {workspace.loading && <span className="geometry-loading">{text.loading}</span>}
      {!workspace.loading && workspace.targets.length === 0 && <div className="geometry-inline-help">{text.noTarget}</div>}
      {workspace.capturing && <span className="geometry-loading">{text.capturing}</span>}
      {workspace.error && <GeometryError language={language} error={workspace.error} title={language === 'zh' ? '地图交互不可用' : 'Map interaction unavailable'} />}
      <div className="geometry-topology-map-controls">
        <label className="geometry-form-field">{text.scope}<select value={workspace.selectionScope} onChange={(event) => workspace.onSelectionScopeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['selectionScope'])}>
          <option value="All">{text.all}</option><option value="Arcs">{text.arcs}</option><option value="Nodes">{text.nodes}</option><option value="Regions">{text.regions}</option>
        </select></label>
        <label className="geometry-form-field">{text.mode}<select value={workspace.selectionMode} onChange={(event) => workspace.onSelectionModeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['selectionMode'])}>
          <option value="Replace">{text.replace}</option><option value="Append">{text.append}</option><option value="EnsureIncluded">{text.ensure}</option><option value="Toggle">{text.toggle}</option><option value="Remove">{text.remove}</option>
        </select></label>
        <label className="geometry-form-field">{text.hit}<select value={workspace.marqueeHitMode} onChange={(event) => workspace.onMarqueeHitModeChange(event.currentTarget.value as GeometryTopologyMapWorkspace['marqueeHitMode'])}>
          <option value="Intersects">{text.intersects}</option><option value="Contained">{text.contained}</option>
        </select></label>
      </div>
      <GeometryTopologyCanvas language={language} {...workspace.canvasProps} />
    </div>
  </section>;
}

export function TopologyNodesEditor(props: TopologyNodesEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));

  useEffect(() => {
    const available = new Set(rows.map((row) => row.id));
    setSelectedIDs((current) => {
      const retained = current.filter((id) => available.has(id));
      return retained.length === current.length ? current : retained;
    });
  }, [rows]);

  const selectRows = (nextRows: GeometryRow[]): void => {
    const nextIDs = nextRows.map((row) => row.id);
    setSelectedIDs(nextIDs);
    props.onSelectionChange?.(nextRows);
  };

  const mapNodeSelection = props.mapWorkspace?.canvasProps.selectedFIDByKind.node ?? [];
  const mapNodeSelectionKey = mapNodeSelection.join(',');
  useEffect(() => {
    if (!props.mapWorkspace) return;
    const wanted = new Set(mapNodeSelection);
    const next = rows.filter((row) => Number.isInteger(row.fid) && wanted.has(row.fid!)).map((row) => row.id);
    setSelectedIDs((current) => current.length === next.length && current.every((id, index) => id === next[index]) ? current : next);
  }, [mapNodeSelectionKey, props.mapWorkspace, rows]);

  return <div className="geometry-grid" data-testid="geometry-topology-nodes-editor">
    <TopologyNodeActiveFeatures {...props} selectedRowIDs={selectedIDs} onSelectRows={selectRows} />
    {props.mapWorkspace && topologyMapPanel(props.language, props.mapWorkspace)}
    <NodeEditor {...props} selectedRows={selectedRows} />
    <TopologyFeatureErrorControl language={props.language} featureKind="node" rows={selectedRows} snapshot={props.topologySnapshot} />
  </div>;
}

export default TopologyNodesEditor;
