import type { MapResultFramesController, MapResultRenderSnapshot } from '../useMapResultFrames';

export interface ActiveResultProfile {
  ready: boolean;
  selectedProfileIndex: number | null;
  selectedProfileTime: string | null;
  renderSnapshot: MapResultRenderSnapshot | null;
  status: MapResultFramesController['status'];
}

/**
 * Existing identify, velocity and analysis APIs take the target group's own
 * slot-zero ordinal, not the shared keyframe ordinal. The verified rendered
 * slot-zero frame and its matching source status are the authority for it.
 */
export function activeResultProfile(controller: MapResultFramesController): ActiveResultProfile {
  const unchanged = { ready: true, selectedProfileIndex: controller.selectedProfileIndex,
    selectedProfileTime: controller.selectedProfileTime, renderSnapshot: controller.renderSnapshot, status: controller.status };
  if (!controller.keyframeNodeKey || controller.activeNodeKey === controller.keyframeNodeKey) return unchanged;
  const unavailable: ActiveResultProfile = { ready: false, selectedProfileIndex: null, selectedProfileTime: null,
    renderSnapshot: null, status: 'unavailable' };
  const node = controller.activeNode, snapshot = controller.renderSnapshot, catalog = controller.sources;
  const source = node?.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
  if (controller.status !== 'ready' || !node || !snapshot || !catalog || !source || source.isComputing ||
      snapshot.contextToken !== catalog.contextToken || !['Result', 'MultiResult'].includes(node.node.nodeType)) return unavailable;
  if (node.node.nodeType === 'MultiResult' && (!snapshot.sharedKeyframeAnchor ||
      snapshot.selectedProfileIndex !== controller.selectedProfileIndex || snapshot.selectedProfileTime !== controller.selectedProfileTime)) return unavailable;
  const frames = snapshot.layers.filter(layer => layer.selectionIndices?.includes(0) &&
    layer.resultPath === source.resultPath && layer.revision.toUpperCase() === source.revision.toUpperCase() &&
    layer.sourceID === snapshot.sourceID && layer.sourceMapID === catalog.mapID &&
    Number.isInteger(layer.profileIndex) && layer.profileIndex >= 0 && !!layer.profileName && !!layer.time);
  if (frames.length !== 1) return unavailable;
  const frame = frames[0];
  const statuses = snapshot.sources.filter(status => Array.isArray(status.selectionIndices) && status.selectionIndices.includes(0) &&
    status.resultPath === source.resultPath && typeof status.revision === 'string' &&
    status.revision.toUpperCase() === source.revision.toUpperCase() && status.available === true && status.profileIndex === frame.profileIndex);
  if (statuses.length !== 1) return unavailable;
  return { ready: true, selectedProfileIndex: frame.profileIndex, selectedProfileTime: frame.time, status: controller.status,
    renderSnapshot: { ...snapshot, selectedProfileIndex: frame.profileIndex, selectedProfileTime: frame.time } };
}
