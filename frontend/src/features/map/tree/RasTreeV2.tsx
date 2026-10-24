import type { CatalogTreeNode } from '../mapTypes';
import { MapTreeNode } from './MapTreeNode';
import type { MapTreeLanguage } from './types';

export interface RasTreeV2Props {
  roots: CatalogTreeNode[];
  label: string;
  selectedID: string | null;
  busyNodeID: string | null;
  language: MapTreeLanguage;
  onSelect: (node: CatalogTreeNode) => void;
  onZoom: (node: CatalogTreeNode) => void;
  onToggle: (node: CatalogTreeNode, checked: boolean) => void;
}

/** Recursive map tree host corresponding to RasTreeV2's expanded source tree. */
export function RasTreeV2({
  roots,
  label,
  selectedID,
  busyNodeID,
  language,
  onSelect,
  onZoom,
  onToggle,
}: RasTreeV2Props) {
  return (
    <ul className="map-backend-tree" role="tree" aria-label={label}>
      {roots.map((node) => (
        <MapTreeNode
          key={node.id}
          node={node}
          depth={0}
          selectedID={selectedID}
          busyNodeID={busyNodeID}
          language={language}
          onSelect={onSelect}
          onZoom={onZoom}
          onToggle={onToggle}
        />
      ))}
    </ul>
  );
}
