import { useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react';
import type { BackendGateway } from '../../shared/workbench';
import { projectXmlSha, type MapDocumentCatalog, type MapDocumentSummary, type MapTreeSnapshot } from '../map/ProjectTreeListener';
import type { CatalogTreeNode } from '../map/mapTypes';
import type {
  TerrainInputMapContextData,
  TerrainInputMapContextRequest,
  TerrainMapProjectOwner,
  TerrainMapTreeRequest,
  TerrainMapViewport,
  TerrainPolygonEditSettings,
  TerrainTopologyCommand,
  TerrainTopologyCoordinate,
  TerrainTopologyData,
  TerrainTopologyFeature,
  TerrainTopologyResponse,
} from './terrainTopologyTypes';
import { TerrainModCreator, type TerrainModTool } from './TerrainModCreator';
import { TerrainModLayerViewer } from './TerrainModLayerViewer';
import { TerrainModPicker } from './TerrainModPicker';
import { TerrainModStats } from './TerrainModStats';
import './terrain-polygon-editor.css';

const polygonSourceClass = 'PolygonRasterModTerrainInputVisualContext';
type CapturedMap = { summary: MapDocumentSummary; request: TerrainMapTreeRequest; roots: CatalogTreeNode[]; nodes: CatalogTreeNode[] };
type WorldExtent = Pick<TerrainMapViewport, 'minX' | 'minY' | 'maxX' | 'maxY'>;

/** Terrain polygon modifications use an independent camera and only Host-captured map coordinates. */
export function TerrainPolygonMapEditor({
  backend,
  owner,
  terrainPath,
  inputId,
  inputName,
  inputIndex,
  sourceRevision,
  canEdit,
  onRun,
}: {
  backend?: BackendGateway;
  owner?: TerrainMapProjectOwner;
  terrainPath: string;
  inputId: string;
  inputName: string;
  inputIndex?: number | null;
  sourceRevision: string;
  canEdit: boolean;
  onRun?: (command: TerrainTopologyCommand) => Promise<TerrainTopologyResponse | null>;
}) {
  const ownerKey = owner ? JSON.stringify([owner.projectPath, owner.projectXml, owner.projectRevision, owner.projectSessionID, owner.coreLayerContext]) : '';
  const propsRef = useRef({ backend, owner, terrainPath, inputId, inputName, inputIndex, sourceRevision, canEdit, onRun });
  propsRef.current = { backend, owner, terrainPath, inputId, inputName, inputIndex, sourceRevision, canEdit, onRun };
  const generationRef = useRef(0);
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const pendingCameraFit = useRef<WorldExtent | null>(null);
  const [catalog, setCatalog] = useState<MapDocumentCatalog | null>(null);
  const [mapsBusy, setMapsBusy] = useState(false);
  const [mapsError, setMapsError] = useState('');
  const [selectedMapIndex, setSelectedMapIndex] = useState('');
  const [mapBusy, setMapBusy] = useState(false);
  const [mapCapture, setMapCapture] = useState<CapturedMap | null>(null);
  const [selectedNodeID, setSelectedNodeID] = useState('');
  const [sourceCapture, setSourceCapture] = useState<TerrainInputMapContextData | null>(null);
  const [topology, setTopology] = useState<TerrainTopologyData | null>(null);
  const [settings, setSettings] = useState<TerrainPolygonEditSettings | null>(null);
  const [camera, setCamera] = useState<WorldExtent | null>(null);
  const [extentText, setExtentText] = useState({ minX: '', minY: '', maxX: '', maxY: '' });
  const [canvasSize, setCanvasSize] = useState({ width: 0, height: 0 });
  const [tool, setTool] = useState<TerrainModTool>('select');
  const [pendingPoints, setPendingPoints] = useState<TerrainTopologyCoordinate[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [diagnostic, setDiagnostic] = useState('');

  useEffect(() => {
    generationRef.current++;
    setCatalog(null);
    setMapsBusy(false);
    setMapsError('');
    setSelectedMapIndex('');
    setMapCapture(null);
    setSelectedNodeID('');
    setSourceCapture(null);
    setTopology(null);
    setSettings(null);
    setCamera(null);
    pendingCameraFit.current = null;
    setExtentText({ minX: '', minY: '', maxX: '', maxY: '' });
    setPendingPoints([]);
    setTool('select');
    setBusy(false);
    setMessage('');
    setDiagnostic('');
    return () => { generationRef.current++; };
  }, [ownerKey, terrainPath, inputId, inputIndex, sourceRevision]);

  useEffect(() => {
    const element = canvasRef.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver((entries) => {
      const bounds = entries[0]?.contentRect;
      if (!bounds || !Number.isFinite(bounds.width) || !Number.isFinite(bounds.height)) return;
      setCanvasSize({ width: Math.floor(bounds.width), height: Math.floor(bounds.height) });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, [sourceCapture, topology, camera]);

  useEffect(() => {
    const target = pendingCameraFit.current;
    if (!target || canvasSize.width <= 0 || canvasSize.height <= 0) return;
    pendingCameraFit.current = null;
    setCamera(fitExtent(target, canvasSize.width, canvasSize.height));
  }, [canvasSize.width, canvasSize.height]);

  const selectedMap = useMemo(() => catalog?.maps.find((map) => String(map.mapIndex) === selectedMapIndex) ?? null,
    [catalog, selectedMapIndex]);
  const matchingNodes = mapCapture?.nodes ?? [];
  const selectedNode = matchingNodes.find((node) => node.id === selectedNodeID) ?? null;
  const features = topology?.features ?? (sourceCapture?.features.map((feature) => ({
    id: feature.id,
    kind: feature.kind,
    points: feature.points,
    rings: [feature.points],
    metadata: [],
  })) ?? []);
  const selectedIDs = topology?.selectedFeatureIDs ?? [];
  const selectedFeatureIDs = new Set(selectedIDs);
  const selectedFeatures = features.filter((feature) => selectedFeatureIDs.has(feature.id));
  const viewport = camera && canvasSize.width > 0 && canvasSize.height > 0
    ? { ...camera, pixelWidth: canvasSize.width, pixelHeight: canvasSize.height }
    : null;
  const canUseCanvas = !!sourceCapture && !!topology && !!camera && !!viewport && !busy && !mapBusy;
  const canRun = canEdit && !!onRun && canUseCanvas;
  const polygonsReady = topology?.inputId === inputId && topology.sourceRevision === sourceRevision;

  async function readMapList() {
    const current = propsRef.current;
    const backendGateway = current.backend;
    const project = current.owner;
    if (!backendGateway || !project?.projectPath || !project.projectRevision || !project.projectXml) {
      setMapsError('请先打开已保存的工程。');
      return;
    }
    if (!backendGateway.supports('map.document.list') || !backendGateway.supports('map.tree.load')
      || !backendGateway.supports('map.terrainInput.render')) {
      setMapsError('当前服务未提供地形地图读取能力。');
      return;
    }
    const generation = generationRef.current;
    setMapsBusy(true);
    setMapsError('');
    setMessage('');
    setDiagnostic('');
    try {
      const xmlSHA = await projectXmlSha(project.projectXml);
      if (!xmlSHA) throw new Error('The project XML snapshot could not be fingerprinted.');
      const result = await backendGateway.request<MapDocumentCatalog>('map.document.list', {
        projectPath: project.projectPath,
        projectXml: project.projectXml,
        projectXmlSHA: xmlSHA,
        expectedProjectRevision: project.projectRevision,
      });
      if (generation !== generationRef.current) return;
      if (result.projectRevision !== project.projectRevision || !Array.isArray(result.maps)
        || (result.maps.length > 0 && !result.visualizationRevision)
        || result.maps.some((map) => !Number.isInteger(map.mapIndex) || map.mapIndex < 0
          || !map.mapName?.trim() || !map.mapType?.trim()))
        throw new Error('The saved map catalog does not match the current project revision.');
      setCatalog(result);
      setSelectedMapIndex(result.maps[0] ? String(result.maps[0].mapIndex) : '');
      setMapCapture(null);
      setSourceCapture(null);
      setTopology(null);
      setCamera(null);
      pendingCameraFit.current = null;
      setMessage(result.maps.length ? '已读取工程地图。选择地图后读取其地形输入。' : '当前工程没有可读取的已保存地图。');
    } catch (error) {
      if (generation === generationRef.current) {
        setMapsError(errorMessage(error));
        setDiagnostic(errorMessage(error));
      }
    } finally {
      if (generation === generationRef.current) setMapsBusy(false);
    }
  }

  async function readSelectedMapTree() {
    const current = propsRef.current;
    const backendGateway = current.backend;
    const project = current.owner;
    const map = selectedMap;
    const mapRevision = catalog?.visualizationRevision;
    if (!backendGateway || !project || !map || !mapRevision || current.inputIndex == null) return;
    const generation = generationRef.current;
    setMapBusy(true);
    setMessage('');
    setDiagnostic('');
    setMapCapture(null);
    setSourceCapture(null);
    setTopology(null);
    setCamera(null);
    pendingCameraFit.current = null;
    try {
      const xmlSHA = await projectXmlSha(project.projectXml);
      if (!xmlSHA) throw new Error('The project XML snapshot could not be fingerprinted.');
      const request: TerrainMapTreeRequest = {
        projectPath: project.projectPath,
        projectXml: project.projectXml,
        projectXmlSHA: xmlSHA,
        mapID: crypto.randomUUID(),
        mapIndex: map.mapIndex,
        mapName: map.mapName,
        mapType: map.mapType,
        expectedVisualizationRevision: mapRevision,
        projectSessionID: project.projectSessionID,
        ...(project.externalCoreLayerReferences ? { externalCoreLayerReferences: project.externalCoreLayerReferences } : {}),
        ...(project.coreLayerContext ? { coreLayerContext: project.coreLayerContext } : {}),
      };
      const tree = await backendGateway.request<MapTreeSnapshot>('map.tree.load', request);
      if (generation !== generationRef.current) return;
      if (!Array.isArray(tree.roots) || tree.visualizationRevision !== mapRevision)
        throw new Error('The map tree changed while it was being read. Reload the map list.');
      const allNodes = flattenNodes(tree.roots);
      const nodes = allNodes.filter((node) => node.sourceClass === polygonSourceClass
        && node.locator?.nodeType === 'TerrainInput'
        && samePath(node.assetID ?? '', current.terrainPath)
        && nodeInputIndex(node) === current.inputIndex);
      setMapCapture({ summary: map, request, roots: tree.roots, nodes });
      setSelectedNodeID(nodes.length === 1 ? nodes[0].id : '');
      setMessage(nodes.length
        ? `找到 ${nodes.length} 个与“${current.inputName}”路径和来源序号一致的地图输入。`
        : '所选地图没有找到与当前地形输入路径和来源序号完全匹配的保存节点。');
      if (nodes.length === 0) setDiagnostic('No exact saved PolygonRasterMod TerrainInput node matched the selected terrain path and SourceVectorIndex.');
    } catch (error) {
      if (generation === generationRef.current) {
        setMessage('无法读取所选地图图层。');
        setDiagnostic(errorMessage(error));
      }
    } finally {
      if (generation === generationRef.current) setMapBusy(false);
    }
  }

  async function readSourceLayer() {
    const current = propsRef.current;
    const backendGateway = current.backend;
    const project = current.owner;
    const capture = mapCapture;
    const node = selectedNode;
    if (!backendGateway || !project || !capture || !node || current.inputIndex == null) return;
    const generation = generationRef.current;
    setMapBusy(true);
    setMessage('');
    setDiagnostic('');
    setSourceCapture(null);
    setTopology(null);
    setCamera(null);
    pendingCameraFit.current = null;
    setPendingPoints([]);
    try {
      const request = createMapContext(capture.request, node, current.terrainPath,
        current.inputIndex, current.sourceRevision);
      const rendered = await backendGateway.request<TerrainInputMapContextData>('map.terrainInput.render', request);
      if (generation !== generationRef.current) return;
      validateMapRender(rendered, request, current.sourceRevision);
      setSourceCapture(rendered);
      if (rendered.features.length > 0) {
        const extent = boundsForFeatures(rendered.features.map((feature) => feature.points));
        if (!extent) throw new Error('The map returned source polygons without a finite extent.');
        setCameraFromExtent(extent);
        setMessage(`已读取 ${rendered.features.length} 个源多边形。确认画布后读取修改状态。`);
      } else {
        setExtentText({ minX: '', minY: '', maxX: '', maxY: '' });
        setMessage('该保存输入尚无多边形。请输入实际工程坐标范围后，才能在空图层上编辑。');
      }
    } catch (error) {
      if (generation === generationRef.current) {
        setMessage('无法读取该地形修改图层。');
        setDiagnostic(errorMessage(error));
      }
    } finally {
      if (generation === generationRef.current) setMapBusy(false);
    }
  }

  function currentMapContext(): TerrainInputMapContextRequest | null {
    if (!mapCapture || !selectedNode || !sourceCapture || !camera || !viewport) return null;
    return createMapContext(mapCapture.request, selectedNode, terrainPath, sourceCapture.inputIndex,
      sourceCapture.terrainRevision, viewport);
  }

  async function runTopology(operation: TerrainTopologyCommand['operation'], fields: Omit<TerrainTopologyCommand, 'inputId' | 'operation' | 'expectedDraftIdentity' | 'mapContext'> = {}) {
    const current = propsRef.current;
    const callback = current.onRun;
    const capturedTopology = topology;
    const mapContext = currentMapContext();
    if (!callback || !mapContext || busy || mapBusy) {
      setMessage('当前地图捕获或地形拓扑尚未就绪；请重新读取后再操作。');
      return false;
    }
    if (operation !== 'load' && (!capturedTopology || capturedTopology.inputId !== current.inputId
      || capturedTopology.sourceRevision !== current.sourceRevision || !capturedTopology.draftIdentity)) {
      setMessage('修改快照已过期，请重新读取修改状态。');
      return false;
    }
    const generation = generationRef.current;
    const request: TerrainTopologyCommand = {
      inputId: current.inputId,
      operation,
      ...fields,
      mapContext,
      ...(operation === 'load' ? {} : { expectedDraftIdentity: capturedTopology!.draftIdentity }),
    };
    setBusy(true);
    setMessage('');
    setDiagnostic('');
    try {
      const response = await callback(request);
      if (generation !== generationRef.current) return false;
      if (!response) {
        setMessage('服务未接纳此修改回执；保留上次确认内容，请重新读取后再试。');
        return false;
      }
      const issue = validateTopology(response, current.inputId, current.sourceRevision,
        sourceCapture?.inputIndex ?? -1, selectedNode?.id ?? '');
      if (issue) throw new Error(issue);
      setTopology(response.topology);
      if (response.topology.polygonSettings) setSettings(response.topology.polygonSettings);
      setMessage(operation === 'load' || operation === 'select' || operation === 'selectAt'
        ? `已读取 ${response.topology.features.length} 个当前地形修改。`
        : '地形修改已进入上方地形草稿；点击“保存”后才会写入文件。');
      return true;
    } catch (error) {
      if (generation === generationRef.current) {
        setMessage('地形多边形操作失败。');
        setDiagnostic(errorMessage(error));
      }
      return false;
    } finally {
      if (generation === generationRef.current) setBusy(false);
    }
  }

  function setManualExtent() {
    if (Object.values(extentText).some((value) => value.trim() === '')) {
      setMessage('请填写四个工程坐标范围值。');
      return;
    }
    const parsed = Object.fromEntries(Object.entries(extentText).map(([key, value]) => [key, Number(value)])) as Record<keyof WorldExtent, number>;
    const value: WorldExtent = parsed;
    if (!validExtent(value)) {
      setMessage('请输入有限且不重合的最小、最大地图坐标。');
      return;
    }
    setCameraFromExtent(value);
    setMessage('已采用你输入的工程坐标范围。');
    setDiagnostic('');
  }

  function pointFromEvent(event: ReactMouseEvent<SVGSVGElement>): TerrainTopologyCoordinate | null {
    const svg = event.currentTarget;
    const rect = svg.getBoundingClientRect();
    if (!camera || rect.width <= 0 || rect.height <= 0) return null;
    const x = camera.minX + ((event.clientX - rect.left) / rect.width) * (camera.maxX - camera.minX);
    const y = camera.maxY - ((event.clientY - rect.top) / rect.height) * (camera.maxY - camera.minY);
    return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
  }

  async function handleCanvasClick(event: ReactMouseEvent<SVGSVGElement>) {
    if (!canUseCanvas || !canEdit || !settings) return;
    const point = pointFromEvent(event);
    if (!point) return;
    if (tool === 'draw') {
      setPendingPoints((current) => [...current, point]);
      return;
    }
    if (tool === 'stamp') {
      if (settings.shape === 'Custom') {
        setMessage('自定义形状请使用“绘制多边形”，放置形状仅支持三角形和矩形。');
        return;
      }
      const cell = worldUnitsPerPixel();
      if (!cell) return;
      await runTopology('stamp', { points: [point], mapCellSize: cell, polygonSettings: settings });
      return;
    }
    const cell = worldUnitsPerPixel();
    if (!cell) return;
    await runTopology('selectAt', { points: [point], mapCellSize: cell });
  }

  function worldUnitsPerPixel(): number | null {
    if (!camera || canvasSize.width <= 0 || canvasSize.height <= 0) return null;
    const value = Math.max((camera.maxX - camera.minX) / canvasSize.width,
      (camera.maxY - camera.minY) / canvasSize.height) * 4;
    return Number.isFinite(value) && value > 0 ? value : null;
  }

  async function finishDraw() {
    if (!settings || pendingPoints.length < 3) return;
    const done = await runTopology('draw', { points: pendingPoints, polygonSettings: settings });
    if (done) setPendingPoints([]);
  }

  function selectFeature(feature: TerrainTopologyFeature, wasSelected: boolean) {
    const current = new Set(topology?.selectedFeatureIDs ?? []);
    if (wasSelected) current.delete(feature.id);
    else current.add(feature.id);
    void runTopology('select', { featureIDs: [...current] });
  }

  function zoom(factor: number) {
    if (!camera) return;
    pendingCameraFit.current = null;
    const centerX = (camera.minX + camera.maxX) / 2;
    const centerY = (camera.minY + camera.maxY) / 2;
    const halfWidth = (camera.maxX - camera.minX) * factor / 2;
    const halfHeight = (camera.maxY - camera.minY) * factor / 2;
    setCamera({ minX: centerX - halfWidth, maxX: centerX + halfWidth, minY: centerY - halfHeight, maxY: centerY + halfHeight });
  }

  function setCameraFromExtent(extent: WorldExtent) {
    if (canvasSize.width > 0 && canvasSize.height > 0) {
      pendingCameraFit.current = null;
      setCamera(fitExtent(extent, canvasSize.width, canvasSize.height));
    } else {
      pendingCameraFit.current = extent;
      setCamera(extent);
    }
  }

  function fitSource() {
    const paths = topology?.features.map((feature) => feature.points)
      ?? sourceCapture?.features.map((feature) => feature.points) ?? [];
    const extent = boundsForFeatures(paths);
    if (!extent) {
      setMessage('没有可用于自动缩放的已确认多边形。');
      return;
    }
    setCameraFromExtent(extent);
  }

  const canDraw = canRun && settings !== null && validPolygonSettings(settings);
  const canStamp = canDraw && settings?.shape !== 'Custom';

  return <section className="editor-terrain-card terrain-polygon-editor" data-testid="terrain-polygon-map-editor">
    <header className="terrain-polygon-heading">
      <div><h3>地形多边形修改</h3><p>地图坐标、修改要素和输入身份均来自当前工程的保存图层。</p></div>
      <button type="button" className="editor-button" data-testid="terrain-polygon-read-maps" disabled={mapsBusy || mapBusy || busy} onClick={() => void readMapList()}>
        {mapsBusy ? '读取地图…' : '读取工程地图'}
      </button>
    </header>

    <div className="terrain-polygon-capture-controls">
      <label className="editor-field">
        <span>保存地图</span>
        <select data-testid="terrain-polygon-map-select" value={selectedMapIndex} disabled={!catalog || mapsBusy || mapBusy || busy} onChange={(event) => {
          setSelectedMapIndex(event.target.value);
          setMapCapture(null);
          setSourceCapture(null);
          setTopology(null);
          setCamera(null);
          pendingCameraFit.current = null;
          setSelectedNodeID('');
        }}>
          <option value="">请选择地图</option>
          {(catalog?.maps ?? []).map((map) => <option key={map.mapIndex} value={map.mapIndex}>{map.mapName}</option>)}
        </select>
      </label>
      <button type="button" className="editor-button" data-testid="terrain-polygon-read-tree" disabled={!selectedMap || mapBusy || busy || inputIndex == null} onClick={() => void readSelectedMapTree()}>
        {mapBusy ? '读取中…' : '读取地图图层'}
      </button>
      <label className="editor-field terrain-polygon-node-select">
        <span>当前地形输入</span>
        <select data-testid="terrain-polygon-source-node" value={selectedNodeID} disabled={!mapCapture || mapBusy || busy} onChange={(event) => {
          setSelectedNodeID(event.target.value);
          setSourceCapture(null);
          setTopology(null);
          setCamera(null);
          pendingCameraFit.current = null;
        }}>
          <option value="">请选择精确匹配的图层</option>
          {matchingNodes.map((node) => <option key={node.id} value={node.id}>{node.name} · 序号 {nodeInputIndex(node)}</option>)}
        </select>
      </label>
      <button type="button" className="editor-button" data-testid="terrain-polygon-read-source" disabled={!selectedNode || mapBusy || busy || inputIndex == null} onClick={() => void readSourceLayer()}>
        读取源修改
      </button>
      <button type="button" className="editor-button" data-testid="terrain-polygon-load" disabled={!sourceCapture || !camera || canvasSize.width <= 0 || canvasSize.height <= 0 || busy || mapBusy || !onRun} onClick={() => void runTopology('load')}>
        读取修改状态
      </button>
    </div>

    {inputIndex == null && <p className="terrain-polygon-notice">此输入尚未关联到已保存的地图来源。请先在上方地形编辑器中保存工程，再重新读取地图图层。</p>}
    {mapsError && <p className="editor-inline-error" role="alert">{mapsError}</p>}
    {message && <p className="terrain-polygon-message" role="status">{message}</p>}
    {diagnostic && <details className="terrain-polygon-diagnostic"><summary>原始诊断</summary><pre>{diagnostic}</pre></details>}

    {sourceCapture && <>
      {!camera && <div className="terrain-polygon-extent-form">
        <p>没有可用于确定视窗的源多边形。请填写工程坐标范围；这里不会替你猜地图位置。</p>
        {(['minX', 'minY', 'maxX', 'maxY'] as const).map((key) => <label className="editor-field" key={key}>
          <span>{{ minX: '最小 X', minY: '最小 Y', maxX: '最大 X', maxY: '最大 Y' }[key]}</span>
          <input type="number" step="any" data-testid={`terrain-polygon-extent-${key}`} value={extentText[key]} onChange={(event) => setExtentText((current) => ({ ...current, [key]: event.currentTarget.value }))} />
        </label>)}
        <button type="button" className="editor-button" data-testid="terrain-polygon-extent-apply" onClick={setManualExtent}>采用坐标范围</button>
      </div>}

      {camera && <div className="terrain-polygon-workspace">
        <div className="terrain-polygon-main">
          <TerrainModCreator
            tool={tool}
            onToolChange={(next) => { setTool(next); if (next !== 'draw') setPendingPoints([]); }}
            canEdit={canEdit && polygonsReady && !!settings && !!camera}
            canDraw={canDraw}
            canStamp={canStamp}
            pendingPointCount={pendingPoints.length}
            selectedCount={selectedFeatures.length}
            canUndo={topology?.canUndo === true}
            canRedo={topology?.canRedo === true}
            busy={busy || mapBusy}
            onFinishDraw={() => void finishDraw()}
            onCancelDraw={() => setPendingPoints([])}
            onRemove={() => void runTopology('remove', { featureIDs: selectedIDs })}
            onUndo={() => void runTopology('undo')}
            onRedo={() => void runTopology('redo')}
          />
          <div className="terrain-polygon-camera-tools">
            <span>坐标范围来自 {sourceCapture.features.length ? 'Host 源多边形' : '你输入的工程坐标'}</span>
            <button type="button" className="editor-button" disabled={!camera || busy} onClick={() => zoom(0.8)}>放大</button>
            <button type="button" className="editor-button" disabled={!camera || busy} onClick={() => zoom(1.25)}>缩小</button>
            <button type="button" className="editor-button" disabled={busy} onClick={fitSource}>缩放至修改范围</button>
          </div>
          <div className="terrain-polygon-canvas" data-testid="terrain-polygon-canvas" ref={canvasRef}>
            {canvasSize.width > 0 && canvasSize.height > 0 && <svg
              aria-label="地形修改地图"
              role="img"
              viewBox={`0 0 ${canvasSize.width} ${canvasSize.height}`}
              preserveAspectRatio="none"
              onClick={(event) => void handleCanvasClick(event)}
              style={{ cursor: tool === 'draw' ? 'crosshair' : tool === 'stamp' ? 'copy' : 'default' }}
            >
              <rect x="0" y="0" width={canvasSize.width} height={canvasSize.height} fill="#f7faf9" />
              {features.map((feature) => {
                const ring = feature.rings?.[0]?.length ? feature.rings[0] : feature.points;
                if (ring.length < 3) return null;
                const pointList = ring.map((point) => `${mapX(point.x, camera, canvasSize.width)},${mapY(point.y, camera, canvasSize.height)}`).join(' ');
                const isSelected = selectedFeatureIDs.has(feature.id);
                return <polygon
                  key={feature.id}
                  points={pointList}
                  fill={isSelected ? 'rgba(216, 116, 48, 0.35)' : 'rgba(41, 112, 105, 0.26)'}
                  stroke={isSelected ? '#b95321' : '#236c66'}
                  strokeWidth={isSelected ? 2.5 : 1.5}
                  vectorEffect="non-scaling-stroke"
                  data-terrain-feature-id={feature.id}
                />;
              })}
              {pendingPoints.length > 0 && <>
                <polyline points={pendingPoints.map((point) => `${mapX(point.x, camera, canvasSize.width)},${mapY(point.y, camera, canvasSize.height)}`).join(' ')} fill="none" stroke="#d86b28" strokeWidth="2" vectorEffect="non-scaling-stroke" />
                {pendingPoints.map((point, index) => <circle key={index} cx={mapX(point.x, camera, canvasSize.width)} cy={mapY(point.y, camera, canvasSize.height)} r="4" fill="#d86b28" />)}
              </>}
            </svg>}
            {busy && <div className="terrain-polygon-busy" role="status">正在确认地形操作…</div>}
          </div>
          <div className="terrain-polygon-coordinate-status" data-testid="terrain-polygon-camera-extent">
            {camera ? `X ${format(camera.minX)}–${format(camera.maxX)} · Y ${format(camera.minY)}–${format(camera.maxY)}` : '尚未设置坐标范围'}
          </div>
        </div>
        <aside className="terrain-polygon-sidebar">
          {settings
            ? <>
              <TerrainModPicker value={settings.shape} disabled={!canEdit || busy} onChange={(shape) => setSettings((current) => current ? { ...current, shape } : current)} />
          <TerrainModStats value={settings} disabled={!canEdit || busy} onChange={(next) => setSettings(next)} />
            </>
            : <p className="editor-field-muted">读取修改状态后显示当前 Host 参数。</p>}
          <TerrainModLayerViewer features={features} selectedIDs={selectedIDs} disabled={!polygonsReady || !canEdit || busy} onSelect={selectFeature} />
          <p className="terrain-polygon-save-boundary">所有更改先进入地形草稿；上方“保存”才会写入地形文件。</p>
        </aside>
      </div>}
      {!camera && <div className="terrain-polygon-canvas terrain-polygon-canvas-measure" ref={canvasRef} aria-hidden="true" />}
    </>}
  </section>;
}

function createMapContext(
  map: TerrainMapTreeRequest,
  node: CatalogTreeNode,
  terrainPath: string,
  inputIndex: number,
  terrainRevision: string,
  viewport?: TerrainMapViewport,
): TerrainInputMapContextRequest {
  return {
    map,
    inputNodeID: node.id,
    expectedSourceClass: polygonSourceClass,
    expectedTerrainPath: node.assetID ?? terrainPath,
    expectedInputIndex: inputIndex,
    expectedTerrainRevision: terrainRevision,
    ...(viewport ? { renderOptions: { viewport } } : {}),
  };
}

function validateMapRender(data: TerrainInputMapContextData, request: TerrainInputMapContextRequest, sourceRevision: string): void {
  if (data.inputNodeID !== request.inputNodeID || data.inputIndex !== request.expectedInputIndex
    || data.sourceClass !== polygonSourceClass || data.terrainRevision.toUpperCase() !== sourceRevision.toUpperCase()
    || !samePath(data.terrainPath, request.expectedTerrainPath) || !Array.isArray(data.features))
    throw new Error('The source terrain input changed while its map layer was being read.');
  const prefix = `${data.inputNodeID}:feature:`;
  const ids = data.features.map((feature) => feature.id);
  if (ids.some((id) => !id.startsWith(prefix)) || new Set(ids).size !== ids.length
    || data.features.some((feature) => !feature.closed || feature.points.length < 3
      || feature.points.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y))))
    throw new Error('The source map returned invalid terrain modification features.');
}

function validateTopology(response: TerrainTopologyResponse, inputId: string, revision: string,
  inputIndex: number, inputNodeID: string): string | null {
  const data = response?.topology;
  if (!response?.snapshot || !data || data.inputId !== inputId || data.sourceRevision?.toUpperCase() !== revision.toUpperCase()
    || data.inputIndex !== inputIndex || !isSha256(data.draftIdentity)
    || !Array.isArray(data.features) || !Array.isArray(data.selectedFeatureIDs))
    return '返回的地形修改状态与当前工程、输入或修订不符。';
  const prefix = `${inputNodeID}:feature:`;
  const ids = data.features.map((feature) => feature.id);
  if (ids.some((id) => !id.startsWith(prefix) || !Number.isInteger(Number(id.slice(prefix.length))))
    || new Set(ids).size !== ids.length
    || data.features.some((feature) => feature.kind !== 'polygon' || feature.points.length < 3
      || feature.points.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y)))
    || data.selectedFeatureIDs.some((id) => !ids.includes(id)))
    return '返回要素没有保留来源图层中的稳定身份或有效坐标。';
  if (data.polygonSettings && !validPolygonSettings(data.polygonSettings))
    return 'Host 返回的修改参数无效。';
  return null;
}

function validPolygonSettings(settings: TerrainPolygonEditSettings): boolean {
  return ['Triangle', 'Rectangle', 'Custom'].includes(settings.shape)
    && Number.isFinite(settings.width) && settings.width > 0
    && Number.isFinite(settings.height) && settings.height > 0
    && Number.isFinite(settings.rotation) && Number.isFinite(settings.value)
    && Math.abs(settings.value) <= 3.4028235e38
    && ['SetValue', 'TakeHigher', 'TakeLower', 'AddValue'].includes(settings.modificationType);
}

function validExtent(extent: WorldExtent): boolean {
  return Number.isFinite(extent.minX) && Number.isFinite(extent.minY)
    && Number.isFinite(extent.maxX) && Number.isFinite(extent.maxY)
    && Number.isFinite(extent.maxX - extent.minX) && Number.isFinite(extent.maxY - extent.minY)
    && extent.maxX > extent.minX && extent.maxY > extent.minY;
}

function flattenNodes(nodes: CatalogTreeNode[]): CatalogTreeNode[] {
  return nodes.flatMap((node) => [node, ...flattenNodes(node.children ?? [])]);
}

function nodeInputIndex(node: CatalogTreeNode): number | null {
  const properties = (node as CatalogTreeNode & { properties?: Array<{ id: string; value: string }> | null }).properties;
  const property = properties?.find((item) => item.id === 'TerrainInput/Index');
  if (!property) return null;
  const index = Number(property.value);
  return Number.isInteger(index) && index >= 0 ? index : null;
}

function samePath(left: string, right: string): boolean {
  const normalize = (value: string) => value.trim().replace(/\\/g, '/').replace(/\/+$/, '');
  return normalize(left) === normalize(right);
}

function boundsForFeatures(paths: TerrainTopologyCoordinate[][]): WorldExtent | null {
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  let count = 0;
  for (const path of paths) {
    for (const point of path) {
      if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) return null;
      count++;
      minX = Math.min(minX, point.x);
      minY = Math.min(minY, point.y);
      maxX = Math.max(maxX, point.x);
      maxY = Math.max(maxY, point.y);
    }
  }
  if (count === 0 || !validExtent({ minX, minY, maxX, maxY })) return null;
  if (!(maxX > minX) || !(maxY > minY)) return null;
  return { minX, minY, maxX, maxY };
}

function fitExtent(extent: WorldExtent, width: number, height: number): WorldExtent {
  const spanX = extent.maxX - extent.minX;
  const spanY = extent.maxY - extent.minY;
  const margin = 0.08;
  let targetX = spanX * (1 + margin * 2);
  let targetY = spanY * (1 + margin * 2);
  if (width > 0 && height > 0) {
    const aspect = width / height;
    if (targetX / targetY < aspect) targetX = targetY * aspect;
    else targetY = targetX / aspect;
  }
  const centerX = (extent.minX + extent.maxX) / 2;
  const centerY = (extent.minY + extent.maxY) / 2;
  return { minX: centerX - targetX / 2, maxX: centerX + targetX / 2, minY: centerY - targetY / 2, maxY: centerY + targetY / 2 };
}

function mapX(x: number, extent: WorldExtent, width: number): number {
  return ((x - extent.minX) / (extent.maxX - extent.minX)) * width;
}

function mapY(y: number, extent: WorldExtent, height: number): number {
  return ((extent.maxY - y) / (extent.maxY - extent.minY)) * height;
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[A-F0-9]{64}$/i.test(value);
}

function format(value: number): string {
  return Number.isFinite(value) ? value.toLocaleString(undefined, { maximumSignificantDigits: 8 }) : '—';
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
