import { useEffect, useMemo, useState, type FormEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTopologySnapshot } from './geometryTypes';

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

const tagTypes = ['String', 'Integer', 'Numeric', 'Boolean'] as const;

function typeLabel(language: Language, type: string): string {
  if (language !== 'zh') return type;
  const names: Record<string, string> = { String: '文本', Integer: '整数', Numeric: '数值', Boolean: '布尔值' };
  return names[type] ?? type;
}

function sourceRows(rows: GeometryRow[]): GeometryRow[] {
  return rows.filter((row) => row.fid != null && row.fid >= 0);
}

export function ArcEditor({ language, rows, snapshot, canEdit, busy, onTagValueChange, onCreateTag, onEditPoints }: ArcEditorProps) {
  const [tagDrafts, setTagDrafts] = useState<Record<string, string>>({});
  const [newTagName, setNewTagName] = useState('');
  const [newTagType, setNewTagType] = useState<(typeof tagTypes)[number]>('String');
  const [error, setError] = useState('');
  const selected = sourceRows(rows);
  const selectionKey = selected.map((row) => row.fid).join(',');
  const definitions = snapshot?.arcTags ?? [];
  const recentTags = snapshot?.recentArcTags ?? [];
  const pointEditArc = rows.length === 1 && selected.length === 1 ? selected[0] : undefined;

  useEffect(() => { setTagDrafts({}); setError(''); }, [selectionKey, snapshot?.draftState, snapshot?.revision]);

  const selectedValues = useMemo(() => new Map(definitions.map((definition) => {
    const values = selected.map((row) => row.values[definition.id] ?? '');
    return [definition.id, { values, mixed: new Set(values).size > 1, common: values[0] ?? '' }] as const;
  })), [definitions, selected]);

  const applyTag = async (tagName: string, value: string | null): Promise<void> => {
    if (!onTagValueChange || !selected.length || selected.length !== rows.length || !canEdit || busy) return;
    setError('');
    try { await onTagValueChange(selected, tagName, value); setTagDrafts((current) => { const next = { ...current }; delete next[tagName]; return next; }); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  const createTag = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (!onCreateTag || !canEdit || busy || !newTagName.trim()) return;
    setError('');
    try { await onCreateTag(newTagName.trim(), newTagType); setNewTagName(''); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  return (
    <section className="geometry-panel" aria-label={language === 'zh' ? '弧段属性和标签' : 'Arc properties and tags'}>
      <header className="geometry-panel-heading"><div>
        <h3>{language === 'zh' ? '弧段标签' : 'Arc tags'}</h3>
        <p>{selected.length === rows.length && selected.length
          ? `${selected.length} ${language === 'zh' ? '个源弧段' : 'source arcs'} · ${language === 'zh' ? '拓扑修订' : 'topology revision'} ${snapshot?.revision ?? '—'}`
          : (language === 'zh' ? '选择源弧段后管理标签。' : 'Select source arcs to manage their tags.')}</p>
      </div>
        {pointEditArc && onEditPoints && <button className="geometry-secondary" type="button" disabled={busy}
          onClick={() => onEditPoints(pointEditArc.fid!)}>{language === 'zh' ? '编辑弧段点坐标…' : 'Edit arc points…'}</button>}
      </header>
      {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
      {!snapshot ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前没有源拓扑标签快照。' : 'There is no source topology tag snapshot.'}</strong></div> : selected.length === 0 ? (
        <div className="geometry-empty"><strong>{language === 'zh' ? '选择一个或多个弧段以显示 TagList。' : 'Select one or more arcs to display the tag list.'}</strong></div>
      ) : selected.length !== rows.length ? (
        <div className="geometry-empty"><strong>{language === 'zh' ? '所选集合含本地临时行；标签写入仅针对全部源弧段。' : 'The selection contains local temporary rows; tag writes require source arcs only.'}</strong></div>
      ) : (
        <>
          {!definitions.length ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前拓扑尚无弧段标签定义。' : 'The topology has no arc-tag definitions.'}</strong></div> : (
            <div className="geometry-arc-tags">
              {definitions.map((definition) => {
                const state = selectedValues.get(definition.id);
                const mixed = state?.mixed ?? false;
                const value = tagDrafts[definition.id] ?? (mixed ? '' : state?.common ?? '');
                const readOnly = snapshot.arcs.columns.find((column) => column.id === definition.id)?.readOnly ?? false;
                const disabled = !canEdit || busy || !onTagValueChange || readOnly;
                const type = definition.type;
                return <div className="geometry-arc-tag" key={definition.id}>
                  <div className="geometry-arc-tag-heading"><strong>{definition.id}</strong><span>{typeLabel(language, type)}</span>{readOnly && <span>{language === 'zh' ? '只读' : 'Read only'}</span>}{mixed && <span className="geometry-tag-mixed">{language === 'zh' ? '多值' : 'Mixed'}</span>}</div>
                  {type === 'Boolean' ? <select value={value === 'true' ? 'true' : value === 'false' ? 'false' : ''} disabled={disabled}
                    onChange={(event) => void applyTag(definition.id, event.target.value)}>
                    <option value="">—</option><option value="true">{language === 'zh' ? '是' : 'True'}</option><option value="false">{language === 'zh' ? '否' : 'False'}</option>
                  </select> : <input type={type === 'Integer' || type === 'Numeric' ? 'number' : 'text'} step={type === 'Integer' ? '1' : 'any'} value={value}
                    placeholder={mixed ? (language === 'zh' ? '所选值不同' : 'Mixed values') : ''} disabled={disabled}
                    onChange={(event) => setTagDrafts((current) => ({ ...current, [definition.id]: event.target.value }))}
                    onBlur={() => { if (Object.hasOwn(tagDrafts, definition.id)) void applyTag(definition.id, value); }}
                    onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur(); }} />}
                  <div className="geometry-arc-tag-actions">
                    <button type="button" className="geometry-detail-clear" disabled={disabled} onClick={() => void applyTag(definition.id, null)}>{language === 'zh' ? '清除到默认值' : 'Reset to default'}</button>
                    {recentTags.filter((tag) => tag.name === definition.id).slice(0, 3).map((tag, index) => <button type="button" className="geometry-secondary" key={`${tag.name}-${tag.value}-${index}`}
                      disabled={disabled} onClick={() => void applyTag(definition.id, tag.value)}>{language === 'zh' ? '最近' : 'Recent'}: {tag.value}</button>)}
                  </div>
                </div>;
              })}
            </div>
          )}
          <form className="geometry-arc-tag-create" onSubmit={(event) => void createTag(event)}>
            <label className="geometry-form-field">{language === 'zh' ? '新建弧段标签' : 'Create arc tag'}
              <input value={newTagName} onChange={(event) => setNewTagName(event.target.value)} disabled={!canEdit || busy || !onCreateTag} />
            </label>
            <label className="geometry-form-field">{language === 'zh' ? '类型' : 'Type'}
              <select value={newTagType} onChange={(event) => setNewTagType(event.target.value as (typeof tagTypes)[number])} disabled={!canEdit || busy || !onCreateTag}>
                {tagTypes.map((type) => <option key={type} value={type}>{typeLabel(language, type)}</option>)}
              </select>
            </label>
            <button className="geometry-secondary" type="submit" disabled={!canEdit || busy || !onCreateTag || !newTagName.trim()}>{language === 'zh' ? '创建标签' : 'Create tag'}</button>
          </form>
        </>
      )}
      <div className="geometry-inline-help">{language === 'zh'
        ? '标签定义、当前值与最近值来自拓扑快照；创建、赋值和重置使用现有拓扑草稿命令，可撤销并随父级保存。字段元数据由上方弧段属性表承载。'
        : 'Definitions, values, and recent values come from the topology snapshot. Create, assign, and reset use the existing undoable topology draft commands. Field metadata stays in the arc property table.'}</div>
    </section>
  );
}

export default ArcEditor;
