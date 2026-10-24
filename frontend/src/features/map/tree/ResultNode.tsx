import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, sourceFilename } from './model';
import { completionStatus } from './completionStatus';
import { bindTreeNode } from './bindTreeNode';

function presentResultNode({ node, language }: NodePresentationProps): NodePresentation {
  const status = completionStatus(node.resultComputeState, language);
  const stateLabel = node.resultComputeState === 'completed'
    ? localText(language, '计算结果已保存', 'Saved compute result')
    : node.resultComputeState === 'failed'
      ? localText(language, '计算记录失败', 'Compute record failed')
      : node.diagnosticStatus || null;
  return {
    icon: '◉', kind: localText(language, '结果', 'Result'), status,
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '状态', 'State')} value={stateLabel} />
    </DetailGroup>,
  };
}

export const ResultNode = bindTreeNode(presentResultNode, 'ResultNode');
