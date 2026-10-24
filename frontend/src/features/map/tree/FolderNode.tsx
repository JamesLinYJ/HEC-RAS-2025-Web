import type { NodePresentation, NodePresentationProps } from './types';
import { localText } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentFolderNode({ language }: NodePresentationProps): NodePresentation {
  return {
    icon: '▱',
    kind: localText(language, '文件夹', 'Folder'),
  };
}

export const FolderNode = bindTreeNode(presentFolderNode, 'FolderNode');
