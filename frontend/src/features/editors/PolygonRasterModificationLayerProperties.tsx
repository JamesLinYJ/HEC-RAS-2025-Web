import type { EditorPageProps, ModelRow, ModelTable } from './model';
import { TerrainFeatureTable } from './TerrainFeatureTable';

/** The source polygon table is read from the Host draft and retains its feature row IDs. */
export function PolygonRasterModificationLayerProperties({ language, tableID, table, rows, canEdit, onCellChange, onTransform }: {
  language: 'zh' | 'en';
  tableID: string;
  table?: ModelTable;
  rows: ModelRow[];
  canEdit: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
  onTransform?: EditorPageProps['onTerrainTransform'];
}) {
  if (!table) return <section className="editor-terrain-card" aria-label={language === 'zh' ? '多边形修改要素' : 'Polygon modification features'}>
    <h4>{language === 'zh' ? '多边形修改要素' : 'Polygon modification features'}</h4>
    <p className="editor-field-muted">{language === 'zh'
      ? '当前 Host 快照未返回可编辑的源矢量要素表；不会在此生成模拟要素。'
      : 'The current Host snapshot did not return an editable source-vector feature table. No placeholder features are created.'}</p>
  </section>;

  return <TerrainFeatureTable
    tableID={tableID}
    title={language === 'zh' ? '多边形修改要素（源矢量）' : 'Polygon modification features (source vector)'}
    table={table}
    rows={rows}
    language={language}
    canEdit={canEdit}
    onCellChange={onCellChange}
    onTransform={onTransform}
    selectionMode="multiple"
  />;
}
