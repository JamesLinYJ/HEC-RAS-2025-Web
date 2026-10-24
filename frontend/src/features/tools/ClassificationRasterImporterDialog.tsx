import { useMemo } from 'react';
import type { Language } from '../../shared/workbench';
import { rgbaFrameImage } from '../map/mapImages';
import type { ClassificationImportFile, ClassificationImportItem } from './coreImportWorkflow';

interface Props {
  language: Language;
  item: ClassificationImportItem;
  selection: ClassificationImportFile;
  included: boolean;
  busy: boolean;
  projection: string;
  onIncludeChange: (included: boolean) => void;
  onSelectionChange: (selection: ClassificationImportFile) => void;
}

function label(language: Language, zh: string, en: string): string { return language === 'zh' ? zh : en; }

export function ClassificationRasterImporterDialog({ language, item, selection, included, busy, projection, onIncludeChange, onSelectionChange }: Props) {
  const raster = item.raster;
  const preview = useMemo(() => {
    if (!raster) return { image: '', error: '' };
    try { return { image: rgbaFrameImage(raster.rgba, raster.width, raster.height), error: '' }; }
    catch (error) { return { image: '', error: error instanceof Error ? error.message : String(error) }; }
  }, [raster]);

  return (
    <article className="ci-import-row" aria-label={label(language, '分类栅格来源', 'Classification raster source')}>
      <label className="ci-check-line"><input type="checkbox" checked={included} disabled={busy}
        onChange={event => onIncludeChange(event.target.checked)} /><strong>{selection.name || item.selection.name || item.selection.file}</strong></label>
      <code>{item.selection.file}</code>
      <code>{item.selection.revision}</code>
      <div className="ci-inline-meta">{item.rows ?? '—'} × {item.columnsCount ?? '—'} · {item.bands ?? '—'} {label(language, '波段', 'bands')} · {label(language, '目标投影', 'Target projection')}: {projection || '—'}</div>
      <div className="ci-inline-meta">{label(language, '像元范围', 'Value range')}: {raster?.minimum ?? '—'} – {raster?.maximum ?? '—'} · {label(language, '坐标范围', 'Extent')}: {raster ? `${raster.minX}, ${raster.minY} – ${raster.maxX}, ${raster.maxY}` : '—'}</div>
      {preview.image && <img className="ci-grid-preview" src={preview.image} alt={label(language, `服务端分类栅格预览 ${item.rows ?? ''} 行 ${item.columnsCount ?? ''} 列`, `Server classification raster preview ${item.rows ?? ''} rows by ${item.columnsCount ?? ''} columns`)} />}
      {preview.error && <div className="ci-warning" role="status">{label(language, '预览无法显示', 'Preview could not be displayed')}: {preview.error}</div>}
      <label className="ci-field"><span>{label(language, '分类图层名称', 'Classification layer name')}</span><input value={selection.name ?? ''} disabled={busy}
        onChange={event => onSelectionChange({ ...selection, name: event.target.value })} /></label>
      <p className="ci-help">{label(language,
        '当前 Host 仅支持 TIFF 分类栅格。导入时会由 C# 转换到工程投影，并在保存模型时发布到分类 HDF。',
        'The Host supports TIFF classification rasters. C# converts the raster to the project projection and publishes it to the classification HDF during model save.')}</p>
    </article>
  );
}
