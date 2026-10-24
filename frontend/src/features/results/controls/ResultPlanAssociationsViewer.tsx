import type { Language } from '../../../shared/workbench';
import type { ResultInspectorSnapshot, ResultPlanAssociationsSnapshot } from './resultDetailsTypes';

interface ResultPlanAssociationsViewerProps {
  language: Language;
  snapshot: ResultPlanAssociationsSnapshot | null;
  inspector: ResultInspectorSnapshot;
  loading: boolean;
  error: string | null;
}

/** Displays associations read from the Plan embedded in this exact Result. */
export function ResultPlanAssociationsViewer({ language, snapshot, inspector, loading, error }: ResultPlanAssociationsViewerProps) {
  const zh = language === 'zh';
  const fallback = snapshot === null;
  const values = snapshot ? {
    hasPlan: snapshot.hasPlan,
    planName: snapshot.planName,
    geometryName: snapshot.geometryName,
    boundaryConditionName: snapshot.boundaryConditionName,
  } : {
    hasPlan: inspector.plan !== null,
    planName: inspector.plan?.name ?? null,
    geometryName: inspector.geometry?.name ?? null,
    boundaryConditionName: inspector.boundaryCondition?.name ?? null,
  };
  const empty = zh ? '未关联' : 'Not associated';
  const models = [
    { label: zh ? '方案模型' : 'Plan model', identity: inspector.plan },
    { label: zh ? '几何模型' : 'Geometry model', identity: inspector.geometry },
    { label: zh ? '边界模型' : 'Boundary model', identity: inspector.boundaryCondition },
  ].filter((item) => item.identity !== null);

  return <section className="results-detail-section results-plan-associations" aria-label={zh ? '结果方案关联' : 'Result Plan associations'}>
    <h4>{zh ? '结果中的方案关联' : 'Plan associations stored in Result'}</h4>
    {loading && <p className="results-detail-status">{zh ? '正在读取结果内嵌方案关联…' : 'Reading associations from the embedded Result Plan…'}</p>}
    {error && <p className="results-detail-status is-error">{zh ? '关联接口状态：' : 'Association endpoint status: '}<code>{error}</code></p>}
    {fallback && !loading && <p className="results-detail-status">{zh ? '关联接口未返回数据；下方为结果元数据接口返回的内嵌模型信息。' : 'The association endpoint returned no snapshot; values below come from result metadata inspection.'}</p>}
    {!values.hasPlan && <p className="results-detail-status">{zh ? '此结果没有内嵌方案。' : 'This Result has no embedded Plan.'}</p>}
    <dl className="results-association-grid">
      <div><dt>{zh ? '方案' : 'Plan'}</dt><dd>{values.planName ?? empty}</dd></div>
      <div><dt>{zh ? '几何' : 'Geometry'}</dt><dd>{values.geometryName ?? empty}</dd></div>
      <div><dt>{zh ? '边界条件' : 'Boundary conditions'}</dt><dd>{values.boundaryConditionName ?? empty}</dd></div>
    </dl>
    {models.length > 0 && <div className="results-associated-models">
      {models.map(({ label, identity }) => identity && <article key={label}>
        <div className="results-associated-model-heading"><strong>{label}</strong><span>{identity.name}</span><small>{identity.embeddedInResult ? (zh ? '内嵌快照' : 'Embedded snapshot') : (zh ? '外部引用' : 'External reference')}</small></div>
        <dl>
          {identity.description && <div><dt>{zh ? '描述' : 'Description'}</dt><dd>{identity.description}</dd></div>}
          {identity.shortID && <div><dt>{zh ? '短 ID' : 'Short ID'}</dt><dd><code>{identity.shortID}</code></dd></div>}
          {identity.filename && <div><dt>{zh ? '文件' : 'File'}</dt><dd title={identity.filename}>{identity.filename}</dd></div>}
          {identity.dateCreated && <div><dt>{zh ? '创建时间' : 'Created'}</dt><dd>{identity.dateCreated}</dd></div>}
          {identity.dateLastModified && <div><dt>{zh ? '最后修改' : 'Last modified'}</dt><dd>{identity.dateLastModified}</dd></div>}
        </dl>
      </article>)}
    </div>}
  </section>;
}
