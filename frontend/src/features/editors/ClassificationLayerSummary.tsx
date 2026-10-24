import { Fragment } from 'react';
import type { EditorPageProps } from './model';
import { schemaForPage } from './model';
import { editorAssetKind, editorText } from './editorLocalization';

/** React port of Ras.Visual.WinUI.Controls.ClassificationLayerSummary. */
export function ClassificationLayerSummary(props: EditorPageProps) {
  const { language, asset, snapshot, draft, schemas } = props;
  const schema = schemaForPage(schemas, 'ClassificationLayerSummary');
  if (!schema) return <div className="editor-empty-state">{language === 'zh' ? '当前模型没有提供分类图层摘要字段。' : 'The current model did not provide classification layer summary fields.'}</div>;
  const groups = schema.groups?.length ? schema.groups : [{ title: '', fields: schema.fields.map((field) => field.id) }];

  return (
    <section className="editor-schema-page editor-classification-layer-summary" aria-label={editorText(language, schema.title)}>
      <div className="editor-schema-heading">
        <div>
          <p className="editor-eyebrow">{editorAssetKind(language, asset.kind)}</p>
          <h2>{editorText(language, schema.title)}</h2>
        </div>
        <p className="editor-field-muted">{asset.path}</p>
      </div>
      {groups.map((group) => (
        <section className="editor-terrain-card" key={group.title} aria-label={editorText(language, group.title)}>
          <h3>{editorText(language, group.title)}</h3>
          <dl className="editor-readonly-list">
            {group.fields.map((id) => {
              const field = schema.fields.find((candidate) => candidate.id === id);
              if (!field) return null;
              const fieldID = `${field.scope ?? schema.id}/${field.id}`;
              const value = draft.fields[fieldID] ?? snapshot.fields[fieldID] ?? '';
              return <Fragment key={field.id}><dt>{editorText(language, field.title)}</dt><dd>{value || '—'}</dd></Fragment>;
            })}
          </dl>
        </section>
      ))}
    </section>
  );
}
