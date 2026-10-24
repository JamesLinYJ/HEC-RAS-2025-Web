import { useEffect, useRef, useState } from 'react';
import type { ClassificationMapData, EditorPageProps, NValuePreviewData, SpatialGridPreview } from './model';

/** Draws the exact RGBA byte buffer returned by the preview operation. */
export function RasterPreviewCanvas({ raster, language, opacity = 100, label }: {
  raster: SpatialGridPreview | null | undefined;
  language: EditorPageProps['language'];
  opacity?: number;
  label: string;
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const element = canvas.current;
    const context = element?.getContext('2d');
    setError('');
    if (!element || !context || !raster) return;
    try {
      const binary = atob(raster.rgba);
      if (!Number.isInteger(raster.width) || !Number.isInteger(raster.height) || raster.width <= 0 || raster.height <= 0
        || binary.length !== raster.width * raster.height * 4) throw new Error('RGBA preview dimensions do not match the pixel buffer.');
      const pixels = new Uint8ClampedArray(binary.length);
      for (let index = 0; index < binary.length; index++) pixels[index] = binary.charCodeAt(index);
      const alphaScale = Math.max(0, Math.min(100, opacity)) / 100;
      for (let index = 3; index < pixels.length; index += 4) pixels[index] = Math.round(pixels[index] * alphaScale);
      element.width = raster.width;
      element.height = raster.height;
      context.putImageData(new ImageData(pixels, raster.width, raster.height), 0, 0);
    } catch (cause) {
      context.clearRect(0, 0, element.width, element.height);
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [raster, opacity]);

  if (!raster) return <div className="editor-empty-state">{language === 'zh' ? '此来源没有可绘制的预览栅格。' : 'This source has no renderable preview raster.'}</div>;
  return (
    <div className="editor-raster-preview">
      <canvas ref={canvas} aria-label={label} role="img" style={{ width: '100%', height: 'auto', imageRendering: 'pixelated' }} />
      {error && <p className="editor-config-notice" role="alert">{language === 'zh' ? '预览像素无法显示。' : 'The preview pixels could not be displayed.'}<details><summary>{language === 'zh' ? '诊断' : 'Diagnostic'}</summary><pre>{error}</pre></details></p>}
      <small className="editor-field-muted">{language === 'zh' ? '范围' : 'Extent'}：{raster.minX}, {raster.minY} — {raster.maxX}, {raster.maxY}</small>
    </div>
  );
}

/** Browser-side view controls over the two map rasters returned by model.classificationMap. */
export function ClassificationVisualizationOptions({ map, language, selectedInputName }: {
  map: ClassificationMapData;
  language: EditorPageProps['language'];
  selectedInputName?: string;
}) {
  const [showSelected, setShowSelected] = useState(Boolean(map.selectedRaster));
  const [opacity, setOpacity] = useState(100);
  useEffect(() => setShowSelected(Boolean(map.selectedRaster)), [map]);
  const raster = showSelected && map.selectedRaster ? map.selectedRaster : map.raster;
  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类显示' : 'Classification visualization'}>
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '分类显示' : 'Classification visualization'}</h3>
        {map.projection && <span>{map.projection}</span>}
      </div>
      <div className="editor-field-grid">
        <div className="editor-field">
          <label htmlFor="classification-preview-source">{language === 'zh' ? '显示数据' : 'Displayed data'}</label>
          <select id="classification-preview-source" value={showSelected && map.selectedRaster ? 'selected' : 'full'} onChange={(event) => setShowSelected(event.currentTarget.value === 'selected')}>
            <option value="full">{language === 'zh' ? '完整分类图层' : 'Full classification layer'}</option>
            <option value="selected" disabled={!map.selectedRaster}>{selectedInputName ? `${language === 'zh' ? '当前输入' : 'Selected input'}：${selectedInputName}` : language === 'zh' ? '当前输入' : 'Selected input'}</option>
          </select>
        </div>
        <div className="editor-field">
          <label htmlFor="classification-preview-opacity">{language === 'zh' ? `预览透明度 ${opacity}%` : `Preview opacity ${opacity}%`}</label>
          <input id="classification-preview-opacity" type="range" min="0" max="100" value={opacity} onChange={(event) => setOpacity(Number(event.currentTarget.value))} />
        </div>
      </div>
      <p className="editor-field-muted">{language === 'zh' ? '透明度仅影响此预览，不会保存到模型。' : 'Opacity affects this preview only and is not saved to the model.'}</p>
      <RasterPreviewCanvas raster={raster} opacity={opacity} language={language} label={language === 'zh' ? '分类栅格预览' : 'Classification raster preview'} />
      <div className="editor-classification-legend" aria-label={language === 'zh' ? '分类图例' : 'Classification legend'}>
        {map.legend.map((item) => <div key={item.id} className="editor-classification-legend-item"><span aria-hidden="true" style={{ backgroundColor: item.color }} />{item.name}<small>{item.id}</small></div>)}
      </div>
      {map.legend.length === 0 && <p className="editor-field-muted">{language === 'zh' ? '没有可显示的分类图例项。' : 'There are no classification legend entries to display.'}</p>}
    </section>
  );
}

export function NValuePreview({ preview, language }: { preview: NValuePreviewData; language: EditorPageProps['language'] }) {
  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '糙率栅格预览' : 'Manning raster preview'}>
      <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '糙率栅格预览' : 'Manning raster preview'}</h3><span>{preview.mode}</span></div>
      <RasterPreviewCanvas raster={preview.raster} language={language} label={language === 'zh' ? '糙率栅格预览' : 'Manning raster preview'} />
      {preview.histogram && <dl className="editor-readonly-list">
        <dt>{language === 'zh' ? '最小值' : 'Minimum'}</dt><dd>{preview.histogram.minimum}</dd>
        <dt>{language === 'zh' ? '最大值' : 'Maximum'}</dt><dd>{preview.histogram.maximum}</dd>
        <dt>{language === 'zh' ? '分箱宽度' : 'Bin width'}</dt><dd>{preview.histogram.binWidth}</dd>
      </dl>}
      {preview.legendStops.length > 0 && <div className="editor-classification-legend" aria-label={language === 'zh' ? '糙率图例' : 'Manning legend'}>
        {preview.legendStops.map((stop) => <div key={`${stop.value}:${stop.red}:${stop.green}:${stop.blue}`} className="editor-classification-legend-item"><span aria-hidden="true" style={{ backgroundColor: `rgba(${stop.red}, ${stop.green}, ${stop.blue}, ${stop.alpha / 255})` }} />{stop.value}</div>)}
      </div>}
    </section>
  );
}
