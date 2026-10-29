import type { CatalogTreeNodeLocator } from '../mapTypes';
import type { MapResultActiveNode, MapResultRenderSnapshot } from '../useMapResultFrames';

export interface SharedKeyframeAnchor {
  node: CatalogTreeNodeLocator;
  selectionIndex: 0;
  resultPath: string;
  revision: string;
  runID: null;
  outputBlockName: null;
  profileCount: number;
  profileIndex: number;
  profileName: string;
  time: string;
  profiles: { index: number; name: string; time: string }[];
}

const canonical = (value: unknown): unknown => {
  if (Array.isArray(value)) return value.map(canonical);
  if (value === null || typeof value !== 'object') return value;
  const item = value as Record<string, unknown>;
  return Object.fromEntries(Object.keys(item).sort().map(key => [key, canonical(item[key])]));
};

export const resultNodeIdentity = (node: CatalogTreeNodeLocator) => JSON.stringify(canonical(node));

/** The source's full time list comes from a verified render response, never another node's ordinal. */
export function sharedKeyframeFromRender(node: MapResultActiveNode, snapshot: MapResultRenderSnapshot,
  profileIndex: number): SharedKeyframeAnchor {
  const source = node.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
  const profiles = snapshot.steps.map(step => ({ index: step.id, name: step.label, time: step.time }));
  const selected = profiles.find(profile => profile.index === profileIndex);
  if (node.nodeKind !== 'MultiResult' || node.node.nodeType !== 'MultiResult' || !source || source.isComputing ||
      !profiles.length || profiles.some((profile, index) => profile.index !== index || !profile.name || !profile.time) ||
      !selected || snapshot.selectedProfileIndex !== profileIndex || snapshot.selectedProfileTime !== selected.time) {
    throw new Error('共享时刻来源、完整时间序列或当前时刻未通过核对。');
  }
  return { node: node.node, selectionIndex: 0, resultPath: source.resultPath, revision: source.revision,
    runID: null, outputBlockName: null, profileCount: profiles.length, profileIndex,
    profileName: selected.name, time: selected.time, profiles };
}

/** Require the server to echo the precise shared source and time used for every target group. */
export function verifySharedKeyframe(anchor: SharedKeyframeAnchor, raw: unknown): void {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('渲染结果缺少共享时刻回执。');
  const value = raw as Record<string, unknown>;
  if (!value.node || resultNodeIdentity(value.node as CatalogTreeNodeLocator) !== resultNodeIdentity(anchor.node) ||
      value.selectionIndex !== 0 || value.resultPath !== anchor.resultPath ||
      typeof value.revision !== 'string' || value.revision.toUpperCase() !== anchor.revision.toUpperCase() ||
      value.runID != null || value.profileIndex !== anchor.profileIndex || value.profileName !== anchor.profileName ||
      value.time !== anchor.time) throw new Error('渲染结果的共享时刻与所选来源不一致。');
}
