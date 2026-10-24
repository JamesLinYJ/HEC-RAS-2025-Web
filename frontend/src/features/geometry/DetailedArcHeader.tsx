import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';

export function DetailedArcHeader({ language, rows, snapshot }: { language: Language; rows: GeometryRow[]; snapshot?: GeometryTopologySnapshot }) {
  const sourceRows = rows.filter((row) => row.fid != null && row.fid >= 0);
  const errorCount = (row: GeometryRow): number => (snapshot?.completion?.messages ?? []).filter((message) =>
    message.severity.toLowerCase() === 'error' && message.targets?.some((target) => target.featureKind.toLowerCase() === 'arc'
      && (target.fid === row.fid || target.tableRowID === row.id)),
  ).length;
  const label = language === 'zh' ? '弧段详细信息' : 'Arc details';
  return <section className="geometry-panel" aria-label={label}>
    <header className="geometry-panel-heading"><div><h3>{label}</h3><p>{rows.length ? `${sourceRows.length} ${language === 'zh' ? '个源弧段' : 'source arcs'} · ${language === 'zh' ? '源修订' : 'Source revision'} ${snapshot?.revision ?? '—'}` : (language === 'zh' ? '选择弧段查看原始要素身份和标签摘要。' : 'Select arcs to inspect source identity and tag summary.')}</p></div>
      {snapshot && <span className="geometry-count-pill">{language === 'zh' ? '草稿' : 'Draft'} · {snapshot.draftState}</span>}
    </header>
    {sourceRows.length === 0 ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前所选弧段没有源 FID。' : 'The selection has no source arc FID.'}</strong></div> : <div className="geometry-panel-body geometry-grid two">
      {sourceRows.map((row) => {
        const assignedTags = (snapshot?.arcTags ?? []).filter((tag) => (row.values[tag.id] ?? '').trim() !== '').length;
        const errors = errorCount(row);
        return <div className="geometry-kv" key={row.id}>
          <span>{language === 'zh' ? '弧段 FID' : 'Arc FID'} · {row.id}</span>
          <strong>{row.fid} · {(snapshot?.arcTags ?? []).length} {language === 'zh' ? '个标签定义' : 'tag definitions'} / {assignedTags} {language === 'zh' ? '个有值' : 'assigned'} · {errors} {language === 'zh' ? '项错误' : 'errors'}</strong>
        </div>;
      })}
    </div>}
    <footer className="geometry-table-footer"><span>{gt(language, 'completion')} · {snapshot?.completion?.draftIdentity ?? '—'}</span><span>{language === 'zh' ? 'ID 来自当前拓扑响应' : 'IDs come from the current topology response'}</span></footer>
  </section>;
}

export default DetailedArcHeader;
