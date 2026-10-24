import type { Language } from '../../../shared/workbench';
import type { TerrainElevationCaptureState } from './terrainElevationCapture';
import type { TerrainElevationMeasurementState } from './useTerrainElevationMeasurement';
import './terrain-measurement.css';

function valueLabel(value: number): string {
  return Number.isFinite(value) ? Number(value.toPrecision(3)).toString() : '—';
}

export function TerrainMeasurementReadout({ language, visible, scopeKey, captureState, state }:
  { language: Language; visible: boolean; scopeKey: string; captureState: TerrainElevationCaptureState;
    state: TerrainElevationMeasurementState | null }) {
  if (!visible) return null;
  const zh = language === 'zh';
  const current = state && state.scopeKey === scopeKey &&
    (!captureState.capture || state.captureKey === captureState.capture.key) ? state : null;
  return <section className="map-terrain-measurement" data-testid="map-terrain-measurement"
    aria-label={zh ? '地形高程与末段纵坡' : 'Terrain elevations and final-segment slope'}>
    <strong>{zh ? '地形测距取样' : 'Terrain measurement'}</strong>
    {!captureState.capture && !current && <p className="map-terrain-measurement-note" role="status">{captureState.reason}</p>}
    {captureState.capture && !current && !state && <p className="map-terrain-measurement-note" role="status">{zh
      ? `可从当前可见地形层“${captureState.terrainName ?? ''}”读取末段高程；完成测距后取样。`
      : `Final-segment elevations can be sampled from “${captureState.terrainName ?? ''}” after finishing the measurement.`}</p>}
    {current?.status === 'loading' && <p role="status">{zh ? '正在读取末段两个点的 Terrain 高程…' : 'Reading Terrain elevations at the two final-segment points…'}</p>}
    {current?.status === 'ready' && <dl>
      <div><dt>{zh ? '地形层' : 'Terrain layer'}</dt><dd>{current.terrainName}</dd></div>
      <div><dt>{zh ? '末段长度（地图坐标单位）' : 'Final segment length (map units)'}</dt><dd>{valueLabel(current.segmentLength)}</dd></div>
      <div><dt>{zh ? '最新点高程（Terrain 原始单位）' : 'Latest point elevation (Terrain units)'}</dt><dd>{current.latestElevation === null ? (zh ? '无数据' : 'NoData') : valueLabel(current.latestElevation)}</dd></div>
      <div><dt>{zh ? '前一点高程（Terrain 原始单位）' : 'Previous point elevation (Terrain units)'}</dt><dd>{current.previousElevation === null ? (zh ? '无数据' : 'NoData') : valueLabel(current.previousElevation)}</dd></div>
      <div><dt>{zh ? '末段纵坡（原工具端点顺序）' : 'Final-segment slope (native endpoint order)'}</dt><dd>{current.slope === null
        ? (zh ? '无数据' : 'NoData') : `${valueLabel(current.slope)} ${zh ? '（Terrain 单位 / 地图坐标单位）' : '(Terrain units / map units)'}`}</dd></div>
    </dl>}
    {current?.status === 'ready' && <p className="map-terrain-measurement-note">{zh
      ? '高程来自 Host Terrain 单精度取样；按原工具格式显示 3 位有效数字。单位沿用 Terrain 源值和地图坐标。'
      : 'Elevations use the Host Terrain single-precision samples and are shown to 3 significant digits, as in the original tool. Units remain those of the Terrain source and map coordinates.'}</p>}
    {(current?.status === 'error' || current?.status === 'unavailable') && <div className="map-terrain-measurement-error" role="alert">
      <p>{current.message}</p>
      {current.status === 'error' && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{current.message}</pre></details>}
    </div>}
    {state && !current && <p className="map-terrain-measurement-note" role="status">{zh
      ? '地图或地形来源已变化；旧取样已失效，请重新测距。'
      : 'The map or Terrain source changed; the previous sample is stale. Measure again.'}</p>}
  </section>;
}
