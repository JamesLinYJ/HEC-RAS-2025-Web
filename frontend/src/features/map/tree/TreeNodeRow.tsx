import { useEffect, useRef, type ReactNode } from 'react';
import { localizeProjectTreeSectionTitle, localizeProjectTreeSourceClass } from '../../project/ProjectTreeModeSectionTitles';
import type { CatalogTreeNode } from '../mapTypes';
import { DiagnosticRows } from './NodeDetails';
import type { MapTreeLanguage, MapTreeNodeData, NodePresentation } from './types';
import './mapTree.css';

const visibilityTypes = new Set([
  'Geometry', 'Result', 'MultiResult', 'Terrain', 'BoundaryCondition', 'WindLayer', 'PrecipitationLayer',
  'NValue', 'Classification', 'Feature', 'MapLayer', 'GdbDataset', 'GdbVectorLayer',
  'ConceptualMesh', 'FlowAreaLayer', 'CulvertBarrelLayer',
]);

type CheckState = 'checked' | 'unchecked' | 'mixed' | 'unavailable';

function checkState(value: string | null | undefined): CheckState {
  if (value === 'checked') return 'checked';
  if (value === 'unchecked') return 'unchecked';
  if (value === 'mixed' || value === 'indeterminate') return 'mixed';
  return 'unavailable';
}

function diagnosticBadge(value: string | null | undefined, language: MapTreeLanguage) {
  if (!value) return undefined;
  const labels: Record<string, readonly [string, string, 'success' | 'warning' | 'error']> = {
    loaded: ['已读取', 'Loaded', 'success'],
    sourceChanged: ['源文件已变化', 'Source changed', 'warning'],
    unavailable: ['不可用', 'Unavailable', 'warning'],
    readFailed: ['读取失败', 'Read failed', 'error'],
  };
  const item = labels[value];
  return {
    text: item ? item[language === 'zh' ? 0 : 1] : value,
    tone: item?.[2] ?? 'neutral' as const,
  };
}

export interface TreeNodeRowProps {
  node: MapTreeNodeData;
  presentation: NodePresentation;
  depth: number;
  hasChildren: boolean;
  expanded: boolean;
  selected: boolean;
  busy: boolean;
  language: MapTreeLanguage;
  onExpand: () => void;
  onSelect: (node: CatalogTreeNode) => void;
  onZoom: (node: CatalogTreeNode) => void;
  onToggle: (node: CatalogTreeNode, checked: boolean) => void;
  children?: ReactNode;
}

export function TreeNodeRow({
  node,
  presentation,
  depth,
  hasChildren,
  expanded,
  selected,
  busy,
  language,
  onExpand,
  onSelect,
  onZoom,
  onToggle,
  children,
}: TreeNodeRowProps) {
  const checkboxRef = useRef<HTMLInputElement | null>(null);
  const state = checkState(node.checked);
  const nodeType = node.locator?.nodeType ?? '';
  const checkableType = visibilityTypes.has(nodeType);
  const canToggle = checkableType && state !== 'unavailable';
  const localizedName = node.localizeName || nodeType === 'Root'
    ? localizeProjectTreeSectionTitle(language, node.name)
    : node.name;
  const kind = presentation.kind && presentation.kind !== node.sourceClass
    ? presentation.kind
    : localizeProjectTreeSourceClass(language, node.sourceClass);
  const locator = node.locator ? JSON.stringify(node.locator) : undefined;
  const status = presentation.status ?? diagnosticBadge(node.diagnosticStatus, language) ??
    (checkableType && state === 'unavailable'
      ? { text: language === 'zh' ? '不可用' : 'Unavailable', tone: 'warning' as const }
      : undefined);

  useEffect(() => {
    if (checkboxRef.current) checkboxRef.current.indeterminate = state === 'mixed';
  }, [state]);

  return (
    <>
      <li role="treeitem" aria-expanded={hasChildren ? expanded : undefined} aria-selected={selected}
        aria-checked={canToggle ? state === 'mixed' ? 'mixed' : state === 'checked' : undefined}
        data-node-check-state={state}>
        <div className={'map-tree-row' + (selected ? ' is-selected' : '')}
          data-node-id={node.id}
          data-node-root={node.locator?.rootName}
          data-node-type={nodeType || undefined}
          data-node-path={node.locator?.nodePath ? JSON.stringify(node.locator.nodePath) : undefined}
          data-node-locator={locator}
          data-node-asset-id={node.locator?.assetID ?? node.assetID ?? undefined}
          data-node-map-type={node.locator?.mapType ?? undefined}
          data-node-checked={node.checked ?? undefined}
          style={{ paddingInlineStart: 8 + depth * 15 }}>
          {hasChildren ? (
            <button type="button" className="map-tree-expand"
              aria-label={expanded ? (language === 'zh' ? '折叠' : 'Collapse') : (language === 'zh' ? '展开' : 'Expand')}
              aria-expanded={expanded} onClick={onExpand}>{expanded ? '⌄' : '›'}</button>
          ) : <span className="map-tree-leaf" aria-hidden="true" />}
          {canToggle && (
            <input
              ref={checkboxRef}
              type="checkbox"
              aria-label={(language === 'zh' ? '显示 ' : 'Show ') + node.name}
              aria-checked={state === 'mixed' ? 'mixed' : state === 'checked'}
              checked={state === 'checked'}
              disabled={busy}
              onChange={(event) => onToggle(node, event.currentTarget.checked)}
            />
          )}
          <button type="button" className="map-tree-name"
            onClick={() => onSelect(node)}
            onDoubleClick={() => onZoom(node)}
            title={node.name}>
            <span className="map-tree-node-icon" aria-hidden="true">{presentation.icon}</span>
            <span>{localizedName}</span>
          </button>
          {status && (
            <span className={`map-tree-status is-${status.tone ?? 'neutral'}`} role="status">
              {status.text}
            </span>
          )}
          <span className="map-tree-kind">{kind}</span>
        </div>
        {presentation.detail && <div className="map-tree-detail-row" style={{ marginInlineStart: 36 + depth * 15 }}>{presentation.detail}</div>}
        <DiagnosticRows node={node} language={language} depth={depth} />
        {children}
      </li>
    </>
  );
}
