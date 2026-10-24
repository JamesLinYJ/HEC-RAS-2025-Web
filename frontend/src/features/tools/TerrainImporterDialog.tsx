import type { Language } from '../../shared/workbench';
import type { TerrainInputImportInspection, TerrainInputImportSource, TerrainInputImportSelection } from './coreImportWorkflow';

export interface TerrainImportRowSettings {
  include: boolean;
  rounding: string;
  unitConversion: string;
  customScaleFactor: string;
}

interface Props {
  language: Language;
  inspection: TerrainInputImportInspection;
  settings: Record<string, TerrainImportRowSettings>;
  stitchTiles: boolean;
  busy: boolean;
  canImport: boolean;
  onSettingsChange: (sourceID: string, patch: Partial<TerrainImportRowSettings>) => void;
  onStitchTilesChange: (value: boolean) => void;
  onImport: () => void;
}

function label(language: Language, zh: string, en: string): string { return language === 'zh' ? zh : en; }

function pretty(value: number | null | undefined): string {
  return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString(undefined, { maximumFractionDigits: 6 }) : '—';
}

function selectionFor(source: TerrainInputImportSource, setting: TerrainImportRowSettings): TerrainInputImportSelection {
  return {
    path: source.path,
    sourceRevision: source.revision,
    rounding: setting.rounding,
    unitConversion: setting.unitConversion,
    ...(setting.customScaleFactor.trim() ? { customScaleFactor: Number(setting.customScaleFactor) } : {}),
    sourceId: source.id,
  };
}

export function TerrainImporterDialog({ language, inspection, settings, stitchTiles, busy, canImport,
  onSettingsChange, onStitchTilesChange, onImport }: Props) {
  const included = inspection.sources.filter(source => source.isValid && settings[source.id]?.include);
  return (
    <section className="ci-subdialog" aria-labelledby="terrain-import-dialog-title">
      <header className="ci-import-heading">
        <div><span className="ci-kicker">TerrainImporterDialog</span><h3 id="terrain-import-dialog-title">{label(language, '地形栅格导入设置', 'Terrain raster import settings')}</h3></div>
        <small>{inspection.sources.length} {label(language, '个服务端检查来源', 'inspected server sources')}</small>
      </header>
      <p className="ci-inline-meta">{label(language, '目标投影', 'Target projection')}: {inspection.targetProjection || '—'}</p>
      <div className="ci-table-wrap">
        <table className="ci-table">
          <thead><tr>
            <th>{label(language, '导入', 'Include')}</th><th>{label(language, '地形栅格', 'Terrain raster')}</th>
            <th>{label(language, '网格', 'Grid')}</th><th>{label(language, '范围', 'Range')}</th>
            <th>{label(language, '高程取整', 'Elevation rounding')}</th><th>{label(language, '单位换算', 'Unit conversion')}</th>
          </tr></thead>
          <tbody>{inspection.sources.map(source => {
            const setting = settings[source.id];
            if (!setting) return null;
            const selection = selectionFor(source, setting);
            const customScaleInvalid = setting.unitConversion === 'Custom' &&
              (!Number.isFinite(selection.customScaleFactor) || Number(selection.customScaleFactor) <= 0);
            return <tr key={`${source.id}:${source.revision}`}>
              <td><input type="checkbox" aria-label={label(language, `导入 ${source.name}`, `Include ${source.name}`)} checked={setting.include}
                disabled={!source.isValid || busy} onChange={event => onSettingsChange(source.id, { include: event.target.checked })} /></td>
              <td><strong>{source.name}</strong><code>{source.path}</code><code>{source.revision}</code>
                {!source.isValid && <span className="ci-error" role="alert">{source.error || label(language, '该来源无效。', 'This source is invalid.')}</span>}</td>
              <td>{source.width && source.height ? `${source.width} × ${source.height}` : '—'}<br />{source.bandCount ?? '—'} {label(language, '波段', 'bands')} · {pretty(source.cellSize)}<br />{source.overviewCount == null ? '—' : `${source.overviewCount} ${label(language, '级', 'levels')}`}</td>
              <td>{pretty(source.minimum)} – {pretty(source.maximum)}<br /><span className="ci-inline-meta">{source.projection || '—'}</span><br />{source.defaultScaleFactor == null ? '—' : `× ${pretty(source.defaultScaleFactor)}`}</td>
              <td><select value={setting.rounding} disabled={!source.isValid || busy} onChange={event => onSettingsChange(source.id, { rounding: event.target.value })}>
                {inspection.roundingOptions.map(value => <option key={value} value={value}>{value}</option>)}
              </select></td>
              <td><select value={setting.unitConversion} disabled={!source.isValid || busy} onChange={event => onSettingsChange(source.id, { unitConversion: event.target.value, customScaleFactor: event.target.value === 'Custom' ? setting.customScaleFactor : '' })}>
                {inspection.unitConversionOptions.map(value => <option key={value} value={value}>{value}</option>)}
              </select>
              {setting.unitConversion === 'Custom' && <label className="ci-field"><span>{label(language, '自定义比例因子', 'Custom scale factor')}</span>
                <input inputMode="decimal" value={setting.customScaleFactor} disabled={!source.isValid || busy} aria-invalid={customScaleInvalid}
                  onChange={event => onSettingsChange(source.id, { customScaleFactor: event.target.value })} /></label>}
              {customScaleInvalid && <span className="ci-error" role="alert">{label(language, '请输入有限的正数比例因子。', 'Enter a finite positive scale factor.')}</span>}
              </td>
            </tr>;
          })}</tbody>
        </table>
      </div>
      <label className="ci-check-line"><input type="checkbox" checked={stitchTiles} disabled={busy} onChange={event => onStitchTilesChange(event.target.checked)} />
        <span>{label(language, '拼接相邻栅格瓦片', 'Stitch adjacent raster tiles')}</span></label>
      <p className="ci-help">{label(language,
        '原控件提供高程取整、单位转换和自动拼接；这里保留服务端返回的选项令牌。导入会先暂存转换结果，再以模型保存回执确认落盘。',
        'The source control provides elevation rounding, unit conversion, and tile stitching. The server-provided option tokens are preserved. Import stages the converted rasters, then confirms publication with a model-save receipt.')}</p>
      <div className="ci-actions">
        <button type="button" className="ci-button ci-primary" disabled={!canImport || busy || included.length === 0} onClick={onImport}>
          {busy ? label(language, '转换并保存中…', 'Converting and saving…') : label(language, '导入并保存地形', 'Import and save terrain')}
        </button>
        <span className="ci-inline-meta">{included.length} / {inspection.sources.filter(source => source.isValid).length} {label(language, '个有效来源已选择', 'valid sources selected')}</span>
      </div>
    </section>
  );
}
