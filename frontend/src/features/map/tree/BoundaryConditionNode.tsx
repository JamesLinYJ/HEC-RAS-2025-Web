import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentBoundaryConditionNode({ node, language }: NodePresentationProps): NodePresentation {
  return {
    icon: '⇥', kind: localText(language, '边界条件', 'Boundary condition'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '说明', 'Description')} value={node.catalogSummary?.description || propertyValue(node, 'Description')} />
    </DetailGroup>,
  };
}

export const BoundaryConditionNode = bindTreeNode(presentBoundaryConditionNode, 'BoundaryConditionNode');
