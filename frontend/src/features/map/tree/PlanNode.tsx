import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentPlanNode({ node, language }: NodePresentationProps): NodePresentation {
  const description = node.catalogSummary?.description || propertyValue(node, 'Description');
  return {
    icon: '▤', kind: localText(language, '方案', 'Plan'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '最近修改', 'Modified')} value={node.catalogSummary?.dateLastModified || propertyValue(node, 'DateLastModified')} />
      <InfoLine label={localText(language, '说明', 'Description')} value={description} />
    </DetailGroup>,
  };
}

export const PlanNode = bindTreeNode(presentPlanNode, 'PlanNode');
