import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';

export function DetailedControlPointHeader({ language, rows, snapshot }: { language: Language; rows: GeometryRow[]; snapshot?: GeometryTopologySnapshot }) {
  const sourceRows = rows.filter((row) => row.fid != null && row.fid >= 0);
  const label = language === 'zh' ? '控制点详细信息' : 'Control point details';
  return <section className="geometry-panel" aria-label={label}>
    <header className="geometry-panel-heading"><div><h3>{label}</h3><p>{rows.length ? `${sourceRows.length} ${language === 'zh' ? '个源控制点' : 'source control points'} · ${language === 'zh' ? '源修订' : 'Source revision'} ${snapshot?.revision ?? '—'}` : (language === 'zh' ? '选择区域控制点查看 FID、标签和完成检查关联。' : 'Select region control points to inspect FIDs, tags, and completion-check links.')}</p></div>
      {snapshot && <span className="geometry-count-pill">{language === 'zh' ? '草稿' : 'Draft'} · {snapshot.draftState}</span>}
    </header>
    {sourceRows.length === 0 ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前所选控制点没有源 FID。' : 'The selection has no source control-point FID.'}</strong></div> : <div className="geometry-panel-body geometry-grid two">
      {sourceRows.map((row) => {
        const assignedTags = (snapshot?.regionTags ?? []).filter((tag) => (row.values[tag.id] ?? '').trim() !== '').length;
        return <div className="geometry-kv" key={row.id}>
          <span>{language === 'zh' ? '控制点 FID' : 'Control point FID'} · {row.id}</span>
          <strong>{row.fid} · {(snapshot?.regionTags ?? []).length} {language === 'zh' ? '个标签定义' : 'tag definitions'} / {assignedTags} {language === 'zh' ? '个有值' : 'assigned'}</strong>
        </div>;
      })}
    </div>}
    <footer className="geometry-table-footer"><span>{gt(language, 'completion')} · {snapshot?.completion?.draftIdentity ?? '—'}</span><span>{language === 'zh' ? '当前 Host 未返回控制点所属区域 FID 映射' : 'The current host does not return control-point region-FID mappings'}</span></footer>
  </section>;
}

export default DetailedControlPointHeader;
