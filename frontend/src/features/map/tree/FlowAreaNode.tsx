import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { completionStatus } from './completionStatus';
import { bindTreeNode } from './bindTreeNode';

function presentFlowAreaNode({ node, language }: NodePresentationProps): NodePresentation {
  const status = completionStatus(node.resultComputeState, language);
  return {
    icon: '▱', kind: localText(language, '漫滩区域', 'Flow area'), status,
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '编号', 'Index')} value={propertyValue(node, 'Index')} />
    </DetailGroup>,
  };
}

export const FlowAreaNode = bindTreeNode(presentFlowAreaNode, 'FlowAreaNode');
