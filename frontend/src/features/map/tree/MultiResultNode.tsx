import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentMultiResultNode({ node, language }: NodePresentationProps): NodePresentation {
  const count = node.children?.length ?? 0;
  const plan = node.locator?.parentPlanNodeName || propertyValue(node, 'PlanName');
  return {
    icon: '◉', kind: localText(language, '多时刻结果', 'Multi-result'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '方案', 'Plan')} value={plan} />
      <InfoLine label={localText(language, '结果数', 'Results')} value={count ? String(count) : null} />
      <InfoLine label={localText(language, '来源', 'Source')} value={sourceFilename(node)} />
    </DetailGroup>,
  };
}

export const MultiResultNode = bindTreeNode(presentMultiResultNode, 'MultiResultNode');
