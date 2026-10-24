import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentClassificationNode({ node, language }: NodePresentationProps): NodePresentation {
  const opacity = node.classificationEditModeOpacity;
  return {
    icon: '▦', kind: localText(language, '分类图层', 'Classification'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '编辑透明度', 'Edit opacity')} value={opacity == null ? null : `${opacity}%`} />
    </DetailGroup>,
  };
}

export const ClassificationNode = bindTreeNode(presentClassificationNode, 'ClassificationNode');
