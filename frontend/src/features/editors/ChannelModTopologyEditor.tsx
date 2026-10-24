import { useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent, type PointerEvent as ReactPointerEvent } from 'react';
import type {
  TerrainTopologyCommand,
  TerrainTopologyCoordinate,
  TerrainTopologyData,
  TerrainTopologyFeature,
  TerrainTopologyResponse,
} from './terrainTopologyTypes';

type ToolMode = 'select' | 'draw' | 'move' | 'split' | 'insertVertex';
type ManualPointRow = { id: number; x: string; y: string };

/** ChannelModTopologyEditor port backed only by the current Host topology response. */
export function ChannelModTopologyEditor({
  language,
  inputId,
  sourceRevision,
  referenceExtent,
  referenceExtentSource,
  canEdit,
  onRun,
}: {
  language: 'zh' | 'en';
  inputId: string;
  sourceRevision: string;
  referenceExtent?: { minX: number; maxX: number; minY: number; maxY: number } | null;
  referenceExtentSource?: string | null;
  canEdit: boolean;
  onRun?: (command: TerrainTopologyCommand) => Promise<TerrainTopologyResponse | null>;
}) {
  const [topology, setTopology] = useState<TerrainTopologyData | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [mode, setMode] = useState<ToolMode>('select');
  const [pendingPoints, setPendingPoints] = useState<TerrainTopologyCoordinate[]>([]);
  const [focusedFeatureID, setFocusedFeatureID] = useState<string | null>(null);
  const [splitMode, setSplitMode] = useState<'Anywhere' | 'AtVertex'>('Anywhere');
  const [metadataValues, setMetadataValues] = useState<Record<string, string>>({});
  const [manualPoints, setManualPoints] = useState<ManualPointRow[]>([{ id: 1, x: '', y: '' }, { id: 2, x: '', y: '' }]);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const generationRef = useRef(0);
  const busyRef = useRef(false);
  const skipClickRef = useRef(false);
  const manualPointIDRef = useRef(3);
  const dragRef = useRef<{ pointerId: number; feature: TerrainTopologyFeature; start: TerrainTopologyCoordinate; pointerStart: TerrainTopologyCoordinate } | null>(null);
  const currentRef = useRef({ inputId, sourceRevision, onRun });
  const topologyRef = useRef(topology);
  currentRef.current = { inputId, sourceRevision, onRun };
  topologyRef.current = topology;

  useEffect(() => {
    generationRef.current++;
    busyRef.current = false;
    setBusy(false);
    dragRef.current = null;
    setTopology(null);
    setMessage('');
    setPendingPoints([]);
    setFocusedFeatureID(null);
    setMetadataValues({});
    setMode('select');
    manualPointIDRef.current = 3;
    setManualPoints([{ id: 1, x: '', y: '' }, { id: 2, x: '', y: '' }]);
    return () => {
      generationRef.current++;
      busyRef.current = false;
      dragRef.current = null;
    };
  }, [inputId, sourceRevision]);

  const bounds = useMemo(() => topology ? topologyBounds(topology.features, referenceExtent) : null, [topology, referenceExtent]);
  const manualCoordinateCapture = useMemo(() => captureManualPoints(manualPoints), [manualPoints]);
  const selectedIDs = topology?.selectedFeatureIDs ?? [];
  const activeFeature = topology?.features.find((feature) => feature.id === focusedFeatureID)
    ?? topology?.features.find((feature) => selectedIDs.includes(feature.id))
    ?? null;
  const topologyEnabled = !!topology && !!onRun && !busy;
  const editingEnabled = topologyEnabled && canEdit;
  const canSelect = !!topology && !!onRun && canEdit && !busy;
  const mapCellSize = bounds ? Math.max(bounds.spanX, bounds.spanY) / 100 : manualCoordinateCapture.mapCellSize;

  async function run(operation: TerrainTopologyCommand['operation'], fields: Omit<TerrainTopologyCommand, 'inputId' | 'operation' | 'expectedDraftIdentity'> = {}, focusID?: string | null): Promise<boolean> {
    const current = currentRef.current;
    const capturedTopology = topologyRef.current;
    const callback = current.onRun;
    if (!callback || busyRef.current) return false;
    if (operation !== 'load' && (!capturedTopology || capturedTopology.inputId !== current.inputId
      || capturedTopology.sourceRevision !== current.sourceRevision || !capturedTopology.draftIdentity)) {
      setMessage(language === 'zh' ? '拓扑快照已过期，请重新读取后再编辑。' : 'The topology snapshot is stale. Reload it before editing.');
      return false;
    }

    const generation = generationRef.current;
    const command: TerrainTopologyCommand = {
      inputId: current.inputId,
      operation,
      ...fields,
      ...(operation === 'load' ? {} : { expectedDraftIdentity: capturedTopology!.draftIdentity }),
    };
    busyRef.current = true;
    setBusy(true);
    setMessage('');
    try {
      const response = await callback(command);
      const latest = currentRef.current;
      if (generation !== generationRef.current || latest.inputId !== current.inputId || latest.sourceRevision !== current.sourceRevision) return false;
      if (!response) {
        setMessage(language === 'zh'
          ? '服务未接纳此拓扑回执；保留当前已确认内容，请重新读取。'
          : 'The Host did not accept this topology receipt. Confirmed content was retained; reload before retrying.');
        return false;
      }
      const validation = validateResponse(response, current.inputId, current.sourceRevision);
      if (validation) {
        setMessage(validation);
        return false;
      }
      setTopology(response.topology);
      setFocusedFeatureID(focusID ?? response.topology.selectedFeatureIDs[0] ?? null);
      if (operation === 'draw' || operation === 'remove' || operation === 'undo' || operation === 'redo') setPendingPoints([]);
      if (operation === 'draw') {
        setManualPoints([{ id: 1, x: '', y: '' }, { id: 2, x: '', y: '' }]);
        manualPointIDRef.current = 3;
      }
      if (operation === 'load') {
        setMessage(language === 'zh'
          ? `已读取 ${response.topology.features.length} 个 Host 拓扑要素，源修订 ${response.topology.sourceRevision}。`
          : `Loaded ${response.topology.features.length} Host topology features from revision ${response.topology.sourceRevision}.`);
      }
      return true;
    } catch (error) {
      const detail = error instanceof Error ? error.message : String(error);
      setMessage(language === 'zh' ? `拓扑操作失败（原始诊断）：${detail}` : `Topology operation failed (raw diagnostic): ${detail}`);
      return false;
    } finally {
      if (generation === generationRef.current) {
        busyRef.current = false;
        setBusy(false);
      }
    }
  }

  async function selectFeature(feature: TerrainTopologyFeature) {
    if (!topology || !canSelect) return;
    const relatedIDs = selectionIDsForFeature(feature, topology);
    const isSelected = relatedIDs.some((id) => selectedIDs.includes(id));
    const next = isSelected
      ? selectedIDs.filter((id) => !relatedIDs.includes(id))
      : [...selectedIDs, feature.id];
    await run('select', { featureIDs: next }, feature.id);
  }

  function selectedFeatures(): TerrainTopologyFeature[] {
    if (!topology) return [];
    const selected = new Set(selectedIDs);
    return topology.features.filter((feature) => selected.has(feature.id)
      || (feature.id === focusedFeatureID && featureSelected(feature, topology)));
  }

  function getWorldPoint(event: { clientX: number; clientY: number }): TerrainTopologyCoordinate | null {
    const svg = svgRef.current;
    const matrix = svg?.getScreenCTM();
    if (!svg || !matrix) return null;
    const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    const result = { x: point.x, y: -point.y };
    return Number.isFinite(result.x) && Number.isFinite(result.y) ? result : null;
  }

  function handlePointerDown(event: ReactPointerEvent<SVGSVGElement>) {
    if (mode !== 'move' || !editingEnabled) return;
    const target = event.target instanceof Element ? event.target.closest<SVGElement>('[data-topology-feature-id]') : null;
    const featureID = target?.dataset.topologyFeatureId;
    const feature = topology?.features.find((item) => item.id === featureID);
    if (!feature || !['node', 'arcVertex'].includes(feature.kind)) return;
    const start = getWorldPoint(event);
    if (!start) return;
    event.preventDefault();
    event.stopPropagation();
    const featurePoint = feature.points[0];
    if (!featurePoint || !Number.isFinite(featurePoint.x) || !Number.isFinite(featurePoint.y)) return;
    dragRef.current = {
      pointerId: event.pointerId,
      feature,
      start: { ...featurePoint },
      pointerStart: start,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  async function handlePointerUp(event: ReactPointerEvent<SVGSVGElement>) {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    dragRef.current = null;
    const end = getWorldPoint(event);
    if (!end) return;
    const moved = Math.hypot(end.x - drag.pointerStart.x, end.y - drag.pointerStart.y) > Math.max(mapCellSize * 0.05, Number.EPSILON);
    if (!moved) {
      skipClickRef.current = true;
      window.setTimeout(() => { skipClickRef.current = false; }, 300);
      await selectFeature(drag.feature);
      return;
    }
    skipClickRef.current = true;
    window.setTimeout(() => { skipClickRef.current = false; }, 300);
    if (drag.feature.kind === 'node') {
      await run('moveNodes', { featureIDs: [drag.feature.id], points: [drag.start, end], mapCellSize });
      return;
    }
    if (drag.feature.kind === 'arcVertex' && drag.feature.parentId && drag.feature.vertexIndex != null) {
      await run('moveVertex', { featureIDs: [drag.feature.parentId], points: [drag.start, end], mapCellSize, vertexIndex: drag.feature.vertexIndex });
    }
  }

  async function handleCanvasClick(event: ReactMouseEvent<SVGSVGElement>) {
    if (event.target !== event.currentTarget || !bounds || !topology) return;
    const point = getWorldPoint(event);
    if (!point) return;
    if (mode === 'draw') {
      setPendingPoints((current) => [...current, point]);
      return;
    }
    if (mode === 'split' || mode === 'insertVertex') {
      const targetArc = selectedFeatures().find((feature) => feature.kind === 'arc')
        ?? (activeFeature?.kind === 'arcVertex' ? topology.features.find((feature) => feature.id === activeFeature.parentId && feature.kind === 'arc') : null);
      if (!targetArc) {
        setMessage(language === 'zh' ? '请先选择一条渠道弧段。' : 'Select a channel arc first.');
        return;
      }
      if (mode === 'split') {
        await run('split', { featureIDs: [targetArc.id], points: [point], mapCellSize, splitMode });
      } else {
        await run('insertVertex', { featureIDs: [targetArc.id], points: [point], mapCellSize });
      }
    }
  }

  function featureClick(event: ReactMouseEvent<SVGElement>, feature: TerrainTopologyFeature) {
    event.stopPropagation();
    if (skipClickRef.current) return;
    if (mode === 'draw') {
      const point = getWorldPoint(event);
      if (point) setPendingPoints((current) => [...current, point]);
      return;
    }
    if (mode === 'split' || mode === 'insertVertex') {
      const point = getWorldPoint(event);
      if (!point || !topology) return;
      const targetArc = feature.kind === 'arc' ? feature
        : feature.kind === 'arcVertex' && feature.parentId ? topology.features.find((item) => item.id === feature.parentId && item.kind === 'arc') : null;
      if (!targetArc) return;
      if (mode === 'split') void run('split', { featureIDs: [targetArc.id], points: [point], mapCellSize, splitMode });
      else void run('insertVertex', { featureIDs: [targetArc.id], points: [point], mapCellSize });
      return;
    }
    void selectFeature(feature);
  }

  const featureLabel = (feature: TerrainTopologyFeature) => {
    const names: Record<string, string> = {
      arc: language === 'zh' ? '弧段' : 'Arc',
      arcVertex: language === 'zh' ? '弧段顶点' : 'Arc vertex',
      node: language === 'zh' ? '节点' : 'Node',
      controlPoint: language === 'zh' ? '区域控制点' : 'Region control point',
      region: language === 'zh' ? '区域' : 'Region',
    };
    return names[feature.kind] ?? feature.kind;
  };

  const svgFeatures = topology?.features ?? [];
  const regionFeatures = svgFeatures.filter((feature) => feature.kind === 'region');
  const lineFeatures = svgFeatures.filter((feature) => feature.kind === 'arc');
  const pointFeatures = svgFeatures.filter((feature) => feature.kind === 'node' || feature.kind === 'controlPoint' || feature.kind === 'arcVertex');
  const metadataFeature = activeFeature && ['arc', 'node', 'controlPoint'].includes(activeFeature.kind) ? activeFeature : null;
  const writableMetadata = metadataFeature?.metadata?.filter((field) => !field.readOnly) ?? [];
  const drawPoints = bounds ? pendingPoints : manualCoordinateCapture.points;
  const manualCoordinatesNeeded = !!topology && !bounds;
  const drawEnabled = editingEnabled && (bounds ? pendingPoints.length >= 2 : manualCoordinateCapture.valid);

  return <section className="editor-terrain-card editor-channel-topology" data-testid="channel-mod-topology-editor" aria-label={language === 'zh' ? '渠道修改拓扑编辑器' : 'Channel modification topology editor'}>
    <div className="editor-terrain-table-heading">
      <h5>{language === 'zh' ? '渠道修改拓扑' : 'Channel modification topology'}</h5>
      <span>{topology ? `${topology.features.length} ${language === 'zh' ? '个要素' : 'features'}` : (language === 'zh' ? '尚未读取' : 'Not loaded')}</span>
    </div>
    <p className="editor-field-muted">
      {language === 'zh'
        ? '仅显示 Host 返回的渠道拓扑源坐标；此独立视图不叠加工程地图或影像。绘制与命中位置直接使用当前拓扑坐标。'
        : 'Shows only source coordinates returned by the Host; this standalone view does not overlay the project map or imagery.'}
    </p>
    <div className="editor-button-row">
      <button type="button" className="editor-button" data-testid="channel-topology-load" disabled={!onRun || !canEdit || busy} onClick={() => void run('load')}>
        {topology ? (language === 'zh' ? '重新读取拓扑' : 'Reload topology') : (language === 'zh' ? '读取拓扑' : 'Load topology')}
      </button>
      <button type="button" className="editor-button" data-testid="channel-topology-undo" disabled={!topologyEnabled || !topology?.canUndo || !canEdit} onClick={() => void run('undo')}>
        {language === 'zh' ? '撤销拓扑' : 'Undo topology'}
      </button>
      <button type="button" className="editor-button" data-testid="channel-topology-redo" disabled={!topologyEnabled || !topology?.canRedo || !canEdit} onClick={() => void run('redo')}>
        {language === 'zh' ? '重做拓扑' : 'Redo topology'}
      </button>
    </div>
    {!onRun && <div className="editor-config-notice">{language === 'zh' ? '当前工作台没有提供渠道拓扑操作接口。' : 'The workbench did not provide the channel topology operation.'}</div>}
    {!!onRun && !canEdit && !busy && <div className="editor-config-notice">{language === 'zh' ? '请先结束当前保存事务并修正模型草稿中的无效值，再读取或编辑拓扑。' : 'Finish any active save and correct invalid model draft values before loading or editing topology.'}</div>}
    {message && <div data-testid="channel-topology-status" className={message.includes('失败') || message.includes('过期') || message.includes('未接纳') ? 'editor-inline-error' : 'editor-save-notice'} role="status" aria-live="polite">{message}</div>}
    {topology && <>
      <div className="editor-button-row" aria-label={language === 'zh' ? '拓扑工具' : 'Topology tools'}>
        {(['select', 'draw', 'move', 'split', 'insertVertex'] as const).map((tool) => <button
          key={tool}
          type="button"
          className={`editor-button${mode === tool ? ' editor-button-selected' : ''}`}
          data-testid={`channel-topology-tool-${tool}`}
          aria-pressed={mode === tool}
          disabled={!canSelect || (tool !== 'select' && !canEdit)}
          onClick={() => { setMode(tool); if (tool !== 'draw') setPendingPoints([]); }}
        >{toolTitle(tool, language)}</button>)}
        {mode === 'split' && <label className="editor-field" style={{ maxWidth: 180 }}>
          <span>{language === 'zh' ? '拆分方式' : 'Split mode'}</span>
          <select value={splitMode} disabled={busy || !canEdit} onChange={(event) => setSplitMode(event.currentTarget.value as typeof splitMode)}>
            <option value="Anywhere">{language === 'zh' ? '点击位置' : 'At click'}</option>
            <option value="AtVertex">{language === 'zh' ? '最近顶点' : 'At nearest vertex'}</option>
          </select>
        </label>}
        {mode === 'draw' && <button type="button" className="editor-button editor-button-primary" data-testid="channel-topology-draw-finish" disabled={!drawEnabled} onClick={() => void run('draw', { points: drawPoints, mapCellSize })}>
          {language === 'zh' ? `完成绘制 (${drawPoints.length} 点)` : `Finish drawing (${drawPoints.length} points)`}
        </button>}
        {selectedIDs.length > 0 && <button type="button" className="editor-button" data-testid="channel-topology-remove" disabled={!editingEnabled} onClick={() => void run('remove', { featureIDs: selectedIDs, mapCellSize })}>
          {language === 'zh' ? `删除所选 (${selectedIDs.length})` : `Remove selected (${selectedIDs.length})`}
        </button>}
      </div>
      {mode === 'draw' && <small className="editor-field-muted">{language === 'zh' ? '在图面空白处逐点绘制；至少两个源坐标点后提交。' : 'Click the canvas to add source-coordinate vertices; at least two points are required.'}</small>}
      {mode === 'draw' && manualCoordinatesNeeded && <div className="editor-table-card" data-testid="channel-topology-coordinate-entry">
        <strong>{language === 'zh' ? '输入渠道拓扑源坐标' : 'Enter channel topology source coordinates'}</strong>
        <p className="editor-field-muted">{language === 'zh'
          ? 'Host 没有提供可用范围。请明确输入 X/Y 源坐标；不会用工程地图坐标或默认数值代填。'
          : 'The Host returned no usable extent. Enter source X/Y coordinates explicitly; project-map coordinates and defaults are not substituted.'}</p>
        {manualPoints.map((point, index) => <div key={point.id} style={{ display: 'grid', gridTemplateColumns: '50px 1fr 1fr auto', alignItems: 'center', gap: 8, marginBottom: 6 }}>
          <span>{language === 'zh' ? `第 ${index + 1} 点` : `Point ${index + 1}`}</span>
          <label className="editor-field">
            <span>X</span>
            <input data-testid={`channel-topology-point-x-${index + 1}`} aria-label={language === 'zh' ? `第 ${index + 1} 点 X 源坐标` : `Point ${index + 1} source X`} inputMode="decimal" value={point.x} disabled={!editingEnabled} onChange={(event) => { const value = event.currentTarget.value; setManualPoints((current) => current.map((item) => item.id === point.id ? { ...item, x: value } : item)); }} />
          </label>
          <label className="editor-field">
            <span>Y</span>
            <input data-testid={`channel-topology-point-y-${index + 1}`} aria-label={language === 'zh' ? `第 ${index + 1} 点 Y 源坐标` : `Point ${index + 1} source Y`} inputMode="decimal" value={point.y} disabled={!editingEnabled} onChange={(event) => { const value = event.currentTarget.value; setManualPoints((current) => current.map((item) => item.id === point.id ? { ...item, y: value } : item)); }} />
          </label>
          {manualPoints.length > 2 && <button type="button" className="editor-icon-button" aria-label={language === 'zh' ? `删除第 ${index + 1} 点` : `Remove point ${index + 1}`} onClick={() => setManualPoints((current) => current.filter((item) => item.id !== point.id))}>×</button>}
        </div>)}
        <button type="button" className="editor-button" data-testid="channel-topology-point-add" disabled={!editingEnabled} onClick={() => { const id = manualPointIDRef.current++; setManualPoints((current) => [...current, { id, x: '', y: '' }]); }}>{language === 'zh' ? '添加坐标点' : 'Add coordinate point'}</button>
        {!manualCoordinateCapture.valid && <small className="editor-field-error">{language === 'zh' ? '至少输入两个不同的有限源坐标点。' : 'Enter at least two distinct finite source-coordinate points.'}</small>}
      </div>}
      {mode === 'move' && <small className="editor-field-muted">{language === 'zh' ? '拖动节点或弧段内部顶点。弧段端点属于节点，请拖动节点本身。' : 'Drag a node or internal arc vertex. Arc endpoints are nodes and should be moved as nodes.'}</small>}
      {mode === 'split' && <small className="editor-field-muted">{language === 'zh' ? '选择一条弧段后，在弧线附近点选拆分位置。' : 'Select an arc, then click near it to split.'}</small>}
      {mode === 'insertVertex' && <small className="editor-field-muted">{language === 'zh' ? '选择一条弧段后，在弧线上点选插入位置。' : 'Select an arc, then click near it to insert a vertex.'}</small>}
      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 2fr) minmax(220px, 1fr)', gap: 12 }}>
        <div className="editor-table-card" style={{ minWidth: 0 }}>
          {bounds ? <svg
            ref={svgRef}
            viewBox={`${bounds.minX - bounds.padding} ${-(bounds.maxY + bounds.padding)} ${bounds.spanX + 2 * bounds.padding} ${bounds.spanY + 2 * bounds.padding}`}
            role="img"
            data-testid="channel-topology-canvas"
            aria-label={language === 'zh' ? '渠道拓扑源坐标图' : 'Channel topology source-coordinate map'}
            onClick={(event) => void handleCanvasClick(event)}
            onPointerDown={handlePointerDown}
            onPointerUp={(event) => void handlePointerUp(event)}
            onPointerCancel={() => { dragRef.current = null; }}
            style={{ display: 'block', width: '100%', height: 360, border: '1px solid #dfe5ea', borderRadius: 6, background: '#f8fafb', touchAction: 'none', cursor: mode === 'draw' ? 'crosshair' : mode === 'move' ? 'move' : 'default' }}
          >
            <rect x={bounds.minX - bounds.padding} y={-(bounds.maxY + bounds.padding)} width={bounds.spanX + 2 * bounds.padding} height={bounds.spanY + 2 * bounds.padding} fill="transparent" pointerEvents="none" />
            {regionFeatures.map((feature) => {
              const rings = feature.rings?.length ? feature.rings : [feature.points];
              const path = rings.map((ring) => ring.length ? `M ${ring.map((point) => `${point.x} ${-point.y}`).join(' L ')} Z` : '').join(' ');
              return <path
                key={feature.id}
                data-topology-feature-id={feature.id}
                d={path}
                fill={feature.isMask ? '#b4530940' : '#2563eb18'}
                fillRule="evenodd"
                stroke={featureSelected(feature, topology) ? '#b42318' : '#587997'}
                strokeWidth={featureSelected(feature, topology) ? 2.5 : 1.2}
                vectorEffect="non-scaling-stroke"
                onClick={(event) => featureClick(event, feature)}
                style={{ cursor: 'pointer' }}
              ><title>{`${featureLabel(feature)} · ${feature.id}${feature.isMask == null ? '' : feature.isMask ? ' · mask' : ' · region'}`}</title></path>;
            })}
            {lineFeatures.map((feature) => <polyline
              key={feature.id}
              data-topology-feature-id={feature.id}
              points={feature.points.map((point) => `${point.x},${-point.y}`).join(' ')}
              fill="none"
              stroke={featureSelected(feature, topology) ? '#b42318' : '#1769aa'}
              strokeWidth={featureSelected(feature, topology) ? 3 : 2}
              vectorEffect="non-scaling-stroke"
              onClick={(event) => featureClick(event, feature)}
              style={{ cursor: 'pointer' }}
            ><title>{`${featureLabel(feature)} · ${feature.id}`}</title></polyline>)}
            {pointFeatures.map((feature) => {
              const point = feature.points[0];
              if (!point) return null;
              const isVertex = feature.kind === 'arcVertex';
              const isSelected = featureSelected(feature, topology);
              return <circle
                key={feature.id}
                data-topology-feature-id={feature.id}
                cx={point.x}
                cy={-point.y}
                r={Math.max(Math.max(bounds.spanX, bounds.spanY) / 125, Number.EPSILON)}
                fill={isSelected ? '#b42318' : feature.kind === 'node' ? '#0f766e' : isVertex ? '#c2410c' : '#7c3aed'}
                stroke="#fff"
                strokeWidth={1.3}
                vectorEffect="non-scaling-stroke"
                onClick={(event) => featureClick(event, feature)}
                style={{ cursor: mode === 'move' ? 'move' : 'pointer' }}
              ><title>{`${featureLabel(feature)} · ${feature.id}`}</title></circle>;
            })}
            {pendingPoints.length > 0 && <>
              <polyline points={pendingPoints.map((point) => `${point.x},${-point.y}`).join(' ')} fill="none" stroke="#7c3aed" strokeWidth="2" strokeDasharray="5 4" vectorEffect="non-scaling-stroke" />
              {pendingPoints.map((point, index) => <circle key={`${index}:${point.x}:${point.y}`} cx={point.x} cy={-point.y} r={Math.max(Math.max(bounds.spanX, bounds.spanY) / 150, Number.EPSILON)} fill="#7c3aed" />)}
            </>}
          </svg> : <div className="editor-empty-state">{language === 'zh' ? '当前没有 Host 要素坐标或真实输入范围；请切换绘制工具并明确输入坐标。' : 'No Host feature coordinates or source extent are available. Use the draw tool and enter coordinates explicitly.'}</div>}
          <small className="editor-field-muted" style={{ display: 'block', marginTop: 8 }}>
            {language === 'zh'
              ? `视图命中尺度：${mapCellSize.toPrecision(5)} 源坐标单位/单元；${referenceExtentSource && topology.features.every((feature) => feature.points.length === 0 && !feature.rings?.length) ? `空拓扑画布范围来自「${referenceExtentSource}」的 Host 范围；` : ''}坐标不转换为工程地图坐标。`
              : `View hit scale: ${mapCellSize.toPrecision(5)} source units/cell; coordinates are used as returned, without project-map conversion.`}
          </small>
        </div>
        <div className="editor-table-card">
          <div className="editor-terrain-table-heading" data-testid="channel-topology-selection">
            <h5>{language === 'zh' ? '所选拓扑要素' : 'Selected topology feature'}</h5>
            <span>{selectedIDs.length}</span>
          </div>
          {selectedIDs.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '在图上点选弧段、节点、控制点或区域。' : 'Select an arc, node, control point, or region on the canvas.'}</small>}
          {selectedFeatures().map((feature) => <div key={feature.id} className="editor-source-details" style={{ marginBottom: 8 }}>
            <strong>{featureLabel(feature)}</strong><br /><code>{feature.id}</code>
          </div>)}
          {activeFeature?.kind === 'region' && activeFeature.controlPointIndex != null && <button
            type="button"
            className="editor-button"
            disabled={!editingEnabled}
            onClick={() => void run('maskRegion', { featureIDs: [activeFeature.id] }, activeFeature.id)}
          >{activeFeature.isMask ? (language === 'zh' ? '取消遮罩区域' : 'Unmask region') : (language === 'zh' ? '切换遮罩区域' : 'Toggle mask')}</button>}
          {activeFeature?.kind === 'arcVertex' && activeFeature.parentId && activeFeature.vertexIndex != null && <button
            type="button"
            className="editor-button"
            disabled={!editingEnabled}
            onClick={() => void run('deleteVertex', { featureIDs: [activeFeature.parentId!], vertexIndex: activeFeature.vertexIndex! })}
          >{language === 'zh' ? '删除此顶点' : 'Delete this vertex'}</button>}
          {metadataFeature && writableMetadata.length > 0 && <div className="editor-field-grid" style={{ marginTop: 10 }}>
            {writableMetadata.map((field) => {
              const key = `${metadataFeature.id}/${field.id}`;
              const value = Object.hasOwn(metadataValues, key) ? metadataValues[key] : field.value ?? '';
              return <label className="editor-field" key={field.id}>
                <span>{field.title}</span>
                {field.options?.length ? <select value={value} disabled={!editingEnabled} onChange={(event) => { const next = event.currentTarget.value; setMetadataValues((current) => ({ ...current, [key]: next })); }}>
                  {field.options.map((option) => <option key={option} value={option}>{option}</option>)}
                </select> : field.sourceType.toLowerCase() === 'boolean' ? <select value={value} disabled={!editingEnabled} onChange={(event) => { const next = event.currentTarget.value; setMetadataValues((current) => ({ ...current, [key]: next })); }}>
                  <option value="true">true</option><option value="false">false</option>
                </select> : <input value={value} disabled={!editingEnabled} onChange={(event) => { const next = event.currentTarget.value; setMetadataValues((current) => ({ ...current, [key]: next })); }} />}
                <small className="editor-field-muted">{field.sourceType}</small>
                <button type="button" className="editor-button" disabled={!editingEnabled} onClick={() => void run('setMetadata', {
                  featureIDs: [metadataFeature.id],
                  metadataKind: metadataFeature.kind as 'arc' | 'node' | 'controlPoint',
                  metadataColumn: field.id,
                  metadataValue: value,
                }, metadataFeature.id)}>{language === 'zh' ? '应用属性' : 'Apply attribute'}</button>
              </label>;
            })}
          </div>}
          {metadataFeature && writableMetadata.length === 0 && <small className="editor-field-muted">{language === 'zh' ? '此要素没有 Host 声明为可编辑的属性字段。' : 'The Host did not declare editable metadata fields for this feature.'}</small>}
        </div>
      </div>
      {busy && <div className="editor-config-notice" data-testid="channel-topology-busy" role="status">{language === 'zh' ? '正在等待拓扑回执…' : 'Waiting for topology receipt…'}</div>}
    </>}
  </section>;
}

function validateResponse(response: TerrainTopologyResponse, inputId: string, sourceRevision: string): string | null {
  const topology = response.topology;
  if (!response.snapshot || response.snapshot.revision !== sourceRevision || topology.inputId !== inputId
    || topology.sourceRevision !== sourceRevision || !topology.draftIdentity.trim()) {
    return 'Host 拓扑回执的输入身份或源修订与当前工作台不匹配，已保留原显示。';
  }
  if (!Array.isArray(topology.features) || !Array.isArray(topology.selectedFeatureIDs)) return 'Host 拓扑回执缺少要素或选择集合。';
  const ids = new Set<string>();
  for (const feature of topology.features) {
    if (!feature.id.trim() || ids.has(feature.id) || !Array.isArray(feature.points)
      || feature.points.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y))) {
      return 'Host 拓扑回执包含重复要素身份或无效源坐标。';
    }
    ids.add(feature.id);
    if (feature.rings?.some((ring) => ring.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y)))) {
      return 'Host 拓扑回执包含无效区域坐标。';
    }
  }
  if (topology.selectedFeatureIDs.some((id) => !ids.has(id))) return 'Host 拓扑回执的选择集合引用了不存在的要素。';
  return null;
}

function featureSelected(feature: TerrainTopologyFeature, topology: TerrainTopologyData): boolean {
  const selected = new Set(topology.selectedFeatureIDs);
  if (selected.has(feature.id)) return true;
  if (feature.kind === 'arcVertex' && feature.parentId && selected.has(feature.parentId)) return true;
  if (feature.kind === 'region' && feature.controlPointIndex != null) {
    const controlPoint = topology.features.find((item) => item.kind === 'controlPoint' && item.id.endsWith(`:controlPoint:${feature.controlPointIndex}`));
    return !!controlPoint && selected.has(controlPoint.id);
  }
  return false;
}

function selectionIDsForFeature(feature: TerrainTopologyFeature, topology: TerrainTopologyData): string[] {
  if (feature.kind === 'arcVertex' && feature.parentId) return [feature.parentId];
  if (feature.kind === 'region' && feature.controlPointIndex != null) {
    const controlPoint = topology.features.find((item) => item.kind === 'controlPoint'
      && item.id.endsWith(`:controlPoint:${feature.controlPointIndex}`));
    if (controlPoint) return [controlPoint.id];
  }
  return [feature.id];
}

function topologyBounds(features: TerrainTopologyFeature[], referenceExtent?: { minX: number; maxX: number; minY: number; maxY: number } | null) {
  const points = features.flatMap((feature) => [...feature.points, ...(feature.rings?.flat() ?? [])])
    .filter((point) => Number.isFinite(point.x) && Number.isFinite(point.y));
  if (!points.length && referenceExtent && Number.isFinite(referenceExtent.minX) && Number.isFinite(referenceExtent.maxX)
    && Number.isFinite(referenceExtent.minY) && Number.isFinite(referenceExtent.maxY)
    && referenceExtent.minX <= referenceExtent.maxX && referenceExtent.minY <= referenceExtent.maxY) {
    points.push(
      { x: referenceExtent.minX, y: referenceExtent.minY },
      { x: referenceExtent.maxX, y: referenceExtent.maxY },
    );
  }
  if (!points.length) return null;
  let minX = Number.POSITIVE_INFINITY; let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY; let maxY = Number.NEGATIVE_INFINITY;
  for (const point of points) {
    minX = Math.min(minX, point.x); maxX = Math.max(maxX, point.x);
    minY = Math.min(minY, point.y); maxY = Math.max(maxY, point.y);
  }
  const spanX = Math.max(maxX - minX, 1);
  const spanY = Math.max(maxY - minY, 1);
  return { minX, maxX, minY, maxY, spanX, spanY, padding: Math.max(spanX, spanY) * 0.06 };
}

function captureManualPoints(rows: ManualPointRow[]): { points: TerrainTopologyCoordinate[]; mapCellSize: number; valid: boolean } {
  if (rows.length < 2 || rows.some((row) => !row.x.trim() || !row.y.trim())) return { points: [], mapCellSize: 1, valid: false };
  const points = rows.map((row) => ({ x: Number(row.x), y: Number(row.y) }));
  if (points.some((point) => !Number.isFinite(point.x) || !Number.isFinite(point.y))) return { points: [], mapCellSize: 1, valid: false };
  let minX = Number.POSITIVE_INFINITY; let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY; let maxY = Number.NEGATIVE_INFINITY;
  for (const point of points) {
    minX = Math.min(minX, point.x); maxX = Math.max(maxX, point.x);
    minY = Math.min(minY, point.y); maxY = Math.max(maxY, point.y);
  }
  const span = Math.max(maxX - minX, maxY - minY);
  if (!Number.isFinite(span) || span <= 0) return { points: [], mapCellSize: 1, valid: false };
  const mapCellSize = span / 100;
  if (!Number.isFinite(mapCellSize) || mapCellSize <= 0) return { points: [], mapCellSize: 1, valid: false };
  return { points, mapCellSize, valid: true };
}

function toolTitle(tool: ToolMode, language: 'zh' | 'en'): string {
  if (language === 'zh') {
    return { select: '选择', draw: '绘制弧段', move: '移动节点/顶点', split: '拆分弧段', insertVertex: '插入顶点' }[tool];
  }
  return { select: 'Select', draw: 'Draw arc', move: 'Move node/vertex', split: 'Split arc', insertVertex: 'Insert vertex' }[tool];
}
