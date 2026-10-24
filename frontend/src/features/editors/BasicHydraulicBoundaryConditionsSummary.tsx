import type { EditorPageProps } from './model';

/** Snapshot-only hydraulic boundary counts, corresponding to the original TypeCounts summary. */
export function BasicHydraulicBoundaryConditionsSummary(props: EditorPageProps) {
  const boundary = props.snapshot.boundary;
  if (!boundary) return <p className="editor-field-muted">{props.language === 'zh' ? '服务端快照未提供水力边界线统计。' : 'The Host snapshot does not include hydraulic boundary statistics.'}</p>;

  const lines = boundary.lines;
  const payloads = lines.flatMap((line) => line.payloads);
  const hydraulic = payloads.filter((payload) => payload.isHydraulic);
  const activeTypes = lines.flatMap((line) => {
    if (!line.activeHydraulicPayloadId) return [];
    const payload = line.payloads.find((candidate) => candidate.id === line.activeHydraulicPayloadId && candidate.isHydraulic);
    return payload ? [payload.variable || (props.language === 'zh' ? '未提供类型' : 'Type unavailable')] : [];
  });
  const typeCounts = new Map<string, number>();
  for (const type of activeTypes) typeCounts.set(type, (typeCounts.get(type) ?? 0) + 1);

  return <section className="editor-table-card" aria-label={props.language === 'zh' ? '水力边界快照统计' : 'Hydraulic boundary snapshot statistics'}>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '水力边界概览' : 'Hydraulic boundary overview'}</h3>
      <button type="button" className="editor-button" onClick={() => props.onPageChange('BoundaryConditionEditor')}>
        {props.language === 'zh' ? '打开边界线' : 'Open boundary lines'}
      </button>
    </div>
    <div className="editor-status-strip">
      <span>{props.language === 'zh' ? '边界线' : 'Boundary lines'}：<strong>{lines.length}</strong></span>
      <span>{props.language === 'zh' ? '载荷' : 'Payloads'}：<strong>{payloads.length}</strong></span>
      <span>{props.language === 'zh' ? '水力载荷' : 'Hydraulic payloads'}：<strong>{hydraulic.length}</strong></span>
      <span>{props.language === 'zh' ? '活动水力载荷' : 'Active hydraulic payloads'}：<strong>{activeTypes.length}</strong></span>
    </div>
    {typeCounts.size > 0
      ? <ul>{[...typeCounts].sort(([left], [right]) => left.localeCompare(right)).map(([type, count]) => <li key={type}>{type}：{count}</li>)}</ul>
      : <p className="editor-field-muted">{props.language === 'zh' ? '快照中没有活动水力载荷。' : 'No active hydraulic payloads are present in the snapshot.'}</p>}
    <small className="editor-field-muted">{props.language === 'zh' ? `统计来源：服务端快照修订 ${props.snapshot.revision}` : `Counts from Host snapshot revision ${props.snapshot.revision}`}</small>
  </section>;
}
