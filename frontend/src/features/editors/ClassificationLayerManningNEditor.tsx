import type { EditorPageProps, ModelRow } from './model';
import { ModelDataTable } from './ModelDataTable';

const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';
const interpretationTable = 'ClassificationLayerManningNEditor/InterpretationTable';

/** Classification chooser and interpretation DataGrid child of ManningsNEditor. */
export function ClassificationLayerManningNEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const manning = draft.manningView ?? snapshot.manning;
  const choices = manning?.classifications ?? [];
  const association = draft.fields[associationField] ?? '';
  const rows: ModelRow[] = draft.rows[interpretationTable] ?? manning?.rows ?? [];
  const canApplyDefaults = snapshot.canEdit && choices.some((choice) => choice.id === association);

  return (
    <section className="editor-field-grid" aria-label={language === 'zh' ? '分类图层糙率属性' : 'Classification layer Manning properties'}>
      <div className="editor-field">
        <label htmlFor="manning-classification-layer">{language === 'zh' ? '关联分类图层' : 'Classification layer'}</label>
        <select
          id="manning-classification-layer"
          value={association}
          disabled={!snapshot.canEdit}
          onChange={(event) => props.onFieldChange(associationField, event.currentTarget.value)}
        >
          <option value="">{language === 'zh' ? '未关联' : 'None'}</option>
          {association && !choices.some((choice) => choice.id === association) && <option value={association}>{association}</option>}
          {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.title}</option>)}
        </select>
      </div>
      <section className="editor-table-card" aria-label={language === 'zh' ? '糙率解释表' : 'Manning interpretation table'}>
        <div className="editor-terrain-table-heading">
          <h3>{language === 'zh' ? '糙率解释表' : 'Manning interpretation table'}</h3>
          <span>{rows.length} {language === 'zh' ? '个分类' : 'classes'}</span>
        </div>
        {canApplyDefaults && <div className="editor-table-toolbar">
          <button type="button" className="editor-button" disabled={!props.onManningTransform} onClick={() => void props.onManningTransform?.('setNLCD')}>{language === 'zh' ? '套用 NLCD 糙率默认值' : 'Set NLCD defaults'}</button>
        </div>}
        <ModelDataTable
          tableID={interpretationTable}
          rows={rows}
          columns={[
            { id: 'Key', title: language === 'zh' ? '分类' : 'Classification', readOnly: true },
            { id: 'Value', title: language === 'zh' ? '糙率 n' : "Manning's n", sourceType: 'float' },
          ]}
          canEdit={snapshot.canEdit}
          onCellChange={props.onRowCellChange}
          language={language}
        />
      </section>
      <p className="editor-field-muted">{language === 'zh' ? '分类键和行数由源分类数据决定；仅可编辑现有糙率值。NLCD 默认值按分类名称匹配并加入草稿。' : 'Classification keys and row count come from the source data. Existing Manning values are editable; NLCD defaults are matched by name and added to the draft.'}</p>
      {!props.onManningTransform && canApplyDefaults && <p className="editor-field-muted">{language === 'zh' ? '当前工作环境不支持套用 NLCD 默认值，操作已停用。' : 'NLCD defaults are unavailable in this workspace.'}</p>}
    </section>
  );
}
