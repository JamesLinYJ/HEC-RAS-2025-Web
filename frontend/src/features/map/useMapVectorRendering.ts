import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { message } from '../../shared/workbench';
import { vectorRgbaFrameImage } from './mapImages';
import { parseVectorSymbologySnapshot } from './vector-render/vectorRenderWorkflow';
import type { VectorLayerSource, VectorLayerSymbologySnapshot, VectorRenderPreviewDraft, VectorRenderViewport } from './vector-render/vectorRenderTypes';

export interface MapVectorFrame {
  layerID: string;
  sourceKey: string;
  configurationKey: string;
  sourcePath: string;
  sourceRevision: string;
  visualizationRevision: string;
  styleRevision: string;
  globalRevision: string;
  pixelFormat: string;
  imageSrc: string;
  viewport: VectorRenderViewport;
  labels: Array<{ text: string; point: { x: number; y: number }; polyline: Array<{ x: number; y: number }> }>;
}

export interface MapVectorRenderIssue { layerID: string; message: string; diagnostic?: unknown }
export interface MapVectorPreview { sourceKey: string; draft: VectorRenderPreviewDraft }
interface InspectionState {
  backend: BackendGateway | null;
  key: string;
  snapshots: Record<string, VectorLayerSymbologySnapshot>;
  errors: MapVectorRenderIssue[];
  loading: boolean;
}
interface FrameState {
  backend: BackendGateway | null;
  frames: MapVectorFrame[];
  errors: MapVectorRenderIssue[];
  loading: boolean;
}

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function sameRevision(left: unknown, right: string | null): boolean {
  return typeof left === 'string' && typeof right === 'string' && left.toUpperCase() === right.toUpperCase();
}
function sameViewport(value: unknown, expected: VectorRenderViewport): boolean {
  return record(value) && (Object.keys(expected) as Array<keyof VectorRenderViewport>)
    .every(key => value[key] === expected[key]);
}
function coordinate(value: unknown): value is { x: number; y: number } {
  return record(value) && typeof value.x === 'number' && Number.isFinite(value.x)
    && typeof value.y === 'number' && Number.isFinite(value.y);
}

/** Uses the exact inspected source to validate pixels before any image is published. */
function parseFrame(value: unknown, source: VectorLayerSource, inspection: VectorLayerSymbologySnapshot,
  viewport: VectorRenderViewport, configurationKey: string): MapVectorFrame {
  if (!record(value) || typeof value.mapID !== 'string' || value.mapID.toUpperCase() !== source.map.mapID.toUpperCase()
    || value.layerID !== source.layerID || value.sourcePath !== inspection.sourcePath
    || !sameRevision(value.sourceRevision, source.sourceRevision)
    || !sameRevision(value.visualizationRevision, source.map.expectedVisualizationRevision)
    || !sameViewport(value.viewport, viewport) || value.pixelWidth !== viewport.pixelWidth
    || value.pixelHeight !== viewport.pixelHeight || value.renderScale !== 1
    || typeof value.rgba !== 'string' || typeof value.pixelFormat !== 'string' || !Array.isArray(value.labels))
    throw new Error('矢量绘图回执与当前图层、源修订或相机窗口不一致。');
  const labels = value.labels.map((label): MapVectorFrame['labels'][number] => {
    if (!record(label) || typeof label.text !== 'string' || !coordinate(label.point)
      || !Array.isArray(label.polyline) || !label.polyline.every(coordinate))
      throw new Error('矢量绘图返回了无效的标注位置。');
    return { text: label.text, point: label.point, polyline: label.polyline };
  });
  return {
    layerID: source.layerID, sourceKey: source.key, configurationKey,
    sourcePath: inspection.sourcePath, sourceRevision: source.sourceRevision,
    visualizationRevision: value.visualizationRevision as string,
    styleRevision: inspection.styleSHA256, globalRevision: inspection.globalSymbologyRevision,
    pixelFormat: value.pixelFormat,
    imageSrc: vectorRgbaFrameImage(value.rgba, viewport.pixelWidth, viewport.pixelHeight, value.pixelFormat),
    viewport, labels,
  };
}

/** Two source readers at a time bound worker/memory use on maps with many layers. */
async function visitSources<T>(items: T[], visit: (item: T) => Promise<void>): Promise<void> {
  let index = 0;
  await Promise.all(Array.from({ length: Math.min(2, items.length) }, async () => {
    while (index < items.length) {
      const item = items[index++];
      if (item !== undefined) await visit(item);
    }
  }));
}

export function useMapVectorRendering({ backend, backendRevision, sources, viewport, preview, settingsEpoch, language }: {
  backend: BackendGateway;
  backendRevision?: number;
  sources: VectorLayerSource[];
  viewport: VectorRenderViewport | null;
  preview: MapVectorPreview | null;
  settingsEpoch: number;
  language: Language;
}) {
  const sourceKey = JSON.stringify(sources.map(source => source.key));
  const inspectionKey = JSON.stringify([sourceKey, backendRevision, settingsEpoch]);
  const [inspections, setInspections] = useState<InspectionState>({ backend: null, key: '', snapshots: {}, errors: [], loading: false });
  const [rendered, setRendered] = useState<FrameState>({ backend: null, frames: [], errors: [], loading: false });
  const inspectGeneration = useRef(0);
  const renderGeneration = useRef(0);
  const currentSnapshots = inspections.backend === backend && inspections.key === inspectionKey ? inspections.snapshots : null;

  useEffect(() => {
    const generation = ++inspectGeneration.current;
    const controller = new AbortController();
    const current = () => inspectGeneration.current === generation && !controller.signal.aborted;
    setInspections({ backend, key: inspectionKey, snapshots: {}, errors: [], loading: sources.length > 0 });
    void visitSources(sources, async source => {
      if (!current()) return;
      try {
        if (!backend.supports('map.vector.symbology.inspect')) throw new Error('当前服务尚未提供地图矢量符号读取。');
        const { key: _key, ...parameters } = source;
        const raw = await backend.request<unknown>('map.vector.symbology.inspect', parameters, { signal: controller.signal });
        if (!current()) return;
        const snapshot = parseVectorSymbologySnapshot(raw, source);
        setInspections(state => state.key === inspectionKey && state.backend === backend
          ? { ...state, snapshots: { ...state.snapshots, [source.key]: snapshot } } : state);
      } catch (error) {
        if (current()) setInspections(state => state.key === inspectionKey && state.backend === backend
          ? { ...state, errors: [...state.errors, { layerID: source.layerID, message: message(error, language), diagnostic: error }] } : state);
      }
    }).finally(() => {
      if (current()) setInspections(state => state.key === inspectionKey && state.backend === backend ? { ...state, loading: false } : state);
    });
    return () => { inspectGeneration.current++; controller.abort(); };
  }, [backend, inspectionKey, sources, language]);

  const configurations = useMemo(() => sources.flatMap(source => {
    const inspection = currentSnapshots?.[source.key];
    if (!inspection || inspection.hasUnsupportedProjectConditions || inspection.hasUnknownProjectSymbologyContent) return [];
    const draft = preview?.sourceKey === source.key ? preview.draft : null;
    const styles = draft?.styles ?? inspection.styles;
    const plotOptions = draft?.plotOptions ?? {};
    return [{ source, inspection, styles, plotOptions,
      key: JSON.stringify([source.key, inspection.styleSHA256, inspection.globalSymbologyRevision, styles, plotOptions]) }];
  }), [sources, currentSnapshots, preview]);
  const configurationKeys = useMemo(() => new Set(configurations.map(configuration => configuration.key)), [configurations]);

  useEffect(() => {
    const generation = ++renderGeneration.current;
    const controller = new AbortController();
    const current = () => renderGeneration.current === generation && !controller.signal.aborted;
    setRendered(state => ({ backend,
      frames: state.backend === backend ? state.frames.filter(frame => configurationKeys.has(frame.configurationKey)) : [],
      errors: [], loading: configurations.length > 0 && viewport !== null }));
    // Moving the camera may retain an older, correctly georeferenced frame
    // until its replacement arrives; changing source/style retires it at once.
    const timer = setTimeout(() => {
      if (!viewport || !current()) return;
      void visitSources(configurations, async configuration => {
        if (!current()) return;
        const { source, inspection, styles, plotOptions } = configuration;
        try {
          if (!backend.supports('map.vector.plot.render')) throw new Error('当前服务尚未提供地图矢量绘图。');
          const raw = await backend.request<unknown>('map.vector.plot.render', {
            map: source.map, node: source.node, layerID: source.layerID,
            expectedFamily: source.expectedFamily, sourceRevision: source.sourceRevision,
            viewport, styles, plotOptions, renderScale: 1,
            expectedGlobalSymbologyRevision: inspection.globalSymbologyRevision,
          }, { signal: controller.signal });
          if (!current()) return;
          const frame = parseFrame(raw, source, inspection, viewport, configuration.key);
          setRendered(state => state.backend === backend
            ? { ...state, frames: [...state.frames.filter(value => value.layerID !== frame.layerID), frame] } : state);
        } catch (error) {
          if (current()) setRendered(state => ({ ...state,
            errors: [...state.errors, { layerID: source.layerID, message: message(error, language), diagnostic: error }] }));
        }
      }).finally(() => {
        if (current()) setRendered(state => ({ ...state, loading: false }));
      });
    }, 140);
    return () => { clearTimeout(timer); renderGeneration.current++; controller.abort(); };
  }, [backend, configurations, configurationKeys, viewport, language]);

  const unsupported = sources.flatMap(source => {
    const snapshot = currentSnapshots?.[source.key];
    return snapshot && (snapshot.hasUnsupportedProjectConditions || snapshot.hasUnknownProjectSymbologyContent)
      ? [{ layerID: source.layerID, message: language === 'zh'
        ? '该图层含尚不支持的项目符号条件，暂保留几何预览；符号绘制和修改已停用。'
        : 'This layer has unsupported project style content. Geometry preview remains available; styled rendering and edits are disabled.' }]
      : [];
  });
  return {
    frames: rendered.backend === backend ? rendered.frames.filter(frame => configurationKeys.has(frame.configurationKey)) : [],
    snapshots: currentSnapshots ?? {},
    errors: [...(currentSnapshots ? inspections.errors : []), ...unsupported, ...(rendered.backend === backend ? rendered.errors : [])],
    loading: (inspections.key !== inspectionKey || inspections.loading) || rendered.loading,
  };
}
