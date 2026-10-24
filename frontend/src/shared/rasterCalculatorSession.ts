import type { RasterCalculatorEvaluateResult, RasterCalculatorMapCapture, RasterCalculatorProfile,
  RasterCalculatorDraftInput, RasterCalculatorRecipePayload } from '../features/tools/RasterCalculatorRecipe';
import type { MapDocumentTab } from '../features/map/mapTypes';
import type { WorkbenchContext } from './workbench';

/** A live calculator recipe, never serialized into the saved map document. */
export interface RasterCalculatorSessionLayer {
  id: string;
  /** Changes only when the recipe/capture is replaced, for edit compare-and-swap. */
  revision: string;
  name: string;
  capture: RasterCalculatorMapCapture;
  recipe: RasterCalculatorRecipePayload;
  isDynamic: boolean;
  profiles: RasterCalculatorProfile[];
  profileCount: number;
  profileIndex: number;
  visible: boolean;
  opacity: number;
  extent: { minX: number; minY: number; maxX: number; maxY: number };
}
export interface RasterCalculatorSessionCandidate {
  capture: RasterCalculatorMapCapture;
  evaluation: RasterCalculatorEvaluateResult;
}
export interface RasterCalculatorSessionTarget { layerID: string; revision: string }
export interface RasterCalculatorSessionRequest extends RasterCalculatorSessionTarget { requestID: string }
export interface RasterCalculatorSessions {
  layers: readonly RasterCalculatorSessionLayer[];
  selectedLayerID: string | null;
  editRequest: RasterCalculatorSessionRequest | null;
  focusRequest: { requestID: string; layerID: string } | null;
  publish: (candidate: RasterCalculatorSessionCandidate, replace?: RasterCalculatorSessionTarget) => string;
  select: (layerID: string | null) => void;
  setVisible: (layerID: string, visible: boolean) => void;
  setOpacity: (layerID: string, opacity: number) => void;
  setProfile: (layerID: string, profileIndex: number) => void;
  remove: (layerID: string) => void;
  edit: (layerID: string) => void;
  finishEditRequest: (requestID: string) => void;
}

/**
 * A calculator descriptor belongs to the exact open project session and path,
 * and to the exact saved map identity. Current .ras/.vis revisions are allowed
 * to advance after reinspection; the next evaluation validates source files.
 */
export function calculatorLayerMatchesMap(
  layer: RasterCalculatorSessionLayer,
  context: WorkbenchContext,
  document: MapDocumentTab | null,
): boolean {
  const mapRequest = layer.capture.map;
  return Boolean(document && context.projectSessionID && context.projectPath &&
    layer.capture.projectPath === context.projectPath &&
    mapRequest.projectPath === context.projectPath &&
    mapRequest.projectSessionID === context.projectSessionID &&
    layer.capture.mapID === document.mapID && mapRequest.mapID === document.mapID &&
    layer.capture.mapIndex === document.mapIndex && mapRequest.mapIndex === document.mapIndex &&
    layer.capture.mapName === document.name && mapRequest.mapName === document.name &&
    layer.capture.mapType === document.mapType && mapRequest.mapType === document.mapType);
}

/** Convert a stored recipe into editor state with repeatable row identities. */
export function calculatorRecipeDraft(recipe: RasterCalculatorRecipePayload): RasterCalculatorDraftInput {
  return {
    expression: recipe.expression,
    noDataBehavior: recipe.noDataBehavior,
    inputs: recipe.inputs.map((input, index) => ({
      id: `calculator-input-${index}`,
      variableName: input.variableName,
      sourceID: input.sourceID,
      sourceRevision: input.sourceRevision,
      kind: input.kind,
      mapType: input.mapType ?? null,
      forceHorizontal: input.forceHorizontal,
      isKeyFrame: input.isKeyFrame,
    })),
    constants: recipe.constants.map((constant, index) => ({
      id: `calculator-constant-${index}`,
      name: constant.name,
      value: constant.value,
    })),
  };
}
