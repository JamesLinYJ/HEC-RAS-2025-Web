import { useEffect, useMemo, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryRow, GeometryTopologyFilterClause, GeometryTopologyFilterSnapshot, GeometryTopologySnapshot } from './geometryTypes';
import { TagFilterItemControl, type GeometryTagFilterDraft } from './TagFilterItemControl';

export interface TagFilterControlProps {
  language: Language;
  featureKind: 'arc' | 'region';
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canFilter: boolean;
  busy: boolean;
  onFilter?: (kind: 'arc' | 'region', clauses: GeometryTopologyFilterClause[], signal?: AbortSignal) => Promise<GeometryTopologyFilterSnapshot>;
  onSelectMatches?: (featureIDs: number[]) => void;
}

function key(): string {
  return globalThis.crypto?.randomUUID?.() ?? `filter-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function clausesForRequest(filters: GeometryTagFilterDraft[]): GeometryTopologyFilterClause[] {
  return filters.map((filter) => ({
    type: filter.type,
    tagDefinitionID: filter.type === 'Tag' ? filter.tagDefinitionID ?? null : null,
    propertyColumnID: filter.type === 'Property' ? filter.propertyColumnID ?? null : null,
    operator: filter.operator,
    value: filter.value ?? null,
  }));
}

/** Source-backed topology filter. Returned feature IDs remain tied to the captured topology draft. */
export function TagFilterControl({ language, featureKind, rows, snapshot, canFilter, busy, onFilter, onSelectMatches }: TagFilterControlProps) {
  const [filters, setFilters] = useState<GeometryTagFilterDraft[]>([]);
  const [propertyDescriptors, setPropertyDescriptors] = useState<GeometryTopologyFilterSnapshot['propertyDescriptors']>([]);
  const [result, setResult] = useState<GeometryTopologyFilterSnapshot>();
  const [error, setError] = useState('');
  const [filtering, setFiltering] = useState(false);
  const activeFilterRequest = useRef<AbortController | undefined>(undefined);
  const sourceRevision = snapshot?.revision ?? '';
  const sourceDraft = snapshot?.draftState ?? '';
  const sourceDraftIdentity = snapshot?.completion?.draftIdentity ?? '';
  const ownTagDefinitions = featureKind === 'arc' ? snapshot?.arcTags ?? [] : snapshot?.regionTags ?? [];
  const displayRows = useMemo(() => rows.filter((row) => row.fid != null && row.fid >= 0), [rows]);
  const matchedRows = result ? displayRows.filter((row) => result.matchedFeatureIDs.includes(row.fid!)) : [];

  useEffect(() => {
    activeFilterRequest.current?.abort();
    activeFilterRequest.current = undefined;
    setFilters([]);
    setPropertyDescriptors([]);
    setResult(undefined);
    setError('');
    setFiltering(false);
    return () => activeFilterRequest.current?.abort();
  }, [featureKind, sourceRevision, sourceDraft, sourceDraftIdentity]);

  const query = async (): Promise<void> => {
    if (!snapshot || !onFilter || !canFilter || busy || filtering) return;
    setError(''); setFiltering(true);
    const controller = new AbortController();
    activeFilterRequest.current = controller;
    try {
      const response = await onFilter(featureKind, clausesForRequest(filters), controller.signal);
      if (controller.signal.aborted) return;
      if (response.revision !== sourceRevision || response.featureKind !== featureKind) return;
      setPropertyDescriptors(response.propertyDescriptors);
      setResult(response);
    } catch (reason) {
      if (controller.signal.aborted || reason instanceof DOMException && reason.name === 'AbortError') return;
      setError(geometryDiagnosticMessage(reason));
    } finally {
      if (activeFilterRequest.current === controller) activeFilterRequest.current = undefined;
      if (!controller.signal.aborted) setFiltering(false);
    }
  };

  const addFilter = (): void => {
    setResult(undefined);
    setFilters((current) => [...current, {
      key: key(), type: 'Tag', tagDefinitionID: ownTagDefinitions[0]?.id ?? null,
      propertyColumnID: null, operator: 'IsNotBlank', value: null,
    }]);
  };

  const editFilter = (next: GeometryTagFilterDraft): void => { setResult(undefined); setFilters((current) => current.map((item) => item.key === next.key ? next : item)); };
  const removeFilter = (filterKey: string): void => { setResult(undefined); setFilters((current) => current.filter((item) => item.key !== filterKey)); };
  const clearFilters = (): void => { setFilters([]); setResult(undefined); setError(''); };

  return <section className="geometry-panel" aria-label={language === 'zh' ? '拓扑标签和属性筛选' : 'Topology tag and property filters'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '标签与属性筛选' : 'Tag and property filters'}</h3>
      <p>{language === 'zh' ? '使用当前拓扑快照筛选源要素；同一标签的多条件按任一匹配，其它条件逐项同时满足。' : 'Filter source features against the current topology snapshot. Multiple clauses for one tag are alternatives; other clauses must all match.'}</p>
    </div><div className="geometry-toolbar-group">
      <button type="button" className="geometry-secondary" disabled={!canFilter || busy || filtering || !snapshot || !onFilter} onClick={addFilter}>{language === 'zh' ? '添加条件' : 'Add filter'}</button>
      <button type="button" className="geometry-secondary" disabled={!canFilter || busy || filtering || !snapshot || !onFilter} onClick={() => void query()}>
        {filtering ? (language === 'zh' ? '筛选中…' : 'Filtering…') : (language === 'zh' ? '筛选源要素' : 'Filter source features')}
      </button>
      <button type="button" className="geometry-detail-clear" disabled={busy || filtering || filters.length === 0 && !result} onClick={clearFilters}>{language === 'zh' ? '清除条件' : 'Clear filters'}</button>
    </div></header>
    {error && <GeometryError language={language} error={error} style={{ margin: 12 }} />}
    {filters.map((filter) => <TagFilterItemControl key={filter.key} language={language} filterItem={filter}
      tagDefinitions={ownTagDefinitions} propertyDescriptors={propertyDescriptors} disabled={busy || filtering}
      onChange={editFilter} onRemove={removeFilter} />)}
    {!canFilter && <div className="geometry-empty"><strong>{language === 'zh' ? '当前环境暂不支持筛选。' : 'Filtering is not available in the current environment.'}</strong></div>}
    {result && <div className="geometry-table-footer">
      <span>{language === 'zh' ? '命中源要素' : 'Matched source features'}: {result.matchedFeatureIDs.length}</span>
      <button type="button" className="geometry-secondary" disabled={!matchedRows.length || !onSelectMatches}
        onClick={() => onSelectMatches?.(result.matchedFeatureIDs)}>{language === 'zh' ? '在表格中选中命中行' : 'Select matching table rows'}</button>
      {!matchedRows.length && <span>{language === 'zh' ? '当前显示表格中没有命中行。' : 'No matches are present in the currently displayed table.'}</span>}
    </div>}
    {!propertyDescriptors.length && <div className="geometry-inline-help">{language === 'zh'
      ? '可用属性字段会在首次筛选后加载；标签定义直接取自当前图形数据。'
      : 'Available property fields load after the first filter; tag definitions come from the current geometry data.'}</div>}
  </section>;
}

export default TagFilterControl;
