import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { mapDocumentKind } from '../mapText';
import { bindTreeNode } from './bindTreeNode';

function presentGenericTreeNode({ node, language }: NodePresentationProps): NodePresentation {
  const mapType = node.locator?.mapType;
  return {
    icon: node.sourceClass === 'MapTreeRootNode' ? '⌖' : '◇',
    kind: mapType
      ? mapDocumentKind(language, mapType)
      : node.sourceClass === 'MapTreeRootNode'
        ? localText(language, '地图根节点', 'Map root') : node.sourceClass,
    detail: <DetailGroup>
      <InfoLine label={localText(language, '类型', 'Type')} value={mapType ? mapDocumentKind(language, mapType) : null} />
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, 'GDB 图层', 'GDB layer')} value={propertyValue(node, 'GdbLayerName')} />
    </DetailGroup>,
  };
}


export const GenericTreeNode = bindTreeNode(presentGenericTreeNode, 'GenericTreeNode');
