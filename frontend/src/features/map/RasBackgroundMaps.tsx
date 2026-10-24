import { useId } from 'react';
import {
  DEFAULT_BACKGROUND_MAPS_VM_STATE,
  getBackgroundMapRenderStatus,
  hasKnownProjectedMapCoordinates,
  isBackgroundMapDraftDirty,
  shouldOpenStreetMapDraw,
} from './BackgroundMapsVM';
import type { BackgroundMapsVMState } from './BackgroundMapsVM';

export interface RasBackgroundMapsProps {
  state: BackgroundMapsVMState;
  crs: string;
  backendRenderMethodAvailable: boolean;
  overlayRendererConnected: boolean;
  language: 'zh' | 'en';
  onDraftChange: (next: BackgroundMapsVMState) => void;
}

function opacityPercent(value: number): number {
  return Math.round(value / 255 * 100);
}

export function RasBackgroundMaps({
  state,
  crs,
  backendRenderMethodAvailable,
  overlayRendererConnected,
  language,
  onDraftChange,
}: RasBackgroundMapsProps) {
  const id = useId();
  const projectedCoordinatesKnown = hasKnownProjectedMapCoordinates(crs);
  const renderStatus = getBackgroundMapRenderStatus({
    projectionResolved: projectedCoordinatesKnown,
    backendRendererConnected: backendRenderMethodAvailable && overlayRendererConnected,
  });
  const osmRequested = shouldOpenStreetMapDraw(state);
  const labels = language === 'zh'
    ? {
      title: '底图', master: '启用背景地图', osm: '开放街图（OpenStreetMap）', relief: '阴影地形', satellite: '卫星影像', opacity: '卫星影像不透明度',
      reset: '恢复默认', draft: '本地底图设置草稿', projected: '已识别投影坐标', needProjection: '背景地图需要已识别的投影坐标。',
      rendererMissing: backendRenderMethodAvailable ? '服务端已提供相关方法，但当前地图画布没有接入其渲染结果。设置仅作草稿，不会显示底图。' : '当前服务端未提供底图渲染功能。设置仅作草稿，不会显示底图。',
      ready: '渲染器已连接；当前选项仍为本地草稿。', pending: osmRequested ? '已在草稿中请求开启开放街图。' : '背景地图尚未在草稿中启用。',
      notSaved: '此草稿不会写入工程 .vis，也不会改写工程文件。',
    }
    : {
      title: 'Background maps', master: 'Enable background maps', osm: 'OpenStreetMap', relief: 'Shaded relief', satellite: 'Satellite imagery', opacity: 'Satellite imagery opacity',
      reset: 'Restore defaults', draft: 'Local background-map settings draft', projected: 'Projected source coordinates identified', needProjection: 'Background maps require identified projected coordinates.',
      rendererMissing: backendRenderMethodAvailable ? 'The backend method exists, but this map canvas is not connected to a response renderer. These settings remain a draft and no map is shown.' : 'This backend gateway does not expose map.background.render. These settings remain a draft and no map is shown.',
      ready: 'A renderer is connected; the current options are still a local draft.', pending: osmRequested ? 'OpenStreetMap is requested in this draft.' : 'Background maps are not enabled in this draft.',
      notSaved: 'This draft is not written to the project .vis or project files.',
    };
  const projectionLabel = projectedCoordinatesKnown
    ? labels.projected
    : language === 'zh' ? '投影未确认' : 'Projection unresolved';
  const renderDraftLabel = renderStatus === 'ready' ? labels.ready : labels.pending;
  const set = (update: Partial<BackgroundMapsVMState>) => onDraftChange({ ...state, ...update });
  const isDefault = !isBackgroundMapDraftDirty(state);

  return (
    <section className="map-setting-group map-background-panel" aria-labelledby={`${id}-title`}>
      <div className="map-background-heading">
        <h3 id={`${id}-title`}>{labels.title}</h3>
        <span>{labels.draft}</span>
      </div>
      <label className="map-setting-row map-background-master">
        <input type="checkbox" checked={state.AreBackgroundMapsOn} onChange={(event) => set({ AreBackgroundMapsOn: event.currentTarget.checked })} />
        <span>{labels.master}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsOpenStreetMapOn} onChange={(event) => set({ IsOpenStreetMapOn: event.currentTarget.checked })} />
        <span>{labels.osm}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsShadedReliefOn} onChange={(event) => set({ IsShadedReliefOn: event.currentTarget.checked })} />
        <span>{labels.relief}</span>
      </label>
      <label className="map-setting-row">
        <input type="checkbox" checked={state.IsSatelliteImageryOn} onChange={(event) => set({ IsSatelliteImageryOn: event.currentTarget.checked })} />
        <span>{labels.satellite}</span>
      </label>
      <label className="map-background-opacity" htmlFor={`${id}-opacity`}>
        <span>{labels.opacity}<strong>{opacityPercent(state.SatelliteMapOpacity)}%</strong></span>
        <input
          id={`${id}-opacity`}
          type="range"
          min={0}
          max={255}
          step={1}
          value={state.SatelliteMapOpacity}
          onChange={(event) => set({ SatelliteMapOpacity: Math.max(0, Math.min(255, Math.round(Number(event.currentTarget.value)))) })}
          aria-valuetext={`${opacityPercent(state.SatelliteMapOpacity)}%`}
        />
      </label>
      <div className="map-background-status-line" role="status">
        <span className={projectedCoordinatesKnown ? 'map-status-dot is-projected' : 'map-status-dot'} />
        <span>{projectionLabel}</span>
      </div>
      {projectedCoordinatesKnown && renderStatus === 'renderer-unavailable' && <p className="map-background-render-warning">{labels.rendererMissing}</p>}
      {!projectedCoordinatesKnown && <p className="map-background-render-warning">{labels.needProjection}</p>}
      <p className="map-background-render-warning">{renderDraftLabel} {labels.notSaved}</p>
      <button type="button" className="map-background-reset" disabled={isDefault} onClick={() => onDraftChange({ ...DEFAULT_BACKGROUND_MAPS_VM_STATE })}>{labels.reset}</button>
    </section>
  );
}
