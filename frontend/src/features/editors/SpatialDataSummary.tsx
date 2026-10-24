import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { SpatialGridData, SpatialGridPreview } from './model';
import { GriddedDataImporterSlider } from './GriddedDataImporterSlider';
import { GriddedDataImporterTimeline } from './GriddedDataImporterTimeline';

export function SpatialDataSummary(props: {
  language: Language;
  layerId: string;
  revision: string;
  grid?: SpatialGridData | null;
  loading?: boolean;
  canRead?: boolean;
  onFrame: (timeId: string | undefined, component: string) => void;
}) {
  const { language, grid } = props;
  const [selection, setSelection] = useState({ timeId: '', component: 'Value' });
  useEffect(() => {
    const selectedTime = grid?.selectedTimeId && grid.times.includes(grid.selectedTimeId)
      ? grid.selectedTimeId
      : grid?.times[0] ?? '';
    const selectedComponent = grid?.selectedComponent && grid.components.includes(grid.selectedComponent)
      ? grid.selectedComponent
      : grid?.components[0] ?? 'Value';
    setSelection({ timeId: selectedTime, component: selectedComponent });
  }, [props.layerId, props.revision, grid?.layerId, grid?.selectedTimeId, grid?.selectedComponent]);
  const timeId = grid?.times.includes(selection.timeId) ? selection.timeId : grid?.times[0] ?? '';
  const component = grid?.components.includes(selection.component) ? selection.component : grid?.components[0] ?? 'Value';
  const frame = () => props.onFrame(timeId || undefined, component);
  const selectFrame = (nextTimeId: string, nextComponent: string) => {
    setSelection({ timeId: nextTimeId, component: nextComponent });
    props.onFrame(nextTimeId || undefined, nextComponent);
  };
  const selectedIndex = grid?.times.indexOf(timeId) ?? -1;
  const frameMatchesSelection = grid?.preview
    && grid.selectedTimeId === timeId
    && (grid.selectedComponent ?? grid.components[0] ?? 'Value') === component;
  const timelineItems = grid?.times.map((time, index) => ({ id: String(index), time })) ?? [];
  return <section className="editor-table-card" aria-label={language === 'zh' ? '栅格快照' : 'Grid snapshot'}>
    <header className="editor-meteorology-layer-header">
      <div><p className="editor-eyebrow">{language === 'zh' ? '已保存的格网' : 'Saved grid'}</p>
        <h3>{language === 'zh' ? '已保存格网预览' : 'Saved grid preview'}</h3></div>
      {grid && grid.times.length > 0 && <button type="button" className="editor-button" disabled={props.loading || !props.canRead} onClick={frame}>{props.loading ? (language === 'zh' ? '读取中…' : 'Loading…') : (language === 'zh' ? '读取所选帧' : 'Read selected frame')}</button>}
    </header>
    {!grid ? <p className="editor-field-muted">{language === 'zh' ? '快照没有该图层的已保存格网。可在下方管理点输入，或通过栅格导入器创建格网。' : 'The snapshot has no saved grid for this layer. Manage point inputs below or import a raster grid.'}</p>
      : grid.error ? <p className="editor-field-error">{grid.error}</p>
        : <>
          <div className="editor-meteorology-source-summary">
            <span>{(language === 'zh' ? '行' : 'Rows') + ': ' + grid.rows}</span>
            <span>{(language === 'zh' ? '列' : 'Columns') + ': ' + grid.columns}</span>
            <span>{(language === 'zh' ? '时刻数' : 'Timestep count') + ': ' + grid.times.length}</span>
            {timeId && <span>{(language === 'zh' ? '当前帧' : 'Selected frame') + ': ' + timeId}</span>}
            {component && <span>{(language === 'zh' ? '分量' : 'Component') + ': ' + component}</span>}
            {grid.units && <span>{(language === 'zh' ? '单位' : 'Units') + ': ' + grid.units}</span>}
            {grid.projection && <span>{(language === 'zh' ? '投影' : 'Projection') + ': ' + grid.projection}</span>}
          </div>
          {grid.times.length === 0 && <p className="editor-field-muted">{language === 'zh' ? '已保存格网没有可读时刻。' : 'The saved grid has no readable timesteps.'}</p>}
          {grid.times.length > 0 && <>
            <GriddedDataImporterTimeline
              language={language}
              items={timelineItems}
              selectedId={selectedIndex < 0 ? '' : String(selectedIndex)}
              loading={props.loading}
              disabled={!grid.times.length || !props.canRead}
              onSelect={(item) => selectFrame(item.time, component)}
            />
            <GriddedDataImporterSlider
              language={language}
              times={grid.times}
              components={grid.components}
              selectedTimeId={timeId}
              selectedComponent={component}
              disabled={props.loading || !props.canRead}
              onChange={selectFrame}
            />
            {frameMatchesSelection
              ? <RasterPreviewCanvas language={language} preview={grid.preview!} />
              : <p className="editor-field-muted">{!props.canRead
                ? language === 'zh' ? '当前数据源不支持读取格网帧。' : 'The current data source does not support reading grid frames.'
                : language === 'zh' ? '尚无当前所选时刻与分量的帧预览；读取所选帧后显示。' : 'No preview is available for the selected time and component. Read the selected frame to display it.'}</p>}
          </>}
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
    <canvas ref={canvasRef} style={{ maxWidth: '100%', height: 'auto', imageRendering: 'pixelated', border: '1px solid #dfe5e9', background: '#f6f8f9' }} aria-label={language === 'zh' ? '栅格帧预览' : 'Grid frame preview'} />
    <figcaption style={{ color: '#72808b', fontSize: 10, overflowWrap: 'anywhere' }}>{language === 'zh' ? '边界范围' : 'Extent'}: {preview.minX}, {preview.minY} – {preview.maxX}, {preview.maxY}
      {preview.minimum != null && preview.maximum != null && ` · ${language === 'zh' ? '值域' : 'Range'}: ${preview.minimum} – ${preview.maximum}`}</figcaption>
  </figure>;
}
