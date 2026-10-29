import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, FeaturePageProps, Language, WorkbenchContext } from '../../../shared/workbench';
import { mapCatalogMatchesDocument } from '../associatedMapLayers';
import type { MapDocumentTab } from '../mapTypes';
import type { MapResultFramesController } from '../useMapResultFrames';
import { isMapResultSourcePending } from '../useMapResultFrames';
import { normalizedResultMapType, pendingResultLayerWrites, ResultLayerWrite, RESULT_MAP_TYPES } from './resultLayerSettingsTransaction';
import type { ResultLayerReadback, ResultLayerWriteCapture, ResultLayerWriteState, ResultMapType } from './resultLayerSettingsTransaction';

interface Options extends Pick<FeaturePageProps, 'onBeginMapVisualizationMutation' | 'onCommitMapVisualization' | 'onEndMapVisualizationMutation'> {
  backend: BackendGateway;
  context: WorkbenchContext;
  document: MapDocumentTab | null;
  frames: MapResultFramesController;
  language: Language;
  scope: string;
  otherBusy: boolean;
  onGuardChange: (locked: boolean) => void;
  onInstall: (readback: ResultLayerReadback, capture: ResultLayerWriteCapture) => boolean;
}
export interface ResultLayerSettingsController {
  groupNames: string[];
  currentType: string | null;
  mixed: boolean;
  locked: boolean;
  canChange: boolean;
  state: ResultLayerWriteState | null;
  canResume: boolean;
  selectType: (value: string) => void;
  resume: () => void;
}

export function useResultLayerSettings(options: Options): ResultLayerSettingsController {
  const opts = useRef(options); opts.current = options;
  const [sequence, setSequence] = useState(0);
  const last = useRef<ResultLayerWrite | null>(null);
  const current = pendingResultLayerWrites.get(options.scope) ?? (last.current?.capture.scope === options.scope ? last.current : null);
  const installer = useRef((readback: ResultLayerReadback, capture: ResultLayerWriteCapture) => opts.current.onInstall(readback, capture));
  useEffect(() => {
    if (!current) { options.onGuardChange(false); return; }
    last.current = current;
    current.attach(installer.current);
    const update = () => {
      opts.current.onGuardChange(current.state.phase !== 'complete');
      setSequence(value => value + 1);
    };
    const unsubscribe = current.subscribe(update);
    update();
    return () => { unsubscribe(); current.detach(installer.current); };
  }, [current, options.scope]);
  void sequence;

  const catalog = options.frames.sources;
  const map = options.frames.mapRequest;
  const active = catalog?.activeNodes.filter(node => node.nodeKind === 'MultiResult' && node.node.nodeType === 'MultiResult') ?? [];
  const values = new Set(active.map(node => normalizedResultMapType(node.selectedMapType)));
  const currentType = values.size === 1 ? [...values][0] : null;
  const mixed = active.length > 0 && (values.size !== 1 || currentType === null);
  const locked = Boolean(current && current.state.phase !== 'complete');
  const callbacks = options.onBeginMapVisualizationMutation && options.onCommitMapVisualization && options.onEndMapVisualizationMutation;
  const canChange = Boolean(active.length && catalog && map && options.document && !locked && !options.otherBusy && callbacks &&
    options.frames.status !== 'loading' && mapCatalogMatchesDocument(catalog, options.context, options.document) &&
    !catalog.activeNodes.some(node => node.selections.some(selection => selection.selectedResult && isMapResultSourcePending(selection.selectedResult))) &&
    ['map.tree.perform', 'map.tree.load', 'map.document.list', 'map.resultSources'].every(method => options.backend.supports(method)));

  const selectType = (value: string) => {
    const latest = opts.current;
    const source = latest.frames.sources, request = latest.frames.mapRequest, document = latest.document;
    const selected = RESULT_MAP_TYPES.find(type => type === value);
    if (!selected || !canChange || !source || !request || !document || pendingResultLayerWrites.has(latest.scope) ||
        !latest.onBeginMapVisualizationMutation || !latest.onCommitMapVisualization || !latest.onEndMapVisualizationMutation ||
        !mapCatalogMatchesDocument(source, latest.context, document)) return;
    const activeGroups = source.activeNodes.filter(node => node.nodeKind === 'MultiResult' && node.node.nodeType === 'MultiResult');
    if (!activeGroups.length || activeGroups.every(node => normalizedResultMapType(node.selectedMapType) === selected)) return;
    const target = { mapID: document.mapID, mapIndex: document.mapIndex, name: document.name, mapType: document.mapType,
      visualizationRevision: document.visualizationRevision };
    const leaseID = latest.onBeginMapVisualizationMutation(latest.context, target, 'map.write:' + latest.scope);
    if (!leaseID) return;
    const capture: ResultLayerWriteCapture = {
      scope: latest.scope, backend: latest.backend, context: structuredClone(latest.context), document: structuredClone(document),
      map: structuredClone(request), catalog: structuredClone(source), type: selected as ResultMapType,
      target, leaseID, commit: latest.onCommitMapVisualization, end: latest.onEndMapVisualizationMutation,
    };
    const write = new ResultLayerWrite(capture);
    pendingResultLayerWrites.set(latest.scope, write);
    last.current = write;
    write.attach(installer.current);
    latest.onGuardChange(true);
    setSequence(number => number + 1);
    write.start();
  };
  return { groupNames: active.map(node => node.name), currentType, mixed, locked, canChange,
    state: current?.state ?? null, canResume: current?.canResume ?? false,
    selectType, resume: () => { if (current) void current.resume(); } };
}
