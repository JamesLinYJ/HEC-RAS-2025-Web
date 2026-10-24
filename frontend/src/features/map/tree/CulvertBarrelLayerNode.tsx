import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentCulvertBarrelLayerNode({ node, language }: NodePresentationProps): NodePresentation {
  return {
    icon: '▥', kind: localText(language, '涵洞管节', 'Culvert barrel'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '编号', 'Index')} value={propertyValue(node, 'Index')} />
      <InfoLine label={localText(language, '所属几何', 'Geometry')} value={node.locator?.parentGeometryNodeName} />
    </DetailGroup>,
  };
}

export const CulvertBarrelLayerNode = bindTreeNode(presentCulvertBarrelLayerNode, 'CulvertBarrelLayerNode');
