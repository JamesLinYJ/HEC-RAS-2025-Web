import { useId } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import {
  DEFAULT_BACKGROUND_MAPS_VM_STATE,
  hasKnownProjectedMapCoordinates,
  isBackgroundMapDraftDirty,
} from './BackgroundMapsVM';
import type { BackgroundMapsVMState } from './BackgroundMapsVM';
import type { BackgroundMapRenderingState, BackgroundMapSourceStatus } from './background/types';
import { OSMStylePicker } from './OSMStylePicker';
import './backgroundMaps.css';

export interface RasBackgroundMapsProps {
  state: BackgroundMapsVMState;
  crs: string;
  backend: BackendGateway;
  backendRevision?: number;
  backendRenderMethodAvailable: boolean;
  overlayRendererConnected: boolean;
  rendering?: BackgroundMapRenderingState;
  language: Language;
  onDraftChange: (next: BackgroundMapsVMState) => void;
}

function opacityPercent(value: number): number {
  return Math.round(value / 255 * 100);
}

function sourceName(source: BackgroundMapSourceStatus, language: Language): string {
  return source.id === 'osm' ? text(language, '开放街图', 'OpenStreetMap')
    : source.id === 'satellite' ? text(language, '卫星影像', 'Satellite imagery')
    : text(language, '阴影地形', 'Shaded relief');
}

function sourceState(source: BackgroundMapSourceStatus, language: Language): string {
  return source.state === 'available' ? text(language, '来源可用', 'Available')
    : source.state === 'failed' ? text(language, '来源加载失败', 'Source failed')
    : source.state === 'outsideCoverage' ? text(language, '当前范围无覆盖', 'Outside coverage')
    : text(language, '未启用', 'Disabled');
}

export function RasBackgroundMaps({
  state,
  crs,
  backend,
  backendRevision,
  backendRenderMethodAvailable,
  overlayRendererConnected,
  rendering,
  language,
  onDraftChange,
}: RasBackgroundMapsProps) {
  const id = useId();
  const projectedCoordinatesKnown = rendering?.projectionResolved ?? hasKnownProjectedMapCoordinates(crs);
  const rendererConnected = backendRenderMethodAvailable && overlayRendererConnected
    && (rendering?.rendererConnected ?? true);
  const sourceSelected = state.IsOpenStreetMapOn || state.IsSatelliteImageryOn || state.IsShadedReliefOn;
  const enabled = state.AreBackgroundMapsOn && sourceSelected;
  const status = !enabled ? 'disabled' : !rendererConnected ? 'renderer-unavailable'
    : rendering?.status ?? (projectedCoordinatesKnown ? 'waiting' : 'projection-required');
  const hasRenderedFrame = rendererConnected && Boolean(rendering?.frame);
  const sources = enabled ? rendering?.sources ?? [] : [];
  const noCoverage = sources.some(source => source.state === 'outsideCoverage')
    && !hasRenderedFrame && !sources.some(source => source.state === 'failed');
  const summary = status === 'disabled' ? !state.AreBackgroundMapsOn
    ? text(language, '背景地图已关闭。', 'Background maps are off.')
    : text(language, '请选择至少一个底图来源。', 'Select at least one background source.')
    : status === 'renderer-unavailable' ? text(language, '底图渲染器尚未就绪。', 'The background renderer is not ready.')
    : status === 'loading' ? text(language, '正在加载并绘制当前底图…', 'Loading and rendering the current background…')
    : status === 'ready' && hasRenderedFrame ? text(language, '当前底图已渲染。', 'The current background has been rendered.')
    : status === 'partial' && hasRenderedFrame ? text(language, '部分底图已渲染，请查看来源状态。', 'Part of the background has been rendered. Check the source status.')
    : status === 'error' ? text(language, '底图加载失败，可重试。', 'Background loading failed. You can retry.')
    : status === 'cancelled' ? text(language, '已取消当前底图加载。', 'Current background loading was cancelled.')
    : noCoverage ? text(language, '所选底图在当前范围没有覆盖。', 'Selected background sources do not cover this extent.')
    : status === 'projection-required' ? text(language, '需要确认工程投影后才能绘制底图。', 'The project projection must be resolved before rendering.')
    : status === 'blocked' ? text(language, '当前底图暂时无法绘制。', 'The current background cannot be rendered yet.')
    : text(language, '等待绘制当前底图。', 'Waiting to render the current background.');
  const rendererMessage = !backendRenderMethodAvailable
    ? text(language, '当前服务未提供底图绘制功能。', 'This service does not provide background rendering.')
    : text(language, '地图画布尚未连接底图渲染结果。', 'The map canvas is not connected to the background render result.');
  const set = (update: Partial<BackgroundMapsVMState>) => onDraftChange({ ...state, ...update });
  const isDefault = !isBackgroundMapDraftDirty(state);

  return (
    <section className="map-setting-group map-background-panel" aria-labelledby={`${id}-title`} data-testid="background-map-settings">
      <div className="map-background-heading">
        <h3 id={`${id}-title`}>{text(language, '底图', 'Background maps')}</h3>
        <span>{text(language, '本地视图设置', 'Local view settings')}</span>
      </div>
      <label className="map-setting-row map-background-master">
        <input type="checkbox" checked={state.AreBackgroundMapsOn} onChange={event => set({ AreBackgroundMapsOn: event.currentTarget.checked })} />
        <span>{text(language, '启用背景地图', 'Enable background maps')}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsOpenStreetMapOn} onChange={event => set({ IsOpenStreetMapOn: event.currentTarget.checked })} />
        <span>{text(language, '开放街图（OpenStreetMap）', 'OpenStreetMap')}</span>
      </label>
      <OSMStylePicker backend={backend} backendRevision={backendRevision} language={language}
        state={state} onChange={set} onCacheCleared={rendering?.refresh} />
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsShadedReliefOn} onChange={event => set({ IsShadedReliefOn: event.currentTarget.checked })} />
        <span>{text(language, '阴影地形', 'Shaded relief')}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsSatelliteImageryOn} onChange={event => set({ IsSatelliteImageryOn: event.currentTarget.checked })} />
        <span>{text(language, '卫星影像', 'Satellite imagery')}</span>
      </label>
      <label className="map-background-opacity" htmlFor={`${id}-opacity`}>
        <span>{text(language, '卫星影像不透明度', 'Satellite imagery opacity')}<strong>{opacityPercent(state.SatelliteMapOpacity)}%</strong></span>
        <input id={`${id}-opacity`} type="range" min={0} max={255} step={1} value={state.SatelliteMapOpacity}
          onChange={event => set({ SatelliteMapOpacity: Math.max(0, Math.min(255, Math.round(Number(event.currentTarget.value)))) })}
          aria-valuetext={`${opacityPercent(state.SatelliteMapOpacity)}%`} />
      </label>
      <div className="map-background-status-line">
        <span className={projectedCoordinatesKnown ? 'map-status-dot is-projected' : 'map-status-dot'} aria-hidden="true" />
        <span>{projectedCoordinatesKnown ? text(language, '工程投影已确认', 'Project projection resolved')
          : status === 'loading' ? text(language, '正在确认工程投影…', 'Resolving project projection…')
          : text(language, '工程投影未确认', 'Project projection unresolved')}</span>
      </div>
      <div className="map-background-render-state" data-state={status} aria-busy={status === 'loading'}>
        <p role="status" data-testid="background-map-render-status">{summary}</p>
        {enabled && status === 'renderer-unavailable' && <p>{rendererMessage}</p>}
        {enabled && rendering?.message && status !== 'renderer-unavailable' && <p className="map-background-render-detail">{rendering.message}</p>}
        {sources.length > 0 && <ul className="map-background-sources" aria-label={text(language, '底图来源状态', 'Background source status')}>
          {sources.map(source => <li key={source.id} data-state={source.state}>
            <span>{sourceName(source, language)}</span><strong>{sourceState(source, language)}</strong>
            {source.message && <details>
              <summary>{text(language, '来源诊断', 'Source diagnostics')}</summary><p>{source.message}</p>
            </details>}
          </li>)}
        </ul>}
        {enabled && rendering && <div className="map-background-render-actions">
          {status === 'loading'
            ? <button type="button" onClick={rendering.cancel}>{text(language, '取消加载', 'Cancel loading')}</button>
            : <button type="button" disabled={!rendererConnected} onClick={rendering.refresh}>{hasRenderedFrame
              ? text(language, '刷新底图', 'Refresh background') : text(language, '重试底图', 'Retry background')}</button>}
        </div>}
      </div>
      <p className="map-background-local-note">{text(language, '这些设置仅用于当前本地视图，不写入工程 .vis 文件。',
        'These settings apply to the current local view and are not written to the project .vis file.')}</p>
      <button type="button" className="map-background-reset" disabled={isDefault}
        onClick={() => onDraftChange({ ...DEFAULT_BACKGROUND_MAPS_VM_STATE })}>{text(language, '恢复默认', 'Restore defaults')}</button>
    </section>
  );
}
