import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryRow } from './geometryTypes';

export interface FlowAreaInformationProps {
  language: Language;
  rows: GeometryRow[];
}

/** Mirrors the source linked information panel, which displays the active flow area's cell count. */
export function FlowAreaInformation({ language, rows }: FlowAreaInformationProps) {
  const flowArea = rows.length === 1 ? rows[0] : undefined;
  const emptyText = rows.length > 1
    ? (language === 'zh' ? '此信息面板一次显示一个流动区域，请只保留一行选中。' : 'This information panel shows one flow area at a time. Select a single row.')
    : (language === 'zh' ? '从流动区域表中选择一行。' : 'Select a row in the flow area table.');

  return (
    <section className="geometry-panel geometry-detail-panel" aria-label={language === 'zh' ? '流动区域信息' : 'Flow area information'}>
      <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '流动区域网格信息' : 'Flow area mesh information'}</h3><p>{flowArea ? (language === 'zh' ? '当前所选区域' : 'Current selection') : (language === 'zh' ? '尚未选择流动区域。' : 'No flow area selected.')}</p></div></header>
      {flowArea ? <div className="geometry-panel-body geometry-kv-grid"><div className="geometry-kv"><span>{gt(language, 'cells')}</span><strong>{flowArea.values.CellCount || '—'}</strong></div></div> : <div className="geometry-empty geometry-detail-empty">{emptyText}</div>}
    </section>
  );
}

export default FlowAreaInformation;
