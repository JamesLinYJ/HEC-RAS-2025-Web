import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTable } from './geometryTypes';
import { CreateTagControl } from './CreateTagControl';
import { TagPreviewControl, type GeometryRecentTag } from './TagPreviewControl';

export interface SetTagValueControlProps {
  language: Language;
  rows: GeometryRow[];
  table?: GeometryTable;
  sourceRevision: string;
  sourceDraftState: string;
  tagDefinitions: Array<{ id: string; type: string }>;
  recentTags: GeometryRecentTag[];
  canEdit: boolean;
  busy: boolean;
  onTagValueChange?: (rows: GeometryRow[], tagName: string, value: string | null) => Promise<void>;
  onCreateTag?: (tagName: string, tagType: string) => Promise<void>;
}

const requiresTypeParsing = new Set(['Integer', 'Numeric']);

function tagTypeLabel(language: Language, type: string): string {
  if (language !== 'zh') return type;
  return ({ String: '文本', Integer: '整数', Numeric: '数值', Boolean: '布尔值' } as Record<string, string>)[type] ?? type;
}

/** Implements SetTagValueControl around the parent-owned source rows and topology draft commands. */
export function SetTagValueControl({ language, rows, table, sourceRevision, sourceDraftState, tagDefinitions, recentTags, canEdit, busy, onTagValueChange, onCreateTag }: SetTagValueControlProps) {
  const [selectedName, setSelectedName] = useState('');
  const [value, setValue] = useState('');
  const [error, setError] = useState('');
  const selectedDefinition = tagDefinitions.find((definition) => definition.id === selectedName);
  const sourceSelection = rows.length > 0 && rows.every((row) => row.fid != null && row.fid >= 0)
    && new Set(rows.map((row) => row.fid)).size === rows.length;
  const readOnly = !!selectedDefinition && (table?.columns.find((column) => column.id === selectedDefinition.id)?.readOnly ?? false);
  const disabled = !canEdit || busy || !sourceSelection || !onTagValueChange || !selectedDefinition || readOnly;
  const selectionKey = rows.map((row) => row.fid ?? row.id).join(',');

  useEffect(() => { setValue(''); setError(''); }, [selectionKey, sourceRevision, sourceDraftState]);

  const apply = async (tagName: string, tagValue: string): Promise<void> => {
    if (!onTagValueChange || !sourceSelection || !canEdit || busy) return;
    if (tagValue.length === 0) {
      setError(language === 'zh' ? '标签值不能为空；请在标签列表中移除已有标签以重置。' : 'A tag value cannot be empty. Remove the existing tag from the list to reset it.');
      return;
    }
    setError('');
    try {
      await onTagValueChange(rows, tagName, tagValue);
      setSelectedName('');
      setValue('');
    } catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  const type = selectedDefinition?.type ?? '';
  const valueControl = type === 'Boolean'
    ? <select value={value} disabled={disabled} onChange={(event) => setValue(event.target.value)}>
      <option value="">{language === 'zh' ? '选择布尔值' : 'Select a Boolean value'}</option>
      <option value="true">{language === 'zh' ? '是' : 'True'}</option>
      <option value="false">{language === 'zh' ? '否' : 'False'}</option>
    </select>
    : <input type={requiresTypeParsing.has(type) ? 'number' : 'text'} step={type === 'Integer' ? '1' : 'any'} value={value} disabled={disabled}
      onChange={(event) => setValue(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') void apply(selectedName, value); }} />;

  return <div className="geometry-tag-value-control">
    {error && <GeometryError language={language} error={error} style={{ margin: '8px 0' }} />}
    <div className="geometry-arc-tag-create">
      <label className="geometry-form-field">{language === 'zh' ? '标签定义' : 'Tag definition'}
        <select value={selectedName} disabled={!canEdit || busy || !sourceSelection || tagDefinitions.length === 0}
          onChange={(event) => { setSelectedName(event.target.value); setValue(''); }}>
          <option value="">{language === 'zh' ? '选择标签' : 'Select a tag'}</option>
          {tagDefinitions.map((definition) => <option key={definition.id} value={definition.id}>{definition.id} · {tagTypeLabel(language, definition.type)}</option>)}
        </select>
      </label>
      <label className="geometry-form-field">{language === 'zh' ? '标签值' : 'Tag value'}{valueControl}</label>
      <button className="geometry-secondary" type="button" disabled={disabled || value.length === 0} onClick={() => void apply(selectedName, value)}>
        {language === 'zh' ? '赋给所选要素' : 'Assign to selected features'}
      </button>
    </div>
    <TagPreviewControl language={language} itemsSource={recentTags} disabled={!canEdit || busy || !sourceSelection || !onTagValueChange}
      onTagClicked={(tag) => void apply(tag.name, tag.value)} />
    <CreateTagControl language={language} tagDefinitions={tagDefinitions} canCreate={canEdit && sourceSelection}
      busy={busy} onCreateTag={onCreateTag} onCreated={(tagName) => { setSelectedName(tagName); setValue(''); }} />
    {!sourceSelection && <div className="geometry-inline-help">{language === 'zh' ? '请选择一个或多个源要素；本地临时行不会写入拓扑标签。' : 'Select one or more source features. Local rows are not written to topology tags.'}</div>}
  </div>;
}

export default SetTagValueControl;
