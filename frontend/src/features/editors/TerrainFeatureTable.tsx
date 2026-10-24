import type { EditorPageProps, ModelRow, ModelTable } from './model';
import { LeveeModsTable } from './LeveeModsTable';

/** Shows the exact source rows and stable IDs returned for a terrain modification layer. */
export function TerrainFeatureTable({ tableID, title, table, rows, language, canEdit, onCellChange, onTransform, selectionMode }: {
  tableID: string;
  title: string;
  table: ModelTable;
  rows: ModelRow[];
  language: 'zh' | 'en';
  canEdit: boolean;
  onCellChange: EditorPageProps['onRowCellChange'];
  onTransform?: EditorPageProps['onTerrainTransform'];
  selectionMode: 'none' | 'single' | 'multiple';
}) {
  const selected = table.selectedRowIDs ?? table.requestedSelectionRowIDs ?? [];
  return <section className="editor-terrain-card" data-testid={`terrain-mod-layer-${encodeURIComponent(tableID)}`}>
    <LeveeModsTable
      tableID={tableID}
      title={title}
      table={table}
      rows={rows}
      language={language}
      canEdit={canEdit}
      onCellChange={onCellChange}
      onTransform={onTransform}
      selectionMode={selectionMode}
    />
    {selectionMode !== 'none' && <small className="editor-field-muted">{language === 'zh'
      ? `选择状态由当前地形草稿中的稳定要素 ID 保存（${selected.length} 项）。`
      : `Selection is stored in the current terrain draft by stable feature IDs (${selected.length} selected).`}</small>}
  </section>;
}
