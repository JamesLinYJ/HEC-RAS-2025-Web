import type { Language } from '../../shared/workbench';
import type { GeometryRow } from './geometryTypes';

export interface FlowAreaMetaDataContextualControlProps {
  language: Language;
  rows: GeometryRow[];
}

/** Read-only mesh metadata from the selected flow-area snapshot. */
export function FlowAreaMetaDataContextualControl({ language, rows }: FlowAreaMetaDataContextualControlProps) {
  const selected = rows.length === 1 ? rows[0] : undefined;
  const metrics = [
    ['HasMesh', language === 'zh' ? '已有网格' : 'Mesh exists'],
    ['FaceCount', language === 'zh' ? '网格面数' : 'Mesh faces'],
    ['NodeCount', language === 'zh' ? '网格节点' : 'Mesh nodes'],
    ['HasMeshErrors', language === 'zh' ? '网格错误' : 'Mesh errors'],
    ['HasPropertyTables', language === 'zh' ? '属性表' : 'Property tables'],
  ];
  return <section className="geometry-panel geometry-detail-panel" aria-label={language === 'zh' ? '流动区域元数据' : 'Flow area metadata'}>
    <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '网格元数据' : 'Mesh metadata'}</h3>
      <p>{selected?.values.Name || (language === 'zh' ? '选择一个流动区域查看其网格状态。' : 'Select a flow area to inspect its mesh status.')}</p></div></header>
    {selected ? <div className="geometry-kv-grid">{metrics.map(([field, title]) => <div className="geometry-kv" key={field}>
      <span>{title}</span><strong>{displayValue(selected.values[field] ?? '', language)}</strong>
    </div>)}</div> : <div className="geometry-empty geometry-detail-empty"><strong>{language === 'zh' ? '元数据面板一次显示一个流动区域。' : 'This metadata panel shows one flow area at a time.'}</strong></div>}
  </section>;
}

function displayValue(value: string, language: Language): string {
  if (value.toLowerCase() === 'true') return language === 'zh' ? '是' : 'Yes';
  if (value.toLowerCase() === 'false') return language === 'zh' ? '否' : 'No';
  return value || '—';
}

export default FlowAreaMetaDataContextualControl;
