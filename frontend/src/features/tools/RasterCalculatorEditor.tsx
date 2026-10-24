import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { errorDiagnostics, text } from '../../shared/workbench';
import { loadMapDocumentCatalog, projectXmlSha } from '../map/ProjectTreeListener';
import type { LoadedMapDocumentCatalog } from '../map/ProjectTreeListener';
import type { MapDocumentTab } from '../map/mapTypes';
import { rgbaFrameImage } from '../map/mapImages';
import { RasterCalculatorConstantRecipe } from './RasterCalculatorConstantRecipe';
import { RasterCalculatorInputRecipe } from './RasterCalculatorInputRecipe';
import {
  buildRasterCalculatorMapRequest,
  calculatorSourceExtent,
  defaultRasterCalculatorVariableName,
  isRecord,
  isSha256,
  normalizeRasterCalculatorKeyFrame,
  parseRasterCalculatorEvaluation,
  parseRasterCalculatorInspection,
  rasterCalculatorOwner,
  validateRasterCalculatorRecipe,
} from './RasterCalculatorRecipe';
import type {
  RasterCalculatorConstantDraft,
  RasterCalculatorEvaluateResult,
  RasterCalculatorInputDraft,
  RasterCalculatorMapCapture,
  RasterMapViewExtent,
} from './RasterCalculatorRecipe';

type Props = FeaturePageProps;
type Operation = 'maps' | 'inspect' | 'evaluate' | null;
type ViewportMode = 'source' | 'explicit';
type BoundsDraft = { minX: string; minY: string; maxX: string; maxY: string };

const supportedMapTypes = new Set(['Project', 'Buildable', 'Geometry', 'Plan']);
const defaultPixelSize = '256';

function mapKey(map: MapDocumentTab): string {
  return JSON.stringify([map.mapIndex, map.name, map.mapType]);
}

function showRawError(caught: unknown): string {
  const details = errorDiagnostics(caught);
  try { return typeof details === 'string' ? details : JSON.stringify(details, null, 2); }
  catch { return String(details); }
}

function finiteBound(value: string): number | null {
  if (!value.trim()) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function selectedViewport(
  mode: ViewportMode,
  bounds: BoundsDraft,
  sourceExtent: ReturnType<typeof calculatorSourceExtent>,
  pixelWidthText: string,
  pixelHeightText: string,
  language: FeaturePageProps['language'],
): { viewport?: RasterMapViewExtent; error?: string } {
  const say = (zh: string, en: string): string => text(language, zh, en);
  const pixelWidth = Number(pixelWidthText);
  const pixelHeight = Number(pixelHeightText);
  if (!Number.isInteger(pixelWidth) || !Number.isInteger(pixelHeight) || pixelWidth < 1 || pixelHeight < 1 ||
      pixelWidth > 1536 || pixelHeight > 1536 || pixelWidth * pixelHeight > 2_097_152) {
    return { error: say('预览像元宽、高必须是 1–1536 的整数，且总像元数不能超过 2,097,152。', 'Preview width and height must be integers from 1 to 1536, with no more than 2,097,152 total pixels.') };
  }
  const extent = mode === 'source' ? sourceExtent : {
    minX: finiteBound(bounds.minX), minY: finiteBound(bounds.minY),
    maxX: finiteBound(bounds.maxX), maxY: finiteBound(bounds.maxY),
  };
  if (!extent) return { error: say('当前输入源没有可用的共同范围；可改用手动有限世界坐标范围。', 'The selected inputs have no shared valid extent. Enter finite world-coordinate bounds instead.') };
  const { minX, minY, maxX, maxY } = extent;
  if (typeof minX !== 'number' || typeof minY !== 'number' || typeof maxX !== 'number' || typeof maxY !== 'number' ||
      ![minX, minY, maxX, maxY].every(Number.isFinite) || minX >= maxX || minY >= maxY) {
    return { error: say('视口范围必须是有限世界坐标，并满足最小值小于最大值。', 'Viewport bounds must be finite world coordinates with each minimum below its maximum.') };
  }
  return { viewport: { minX, minY, maxX, maxY, pixelWidth, pixelHeight } };
}

function RasterCalculatorCanvas({ result, language }: { result: RasterCalculatorEvaluateResult; language: FeaturePageProps['language'] }) {
  const image = useMemo(() => rgbaFrameImage(result.layer.rgba, result.layer.width, result.layer.height),
    [result.layer.rgba, result.layer.width, result.layer.height]);
  return <img data-testid="raster-calculator-rgba-preview" src={image}
    aria-label={text(language, `Host 栅格计算预览，${result.layer.width} × ${result.layer.height} 像元`,
      `Host raster calculation preview, ${result.layer.width} by ${result.layer.height} pixels`)}
    alt={text(language, `Host 栅格计算预览，${result.layer.width} × ${result.layer.height} 像元`,
      `Host raster calculation preview, ${result.layer.width} by ${result.layer.height} pixels`)}
    style={{ display: 'block', width: '100%', maxHeight: '560px', objectFit: 'contain', imageRendering: 'pixelated' }} />;
}

export function RasterCalculatorEditor({ language, context, backend, onDirtyStateChange }: Props) {
  const label = (zhText: string, enText: string): string => text(language, zhText, enText);
  const defaultLayerName = label('栅格计算器预览', 'Raster calculator preview');
  const owner = rasterCalculatorOwner(context);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const previousDefaultLayerName = useRef(defaultLayerName);
  const [mapCatalog, setMapCatalog] = useState<LoadedMapDocumentCatalog | null>(null);
  const [selectedMapKey, setSelectedMapKey] = useState('');
  const [capture, setCapture] = useState<RasterCalculatorMapCapture | null>(null);
  const [inputs, setInputs] = useState<RasterCalculatorInputDraft[]>([]);
  const [constants, setConstants] = useState<RasterCalculatorConstantDraft[]>([]);
  const [expression, setExpression] = useState('');
  const [noDataBehavior, setNoDataBehavior] = useState('WriteNDV');
  const [layerName, setLayerName] = useState(defaultLayerName);
  const [viewportMode, setViewportMode] = useState<ViewportMode>('source');
  const [bounds, setBounds] = useState<BoundsDraft>({ minX: '', minY: '', maxX: '', maxY: '' });
  const [pixelWidth, setPixelWidth] = useState(defaultPixelSize);
  const [pixelHeight, setPixelHeight] = useState(defaultPixelSize);
  const [profileIndex, setProfileIndex] = useState(0);
  const [preview, setPreview] = useState<RasterCalculatorEvaluateResult | null>(null);
  const [operation, setOperation] = useState<Operation>(null);
  const [error, setError] = useState<string>();
  const [diagnostic, setDiagnostic] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const hasProject = Boolean(context.projectPath && context.projectRevision && context.projectXml?.trim() && context.projectSessionID);
  const inspectAvailable = backend.supports('raster.calculator.inspect');
  const evaluateAvailable = backend.supports('raster.calculator.evaluate');
  const selectedMap = mapCatalog?.documents.find(document => mapKey(document) === selectedMapKey) ?? null;
  const sourceExtent = useMemo(() => capture ? calculatorSourceExtent(inputs, capture.sources) : null, [capture, inputs]);
  const recipeValidation = useMemo(() => capture ? validateRasterCalculatorRecipe({ expression, noDataBehavior, inputs, constants }, capture, language) : {},
    [capture, expression, noDataBehavior, inputs, constants, language]);
  const viewportValidation = useMemo(() => selectedViewport(viewportMode, bounds, sourceExtent, pixelWidth, pixelHeight, language),
    [viewportMode, bounds, sourceExtent, pixelWidth, pixelHeight, language]);
  const layerNameValid = Boolean(layerName.trim());
  const dirty = expression !== '' || inputs.length > 0 || constants.length > 0 ||
    (capture ? noDataBehavior !== capture.defaultNoDataBehavior : noDataBehavior !== 'WriteNDV') ||
    layerName !== defaultLayerName || viewportMode === 'explicit' || pixelWidth !== defaultPixelSize || pixelHeight !== defaultPixelSize;
  const canEvaluate = Boolean(capture && capture.owner === owner && evaluateAvailable && !operation && !recipeValidation.error &&
    viewportValidation.viewport && layerNameValid && selectedMap && mapKey(selectedMap) === selectedMapKey &&
    selectedMap.visualizationRevision?.toUpperCase() === capture.visualizationRevision.toUpperCase() &&
    capture.projectRevision.toUpperCase() === context.projectRevision?.toUpperCase() &&
    capture.mapName === selectedMap.name && capture.mapIndex === selectedMap.mapIndex && capture.mapType === selectedMap.mapType);

  useEffect(() => {
    setLayerName(current => current === previousDefaultLayerName.current ? defaultLayerName : current);
    previousDefaultLayerName.current = defaultLayerName;
  }, [defaultLayerName]);

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort(); controller.current = null;
    setMapCatalog(null); setSelectedMapKey(''); setCapture(null); setInputs([]); setConstants([]);
    setExpression(''); setNoDataBehavior('WriteNDV'); setLayerName(defaultLayerName);
    setViewportMode('source'); setBounds({ minX: '', minY: '', maxX: '', maxY: '' });
    setPixelWidth(defaultPixelSize); setPixelHeight(defaultPixelSize); setProfileIndex(0); setPreview(null);
    setOperation(null); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
  }, [owner]);

  useEffect(() => {
    onDirtyStateChange?.('tools.raster-calculator', dirty);
    return () => onDirtyStateChange?.('tools.raster-calculator', false);
  }, [dirty, onDirtyStateChange]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort(); controller.current = null;
  }, []);

  const cancelCurrent = (): void => {
    generation.current += 1;
    controller.current?.abort(); controller.current = null;
    setOperation(null);
    setPreview(null);
    setNotice(label('已取消当前读取或计算；未应用任何地图更改。', 'The current read or evaluation was cancelled; no map changes were applied.'));
  };

  const invalidatePreview = (): void => {
    if (controller.current) {
      generation.current += 1;
      controller.current.abort(); controller.current = null;
    }
    setOperation(null);
    setPreview(null);
    setError(undefined); setDiagnostic(undefined); setNotice(undefined);
  };

  const readMapCatalog = async (): Promise<void> => {
    invalidatePreview();
    setCapture(null); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    if (!context.projectPath || !context.projectRevision || !context.projectXml?.trim() || !context.projectSessionID) {
      setError(label('请先打开带有当前 XML 快照和工程修订的已保存工程。', 'Open a saved project with its current XML snapshot and revision first.'));
      return;
    }
    if (!backend.supports('map.document.list') || !backend.supports('map.tree.load')) {
      setError(label('当前服务未开放保存地图目录和地图树读取。', 'The service does not expose saved-map and map-tree reads.'));
      return;
    }
    const requestOwner = owner;
    const previousKey = selectedMapKey;
    const requestGeneration = ++generation.current;
    const aborter = new AbortController(); controller.current = aborter;
    setOperation('maps'); setMapCatalog(null);
    try {
      const result = await loadMapDocumentCatalog(backend, context, { signal: aborter.signal });
      if (aborter.signal.aborted || generation.current !== requestGeneration || ownerRef.current !== requestOwner) return;
      if (result.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase() ||
          (result.documents.length > 0 && result.documents.some(document => !isSha256(document.visualizationRevision)))) {
        throw new Error('地图目录与当前工程修订或 .vis 修订不匹配。');
      }
      setMapCatalog(result);
      const next = result.documents.find(document => mapKey(document) === previousKey) ??
        result.documents.find(document => document.mapIndex === result.activeMapIndex && supportedMapTypes.has(document.mapType)) ??
        result.documents.find(document => supportedMapTypes.has(document.mapType));
      const nextKey = next ? mapKey(next) : '';
      setSelectedMapKey(nextKey);
      if (previousKey && nextKey && previousKey !== nextKey) {
        setInputs([]); setPreview(null); setProfileIndex(0);
      }
      if (!next) setError(label('当前工程没有可供栅格计算器读取的保存地图。', 'The project has no saved map supported by the raster calculator.'));
      else if (!supportedMapTypes.has(next.mapType)) setError(label('栅格计算器只支持 Project、Buildable、Geometry 或 Plan 保存地图。', 'The raster calculator supports Project, Buildable, Geometry, or Plan maps only.'));
    } catch (caught) {
      if (!aborter.signal.aborted && generation.current === requestGeneration && ownerRef.current === requestOwner) {
        setError(label('读取保存地图失败。', 'Could not read saved maps.'));
        setDiagnostic(showRawError(caught));
      }
    } finally {
      if (generation.current === requestGeneration) {
        setOperation(null);
        if (controller.current === aborter) controller.current = null;
      }
    }
  };

  const inspectSources = async (): Promise<void> => {
    invalidatePreview(); setCapture(null); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    if (!selectedMap || !context.projectPath || !context.projectRevision || !context.projectXml?.trim() ||
        !context.projectSessionID || !isSha256(selectedMap.visualizationRevision)) {
      setError(label('请选择具有有效 .vis 修订的保存地图。', 'Choose a saved map with a valid .vis revision.'));
      return;
    }
    if (!inspectAvailable || !backend.supports('map.document.list') || !backend.supports('map.tree.load')) {
      setError(label('当前服务未开放真实栅格源检查。', 'The service does not expose real raster-source inspection.'));
      return;
    }
    const requestOwner = owner;
    const selectedKey = mapKey(selectedMap);
    const requestGeneration = ++generation.current;
    const aborter = new AbortController(); controller.current = aborter;
    setOperation('inspect');
    try {
      const xmlSHA = await projectXmlSha(context.projectXml);
      if (!xmlSHA) throw new Error('当前工程 XML 快照缺失，不能构造来源检查请求。');
      const map = buildRasterCalculatorMapRequest(context, selectedMap, xmlSHA);
      const raw: unknown = await backend.request('raster.calculator.inspect', { map }, { signal: aborter.signal });
      if (aborter.signal.aborted || generation.current !== requestGeneration || ownerRef.current !== requestOwner) return;
      if (!isRecord(raw)) throw new Error('栅格计算器来源检查回执格式无效。');
      const inspected = parseRasterCalculatorInspection(raw, { context, map: selectedMap, projectXmlSHA: xmlSHA });
      if (mapKey(selectedMap) !== selectedKey || selectedMapKey !== selectedKey) throw new Error('地图选择在来源检查期间发生变化。');
      setCapture(inspected);
      setNoDataBehavior(current => inspected.noDataBehaviors.includes(current) ? current : inspected.defaultNoDataBehavior);
      if (inspected.sources.length === 0) setNotice(label('Host 没有返回 Terrain 或带地图输出的 Result 栅格源；可以检查目录，但还不能计算。', 'The Host returned no Terrain or map-output Result sources; inspect succeeded, but evaluation is unavailable.'));
      else setNotice(label(`已从 Host 读取 ${inspected.sources.length} 个真实来源；表达式执行会重新核对所有来源文件 SHA。`,
        `Inspected ${inspected.sources.length} Host sources; evaluation will verify each source-file SHA again.`));
    } catch (caught) {
      if (!aborter.signal.aborted && generation.current === requestGeneration && ownerRef.current === requestOwner) {
        setError(label('检查栅格来源失败。', 'Could not inspect raster sources.'));
        setDiagnostic(showRawError(caught));
      }
    } finally {
      if (generation.current === requestGeneration) {
        setOperation(null);
        if (controller.current === aborter) controller.current = null;
      }
    }
  };

  const evaluate = async (requestedProfileIndex = 0): Promise<void> => {
    invalidatePreview(); setError(undefined); setDiagnostic(undefined); setNotice(undefined);
    if (!capture || capture.owner !== owner || !selectedMap || !isSha256(capture.contextToken)) {
      setError(label('请先检查当前保存地图的真实栅格源。', 'Inspect the selected saved map sources first.'));
      return;
    }
    const validation = validateRasterCalculatorRecipe({ expression, noDataBehavior, inputs, constants }, capture, language);
    if (validation.error || !validation.recipe) {
      setError(validation.error ?? label('栅格配方无效。', 'The raster recipe is invalid.'));
      return;
    }
    const viewport = selectedViewport(viewportMode, bounds, calculatorSourceExtent(inputs, capture.sources), pixelWidth, pixelHeight, language);
    if (viewport.error || !viewport.viewport) {
      setError(viewport.error ?? label('预览视口无效。', 'The preview viewport is invalid.'));
      return;
    }
    const outputName = layerName.trim();
    if (!outputName) { setError(label('请填写预览图层名称。', 'Enter a preview layer name.')); return; }
    if (!Number.isInteger(requestedProfileIndex) || requestedProfileIndex < 0) {
      setError(label('所选结果时刻无效。', 'The selected Result profile is invalid.')); return;
    }

    const requestOwner = owner;
    const inspectedCapture = capture;
    const inspectedMapKey = mapKey(selectedMap);
    const requestGeneration = ++generation.current;
    const aborter = new AbortController(); controller.current = aborter;
    setOperation('evaluate'); setProfileIndex(requestedProfileIndex);
    const layerID = `raster-calculator-${crypto.randomUUID()}`;
    try {
      const payload = {
        map: inspectedCapture.map,
        expectedContextToken: inspectedCapture.contextToken,
        recipe: validation.recipe,
        layerID,
        layerName: outputName,
        profileIndex: requestedProfileIndex,
        viewport: viewport.viewport,
      };
      const raw: unknown = await backend.request('raster.calculator.evaluate', payload, { signal: aborter.signal });
      if (aborter.signal.aborted || generation.current !== requestGeneration || ownerRef.current !== requestOwner) return;
      if (mapKey(selectedMap) !== inspectedMapKey || selectedMapKey !== inspectedMapKey) throw new Error('地图选择在栅格计算期间发生变化。');
      const result = parseRasterCalculatorEvaluation(raw, { capture: inspectedCapture, layerID, layerName: outputName, profileIndex: requestedProfileIndex });
      setPreview(result);
      setProfileIndex(result.selectedProfileIndex);
      setNotice(label('预览由当前 C# Host 按所选来源 SHA、时刻和世界坐标视口生成。该回执是当前页面中的临时预览。',
        'The C# Host rendered this preview from the selected source SHAs, profile, and world-coordinate viewport. The receipt is a page-local preview.'));
    } catch (caught) {
      if (!aborter.signal.aborted && generation.current === requestGeneration && ownerRef.current === requestOwner) {
        setError(label('栅格计算或来源复核失败。', 'Raster evaluation or source revalidation failed.'));
        setDiagnostic(showRawError(caught));
      }
    } finally {
      if (generation.current === requestGeneration) {
        setOperation(null);
        if (controller.current === aborter) controller.current = null;
      }
    }
  };

  const addInput = (): void => {
    invalidatePreview();
    setInputs(current => normalizeRasterCalculatorKeyFrame([...current, {
      id: crypto.randomUUID(), variableName: defaultRasterCalculatorVariableName(current),
      sourceID: '', sourceRevision: '', kind: '', mapType: null, forceHorizontal: false, isKeyFrame: false,
    }]));
  };

  const updateInput = (id: string, value: RasterCalculatorInputDraft, preferKeyFrame = false): void => {
    invalidatePreview();
    setInputs(current => normalizeRasterCalculatorKeyFrame(current.map(input => input.id === id ? value : input), preferKeyFrame ? id : undefined));
  };

  const removeInput = (id: string): void => {
    invalidatePreview();
    setInputs(current => normalizeRasterCalculatorKeyFrame(current.filter(input => input.id !== id)));
  };

  const addConstant = (): void => {
    invalidatePreview();
    setConstants(current => [...current, { id: crypto.randomUUID(), name: '', value: '' }]);
  };

  const updateConstant = (id: string, value: RasterCalculatorConstantDraft): void => {
    invalidatePreview();
    setConstants(current => current.map(constant => constant.id === id ? value : constant));
  };

  const removeConstant = (id: string): void => {
    invalidatePreview();
    setConstants(current => current.filter(constant => constant.id !== id));
  };

  return <div className="tools-area" data-testid="raster-calculator-editor">
    <section className="tools-hero tools-hero-compact">
      <div><span className="tools-kicker">{label('栅格计算器', 'RASTER CALCULATOR')}</span><h2>{label('栅格计算器', 'Raster Calculator')}</h2>
        <p>{label('从已保存地图中选择真实地形或结果栅格，按原表达式、常量、NoData 和结果时刻配方执行 Host 预览。',
          'Choose real Terrain or Result rasters from a saved map, then evaluate the original expression, constants, NoData, and Result profile recipe through the Host.')}</p></div>
      <span className={`tools-runtime-pill ${inspectAvailable && evaluateAvailable ? 'is-connected' : 'is-unavailable'}`}>
        {inspectAvailable && evaluateAvailable ? 'C#' : label('C# 未接通', 'C# unavailable')}
      </span>
    </section>

    <section className="tools-subpanel tools-raster-source-capture">
      <div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {label('保存地图与来源快照', 'SAVED MAP AND SOURCE CAPTURE')}</span>
        <h3>{label('明确选择地图并检查栅格目录', 'Choose a saved map and inspect its raster catalog')}</h3></div>
        <span className="tools-badge">map.document.list → raster.calculator.inspect</span>
      </div>
      <div className="tools-context-card"><span className={`tools-state-dot ${hasProject ? 'is-ready' : ''}`} /><div>
        <strong>{context.projectPath || label('未打开工程', 'No project open')}</strong>
        <small>{label('会话', 'Session')}: {context.projectSessionID || '—'} · {label('工程 SHA', 'Project SHA')}: {context.projectRevision || '—'}</small>
      </div></div>
      <div className="tools-actions">
        <button className="tools-button tools-button-secondary" type="button" disabled={!hasProject || operation !== null ||
          !backend.supports('map.document.list') || !backend.supports('map.tree.load')} onClick={() => void readMapCatalog()}>
          {operation === 'maps' ? label('读取地图中…', 'Reading maps…') : label(mapCatalog ? '重新读取保存地图' : '读取保存地图', mapCatalog ? 'Refresh saved maps' : 'Read saved maps')}
        </button>
        <label className="tools-field tools-calculator-map-select"><span>{label('保存地图身份', 'Saved map identity')}</span>
          <select data-testid="raster-calculator-map-picker" aria-label={label('栅格计算器保存地图', 'Raster calculator saved map')}
            value={selectedMapKey} disabled={!mapCatalog || operation !== null}
            onChange={event => {
              const key = event.target.value;
              setSelectedMapKey(key); setCapture(null); setPreview(null); setInputs([]); setProfileIndex(0);
              setError(undefined); setDiagnostic(undefined); setNotice(undefined);
            }}>
            <option value="">{label('请选择已保存地图', 'Choose a saved map')}</option>
            {(mapCatalog?.documents ?? []).map(document => <option key={mapKey(document)} value={mapKey(document)}
              disabled={!supportedMapTypes.has(document.mapType)}>
              {document.name} · {document.mapType} · #{document.mapIndex}
            </option>)}
          </select>
        </label>
        <button className="tools-button tools-button-primary" type="button" disabled={!selectedMap || operation !== null || !inspectAvailable ||
          !selectedMap.visualizationRevision || !supportedMapTypes.has(selectedMap.mapType)} onClick={() => void inspectSources()}>
          {operation === 'inspect' ? label('检查真实栅格源中…', 'Inspecting raster sources…') : label('检查所选地图来源', 'Inspect selected map sources')}
        </button>
        {operation && <button className="tools-button tools-button-secondary" type="button" onClick={cancelCurrent}>{label('取消当前操作', 'Cancel current operation')}</button>}
      </div>
      {selectedMap && <dl className="tools-source-details">
        <div><dt>{label('地图名称', 'Map name')}</dt><dd>{selectedMap.name}</dd></div>
        <div><dt>{label('地图类型 / 索引', 'Map type / index')}</dt><dd>{selectedMap.mapType} · #{selectedMap.mapIndex}</dd></div>
        <div><dt>{label('.vis 修订', '.vis revision')}</dt><dd><code>{selectedMap.visualizationRevision || '—'}</code></dd></div>
      </dl>}
      {mapCatalog && <p className="tools-field-help">{label('Host 地图目录工程修订', 'Host map catalog project revision')}: <code>{mapCatalog.projectRevision}</code> · {label('地图数', 'maps')}: {mapCatalog.documents.length}</p>}
      {capture && <div className="tools-service-grid">
        <div className="tools-service-item"><code>{label('来源上下文令牌', 'Source context token')}</code><span><code>{capture.contextToken}</code></span></div>
        <div className="tools-service-item"><code>{label('Host 栅格源数', 'Host raster sources')}</code><span>{capture.sources.length}</span></div>
        <div className="tools-service-item"><code>{label('NoData 默认', 'Default NoData')}</code><span>{capture.defaultNoDataBehavior}</span></div>
      </div>}
      {capture && capture.sources.length > 0 && <div className="tools-input-bindings">
        <div className="tools-subheading-row"><h4>{label('Host 来源清单', 'Host source catalog')}</h4><span className="tools-badge">{capture.sources.length}</span></div>
        <ul className="tools-service-list">{capture.sources.map(source => <li key={source.id}>
          <strong>{source.kind === 'terrain' ? label('地形', 'Terrain') : label('结果地图', 'Result map')} · {source.name}</strong>
          <code>{source.assetPath}</code><small>{label('修订', 'Revision')}: {source.revision} · {label('可用', 'available')}: {source.isAvailable ? label('是', 'yes') : label('否', 'no')}</small>
          {!source.isAvailable && <small>{source.unavailableReason}</small>}
          <small>{source.kind === 'resultMap' ? `${source.profileCount} ${label('个结果时刻', 'Result profiles')} · ${source.mapTypes.join(', ')}` : `${label('NoData', 'NoData')}: ${source.noDataValue ?? '—'}`}</small>
        </li>)}</ul>
      </div>}
      {capture && <div className="tools-callout">{label('来源身份含 Host 返回的稳定 ID、聚合文件 SHA、每个 Terrain/Result 依赖路径与文件修订。重新读取会使旧检查失效；来源修订变化后必须显式重新绑定。',
        'Source identity includes the Host ID, aggregate file SHA, every Terrain/Result dependency path, and file revision. Refresh invalidates the old inspection; changed sources require explicit rebinding.')}</div>}
    </section>

    <div className="tools-raster-grid">
      <section className="tools-subpanel tools-raster-editor">
        <div className="tools-subpanel-heading"><div><span className="tools-kicker">02 / {label('计算配方', 'CALCULATOR RECIPE')}</span><h3>{label('表达式、输入与常量', 'Expression, inputs, and constants')}</h3></div>
          <span className="tools-badge">RasterCalculatorRecipe</span></div>
        <label className="tools-field"><span>{label('表达式', 'Expression')}</span><textarea className="tools-expression" rows={4} spellCheck={false}
          value={expression} data-testid="raster-calculator-expression"
          onChange={event => { invalidatePreview(); setExpression(event.target.value); }}
          placeholder={label('例如：depth - ndv', 'For example: depth - ndv')} /></label>
        <p className="tools-field-help">{label('Host 保留常量', 'Host reserved constants')}: <code>{capture?.reservedConstants.join(', ') || 'ndv, pi, e, tau'}</code> ·
          {label('非默认来源 NoData 可生成变量专属', 'Non-default source NoData can expose per-variable')} <code>&lt;var&gt;_ndv</code></p>
        <div className="tools-subheading-row"><h4>{label('输入栅格配方', 'Raster input recipes')}</h4>
          <button className="tools-text-button" type="button" disabled={!capture} onClick={addInput}>{label('+ 添加输入', '+ Add input')}</button></div>
        {inputs.length === 0 && <p className="tools-muted">{label('从当前 Host 栅格源中添加 Terrain 或 Result map 输入。', 'Add a Terrain or Result map input from the current Host source catalog.')}</p>}
        {inputs.map(input => <RasterCalculatorInputRecipe key={input.id} language={language} value={input}
          sources={capture?.sources ?? []} onChange={(value, preferKeyFrame) => updateInput(input.id, value, preferKeyFrame)} onRemove={() => removeInput(input.id)} />)}

        <div className="tools-subheading-row"><h4>{label('自定义常量', 'Custom constants')}</h4>
          <button className="tools-text-button" type="button" onClick={addConstant}>{label('+ 添加常量', '+ Add constant')}</button></div>
        {constants.length === 0 && <p className="tools-muted">{label('尚未添加自定义常量。', 'No custom constants have been added.')}</p>}
        {constants.map(constant => <RasterCalculatorConstantRecipe key={constant.id} language={language} value={constant}
          onChange={value => updateConstant(constant.id, value)} onRemove={() => removeConstant(constant.id)} />)}

        <div className="tools-field-grid">
          <label className="tools-field"><span>{label('NoData 行为', 'NoData behavior')}</span>
            <select value={noDataBehavior} disabled={!capture} onChange={event => { invalidatePreview(); setNoDataBehavior(event.target.value); }}>
              {(capture?.noDataBehaviors ?? ['Allow', 'WriteNDV', 'WriteZero']).map(value => <option key={value} value={value}>
                {value === 'Allow' ? label('允许 NoData 传播', 'Allow NoData propagation') : value === 'WriteNDV' ? label('写入 NoData 值', 'Write NoData value') : value === 'WriteZero' ? label('写入 0', 'Write zero') : value}
              </option>)}
            </select>
          </label>
          <label className="tools-field"><span>{label('预览图层名称', 'Preview layer name')}</span>
            <input value={layerName} onChange={event => { invalidatePreview(); setLayerName(event.target.value); }} />
          </label>
        </div>
        {capture && recipeValidation.error && <div className="tools-notice" role="status">{recipeValidation.error}</div>}
      </section>

      <section className="tools-subpanel tools-source-inspect">
        <div className="tools-subpanel-heading"><div><span className="tools-kicker">03 / {label('空间视口', 'SPATIAL VIEWPORT')}</span><h3>{label('真实世界坐标范围与预览分辨率', 'Real world bounds and preview resolution')}</h3></div></div>
        <label className="tools-field"><span>{label('预览范围来源', 'Preview extent source')}</span>
          <select value={viewportMode} onChange={event => { invalidatePreview(); setViewportMode(event.target.value as ViewportMode); }}>
            <option value="source">{label('输入源共同有效范围', 'Intersection of input source extents')}</option>
            <option value="explicit">{label('手动输入世界坐标范围', 'Enter explicit world-coordinate bounds')}</option>
          </select>
        </label>
        {viewportMode === 'source' ? <div className="tools-context-card">
          <span className={`tools-state-dot ${sourceExtent ? 'is-ready' : ''}`} /><div><strong>{label('由已检查来源范围求交', 'Intersection from inspected source extents')}</strong>
            <small>{sourceExtent ? `${sourceExtent.minX}, ${sourceExtent.minY} → ${sourceExtent.maxX}, ${sourceExtent.maxY}` : label('添加可用输入且范围相交后显示', 'Add available inputs with overlapping extents to show bounds')}</small></div>
        </div> : <div className="tools-field-grid tools-calculator-bounds">
          {(['minX', 'minY', 'maxX', 'maxY'] as const).map(key => <label className="tools-field" key={key}>
            <span>{key}</span><input type="number" step="any" value={bounds[key]} aria-label={label(`世界坐标 ${key}`, `World coordinate ${key}`)}
              onChange={event => { invalidatePreview(); setBounds(current => ({ ...current, [key]: event.target.value })); }} />
          </label>)}
        </div>}
        <div className="tools-field-grid">
          <label className="tools-field"><span>{label('像元宽', 'Pixel width')}</span><input type="number" min={1} max={1536} step={1} value={pixelWidth}
            onChange={event => { invalidatePreview(); setPixelWidth(event.target.value); }} /></label>
          <label className="tools-field"><span>{label('像元高', 'Pixel height')}</span><input type="number" min={1} max={1536} step={1} value={pixelHeight}
            onChange={event => { invalidatePreview(); setPixelHeight(event.target.value); }} /></label>
        </div>
        <p className="tools-field-help">{label('服务端限制：单边不超过 1536，总像元不超过 2,097,152。视口数值会直接传给 Host，不从本地地图 UI 猜测。',
          'Host limits each side to 1536 pixels and the total to 2,097,152. Bounds are sent explicitly; they are not guessed from the local map UI.')}</p>
        {viewportValidation.error && <div className="tools-notice" role="status">{viewportValidation.error}</div>}
        <div className="tools-actions">
          <button className="tools-button tools-button-primary" type="button" data-testid="raster-calculator-evaluate"
            disabled={!canEvaluate} onClick={() => void evaluate(0)}>
            {operation === 'evaluate' ? label('Host 计算中…', 'Evaluating on Host…') : label('计算并生成临时预览', 'Evaluate and preview')}
          </button>
          {operation === 'evaluate' && <button className="tools-button tools-button-secondary" type="button" onClick={cancelCurrent}>{label('取消计算', 'Cancel evaluation')}</button>}
        </div>
        {!capture && <div className="tools-callout">{inspectAvailable ? label('检查所选保存地图后，才会启用真实源、NoData、配方和范围校验。',
          'Inspect the selected saved map before source, NoData, recipe, and extent checks become available.') : label('raster.calculator.inspect 尚未由当前服务发布。', 'The current service has not advertised raster.calculator.inspect.')}</div>}
        {capture && !evaluateAvailable && <div className="tools-callout">{label('raster.calculator.evaluate 尚未由当前服务发布。', 'The current service has not advertised raster.calculator.evaluate.')}</div>}
      </section>
    </div>

    {preview && <section className="tools-subpanel tools-calculator-preview" data-testid="raster-calculator-preview">
      <div className="tools-subpanel-heading"><div><span className="tools-kicker">04 / {label('计算结果预览', 'CALCULATED PREVIEW')}</span><h3>{preview.layerName}</h3></div>
        <span className="tools-badge">{preview.layer.width} × {preview.layer.height}</span></div>
      <div className="tools-field-grid">
        <label className="tools-field"><span>{label('结果时刻', 'Result profile')}</span>
          <select data-testid="raster-calculator-profile" value={profileIndex}
            disabled={operation !== null || preview.profiles.length < 2}
            onChange={event => void evaluate(Number(event.target.value))}>
            {preview.profiles.map(profile => <option key={profile.index} value={profile.index}>
              #{profile.index} · {profile.time}
            </option>)}
          </select>
        </label>
        <div className="tools-context-card"><span className={`tools-state-dot ${preview.isDynamic ? 'is-ready' : ''}`} /><div>
          <strong>{preview.isDynamic ? label('动态结果栅格', 'Dynamic Result raster') : label('静态栅格', 'Static raster')}</strong>
          <small>{preview.profileCount} {label('个 Host 配置时刻', 'Host profiles')} · {preview.selectedProfileTime ?? label('无时间戳', 'no time stamp')}</small>
        </div></div>
      </div>
      <div className="tools-context-card"><span className="tools-state-dot is-ready" /><div>
        <strong>{label('服务端 RGBA 帧', 'Host RGBA frame')}</strong>
        <small>{preview.layer.minX}, {preview.layer.minY} → {preview.layer.maxX}, {preview.layer.maxY} ·
          {(preview.layer.width * preview.layer.height * 4).toLocaleString()} {label('像素字节', 'pixel bytes')}</small>
      </div></div>
      <RasterCalculatorCanvas result={preview} language={language} />
      <div className="tools-callout">{label('此图像由 Host RasterRenderer 返回并仅在本面板显示。当前 MapView 没有接收此计算临时图层的父级 API；RasterCalculator Host inspect/evaluate 也不写工程或 .vis，因此不提供添加到地图或保存按钮。',
        'This image is returned by the Host RasterRenderer and displayed only in this panel. MapView exposes no parent API for this transient layer, and the RasterCalculator Host inspect/evaluate operations do not write the project or .vis, so this editor has no add-to-map or save action.')}</div>
      <details><summary>{label('来源和地图修订', 'Source and map revisions')}</summary>
        <dl className="tools-source-details"><div><dt>{label('工程 SHA', 'Project SHA')}</dt><dd><code>{preview.projectXmlSHA}</code></dd></div>
          <div><dt>{label('.vis 修订', '.vis revision')}</dt><dd><code>{preview.visualizationRevision}</code></dd></div>
          <div><dt>{label('来源上下文令牌', 'Source context token')}</dt><dd><code>{preview.contextToken}</code></dd></div>
          {preview.sources.map(source => <div key={source.id}><dt>{source.name}</dt><dd><code>{source.assetPath}</code><small>{source.revision}</small></dd></div>)}
        </dl>
      </details>
    </section>}

    {(error || diagnostic || notice) && <section className={`tools-notice ${error ? '' : 'is-success'}`} role={error ? 'alert' : 'status'}>
      {error && <strong>{error}</strong>}{notice && <span>{notice}</span>}
      {diagnostic && <details><summary>{label('Host / 请求诊断', 'Host / request diagnostics')}</summary><pre>{diagnostic}</pre></details>}
    </section>}

    <section className="tools-callout" data-testid="raster-calculator-draft-boundary">
      {label('配方仅保存在当前编辑器草稿中；切换工程前会按 tools.raster-calculator dirty owner 参与草稿保护。本控件不会伪装持久化，也不会修改 .ras、.vis 或输入栅格。',
        'The recipe remains a page-local draft and participates in context protection as tools.raster-calculator. This editor does not claim persistence or modify .ras, .vis, or input rasters.')}
    </section>
  </div>;
}
