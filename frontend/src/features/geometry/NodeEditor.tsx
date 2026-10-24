import { useMemo, useState } from 'react';
import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import { gt } from './geometryText';

export type NodeEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'>;

export function NodeEditor(props: NodeEditorProps) {
  const [selectedIDs, setSelectedIDs] = useState<string[]>([]);
  const rows = useMemo(() => [...(props.table?.rows ?? []), ...props.localRows], [props.table?.rows, props.localRows]);
  const selectedRows = rows.filter((row) => selectedIDs.includes(row.id));
  const selectedSourceRows = selectedRows.filter((row) => row.fid != null && row.fid >= 0);
  return <div className="geometry-grid">
    <section className="geometry-panel">
      <header className="geometry-panel-heading"><div><h3>{gt(props.language, 'node')} · {props.language === 'zh' ? '详细信息' : 'Details'}</h3><p>{selectedSourceRows.length} {props.language === 'zh' ? '个源节点' : 'source nodes'} · {props.language === 'zh' ? '拓扑修订' : 'Topology revision'} {props.topologySnapshot?.revision ?? '—'}</p></div>
        {props.topologySnapshot && <span className="geometry-count-pill">{props.language === 'zh' ? '草稿' : 'Draft'} · {props.topologySnapshot.draftState}</span>}
      </header>
      {selectedRows.length === 0 ? <div className="geometry-empty"><strong>{props.language === 'zh' ? '从节点表选择一个源节点查看 FID。' : 'Select a source node in the table to inspect its FID.'}</strong></div> : <div className="geometry-panel-body geometry-grid two">
        {selectedRows.map((row) => <div className="geometry-kv" key={row.id}><span>{props.language === 'zh' ? '节点 FID' : 'Node FID'} · {row.id}</span><strong>{row.fid != null && row.fid >= 0 ? row.fid : (props.language === 'zh' ? '本地草稿行' : 'Local draft row')}</strong></div>)}
      </div>}
    </section>
    <GeometryFeatureDetailEditor language={props.language} kind="node" table={props.table} localRows={props.localRows}
      fieldOrder={['X', 'Y']} readOnlyFieldIDs={['X', 'Y']} editable={props.canEdit} busy={props.busy}
      onChange={props.onChange} onDelete={props.onDelete}
      onSelectionChange={(selected) => { setSelectedIDs(selected.map((row) => row.id)); props.onSelectionChange?.(selected); }} />
    <div className="geometry-inline-help">{props.language === 'zh'
      ? `节点身份与坐标来自拓扑快照（源修订 ${props.topologySnapshot?.revision ?? '—'}）；当前 Host 未提供节点移动命令，坐标保持只读。`
      : `Node identity and coordinates come from the topology snapshot (source revision ${props.topologySnapshot?.revision ?? '—'}); the current host has no node-move command, so coordinates stay read-only.`}</div>
  </div>;
}

export default NodeEditor;
