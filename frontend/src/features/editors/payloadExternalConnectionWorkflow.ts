import type { ModelDraft, ModelSnapshot } from './model';

type BoundaryProjection = NonNullable<ModelSnapshot['boundary']>;
type BoundaryLine = BoundaryProjection['lines'][number];

const externalConnectionChoiceField = 'NewPayloadPage/ViewModel_SelectedExternalConnection';
const sha256Pattern = /^[0-9a-f]{64}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function sameRevision(left: unknown, right: unknown): boolean {
  return typeof left === 'string' && typeof right === 'string' &&
    sha256Pattern.test(left) && sha256Pattern.test(right) && left.toUpperCase() === right.toUpperCase();
}

function requireEditableBoundary(snapshot: ModelSnapshot, description: string): BoundaryProjection {
  const boundary = snapshot.boundary;
  if (snapshot.canEdit !== true || !boundary || !Array.isArray(boundary.lines) || !isRecord(boundary.series)) {
    throw new Error(`${description}不是可编辑的边界条件快照。`);
  }
  return boundary;
}

function validateRefreshMetadata(snapshot: ModelSnapshot): void {
  if (!isRecord(snapshot.fieldGroups) || Object.values(snapshot.fieldGroups).some((fields) =>
    !Array.isArray(fields) || fields.some((field) => typeof field !== 'string'))) {
    throw new Error('服务端重放回执缺少有效的字段分组投影。');
  }
  if (snapshot.readOnlyFields != null && (!Array.isArray(snapshot.readOnlyFields) ||
      snapshot.readOnlyFields.some((field) => typeof field !== 'string'))) {
    throw new Error('服务端重放回执包含无效的只读字段清单。');
  }
}

function requireIdentity(value: unknown, description: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${description}缺少稳定身份。`);
  }
  return value;
}

function nullableIdentity(value: string | null | undefined): string | null {
  return value ?? null;
}

function assertSameLineAndPayloadStructure(
  current: BoundaryProjection,
  refreshed: BoundaryProjection,
): void {
  if (current.lines.length !== refreshed.lines.length) {
    throw new Error('工程连接重放改变了边界条件线数量；未采用新的载荷投影。');
  }

  const currentLineIDs = new Set<string>();
  const currentLineScopes = new Set<string>();
  const currentPayloadIDs = new Set<string>();
  const currentPayloadScopes = new Set<string>();
  for (let lineIndex = 0; lineIndex < current.lines.length; lineIndex += 1) {
    const line = current.lines[lineIndex];
    const nextLine = refreshed.lines[lineIndex];
    if (!line || !nextLine) throw new Error('边界条件线身份清单不完整。');

    const lineID = requireIdentity(line.id, `当前第 ${lineIndex + 1} 条边界线`);
    const lineScope = requireIdentity(line.scope, `当前第 ${lineIndex + 1} 条边界线`);
    if (currentLineIDs.has(lineID) || currentLineScopes.has(lineScope)) {
      throw new Error('当前边界草稿包含重复的线身份或作用域。');
    }
    currentLineIDs.add(lineID);
    currentLineScopes.add(lineScope);

    if (nextLine.id !== lineID || nextLine.scope !== lineScope || !Array.isArray(nextLine.payloads) ||
        !Array.isArray(line.payloads) || nextLine.payloads.length !== line.payloads.length) {
      throw new Error('工程连接重放改变了边界线身份、作用域或载荷数量；未采用新的载荷投影。');
    }

    const linePayloadIDs = new Set<string>();
    for (let payloadIndex = 0; payloadIndex < line.payloads.length; payloadIndex += 1) {
      const payload = line.payloads[payloadIndex];
      const nextPayload = nextLine.payloads[payloadIndex];
      if (!payload || !nextPayload) throw new Error('边界载荷身份清单不完整。');
      const payloadID = requireIdentity(payload.id, `当前边界线 ${lineID} 的第 ${payloadIndex + 1} 个载荷`);
      const payloadScope = requireIdentity(payload.scope, `当前边界线 ${lineID} 的第 ${payloadIndex + 1} 个载荷`);
      if (currentPayloadIDs.has(payloadID) || currentPayloadScopes.has(payloadScope)) {
        throw new Error('当前边界草稿包含重复的载荷身份或作用域。');
      }
      currentPayloadIDs.add(payloadID);
      currentPayloadScopes.add(payloadScope);
      linePayloadIDs.add(payloadID);
      if (nextPayload.id !== payloadID || nextPayload.scope !== payloadScope) {
        throw new Error('工程连接重放改变了载荷身份、作用域或顺序；未采用新的载荷投影。');
      }
    }
    const refreshedActivePayloadID = nextLine.activeHydraulicPayloadId;
    if (refreshedActivePayloadID != null && typeof refreshedActivePayloadID !== 'string') {
      throw new Error('服务端重放回执包含无效的活动水力载荷身份。');
    }
    if (typeof refreshedActivePayloadID === 'string' && refreshedActivePayloadID.length > 0 &&
        !linePayloadIDs.has(refreshedActivePayloadID)) {
      throw new Error('服务端重放回执的活动水力载荷不属于该边界线。');
    }
  }

  const selectedLineID = nullableIdentity(current.selectedLineId);
  const selectedPayloadID = nullableIdentity(current.selectedPayloadId);
  const selectedLine = current.lines.find(line => line.id === selectedLineID);
  if ((selectedLineID !== null && !selectedLine) ||
      (selectedPayloadID !== null && !selectedLine?.payloads.some(payload => payload.id === selectedPayloadID))) {
    throw new Error('当前边界草稿的选中项不属于已核对的边界线或载荷。');
  }
  // Read-only snapshots replay structural SourceState but do not carry the
  // transient selection set by MutatePayload. Null therefore means unspecified,
  // not a request to clear the local selection. Reject explicit substitutions.
  if ((refreshed.selectedLineId != null && refreshed.selectedLineId !== selectedLineID) ||
      (refreshed.selectedPayloadId != null && refreshed.selectedPayloadId !== selectedPayloadID)) {
    throw new Error('工程连接重放改变了边界线或载荷选择；未采用新的载荷投影。');
  }
}

function requireCreatedConnectionChoice(
  boundary: BoundaryProjection,
  lineID: string,
  connectionName: string,
): BoundaryLine {
  const matchingLines = boundary.lines.filter((line) => line.id === lineID);
  if (matchingLines.length !== 1) {
    throw new Error('新建载荷目标线在边界条件投影中缺失或身份不唯一。');
  }
  const line = matchingLines[0];
  if (!line?.newPayload || !isRecord(line.newPayload.choices)) {
    throw new Error('服务端没有为目标线返回新建载荷选项。');
  }
  const choices = line.newPayload.choices[externalConnectionChoiceField];
  if (!Array.isArray(choices)) {
    throw new Error('服务端没有返回外部连接选择项。');
  }
  const exactMatches = choices.filter((choice) => choice?.id === connectionName);
  if (exactMatches.length !== 1) {
    throw new Error('服务端的外部连接选项未唯一包含刚创建的连接名称。');
  }
  return line;
}

/**
 * Adopts only the boundary projection refreshed against the committed Project
 * context. Persisted model data and the user's complete local draft stay owned
 * by their existing baseline/draft objects.
 */
export function rebaseBoundaryExternalConnection(
  snapshot: ModelSnapshot,
  draft: ModelDraft,
  refreshed: ModelSnapshot,
  lineID: string,
  connectionName: string,
): { snapshot: ModelSnapshot; draft: ModelDraft; connectionName: string } {
  if (!sameRevision(refreshed.revision, snapshot.revision)) {
    throw new Error('工程连接重放返回了不同的边界源修订；请重新读取边界条件。');
  }
  if (typeof connectionName !== 'string' || connectionName.trim().length === 0) {
    throw new Error('工程服务没有返回有效的新建连接名称。');
  }
  if (typeof draft.sourceState !== 'string' || draft.sourceState.length === 0) {
    throw new Error('当前边界草稿缺少来源状态，无法安全重放外部连接选项。');
  }

  requireEditableBoundary(snapshot, '原始边界条件快照');
  const currentBoundary = requireEditableBoundary({ ...snapshot, boundary: draft.boundaryView ?? snapshot.boundary }, '当前边界草稿');
  const refreshedBoundary = requireEditableBoundary(refreshed, '服务端重放回执');
  validateRefreshMetadata(refreshed);

  assertSameLineAndPayloadStructure(currentBoundary, refreshedBoundary);
  requireCreatedConnectionChoice(refreshedBoundary, lineID, connectionName);

  const nextSnapshot: ModelSnapshot = {
    ...snapshot,
    ...(Object.prototype.hasOwnProperty.call(refreshed, 'coreLayerContext') && refreshed.coreLayerContext !== undefined
      ? { coreLayerContext: refreshed.coreLayerContext }
      : {}),
  };
  const nextDraft: ModelDraft = {
    ...draft,
    boundaryView: { ...refreshedBoundary,
      selectedLineId: currentBoundary.selectedLineId ?? null,
      selectedPayloadId: currentBoundary.selectedPayloadId ?? null },
    boundaryFieldGroups: Object.fromEntries(Object.entries(refreshed.fieldGroups)
      .map(([id, fields]) => [id, [...fields]])),
    boundaryReadOnlyFields: [...(refreshed.readOnlyFields ?? [])],
  };
  return { snapshot: nextSnapshot, draft: nextDraft, connectionName };
}
