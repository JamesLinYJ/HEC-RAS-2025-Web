import type { EditorPageProps, ModelRow } from './model';
import { ModelDataTable } from './ModelDataTable';

const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';
const interpretationTable = 'ClassificationLayerManningNEditor/InterpretationTable';

const nlcdDefaults: Record<string, string> = {
  'Open Water': '0.035',
  'Perennial Ice/Snow': '0.022',
  'Developed, Open Space': '0.04',
  'Developed, Low Intensity': '0.06',
  'Developed, Medium Intensity': '0.1',
  'Developed, High Intensity': '0.16',
  'Barren Land Rock/Sand/Clay': '0.026',
  'Deciduous Forest': '0.15',
  'Evergreen Forest': '0.12',
  'Mixed Forest': '0.14',
  'Dwarf Scrub': '0.036',
  'Shrub/Scrub': '0.11',
  'Grassland/Herbaceous': '0.04',
  'Sedge/Herbaceous': '0.04',
  Lichens: '0.027',
  Moss: '0.025',
  'Pasture/Hay': '0.04',
  'Cultivated Crops': '0.04',
  'Woody Wetlands': '0.1',
  'Emergent Herbaceous Wetlands': '0.07',
};

/** Classification chooser and interpretation DataGrid child of ManningsNEditor. */
export function ClassificationLayerManningNEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const choices = snapshot.manning?.classifications ?? [];
  const association = draft.fields[associationField] ?? '';
  const rows: ModelRow[] = draft.rows[interpretationTable] ?? snapshot.manning?.rows ?? [];
  const canApplyDefaults = snapshot.canEdit && choices.some((choice) => choice.id === association);

  const applyNLCDDefaults = () => {
    for (const row of rows) {
      const key = row.values.Key;
      const value = key === undefined ? undefined : nlcdDefaults[key];
      if (value !== undefined) props.onRowCellChange(interpretationTable, row.id, 'Value', value);
    }
  };

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
          <button type="button" onClick={applyNLCDDefaults}>{language === 'zh' ? '套用 NLCD 糙率默认值' : 'Set NLCD defaults'}</button>
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
      <p className="editor-field-muted">{language === 'zh' ? '分类键和行数由源分类数据决定；仅可编辑现有糙率值。NLCD 默认操作只更新当前草稿中名称匹配的值。' : 'Classification keys and row count come from the source data. Existing Manning values are editable; NLCD defaults update matching values in this draft.'}</p>
    </section>
  );
}
