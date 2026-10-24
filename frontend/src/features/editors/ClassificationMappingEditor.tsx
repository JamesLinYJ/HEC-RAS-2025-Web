import type { EditorPageProps, ModelRow } from './model';
import { ModelDataTable, type DataColumn } from './ModelDataTable';

/** DataGrid-backed classification map editor hosted by RasterClassificationInputProperties. */
export function ClassificationMappingEditor({ language, rows, tableID, canEdit, onCellChange, columns }: {
  language: EditorPageProps['language'];
  rows: ModelRow[];
  tableID: string;
  canEdit: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
  columns?: DataColumn[];
}) {
  const displayColumns = columns?.length ? columns : [
    { id: 'Key', title: language === 'zh' ? '栅格值' : 'Raster value', sourceType: 'int', readOnly: true },
    { id: 'Value', title: language === 'zh' ? '分类' : 'Classification', sourceType: 'string' },
  ];

  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类值映射' : 'Classification mapping'}>
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '分类值映射' : 'Classification mapping'}</h3>
        <span>{rows.length} {language === 'zh' ? '项' : 'entries'}</span>
      </div>
      <ModelDataTable tableID={tableID} rows={rows} columns={displayColumns} canEdit={canEdit} onCellChange={onCellChange} language={language} />
    </section>
  );
}
