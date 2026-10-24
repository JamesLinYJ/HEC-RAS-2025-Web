import { useCallback, useRef, useState } from 'react';
import type { FeatureId, WorkbenchContext } from './workbench';
import {
  rasterCalculatorOwner,
  parseRasterCalculatorEvaluation,
  validateRasterCalculatorRecipe,
  type RasterCalculatorEvaluateResult,
  type RasterCalculatorMapCapture,
} from '../features/tools/RasterCalculatorRecipe';
import type {
  RasterCalculatorSessionCandidate,
  RasterCalculatorSessionLayer,
  RasterCalculatorSessionRequest,
  RasterCalculatorSessionTarget,
  RasterCalculatorSessions,
} from './rasterCalculatorSession';
import { calculatorRecipeDraft } from './rasterCalculatorSession';

interface CalculatorSessionState {
  scope: string;
  layers: RasterCalculatorSessionLayer[];
  selectedLayerID: string | null;
  editRequest: RasterCalculatorSessionRequest | null;
  focusRequest: { requestID: string; layerID: string } | null;
}

function scopeFor(context: WorkbenchContext): string {
  return JSON.stringify([context.projectSessionID, context.projectPath]);
}

function emptyState(scope: string): CalculatorSessionState {
  return { scope, layers: [], selectedLayerID: null, editRequest: null, focusRequest: null };
}

function newRequestID(): string {
  return crypto.randomUUID();
}

function captureMapSession(capture: RasterCalculatorMapCapture): string | null {
  const sessionID = capture.map.projectSessionID;
  return typeof sessionID === 'string' && sessionID.length > 0
    ? sessionID
    : null;
}

function sameCaptureMap(left: RasterCalculatorMapCapture, right: RasterCalculatorMapCapture): boolean {
  return left.projectPath === right.projectPath &&
    captureMapSession(left) !== null && captureMapSession(left) === captureMapSession(right) &&
    left.mapID === right.mapID && left.mapIndex === right.mapIndex &&
    left.mapName === right.mapName && left.mapType === right.mapType;
}

function evaluationMatchesCapture(
  evaluation: RasterCalculatorEvaluateResult,
  capture: RasterCalculatorMapCapture,
): boolean {
  return evaluation.projectPath === capture.projectPath &&
    evaluation.projectRevision.toUpperCase() === capture.projectRevision.toUpperCase() &&
    evaluation.projectXmlSHA.toUpperCase() === capture.projectXmlSHA.toUpperCase() &&
    evaluation.visualizationRevision.toUpperCase() === capture.visualizationRevision.toUpperCase() &&
    evaluation.mapID === capture.mapID && evaluation.mapIndex === capture.mapIndex &&
    evaluation.mapName === capture.mapName && evaluation.mapType === capture.mapType &&
    evaluation.contextToken.toUpperCase() === capture.contextToken.toUpperCase();
}

function recipeIsBoundToCapture(
  evaluation: RasterCalculatorEvaluateResult,
  capture: RasterCalculatorMapCapture,
): boolean {
  if (evaluation.recipe.inputs.length === 0) return false;
  return evaluation.recipe.inputs.every(input => {
    const source = capture.sources.find(candidate => candidate.id === input.sourceID);
    if (!source || !source.isAvailable || source.revision.toUpperCase() !== input.sourceRevision.toUpperCase() ||
        source.kind !== input.kind) return false;
    if (input.kind === 'terrain') {
      return input.mapType === undefined && !input.forceHorizontal && !input.isKeyFrame;
    }
    return Boolean(input.mapType && source.mapTypes.includes(input.mapType));
  });
}

function validateCandidate(candidate: RasterCalculatorSessionCandidate, context: WorkbenchContext): void {
  const { capture, evaluation } = candidate;
  parseRasterCalculatorEvaluation({ ...evaluation, recipe: { ...evaluation.recipe,
    inputs: evaluation.recipe.inputs.map(input => ({ ...input, mapType: input.mapType ?? null })) } },
    { capture, layerID: evaluation.layerID,
    layerName: evaluation.layerName, profileIndex: evaluation.selectedProfileIndex });
  const validation = validateRasterCalculatorRecipe(calculatorRecipeDraft(evaluation.recipe), capture);
  if (!validation.recipe || validation.error || JSON.stringify(validation.recipe) !== JSON.stringify(evaluation.recipe)) {
    throw new Error(validation.error ?? '计算图层配方与当前来源捕获不一致，请重新计算。');
  }
  if (!context.projectPath || !context.projectSessionID || capture.owner !== rasterCalculatorOwner(context) ||
      capture.projectPath !== context.projectPath || captureMapSession(capture) !== context.projectSessionID ||
      capture.map.projectPath !== context.projectPath || capture.map.projectSessionID !== context.projectSessionID ||
      capture.map.projectXmlSHA !== capture.projectXmlSHA ||
      capture.map.expectedVisualizationRevision !== capture.visualizationRevision ||
      capture.map.mapID !== capture.mapID || capture.map.mapIndex !== capture.mapIndex ||
      capture.map.mapName !== capture.mapName || capture.map.mapType !== capture.mapType ||
      !evaluationMatchesCapture(evaluation, capture) || !recipeIsBoundToCapture(evaluation, capture)) {
    throw new Error('栅格计算结果的工程、地图或来源身份已过期，请重新检查来源并计算。');
  }
  const { layer } = evaluation;
  if (!evaluation.layerID || !evaluation.layerName || layer.id !== evaluation.layerID ||
      layer.mapID !== capture.mapID || layer.name !== evaluation.layerName ||
      !Number.isInteger(evaluation.profileCount) || evaluation.profileCount < 1 ||
      evaluation.profiles.length !== evaluation.profileCount ||
      !Number.isInteger(evaluation.selectedProfileIndex) || evaluation.selectedProfileIndex < 0 ||
      evaluation.selectedProfileIndex >= evaluation.profileCount ||
      ![layer.minX, layer.minY, layer.maxX, layer.maxY].every(Number.isFinite) ||
      layer.minX >= layer.maxX || layer.minY >= layer.maxY) {
    throw new Error('栅格计算结果的图层、范围或 Profile 回执无效。');
  }
}

function sessionLayerFrom(
  evaluation: RasterCalculatorEvaluateResult,
  capture: RasterCalculatorMapCapture,
  id: string,
  revision: string,
  name: string,
  visible: boolean,
  opacity: number,
): RasterCalculatorSessionLayer {
  return {
    id,
    revision,
    name,
    capture: {
      ...capture,
      map: { ...capture.map },
      sources: capture.sources.map(source => ({
        ...source,
        files: source.files.map(file => ({ ...file })),
        mapTypes: [...source.mapTypes],
        profileTimes: [...source.profileTimes],
      })),
    },
    recipe: {
      ...evaluation.recipe,
      inputs: evaluation.recipe.inputs.map(input => ({ ...input })),
      constants: evaluation.recipe.constants.map(constant => ({ ...constant })),
    },
    isDynamic: evaluation.isDynamic,
    profiles: evaluation.profiles.map(profile => ({ ...profile })),
    profileCount: evaluation.profileCount,
    profileIndex: evaluation.selectedProfileIndex,
    visible,
    opacity,
    extent: {
      minX: evaluation.minX,
      minY: evaluation.minY,
      maxX: evaluation.maxX,
      maxY: evaluation.maxY,
    },
  };
}

export function useRasterCalculatorSessions(
  context: WorkbenchContext,
  onNavigate: (feature: FeatureId) => void,
  blocked: boolean,
): RasterCalculatorSessions {
  const scope = scopeFor(context);
  const [, forceRender] = useState(0);
  const stateRef = useRef<CalculatorSessionState>(emptyState(scope));
  const scopeRef = useRef(scope);
  const contextRef = useRef(context);
  const navigateRef = useRef(onNavigate);
  const blockedRef = useRef(blocked);

  contextRef.current = context;
  navigateRef.current = onNavigate;
  blockedRef.current = blocked;
  if (scopeRef.current !== scope) {
    scopeRef.current = scope;
    stateRef.current = emptyState(scope);
  }

  const commit = useCallback((expectedScope: string, next: CalculatorSessionState): boolean => {
    if (blockedRef.current || scopeRef.current !== expectedScope || next.scope !== expectedScope) return false;
    stateRef.current = next;
    forceRender(value => value + 1);
    return true;
  }, []);

  const publish = useCallback((
    candidate: RasterCalculatorSessionCandidate,
    replace?: RasterCalculatorSessionTarget,
  ): string => {
    if (blockedRef.current || scopeRef.current !== scope) {
      throw new Error('当前工程正在切换或保存，请等待操作结束后再应用计算图层。');
    }
    const currentContext = contextRef.current;
    validateCandidate(candidate, currentContext);
    const current = stateRef.current;
    const evaluation = candidate.evaluation;
    let id = evaluation.layerID;
    let name = evaluation.layerName;
    let visible = true;
    let opacity = 1;
    let replacingIndex = -1;
    if (replace) {
      replacingIndex = current.layers.findIndex(layer => layer.id === replace.layerID);
      const target = current.layers[replacingIndex];
      if (!target || target.revision !== replace.revision ||
          !sameCaptureMap(target.capture, candidate.capture) || target.isDynamic !== evaluation.isDynamic) {
        throw new Error('要替换的计算图层已变化，或与新结果的地图/静态动态类型不兼容。');
      }
      id = target.id;
      name = target.name;
      visible = target.visible;
      opacity = target.opacity;
    } else if (current.layers.some(layer => layer.id === id)) {
      // Add creates a new context even when the editor reuses one preview.
      id = `raster-calculator-${newRequestID()}`;
    }

    const revision = newRequestID();
    const nextLayer = sessionLayerFrom(evaluation, candidate.capture, id, revision, name, visible, opacity);
    const layers = current.layers.slice();
    if (replacingIndex >= 0) layers[replacingIndex] = nextLayer;
    else layers.push(nextLayer);
    const focusRequest = { requestID: newRequestID(), layerID: id };
    if (!commit(scope, {
      ...current,
      layers,
      selectedLayerID: id,
      editRequest: null,
      focusRequest,
    })) throw new Error('工程会话已变化，未应用过期的计算图层。');
    navigateRef.current('map');
    return id;
  }, [commit, scope]);

  const select = useCallback((layerID: string | null): void => {
    if (blockedRef.current || scopeRef.current !== scope) return;
    const current = stateRef.current;
    if (layerID !== null && !current.layers.some(layer => layer.id === layerID)) return;
    const editRequest = current.editRequest && current.editRequest.layerID === layerID &&
      current.layers.some(layer => layer.id === layerID && layer.revision === current.editRequest?.revision)
      ? current.editRequest : null;
    const focusRequest = current.focusRequest && current.focusRequest.layerID === layerID
      ? current.focusRequest : null;
    if (current.selectedLayerID === layerID && current.editRequest === editRequest &&
        current.focusRequest === focusRequest) return;
    commit(scope, { ...current, selectedLayerID: layerID, editRequest, focusRequest });
  }, [commit, scope]);

  const setVisible = useCallback((layerID: string, visible: boolean): void => {
    if (blockedRef.current || scopeRef.current !== scope) return;
    const current = stateRef.current;
    if (!current.layers.some(layer => layer.id === layerID)) return;
    commit(scope, {
      ...current,
      layers: current.layers.map(layer => layer.id === layerID ? { ...layer, visible } : layer),
    });
  }, [commit, scope]);

  const setOpacity = useCallback((layerID: string, opacity: number): void => {
    if (blockedRef.current || scopeRef.current !== scope || !Number.isFinite(opacity)) return;
    const boundedOpacity = Math.max(0, Math.min(1, opacity));
    const current = stateRef.current;
    if (!current.layers.some(layer => layer.id === layerID)) return;
    commit(scope, {
      ...current,
      layers: current.layers.map(layer => layer.id === layerID ? { ...layer, opacity: boundedOpacity } : layer),
    });
  }, [commit, scope]);

  const setProfile = useCallback((layerID: string, profileIndex: number): void => {
    if (blockedRef.current || scopeRef.current !== scope || !Number.isInteger(profileIndex)) return;
    const current = stateRef.current;
    const target = current.layers.find(layer => layer.id === layerID);
    if (!target || profileIndex < 0 || profileIndex >= target.profileCount || target.profileIndex === profileIndex) return;
    commit(scope, {
      ...current,
      layers: current.layers.map(layer => layer.id === layerID ? { ...layer, profileIndex } : layer),
    });
  }, [commit, scope]);

  const remove = useCallback((layerID: string): void => {
    if (blockedRef.current || scopeRef.current !== scope) return;
    const current = stateRef.current;
    if (!current.layers.some(layer => layer.id === layerID)) return;
    commit(scope, {
      ...current,
      layers: current.layers.filter(layer => layer.id !== layerID),
      selectedLayerID: current.selectedLayerID === layerID ? null : current.selectedLayerID,
      editRequest: current.editRequest?.layerID === layerID ? null : current.editRequest,
      focusRequest: current.focusRequest?.layerID === layerID ? null : current.focusRequest,
    });
  }, [commit, scope]);

  const edit = useCallback((layerID: string): void => {
    if (blockedRef.current || scopeRef.current !== scope) return;
    const current = stateRef.current;
    const target = current.layers.find(layer => layer.id === layerID);
    if (!target) return;
    const editRequest: RasterCalculatorSessionRequest = {
      layerID,
      revision: target.revision,
      requestID: newRequestID(),
    };
    if (!commit(scope, { ...current, selectedLayerID: layerID, editRequest, focusRequest: null })) return;
    navigateRef.current('tools');
  }, [commit, scope]);

  const finishEditRequest = useCallback((requestID: string): void => {
    const current = stateRef.current;
    if (scopeRef.current !== scope || current.scope !== scope || current.editRequest?.requestID !== requestID) return;
    stateRef.current = { ...current, editRequest: null };
    forceRender(value => value + 1);
  }, [scope]);

  const current = stateRef.current.scope === scope ? stateRef.current : emptyState(scope);
  return {
    layers: current.layers,
    selectedLayerID: current.selectedLayerID,
    editRequest: current.editRequest,
    focusRequest: current.focusRequest,
    publish,
    select,
    setVisible,
    setOpacity,
    setProfile,
    remove,
    edit,
    finishEditRequest,
  };
}
