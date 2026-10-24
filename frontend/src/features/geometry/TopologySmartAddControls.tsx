import { useMemo, useState } from 'react';
import type {
  GeometryTopologySmartAddOptions,
  GeometryTopologySmartAddTerrainCapture,
  GeometryTopologySmartAddWindow,
} from './geometryTypes';

export type TopologySmartAddOptions = GeometryTopologySmartAddOptions;
export type TopologySmartAddTerrainCapture = GeometryTopologySmartAddTerrainCapture;
export type TopologySmartAddWindow = GeometryTopologySmartAddWindow;

/**
 * `canApply` must be bound by the owner to the same current Geometry/map/draft
 * identity as `terrainCapture` and `measuredWindow`. The window and scale are
 * measured by the map canvas; this control never estimates raster dimensions.
 */
export interface TopologySmartAddControlsProps {
  language: 'zh' | 'en';
  canApply: boolean;
  busy: boolean;
  terrainCapture: GeometryTopologySmartAddTerrainCapture | null;
  measuredWindow: GeometryTopologySmartAddWindow | null;
  mapCellSize: number | null;
  initialOptions?: Partial<GeometryTopologySmartAddOptions>;
  onApply: (options: GeometryTopologySmartAddOptions) => void;
}

export const TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS: GeometryTopologySmartAddOptions = {
  algorithm: 'HessianRidgeness',
  dpTolerancePixels: 1.5,
  maxSegmentPixels: 1500,
  previewThrottlePixels: 1.5,
  highGroundAlpha: 3,
  highGroundBailRatio: 5,
  highGroundSnapRadiusCells: 15,
  hessianFrangiBeta: 0.5,
  hessianFeaturePenaltyKappa: 3,
  hessianOffFeatureBailMultiplier: 100,
  hessianMinExpansionsBudget: 250000,
  hessianExpansionsPerEuclidPx: 500,
  hessianMaxEuclidCells: 1500,
};

type NumericOptionKey = Exclude<keyof GeometryTopologySmartAddOptions, 'algorithm' | 'previewThrottlePixels'>;
export type TopologySmartAddOptionsDraft = { algorithm: string; previewThrottlePixels: string } & Record<NumericOptionKey, string>;
export type TopologySmartAddOptionErrors = Partial<Record<keyof GeometryTopologySmartAddOptions, string>>;

export interface TopologySmartAddOptionsParseResult {
  options: GeometryTopologySmartAddOptions | null;
  errors: TopologySmartAddOptionErrors;
}

interface NumberFieldDescriptor {
  key: NumericOptionKey;
  zh: string;
  en: string;
  descriptionZh: string;
  descriptionEn: string;
  integer?: boolean;
  minimum: number;
  inclusive: boolean;
}

const NUMBER_FIELDS: NumberFieldDescriptor[] = [
  { key: 'dpTolerancePixels', zh: '简化容差（px）', en: 'Simplification tolerance (px)', descriptionZh: '对路径结果执行 Douglas–Peucker 简化，单位为屏幕像素。', descriptionEn: 'Douglas–Peucker tolerance applied to the path, in screen pixels.', minimum: 0, inclusive: true },
  { key: 'maxSegmentPixels', zh: '最大线段长度（px）', en: 'Maximum segment length (px)', descriptionZh: '端点间直线距离超过此像素值时，智能路径不执行。', descriptionEn: 'Smart path is unavailable when the straight endpoint distance exceeds this pixel value.', minimum: 0, inclusive: false },
  { key: 'highGroundAlpha', zh: 'HighGround 脊线权重 α', en: 'HighGround ridge weight α', descriptionZh: '0 表示直线偏好；数值越大越倾向沿高地脊线。', descriptionEn: 'Zero prefers straight paths; higher values bias the path toward ridges.', minimum: 0, inclusive: true },
  { key: 'highGroundBailRatio', zh: 'HighGround 搜索退出比例', en: 'HighGround search bail ratio', descriptionZh: '相对于端点直线距离的搜索退出倍率。', descriptionEn: 'Search bail multiplier relative to the straight endpoint distance.', minimum: 0, inclusive: false },
  { key: 'highGroundSnapRadiusCells', zh: 'HighGround 捕捉半径（网格）', en: 'HighGround snap radius (cells)', descriptionZh: '将目标捕捉至附近最高网格的搜索半径。', descriptionEn: 'Search radius used to snap the target to a nearby highest grid cell.', integer: true, minimum: 0, inclusive: true },
  { key: 'hessianFrangiBeta', zh: 'Hessian Frangi β', en: 'Hessian Frangi β', descriptionZh: 'Frangi 各向异性权重；较低值会更强地惩罚片状响应。', descriptionEn: 'Frangi anisotropy weight; lower values penalize plate-like responses more strongly.', minimum: 0, inclusive: false },
  { key: 'hessianFeaturePenaltyKappa', zh: 'Hessian 特征外惩罚 κ', en: 'Hessian off-feature penalty κ', descriptionZh: 'A* 特征外代价乘数，代价为步长 × (1 + κ × (1 − V̂))。', descriptionEn: 'A* off-feature edge-cost multiplier: step × (1 + κ × (1 − V̂)).', minimum: 0, inclusive: true },
  { key: 'hessianOffFeatureBailMultiplier', zh: 'Hessian 特征外退出倍率', en: 'Hessian off-feature bail multiplier', descriptionZh: '限制路径在特征外绕行的欧氏距离倍率。', descriptionEn: 'Multiplier that limits off-feature meandering relative to Euclidean distance.', minimum: 0, inclusive: false },
  { key: 'hessianMinExpansionsBudget', zh: 'Hessian 最小扩展预算', en: 'Hessian minimum expansion budget', descriptionZh: '与端点距离无关的 A* 扩展次数下限。', descriptionEn: 'Floor on the A* expansion budget regardless of endpoint distance.', integer: true, minimum: 1, inclusive: true },
  { key: 'hessianExpansionsPerEuclidPx', zh: 'Hessian 每像素扩展预算', en: 'Hessian expansions per Euclidean pixel', descriptionZh: '按端点欧氏像素距离增加的 A* 扩展预算。', descriptionEn: 'Additional A* expansion budget per Euclidean pixel.', integer: true, minimum: 1, inclusive: true },
  { key: 'hessianMaxEuclidCells', zh: 'Hessian 最大直线距离（网格）', en: 'Hessian maximum straight distance (cells)', descriptionZh: '在构建特征表面前允许的最大直线距离。', descriptionEn: 'Maximum straight-line distance allowed before building the feature surface.', integer: true, minimum: 1, inclusive: true },
];

const INTEGER_PATTERN = /^[+-]?\d+$/;
const DECIMAL_PATTERN = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;
const INT32_MAX = 2_147_483_647;

function makeOptionsDraft(options: GeometryTopologySmartAddOptions): TopologySmartAddOptionsDraft {
  return {
    algorithm: options.algorithm,
    dpTolerancePixels: String(options.dpTolerancePixels),
    maxSegmentPixels: String(options.maxSegmentPixels),
    // This source option only throttles live pointer-move previews in the C# UI.
    // The current Host computes a SmartAdd path only on the endpoint click.
    previewThrottlePixels: String(TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS.previewThrottlePixels),
    highGroundAlpha: String(options.highGroundAlpha),
    highGroundBailRatio: String(options.highGroundBailRatio),
    highGroundSnapRadiusCells: String(options.highGroundSnapRadiusCells),
    hessianFrangiBeta: String(options.hessianFrangiBeta),
    hessianFeaturePenaltyKappa: String(options.hessianFeaturePenaltyKappa),
    hessianOffFeatureBailMultiplier: String(options.hessianOffFeatureBailMultiplier),
    hessianMinExpansionsBudget: String(options.hessianMinExpansionsBudget),
    hessianExpansionsPerEuclidPx: String(options.hessianExpansionsPerEuclidPx),
    hessianMaxEuclidCells: String(options.hessianMaxEuclidCells),
  };
}

/** Parses and validates the exact numeric SmartAppend options accepted by the Host. */
export function parseTopologySmartAddOptions(
  draft: TopologySmartAddOptionsDraft,
  language: 'zh' | 'en' = 'zh',
): TopologySmartAddOptionsParseResult {
  const errors: TopologySmartAddOptionErrors = {};
  if (draft.algorithm !== 'HessianRidgeness' && draft.algorithm !== 'HighGround') {
    errors.algorithm = language === 'zh' ? '请选择受支持的算法。' : 'Choose a supported algorithm.';
  }

  const values = {} as Record<NumericOptionKey, number>;
  for (const field of NUMBER_FIELDS) {
    const raw = draft[field.key].trim();
    const matchesPattern = field.integer ? INTEGER_PATTERN.test(raw) : DECIMAL_PATTERN.test(raw);
    const value = matchesPattern ? Number(raw) : Number.NaN;
    const inRange = Number.isFinite(value) && (field.inclusive ? value >= field.minimum : value > field.minimum)
      && (!field.integer || Number.isSafeInteger(value) && value <= INT32_MAX);
    if (!inRange) {
      errors[field.key] = field.integer
        ? language === 'zh'
          ? `请输入不小于 ${field.minimum} 的有效整数。`
          : `Enter a valid integer greater than or equal to ${field.minimum}.`
        : language === 'zh'
          ? field.inclusive ? `请输入大于或等于 ${field.minimum} 的有限数值。` : `请输入大于 ${field.minimum} 的有限数值。`
          : field.inclusive ? `Enter a finite number greater than or equal to ${field.minimum}.` : `Enter a finite number greater than ${field.minimum}.`;
      continue;
    }
    values[field.key] = value;
  }

  if (Object.keys(errors).length > 0) return { options: null, errors };
  return {
    options: {
      algorithm: draft.algorithm as GeometryTopologySmartAddOptions['algorithm'],
      previewThrottlePixels: TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS.previewThrottlePixels,
      ...values,
    },
    errors,
  };
}

function hasTerrainIdentity(capture: GeometryTopologySmartAddTerrainCapture | null): capture is GeometryTopologySmartAddTerrainCapture {
  return !!capture && [capture.geometryAssetPath, capture.geometryRevision, capture.projectPath,
    capture.canonicalTerrainPath, capture.terrainName, capture.sourceRevision].every((value) => typeof value === 'string' && value.trim().length > 0);
}

function hasMeasuredWindow(window: GeometryTopologySmartAddWindow | null): window is GeometryTopologySmartAddWindow {
  if (!window || !window.extent || !Number.isSafeInteger(window.rows) || !Number.isSafeInteger(window.columns)) return false;
  const { minX, minY, maxX, maxY } = window.extent;
  return window.rows >= 2 && window.columns >= 2 && window.rows * window.columns <= 8_000_000
    && Number.isFinite(minX) && Number.isFinite(minY) && Number.isFinite(maxX) && Number.isFinite(maxY)
    && maxX > minX && maxY > minY;
}

function formatValue(value: number): string {
  return Number.isFinite(value) ? String(value) : '—';
}

export function TopologySmartAddControls({
  language,
  canApply,
  busy,
  terrainCapture,
  measuredWindow,
  mapCellSize,
  initialOptions,
  onApply,
}: TopologySmartAddControlsProps) {
  const text = language === 'zh';
  const [draft, setDraft] = useState<TopologySmartAddOptionsDraft>(() => makeOptionsDraft({
    ...TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS,
    ...initialOptions,
  }));
  const parsed = useMemo(() => parseTopologySmartAddOptions(draft, language), [draft, language]);
  const terrainReady = hasTerrainIdentity(terrainCapture);
  const windowReady = hasMeasuredWindow(measuredWindow);
  const cellSizeReady = Number.isFinite(mapCellSize) && (mapCellSize ?? 0) > 0;
  const canStart = canApply && !busy && terrainReady && windowReady && cellSizeReady && parsed.options !== null;
  const setOption = (key: keyof TopologySmartAddOptionsDraft, value: string): void => {
    setDraft((current) => ({ ...current, [key]: value }));
  };
  const status = !terrainReady
    ? (text ? '当前没有可用的真实关联 Terrain 捕获；请先加载已关联且可采样的地形。' : 'No current associated Terrain capture is available; load an associated Terrain sampler first.')
    : !windowReady
      ? (text ? '尚无有效的实测地图窗口；请等待地图相机和画布尺寸就绪。' : 'No valid measured map window is available; wait for the map camera and canvas size.')
      : !cellSizeReady
        ? (text ? '当前地图比例尚未就绪。' : 'The current map scale is not ready.')
        : busy
          ? (text ? '地图操作进行中。' : 'A map operation is in progress.')
          : !canApply
            ? (text ? '当前几何拓扑草稿尚不可编辑。' : 'The current Geometry topology draft is not editable yet.')
            : (text ? '应用参数后，在地图上依次点选起点和终点；路径将由当前地形计算并写入未保存的几何草稿。' : 'Apply the options, then click a start and end point on the map. The current Terrain computes the path into the unsaved Geometry draft.');

  const terrainName = terrainReady ? terrainCapture.terrainName : null;
  const terrainPath = terrainReady ? terrainCapture.canonicalTerrainPath : null;
  const sourceRevision = terrainReady ? terrainCapture.sourceRevision : null;

  return (
    <section className="geometry-panel geometry-topology-smart-add" data-testid="geometry-topology-smart-add-controls"
      aria-label={text ? '智能追加路径设置' : 'Smart append path settings'}>
      <header className="geometry-panel-heading">
        <div>
          <h3>{text ? '智能追加弧段' : 'Smart append arc'}</h3>
          <p>{text ? '使用当前几何的关联 Terrain，为每段端点生成顺地形路径。结果仅进入拓扑草稿。' : 'Use the associated Terrain for each endpoint pair. The result is added to the topology draft.'}</p>
        </div>
      </header>

      <div className="geometry-panel-body" style={{ display: 'grid', gap: 10 }}>
        <section aria-label={text ? '当前地形捕获' : 'Current Terrain capture'} data-testid="smart-add-terrain-capture"
          style={{ border: '1px solid #d8dee5', borderRadius: 6, padding: 10 }}>
          <strong>{text ? '关联地形来源' : 'Associated Terrain source'}</strong>
          {terrainReady ? <dl style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', gap: '4px 10px', margin: '8px 0 0' }}>
            <dt>{text ? '名称' : 'Name'}</dt><dd style={{ margin: 0 }}>{terrainName}</dd>
            <dt>{text ? '路径' : 'Path'}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }} title={terrainPath ?? undefined}>{terrainPath}</dd>
            <dt>{text ? '源修订' : 'Source revision'}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{sourceRevision}</dd>
          </dl> : <p role="status" style={{ margin: '6px 0 0' }}>{text ? '没有可用捕获，不能启用智能追加。' : 'No current capture is available; Smart Add is disabled.'}</p>}
        </section>

        <section aria-label={text ? '实测地图窗口' : 'Measured map window'} data-testid="smart-add-measured-window"
          style={{ border: '1px solid #d8dee5', borderRadius: 6, padding: 10 }}>
          <strong>{text ? '当前实测地图窗口' : 'Current measured map window'}</strong>
          {windowReady ? <div style={{ display: 'grid', gap: 4, marginTop: 6 }}>
            <span>{text ? '范围' : 'Extent'}: X {formatValue(measuredWindow.extent.minX)}–{formatValue(measuredWindow.extent.maxX)}, Y {formatValue(measuredWindow.extent.minY)}–{formatValue(measuredWindow.extent.maxY)}</span>
            <span>{text ? '采样网格' : 'Sampling grid'}: {measuredWindow.rows} × {measuredWindow.columns}</span>
            <span>{text ? '比例' : 'Scale'}: {cellSizeReady ? formatValue(mapCellSize!) : '—'} {text ? '源坐标单位/屏幕像素' : 'source units/screen pixel'}</span>
          </div> : <p role="status" style={{ margin: '6px 0 0' }}>{text ? '等待真实地图相机和画布尺寸测量；行列数不会由表单估算。' : 'Waiting for the real map camera and measured canvas size; dimensions are not estimated in this form.'}</p>}
        </section>

        <fieldset disabled={busy} style={{ border: '1px solid #d8dee5', borderRadius: 6, padding: 10 }}>
          <legend>{text ? '路径算法' : 'Path algorithm'}</legend>
          <div data-testid="smart-add-field-previewThrottlePixels" aria-label={text ? '预览节流参数只读说明' : 'Preview throttle read-only explanation'}
            style={{ border: '1px solid #d8dee5', borderRadius: 6, padding: 8, marginBottom: 10 }}>
            <strong>{text ? '预览节流距离（px）：' : 'Preview throttle distance (px): '}{TOPOLOGY_SMART_ADD_DEFAULT_OPTIONS.previewThrottlePixels} ({text ? '源默认值，只读' : 'source default, read-only'})</strong>
            <p style={{ margin: '4px 0 0' }}>{text
              ? '当前界面尚未接入鼠标移动路径预览；服务仅在终点点击时计算最终路径。该参数目前不适用，提交时仍保留源默认值。'
              : 'Live pointer-move path preview is not connected; the service computes the final path only on the endpoint click. This option is not currently applicable and remains at its source default in requests.'}</p>
          </div>
          <label style={{ display: 'grid', gap: 4, marginBottom: 8 }}>
            <span>{text ? '算法' : 'Algorithm'}</span>
            <select data-testid="smart-add-field-algorithm" value={draft.algorithm} onChange={(event) => setOption('algorithm', event.currentTarget.value)}
              aria-invalid={Boolean(parsed.errors.algorithm)}>
              <option value="HessianRidgeness">HessianRidgeness</option>
              <option value="HighGround">HighGround</option>
            </select>
            <small>{text ? 'HessianRidgeness 对脊线和谷线极性不敏感；HighGround 偏向高地脊线。' : 'HessianRidgeness is polarity-agnostic; HighGround biases toward ridges.'}</small>
            {parsed.errors.algorithm && <small role="alert">{parsed.errors.algorithm}</small>}
          </label>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 8 }}>
            {NUMBER_FIELDS.map((field) => {
              const error = parsed.errors[field.key];
              const fieldID = `smart-add-${field.key}`;
              return <label key={field.key} htmlFor={fieldID} style={{ display: 'grid', gap: 4 }}>
                <span>{text ? field.zh : field.en}</span>
                <input id={fieldID} data-testid={`smart-add-field-${field.key}`} type="text"
                  inputMode={field.integer ? 'numeric' : 'decimal'} value={draft[field.key]}
                  aria-invalid={Boolean(error)} aria-describedby={error ? `${fieldID}-description ${fieldID}-error` : `${fieldID}-description`}
                  onChange={(event) => setOption(field.key, event.currentTarget.value)} />
                <small id={`${fieldID}-description`}>{text ? field.descriptionZh : field.descriptionEn}</small>
                {error && <small id={`${fieldID}-error`} role="alert">{text ? `无效数值：${error}` : `Invalid value: ${error}`}</small>}
              </label>;
            })}
          </div>
        </fieldset>

        <div role="status" aria-live="polite" data-testid="smart-add-status">{status}</div>
        <button type="button" data-testid="geometry-topology-smart-add-apply" disabled={!canStart}
          onClick={() => { if (canStart && parsed.options) onApply(parsed.options); }}>
          {text ? '应用参数并启用智能追加' : 'Apply options and enable Smart Add'}
        </button>
      </div>
    </section>
  );
}

export default TopologySmartAddControls;
