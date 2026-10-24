import { useEffect, useMemo, useRef, useState } from 'react';
import { errorDiagnostics, text } from '../../shared/workbench';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { calculatorLayerMatchesMap, calculatorRecipeDraft, type RasterCalculatorSessionLayer } from '../../shared/rasterCalculatorSession';
import { projectXmlSha } from './ProjectTreeListener';
import { rgbaFrameImage } from './mapImages';
import type { MapDocumentTab, MapImageLayer } from './mapTypes';
import {
  buildRasterCalculatorMapRequest,
  isRecord,
  parseRasterCalculatorEvaluation,
  parseRasterCalculatorInspection,
  rasterCalculatorOwner,
  validateRasterCalculatorRecipe,
} from '../tools/RasterCalculatorRecipe';
import type { RasterCalculatorMapCapture, RasterCalculatorRecipePayload, RasterMapViewExtent } from '../tools/RasterCalculatorRecipe';

export interface UseRasterCalculatorSessionLayersOptions {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  layers: readonly RasterCalculatorSessionLayer[];
  viewport: RasterMapViewExtent | null;
  language: Language;
  backendRevision: number;
}

export interface RasterCalculatorSessionLayerState {
  status: 'loading' | 'ready' | 'error';
  message?: string;
  diagnostic?: string;
}

interface InternalLayerState extends RasterCalculatorSessionLayerState {
  frameKey: string;
}

interface RenderedFrame {
  frameKey: string;
  image: MapImageLayer;
}

interface CapturedSources {
  descriptorKey: string;
  mapOwnerKey: string;
  capture: RasterCalculatorMapCapture;
}

const viewportDebounceMilliseconds = 150;

/**
 * Re-renders checked calculator descriptors as transient map images. The
 * descriptor remains in the session registry; only Host-generated viewport
 * frames are returned here.
 */
export function useRasterCalculatorSessionLayers({
  backend,
  context,
  document,
  layers,
  viewport,
  language,
  backendRevision,
}: UseRasterCalculatorSessionLayersOptions): {
  images: MapImageLayer[];
  states: Record<string, RasterCalculatorSessionLayerState>;
} {
  const latestOptions = useRef<UseRasterCalculatorSessionLayersOptions | null>(null);
  const latestEpochKey = useRef('');
  const generation = useRef(0);
  const sourceCaptures = useRef(new Map<string, CapturedSources>());
  const [frames, setFrames] = useState<Record<string, RenderedFrame>>({});
  const [internalStates, setInternalStates] = useState<Record<string, InternalLayerState>>({});
  latestOptions.current = { backend, context, document, layers, viewport, language, backendRevision };

  const ownerKey = rasterCalculatorOwner(context);
  const mapOwnerKey = stableJson({
    ownerKey,
    backendRevision,
    coreLayerContext: context.coreLayerContext ?? null,
    map: document ? [document.id, document.mapID, document.mapIndex, document.name, document.mapType,
      document.visualizationRevision] : null,
  });
  const viewportKey = viewport ? stableJson(viewport) : 'no-viewport';
  const matchingLayers = useMemo(() => {
    if (!document) return [];
    return layers.filter((layer) => layer.visible && calculatorLayerMatchesMap(layer, context, document));
  }, [context, document, layers]);

  const descriptorKeys = new Map(matchingLayers.map((layer) => [layer.id, descriptorKey(layer)]));
  const frameKeys = new Map(matchingLayers.map((layer) => {
    const descriptor = descriptorKeys.get(layer.id)!;
    const cached = sourceCaptures.current.get(layer.id);
    const capturedToken = cached?.descriptorKey === descriptor && cached.mapOwnerKey === mapOwnerKey
      ? cached.capture.contextToken : layer.capture.contextToken;
    return [layer.id, stableJson([mapOwnerKey, viewportKey, descriptor, capturedToken])] as const;
  }));
  const epochKey = stableJson([...matchingLayers.map((layer) => [layer.id, descriptorKeys.get(layer.id), frameKeys.get(layer.id)]),
    mapOwnerKey, viewportKey]);
  latestEpochKey.current = epochKey;

  useEffect(() => {
    const requestGeneration = ++generation.current;
    const aborters: AbortController[] = [];
    const timers: ReturnType<typeof setTimeout>[] = [];
    const requestEpochKey = epochKey;
    const activeLayerIDs = new Set(matchingLayers.map((layer) => layer.id));

    for (const layerID of sourceCaptures.current.keys()) {
      if (!activeLayerIDs.has(layerID)) sourceCaptures.current.delete(layerID);
    }
    setFrames((current) => {
      let changed = false;
      const next = { ...current };
      for (const layerID of Object.keys(next)) {
        if (!activeLayerIDs.has(layerID)) { delete next[layerID]; changed = true; }
      }
      return changed ? next : current;
    });
    setInternalStates((current) => {
      let changed = false;
      const next = { ...current };
      for (const layerID of Object.keys(next)) {
        if (!activeLayerIDs.has(layerID)) { delete next[layerID]; changed = true; }
      }
      return changed ? next : current;
    });

    const isCurrent = (signal: AbortSignal): boolean => !signal.aborted &&
      generation.current === requestGeneration && latestEpochKey.current === requestEpochKey;

    const updateState = (layerID: string, key: string, value: RasterCalculatorSessionLayerState): void => {
      setInternalStates((current) => ({ ...current, [layerID]: { ...value, frameKey: key } }));
    };

    const failLayer = (layer: RasterCalculatorSessionLayer, key: string, error: unknown, currentLanguage: Language): void => {
      const code = errorCode(error);
      const message = code === 'sourceChanged'
        ? text(currentLanguage,
          '计算器来源或地图已变化；请重新检查并发布该配方。',
          'A calculator source or map changed. Reinspect the sources and republish this recipe.')
        : text(currentLanguage, '栅格计算图层刷新失败。', 'Could not refresh the raster calculator layer.');
      updateState(layer.id, key, { status: 'error', message, diagnostic: diagnosticText(error) });
    };

    const inspectCurrentMap = async (
      layer: RasterCalculatorSessionLayer,
      map: Record<string, unknown>,
      xmlSHA: string,
      signal: AbortSignal,
      currentLanguage: Language,
    ): Promise<RasterCalculatorMapCapture> => {
      const raw: unknown = await backend.request('raster.calculator.inspect', { map }, { signal });
      if (!isCurrent(signal)) throw new DOMException('The calculator request is no longer current.', 'AbortError');
      const capture = parseRasterCalculatorInspection(raw, { context, map: document!, projectXmlSHA: xmlSHA });
      const validation = validateRasterCalculatorRecipe(calculatorRecipeDraft(layer.recipe), capture, currentLanguage);
      if (!validation.recipe) throw new Error(validation.error ?? 'Stored calculator recipe is no longer valid for the current sources.');
      if (!sameRecipe(validation.recipe, layer.recipe)) {
        throw new Error('The stored calculator recipe changed while it was being checked.');
      }
      return capture;
    };

    const runLayer = async (layer: RasterCalculatorSessionLayer, key: string, signal: AbortSignal,
      currentLanguage: Language): Promise<void> => {
      const currentOptions = latestOptions.current;
      const currentDocument = currentOptions?.document;
      const currentContext = currentOptions?.context;
      if (!currentOptions || !currentDocument || !currentContext || !isCurrent(signal)) return;

      try {
        if (!backend.supports('raster.calculator.inspect') || !backend.supports('raster.calculator.evaluate')) {
          throw new Error(text(currentLanguage,
            '当前 C# 服务尚未发布栅格计算检查和计算操作。',
            'The C# Host has not advertised calculator inspect and evaluate operations.'));
        }
        const currentViewport = currentOptions.viewport;
        if (!isValidViewport(currentViewport)) {
          throw new Error(text(currentLanguage, '当前地图相机没有可用的栅格视口。', 'The current map camera has no valid raster viewport.'));
        }
        if (!calculatorLayerMatchesMap(layer, currentContext, currentDocument)) return;

        const xmlSHA = await projectXmlSha(currentContext.projectXml);
        if (!isCurrent(signal)) return;
        if (!xmlSHA) throw new Error(text(currentLanguage, '当前工程 XML 快照不可用。', 'The current project XML snapshot is unavailable.'));
        const map = buildRasterCalculatorMapRequest(currentContext, currentDocument, xmlSHA);
        const currentMapCaptureKey = stableJson(map);
        const currentMapOwnerKey = mapOwnerKey;
        const currentDescriptorKey = descriptorKey(layer);
        let capture: RasterCalculatorMapCapture;
        const cached = sourceCaptures.current.get(layer.id);
        if (cached?.descriptorKey === currentDescriptorKey && cached.mapOwnerKey === currentMapOwnerKey &&
            captureMatchesCurrent(cached.capture, currentContext, currentDocument, xmlSHA, currentMapCaptureKey)) {
          capture = cached.capture;
        } else if (captureMatchesCurrent(layer.capture, currentContext, currentDocument, xmlSHA, currentMapCaptureKey)) {
          capture = layer.capture;
          sourceCaptures.current.set(layer.id, { descriptorKey: currentDescriptorKey, mapOwnerKey: currentMapOwnerKey, capture });
        } else {
          capture = await inspectCurrentMap(layer, map, xmlSHA, signal, currentLanguage);
          if (!isCurrent(signal)) return;
          sourceCaptures.current.set(layer.id, { descriptorKey: currentDescriptorKey, mapOwnerKey: currentMapOwnerKey, capture });
        }

        const validation = validateRasterCalculatorRecipe(calculatorRecipeDraft(layer.recipe), capture, currentLanguage);
        if (!validation.recipe) throw new Error(validation.error ?? 'Stored calculator recipe is no longer valid for the current sources.');
        if (!sameRecipe(validation.recipe, layer.recipe)) throw new Error('The stored calculator recipe is no longer identical to the validated recipe.');
        if (!Number.isInteger(layer.profileIndex) || layer.profileIndex < 0 || layer.profileIndex >= layer.profileCount) {
          throw new Error(text(currentLanguage, '计算器所选 Profile 已不在有效时刻范围内。', 'The selected calculator profile is outside the valid profile range.'));
        }

        const evaluate = async (activeCapture: RasterCalculatorMapCapture): Promise<ReturnType<typeof parseRasterCalculatorEvaluation>> => {
          const requestID = layer.id;
          const raw: unknown = await backend.request('raster.calculator.evaluate', {
            map: activeCapture.map,
            expectedContextToken: activeCapture.contextToken,
            recipe: validation.recipe,
            layerID: requestID,
            layerName: layer.name,
            profileIndex: layer.profileIndex,
            viewport: currentViewport,
          }, { signal });
          if (!isCurrent(signal)) throw new DOMException('The calculator request is no longer current.', 'AbortError');
          const result = parseRasterCalculatorEvaluation(raw, {
            capture: activeCapture,
            layerID: requestID,
            layerName: layer.name,
            profileIndex: layer.profileIndex,
          });
          validateEvaluation(result, layer, validation.recipe!, currentViewport);
          return result;
        };

        let result: ReturnType<typeof parseRasterCalculatorEvaluation>;
        try {
          result = await evaluate(capture);
        } catch (error) {
          if (!isCurrent(signal) || errorCode(error) !== 'sourceChanged') throw error;
          setFrames((current) => {
            if (!(layer.id in current)) return current;
            const next = { ...current };
            delete next[layer.id];
            return next;
          });
          updateState(layer.id, key, {
            status: 'loading',
            message: text(currentLanguage, '来源发生变化，正在重新检查原配方…', 'A source changed; rechecking the stored recipe…'),
          });
          // A fresh inspect can advance unrelated owner/source captures, but
          // the stored recipe must still bind the same exact source IDs/revisions.
          const refreshed = await inspectCurrentMap(layer, map, xmlSHA, signal, currentLanguage);
          if (!isCurrent(signal)) return;
          sourceCaptures.current.set(layer.id, { descriptorKey: currentDescriptorKey, mapOwnerKey: currentMapOwnerKey, capture: refreshed });
          const refreshedValidation = validateRasterCalculatorRecipe(calculatorRecipeDraft(layer.recipe), refreshed, currentLanguage);
          if (!refreshedValidation.recipe || !sameRecipe(refreshedValidation.recipe, layer.recipe)) {
            throw new Error(refreshedValidation.error ?? 'The stored recipe no longer matches its captured source identities.');
          }
          result = await evaluate(refreshed);
          capture = refreshed;
        }
        if (!isCurrent(signal)) return;

        const image: MapImageLayer = {
          id: layer.id,
          name: layer.name,
          imageSrc: rgbaFrameImage(result.layer.rgba, result.layer.width, result.layer.height),
          extent: {
            minX: result.layer.minX, minY: result.layer.minY,
            maxX: result.layer.maxX, maxY: result.layer.maxY,
          },
          sourceKind: 'calculator',
          opacity: layer.opacity,
          profileIndex: result.selectedProfileIndex,
        };
        const acceptedFrameKey = makeFrameKey(mapOwnerKey, viewportKey, currentDescriptorKey, capture.contextToken);
        setFrames((current) => ({ ...current, [layer.id]: { frameKey: acceptedFrameKey, image } }));
        updateState(layer.id, acceptedFrameKey, { status: 'ready' });
      } catch (error) {
        if (!isCurrent(signal)) return;
        failLayer(layer, key, error, currentLanguage);
      }
    };

    const activeTargets = matchingLayers.map((layer) => ({ layer, key: frameKeys.get(layer.id)! }));
    for (const { layer, key } of activeTargets) {
      const cachedFrame = frames[layer.id];
      const acceptedFrameKey = frameKeys.get(layer.id)!;
      if (cachedFrame?.frameKey === acceptedFrameKey && internalStates[layer.id]?.status === 'ready' &&
          internalStates[layer.id]?.frameKey === acceptedFrameKey) continue;
      updateState(layer.id, key, { status: 'loading', message: text(language, '正在更新计算图层…', 'Updating calculator layer…') });
      const aborter = new AbortController();
      aborters.push(aborter);
      const timer = setTimeout(() => { void runLayer(layer, key, aborter.signal, language); }, viewportDebounceMilliseconds);
      timers.push(timer);
    }

    return () => {
      if (generation.current === requestGeneration) generation.current += 1;
      for (const timer of timers) clearTimeout(timer);
      for (const aborter of aborters) aborter.abort();
    };
  }, [backend, epochKey, language]);

  const images: MapImageLayer[] = [];
  const states: Record<string, RasterCalculatorSessionLayerState> = {};
  for (const layer of matchingLayers) {
    const key = frameKeys.get(layer.id)!;
    const frame = frames[layer.id];
    if (frame?.frameKey === key) {
      images.push({ ...frame.image, opacity: layer.opacity });
    }
    const state = internalStates[layer.id];
    states[layer.id] = state?.frameKey === key
      ? { status: state.status, ...(state.message ? { message: state.message } : {}), ...(state.diagnostic ? { diagnostic: state.diagnostic } : {}) }
      : { status: 'loading', message: text(language, '正在更新计算图层…', 'Updating calculator layer…') };
  }
  return { images, states };
}

function descriptorKey(layer: RasterCalculatorSessionLayer): string {
  return stableJson({
    id: layer.id,
    revision: layer.revision,
    name: layer.name,
    recipe: recipeKey(layer.recipe),
    captureOwner: layer.capture.owner,
    captureProjectRevision: layer.capture.projectRevision,
    captureProjectXmlSHA: layer.capture.projectXmlSHA,
    captureVisualizationRevision: layer.capture.visualizationRevision,
    captureMap: layer.capture.map,
    captureToken: layer.capture.contextToken,
    captureSources: layer.capture.sources.map((source) => [source.id, source.kind, source.assetPath, source.revision]),
    isDynamic: layer.isDynamic,
    profileCount: layer.profileCount,
    profiles: layer.profiles,
    profileIndex: layer.profileIndex,
    extent: layer.extent,
  });
}

function recipeKey(recipe: RasterCalculatorRecipePayload): string {
  return stableJson({
    expression: recipe.expression,
    noDataBehavior: recipe.noDataBehavior,
    keyFrameVariableName: recipe.keyFrameVariableName ?? null,
    inputs: recipe.inputs.map((input) => ({
      variableName: input.variableName,
      sourceID: input.sourceID,
      sourceRevision: input.sourceRevision,
      kind: input.kind,
      mapType: input.mapType ?? null,
      forceHorizontal: input.forceHorizontal,
      isKeyFrame: input.isKeyFrame,
    })),
    constants: recipe.constants.map((constant) => ({ name: constant.name, value: constant.value })),
  });
}

function sameRecipe(left: RasterCalculatorRecipePayload, right: RasterCalculatorRecipePayload): boolean {
  return recipeKey(left) === recipeKey(right);
}

function captureMatchesCurrent(
  capture: RasterCalculatorMapCapture,
  context: WorkbenchContext,
  document: MapDocumentTab,
  xmlSHA: string,
  mapCaptureKey: string,
): boolean {
  if (capture.owner !== rasterCalculatorOwner(context) || capture.projectPath !== context.projectPath ||
      capture.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
      capture.projectXmlSHA.toUpperCase() !== xmlSHA.toUpperCase() ||
      capture.visualizationRevision.toUpperCase() !== document.visualizationRevision?.toUpperCase() ||
      capture.mapID !== document.mapID || capture.mapIndex !== document.mapIndex ||
      capture.mapName !== document.name || capture.mapType !== document.mapType) return false;
  return stableJson(capture.map) === mapCaptureKey;
}

function validateEvaluation(
  result: ReturnType<typeof parseRasterCalculatorEvaluation>,
  layer: RasterCalculatorSessionLayer,
  expectedRecipe: RasterCalculatorRecipePayload,
  viewport: RasterMapViewExtent,
): void {
  if (!sameRecipe(result.recipe, expectedRecipe)) throw new Error('Host returned a different calculator recipe.');
  if (result.isDynamic !== layer.isDynamic || result.profileCount !== layer.profileCount ||
      result.profiles.length !== layer.profiles.length || result.selectedProfileIndex !== layer.profileIndex) {
    throw new Error('Host returned incompatible calculator profile or static/dynamic metadata.');
  }
  for (let index = 0; index < layer.profiles.length; index += 1) {
    const expected = layer.profiles[index];
    const actual = result.profiles[index];
    if (!expected || !actual || actual.index !== expected.index || actual.time !== expected.time) {
      throw new Error('Host calculator profile times no longer match this session layer.');
    }
  }
  const selectedProfile = result.profiles[result.selectedProfileIndex];
  if (result.isDynamic) {
    if (!selectedProfile || result.selectedProfileTime !== selectedProfile.time) {
      throw new Error('Host returned a mismatched temporal calculator frame.');
    }
  } else if (result.selectedProfileTime !== null) {
    throw new Error('Host returned a time for a static calculator layer.');
  }
  const cellSize = Math.max(
    (viewport.maxX - viewport.minX) / viewport.pixelWidth,
    (viewport.maxY - viewport.minY) / viewport.pixelHeight,
  );
  const expectedExtent = {
    minX: viewport.minX,
    maxX: viewport.minX + cellSize * viewport.pixelWidth,
    maxY: viewport.maxY,
    minY: viewport.maxY - cellSize * viewport.pixelHeight,
  };
  if (result.minX !== layer.extent.minX || result.minY !== layer.extent.minY ||
      result.maxX !== layer.extent.maxX || result.maxY !== layer.extent.maxY ||
      result.layer.width !== viewport.pixelWidth || result.layer.height !== viewport.pixelHeight ||
      !nearlyEqual(result.layer.minX, expectedExtent.minX, cellSize * viewport.pixelWidth) ||
      !nearlyEqual(result.layer.minY, expectedExtent.minY, cellSize * viewport.pixelHeight) ||
      !nearlyEqual(result.layer.maxX, expectedExtent.maxX, cellSize * viewport.pixelWidth) ||
      !nearlyEqual(result.layer.maxY, expectedExtent.maxY, cellSize * viewport.pixelHeight)) {
    throw new Error('Host calculator frame extent or dimensions do not match the current camera.');
  }
}

function nearlyEqual(actual: number, expected: number, span: number): boolean {
  const tolerance = 32 * Number.EPSILON * Math.max(1, Math.abs(actual), Math.abs(expected), Math.abs(span));
  return Math.abs(actual - expected) <= tolerance;
}

function isValidViewport(viewport: RasterMapViewExtent | null): viewport is RasterMapViewExtent {
  return Boolean(viewport && [viewport.minX, viewport.minY, viewport.maxX, viewport.maxY].every(Number.isFinite) &&
    viewport.minX < viewport.maxX && viewport.minY < viewport.maxY &&
    Number.isInteger(viewport.pixelWidth) && Number.isInteger(viewport.pixelHeight) &&
    viewport.pixelWidth > 0 && viewport.pixelHeight > 0 && viewport.pixelWidth <= 1536 && viewport.pixelHeight <= 1536 &&
    viewport.pixelWidth * viewport.pixelHeight <= 2_097_152);
}

function makeFrameKey(owner: string, viewport: string, descriptor: string, contextToken: string): string {
  return stableJson([owner, viewport, descriptor, contextToken]);
}

function stableJson(value: unknown): string {
  return JSON.stringify(stableValue(value));
}

function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value).sort().map((key) => [key, stableValue((value as Record<string, unknown>)[key])]));
}

function errorCode(error: unknown): string | null {
  if (!isRecord(error)) return null;
  return typeof error.code === 'string' ? error.code : null;
}

function diagnosticText(error: unknown): string {
  const diagnostic = errorDiagnostics(error);
  try {
    if (diagnostic instanceof Error) return `${diagnostic.name}: ${diagnostic.message}`;
    const serialized = JSON.stringify(diagnostic, null, 2);
    return serialized && serialized !== '{}' ? serialized : String(error);
  } catch {
    return String(error);
  }
}
