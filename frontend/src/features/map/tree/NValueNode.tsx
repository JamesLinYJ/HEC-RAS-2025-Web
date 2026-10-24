import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentNValueNode({ node, language }: NodePresentationProps): NodePresentation {
  return {
    icon: '≈', kind: localText(language, '糙率', 'Manning’s n'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '说明', 'Description')} value={node.catalogSummary?.description || propertyValue(node, 'Description')} />
    </DetailGroup>,
  };
}

export const NValueNode = bindTreeNode(presentNValueNode, 'NValueNode');
