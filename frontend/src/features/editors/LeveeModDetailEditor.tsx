import type { EditorPageProps, EditorSchema, ModelRow, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';

const featureTableID = 'LeveeModsTable';
const profileTableID = 'LeveeModDetailEditor/Profile';

export function LeveeModDetailEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onRowCellChange, schema }: EditorPageProps & { selected: NonNullable<NonNullable<EditorPageProps['snapshot']['terrain']>['inputs']>[number]; schema?: EditorSchema }) {
  const featureTable = snapshot.terrain?.tables?.[featureTableID];
  const selectedRows = featureTable?.selectedRowIDs ?? featureTable?.requestedSelectionRowIDs ?? [];
  const scopePrefix = selectedRows.length === 1 ? `TerrainSelection/${selectedRows[0]}/LeveeModDetailEditor` : '';
  const hasSelectedFields = !!schema && selectedRows.length === 1 && schema.fields.some((field) => {
    const key = `${scopePrefix}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const featureRows = draft.rows[featureTableID] ?? snapshot.terrain?.rows?.[featureTableID] ?? [];
  const profileRows = draft.rows[profileTableID] ?? snapshot.terrain?.rows?.[profileTableID] ?? [];
  const profileTable = snapshot.terrain?.tables?.[profileTableID];
  return (
    <section className="editor-terrain-card editor-levee-detail" aria-label={language === 'zh' ? '简单挖填线编辑器' : 'Simple cut/fill line editor'}>
      <h4>{language === 'zh' ? '简单挖填线' : 'Simple cut/fill lines'}</h4>
      {hasSelectedFields && schema && <SchemaFieldRenderer schema={schema} fieldKeyPrefix={scopePrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />}
      {featureTable && <TableCard tableID={featureTableID} title={language === 'zh' ? '挖填线' : 'Cut/fill lines'} table={featureTable} rows={featureRows} snapshot={snapshot} language={language} onRowCellChange={onRowCellChange} />}
      {selectedRows.length === 1 && profileTable && <TableCard tableID={profileTableID} title={language === 'zh' ? '线剖面' : 'Line profile'} table={profileTable} rows={profileRows} snapshot={snapshot} language={language} onRowCellChange={onRowCellChange} />}
      {selectedRows.length === 1 && <LeveeProfileChart language={language} rows={profileRows} />}
      {featureTable && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '线属性依赖原编辑器中的要素选择；当前显示服务端返回的行，并提交已有单元格草稿。' : 'Line properties depend on the source editor feature selection; this view shows returned rows and submits edits to existing cells.'}</small>}
    </section>
  );
}

function TableCard({ tableID, title, table, rows, snapshot, language, onRowCellChange }: { tableID: string; title: string; table: ModelTable; rows: ModelRow[]; snapshot: EditorPageProps['snapshot']; language: 'zh' | 'en'; onRowCellChange: EditorPageProps['onRowCellChange'] }) {
  return <div className="editor-table-card">
    <div className="editor-terrain-table-heading"><h5>{title}</h5><span>{rows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
    <ModelDataTable tableID={tableID} rows={rows} columns={table.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
    {(table.canAddRows || table.canDeleteRows || table.canCopyRows) && <small className="editor-field-muted">{language === 'zh' ? '原控件支持新增或删除行；当前接口只提交已有行单元格。' : 'The source editor supports adding or removing rows; the current interface submits existing row cells only.'}</small>}
  </div>;
}

function LeveeProfileChart({ language, rows }: { language: 'zh' | 'en'; rows: ModelRow[] }) {
  const points = rows.flatMap((row) => {
    const station = Number(row.values.Station);
    const elevation = Number(row.values.Elevation);
    return Number.isFinite(station) && Number.isFinite(elevation) ? [{ station, elevation }] : [];
  });
  if (points.length < 2) return null;
  const minStation = Math.min(...points.map((point) => point.station));
  const maxStation = Math.max(...points.map((point) => point.station));
  const minElevation = Math.min(...points.map((point) => point.elevation));
  const maxElevation = Math.max(...points.map((point) => point.elevation));
  const plotted = points.map((point) => {
    const x = 12 + (point.station - minStation) / (maxStation - minStation || 1) * 296;
    const y = 108 - (point.elevation - minElevation) / (maxElevation - minElevation || 1) * 96;
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');
  return <figure className="editor-table-card" aria-label={language === 'zh' ? '挖填线剖面图' : 'Cut/fill line profile chart'}>
    <figcaption>{language === 'zh' ? '剖面' : 'Profile'}</figcaption>
    <svg viewBox="0 0 320 120" role="img" aria-label={language === 'zh' ? '桩号与高程剖面' : 'Station and elevation profile'}>
      <line x1="12" y1="108" x2="308" y2="108" stroke="currentColor" opacity="0.35" />
      <polyline points={plotted} fill="none" stroke="#2563eb" strokeWidth="2" />
    </svg>
  </figure>;
}
