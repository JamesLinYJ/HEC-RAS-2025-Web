export type TimeStepping = 'Explicit' | 'Implicit';
export type NumericComputeField =
  | 'terrainElevation'
  | 'initialStage'
  | 'roughness'
  | 'precipitationRate'
  | 'timestepSeconds'
  | 'durationSeconds';

export interface RawComputeInputs {
  timeStepping: TimeStepping;
  terrainElevation: string;
  initialStage: string;
  roughness: string;
  precipitationRate: string;
  timestepSeconds: string;
  durationSeconds: string;
}

export interface ComputeRequest {
  equationSet: 'SWE';
  timeStepping: TimeStepping;
  terrainElevation: number;
  initialStage: number;
  roughness: number;
  precipitationRate: number;
  timestepSeconds: number;
  durationSeconds: number;
}

export interface ComputeResult {
  status: 'completed' | 'cancelled' | 'failed';
  error?: string;
  solverType: string;
  steps: number;
  finalTimeSeconds: number;
  wetCellCount: number;
  cellWaterSurface: number[];
  cellDepth: number[];
  faceVelocity: number[];
}

export interface ComputeProgress {
  progress: number;
  message: string;
  taskName?: string;
  values?: Record<string, string>;
}

export interface ComputeProgressSample extends ComputeProgress {
  sequence: number;
  timestamp: number;
}

export interface PlanComputeInspection {
  inspectionId: string;
  revision: string;
  projectRevision: string;
  projectXmlFingerprint: string;
  canExecute: boolean;
  requiresCUDA: boolean;
  reasons: string[];
  messages: unknown[];
  sideEffects: string[];
}

export interface PlanComputeExecutionResult {
  inspectionId: string;
  revision: string;
  state: 'completed' | 'cancelled' | 'failed';
  resultPath?: string | null;
  resultRevision?: string | null;
  summary?: {
    success: boolean;
    failureReason?: string | null;
    solver?: string | null;
    softwareVersion?: string | null;
    startTime?: string | null;
    endTime?: string | null;
    totalDurationSeconds?: number | null;
    solverInitDurationSeconds?: number | null;
    outputDurationSeconds?: number | null;
    globalRetryCount?: number | null;
    computeLog?: string | null;
  } | null;
  messages: unknown[];
  errorMessage?: string | null;
  finalDependencies: unknown[];
  sourceValues: Record<string, string>;
  resultCompute?: unknown;
}

export type ComputeIssueCode =
  | 'required'
  | 'number'
  | 'range'
  | 'unsupportedOption'
  | 'stageAboveTerrain'
  | 'durationAtLeastTimestep'
  | 'stepLimit';

export type ComputeValidationIssues = Partial<Record<NumericComputeField | 'timeStepping', ComputeIssueCode>>;

export const defaultComputeInputs: RawComputeInputs = {
  timeStepping: 'Explicit',
  terrainElevation: '42',
  initialStage: '43',
  roughness: '0.035',
  precipitationRate: '0.000001',
  timestepSeconds: '0.1',
  durationSeconds: '4',
};

const fieldRanges: Record<NumericComputeField, readonly [number, number]> = {
  terrainElevation: [-10000, 10000],
  initialStage: [-10000, 10000],
  roughness: [0.005, 0.2],
  precipitationRate: [0, 0.01],
  timestepSeconds: [0.01, 1],
  durationSeconds: [0, 60],
};

const decimalPattern = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/;

export function validateComputeInputs(raw: RawComputeInputs): {
  issues: ComputeValidationIssues;
  request?: ComputeRequest;
} {
  const issues: ComputeValidationIssues = {};
  const numbers: Partial<Record<NumericComputeField, number>> = {};
  const fields = Object.keys(fieldRanges) as NumericComputeField[];

  if (raw.timeStepping !== 'Explicit' && raw.timeStepping !== 'Implicit') {
    issues.timeStepping = 'unsupportedOption';
  }

  for (const field of fields) {
    const value = raw[field].trim();
    if (!value) {
      issues[field] = 'required';
      continue;
    }
    if (!decimalPattern.test(value)) {
      issues[field] = 'number';
      continue;
    }
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      issues[field] = 'number';
      continue;
    }
    const [minimum, maximum] = fieldRanges[field];
    if (parsed < minimum || parsed > maximum) {
      issues[field] = 'range';
      continue;
    }
    numbers[field] = parsed;
  }

  const terrain = numbers.terrainElevation;
  const stage = numbers.initialStage;
  const timestep = numbers.timestepSeconds;
  const duration = numbers.durationSeconds;
  if (terrain !== undefined && stage !== undefined && stage <= terrain) {
    issues.initialStage = 'stageAboveTerrain';
  }
  if (timestep !== undefined && duration !== undefined) {
    if (duration < timestep) issues.durationSeconds = 'durationAtLeastTimestep';
    else if (duration / timestep > 6000) issues.durationSeconds = 'stepLimit';
  }

  if (Object.keys(issues).length > 0) return { issues };

  return {
    issues,
    request: {
      equationSet: 'SWE',
      timeStepping: raw.timeStepping,
      terrainElevation: numbers.terrainElevation!,
      initialStage: numbers.initialStage!,
      roughness: numbers.roughness!,
      precipitationRate: numbers.precipitationRate!,
      timestepSeconds: numbers.timestepSeconds!,
      durationSeconds: numbers.durationSeconds!,
    },
  };
}

export function parseComputeProgress(raw: string): ComputeProgress | undefined {
  try {
    const decoded: unknown = JSON.parse(raw);
    if (!decoded || typeof decoded !== 'object') return undefined;
    const candidate = decoded as Record<string, unknown>;
    const progressValue = typeof candidate.fraction === 'number' && Number.isFinite(candidate.fraction)
      ? candidate.fraction * 100
      : candidate.progress;
    if (typeof progressValue !== 'number' || !Number.isFinite(progressValue) || progressValue < 0 || progressValue > 100) return undefined;
    const message = typeof candidate.message === 'string' ? candidate.message
      : typeof candidate.taskName === 'string' ? candidate.taskName : '';
    return {
      progress: progressValue,
      message,
      ...(typeof candidate.taskName === 'string' ? { taskName: candidate.taskName } : {}),
      ...(isStringRecord(candidate.values) ? { values: candidate.values } : {}),
    };
  } catch {
    return undefined;
  }
}

export function parsePlanComputeInspection(value: unknown): PlanComputeInspection | undefined {
  if (!isRecord(value) || typeof value.inspectionId !== 'string' || typeof value.revision !== 'string'
    || typeof value.projectRevision !== 'string' || typeof value.projectXmlFingerprint !== 'string'
    || typeof value.canExecute !== 'boolean' || typeof value.requiresCUDA !== 'boolean'
    || !Array.isArray(value.reasons) || !value.reasons.every((reason) => typeof reason === 'string')
    || !Array.isArray(value.messages) || !Array.isArray(value.sideEffects)
    || !value.sideEffects.every((effect) => typeof effect === 'string')) return undefined;
  return value as unknown as PlanComputeInspection;
}

export function parsePlanComputeExecutionResult(value: unknown): PlanComputeExecutionResult | undefined {
  if (!isRecord(value) || typeof value.inspectionId !== 'string' || typeof value.revision !== 'string'
    || !['completed', 'cancelled', 'failed'].includes(String(value.state))
    || !Array.isArray(value.messages) || !Array.isArray(value.finalDependencies)
    || !isStringRecord(value.sourceValues)) return undefined;
  if (value.summary !== undefined && value.summary !== null && !isRecord(value.summary)) return undefined;
  if (value.errorMessage !== undefined && value.errorMessage !== null && typeof value.errorMessage !== 'string') return undefined;
  return value as unknown as PlanComputeExecutionResult;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isStringRecord(value: unknown): value is Record<string, string> {
  return isRecord(value) && Object.values(value).every((item) => typeof item === 'string');
}

export function parseComputeResult(raw: string): ComputeResult | undefined {
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== 'object') return undefined;
    const candidate = value as Record<string, unknown>;
    const isFiniteNumber = (number: unknown): number is number => typeof number === 'number' && Number.isFinite(number);
    const isNumberArray = (array: unknown): array is number[] =>
      Array.isArray(array) && array.every(isFiniteNumber);
    if (!['completed', 'cancelled', 'failed'].includes(String(candidate.status))) return undefined;
    if (typeof candidate.solverType !== 'string') return undefined;
    if (!isFiniteNumber(candidate.steps) || !isFiniteNumber(candidate.finalTimeSeconds)) return undefined;
    if (!isFiniteNumber(candidate.wetCellCount) || !isNumberArray(candidate.cellWaterSurface)) return undefined;
    if (!isNumberArray(candidate.cellDepth) || !isNumberArray(candidate.faceVelocity)) return undefined;
    if (candidate.error !== undefined && typeof candidate.error !== 'string') return undefined;
    return {
      status: candidate.status as ComputeResult['status'],
      ...(typeof candidate.error === 'string' ? { error: candidate.error } : {}),
      solverType: candidate.solverType,
      steps: candidate.steps,
      finalTimeSeconds: candidate.finalTimeSeconds,
      wetCellCount: candidate.wetCellCount,
      cellWaterSurface: candidate.cellWaterSurface,
      cellDepth: candidate.cellDepth,
      faceVelocity: candidate.faceVelocity,
    };
  } catch {
    return undefined;
  }
}
