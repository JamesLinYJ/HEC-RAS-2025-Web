import type { EditorPageProps, EditorSchema, ModelRow, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';
import { LeveeModsTable } from './LeveeModsTable';

const featureTableID = 'ChannelModEditor/Features';
const profileTableID = 'ChannelModEditor/Profile';
const terrainProfileTableID = 'ChannelModEditor/TerrainProfile';

export function ChannelModEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onRowCellChange, onTerrainTransform, schema }: EditorPageProps & { schema: EditorSchema }) {
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
      {featureTable && <LeveeModsTable tableID={featureTableID} title={language === 'zh' ? '渠道修改线' : 'Channel modification lines'} table={featureTable} rows={draft.rows[featureTableID] ?? snapshot.terrain?.rows?.[featureTableID] ?? []} language={language} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} onTransform={onTerrainTransform} selectionMode="single" />}
      {selectedRows.length === 1 && tables[profileTableID] && <ProfileTable tableID={profileTableID} title={language === 'zh' ? '渠道剖面' : 'Channel profile'} table={tables[profileTableID]} rows={profileRows} canEdit={snapshot.canEdit} language={language} onRowCellChange={onRowCellChange} onTerrainTransform={onTerrainTransform} />}
      {selectedRows.length === 1 && tables[terrainProfileTableID] && <LeveeModsTable tableID={terrainProfileTableID} title={language === 'zh' ? '现状地形剖面' : 'Terrain profile'} table={tables[terrainProfileTableID]} rows={terrainRows} language={language} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} onTransform={onTerrainTransform} />}
      {selectedRows.length === 1 && <ProfileChart language={language} profile={profileRows} terrain={terrainRows} />}
      {featureTable && selectedRows.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '请选择一条渠道修改线以查看并编辑其剖面。' : 'Select one channel modification line to view and edit its profiles.'}</small>}
    </section>
  );
}

function ProfileTable({ tableID, title, table, rows, canEdit, language, onRowCellChange, onTerrainTransform }: { tableID: string; title: string; table: ModelTable; rows: ModelRow[]; canEdit: boolean; language: 'zh' | 'en'; onRowCellChange: EditorPageProps['onRowCellChange']; onTerrainTransform?: EditorPageProps['onTerrainTransform'] }) {
  return <div className="editor-table-card">
    <div className="editor-terrain-table-heading"><h5>{title}</h5><span>{rows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
    <ModelDataTable tableID={tableID} rows={rows} columns={table.columns} canEdit={canEdit} onCellChange={onRowCellChange} language={language} />
    {table.canAddRows && <button type="button" className="editor-button" disabled={!canEdit || !onTerrainTransform} onClick={() => void onTerrainTransform?.({ operation: 'addRow', tableId: tableID })}>{language === 'zh' ? '新增剖面点' : 'Add profile point'}</button>}
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
