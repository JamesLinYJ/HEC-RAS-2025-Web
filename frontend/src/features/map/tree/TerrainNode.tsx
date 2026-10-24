import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentTerrainNode({ node, language }: NodePresentationProps): NodePresentation {
  return {
    icon: '▧', kind: localText(language, '地形', 'Terrain'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '最近修改', 'Modified')} value={node.catalogSummary?.dateLastModified || propertyValue(node, 'DateLastModified')} />
    </DetailGroup>,
  };
}

export const TerrainNode = bindTreeNode(presentTerrainNode, 'TerrainNode');
