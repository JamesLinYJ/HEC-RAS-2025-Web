import type { EditorPageProps } from './model';
import { editorValueLabel } from './editorLocalization';
import { ClassificationGeometryEditor } from './ClassificationGeometryEditor';
import { ClassificationRasterEditor } from './ClassificationRasterEditor';
import { DetailedClassificationHeader } from './DetailedClassificationHeader';
import { PolygonClassificationInputProperties } from './PolygonClassificationInputProperties';

const selectedClassificationField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';

/** React port of Ras.Visual.WinUI.Controls.ClassificationLayersEditor. */
export function ClassificationLayersEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const classification = draft.classificationView ?? snapshot.classification;
  const inputs = classification?.inputs ?? [];
  const selectedID = draft.fields[selectedClassificationField] ?? classification?.selectedId ?? '';
  const selected = inputs.find((input) => input.id === selectedID) ?? inputs[0];
  const selectedIndex = selected ? inputs.findIndex((input) => input.id === selected.id) : -1;
  if (!selected) return <div className="editor-empty-state">{language === 'zh' ? '该图层没有分类输入。' : 'This layer has no classification inputs.'}</div>;
  const inputNameKey = `${selected.scope}/Name`;
  const displayName = draft.fields[inputNameKey] ?? selected.name;
  const isRaster = Boolean(selected.tableId);
  const isPolygon = !selected.canRemove;
  const constantValueKey = `${selected.scope}/ConstantClassification/Value`;
  const isConstant = !isRaster && !isPolygon && (draft.fields[constantValueKey] !== undefined || selected.extent != null || /constant/i.test(selected.type));
  const canReorder = snapshot.canEdit && !!props.onClassificationTransform && inputs.length > 1;

  return (
    <section className="editor-schema-page editor-classification-inputs">
      <DetailedClassificationHeader props={props} selected={selected} />
      <div className="editor-classification-layout">
        <nav className="editor-line-list" aria-label={language === 'zh' ? '分类输入' : 'Classification inputs'}>
          <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '输入顺序' : 'Input order'}</h3><span>{inputs.length}</span></div>
          {inputs.map((input) => <button type="button" key={input.id} className={input.id === selected.id ? 'selected' : ''} aria-current={input.id === selected.id ? 'true' : undefined} onClick={() => props.onFieldChange(selectedClassificationField, input.id)}>
            {draft.fields[`${input.scope}/Name`] ?? input.name}<small>{editorValueLabel(language, input.type)}</small>
          </button>)}
          <div className="editor-table-toolbar">
            <button type="button" className="editor-button" disabled={!canReorder || selectedIndex <= 0} onClick={() => void props.onClassificationTransform?.({ operation: 'reorder', from: selectedIndex, to: selectedIndex - 1 })}>{language === 'zh' ? '上移' : 'Move up'}</button>
            <button type="button" className="editor-button" disabled={!canReorder || selectedIndex >= inputs.length - 1} onClick={() => void props.onClassificationTransform?.({ operation: 'reorder', from: selectedIndex, to: selectedIndex + 1 })}>{language === 'zh' ? '下移' : 'Move down'}</button>
            <button type="button" className="editor-button" disabled={!snapshot.canEdit || !props.onClassificationTransform || !selected.canRemove} onClick={() => void props.onClassificationTransform?.({ operation: 'remove', itemId: selected.id })}>{language === 'zh' ? '删除输入' : 'Remove input'}</button>
          </div>
          {!selected.canRemove && <p className="editor-field-muted">{language === 'zh' ? '原始控件不允许从此列表删除多边形输入。' : 'The source editor does not allow removing polygon inputs from this list.'}</p>}
          {!props.onClassificationTransform && <p className="editor-field-muted">{language === 'zh' ? '当前工作环境不支持调整分类输入；删除和排序已停用。' : 'Classification input changes are unavailable in this workspace; removal and reordering are disabled.'}</p>}
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
            <ClassificationRasterEditor props={props} input={selected} />
            {isPolygon && <PolygonClassificationInputProperties props={props} input={selected} />}
            {!isRaster && !isPolygon && !isConstant && <p className="editor-field-muted">{language === 'zh' ? '该分类输入类型没有对应的可编辑字段。' : 'This classification input type has no corresponding editable fields.'}</p>}
          </article>
        </div>
      </div>
      <ClassificationGeometryEditor {...props} />
      <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source controls'}</summary><code>ClassificationLayersEditor → ClassificationRasterEditor → ClassificationMappingEditor</code></details>
    </section>
  );
}
