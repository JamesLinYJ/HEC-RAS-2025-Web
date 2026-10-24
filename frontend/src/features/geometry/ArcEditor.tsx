import type { Language } from '../../shared/workbench';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';
import { TagList } from './TagList';

export interface ArcEditorProps {
  language: Language;
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onTagValueChange?: (rows: GeometryRow[], tagName: string, value: string | null) => Promise<void>;
  onCreateTag?: (tagName: string, tagType: string) => Promise<void>;
  onEditPoints?: (arcFID: number) => void;
}

/** ArcEditor hosts the source TagList and the retained single-arc point edit entry point. */
export function ArcEditor({ language, rows, snapshot, canEdit, busy, onTagValueChange, onCreateTag, onEditPoints }: ArcEditorProps) {
  const sourceRows = rows.filter((row) => row.fid != null && row.fid >= 0);
  const pointEditArc = rows.length === 1 && sourceRows.length === 1 ? sourceRows[0] : undefined;
  return <div className="geometry-grid">
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '弧段属性' : 'Arc properties'}</h3>
      <p>{sourceRows.length ? `${sourceRows.length} ${language === 'zh' ? '个源弧段' : 'source arcs'} · ${language === 'zh' ? '修订' : 'revision'} ${snapshot?.revision ?? '—'}`
        : (language === 'zh' ? '选择源弧段以查看标签。' : 'Select source arcs to inspect tags.')}</p>
    </div>
      {pointEditArc && onEditPoints && <button className="geometry-secondary" type="button" disabled={busy}
        onClick={() => onEditPoints(pointEditArc.fid!)}>{language === 'zh' ? '编辑弧段点坐标…' : 'Edit arc points…'}</button>}
    </header>
    {snapshot ? <TagList language={language} title={language === 'zh' ? '弧段标签（TagList）' : 'Arc tags (TagList)'} rows={rows}
      table={snapshot.arcs} snapshot={snapshot} tagDefinitions={snapshot.arcTags ?? []} recentTags={snapshot.recentArcTags ?? []}
      canEdit={canEdit} busy={busy} onTagValueChange={onTagValueChange} onCreateTag={onCreateTag} />
      : <div className="geometry-empty"><strong>{language === 'zh' ? '当前没有源拓扑标签快照。' : 'There is no source topology tag snapshot.'}</strong></div>}
  </div>;
}

export default ArcEditor;
