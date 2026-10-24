import type { WorkbenchContext } from '../../shared/workbench';

export interface PlanExportRequest {
  projectPath: string;
  projectXml: string;
  planPath: string | null;
  outputPath: string | null;
  zip: boolean;
  clipRasters: boolean;
  resampleCellsize: number | null;
  externalConnections: 'ReferenceByAbsolutePath' | 'CopyToProject';
  replaceFile: boolean;
  revision: string | null;
  projectSessionID: string;
}

export interface PlanExportChoice { id: string; title: string }

export interface PlanExportInspection {
  revision: string;
  canExecute: boolean;
  validationMessage: string | null;
  plans: PlanExportChoice[];
  selectedPlanPath: string | null;
  suggestedOutputPath: string | null;
  sourceFiles: string[];
  requiredConnections: string[];
}

export interface PlanExportReceipt {
  outputPath: string;
  zip: boolean;
  projectFilename: string;
  fileCount: number;
  projectPath: string;
  planPath: string;
  projectSessionID: string;
  sourceRevision: string;
  coreLayerIdentityToken: string | null;
  outputSHA256: string | null;
  outputBytes: number;
  receiptID: string;
}

export interface PlanExportDuplexResult {
  state: 'completed' | 'cancelled';
  receipt: PlanExportReceipt | null;
}

export interface PlanExportForm {
  planPath: string;
  outputPath: string;
  zip: boolean;
  clipRasters: boolean;
  resampleCellsize: number | null;
  externalConnections: PlanExportRequest['externalConnections'];
  replaceFile: boolean;
}

export function createPlanExportRequest(context: WorkbenchContext, form: PlanExportForm,
  revision: string | null = null): PlanExportRequest {
  if (!context.projectPath || !context.projectXml) {
    throw new Error('Open a saved project before exporting a plan.');
  }
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    planPath: form.planPath.trim() || null,
    outputPath: form.outputPath.trim() || null,
    zip: form.zip,
    clipRasters: form.clipRasters,
    resampleCellsize: form.resampleCellsize,
    externalConnections: form.externalConnections,
    replaceFile: form.replaceFile,
    revision,
    projectSessionID: context.projectSessionID,
  };
}

/** Context identity is tracked separately; this fingerprint covers mutable export options. */
export function planExportRequestKey(request: PlanExportRequest): string {
  return JSON.stringify([
    request.planPath, request.outputPath, request.zip, request.clipRasters, request.resampleCellsize,
    request.externalConnections, request.replaceFile,
  ]);
}

export function planExportContextKey(context: WorkbenchContext): string {
  const asset = context.selectedAsset;
  return JSON.stringify([
    context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml,
    asset?.kind ?? null, asset?.path ?? null, asset?.revision ?? null,
  ]);
}

export function parsePlanExportInspection(value: unknown): PlanExportInspection {
  if (!isRecord(value) || typeof value.revision !== 'string' || typeof value.canExecute !== 'boolean'
    || !(value.validationMessage === null || typeof value.validationMessage === 'string')
    || !Array.isArray(value.plans) || !value.plans.every(isPlanChoice)
    || !(value.selectedPlanPath === null || typeof value.selectedPlanPath === 'string')
    || !(value.suggestedOutputPath === null || typeof value.suggestedOutputPath === 'string')
    || !isStringArray(value.sourceFiles) || !isStringArray(value.requiredConnections)) {
    throw new Error('The C# Host returned an invalid plan export inspection.');
  }
  return {
    revision: value.revision,
    canExecute: value.canExecute,
    validationMessage: value.validationMessage,
    plans: value.plans,
    selectedPlanPath: value.selectedPlanPath,
    suggestedOutputPath: value.suggestedOutputPath,
    sourceFiles: value.sourceFiles,
    requiredConnections: value.requiredConnections,
  };
}

export function parsePlanExportDuplexResult(value: unknown): PlanExportDuplexResult {
  if (!isRecord(value) || (value.state !== 'completed' && value.state !== 'cancelled')) {
    throw new Error('The C# Host returned an invalid plan export execution result.');
  }
  if (value.state === 'cancelled') {
    if (value.receipt !== null) throw new Error('The C# Host returned a receipt for a cancelled plan export.');
    return { state: 'cancelled', receipt: null };
  }
  if (!isPlanExportReceipt(value.receipt)) {
    throw new Error('The C# Host completed the plan export without a valid output receipt.');
  }
  return { state: 'completed', receipt: value.receipt };
}

function isPlanChoice(value: unknown): value is PlanExportChoice {
  return isRecord(value) && typeof value.id === 'string' && typeof value.title === 'string';
}

function isPlanExportReceipt(value: unknown): value is PlanExportReceipt {
  return isRecord(value)
    && typeof value.outputPath === 'string' && typeof value.zip === 'boolean'
    && typeof value.projectFilename === 'string' && Number.isInteger(value.fileCount)
    && typeof value.projectPath === 'string' && typeof value.planPath === 'string'
    && typeof value.projectSessionID === 'string' && typeof value.sourceRevision === 'string'
    && (value.coreLayerIdentityToken === null || typeof value.coreLayerIdentityToken === 'string')
    && (value.outputSHA256 === null || typeof value.outputSHA256 === 'string')
    && typeof value.outputBytes === 'number' && Number.isFinite(value.outputBytes)
    && typeof value.receiptID === 'string' && value.receiptID.length > 0;
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(item => typeof item === 'string');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
