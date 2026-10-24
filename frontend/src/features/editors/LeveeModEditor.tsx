import type { EditorPageProps, EditorSchema, ModelSnapshot } from './model';
import { LeveeModsTable } from './LeveeModsTable';
import { LeveeModDetailEditor } from './LeveeModDetailEditor';

const featureTableID = 'LeveeModsTable';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** LeveeModEditor selection table and selected-line detail layout. */
export function LeveeModEditor(props: EditorPageProps & { selected: TerrainInput; schema?: EditorSchema }) {
  const { language, snapshot, draft, onRowCellChange, onTerrainTransform } = props;
  const table = snapshot.terrain?.tables?.[featureTableID];
  const rows = draft.rows[featureTableID] ?? snapshot.terrain?.rows?.[featureTableID] ?? [];
  const selectedRows = table?.selectedRowIDs ?? table?.requestedSelectionRowIDs ?? [];
  return <section className="editor-terrain-card editor-levee-editor" aria-label={language === 'zh' ? '挖填线编辑器' : 'Levee modification editor'}>
    <h4>{language === 'zh' ? '简单挖填线' : 'Simple cut/fill lines'}</h4>
    {table && <LeveeModsTable tableID={featureTableID} title={language === 'zh' ? '挖填线' : 'Cut/fill lines'} table={table} rows={rows} language={language} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} onTransform={onTerrainTransform} selectionMode="single" />}
    {table && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '请选择一条线以查看其属性和剖面。' : 'Select a line to view its properties and profile.'}</small>}
    <LeveeModDetailEditor {...props} />
  </section>;
}
