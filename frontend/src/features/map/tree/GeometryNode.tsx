import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentGeometryNode({ node, language }: NodePresentationProps): NodePresentation {
  const backups = node.children?.flatMap((child) => child.children ?? []).filter((child) => child.sourceClass === 'BackupNodeVM').length ?? 0;
  const description = node.catalogSummary?.description || propertyValue(node, 'Description');
  return {
    icon: '◇', kind: localText(language, '几何', 'Geometry'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
      <InfoLine label={localText(language, '备份', 'Backups')} value={backups ? String(backups) : null} />
      <InfoLine label={localText(language, '说明', 'Description')} value={description} />
    </DetailGroup>,
  };
}

export const GeometryNode = bindTreeNode(presentGeometryNode, 'GeometryNode');
