import type { EditorPageProps } from './model';
import { ModelDataTable } from './ModelDataTable';
import { editorValueLabel } from './editorLocalization';

const modeField = 'ManningsNEditor/NValueLayer_Mode';
const constantField = 'ConstantManningsNEditor/NValueLayer_ConstantValue';
const classificationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';
const interpretationTable = 'ClassificationLayerManningNEditor/InterpretationTable';

/** React port of Ras.Visual.WinUI.Controls.ManningsNEditor. */
export function ManningsNEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const currentMode = draft.fields[modeField] ?? 'Constant';
  const constantValue = draft.fields[constantField] ?? '';
  const association = draft.fields[classificationField] ?? '';
  const rows = draft.rows[interpretationTable] ?? snapshot.manning?.rows ?? [];
  const choices = snapshot.manning?.classifications ?? [];
  const tableColumns = [{ id: 'Key', title: language === 'zh' ? '分类' : 'Classification', readOnly: true }, { id: 'Value', title: language === 'zh' ? '糙率 n' : "Manning's n", sourceType: 'float' }];
  return (
    <section className="editor-schema-page editor-manning-editor">
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{language === 'zh' ? '糙率图层' : 'Manning layer'}</p><h2>{language === 'zh' ? '曼宁糙率' : "Manning's n"}</h2></div>
        <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source control'}</summary><code>ManningsNEditor</code></details>
      </div>
      {snapshot.manning?.warning && <div className="editor-config-notice" role="status">{language === 'zh' ? '糙率数据存在提示。' : 'The Manning data includes a warning.'}<details><summary>{language === 'zh' ? '查看详情' : 'View details'}</summary><pre>{snapshot.manning.warning}</pre></details></div>}
      <div className="editor-field-grid">
        <div className="editor-field"><label htmlFor="manning-mode">{language === 'zh' ? '糙率来源' : 'Source mode'}</label>
          <select id="manning-mode" value={currentMode} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(modeField, event.currentTarget.value)}>
            {!['Constant', 'ClassificationLayer'].includes(currentMode) && <option value={currentMode}>{editorValueLabel(language, currentMode)}</option>}
            <option value="Constant">{language === 'zh' ? '常数' : 'Constant'}</option><option value="ClassificationLayer">{language === 'zh' ? '分类图层' : 'Classification layer'}</option>
          </select>
        </div>
        {currentMode === 'Constant' && <div className="editor-field"><label htmlFor="manning-constant">{language === 'zh' ? '常数糙率值' : "Constant Manning's n"}</label><input id="manning-constant" type="text" inputMode="decimal" value={constantValue} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(constantField, event.currentTarget.value)} /></div>}
        {currentMode === 'ClassificationLayer' && <div className="editor-field"><label htmlFor="manning-classification">{language === 'zh' ? '关联分类图层' : 'Associated classification layer'}</label><select id="manning-classification" value={association} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(classificationField, event.currentTarget.value)}>
          <option value="">{language === 'zh' ? '未关联' : 'None'}</option>{choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.title}</option>)}
        </select></div>}
      </div>
      {currentMode === 'ClassificationLayer' && <div className="editor-table-card">
        <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '解释表' : 'Interpretation table'}</h3><span>{rows.length} {language === 'zh' ? '个分类' : 'classes'}</span></div>
        <ModelDataTable tableID={interpretationTable} rows={rows} columns={tableColumns} canEdit={snapshot.canEdit} onCellChange={props.onRowCellChange} language={language} />
      </div>}
      <p className="editor-field-muted">{language === 'zh' ? '解释表的分类键和行数由原始分类数据决定，只能修改现有糙率值。' : 'Interpretation keys and row count come from the source classification; existing roughness values can be edited.'}</p>
    </section>
  );
}
