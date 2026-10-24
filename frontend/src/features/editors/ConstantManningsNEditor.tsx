import type { EditorPageProps } from './model';
import { isValidManningConstant } from './manningEditorUi';

const constantField = 'ConstantManningsNEditor/NValueLayer_ConstantValue';

interface ConstantManningsNEditorProps extends EditorPageProps {
  operationBusy?: boolean;
}

/** Numeric-property child used for the source's Constant Manning mode. */
export function ConstantManningsNEditor(props: ConstantManningsNEditorProps) {
  const value = props.draft.fields[constantField] ?? '';
  const valid = isValidManningConstant(value);
  const zh = props.language === 'zh';
  return (
    <section className="editor-field-grid" aria-label={zh ? '常数糙率属性' : 'Constant Manning properties'} data-testid="manning-constant-editor">
      <div className="editor-field">
        <label htmlFor="manning-constant-value">{zh ? '常数糙率值 n' : "Constant Manning's n"}</label>
        <input
          id="manning-constant-value"
          data-testid="manning-constant-value"
          type="number"
          inputMode="decimal"
          min="0"
          step="any"
          value={value}
          disabled={!props.snapshot.canEdit || props.operationBusy === true}
          aria-invalid={!valid || undefined}
          aria-describedby={!valid ? 'manning-constant-validation' : undefined}
          onChange={(event) => props.onFieldChange(constantField, event.currentTarget.value)}
        />
        {!valid && <small id="manning-constant-validation" className="editor-config-notice" role="alert" data-testid="manning-constant-validation">
          {zh ? '请输入不小于 0 的有限数值。该值会保留在草稿中，修正后才能保存。' : 'Enter a finite number greater than or equal to 0. The current draft value is preserved until corrected.'}
        </small>}
      </div>
      <p className="editor-field-muted">{zh ? '值仅写入当前糙率草稿；点击工作台“保存”后才写入模型文件。' : 'This value stays in the Manning draft until Save writes the model file.'}</p>
    </section>
  );
}
