import type { EditorPageProps, EditorSchema, ModelRow } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';

const profileTableID = 'LeveeModDetailEditor/Profile';

export function LeveeModDetailEditor({ language, snapshot, draft, onFieldChange, onImagesChange, onRowCellChange, onTerrainTransform, schema }: EditorPageProps & { selected: NonNullable<NonNullable<EditorPageProps['snapshot']['terrain']>['inputs']>[number]; schema?: EditorSchema }) {
  const featureTable = snapshot.terrain?.tables?.['LeveeModsTable'];
  const selectedRows = featureTable?.selectedRowIDs ?? featureTable?.requestedSelectionRowIDs ?? [];
  const scopePrefix = selectedRows.length === 1 ? `TerrainSelection/${selectedRows[0]}/LeveeModDetailEditor` : '';
  const hasSelectedFields = !!schema && selectedRows.length === 1 && schema.fields.some((field) => {
    const key = `${scopePrefix}/${field.id}`;
    return Object.hasOwn(snapshot.fields, key) || Object.hasOwn(draft.fields, key);
  });
  const profileRows = draft.rows[profileTableID] ?? snapshot.terrain?.rows?.[profileTableID] ?? [];
  const profileTable = snapshot.terrain?.tables?.[profileTableID];
  return (
    <div className="editor-levee-detail">
      {hasSelectedFields && schema && <SchemaFieldRenderer schema={schema} fieldKeyPrefix={scopePrefix} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />}
      {selectedRows.length === 1 && profileTable && <div className="editor-table-card">
        <div className="editor-terrain-table-heading"><h5>{language === 'zh' ? '线剖面' : 'Line profile'}</h5><span>{profileRows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
        <ModelDataTable tableID={profileTableID} rows={profileRows} columns={profileTable.columns} canEdit={snapshot.canEdit} onCellChange={onRowCellChange} language={language} />
        {profileTable.canAddRows && <button type="button" className="editor-button" disabled={!snapshot.canEdit || !onTerrainTransform} onClick={() => void onTerrainTransform?.({ operation: 'addRow', tableId: profileTableID })}>{language === 'zh' ? '新增剖面点' : 'Add profile point'}</button>}
      </div>}
      {selectedRows.length === 1 && <LeveeProfileChart language={language} rows={profileRows} />}
    </div>
  );
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
