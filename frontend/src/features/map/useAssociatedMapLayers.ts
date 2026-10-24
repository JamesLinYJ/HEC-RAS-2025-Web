import { useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { MapDocumentTab, MapExtent, MapImageLayer } from './mapTypes';
import type { MapResultFramesController } from './useMapResultFrames';
import { mapResultSelectionOverridesFromCatalog } from './useMapResultFrames';
import type { PersistedRasterLayerViewport } from './usePersistedRasterLayers';
import { projectXmlSha } from './ProjectTreeListener';
import { rgbaFrameImage } from './mapImages';
import { mapError } from './mapText';
import { collectMapAssociations, mapCatalogMatchesDocument, mapLocatorKey, sameMapID,
  sameMapLocator, sameMapRevision, stableMapValue, validMapExtent } from './associatedMapLayers';
import type { MapAssociationDependency, MapAssociationHoverCapture, MapAssociationOwners } from './associatedMapLayers';

export interface AssociatedMapImage extends MapImageLayer {
  sourceKind: 'terrain' | 'nvalue' | 'classification';
  treeNodeID: string;
  sourcePath: string;
  sourceRevision: string;
  /** Catalog scope captured before association rendering; distinct from the returned frame token. */
  catalogContextToken: string;
  /** Host association-frame scope for this rendered image. */
  associationContextToken: string;
  width: number;
  height: number;
  sourceExtent: MapExtent | null;
  hoverCapture: MapAssociationHoverCapture;
}

interface AssociationState {
  key: string;
  backend: BackendGateway | null;
  status: 'idle' | 'loading' | 'ready' | 'partial' | 'error';
  layers: AssociatedMapImage[];
  sourceExtents: MapExtent[];
  errors: Array<{ name: string; message: string }>;
}

const EMPTY_STATE: AssociationState = { key: '', backend: null, status: 'idle', layers: [], sourceExtents: [], errors: [] };
const sha256 = (value: unknown): value is string => typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
const object = (value: unknown): Record<string, unknown> => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('服务端返回的关联图层无效。');
  return value as Record<string, unknown>;
};
const required = (value: unknown): string => {
  if (typeof value !== 'string' || !value.trim()) throw new Error('关联图层的来源身份不完整。');
  return value;
};

function validateOwners(layer: Record<string, unknown>, requested: MapAssociationOwners): MapAssociationOwners {
  const arrays = ['geometryNodes', 'planNodes', 'resultOwners'] as const;
  for (const key of arrays) {
    if (!Array.isArray(layer[key]) || (layer[key] as unknown[]).some(value =>
      !requested[key].some(owner => JSON.stringify(stableMapValue(owner)) === JSON.stringify(stableMapValue(value))))) {
      throw new Error('关联图层响应的模型或结果所有者已变化。');
    }
  }
  return { geometryNodes: layer.geometryNodes as MapAssociationOwners['geometryNodes'],
    planNodes: layer.planNodes as MapAssociationOwners['planNodes'],
    resultOwners: layer.resultOwners as MapAssociationOwners['resultOwners'] };
}

/** Actual C# association frames, kept separate from Result's terrain dependency. */
export function useAssociatedMapLayers({ backend, context, document, viewport, controller, language, backendRevision }:
  { backend: BackendGateway; context: WorkbenchContext; document: MapDocumentTab | null;
    viewport: PersistedRasterLayerViewport; controller: MapResultFramesController; language: Language; backendRevision?: number }) {
  const [state, setState] = useState<AssociationState>(EMPTY_STATE);
  const generationRef = useRef(0);
  const input = useMemo(() => {
    const catalog = controller.sources;
    if (!document || !context.projectXml || !mapCatalogMatchesDocument(catalog, context, document)) return null;
    try {
      const owners = collectMapAssociations(document, catalog, mapResultSelectionOverridesFromCatalog(catalog));
      if (!owners.candidates.length) return null;
      if (owners.candidates.length > 256) throw new Error('一次最多可显示 256 个关联图层。');
      if (!backend.supports('map.associations.load')) throw new Error('当前 C# 服务未提供关联图层绘制接口。');
      const key = JSON.stringify(stableMapValue([context.projectSessionID, context.projectPath, context.projectRevision,
        context.projectXml, document.id, document.mapID, document.mapIndex, document.name, document.mapType,
        document.visualizationRevision, catalog.contextToken, catalog.treeSelectionOverrides,
        owners.candidates.map(candidate => candidate.locator), owners.geometryNodes, owners.planNodes, owners.resultOwners,
        viewport, backendRevision]));
      return { key, catalog, owners, error: null };
    } catch (error) {
      return { key: JSON.stringify([document.id, document.visualizationRevision, catalog.contextToken, backendRevision]),
        catalog, owners: null, error };
    }
  }, [backend, backendRevision, context.projectPath, context.projectRevision, context.projectSessionID,
    context.projectXml, controller.sources, document, viewport]);
  const inputKey = input?.key ?? '';
  const activeKeyRef = useRef(inputKey);
  activeKeyRef.current = inputKey;

  useEffect(() => {
    const generation = ++generationRef.current;
    const abort = new AbortController();
    if (!input || !document) {
      setState(EMPTY_STATE);
      return () => abort.abort();
    }
    const captured = input;
    const current = () => !abort.signal.aborted && generation === generationRef.current && activeKeyRef.current === captured.key;
    if (captured.error || !captured.owners) {
      setState({ ...EMPTY_STATE, key: captured.key, backend, status: 'error',
        errors: [{ name: language === 'zh' ? '关联图层' : 'Associated layers', message: mapError(language, captured.error) }] });
      return () => abort.abort();
    }
    const owners = captured.owners;
    setState({ ...EMPTY_STATE, key: captured.key, backend, status: 'loading' });
    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          const xmlSHA = await projectXmlSha(context.projectXml);
          if (!current()) return;
          if (!xmlSHA || !sameMapRevision(xmlSHA, captured.catalog.projectXmlSHA)) throw new Error('关联图层的工程草稿修订已变化。');
          const { pixelWidth, pixelHeight, ...worldViewport } = viewport;
          const request = {
            map: { projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA: xmlSHA,
              mapID: document.mapID, mapIndex: document.mapIndex, mapName: document.name, mapType: document.mapType,
              expectedVisualizationRevision: document.visualizationRevision, projectSessionID: context.projectSessionID,
              resultSelectionOverrides: captured.catalog.treeSelectionOverrides },
            geometryNodes: owners.geometryNodes, planNodes: owners.planNodes, resultOwners: owners.resultOwners,
            associationNodes: owners.candidates.map(candidate => candidate.locator),
            viewport: worldViewport, width: pixelWidth, height: pixelHeight,
          };
          const result = object(await backend.request<unknown>('map.associations.load', request, { signal: abort.signal }));
          if (!current()) return;
          if (!sameMapID(required(result.mapID), document.mapID) || result.mapIndex !== document.mapIndex ||
              result.mapName !== document.name || result.mapType !== document.mapType ||
              !sameMapRevision(required(result.projectRevision), captured.catalog.projectRevision) ||
              !sameMapRevision(required(result.projectXmlSHA), xmlSHA) ||
              !sameMapRevision(required(result.visualizationRevision), document.visualizationRevision!) ||
              !validMapExtent(result.viewport) || JSON.stringify(stableMapValue(result.viewport)) !== JSON.stringify(stableMapValue(worldViewport)) ||
              !Array.isArray(result.layers) || result.layers.length !== owners.candidates.length) {
            throw new Error('关联图层响应已不属于当前地图、来源或视口。');
          }
          required(result.projectCanonicalPath);
          const contextToken = required(result.contextToken);
          const layers: AssociatedMapImage[] = [];
          const sourceExtents: MapExtent[] = [];
          const errors: AssociationState['errors'] = [];
          const seen = new Set<string>();
          let totalPixels = 0;
          for (const value of result.layers) {
            const layer = object(value);
            const locator = object(layer.locator) as unknown as MapAssociationHoverCapture['node'];
            const candidate = owners.candidates.find(candidate => sameMapLocator(candidate.locator, locator));
            const key = mapLocatorKey(locator);
            if (!candidate || seen.has(key) || layer.kind !== candidate.kind) throw new Error('关联图层响应包含重复或未请求的来源。');
            seen.add(key);
            const sourcePath = required(layer.assetPath);
            const revision = required(layer.revision);
            if (!sha256(revision) || !Array.isArray(layer.dependencies) || !layer.dependencies.length) throw new Error('关联图层的来源或依赖修订不完整。');
            const dependencies: MapAssociationDependency[] = layer.dependencies.map(value => {
              const item = object(value);
              if (!sha256(item.revision)) throw new Error('关联图层依赖缺少有效修订。');
              return { path: required(item.path), revision: item.revision, role: required(item.role) };
            });
            if (!dependencies.some(item => item.path === sourcePath && sameMapRevision(item.revision, revision) && item.role === 'source-hdf')) {
              throw new Error('关联图层像素未绑定同一来源修订。');
            }
            const resolvedOwners = validateOwners(layer, owners);
            const sourceExtent = validMapExtent(layer.sourceExtent) ? layer.sourceExtent : null;
            if (sourceExtent) sourceExtents.push(sourceExtent);
            if (['failed', 'unavailable'].includes(String(layer.status))) {
              errors.push({ name: candidate.node.name, message: mapError(language,
                typeof layer.diagnostic === 'string' ? layer.diagnostic : '关联图层暂时无法绘制。') });
              continue;
            }
            if (layer.status === 'empty') continue;
            if (layer.status !== 'available' || layer.isChecked !== true || layer.effectiveVisible !== true) {
              throw new Error('关联图层返回了未确认可见的帧。');
            }
            const width = Number(layer.width), height = Number(layer.height);
            if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
                width > pixelWidth || height > pixelHeight || width * height > 2_097_152) throw new Error('关联图层像素尺寸无效。');
            totalPixels += width * height;
            if (totalPixels > 8 * 1024 * 1024) throw new Error('关联图层超出总像素预算。');
            const kind = candidate.kind === 'Terrain' ? 'terrain' : candidate.kind === 'NValue' ? 'nvalue' : 'classification';
            const frameID = JSON.stringify([document.mapID, key, contextToken, revision, width, height]);
            layers.push({ id: 'associated:' + frameID, treeNodeID: candidate.node.id, name: candidate.node.name,
              imageSrc: rgbaFrameImage(required(layer.rgba), width, height), sourceKind: kind, sourcePath,
              sourceRevision: revision, catalogContextToken: captured.catalog.contextToken,
              associationContextToken: contextToken, width, height,
              extent: worldViewport, sourceExtent, sampling: kind === 'classification' ? 'nearest' : undefined,
              hoverCapture: { ...resolvedOwners, treeNodeID: candidate.node.id, frameID, node: locator, kind,
                sourcePath, sourceRevision: revision, dependencies } });
          }
          if (current()) setState({ key: captured.key, backend, status: errors.length ? 'partial' : 'ready', layers, sourceExtents, errors });
        } catch (error) {
          if (current()) setState({ ...EMPTY_STATE, key: captured.key, backend, status: 'error',
            errors: [{ name: language === 'zh' ? '关联图层' : 'Associated layers', message: mapError(language, error) }] });
        }
      })();
    }, 180);
    return () => { window.clearTimeout(timer); abort.abort(); };
  }, [backend, inputKey, language]);

  if (state.key !== inputKey || state.backend !== backend) return { ...EMPTY_STATE, status: input ? 'loading' as const : 'idle' as const };
  return state;
}
