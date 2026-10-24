export interface ResultHoverFrameIdentity {
  sourceID: string;
  sourceMapID: string;
  resultPath: string;
  revision: string;
  mapType: string;
  profileIndex: number;
  selectionIndices: number[] | null;
}

export interface ResultHoverSourceSelection {
  selectionIndex: number;
  resultPath: string;
  resultRevision: string;
}

export interface ResultHoverFrameContext {
  sourceID: string;
  mapID: string;
  mapType: string;
  nodeType: string;
  timelineProfileIndex: number;
}

function sameRevision(left: string, right: string): boolean {
  return left.toUpperCase() === right.toUpperCase();
}

function sameMapID(left: string, right: string): boolean {
  return left.replace(/[{}]/g, '').toUpperCase() === right.replace(/[{}]/g, '').toUpperCase();
}

function frameMatchesSelectedSource(
  frame: ResultHoverFrameIdentity,
  selections: readonly ResultHoverSourceSelection[],
): boolean {
  const matches = (selection: ResultHoverSourceSelection | undefined) => selection !== undefined &&
    selection.resultPath === frame.resultPath && sameRevision(selection.resultRevision, frame.revision);
  if (frame.selectionIndices !== null) {
    return frame.selectionIndices.length > 0 && frame.selectionIndices.every((selectionIndex) =>
      matches(selections.find((selection) => selection.selectionIndex === selectionIndex)));
  }
  return selections.some(matches);
}

/**
 * Verifies that every Result image in a ready render snapshot belongs to the
 * current node, map and selected source. MultiResult's selected profile is the
 * slot-zero time index; other slots may render a different native profile
 * index after timestamp matching.
 */
export function resultHoverFramesMatchContext(
  frames: readonly ResultHoverFrameIdentity[],
  selections: readonly ResultHoverSourceSelection[],
  context: ResultHoverFrameContext,
): boolean {
  if (frames.length === 0 || selections.length === 0 ||
      !Number.isInteger(context.timelineProfileIndex) || context.timelineProfileIndex < 0 ||
      (context.nodeType !== 'Result' && context.nodeType !== 'MultiResult')) return false;

  return frames.every((frame) => {
    if (frame.sourceID !== context.sourceID || !sameMapID(frame.sourceMapID, context.mapID) ||
        frame.mapType !== context.mapType || !Number.isInteger(frame.profileIndex) || frame.profileIndex < 0 ||
        !frameMatchesSelectedSource(frame, selections)) return false;

    if (context.nodeType === 'Result') return frame.profileIndex === context.timelineProfileIndex;

    const includesTimelineAnchor = frame.selectionIndices?.includes(0) ?? selections.some((selection) =>
      selection.selectionIndex === 0 && selection.resultPath === frame.resultPath &&
      sameRevision(selection.resultRevision, frame.revision));
    return !includesTimelineAnchor || frame.profileIndex === context.timelineProfileIndex;
  });
}

/** Accepts only a value produced by a source frame that is currently drawn. */
export function resultHoverEntryMatchesCurrentFrame(
  frames: readonly ResultHoverFrameIdentity[],
  selections: readonly ResultHoverSourceSelection[],
  context: ResultHoverFrameContext,
  entry: { resultPath: string; resultRevision: string; mapType: string; profileIndex: number },
): boolean {
  return resultHoverFramesMatchContext(frames, selections, context) && frames.some((frame) =>
    frame.sourceID === context.sourceID && sameMapID(frame.sourceMapID, context.mapID) &&
    frame.resultPath === entry.resultPath && sameRevision(frame.revision, entry.resultRevision) &&
    frame.mapType === entry.mapType && frame.profileIndex === entry.profileIndex &&
    frameMatchesSelectedSource(frame, selections));
}
