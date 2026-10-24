import { useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTable, GeometryTopologySnapshot } from './geometryTypes';
import { SetTagValueControl } from './SetTagValueControl';
import { TagControl, type GeometryTagItem } from './TagControl';
import type { GeometryRecentTag } from './TagPreviewControl';

export interface TagListProps {
  language: Language;
  title: string;
  rows: GeometryRow[];
  table?: GeometryTable;
  snapshot: GeometryTopologySnapshot;
  tagDefinitions: Array<{ id: string; type: string }>;
  recentTags: GeometryRecentTag[];
  canEdit: boolean;
  busy: boolean;
  onTagValueChange?: (rows: GeometryRow[], tagName: string, value: string | null) => Promise<void>;
  onCreateTag?: (tagName: string, tagType: string) => Promise<void>;
}

function hasAssignedValue(type: string, value: string | undefined): value is string {
  if (value == null) return false;
  switch (type) {
    case 'String': return value.length > 0;
    case 'Integer': {
      const parsed = Number(value);
      return Number.isInteger(parsed) && parsed !== 0;
    }
    case 'Numeric': {
      const parsed = Number(value);
      return Number.isFinite(parsed) && parsed !== 0;
    }
    case 'Boolean': return value.toLowerCase() === 'true';
    default: return false;
  }
}

/** C# TagList computes only tag/value pairs common to every selected feature and removes them as a group. */
export function TagList({ language, title, rows, table, snapshot, tagDefinitions, recentTags, canEdit, busy, onTagValueChange, onCreateTag }: TagListProps) {
  const [error, setError] = useState('');
  const sourceSelection = rows.length > 0 && rows.every((row) => row.fid != null && row.fid >= 0)
    && new Set(rows.map((row) => row.fid)).size === rows.length;
  const commonTags = useMemo<GeometryTagItem[]>(() => {
    if (!sourceSelection) return [];
    return tagDefinitions.flatMap((definition) => {
      const values = rows.map((row) => row.values[definition.id]);
      const first = values[0];
      if (!hasAssignedValue(definition.type, first) || values.some((value) => value !== first || !hasAssignedValue(definition.type, value))) return [];
      return [{ name: definition.id, type: definition.type, value: first }];
    });
  }, [rows, sourceSelection, tagDefinitions]);

  const removeTag = async (tag: GeometryTagItem): Promise<void> => {
    if (!onTagValueChange || !sourceSelection || !canEdit || busy) return;
    setError('');
    try { await onTagValueChange(rows, tag.name, null); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  return <section className="geometry-panel" aria-label={title}>
    <header className="geometry-panel-heading"><div>
      <h3>{title}</h3>
      <p>{sourceSelection ? `${rows.length} ${language === 'zh' ? '个源要素' : 'source features'}`
        : (language === 'zh' ? '选择一个或多个源要素后管理标签。' : 'Select one or more source features to manage tags.')}</p>
    </div></header>
    {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
    {sourceSelection && <>
      {commonTags.length ? <div className="geometry-tag-list" aria-label={language === 'zh' ? '所有所选要素共有的标签' : 'Tags common to all selected features'}>
        {commonTags.map((tag) => <TagControl key={`${tag.name}\u0000${tag.type}`} language={language} tag={tag}
          disabled={!canEdit || busy || !onTagValueChange || !!table?.columns.find((column) => column.id === tag.name)?.readOnly}
          onDeleteRequested={(tag) => void removeTag(tag)} />)}
      </div> : <div className="geometry-empty"><strong>{language === 'zh' ? '所选要素没有共同的已赋值标签。' : 'The selected features have no common assigned tags.'}</strong></div>}
      <SetTagValueControl language={language} rows={rows} table={table} sourceRevision={snapshot.revision} sourceDraftState={snapshot.draftState}
        tagDefinitions={tagDefinitions} recentTags={recentTags}
        canEdit={canEdit} busy={busy} onTagValueChange={onTagValueChange} onCreateTag={onCreateTag} />
    </>}
    {!sourceSelection && <div className="geometry-empty"><strong>{language === 'zh' ? '标签只对有效源要素启用。' : 'Tags are enabled only for valid source features.'}</strong></div>}
  </section>;
}

export default TagList;
