import type { CatalogTreeNode } from '../mapTypes';
import type { MapTreeNodeData, MapTreeNodeKind } from './types';

const mapLayerKind: Record<string, MapTreeNodeKind> = {
  PointsVector: 'pointLayer',
  PolylinesVector: 'polylineLayer',
  PolygonsVector: 'polygonLayer',
};

export function asMapTreeNode(node: CatalogTreeNode): MapTreeNodeData {
  return node as MapTreeNodeData;
}

export function mapTreeNodeKind(node: MapTreeNodeData): MapTreeNodeKind {
  const type = node.locator?.nodeType;
  if (type === 'MapLayer') return mapLayerKind[node.locator?.mapType ?? ''] ?? 'generic';
  if (type === 'Folder' || node.sourceClass === 'FolderNode' || node.sourceClass === 'FolderVM') return 'folder';
  switch (type) {
    case 'Geometry': return 'geometry';
    case 'Plan': return 'plan';
    case 'Result': return 'result';
    case 'MultiResult': return 'multiResult';
    case 'Terrain': return 'terrain';
    case 'NValue': return 'nValue';
    case 'Classification': return 'classification';
    case 'BoundaryCondition': return 'boundaryCondition';
    case 'TerrainInput': return 'terrainInput';
    case 'FlowAreaLayer': return 'flowArea';
    case 'CulvertBarrelLayer': return 'culvertBarrel';
    case 'GdbDataset':
    case 'GdbVectorLayer': return 'generic';
  }
  if (node.sourceClass === 'ObservableGeometry') return 'geometry';
  if (node.sourceClass === 'ObservablePlan') return 'plan';
  if (node.sourceClass === 'ObservableResult') return 'result';
  if (node.sourceClass === 'MultiResultNodeVM') return 'multiResult';
  if (node.sourceClass === 'ObservableTerrain') return 'terrain';
  if (node.sourceClass === 'ObservableNValueLayer') return 'nValue';
  if (node.sourceClass === 'ObservableClassificationLayer') return 'classification';
  if (node.sourceClass === 'ObservableBoundaryCondition') return 'boundaryCondition';
  if (node.sourceClass === 'FlowAreaLayerVisualContext') return 'flowArea';
  if (node.sourceClass === 'CulvertBarrelLayerVisualContext') return 'culvertBarrel';
  if (node.sourceClass === 'PointMapLayerVisualContext') return 'pointLayer';
  if (node.sourceClass === 'PolylineMapLayerVisualContext') return 'polylineLayer';
  if (node.sourceClass === 'PolygonMapLayerVisualContext') return 'polygonLayer';
  if (node.sourceClass.endsWith('TerrainInputVisualContext') ||
      node.sourceClass === 'ChannelModTerrainInputParentVC' ||
      node.sourceClass === 'LeveeModTerrainInputVC') return 'terrainInput';
  return 'generic';
}

export function propertyValue(node: MapTreeNodeData, ...keys: string[]): string | null {
  const wanted = new Set(keys.map((key) => key.toLowerCase()));
  const property = node.properties?.find((candidate) => {
    const idParts = candidate.id.split('/');
    const shortID = idParts[idParts.length - 1]?.toLowerCase() ?? '';
    return wanted.has(candidate.label.toLowerCase()) || wanted.has(candidate.id.toLowerCase()) || wanted.has(shortID);
  });
  return property?.value || null;
}

export function sourceFilename(node: MapTreeNodeData): string | null {
  return node.locator?.filenameRelative || node.locator?.filename || node.assetID ||
    node.catalogSummary?.filename || propertyValue(node, 'Filename', 'FileName');
}

export function localText(language: 'zh' | 'en', zh: string, en: string): string {
  return language === 'zh' ? zh : en;
}
