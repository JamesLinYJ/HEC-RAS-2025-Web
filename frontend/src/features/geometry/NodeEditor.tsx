import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { gt } from './geometryText';
import type { GeometryRow } from './geometryTypes';

export type NodeEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & { selectedRows: GeometryRow[] };

interface NodeMapFeature {
  featureKind: string;
  fid?: number | null;
  incidentArcs?: Array<{ arcID: number; length: number }> | null;
}

type NodeDetailsSnapshot = NonNullable<NodeEditorProps['topologySnapshot']> & {
  map?: { features?: NodeMapFeature[] } | null;
};

export function NodeEditor({ selectedRows, ...props }: NodeEditorProps) {
  const selectedSourceRows = selectedRows.filter((row) => row.fid != null && row.fid >= 0);
  const snapshot = props.topologySnapshot as NodeDetailsSnapshot | undefined;
  return <div className="geometry-grid">
    <section className="geometry-panel">
      <header className="geometry-panel-heading"><div><h3>{gt(props.language, 'node')} · {props.language === 'zh' ? '详细信息' : 'Details'}</h3><p>{selectedSourceRows.length} {props.language === 'zh' ? '个源节点' : 'source nodes'} · {props.language === 'zh' ? '拓扑修订' : 'Topology revision'} {props.topologySnapshot?.revision ?? '—'}</p></div>
        {props.topologySnapshot && <span className={`geometry-status-pill ${props.topologySnapshot.isDirty ? 'is-dirty' : 'is-clean'}`}>{gt(props.language, props.topologySnapshot.isDirty ? 'dirty' : 'clean')}</span>}
      </header>
      {selectedRows.length === 0 ? <div className="geometry-empty"><strong>{props.language === 'zh' ? '从节点表选择一个源节点查看 FID、坐标和关联弧。' : 'Select a source node to inspect its FID, coordinates, and incident arcs.'}</strong></div> : <div className="geometry-panel-body geometry-grid two">
        {selectedRows.map((row) => {
          const isSourceNode = row.fid != null && row.fid >= 0;
          const nodeFeature = isSourceNode
            ? snapshot?.map?.features?.find((feature) => feature.featureKind === 'node' && feature.fid === row.fid)
            : undefined;
          const incidentArcs = nodeFeature?.incidentArcs ?? [];
          const hasIncidentArcData = Array.isArray(nodeFeature?.incidentArcs);
          const x = row.values.X ?? (row.x == null ? '—' : String(row.x));
          const y = row.values.Y ?? (row.y == null ? '—' : String(row.y));
          return <article className="geometry-panel geometry-panel-body" key={row.id}>
            <div className="geometry-kv-grid">
              <div className="geometry-kv"><span>{props.language === 'zh' ? '节点 FID' : 'Node FID'}</span><strong>{isSourceNode ? row.fid : (props.language === 'zh' ? '本地草稿行' : 'Local draft row')}</strong></div>
              <div className="geometry-kv"><span>{props.language === 'zh' ? '源坐标 X' : 'Source coordinate X'}</span><strong>{x}</strong></div>
              <div className="geometry-kv"><span>{props.language === 'zh' ? '源坐标 Y' : 'Source coordinate Y'}</span><strong>{y}</strong></div>
            </div>
            <div className="geometry-inline-help" style={{ marginTop: 10 }}>
              <strong>{props.language === 'zh' ? '关联弧 ID' : 'Incident arc IDs'}</strong>
              {incidentArcs.length > 0
                ? <span> · {incidentArcs.map((arc) => `${arc.arcID} (${arc.length})`).join(' · ')}</span>
                : <span> · {props.language === 'zh'
                  ? (hasIncidentArcData ? '无关联弧。' : '当前节点快照未返回关联弧数据。')
                  : (hasIncidentArcData ? 'No incident arcs.' : 'The current node snapshot has no incident-arc data.')}</span>}
            </div>
          </article>;
        })}
      </div>}
    </section>
    <div className="geometry-inline-help">{props.language === 'zh'
      ? `节点身份与坐标来自拓扑快照（源修订 ${props.topologySnapshot?.revision ?? '—'}）。节点表中的坐标只读；可在上方拓扑地图拖动节点，修改先进入几何草稿，保存后写入源文件。`
      : `Node identity and coordinates come from the topology snapshot (source revision ${props.topologySnapshot?.revision ?? '—'}). Coordinates in the node table are read-only. Drag nodes on the topology map above to edit the geometry draft, then save to write the source file.`}</div>
  </div>;
}

export default NodeEditor;
