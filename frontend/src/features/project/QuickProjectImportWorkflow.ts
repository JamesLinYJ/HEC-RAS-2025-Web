import { isAbsoluteServerPath } from './ProjectTypes';

export type QuickProjectImportKind = 'archive' | 'result' | 'geometry' | 'boundaryCondition';

export interface WorkspaceFileInspection {
  sourcePath: string;
  canonicalPath: string;
  revision: string;
  length: number;
}

export interface WorkspacePreparedProject {
  sourcePath: string;
  sourceRevision: string;
  destinationDirectory: string;
  projectPath: string;
  projectName: string;
  importKind: QuickProjectImportKind;
  importedAssetPath: string | null;
  importedAssetRevision: string | null;
}

export function isSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value);
}

function normalizedPath(value: string): string {
  const slash = value.replace(/\\/g, '/');
  let prefix = '';
  let remainder = slash;
  const drive = /^([A-Za-z]:)\//.exec(slash);
  if (drive) {
    prefix = `${drive[1]}/`;
    remainder = slash.slice(drive[0].length);
  } else if (slash.startsWith('//')) {
    const parts = slash.slice(2).split('/');
    if (parts.length >= 2 && parts[0] && parts[1]) {
      prefix = `//${parts[0]}/${parts[1]}`;
      remainder = parts.slice(2).join('/');
    } else {
      prefix = '//';
      remainder = slash.slice(2);
    }
  } else if (slash.startsWith('/')) {
    prefix = '/';
    remainder = slash.slice(1);
  }
  const segments: string[] = [];
  for (const segment of remainder.split('/')) {
    if (!segment || segment === '.') continue;
    if (segment === '..') {
      if (segments.length > 0 && segments[segments.length - 1] !== '..') segments.pop();
      else if (!prefix) segments.push(segment);
      continue;
    }
    segments.push(segment);
  }
  const result = `${prefix}${prefix && prefix !== '/' && !prefix.endsWith('/') ? '/' : ''}${segments.join('/')}`;
  return result || prefix || '.';
}

function samePath(left: string, right: string): boolean {
  const normalizedLeft = normalizedPath(left);
  const normalizedRight = normalizedPath(right);
  const windowsPath = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windowsPath(normalizedLeft) && windowsPath(normalizedRight)
    ? normalizedLeft.toLocaleLowerCase('en-US') === normalizedRight.toLocaleLowerCase('en-US')
    : normalizedLeft === normalizedRight;
}

function pathParent(value: string): string {
  const normalized = normalizedPath(value);
  if (normalized.startsWith('//') && normalized.slice(2).split('/').length <= 2) return normalized;
  if (normalized === '/' || /^[A-Za-z]:\/$/.test(normalized)) return normalized;
  const index = normalized.lastIndexOf('/');
  if (index < 0) return '';
  if (index === 0) return '/';
  if (index === 2 && /^[A-Za-z]:\//.test(normalized)) return normalized.slice(0, 3);
  return normalized.slice(0, index);
}

function pathBaseName(value: string): string {
  return normalizedPath(value).split('/').pop() ?? '';
}

function isInsideDirectory(path: string, directory: string): boolean {
  const normalizedPathValue = normalizedPath(path);
  const normalizedDirectory = normalizedPath(directory);
  const comparisonPath = /^[A-Za-z]:\//.test(normalizedDirectory) || normalizedDirectory.startsWith('//')
    ? normalizedPathValue.toLocaleLowerCase('en-US')
    : normalizedPathValue;
  const comparisonDirectory = /^[A-Za-z]:\//.test(normalizedDirectory) || normalizedDirectory.startsWith('//')
    ? normalizedDirectory.toLocaleLowerCase('en-US')
    : normalizedDirectory;
  const separator = comparisonDirectory.endsWith('/') ? '' : '/';
  return comparisonPath.startsWith(`${comparisonDirectory}${separator}`);
}

export function isWorkspaceFileInspection(value: unknown, requestedPath: string): value is WorkspaceFileInspection {
  if (!value || typeof value !== 'object') return false;
  const receipt = value as Partial<WorkspaceFileInspection>;
  return typeof receipt.sourcePath === 'string' && isAbsoluteServerPath(receipt.sourcePath) &&
    samePath(receipt.sourcePath, requestedPath) &&
    typeof receipt.canonicalPath === 'string' && isAbsoluteServerPath(receipt.canonicalPath) &&
    isSHA256(receipt.revision) && Number.isSafeInteger(receipt.length) && (receipt.length ?? -1) >= 0;
}

export function isWorkspacePreparedProject(
  value: unknown,
  source: WorkspaceFileInspection,
  destinationDirectory: string,
  mode: 'archive' | 'coreHdf',
): value is WorkspacePreparedProject {
  if (!value || typeof value !== 'object') return false;
  const receipt = value as Partial<WorkspacePreparedProject>;
  if (typeof receipt.sourcePath !== 'string' || !samePath(receipt.sourcePath, source.sourcePath) ||
      !isSHA256(receipt.sourceRevision) || receipt.sourceRevision.toUpperCase() !== source.revision.toUpperCase() ||
      typeof receipt.destinationDirectory !== 'string' || !isAbsoluteServerPath(receipt.destinationDirectory) ||
      !samePath(pathParent(receipt.destinationDirectory), destinationDirectory) ||
      typeof receipt.projectPath !== 'string' || !isAbsoluteServerPath(receipt.projectPath) ||
      !receipt.projectPath.toLowerCase().endsWith('.ras') ||
      !isInsideDirectory(receipt.projectPath, receipt.destinationDirectory) ||
      typeof receipt.projectName !== 'string' || receipt.projectName.length === 0 ||
      !samePath(receipt.projectName, pathBaseName(receipt.projectPath).slice(0, -4))) return false;

  if (mode === 'archive') {
    return receipt.importKind === 'archive' && receipt.importedAssetPath === null && receipt.importedAssetRevision === null;
  }

  return (receipt.importKind === 'result' || receipt.importKind === 'geometry' || receipt.importKind === 'boundaryCondition') &&
    typeof receipt.importedAssetPath === 'string' && isAbsoluteServerPath(receipt.importedAssetPath) &&
    isInsideDirectory(receipt.importedAssetPath, receipt.destinationDirectory) &&
    samePath(pathBaseName(receipt.importedAssetPath), pathBaseName(source.sourcePath)) &&
    isSHA256(receipt.importedAssetRevision) &&
    receipt.importedAssetRevision.toUpperCase() === source.revision.toUpperCase();
}

export function quickProjectImportMode(sourcePath: string): 'archive' | 'coreHdf' | null {
  const normalized = sourcePath.trim().toLocaleLowerCase('en-US');
  if (normalized.endsWith('.zip')) return 'archive';
  if (normalized.endsWith('.h5')) return 'coreHdf';
  return null;
}

export function isQuickProjectImportAbsolutePath(value: string): boolean {
  return isAbsoluteServerPath(value.trim());
}
