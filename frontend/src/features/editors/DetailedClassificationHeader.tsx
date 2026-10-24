import type { EditorPageProps } from './model';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Source-bound layer name shown by DetailedClassificationHeader in the input pane. */
export function DetailedClassificationHeader({ props, selected }: { props: EditorPageProps; selected?: ClassificationInput }) {
  const { language, asset, draft, snapshot } = props;
  const layerName = draft.fields['ClassificationLayerSummary/ClassificationLayer_Name']
    ?? snapshot.fields['ClassificationLayerSummary/ClassificationLayer_Name']
    ?? asset.name;
  const inputName = selected ? draft.fields[`${selected.scope}/Name`] ?? selected.name : '';
  return (
    <header className="editor-schema-heading editor-detailed-classification-header">
      <div>
        <p className="editor-eyebrow">{language === 'zh' ? '分类图层' : 'Classification layer'}</p>
        <h2>{layerName}</h2>
        <p className="editor-field-muted">{asset.path}</p>
        {selected && <p className="editor-field-muted">{language === 'zh' ? '当前输入' : 'Selected input'}：{inputName} · {selected.type}</p>}
      </div>
    </header>
  );
}
