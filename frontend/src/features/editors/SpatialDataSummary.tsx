import { useEffect, useRef } from 'react';
import type { Language } from '../../shared/workbench';
import type { SpatialGridData, SpatialGridPreview } from './model';

export function SpatialDataSummary(props: {
  language: Language;
  grid?: SpatialGridData | null;
  loading?: boolean;
  onFrame: (timeId: string | undefined, component: string) => void;
}) {
  const { language, grid } = props;
  const [timeId, component] = [grid?.selectedTimeId ?? grid?.times[0] ?? '', grid?.selectedComponent ?? grid?.components[0] ?? 'Value'];
  const frame = () => props.onFrame(timeId || undefined, component);
  return <section className="editor-table-card" aria-label={language === 'zh' ? '栅格快照' : 'Grid snapshot'}>
    <header className="editor-meteorology-layer-header">
      <div><p className="editor-eyebrow">{language === 'zh' ? '已保存的格网' : 'Saved grid'}</p>
        <h3>{language === 'zh' ? '真实模型快照' : 'Model snapshot'}</h3></div>
      {grid && grid.times.length > 0 && <div className="editor-workbench-actions">
        <label className="editor-field"><span>{language === 'zh' ? '时刻' : 'Time'}</span>
          <select value={timeId} onChange={(event) => props.onFrame(event.currentTarget.value || undefined, component)}>
            {grid.times.map((time) => <option key={time} value={time}>{time}</option>)}
          </select>
        </label>
        {grid.components.length > 1 && <label className="editor-field"><span>{language === 'zh' ? '分量' : 'Component'}</span>
          <select value={component} onChange={(event) => props.onFrame(timeId || undefined, event.currentTarget.value)}>
            {grid.components.map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>}
        <button type="button" className="editor-button" disabled={props.loading} onClick={frame}>{props.loading ? (language === 'zh' ? '读取中…' : 'Loading…') : (language === 'zh' ? '读取所选帧' : 'Read selected frame')}</button>
      </div>}
    </header>
    {!grid ? <p className="editor-field-muted">{language === 'zh' ? '快照没有该图层的已保存格网。可在下方管理点输入，或通过栅格导入器创建格网。' : 'The snapshot has no saved grid for this layer. Manage point inputs below or import a raster grid.'}</p>
      : grid.error ? <p className="editor-field-error">{grid.error}</p>
        : <>
          <div className="editor-meteorology-source-summary">
            <span>{(language === 'zh' ? '行' : 'Rows') + ': ' + grid.rows}</span>
            <span>{(language === 'zh' ? '列' : 'Columns') + ': ' + grid.columns}</span>
            <span>{(language === 'zh' ? '时刻数' : 'Timestep count') + ': ' + grid.times.length}</span>
            {grid.units && <span>{(language === 'zh' ? '单位' : 'Units') + ': ' + grid.units}</span>}
            {grid.projection && <span>{(language === 'zh' ? '投影' : 'Projection') + ': ' + grid.projection}</span>}
          </div>
          {grid.times.length === 0 && <p className="editor-field-muted">{language === 'zh' ? '已保存格网没有可读时刻。' : 'The saved grid has no readable timesteps.'}</p>}
          {grid.preview && <RasterPreviewCanvas language={language} preview={grid.preview} />}
          {grid.times.length > 0 && !grid.preview && <button type="button" className="editor-button" disabled={props.loading} onClick={frame}>{language === 'zh' ? '读取首个时刻' : 'Read first timestep'}</button>}
        </>}
  </section>;
}

export function RasterPreviewCanvas({ language, preview }: { language: Language; preview: SpatialGridPreview }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    if (!canvas || !context || preview.width < 1 || preview.height < 1 || typeof preview.rgba !== 'string') return;
    let binary: string;
    try { binary = atob(preview.rgba); } catch { return; }
    if (binary.length !== preview.width * preview.height * 4) return;
    const pixels = new Uint8ClampedArray(binary.length);
    for (let index = 0; index < binary.length; index++) pixels[index] = binary.charCodeAt(index);
    canvas.width = preview.width;
    canvas.height = preview.height;
    context.putImageData(new ImageData(pixels, preview.width, preview.height), 0, 0);
  }, [preview]);
  return <figure style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 6, margin: 0 }}>
    <canvas ref={canvasRef} style={{ maxWidth: '100%', height: 'auto', imageRendering: 'pixelated', border: '1px solid #dfe5e9', background: '#f6f8f9' }} aria-label={language === 'zh' ? '后端栅格帧预览' : 'Backend raster frame preview'} />
    <figcaption style={{ color: '#72808b', fontSize: 10, overflowWrap: 'anywhere' }}>{language === 'zh' ? '边界范围' : 'Extent'}: {preview.minX}, {preview.minY} – {preview.maxX}, {preview.maxY}
      {preview.minimum != null && preview.maximum != null && ` · ${language === 'zh' ? '值域' : 'Range'}: ${preview.minimum} – ${preview.maximum}`}</figcaption>
  </figure>;
}
