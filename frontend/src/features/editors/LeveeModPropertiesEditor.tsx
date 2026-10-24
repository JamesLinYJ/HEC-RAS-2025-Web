import type { EditorPageProps, EditorSchema, ModelSnapshot } from './model';
import { LeveeModsTable } from './LeveeModsTable';
import { LeveeModPropertiesDetailEditor } from './LeveeModPropertiesDetailEditor';

const propertyTableID = 'LeveeModPropertiesEditor/PropertySets';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Property-set selection and structural actions from LeveeModPropertiesEditor. */
export function LeveeModPropertiesEditor(props: EditorPageProps & { selected: TerrainInput; schema?: EditorSchema }) {
  const { language, snapshot, draft, onRowCellChange, onTerrainTransform } = props;
  const table = snapshot.terrain?.tables?.[propertyTableID];
  const rows = draft.rows[propertyTableID] ?? snapshot.terrain?.rows?.[propertyTableID] ?? [];
  const selectedRows = table?.selectedRowIDs ?? table?.requestedSelectionRowIDs ?? [];
  return <section className="editor-terrain-card editor-levee-properties-editor" aria-label={language === 'zh' ? '挖填属性集编辑器' : 'Levee modification property sets'}>
    <h4>{language === 'zh' ? '简单挖填属性集' : 'Simple cut/fill property sets'}</h4>
    {table && <LeveeModsTable tableID={propertyTableID} title={language === 'zh' ? '属性集' : 'Property sets'} table={table} rows={rows} language={language} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} onTransform={onTerrainTransform} selectionMode="multiple" />}
    {table && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '选择一个或多个属性集以查看参数或应用批量设置。' : 'Select one or more property sets to inspect or apply shared values.'}</small>}
    <LeveeModPropertiesDetailEditor {...props} />
  </section>;
}
