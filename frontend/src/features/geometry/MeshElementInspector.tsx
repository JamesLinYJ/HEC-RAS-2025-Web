import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { GeometryMeshSnapshot, GeometryModelRequest, GeometryTopologySnapshot } from './geometryTypes';
import { GeometryError } from './GeometryError';
import {
  parseMeshElementFindResult,
  parseMeshElementMapPreview,
  parseMeshElementPropertyChart,
  sha256Text,
  type MeshElementCapture,
  type MeshElementFindResult,
  type MeshElementMapPreview,
  type MeshElementPropertyChartResult,
} from './meshElementInspectorData';
import './meshElementInspector.css';

type ElementKind = 'Cell' | 'Face';
type InspectorStatus = 'finding' | 'charting' | 'ready' | 'requiresPropertyTables' | 'cancelled' | 'error';

interface InspectorEntry {
  scopeKey: string;
  backend: BackendGateway;
  status: InspectorStatus;
  flowAreaID: string;
  elementKind: ElementKind;
  elementIndex: number;
  findResult?: MeshElementFindResult;
  chartResult?: MeshElementPropertyChartResult;
  message?: string;
}

export interface MeshElementInspectorProps {
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  backendRevision?: number;
  model: GeometryModelRequest | null;
  mesh?: GeometryMeshSnapshot;
  topology?: GeometryTopologySnapshot;
  disabled?: boolean;
}

function randomOperationID(): string {
  if (!globalThis.crypto?.randomUUID) throw new Error('当前环境无法创建唯一的网格读取操作标识。');
  return globalThis.crypto.randomUUID();
}

function errorMessage(error: unknown, language: Language): string {
  if (error instanceof Error && error.message) return error.message;
  return language === 'zh' ? '网格元素读取失败。' : 'Could not read the mesh element.';
}

function previewResult(mesh: GeometryMeshSnapshot | undefined, language: Language): { value: MeshElementMapPreview | null; error: string; diagnostic?: string } {
  try { return { value: parseMeshElementMapPreview(mesh), error: '' }; }
  catch (error) { return { value: null, error: language === 'zh' ? '网格预览数据校验失败，未显示几何。' : 'The mesh preview failed validation and is hidden.', diagnostic: errorMessage(error, language) }; }
}

function elementCount(kind: ElementKind, flowArea: { cellCount: number; faceCount: number }): number {
  return kind === 'Cell' ? flowArea.cellCount : flowArea.faceCount;
}

function scopeFingerprint(
  context: WorkbenchContext,
  model: GeometryModelRequest | null,
  mesh: GeometryMeshSnapshot | undefined,
  topology: GeometryTopologySnapshot | undefined,
  backendRevision: number | undefined,
): string {
  return JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectRevision,
    context.projectXml,
    context.coreLayerContext ?? null,
    context.selectedAsset?.path ?? null,
    context.selectedAsset?.kind ?? null,
    context.selectedAsset?.revision ?? null,
    context.selectedAsset?.locator ?? null,
    model?.assetPath ?? null,
    model?.kind ?? null,
    model?.projectPath ?? null,
    model?.projectXml ?? null,
    model?.coreLayerContext ?? null,
    mesh?.revision ?? null,
    mesh?.draftState ?? null,
    mesh?.topologyFingerprint ?? null,
    topology?.revision ?? null,
    topology?.draftState ?? null,
    backendRevision ?? null,
  ]);
}

function polygonPoints(points: Array<{ x: number; y: number }>): string {
  return points.map((point) => `${point.x},${-point.y}`).join(' ');
}

function mapViewBox(preview: MeshElementMapPreview | null, flowAreaID?: string,
  additionalPoints: Array<{ x: number; y: number }> = []): { viewBox: string; extent: NonNullable<MeshElementMapPreview['extent']> } | null {
  const cells = preview?.cells.filter((cell) => !flowAreaID || cell.flowAreaID === flowAreaID) ?? [];
  if (!cells.length && !additionalPoints.length) return null;
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (const cell of cells) {
    for (const point of cell.coordinates) {
      minX = Math.min(minX, point.x);
      minY = Math.min(minY, point.y);
      maxX = Math.max(maxX, point.x);
      maxY = Math.max(maxY, point.y);
    }
  }
  for (const point of additionalPoints) {
    minX = Math.min(minX, point.x);
    minY = Math.min(minY, point.y);
    maxX = Math.max(maxX, point.x);
    maxY = Math.max(maxY, point.y);
  }
  const extent = { minX, minY, maxX, maxY };
  const width = extent.maxX - extent.minX;
  const height = extent.maxY - extent.minY;
  const span = Math.max(width, height, 1);
  const pad = span * 0.025;
  return { extent, viewBox: `${extent.minX - pad} ${-extent.maxY - pad} ${Math.max(width, span * 0.01) + pad * 2} ${Math.max(height, span * 0.01) + pad * 2}` };
}

interface ChartDomain { minX: number; maxX: number; minY: number; maxY: number }

function chartDomain(points: Array<{ x: number; y: number }>): ChartDomain | null {
  if (!points.length) return null;
  let minX = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (const point of points) {
    minX = Math.min(minX, point.x);
    maxX = Math.max(maxX, point.x);
    minY = Math.min(minY, point.y);
    maxY = Math.max(maxY, point.y);
  }
  const padX = maxX === minX ? Math.max(Math.abs(minX) * 0.02, 1) : (maxX - minX) * 0.04;
  const padY = maxY === minY ? Math.max(Math.abs(minY) * 0.02, 1) : (maxY - minY) * 0.08;
  return { minX: minX - padX, maxX: maxX + padX, minY: minY - padY, maxY: maxY + padY };
}

function chartPoint(point: { x: number; y: number }, domain: ChartDomain): { x: number; y: number } {
  const left = 58;
  const right = 616;
  const top = 18;
  const bottom = 202;
  return {
    x: left + (point.x - domain.minX) / (domain.maxX - domain.minX) * (right - left),
    y: bottom - (point.y - domain.minY) / (domain.maxY - domain.minY) * (bottom - top),
  };
}

function axisLabel(quantity: string, units: string, language: Language): string {
  const label = language === 'zh'
    ? quantity === 'Volume' ? '库容' : quantity === 'Area' ? '过水面积' : quantity === 'Elevation' ? '高程' : quantity
    : quantity;
  return units ? `${label} (${units})` : label;
}

export function MeshElementInspector({
  language, context, backend, backendRevision, model, mesh, topology, disabled = false,
}: MeshElementInspectorProps) {
  const zh = language === 'zh';
  const scopeKey = scopeFingerprint(context, model, mesh, topology, backendRevision);
  const scopeRef = useRef(scopeKey);
  const backendRef = useRef(backend);
  scopeRef.current = scopeKey;
  backendRef.current = backend;
  const generationRef = useRef(0);
  const pendingRef = useRef<AbortController | null>(null);
  const [entry, setEntry] = useState<InspectorEntry>();
  const [elementKind, setElementKind] = useState<ElementKind>('Cell');
  const [selectedFlowAreaID, setSelectedFlowAreaID] = useState(mesh?.flowAreas[0]?.id ?? '');
  const [elementIndex, setElementIndex] = useState('0');
  const mapSource = (mesh as (GeometryMeshSnapshot & { map?: unknown }) | undefined)?.map;
  const parsedPreview = useMemo(() => previewResult(mesh, language), [mapSource, mesh?.flowAreas, language]);
  const preview = parsedPreview.value;
  const flowArea = mesh?.flowAreas.find((area) => area.id === selectedFlowAreaID) ?? mesh?.flowAreas[0];
  const activeEntry = entry?.scopeKey === scopeKey && entry.backend === backend ? entry : undefined;
  const operationAvailable = backend.supports('geometry.mesh.findElement') && backend.supports('geometry.mesh.elementPropertyChart');
  const visiblePreviewCells = preview?.cells.filter((cell) => !flowArea || cell.flowAreaID === flowArea.id) ?? [];
  const selectedFind = activeEntry?.scopeKey === scopeKey && activeEntry.backend === backend && activeEntry.flowAreaID === flowArea?.id
    ? activeEntry.findResult : undefined;
  const mapBounds = mapViewBox(preview, flowArea?.id, selectedFind?.coordinates);
  const displayedMapExtent = mapBounds?.extent;
  const viewBox = mapBounds?.viewBox;

  const cancelPending = (clearResult = true): void => {
    generationRef.current += 1;
    pendingRef.current?.abort();
    pendingRef.current = null;
    if (clearResult) setEntry(undefined);
  };

  useEffect(() => {
    cancelPending(true);
    setSelectedFlowAreaID(mesh?.flowAreas[0]?.id ?? '');
    setElementKind('Cell');
    setElementIndex('0');
    return () => {
      generationRef.current += 1;
      pendingRef.current?.abort();
      pendingRef.current = null;
    };
    // scopeKey includes every source/session/draft identity used by these operations.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeKey, backend, mesh?.flowAreas]);

  const runElement = async (flowAreaID: string, kind: ElementKind, rawIndex: string): Promise<void> => {
    cancelPending(false);
    const generation = generationRef.current;
    const controller = new AbortController();
    pendingRef.current = controller;
    const isCurrent = (): boolean => generationRef.current === generation && scopeRef.current === scopeKey &&
      backendRef.current === backend && !controller.signal.aborted;
    const area = mesh?.flowAreas.find((item) => item.id === flowAreaID);
    const index = rawIndex.trim() ? Number(rawIndex) : Number.NaN;
    if (!model || !context.selectedAsset || context.selectedAsset.kind !== 'Geometry' ||
        model.assetPath !== context.selectedAsset.path || model.kind !== context.selectedAsset.kind ||
        model.projectPath !== context.projectPath || model.projectXml !== context.projectXml || !mesh || !area ||
        !Number.isInteger(index) || index < 0 || index >= elementCount(kind, area)) {
      pendingRef.current = null;
      setEntry({ scopeKey, backend, status: 'error', flowAreaID, elementKind: kind, elementIndex: Number.isInteger(index) ? index : -1,
        message: zh ? '请先选择有网格的流动区域，并输入有效的零基单元或面索引。' : 'Choose a meshed flow area and enter a valid zero-based cell or face index.' });
      return;
    }
    if (!/^[0-9a-f]{64}$/i.test(mesh.revision) || !mesh.draftState || !context.projectSessionID || !context.selectedAsset.path) {
      pendingRef.current = null;
      setEntry({ scopeKey, backend, status: 'error', flowAreaID, elementKind: kind, elementIndex: index,
        message: zh ? '当前工程会话、Geometry 修订或网格草稿不完整，无法安全定位。' : 'The project session, Geometry revision or mesh draft is incomplete.' });
      return;
    }
    if (!operationAvailable) {
      pendingRef.current = null;
      setEntry({ scopeKey, backend, status: 'error', flowAreaID, elementKind: kind, elementIndex: index,
        message: zh ? '当前服务暂不提供网格元素定位或属性曲线读取。' : 'Mesh element lookup or property-chart reading is unavailable from the current service.' });
      return;
    }

    let operationID: string;
    try { operationID = randomOperationID(); }
    catch (error) {
      pendingRef.current = null;
      setEntry({ scopeKey, backend, status: 'error', flowAreaID, elementKind: kind, elementIndex: index,
        message: errorMessage(error, language) });
      return;
    }
    const topologyDraftState = topology?.draftState ?? null;
    const topologyText = topologyDraftState ?? '';
    setEntry({ scopeKey, backend, status: 'finding', flowAreaID, elementKind: kind, elementIndex: index });
    try {
      const [meshDraftSHA256, topologyDraftSHA256] = await Promise.all([
        sha256Text(mesh.draftState), sha256Text(topologyText),
      ]);
      if (!isCurrent()) return;
      const capture: MeshElementCapture = {
        operationID,
        projectSessionID: context.projectSessionID,
        assetID: context.selectedAsset.path,
        model,
        revision: mesh.revision,
        draftState: mesh.draftState,
        topologyDraftState,
        meshDraftSHA256,
        topologyDraftSHA256,
        flowAreaID: area.id,
        flowAreaName: area.name,
        flowAreaIndex: area.rowIndex,
        elementKind: kind,
        elementIndex: index,
      };
      const findRequest = {
        model,
        revision: capture.revision,
        draftState: capture.draftState,
        topologyDraftState: capture.topologyDraftState,
        meshDraftSHA256: capture.meshDraftSHA256,
        topologyDraftSHA256: capture.topologyDraftSHA256,
        flowAreaID: capture.flowAreaID,
        expectedFlowAreaName: capture.flowAreaName,
        elementKind: capture.elementKind,
        index: capture.elementIndex,
      };
      const findResult = parseMeshElementFindResult(
        await backend.request<unknown>('geometry.mesh.findElement', findRequest, { signal: controller.signal }), capture);
      if (!isCurrent()) return;
      setEntry({ scopeKey, backend, status: 'charting', flowAreaID, elementKind: kind, elementIndex: index, findResult });
      const chartRequest = {
        operationID: capture.operationID,
        projectSessionID: capture.projectSessionID,
        assetID: capture.assetID,
        model: capture.model,
        revision: capture.revision,
        draftState: capture.draftState,
        topologyDraftState: capture.topologyDraftState,
        meshDraftSHA256: capture.meshDraftSHA256,
        topologyDraftSHA256: capture.topologyDraftSHA256,
        flowAreaID: capture.flowAreaID,
        expectedFlowAreaName: capture.flowAreaName,
        elementKind: capture.elementKind,
        elementIndex: capture.elementIndex,
      };
      const chartResult = parseMeshElementPropertyChart(
        await backend.request<unknown>('geometry.mesh.elementPropertyChart', chartRequest, { signal: controller.signal }), capture);
      if (!isCurrent()) return;
      const status: InspectorStatus = chartResult.state === 'ready' ? 'ready'
        : chartResult.state === 'requiresPropertyTables' ? 'requiresPropertyTables' : 'cancelled';
      setEntry({ scopeKey, backend, status, flowAreaID, elementKind: kind, elementIndex: index,
        findResult, chartResult, message: chartResult.message ?? undefined });
    } catch (error) {
      if (!isCurrent()) return;
      setEntry({ scopeKey, backend, status: 'error', flowAreaID, elementKind: kind, elementIndex: index,
        message: errorMessage(error, language) });
    } finally {
      if (generationRef.current === generation && pendingRef.current === controller) pendingRef.current = null;
    }
  };

  const selectPreviewCell = (cell: NonNullable<typeof preview>['cells'][number]): void => {
    setSelectedFlowAreaID(cell.flowAreaID);
    setElementKind('Cell');
    setElementIndex(String(cell.cellIndex));
    void runElement(cell.flowAreaID, 'Cell', String(cell.cellIndex));
  };

  const currentChart = activeEntry?.chartResult?.state === 'ready' ? activeEntry.chartResult : undefined;

  return (
    <section className="geometry-panel mesh-element-inspector" data-testid="geometry-mesh-element-inspector">
      <header className="geometry-panel-heading">
        <div>
          <h2>{zh ? '网格元素检查器' : 'Mesh element inspector'}</h2>
          <p>{zh ? '从网格预览选择源单元，或输入从 0 开始的 Cell / Face 索引查看位置与属性曲线。' : 'Select a source cell from the mesh preview or enter a zero-based Cell / Face index to view its location and property curve.'}</p>
        </div>
        <span className="geometry-count-pill">{mesh ? (zh ? '已绑定当前网格草稿' : 'Current mesh draft') : (zh ? '未加载网格' : 'Mesh not loaded')}</span>
      </header>

      <div className="mesh-element-inspector__body">
        <section className="mesh-element-inspector__controls" aria-label={zh ? '网格元素选择' : 'Mesh element selection'}>
          <label className="geometry-form-field" htmlFor="geometry-mesh-element-flow-area">
            {zh ? '流动区域' : 'Flow Area'}
            <select id="geometry-mesh-element-flow-area" data-testid="geometry-mesh-element-flow-area" value={flowArea?.id ?? ''}
              disabled={disabled || !mesh?.flowAreas.length}
              onChange={(event) => { cancelPending(); setSelectedFlowAreaID(event.target.value); }}>
              {(mesh?.flowAreas ?? []).map((area) => <option key={area.id} value={area.id}>{area.name} · {zh ? `第 ${area.rowIndex + 1} 项` : `item ${area.rowIndex + 1}`}</option>)}
            </select>
          </label>
          <label className="geometry-form-field" htmlFor="geometry-mesh-element-kind">
            {zh ? '元素类型' : 'Element kind'}
            <select id="geometry-mesh-element-kind" data-testid="geometry-mesh-element-kind" value={elementKind} disabled={disabled}
              onChange={(event) => {
                cancelPending();
                const next = event.target.value === 'Face' ? 'Face' : 'Cell';
                setElementKind(next);
                setElementIndex('0');
              }}>
              <option value="Cell">Cell</option>
              <option value="Face">Face</option>
            </select>
          </label>
          <label className="geometry-form-field" htmlFor="geometry-mesh-element-index">
            {zh ? '源索引（从 0 开始）' : 'Source index (zero-based)'}
            <input id="geometry-mesh-element-index" data-testid="geometry-mesh-element-index" type="number" min={0}
              max={flowArea ? Math.max(0, elementCount(elementKind, flowArea) - 1) : undefined} step={1}
              value={elementIndex} disabled={disabled} onChange={(event) => {
                cancelPending(); setElementIndex(event.target.value);
              }} />
            <small>{flowArea && elementCount(elementKind, flowArea) > 0
              ? `${zh ? '当前范围' : 'Available'}: 0 – ${elementCount(elementKind, flowArea) - 1} · ${zh ? '共' : 'total'} ${elementCount(elementKind, flowArea)}`
              : (zh ? '当前流动区域没有可用元素' : 'No elements are available in this flow area')}</small>
          </label>
          <div className="mesh-element-inspector__actions">
            <button type="button" className="geometry-primary" data-testid="geometry-mesh-element-find"
              disabled={disabled || !flowArea || !operationAvailable || !mesh || elementCount(elementKind, flowArea) <= 0 || !elementIndex.trim()}
              onClick={() => flowArea && void runElement(flowArea.id, elementKind, elementIndex)}>
              {activeEntry?.status === 'finding' || activeEntry?.status === 'charting'
                ? (zh ? '正在读取…' : 'Reading…') : (zh ? '定位并读取曲线' : 'Locate and read curve')}
            </button>
            <button type="button" className="geometry-secondary" data-testid="geometry-mesh-element-cancel"
              disabled={!pendingRef.current} onClick={() => {
                pendingRef.current?.abort();
                pendingRef.current = null;
                generationRef.current += 1;
                setEntry((current) => current ? { ...current, status: 'cancelled', chartResult: undefined,
                  message: undefined } : undefined);
              }}>{zh ? '取消读取' : 'Cancel read'}</button>
          </div>
          {(!backend.supports('geometry.mesh.findElement') || !backend.supports('geometry.mesh.elementPropertyChart')) &&
            <p className="mesh-element-inspector__notice" role="status">{zh ? '当前服务暂不提供网格元素定位和属性曲线读取。' : 'Mesh element lookup and property-chart reading are unavailable from the current service.'}</p>}
        </section>

        <section className="mesh-element-inspector__preview" aria-label={zh ? '网格单元预览' : 'Source mesh preview'}>
          <div className="mesh-element-inspector__preview-heading">
            <strong>{zh ? '网格预览' : 'Mesh preview'}</strong>
            {preview && flowArea && <span>{zh ? `当前区域显示 ${visiblePreviewCells.length} / ${flowArea.cellCount} 个单元` : `${visiblePreviewCells.length} / ${flowArea.cellCount} cells shown for this area`}</span>}
          </div>
          {parsedPreview.error && <div className="mesh-element-inspector__error" role="alert"><span>{parsedPreview.error}</span><RawDiagnosticDetails message={parsedPreview.diagnostic} language={language} /></div>}
          {!parsedPreview.error && (preview || selectedFind) && (visiblePreviewCells.length > 0 || selectedFind) && viewBox && displayedMapExtent && (
            <svg className="mesh-element-inspector__map" data-testid="geometry-mesh-element-map" viewBox={viewBox} role="img"
              aria-label={zh ? '按原始工程坐标绘制的网格单元预览' : 'Mesh cells drawn in source project coordinates'}>
              {visiblePreviewCells.map((cell) => {
                const selected = activeEntry?.flowAreaID === cell.flowAreaID && activeEntry.elementKind === 'Cell' && activeEntry.elementIndex === cell.cellIndex;
                return <polygon key={cell.id} points={polygonPoints(cell.coordinates)} role="button" tabIndex={disabled ? -1 : 0}
                  aria-label={zh ? `${cell.flowAreaName} 单元 ${cell.cellIndex}` : `${cell.flowAreaName} cell ${cell.cellIndex}`}
                  aria-pressed={selected} data-testid={`geometry-mesh-map-cell-${cell.cellIndex}`}
                  data-flow-area-id={cell.flowAreaID} data-element-kind="Cell" data-element-index={cell.cellIndex}
                  className={`mesh-element-inspector__cell${selected ? ' is-selected' : ''}`}
                  onClick={() => !disabled && selectPreviewCell(cell)}
                  onKeyDown={(event) => {
                    if (!disabled && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); selectPreviewCell(cell); }
                  }} />;
              })}
              {selectedFind && (
                <>
                  {activeEntry?.elementKind === 'Cell'
                    ? <polygon data-testid="geometry-mesh-element-location" points={polygonPoints(selectedFind.coordinates)} className="mesh-element-inspector__located-cell" />
                    : <polyline data-testid="geometry-mesh-element-location" points={polygonPoints(selectedFind.coordinates)} className="mesh-element-inspector__located-face" />}
                  <circle cx={selectedFind.x} cy={-selectedFind.y} r={Math.max(displayedMapExtent.maxX - displayedMapExtent.minX, displayedMapExtent.maxY - displayedMapExtent.minY, 1) * 0.006}
                    className="mesh-element-inspector__location-center" />
                </>
              )}
            </svg>
          )}
          {!parsedPreview.error && (!preview || visiblePreviewCells.length === 0) && !selectedFind && <p className="mesh-element-inspector__empty">{zh ? '当前流动区域没有可绘制的单元预览。仍可输入源索引尝试定位；不会补造几何。' : 'No drawable cell preview is available for this flow area. You can still enter a source index; no geometry is synthesized.'}</p>}
          {preview && <p className="mesh-element-inspector__notice" data-testid="geometry-mesh-preview-budget">
            {zh
              ? `网格预览上限为 ${preview.cellLimit.toLocaleString()} 个单元、${preview.vertexLimit.toLocaleString()} 个顶点；未显示 ${preview.omittedCellCount.toLocaleString()} 个单元，其中 ${preview.invalidCandidateCount.toLocaleString()} 个候选几何无效。点击单元会按其源流动区域和单元索引定位。`
              : `The mesh preview is limited to ${preview.cellLimit.toLocaleString()} cells and ${preview.vertexLimit.toLocaleString()} vertices; ${preview.omittedCellCount.toLocaleString()} cells are omitted, including ${preview.invalidCandidateCount.toLocaleString()} invalid candidates. Clicks retain the source flow area and cell index.`}
          </p>}
        </section>
      </div>

      {activeEntry?.status === 'error' && activeEntry.message && <GeometryError language={language} error={activeEntry.message} style={{ margin: '0 14px 14px' }} />}
      {activeEntry && ['finding', 'charting'].includes(activeEntry.status) && <p className="mesh-element-inspector__status" role="status" data-testid="geometry-mesh-element-status">
        {activeEntry.status === 'finding' ? (zh ? '正在定位所选网格元素…' : 'Locating the selected mesh element…') : (zh ? '定位完成，正在读取属性表…' : 'Located; reading the property table…')}
      </p>}
      {activeEntry?.status === 'requiresPropertyTables' && <div className="mesh-element-inspector__notice is-warning" role="status" data-testid="geometry-mesh-element-requires-tables">
        <strong>{zh ? '属性表尚未计算，当前无法显示曲线。' : 'Property tables are not ready, so no curve can be shown.'}</strong>
        <span>{zh ? '请先在上方“属性表”区域计算属性表，然后重新选择元素。读取操作不会修改源 HDF。' : 'Compute property tables in the existing panel above, then select the element again. Reading does not modify the source HDF.'}</span>
        <RawDiagnosticDetails message={activeEntry.message} language={language} />
      </div>}
      {activeEntry?.status === 'cancelled' && <div className="mesh-element-inspector__status" role="status" data-testid="geometry-mesh-element-cancelled">
        <span>{zh ? '读取已取消。' : 'Reading was canceled.'}</span><RawDiagnosticDetails message={activeEntry.message} language={language} />
      </div>}
      {currentChart && activeEntry?.findResult && <MeshElementCurve language={language} entry={activeEntry} />}
      {activeEntry?.message && activeEntry.status === 'ready' && <p className="mesh-element-inspector__status" role="status">{activeEntry.message}</p>}
    </section>
  );
}

function MeshElementCurve({ language, entry }: { language: Language; entry: InspectorEntry }) {
  const chart = entry.chartResult;
  const zh = language === 'zh';
  const visual = useMemo(() => {
    if (!chart || chart.state !== 'ready') return null;
    const domain = chartDomain(chart.points);
    if (!domain) return null;
    const points = chart.points.map((point) => chartPoint(point, domain));
    return { points, polyline: points.map((point) => `${point.x},${point.y}`).join(' ') };
  }, [chart]);
  if (!chart || chart.state !== 'ready' || !visual) return null;
  return (
    <section className="mesh-element-inspector__curve" data-testid="geometry-mesh-element-chart" aria-label={zh ? '网格元素属性曲线' : 'Mesh element property curve'}>
      <div className="mesh-element-inspector__curve-heading">
        <div>
          <h3>{zh ? `${entry.elementKind === 'Cell' ? '单元' : '面'} ${entry.elementIndex} ${chart.xQuantity === 'Volume' ? '库容—高程' : '过水面积—高程'}曲线` : `${entry.elementKind} ${entry.elementIndex} ${chart.xQuantity}–${chart.yQuantity} curve`}</h3>
          <p>{chart.flowAreaName} · {zh ? `第 ${chart.flowAreaIndex + 1} 个流动区域` : `Flow Area ${chart.flowAreaIndex + 1}`}</p>
        </div>
        <span className="geometry-count-pill">{chart.points.length} {zh ? '个属性表数据点' : 'property-table points'}</span>
      </div>
      <svg className="mesh-element-inspector__chart" viewBox="0 0 640 240" role="img"
        aria-label={`${axisLabel(chart.yQuantity, chart.yUnits, language)} ${zh ? '对' : 'versus'} ${axisLabel(chart.xQuantity, chart.xUnits, language)}`}>
        <line x1="58" y1="18" x2="58" y2="202" className="mesh-element-inspector__axis" />
        <line x1="58" y1="202" x2="616" y2="202" className="mesh-element-inspector__axis" />
        <polyline points={visual.polyline} data-testid="geometry-mesh-element-curve-line" data-point-count={chart.points.length} className="mesh-element-inspector__curve-line" />
        {chart.points.map((point, index) => {
          const mapped = visual.points[index];
          return <circle key={`${index}:${point.x}:${point.y}`} cx={mapped.x} cy={mapped.y} r="3.25"
            data-testid={`geometry-mesh-element-curve-point-${index}`} data-point-index={index} data-x={point.x} data-y={point.y}
            className="mesh-element-inspector__curve-point"><title>{`${point.x}, ${point.y}`}</title></circle>;
        })}
        <text x="337" y="235" textAnchor="middle" className="mesh-element-inspector__axis-label">{axisLabel(chart.xQuantity, chart.xUnits, language)}</text>
        <text x="15" y="110" textAnchor="middle" transform="rotate(-90 15 110)" className="mesh-element-inspector__axis-label">{axisLabel(chart.yQuantity, chart.yUnits, language)}</text>
      </svg>
      <p className="mesh-element-inspector__notice" data-testid="geometry-mesh-element-curve-source">
        {zh ? `${chart.xQuantity === 'Volume' ? '库容—高程' : '过水面积—高程'}属性表，${chart.points.length} 个数据点。` : `${chart.xQuantity}–${chart.yQuantity} property table with ${chart.points.length} data points.`}
      </p>
      <details className="mesh-element-inspector__data" data-testid="geometry-mesh-element-curve-data">
        <summary>{zh ? '查看属性表数值' : 'View property-table values'}</summary>
        <div className="mesh-element-inspector__table-scroll">
          <table>
            <thead><tr><th>{axisLabel(chart.xQuantity, chart.xUnits, language)}</th><th>{axisLabel(chart.yQuantity, chart.yUnits, language)}</th></tr></thead>
            <tbody>{chart.points.map((point, index) => <tr key={`${index}:${point.x}:${point.y}`} data-testid={`geometry-mesh-element-curve-data-${index}`}>
              <td>{point.x}</td><td>{point.y}</td>
            </tr>)}</tbody>
          </table>
        </div>
      </details>
    </section>
  );
}

function RawDiagnosticDetails({ message, language }: { message?: string; language: Language }) {
  if (!message) return null;
  return <details className="mesh-element-inspector__diagnostic">
    <summary>{language === 'zh' ? '服务原始诊断' : 'Raw service diagnostic'}</summary>
    <pre>{message}</pre>
  </details>;
}

export default MeshElementInspector;
