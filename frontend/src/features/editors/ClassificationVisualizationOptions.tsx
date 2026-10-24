import { useEffect, useRef, useState } from 'react';
import type { ClassificationMapData, EditorPageProps, NValuePreviewData, SpatialGridPreview } from './model';

export type ClassificationPolygonMapFeature = {
  id: string;
  inputName: string;
  index: number;
  classification: string;
  vertices: Array<{ x: number; y: number; z?: string | null; m?: string | null }>;
};

/** Draws exact model polygon coordinates and lets the editor select the real feature identity. */
export function ClassificationPolygonCanvas({ features, selectedIDs, language, onSelect }: {
  features: ClassificationPolygonMapFeature[];
  selectedIDs: string[];
  language: EditorPageProps['language'];
  onSelect: (featureID: string, additive: boolean) => void;
}) {
  const drawable = features.filter((feature) => feature.vertices.length >= 3
    && feature.vertices.every((vertex) => Number.isFinite(vertex.x) && Number.isFinite(vertex.y)));
  if (drawable.length === 0) return <div className="editor-empty-state">{language === 'zh' ? '此输入没有可绘制的多边形顶点。' : 'This input has no drawable polygon vertices.'}</div>;

  const coordinates = drawable.flatMap((feature) => feature.vertices);
  const bounds = coordinates.reduce((extent, vertex) => ({
    minX: Math.min(extent.minX, vertex.x),
    maxX: Math.max(extent.maxX, vertex.x),
    minY: Math.min(extent.minY, vertex.y),
    maxY: Math.max(extent.maxY, vertex.y),
  }), { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity });
  const { minX, maxX, minY, maxY } = bounds;
  const rangeX = maxX - minX || 1;
  const rangeY = maxY - minY || 1;
  const width = 920;
  const height = 500;
  const padding = 42;
  const scale = Math.min((width - padding * 2) / rangeX, (height - padding * 2) / rangeY);
  const drawnWidth = rangeX * scale;
  const drawnHeight = rangeY * scale;
  const offsetX = (width - drawnWidth) / 2;
  const offsetY = (height - drawnHeight) / 2;
  const project = (vertex: { x: number; y: number }) => ({
    x: offsetX + (vertex.x - minX) * scale,
    y: height - offsetY - (vertex.y - minY) * scale,
  });
  const formatCoordinate = (value: number) => Number(value.toPrecision(7)).toString();

  return (
    <div className="editor-classification-geometry-preview">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="group"
        aria-label={language === 'zh' ? '分类多边形坐标图，可点选要素' : 'Classification polygon coordinate map; select a feature'}
        data-testid="classification-polygon-map"
        style={{ display: 'block', width: '100%', height: 'min(55vw, 380px)', minHeight: 220, border: '1px solid #d9e0e6', borderRadius: 7, background: '#fbfcfd' }}
      >
        <rect x={padding} y={padding} width={width - padding * 2} height={height - padding * 2} fill="none" stroke="#e2e8ed" />
        {[1, 2, 3].map((index) => {
          const x = padding + (width - padding * 2) * index / 4;
          const y = padding + (height - padding * 2) * index / 4;
          return <g key={index} aria-hidden="true"><line x1={x} x2={x} y1={padding} y2={height - padding} stroke="#edf1f4" /><line x1={padding} x2={width - padding} y1={y} y2={y} stroke="#edf1f4" /></g>;
        })}
        {drawable.map((feature) => {
          const selected = selectedIDs.includes(feature.id);
          const points = feature.vertices.map((vertex) => {
            const point = project(vertex);
            return `${point.x},${point.y}`;
          }).join(' ');
          const center = project({
            x: feature.vertices.reduce((sum, vertex) => sum + vertex.x, 0) / feature.vertices.length,
            y: feature.vertices.reduce((sum, vertex) => sum + vertex.y, 0) / feature.vertices.length,
          });
          const label = language === 'zh'
            ? `${feature.inputName}，要素 ${feature.index + 1}，分类 ${feature.classification}`
            : `${feature.inputName}, feature ${feature.index + 1}, class ${feature.classification}`;
          return <g key={feature.id}>
            <polygon
              points={points}
              role="button"
              tabIndex={0}
              aria-label={label}
              aria-pressed={selected}
              data-testid={`classification-polygon-map-feature-${feature.index + 1}`}
              fill={selected ? '#4c99c880' : '#73899a55'}
              stroke={selected ? '#155f8a' : '#586c7b'}
              strokeWidth={selected ? 3 : 1.5}
              vectorEffect="non-scaling-stroke"
              onClick={(event) => onSelect(feature.id, event.shiftKey || event.ctrlKey || event.metaKey)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  onSelect(feature.id, event.shiftKey || event.ctrlKey || event.metaKey);
                }
              }}
            />
            <text x={center.x} y={center.y} textAnchor="middle" dominantBaseline="middle" fontSize="13" fill="#243746" pointerEvents="none">{feature.index + 1}</text>
          </g>;
        })}
        <text x={padding} y={height - 13} fontSize="11" fill="#647483">X {formatCoordinate(minX)} — {formatCoordinate(maxX)}</text>
        <text x={width - padding} y={height - 13} textAnchor="end" fontSize="11" fill="#647483">Y {formatCoordinate(minY)} — {formatCoordinate(maxY)}</text>
      </svg>
      <p className="editor-field-muted">{language === 'zh'
        ? '图形直接按当前分类草稿中的工程坐标绘制；按住 Shift 或 Ctrl/Cmd 可多选。'
        : 'Shapes use project coordinates from the current classification draft. Hold Shift or Ctrl/Cmd to select multiple features.'}</p>
    </div>
  );
}

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
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类显示' : 'Classification visualization'} data-testid="classification-visualization-options">
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '分类显示' : 'Classification visualization'}</h3>
        {map.projection && <span>{map.projection}</span>}
      </div>
      <div className="editor-field-grid">
        <div className="editor-field">
          <label htmlFor="classification-preview-source">{language === 'zh' ? '显示数据' : 'Displayed data'}</label>
          <select id="classification-preview-source" data-testid="classification-preview-source" value={showSelected && map.selectedRaster ? 'selected' : 'full'} onChange={(event) => setShowSelected(event.currentTarget.value === 'selected')}>
            <option value="full">{language === 'zh' ? '完整分类图层' : 'Full classification layer'}</option>
            <option value="selected" disabled={!map.selectedRaster}>{selectedInputName ? `${language === 'zh' ? '当前输入' : 'Selected input'}：${selectedInputName}` : language === 'zh' ? '当前输入' : 'Selected input'}</option>
          </select>
        </div>
        <div className="editor-field">
          <label htmlFor="classification-preview-opacity">{language === 'zh' ? `预览透明度 ${opacity}%` : `Preview opacity ${opacity}%`}</label>
          <input id="classification-preview-opacity" data-testid="classification-preview-opacity" type="range" min="0" max="100" value={opacity} onChange={(event) => setOpacity(Number(event.currentTarget.value))} />
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
