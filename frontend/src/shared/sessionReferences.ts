import type { WorkbenchContext } from './workbench';

export function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
export function sessionReferenceIdentity(context: WorkbenchContext): string | null {
  const core = context.coreLayerContext;
  return typeof core?.identityToken === 'string' ? core.identityToken : null;
}
export function sessionReferenceSourceIdentity(context: WorkbenchContext): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision,
    context.projectXml, context.selectedAsset?.path, context.selectedAsset?.kind,
    context.selectedAsset?.revision, sessionReferenceIdentity(context)]);
}
export function sameSessionProject(left: WorkbenchContext, right: WorkbenchContext): boolean {
  return left.projectSessionID === right.projectSessionID && left.projectPath === right.projectPath;
}
export function validateSessionReferenceContext(value: unknown, context: WorkbenchContext): asserts value is Record<string, unknown> {
  if (!record(value) || value.projectSessionID !== context.projectSessionID || value.projectPath !== context.projectPath ||
      typeof value.projectRevision !== 'string' || value.projectRevision.toUpperCase() !== context.projectRevision?.toUpperCase() ||
      typeof value.identityToken !== 'string' || !/^[\da-f]{64}$/i.test(value.identityToken) ||
      !Array.isArray(value.references) || !Array.isArray(value.sessionHdfReferences))
    throw new Error('外部来源回执与当前工程会话或修订不一致。');
  const ids = new Set<string>();
  for (const [references, kinds] of [[value.references, ['geometry', 'boundaryCondition']],
    [value.sessionHdfReferences, ['plan', 'result', 'terrain']]] as const) {
    for (const capture of references) {
      if (!record(capture) || typeof capture.referenceID !== 'string' || !capture.referenceID || ids.has(capture.referenceID.toLowerCase()) ||
          capture.projectSessionID !== context.projectSessionID || typeof capture.projectPath !== 'string' ||
          typeof capture.kind !== 'string' || !(kinds as readonly string[]).includes(capture.kind) ||
          typeof capture.sourcePath !== 'string' || !capture.sourcePath || typeof capture.canonicalSourcePath !== 'string' ||
          !capture.canonicalSourcePath || typeof capture.sourceRevision !== 'string' || !/^[\da-f]{64}$/i.test(capture.sourceRevision) ||
          typeof capture.name !== 'string' || capture.sessionOnly !== true)
        throw new Error('外部来源回执包含无效或重复的捕获身份。');
      ids.add(capture.referenceID.toLowerCase());
    }
  }
}

/** Every selected asset receives the same immutable, verified session capability. */
export function withSessionReferenceContext(context: WorkbenchContext, core: Record<string, unknown> | null | undefined): WorkbenchContext {
  if (core) validateSessionReferenceContext(core, context);
  const asset = context.selectedAsset;
  if (!asset) return { ...context, coreLayerContext: core ?? null };
  const locator = { ...(record(asset.locator) ? asset.locator : {}) };
  delete locator.CoreLayerContext;
  if (core) locator.coreLayerContext = core;
  else delete locator.coreLayerContext;
  return { ...context, coreLayerContext: core ?? null, selectedAsset: { ...asset, locator } };
}

/** Navigation preserves the registry; an acknowledged save may replace it with a rebased capability. */
export function mergeSessionReferenceContext(next: WorkbenchContext, current: WorkbenchContext, acknowledged = false): WorkbenchContext {
  if (!sameSessionProject(next, current)) return withSessionReferenceContext(next, next.coreLayerContext);
  const locator = record(next.selectedAsset?.locator) ? next.selectedAsset.locator : null;
  const locatorCore = record(locator?.coreLayerContext) ? locator.coreLayerContext : null;
  const acknowledgedCore = acknowledged
    ? (locatorCore && locatorCore.identityToken !== current.coreLayerContext?.identityToken
      ? locatorCore : next.coreLayerContext)
    : undefined;
  if (acknowledged && acknowledgedCore === null && current.coreLayerContext)
    throw new Error('保存回执不能清除现有外部来源身份，请核对后端返回的会话捕获。');
  const core = acknowledgedCore ?? current.coreLayerContext;
  if (core) {
    validateSessionReferenceContext(core, next);
    const captures = [...(core.references as unknown[]), ...(core.sessionHdfReferences as unknown[])];
    const selected = next.selectedAsset;
    if (acknowledged && selected?.revision && captures.some(capture => record(capture) &&
        (capture.sourcePath === selected.path || capture.canonicalSourcePath === selected.path) &&
        typeof capture.sourceRevision === 'string' && capture.sourceRevision.toUpperCase() !== selected.revision?.toUpperCase()))
      throw new Error('保存后的外部来源修订缺少已确认的新会话捕获，请重新读取保存回执。');
  }
  return withSessionReferenceContext(next, core);
}
