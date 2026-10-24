import type { CatalogTreeNode } from './CatalogTreeSnapshot';

export type ProjectTreeModelKind = 'Geometry' | 'Plan';

export interface ProjectTreeModelActionIdentity {
  kind: ProjectTreeModelKind;
  rootSourceClass: 'GeometriesSectionContext' | 'PlansSectionContext';
  sourceClass: 'ObservableGeometry' | 'ObservablePlan';
}

export interface ProjectTreeModelActionRequest extends ProjectTreeModelActionIdentity {
  projectPath: string;
  projectXml: string;
  projectXmlSHA: string;
  projectRevision: string;
  projectSessionID: string;
  action: 'duplicate';
  nodeID: string;
  nodeName: string;
  assetPath: string;
}

export interface ProjectTreeModelActionInspection extends ProjectTreeModelActionRequest {
  assetRevision: string;
  availableActions: string[];
}

export interface ProjectTreeModelActionPerformReceipt {
  success: boolean;
  assetPublished: boolean;
  projectMetadataCommitted: boolean;
  projectPath: string;
  projectSessionID: string;
  projectRevision: string;
  projectXml: string;
  projectXmlSHA: string;
  action: 'duplicate';
  kind: ProjectTreeModelKind;
  originalAssetPath: string;
  originalAssetSHA: string;
  newAssetPath: string;
  newAssetSHA: string;
  newName: string;
  warning?: string | null;
}

export function projectTreeModelActionIdentity(node: CatalogTreeNode): ProjectTreeModelActionIdentity | null {
  if (!node.assetID) return null;
  if (node.sourceClass === 'ObservableGeometry') {
    return { kind: 'Geometry', rootSourceClass: 'GeometriesSectionContext', sourceClass: 'ObservableGeometry' };
  }
  if (node.sourceClass === 'ObservablePlan') {
    return { kind: 'Plan', rootSourceClass: 'PlansSectionContext', sourceClass: 'ObservablePlan' };
  }
  return null;
}

export async function projectTreeXmlSHA(projectXml: string): Promise<string> {
  if (!globalThis.crypto?.subtle) throw new Error('当前环境无法计算工程 XML 校验值。');
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(projectXml));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
}

export function isProjectTreeSHA256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}
