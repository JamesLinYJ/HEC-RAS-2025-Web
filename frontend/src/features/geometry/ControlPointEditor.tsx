import { useEffect, useMemo, useState, type FormEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { DetailedControlPointHeader } from './DetailedControlPointHeader';
import { geometryDiagnosticMessage } from './geometryErrors';
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

const tagTypes = ['String', 'Integer', 'Numeric', 'Boolean'] as const;

function labelType(language: Language, type: string): string {
  if (language !== 'zh') return type;
  return ({ String: '文本', Integer: '整数', Numeric: '数值', Boolean: '布尔值' } as Record<string, string>)[type] ?? type;
}

export function ControlPointEditor({ language, rows, snapshot, canEdit, busy, onTagValueChange, onCreateTag }: ControlPointEditorProps) {
  const [tagDrafts, setTagDrafts] = useState<Record<string, string>>({});
  const [newName, setNewName] = useState('');
  const [newType, setNewType] = useState<(typeof tagTypes)[number]>('String');
  const [error, setError] = useState('');
  const sourceRows = rows.filter((row) => row.fid != null && row.fid >= 0);
  const selectionKey = sourceRows.map((row) => row.fid).join(',');
  const definitions = snapshot?.regionTags ?? [];
  const recentTags = snapshot?.recentRegionTags ?? [];

  useEffect(() => { setTagDrafts({}); setError(''); }, [selectionKey, snapshot?.draftState, snapshot?.revision]);

  const values = useMemo(() => new Map(definitions.map((definition) => {
    const selectedValues = sourceRows.map((row) => row.values[definition.id] ?? '');
    return [definition.id, { value: selectedValues[0] ?? '', mixed: new Set(selectedValues).size > 1 }] as const;
  })), [definitions, sourceRows]);

  const changeTag = async (tagName: string, value: string | null): Promise<void> => {
    if (!onTagValueChange || sourceRows.length === 0 || sourceRows.length !== rows.length || !canEdit || busy) return;
    setError('');
    try {
      await onTagValueChange(sourceRows, tagName, value);
      setTagDrafts((current) => { const next = { ...current }; delete next[tagName]; return next; });
    } catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  const createTag = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (!onCreateTag || !newName.trim() || !canEdit || busy) return;
    setError('');
    try { await onCreateTag(newName.trim(), newType); setNewName(''); }
    catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  const title = language === 'zh' ? '控制点标签（TagList）' : 'Control point tags (TagList)';
  const sourceSelection = sourceRows.length > 0 && sourceRows.length === rows.length;
  return <div className="geometry-grid">
    <DetailedControlPointHeader language={language} rows={rows} snapshot={snapshot} />
    <section className="geometry-panel" aria-label={title}>
      <header className="geometry-panel-heading"><div><h3>{title}</h3><p>{sourceSelection ? `${sourceRows.length} ${language === 'zh' ? '个源控制点' : 'source control points'} · ${language === 'zh' ? '拓扑修订' : 'Topology revision'} ${snapshot?.revision ?? '—'}` : (language === 'zh' ? '选择源区域控制点以查看或修改标签。' : 'Select source region control points to inspect or edit tags.')}</p></div></header>
      {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
      {!snapshot ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前没有源拓扑标签快照。' : 'There is no source topology tag snapshot.'}</strong></div>
        : !sourceSelection ? <div className="geometry-empty"><strong>{rows.length ? (language === 'zh' ? '选集中含本地临时行；标签写入只接受全部源控制点。' : 'The selection contains local rows; tag writes require source control points only.') : (language === 'zh' ? '选择一个或多个区域控制点以显示 TagList。' : 'Select one or more region control points to display TagList.')}</strong></div>
          : <>
            {!definitions.length ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前拓扑尚无控制点标签定义。' : 'The topology has no control-point tag definitions.'}</strong></div> : <div className="geometry-arc-tags">
              {definitions.map((definition) => {
                const current = values.get(definition.id);
                const mixed = current?.mixed ?? false;
                const value = tagDrafts[definition.id] ?? (mixed ? '' : current?.value ?? '');
                const readOnly = snapshot.regions.columns.find((column) => column.id === definition.id)?.readOnly ?? false;
                const disabled = !canEdit || busy || !onTagValueChange || readOnly;
                return <div className="geometry-arc-tag" key={definition.id}>
                  <div className="geometry-arc-tag-heading"><strong>{definition.id}</strong><span>{labelType(language, definition.type)}</span>{readOnly && <span>{language === 'zh' ? '只读' : 'Read only'}</span>}{mixed && <span className="geometry-tag-mixed">{language === 'zh' ? '多值' : 'Mixed'}</span>}</div>
                  {definition.type === 'Boolean' ? <select value={value === 'true' ? 'true' : value === 'false' ? 'false' : ''} disabled={disabled} onChange={(event) => void changeTag(definition.id, event.target.value)}>
                    <option value="">—</option><option value="true">{language === 'zh' ? '是' : 'True'}</option><option value="false">{language === 'zh' ? '否' : 'False'}</option>
                  </select> : <input type={definition.type === 'Integer' || definition.type === 'Numeric' ? 'number' : 'text'} step={definition.type === 'Integer' ? '1' : 'any'} value={value}
                    placeholder={mixed ? (language === 'zh' ? '所选值不同' : 'Mixed values') : ''} disabled={disabled}
                    onChange={(event) => setTagDrafts((current) => ({ ...current, [definition.id]: event.target.value }))}
                    onBlur={() => { if (Object.hasOwn(tagDrafts, definition.id)) void changeTag(definition.id, value); }}
                    onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur(); }} />}
                  <div className="geometry-arc-tag-actions"><button type="button" className="geometry-detail-clear" disabled={disabled} onClick={() => void changeTag(definition.id, null)}>{language === 'zh' ? '清除到默认值' : 'Reset to default'}</button>
                    {recentTags.filter((tag) => tag.name === definition.id).slice(0, 3).map((tag, index) => <button type="button" className="geometry-secondary" key={`${tag.name}-${tag.value}-${index}`} disabled={disabled} onClick={() => void changeTag(definition.id, tag.value)}>{language === 'zh' ? '最近' : 'Recent'}: {tag.value}</button>)}
                  </div>
                </div>;
              })}
            </div>}
            <form className="geometry-arc-tag-create" onSubmit={(event) => void createTag(event)}>
              <label className="geometry-form-field">{language === 'zh' ? '新建控制点标签' : 'Create control-point tag'}<input value={newName} onChange={(event) => setNewName(event.target.value)} disabled={!canEdit || busy || !onCreateTag} /></label>
              <label className="geometry-form-field">{language === 'zh' ? '类型' : 'Type'}<select value={newType} onChange={(event) => setNewType(event.target.value as (typeof tagTypes)[number])} disabled={!canEdit || busy || !onCreateTag}>{tagTypes.map((type) => <option key={type} value={type}>{labelType(language, type)}</option>)}</select></label>
              <button className="geometry-secondary" type="submit" disabled={!canEdit || busy || !onCreateTag || !newName.trim()}>{language === 'zh' ? '创建标签' : 'Create tag'}</button>
            </form>
          </>}
      <div className="geometry-inline-help">{language === 'zh' ? '标签定义、值和最近值来自拓扑快照；创建、赋值和重置调用已有拓扑草稿命令，可撤销并随父级保存。' : 'Definitions, values, and recent values come from the topology snapshot. Create, assign, and reset use the existing undoable topology draft commands and parent save.'}</div>
    </section>
  </div>;
}

export default ControlPointEditor;
