import type { Language } from '../../../shared/workbench';
import type {
  TerrainInputAppendResult,
  TerrainInputImportInspection,
  TerrainInputOperationResult,
} from '../coreImportWorkflow';

export interface TerrainUSGSMapPoint { x: number; y: number }
export interface TerrainUSGSExtent { minX: number; maxX: number; minY: number; maxY: number }
export interface TerrainUSGSProduct {
  id: string;
  title: string;
  publicationDate?: string | null;
  sizeInBytes?: number | null;
  resolution: string;
  cellSize?: number | null;
  metadataUrl?: string | null;
  boundingBox?: TerrainUSGSExtent | null;
  projectedBoundary?: TerrainUSGSMapPoint[] | null;
  downloadUrl?: string | null;
}
export interface TerrainUSGSQueryData {
  state: string;
  inputId?: string | null;
  extent: TerrainUSGSExtent;
  targetProjection: string;
  total: number;
  products: TerrainUSGSProduct[];
  diagnostic?: string | null;
}
export interface TerrainUSGSDownloadedFile {
  productId: string;
  title: string;
  filePath: string;
  sourceRevision: string;
  defaultScaleFactor: number;
  defaultRounding: string;
  downloadUrl?: string | null;
  metadataUrl?: string | null;
  previewFilePath?: string | null;
  previewSourceRevision?: string | null;
  inputId?: string | null;
}
export interface TerrainUSGSDownloadData {
  state: string;
  cleanupToken?: string | null;
  files: TerrainUSGSDownloadedFile[];
  assetPath: string;
  projectPath: string;
  sourceRevision: string;
  inputId?: string | null;
  extent: TerrainUSGSExtent;
  stitchTiles: boolean;
  diagnostic?: string | null;
}
export interface TerrainUSGSCleanupData { removed: boolean; warning?: string | null }
export interface TerrainUSGSImportStatus {
  state: string;
  result?: TerrainInputAppendResult | null;
  operations: TerrainInputOperationResult[];
  resultUnavailableCode?: string | null;
  diagnostic?: string | null;
}
export interface TerrainUSGSImportDraft {
  download: TerrainUSGSDownloadData;
  inspection: TerrainInputImportInspection;
  defaultSelections: Record<string, boolean>;
}
export type TerrainUSGSLanguage = Language;

export function validUSGSProduct(value: unknown): value is TerrainUSGSProduct {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const item = value as Partial<TerrainUSGSProduct>;
  return typeof item.id === 'string' && item.id.length > 0 &&
    typeof item.title === 'string' && typeof item.resolution === 'string';
}

export function validUSGSQuery(value: unknown): value is TerrainUSGSQueryData {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const result = value as Partial<TerrainUSGSQueryData>;
  return result.state === 'completed' && Number.isFinite(result.total) &&
    typeof result.targetProjection === 'string' && Array.isArray(result.products) &&
    result.products.every(validUSGSProduct) && validExtent(result.extent);
}

export function validExtent(value: unknown): value is TerrainUSGSExtent {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const extent = value as Partial<TerrainUSGSExtent>;
  return [extent.minX, extent.maxX, extent.minY, extent.maxY].every(Number.isFinite) &&
    extent.minX! < extent.maxX! && extent.minY! < extent.maxY!;
}

export function validUSGSDownload(value: unknown): value is TerrainUSGSDownloadData {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const result = value as Partial<TerrainUSGSDownloadData>;
  return result.state === 'completed' && typeof result.cleanupToken === 'string' &&
    result.cleanupToken.length > 0 && Array.isArray(result.files) && result.files.length > 0 &&
    typeof result.assetPath === 'string' && typeof result.projectPath === 'string' &&
    typeof result.sourceRevision === 'string' && typeof result.stitchTiles === 'boolean' && validExtent(result.extent) && result.files.every(file => file &&
      typeof file.productId === 'string' && typeof file.filePath === 'string' &&
      typeof file.sourceRevision === 'string' && typeof file.defaultRounding === 'string' && Number.isFinite(file.defaultScaleFactor));
}
