import type { Language } from '../../shared/workbench';
import { DetailedControlPointHeader } from './DetailedControlPointHeader';
import { TagList } from './TagList';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';

export interface ControlPointEditorProps {
  language: Language;
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onTagValueChange?: (rows: GeometryRow[], tagName: string, value: string | null) => Promise<void>;
  onCreateTag?: (tagName: string, tagType: string) => Promise<void>;
}

/** Control-point editor reuses the same source TagList semantics as ArcEditor. */
export function ControlPointEditor({ language, rows, snapshot, canEdit, busy, onTagValueChange, onCreateTag }: ControlPointEditorProps) {
  return <div className="geometry-grid">
    <DetailedControlPointHeader language={language} rows={rows} snapshot={snapshot} />
    {snapshot ? <TagList language={language} title={language === 'zh' ? '控制点标签（TagList）' : 'Control point tags (TagList)'} rows={rows}
      table={snapshot.regions} snapshot={snapshot} tagDefinitions={snapshot.regionTags ?? []} recentTags={snapshot.recentRegionTags ?? []}
      canEdit={canEdit} busy={busy} onTagValueChange={onTagValueChange} onCreateTag={onCreateTag} />
      : <div className="geometry-empty"><strong>{language === 'zh' ? '当前没有源拓扑标签快照。' : 'There is no source topology tag snapshot.'}</strong></div>}
  </div>;
}

export default ControlPointEditor;
