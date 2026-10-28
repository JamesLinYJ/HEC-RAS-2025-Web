import type { BackendGateway, BackendTransportState } from '../../shared/workbench';
import type { ModelSnapshot } from './model';
import type { TerrainTopologyData } from './terrainTopologyTypes';
import type { ChannelV6ImportController } from './channelV6ImportController';

/** The parent supplies its entire current save request, including unsaved fields and rows. */
export type ChannelV6ImportModel = Record<string, unknown> & {
  assetPath: string;
  revision: string;
  sourceState?: string | null;
};

/** This capture belongs to one parent draft, input, source, session and backend instance. */
export interface ChannelV6ImportCapture {
  owner: string;
  draftKey: string;
  backend: BackendGateway;
  model: ChannelV6ImportModel;
  inputId: string;
  inputIds: string[];
  expectedDraftIdentity: string;
  existingDraftToken: string | null;
  inputName?: string;
}

export interface ChannelV6ImportFile {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface ChannelV6ImportInspection {
  capture: ChannelV6ImportCapture;
  /** Preserve the exact form text independently from the server's canonical path. */
  rawSourcePath: string;
  file: ChannelV6ImportFile;
}

export interface ChannelV6ImportCleanup {
  draftToken: string;
  payloadId: string;
  migratedSHA256: string;
}

export interface ChannelV6ImportCompleted {
  state: 'completed';
  snapshot: ModelSnapshot;
  topology: TerrainTopologyData;
  sourceRevision: string;
  cleanup: ChannelV6ImportCleanup;
  errorMessage?: string | null;
}

export interface ChannelV6ImportCancelled {
  state: 'cancelled';
  errorMessage?: string | null;
}

export interface ChannelV6ImportDiscarded {
  discarded: true;
  payloadId: string;
  draftDirectoryRemoved: boolean;
}

export type ChannelV6ImportPhase = 'running' | 'unknown' | 'cleaning' | 'cleanup-unknown'
  | 'cleanup-failed' | 'completed' | 'cancelled' | 'not-adopted' | 'failed';

export interface ChannelV6ImportProgress {
  fraction?: number;
  taskName?: string;
  message?: string;
}

export interface ChannelV6ImportTask {
  inspection: ChannelV6ImportInspection;
  phase: ChannelV6ImportPhase;
  httpOperationID: string | null;
  cleanupOperationID: string | null;
  cancelRequested: boolean;
  progress: ChannelV6ImportProgress | null;
  transport: BackendTransportState | null;
  receipt: ChannelV6ImportCompleted | null;
  error?: unknown;
  cleanupError?: unknown;
}

export interface ChannelV6ImportState {
  sourcePaths: Readonly<Record<string, string>>;
  inspection: ChannelV6ImportInspection | null;
  inspecting: boolean;
  task: ChannelV6ImportTask | null;
  validationError?: unknown;
}

export interface ChannelV6ImportBindings {
  /** Load the selected empty Channel topology, then capture the resulting exact parent draft. */
  capture(inputId: string, signal: AbortSignal): Promise<ChannelV6ImportCapture | null>;
  isCurrent(capture: ChannelV6ImportCapture): boolean;
  /** Synchronous lease: prevent parent save, navigation and conflicting edits until release. */
  acquire(capture: ChannelV6ImportCapture): boolean;
  /** Atomically adopt and return true; false/throw is allowed only before any payload ownership or draft changes. */
  accept(capture: ChannelV6ImportCapture, receipt: ChannelV6ImportCompleted): boolean;
  /** Called only after a confirmed terminal outcome, including exact payload cleanup. */
  release(capture: ChannelV6ImportCapture): void;
}

/** A stable parent-owned controller survives changes in the selected editor page. */
export interface ChannelV6ImportWorkflow {
  owner: string;
  canInspect: boolean;
  blockedMessage?: string;
  /** Task actions remain usable while the parent disables its ordinary editing fieldset. */
  taskHost?: HTMLDivElement | null;
  controller: ChannelV6ImportController;
}
