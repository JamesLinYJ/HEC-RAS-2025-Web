import type { EditorPageProps, EditorSchema, ModelRow, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';

const featureTableID = 'ChannelModEditor/Features';
const profileTableID = 'ChannelModEditor/Profile';
const terrainProfileTableID = 'ChannelModEditor/TerrainProfile';

export function ChannelModEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onRowCellChange, schema }: EditorPageProps & { schema: EditorSchema }) {
  const featureTable = snapshot.terrain?.tables?.[featureTableID];
  const selectedRows = featureTable?.selectedRowIDs ?? featureTable?.requestedSelectionRowIDs ?? [];
  const selectedFeatureScope = selectedRows.length === 1 ? `TerrainSelection/${selectedRows[0]}/${schema.id}` : '';
  const hasSelectedFeatureFields = selectedRows.length === 1 && schema.fields.some((field) => {
    const key = `${selectedFeatureScope}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const profileRows = draft.rows[profileTableID] ?? snapshot.terrain?.rows?.[profileTableID] ?? [];
  const terrainRows = draft.rows[terrainProfileTableID] ?? snapshot.terrain?.rows?.[terrainProfileTableID] ?? [];
  const tables = snapshot.terrain?.tables ?? {};
  const displaySchema = { ...schema, fields: schema.fields.map((field) => ({ ...field, visibleBy: [] })) };
  return (
    <section className="editor-terrain-card editor-channel-mod" aria-label={language === 'zh' ? '渠道修改编辑器' : 'Channel modification editor'}>
      <h4>{language === 'zh' ? '渠道修改线' : 'Channel modification lines'}</h4>
      {hasSelectedFeatureFields && <SchemaFieldRenderer schema={displaySchema} fieldKeyPrefix={selectedFeatureScope} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />}
      {featureTable && <TableCard tableID={featureTableID} title={language === 'zh' ? '渠道修改线' : 'Channel modification lines'} table={featureTable} rows={draft.rows[featureTableID] ?? snapshot.terrain?.rows?.[featureTableID] ?? []} props={{ snapshot, draft, language, onRowCellChange }} />}
      {selectedRows.length === 1 && tables[profileTableID] && <TableCard tableID={profileTableID} title={language === 'zh' ? '渠道剖面' : 'Channel profile'} table={tables[profileTableID]} rows={profileRows} props={{ snapshot, draft, language, onRowCellChange }} />}
      {selectedRows.length === 1 && tables[terrainProfileTableID] && <TableCard tableID={terrainProfileTableID} title={language === 'zh' ? '现状地形剖面' : 'Terrain profile'} table={tables[terrainProfileTableID]} rows={terrainRows} props={{ snapshot, draft, language, onRowCellChange }} />}
      {selectedRows.length === 1 && <ProfileChart language={language} profile={profileRows} terrain={terrainRows} />}
      {featureTable && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '原控件按选中的渠道线显示剖面；当前表格回显数据和单元格草稿，行选择需接入地形选择操作。' : 'The source editor shows a profile for the selected channel line; this table exposes returned rows and cell drafts, while row selection requires the terrain selection operation.'}</small>}
    </section>
  );
}

function TableCard({ tableID, title, table, rows, props }: { tableID: string; title: string; table: ModelTable; rows: ModelRow[]; props: Pick<EditorPageProps, 'snapshot' | 'draft' | 'language' | 'onRowCellChange'> }) {
  const { snapshot, language, onRowCellChange } = props;
  return <div className="editor-table-card">
    <div className="editor-terrain-table-heading"><h5>{title}</h5><span>{rows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
    <ModelDataTable tableID={tableID} rows={rows} columns={table.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
    {(table.canAddRows || table.canDeleteRows || table.canCopyRows) && <small className="editor-field-muted">{language === 'zh' ? '结构性行操作仍需对应的地形操作回调。' : 'Structural row operations still require the matching terrain operation callback.'}</small>}
  </div>;
}

function ProfileChart({ language, profile, terrain }: { language: 'zh' | 'en'; profile: ModelRow[]; terrain: ModelRow[] }) {
  const lines = [
    { id: 'profile', title: language === 'zh' ? '渠道剖面' : 'Channel profile', points: profilePoints(profile) },
    { id: 'terrain', title: language === 'zh' ? '现状地形' : 'Existing terrain', points: profilePoints(terrain) },
  ].filter((line) => line.points.length > 1);
  if (!lines.length) return null;
  const all = lines.flatMap((line) => line.points);
  const minStation = Math.min(...all.map((point) => point.station));
  const maxStation = Math.max(...all.map((point) => point.station));
  const minElevation = Math.min(...all.map((point) => point.elevation));
  const maxElevation = Math.max(...all.map((point) => point.elevation));
  const pointString = (points: Array<{ station: number; elevation: number }>) => points.map((point) => {
    const x = 12 + (point.station - minStation) / (maxStation - minStation || 1) * 296;
    const y = 108 - (point.elevation - minElevation) / (maxElevation - minElevation || 1) * 96;
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');
  return <figure className="editor-table-card" aria-label={language === 'zh' ? '渠道剖面图' : 'Channel profile chart'}>
    <figcaption>{language === 'zh' ? '剖面' : 'Profile'}</figcaption>
    <svg viewBox="0 0 320 120" role="img" aria-label={language === 'zh' ? '桩号与高程剖面' : 'Station and elevation profile'}>
      <line x1="12" y1="108" x2="308" y2="108" stroke="currentColor" opacity="0.35" />
      {lines.map((line, index) => <polyline key={line.id} points={pointString(line.points)} fill="none" stroke={index === 0 ? '#2563eb' : '#16a34a'} strokeWidth="2" />)}
    </svg>
    <div className="editor-field-muted">{lines.map((line) => <span key={line.id} style={{ marginRight: '1rem' }}>{line.title}</span>)}</div>
  </figure>;
}

function profilePoints(rows: ModelRow[]): Array<{ station: number; elevation: number }> {
  return rows.flatMap((row) => {
    if (!row.values.Station?.trim() || !row.values.Elevation?.trim()) return [];
    const station = Number(row.values.Station);
    const elevation = Number(row.values.Elevation);
    return Number.isFinite(station) && Number.isFinite(elevation) ? [{ station, elevation }] : [];
  });
}
