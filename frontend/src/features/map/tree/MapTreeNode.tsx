import { useState, type ComponentType } from 'react';
import type { CatalogTreeNode } from '../mapTypes';
import { asMapTreeNode, mapTreeNodeKind } from './model';
import { BoundaryConditionNode } from './BoundaryConditionNode';
import { ClassificationNode } from './ClassificationNode';
import { CulvertBarrelLayerNode } from './CulvertBarrelLayerNode';
import { FlowAreaNode } from './FlowAreaNode';
import { FolderNode } from './FolderNode';
import { GenericTreeNode } from './GenericTreeNode';
import { GeometryNode } from './GeometryNode';
import { MultiResultNode } from './MultiResultNode';
import { NValueNode } from './NValueNode';
import { PlanNode } from './PlanNode';
import { PointMapLayerNode } from './PointMapLayerNode';
import { PolygonMapLayerNode } from './PolygonMapLayerNode';
import { PolylineMapLayerNode } from './PolylineMapLayerNode';
import { ResultNode } from './ResultNode';
import { TerrainInputNode } from './TerrainInputNode';
import { TerrainNode } from './TerrainNode';
import type { MapTreeLanguage, MapTreeNodeKind } from './types';
import type { TreeNodeRowProps } from './TreeNodeRow';

type NodeComponentProps = Omit<TreeNodeRowProps, 'presentation'>;

const nodeComponents: Record<MapTreeNodeKind, ComponentType<NodeComponentProps>> = {
  folder: FolderNode,
  geometry: GeometryNode,
  plan: PlanNode,
  result: ResultNode,
  multiResult: MultiResultNode,
  terrain: TerrainNode,
  nValue: NValueNode,
  classification: ClassificationNode,
  boundaryCondition: BoundaryConditionNode,
  terrainInput: TerrainInputNode,
  flowArea: FlowAreaNode,
  culvertBarrel: CulvertBarrelLayerNode,
  pointLayer: PointMapLayerNode,
  polylineLayer: PolylineMapLayerNode,
  polygonLayer: PolygonMapLayerNode,
  generic: GenericTreeNode,
};

export interface MapTreeNodeProps {
  node: CatalogTreeNode;
  depth: number;
  selectedID: string | null;
  busyNodeID: string | null;
  language: MapTreeLanguage;
  onSelect: (node: CatalogTreeNode) => void;
  onZoom: (node: CatalogTreeNode) => void;
  onToggle: (node: CatalogTreeNode, checked: boolean) => void;
}

export function MapTreeNode({
  node: catalogNode,
  depth,
  selectedID,
  busyNodeID,
  language,
  onSelect,
  onZoom,
  onToggle,
}: MapTreeNodeProps) {
  const node = asMapTreeNode(catalogNode);
  const hasChildren = Boolean(node.children?.length);
  const [expanded, setExpanded] = useState(depth < 1);
  const NodeComponent = nodeComponents[mapTreeNodeKind(node)];

  return (
    <NodeComponent
      node={node}
      depth={depth}
      hasChildren={hasChildren}
      expanded={expanded}
      selected={selectedID === node.id}
      busy={busyNodeID === node.id}
      language={language}
      onExpand={() => setExpanded((value) => !value)}
      onSelect={onSelect}
      onZoom={onZoom}
      onToggle={onToggle}
    >
      {hasChildren && expanded && (
        <ul role="group">
          {node.children?.map((child) => (
            <MapTreeNode
              key={child.id}
              node={child}
              depth={depth + 1}
              selectedID={selectedID}
              busyNodeID={busyNodeID}
              language={language}
              onSelect={onSelect}
              onZoom={onZoom}
              onToggle={onToggle}
            />
          ))}
        </ul>
      )}
    </NodeComponent>
  );
}
