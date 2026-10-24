import type { EditorPageProps } from './model';

const constantField = 'ConstantManningsNEditor/NValueLayer_ConstantValue';

/** NumericPropertyItem child used when the source Manning mode is Constant. */
export function ConstantManningsNEditor(props: EditorPageProps) {
  const value = props.draft.fields[constantField] ?? '';
  return (
    <section className="editor-field-grid" aria-label={props.language === 'zh' ? '常数糙率属性' : 'Constant Manning properties'}>
      <div className="editor-field">
        <label htmlFor="manning-constant-value">{props.language === 'zh' ? '常数糙率值 n' : "Constant Manning's n"}</label>
        <input
          id="manning-constant-value"
          type="number"
          inputMode="decimal"
          min="0"
          step="any"
          value={value}
          disabled={!props.snapshot.canEdit}
          onChange={(event) => props.onFieldChange(constantField, event.currentTarget.value)}
        />
      </div>
    </section>
  );
}
