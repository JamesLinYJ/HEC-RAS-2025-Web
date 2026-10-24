import type { EditorSchema } from './model';
import { ConfigSchemaPreview } from './SchemaFieldRenderer';
import { editorText } from './editorLocalization';

export function EditorCatalogBrowser({ schemas, selectedSchemaID, search, language, onSelect, onSearch }: {
  schemas: EditorSchema[];
  selectedSchemaID: string;
  search: string;
  language: 'zh' | 'en';
  onSelect: (id: string) => void;
  onSearch: (value: string) => void;
}) {
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const filtered = schemas.filter((schema) => !normalizedSearch || `${schema.id} ${schema.title} ${editorText(language, schema.title)} ${schema.source} ${schema.fields.map((field) => `${field.id} ${field.title} ${editorText(language, field.title)}`).join(' ')}`.toLocaleLowerCase().includes(normalizedSearch));
  const selected = filtered.find((schema) => schema.id === selectedSchemaID) ?? filtered[0];
  return (
    <div className="editor-catalog-view">
      <aside className="editor-catalog-sidebar">
        <label className="editor-search-box"><span>{language === 'zh' ? '搜索原始控件' : 'Search source controls'}</span><input type="search" value={search} onChange={(event) => onSearch(event.currentTarget.value)} placeholder={language === 'zh' ? '标题、控件 ID、字段…' : 'Title, control ID, field…'} /></label>
        <div className="editor-catalog-count">{filtered.length} / {schemas.length} {language === 'zh' ? '个控件' : 'controls'}</div>
        <nav className="editor-catalog-list" aria-label={language === 'zh' ? '原始编辑器控件' : 'Source editor controls'}>
          {filtered.map((schema) => <button type="button" key={schema.id} className={schema.id === selected?.id ? 'selected' : ''} onClick={() => onSelect(schema.id)}>
            <span>{editorText(language, schema.title)}</span><small>{schema.id} · {schema.fields.length} {language === 'zh' ? '个字段' : 'fields'}</small>
          </button>)}
          {filtered.length === 0 && <p className="editor-field-muted">{language === 'zh' ? '没有匹配的控件。' : 'No controls match the search.'}</p>}
        </nav>
      </aside>
      <div className="editor-catalog-preview">
        {selected ? <ConfigSchemaPreview schema={selected} language={language} /> : <div className="editor-empty-state">{language === 'zh' ? '选择一个原始控件。' : 'Select a source control.'}</div>}
      </div>
    </div>
  );
}
