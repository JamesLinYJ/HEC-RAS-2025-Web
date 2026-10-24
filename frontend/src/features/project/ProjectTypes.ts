export type ProjectionMode = 'local' | 'epsg' | 'wkt' | 'prj';
export type ProjectUnitSystem = 'USCustomary' | 'SI';

export interface NewProjectDraft {
  projectTitle: string;
  description: string;
  createDirectory: string;
  unitSystem: ProjectUnitSystem;
  projectionMode: ProjectionMode;
  projectionText: string;
}

export interface RecentProjectRecord {
  id: string;
  projectPath: string;
  fileName: string;
  revision: string;
  openedAt: number;
}

export interface SelectedProjectFile {
  identity: string;
  fileName: string;
  byteLength: number;
  lastModified: number | null;
  digest?: string;
  xmlSource: string;
  isWellFormedXml: boolean;
}

export interface ProjectionEvaluation {
  isValid: boolean;
  name: string;
  units?: string | null;
  unitName?: string | null;
  epsgCode?: number | null;
  wkt?: string | null;
  error?: string | null;
  normalizedType: string;
  normalizedValue: string;
}

export interface ProjectionMatch {
  id: string;
  name: string;
  code: number;
  authority: string;
  area?: string | null;
}

export const RECENT_PROJECTS_STORAGE_KEY = 'hec-ras-web.project.recent.v1';
export const MAX_RECENT_PROJECTS = 12;

export function createInitialProjectDraft(): NewProjectDraft {
  return {
    projectTitle: '',
    description: '',
    createDirectory: '',
    unitSystem: 'USCustomary',
    projectionMode: 'epsg',
    projectionText: '',
  };
}

export function getProjectionRequest(draft: NewProjectDraft): { type: string; value: string } {
  if (draft.projectionMode === 'local') {
    return { type: 'Local', value: draft.unitSystem };
  }
  if (draft.projectionMode === 'epsg') {
    return { type: 'EPSG', value: draft.projectionText.trim() };
  }
  if (draft.projectionMode === 'prj') {
    // ProjectionService treats ESRI values as filesystem paths. The browser
    // supplies the actual PRJ contents, so validate those contents as WKT;
    // project.create still receives isPRJFile and resolves them through the
    // retained ESRI parser using a temporary file.
    return { type: 'WKT', value: draft.projectionText };
  }
  return {
    type: draft.projectionText.trimStart().startsWith('+proj') ? 'Proj' : 'WKT',
    value: draft.projectionText,
  };
}

export function isAbsoluteServerPath(value: string): boolean {
  return value.startsWith('/') || /^\\\\[^\\]+\\[^\\]+/.test(value) || /^[A-Za-z]:[\\/]/.test(value);
}
