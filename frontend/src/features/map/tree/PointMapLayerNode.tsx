import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { mapDocumentKind } from '../mapText';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentPointMapLayerNode({ node, language }: NodePresentationProps): NodePresentation {
  return {
    icon: '•', kind: localText(language, '点图层', 'Point layer'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '要素类型', 'Feature type')} value={node.locator?.mapType ? mapDocumentKind(language, node.locator.mapType) : null} />
      <InfoLine label={localText(language, '图层', 'Layer')} value={propertyValue(node, 'GdbLayerName')} />
    </DetailGroup>,
  };
}

export const PointMapLayerNode = bindTreeNode(presentPointMapLayerNode, 'PointMapLayerNode');
