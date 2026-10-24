import type { EditorPageProps } from './model';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Name and live source counts shown by DetailedClassificationHeader in the layer pane. */
export function DetailedClassificationHeader({ props, selected }: { props: EditorPageProps; selected?: ClassificationInput }) {
  const { language, asset, draft, snapshot } = props;
  const classification = draft.classificationView ?? snapshot.classification;
  const inputName = selected
    ? draft.fields[`${selected.scope}/Name`] ?? selected.name
    : asset.name;
  return (
    <header className="editor-schema-heading editor-detailed-classification-header">
      <div>
        <p className="editor-eyebrow">{language === 'zh' ? '分类图层' : 'Classification layer'}</p>
        <h2>{inputName}</h2>
        <p className="editor-field-muted">{asset.path}</p>
      </div>
      <dl className="editor-readonly-list">
        <dt>{language === 'zh' ? '输入数' : 'Inputs'}</dt><dd>{classification?.inputs.length ?? 0}</dd>
        <dt>{language === 'zh' ? '分类数' : 'Classes'}</dt><dd>{classification?.classes.length ?? 0}</dd>
        {selected && <><dt>{language === 'zh' ? '输入类型' : 'Input type'}</dt><dd>{selected.type}</dd></>}
      </dl>
    </header>
  );
}
