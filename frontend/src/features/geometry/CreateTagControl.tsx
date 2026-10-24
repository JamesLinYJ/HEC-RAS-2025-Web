import { useState, type FormEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';

const sourceTagTypes = ['String', 'Integer', 'Numeric', 'Boolean'] as const;

export interface CreateTagControlProps {
  language: Language;
  tagDefinitions: Array<{ id: string; type: string }>;
  canCreate: boolean;
  busy: boolean;
  onCreateTag?: (tagName: string, tagType: string) => Promise<void>;
  onCreated?: (tagName: string) => void;
}

/** Mirrors CreateTagControl's name/type request; the caller owns the topology edit and draft. */
export function CreateTagControl({ language, tagDefinitions, canCreate, busy, onCreateTag, onCreated }: CreateTagControlProps) {
  const [name, setName] = useState('');
  const [type, setType] = useState<(typeof sourceTagTypes)[number]>('String');
  const [error, setError] = useState('');
  const duplicate = tagDefinitions.some((definition) => definition.id.toUpperCase() === name.trim().toUpperCase());
  const submit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    const tagName = name.trim();
    if (!tagName || duplicate || !onCreateTag || !canCreate || busy) return;
    setError('');
    try {
      await onCreateTag(tagName, type);
      onCreated?.(tagName);
      setName('');
    } catch (reason) { setError(geometryDiagnosticMessage(reason)); }
  };

  return <div>
    {error && <GeometryError language={language} error={error} style={{ margin: '8px 0' }} />}
    <form className="geometry-arc-tag-create" onSubmit={(event) => void submit(event)}>
    <label className="geometry-form-field">{language === 'zh' ? '新建标签' : 'Create tag'}
      <input value={name} onChange={(event) => setName(event.target.value)} disabled={!canCreate || busy || !onCreateTag} />
    </label>
    <label className="geometry-form-field">{language === 'zh' ? '类型' : 'Type'}
      <select value={type} onChange={(event) => setType(event.target.value as (typeof sourceTagTypes)[number])} disabled={!canCreate || busy || !onCreateTag}>
        {sourceTagTypes.map((tagType) => <option key={tagType} value={tagType}>{language === 'zh'
          ? ({ String: '文本', Integer: '整数', Numeric: '数值', Boolean: '布尔值' } as const)[tagType]
          : tagType}</option>)}
      </select>
    </label>
    <button className="geometry-secondary" type="submit" disabled={!canCreate || busy || !onCreateTag || !name.trim() || duplicate}>
      {language === 'zh' ? '创建标签' : 'Create tag'}
    </button>
    {duplicate && <span className="geometry-inline-help">{language === 'zh' ? '该标签定义已存在。' : 'This tag definition already exists.'}</span>}
    </form>
  </div>;
}

export default CreateTagControl;
