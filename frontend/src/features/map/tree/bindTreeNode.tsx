import type { FC } from 'react';
import type { NodePresentation, NodePresentationProps } from './types';
import { TreeNodeRow, type TreeNodeRowProps } from './TreeNodeRow';

export type TreeNodeComponentProps = Omit<TreeNodeRowProps, 'presentation'>;
type PresentationFactory = (props: NodePresentationProps) => NodePresentation;

export function bindTreeNode(factory: PresentationFactory, displayName: string) {
  const BoundTreeNode: FC<TreeNodeComponentProps> = (props) => {
    const { node, language } = props;
    return <TreeNodeRow {...props} presentation={factory({ node, language })} />;
  };
  BoundTreeNode.displayName = displayName;
  return BoundTreeNode;
}
