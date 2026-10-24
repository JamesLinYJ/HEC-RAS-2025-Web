import type { EditorPageProps } from './model';
import { editorValueLabel } from './editorLocalization';
import { PolygonClassificationInputProperties } from './PolygonClassificationInputProperties';
import { RasterClassificationInputProperties } from './RasterClassificationInputProperties';

const selectedClassificationField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';

/** React port of Ras.Visual.WinUI.Controls.ClassificationLayersEditor. */
export function ClassificationLayersEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const inputs = snapshot.classification?.inputs ?? [];
  const selectedID = draft.fields[selectedClassificationField] ?? snapshot.classification?.selectedId ?? '';
  const selected = inputs.find((input) => input.id === selectedID) ?? inputs[0];
  if (!selected) return <div className="editor-empty-state">{language === 'zh' ? '该图层没有分类输入。' : 'This layer has no classification inputs.'}</div>;
  const inputNameKey = `${selected.scope}/Name`;
  const displayName = draft.fields[inputNameKey] ?? selected.name;
  const isRaster = Boolean(selected.tableId);
  const isPolygon = !selected.canRemove;
  const constantValueKey = `${selected.scope}/ConstantClassification/Value`;
  const isConstant = !isRaster && !isPolygon && (draft.fields[constantValueKey] !== undefined || selected.extent != null || /constant/i.test(selected.type));
  return (
    <section className="editor-schema-page editor-classification-inputs">
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{language === 'zh' ? '分类输入' : 'Classification inputs'}</p><h2>{language === 'zh' ? '图层与映射' : 'Layers and mappings'}</h2></div>
        <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source control'}</summary><code>ClassificationLayersEditor</code></details>
      </div>
      <div className="editor-classification-layout">
        <nav className="editor-line-list" aria-label={language === 'zh' ? '分类输入' : 'Classification inputs'}>
          {inputs.map((input) => <button type="button" key={input.id} className={input.id === selected.id ? 'selected' : ''} onClick={() => props.onFieldChange(selectedClassificationField, input.id)}>{draft.fields[`${input.scope}/Name`] ?? input.name}<small>{editorValueLabel(language, input.type)}</small></button>)}
        </nav>
        <div className="editor-classification-content">
          <article className="editor-terrain-card">
            <h3>{displayName}</h3>
            <dl className="editor-readonly-list">
              <dt>{language === 'zh' ? '类型' : 'Type'}</dt><dd>{editorValueLabel(language, selected.type)}</dd>
              <dt>{language === 'zh' ? '来源' : 'Source'}</dt><dd>{selected.filename ?? selected.extent ?? '—'}</dd>
            </dl>
            <div className="editor-field-grid">
              <div className="editor-field"><label htmlFor="classification-input-name">{language === 'zh' ? '输入名称' : 'Input name'}</label><input id="classification-input-name" type="text" value={draft.fields[inputNameKey] ?? selected.name} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(inputNameKey, event.currentTarget.value)} /></div>
              {isConstant && <div className="editor-field"><label htmlFor="classification-constant-value">{language === 'zh' ? '分类值' : 'Classification value'}</label><input id="classification-constant-value" type="text" value={draft.fields[constantValueKey] ?? ''} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(constantValueKey, event.currentTarget.value)} /></div>}
            </div>
            {isRaster && <RasterClassificationInputProperties props={props} input={selected} />}
            {isPolygon && <PolygonClassificationInputProperties props={props} input={selected} />}
            {!isRaster && !isPolygon && !isConstant && <p className="editor-field-muted">{language === 'zh' ? '该分类输入类型没有对应的可编辑字段。' : 'This classification input type has no corresponding editable fields.'}</p>}
            {snapshot.classification?.canUndoGeometry || snapshot.classification?.canRedoGeometry ? <p className="editor-field-muted">{language === 'zh' ? '检测到几何编辑历史；当前页面保留并提交分类属性，几何命令仍需对应后端操作。' : 'Geometry edit history is present. This page preserves classification properties; geometry commands still require their backend operation.'}</p> : null}
          </article>
        </div>
      </div>
    </section>
  );
}
