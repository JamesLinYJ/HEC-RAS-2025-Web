import type { EditorPageProps } from './model';
import { editorValueLabel } from './editorLocalization';
import { ClassificationLayerManningNEditor } from './ClassificationLayerManningNEditor';
import { ConstantManningsNEditor } from './ConstantManningsNEditor';
import { DetailedManningsNHeader } from './DetailedManningsNHeader';

const modeField = 'ManningsNEditor/NValueLayer_Mode';

/** React port of Ras.Visual.WinUI.Controls.ManningsNEditor. */
export function ManningsNEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const manning = draft.manningView ?? snapshot.manning;
  const currentMode = draft.fields[modeField] ?? 'Constant';
  return (
    <section className="editor-schema-page editor-manning-editor">
      <DetailedManningsNHeader {...props} />
      {manning?.warning && <div className="editor-config-notice" role="status">{language === 'zh' ? '糙率数据存在提示。' : 'The Manning data includes a warning.'}<details><summary>{language === 'zh' ? '查看详情' : 'View details'}</summary><pre>{manning.warning}</pre></details></div>}
      <div className="editor-field-grid">
        <div className="editor-field"><label htmlFor="manning-mode">{language === 'zh' ? '糙率来源' : 'Source mode'}</label>
          <select id="manning-mode" value={currentMode} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(modeField, event.currentTarget.value)}>
            {!['Constant', 'ClassificationLayer'].includes(currentMode) && <option value={currentMode}>{editorValueLabel(language, currentMode)}</option>}
            <option value="Constant">{language === 'zh' ? '常数' : 'Constant'}</option><option value="ClassificationLayer">{language === 'zh' ? '分类图层' : 'Classification layer'}</option>
          </select>
        </div>
      </div>
      {currentMode === 'Constant' && <ConstantManningsNEditor {...props} />}
      {currentMode === 'ClassificationLayer' && <ClassificationLayerManningNEditor {...props} />}
      {!['Constant', 'ClassificationLayer'].includes(currentMode) && <p className="editor-field-muted">{language === 'zh' ? '此来源模式保留在模型中，但当前服务仅提供常数和分类图层模式的编辑。' : 'This source mode is preserved, while the current service only exposes Constant and ClassificationLayer edits.'}</p>}
      <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source controls'}</summary><code>DetailedManningsNHeader → ManningsNEditor → ConstantManningsNEditor / ClassificationLayerManningNEditor</code></details>
    </section>
  );
}
