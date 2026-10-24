import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway } from '../../../shared/workbench';
import type { XY } from '../mapTypes';
import { sameMapID, sameMapLocator, sameMapRevision } from '../associatedMapLayers';
import type { TerrainElevationCapture } from './terrainElevationCapture';

export type TerrainElevationMeasurementState =
  | { status: 'loading'; scopeKey: string; captureKey: string; terrainName: string; segmentLength: number }
  | { status: 'ready'; scopeKey: string; captureKey: string; terrainName: string; segmentLength: number; latestElevation: number | null; previousElevation: number | null; slope: number | null }
  | { status: 'unavailable' | 'error'; scopeKey: string; captureKey: string | null; terrainName: string | null; segmentLength: number | null; message: string };

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function pathEqual(left: string, right: string): boolean {
  const normalize = (value: string) => value.replace(/\\/g, '/');
  const a = normalize(left), b = normalize(right);
  if (a === b) return true;
  return /^[a-z]:\//i.test(a) && /^[a-z]:\//i.test(b) && a.toUpperCase() === b.toUpperCase();
}

function requiredString(value: unknown, field: string): string {
  if (typeof value !== 'string' || value.length === 0) throw new Error(`地形取样回执缺少 ${field}。`);
  return value;
}

function isSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function validateReceipt(value: unknown, capture: TerrainElevationCapture): [number | null, number | null] {
  const response = record(value);
  if (!response) throw new Error('地形取样回执格式无效。');
  if (!pathEqual(requiredString(response.projectPath, 'projectPath'), String(capture.map.projectPath)) ||
      !sameMapRevision(requiredString(response.projectRevision, 'projectRevision'), capture.projectRevision) ||
      !sameMapRevision(requiredString(response.projectXmlSHA, 'projectXmlSHA'), String(capture.map.projectXmlSHA)) ||
      !sameMapRevision(requiredString(response.visualizationRevision, 'visualizationRevision'), String(capture.map.expectedVisualizationRevision)) ||
      !sameMapID(requiredString(response.mapID, 'mapID'), String(capture.map.mapID)) ||
      response.mapIndex !== capture.map.mapIndex || response.mapName !== capture.map.mapName || response.mapType !== capture.map.mapType ||
      requiredString(response.contextToken, 'contextToken') !== capture.catalogContextToken ||
      !sameMapLocator(response.terrainNode as TerrainElevationCapture['terrainNode'], capture.terrainNode) ||
      !pathEqual(requiredString(response.terrainPath, 'terrainPath'), capture.terrainPath) ||
      // Host terrainRevision is a composite fingerprint of the Terrain HDF and tracked raster
      // dependencies. Keep it distinct from capture.sourceHdfRevision below.
      !isSHA256(response.terrainRevision)) {
    throw new Error('Terrain 高程取样回执已不属于当前地图、图层或来源修订。');
  }
  const dependencies = response.sourceDependencies;
  if (!Array.isArray(dependencies) || dependencies.length !== capture.dependencies.length) {
    throw new Error('Terrain 高程取样回执的来源依赖与检查快照不一致。');
  }
  const expected = [...capture.dependencies].sort((a, b) => a.role.localeCompare(b.role) || a.path.localeCompare(b.path));
  const actual = dependencies.map(item => record(item)).sort((a, b) =>
    String(a?.role ?? '').localeCompare(String(b?.role ?? '')) || String(a?.path ?? '').localeCompare(String(b?.path ?? '')));
  if (actual.some((item, index) => !item || item.role !== expected[index].role ||
      typeof item.path !== 'string' || !pathEqual(item.path, expected[index].path) ||
      typeof item.revision !== 'string' || !sameMapRevision(item.revision, expected[index].revision))) {
    throw new Error('Terrain 高程取样回执的来源 SHA 与捕获值不一致。');
  }
  const sourceHdf = actual.find(item => item?.role === 'source-hdf');
  if (!sourceHdf || typeof sourceHdf.path !== 'string' || !pathEqual(sourceHdf.path, capture.terrainPath) ||
      typeof sourceHdf.revision !== 'string' || !sameMapRevision(sourceHdf.revision, capture.sourceHdfRevision)) {
    throw new Error('Terrain 高程取样回执的 HDF 来源 SHA 与捕获值不一致。');
  }
  const elevations = response.elevations;
  if (!Array.isArray(elevations) || elevations.length !== 2 || elevations.some(item =>
      item !== null && (typeof item !== 'number' || !Number.isFinite(item)))) {
    throw new Error('Terrain 高程取样回执必须包含两个有限高程或明确的无数据值。');
  }
  return [elevations[0] as number | null, elevations[1] as number | null];
}

export function useTerrainElevationMeasurement(backend: BackendGateway, capture: TerrainElevationCapture | null,
  measurementScopeKey: string) {
  const [state, setState] = useState<TerrainElevationMeasurementState | null>(null);
  const generationRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);
  const captureRef = useRef(capture);
  const scopeKeyRef = useRef(measurementScopeKey);
  const activeCaptureKeyRef = useRef(capture?.key ?? null);
  const captureKey = capture?.key ?? null;
  captureRef.current = capture;
  scopeKeyRef.current = measurementScopeKey;

  useEffect(() => {
    generationRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
    activeCaptureKeyRef.current = null;
    setState(null);
    return () => {
      generationRef.current += 1;
      controllerRef.current?.abort();
      controllerRef.current = null;
    };
  }, [backend, measurementScopeKey]);

  useEffect(() => {
    // Association rendering can temporarily remove its frame while the camera/layout changes.
    // Keep the semantic source alive through that gap, but reset immediately when a new verified
    // capture identifies a different Terrain or Result owner.
    if (!captureKey || activeCaptureKeyRef.current === captureKey) return;
    activeCaptureKeyRef.current = captureKey;
    generationRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
    setState(null);
  }, [backend, captureKey, measurementScopeKey]);

  const clear = useCallback(() => {
    generationRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
    setState(null);
  }, []);

  const sample = useCallback((latestPoint: XY, previousPoint: XY, segmentLength: number, missingReason: string) => {
    const requestCapture = capture;
    const generation = ++generationRef.current;
    controllerRef.current?.abort();
    controllerRef.current = null;
    if (!requestCapture) {
      setState({ status: 'unavailable', scopeKey: measurementScopeKey, captureKey: null, terrainName: null, segmentLength,
        message: missingReason || '当前没有可核验的 Terrain 来源；仍保留平面测距结果。' });
      return;
    }
    if (activeCaptureKeyRef.current !== requestCapture.key) activeCaptureKeyRef.current = requestCapture.key;
    if (!backend.supports('map.measureTerrainElevations')) {
      setState({ status: 'unavailable', scopeKey: measurementScopeKey, captureKey: requestCapture.key, terrainName: requestCapture.terrainName,
        segmentLength, message: '当前 C# 服务未提供地形高程取样能力；平面测距结果仍有效。' });
      return;
    }
    if (![...latestPoint, ...previousPoint, segmentLength].every(Number.isFinite) || segmentLength <= 0) {
      setState({ status: 'unavailable', scopeKey: measurementScopeKey, captureKey: requestCapture.key, terrainName: requestCapture.terrainName,
        segmentLength, message: '末段长度无效，未请求地形取样；平面测距结果仍有效。' });
      return;
    }
    const controller = new AbortController();
    controllerRef.current = controller;
    const requestScopeKey = measurementScopeKey;
    const requestCaptureKey = requestCapture.key;
    const isCurrent = () => !controller.signal.aborted && generation === generationRef.current &&
      scopeKeyRef.current === requestScopeKey && activeCaptureKeyRef.current === requestCaptureKey &&
      (!captureRef.current || captureRef.current.key === requestCaptureKey);
    setState({ status: 'loading', scopeKey: requestScopeKey, captureKey: requestCaptureKey,
      terrainName: requestCapture.terrainName, segmentLength });
    void (async () => {
      try {
        const response = await backend.request<unknown>('map.measureTerrainElevations', {
          map: requestCapture.map,
          terrainNode: requestCapture.terrainNode,
          expectedTerrainPath: requestCapture.terrainPath,
          expectedTerrainRevision: requestCapture.sourceHdfRevision,
          expectedDependencies: requestCapture.dependencies,
          // Preserve RasSlopeDelegate's direction: newest point first, preceding point second.
          endpoints: [{ x: latestPoint[0], y: latestPoint[1] }, { x: previousPoint[0], y: previousPoint[1] }],
          resultOwners: requestCapture.resultOwners,
        }, { signal: controller.signal });
        if (!isCurrent()) return;
        const [latestElevation, previousElevation] = validateReceipt(response, requestCapture);
        const slope = latestElevation === null || previousElevation === null
          ? null : (previousElevation - latestElevation) / segmentLength;
        setState({ status: 'ready', scopeKey: requestScopeKey, captureKey: requestCaptureKey, terrainName: requestCapture.terrainName,
          segmentLength, latestElevation, previousElevation, slope });
      } catch (error) {
        if (!isCurrent()) return;
        const message = error instanceof Error ? error.message : String(error ?? 'Terrain 高程取样失败。');
        setState({ status: 'error', scopeKey: requestScopeKey, captureKey: requestCaptureKey, terrainName: requestCapture.terrainName,
          segmentLength, message });
      } finally {
        if (controllerRef.current === controller) controllerRef.current = null;
      }
    })();
  }, [backend, capture, measurementScopeKey]);

  return { state, sample, clear };
}
