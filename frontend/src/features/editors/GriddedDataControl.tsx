import type { EditorPageProps, SpatialGridData } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';

/** Saved-grid status and importer entry point corresponding to GriddedDataControl. */
export function GriddedDataControl(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  grid?: SpatialGridData | null;
  onImport: () => void;
}) {
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  const rasterSupported = props.layerId !== 'evapotranspiration';
  const canImport = rasterSupported && props.snapshot.canEdit && !!props.onGridInspect
    && !!props.onGridImport && !!props.canImportGrid;
  const grid = props.grid;
  const count = grid?.times.length ?? 0;
  return <section className="editor-table-card editor-gridded-data-control" aria-label={label('已导入格网数据', 'Imported gridded data')}>
    <header className="editor-meteorology-layer-header">
      <div>
        <p className="editor-eyebrow">{label('气象边界条件', 'Meteorological boundary condition')}</p>
        <h3>{label('格网数据', 'Gridded data')}</h3>
      </div>
      <strong role="status">{count > 0 ? label(`已保存 ${count} 个时刻`, `${count} saved timesteps`) : label('尚无已保存格网', 'No saved grid')}</strong>
    </header>
    {grid && <div className="editor-meteorology-source-summary">
      <span>{label('网格大小', 'Grid size')}: {grid.rows} × {grid.columns}</span>
      {grid.units && <span>{label('单位', 'Units')}: {grid.units}</span>}
      {grid.projection && <span>{label('投影', 'Projection')}: {grid.projection}</span>}
      {grid.error && <span className="editor-field-error">{grid.error}</span>}
    </div>}
    <button type="button" className="editor-button editor-button-primary" disabled={!canImport} onClick={props.onImport}>
      {count > 0 ? label('导入并替换现有格网…', 'Import over current grid…') : label('导入格网…', 'Import grid…')}
    </button>
    {!canImport && <p className="editor-field-muted">{!rasterSupported
      ? label('蒸散发暂不支持格网导入，请使用点输入。', 'Grid import is unavailable for evapotranspiration. Use point inputs.')
      : !props.canImportGrid
        ? label('导入会立即保存模型。先保存或丢弃当前草稿，再执行导入。', 'Import saves the model immediately. Save or discard the current draft first.')
        : label('当前模型不可编辑，或格网导入暂不可用。', 'The current model is read-only or grid import is unavailable.')}</p>}
  </section>;
}
