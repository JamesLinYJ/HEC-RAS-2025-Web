import { useEffect, useRef, useState } from 'react';
import { text } from '../../../shared/workbench';
import type { Language } from '../../../shared/workbench';
import type { FeatureQueryControlProps } from './featureQueryTypes';
import { createOsmClause } from './OsmClauseViewModel';
import type { OsmClauseViewModel } from './OsmClauseViewModel';
import { buildOsmTemplateQl, osmBuilderFromPreset, OSM_PRESETS } from './OsmQueryBuilderViewModel';
import type { OsmBuilderError, OsmQueryBuilderViewModel } from './OsmQueryBuilderViewModel';
import { OsmSuggestField } from './OsmSuggestField';
import { createOsmTagFilter } from './OsmTagFilterViewModel';
import type { OsmTagFilterViewModel } from './OsmTagFilterViewModel';
import { osmOperatorRequiresValue, OSM_TAG_OPERATORS } from './OsmTagOperatorOption';
import type { OsmTagOperator } from './OsmTagOperatorOption';

function builderErrorMessage(error: OsmBuilderError, language: Language): string {
  switch (error.code) {
    case 'noClause': return text(language, '请至少添加一个查询条件。', 'Add at least one search clause before querying.');
    case 'noKinds': return text(language, '查询条件须至少选择一种元素类型：节点、路径或关系。', 'A search clause has no element types selected (node, way, or relation).');
    case 'missingValue': return text(language, `标签“${error.key ?? ''}”的比较方式需要填写值。`, `Tag filter "${error.key ?? ''}" needs a value for its comparison.`);
    case 'noValues': return text(language, `标签“${error.key ?? ''}”没有列出任何值。`, `Tag filter "${error.key ?? ''}" lists no values.`);
  }
}

/** OSM-specific form; query, preview and save are owned by FeatureQueryDialog. */
export function OSMQuerierControl({ language, disabled, value, onChange, backend, backendRevision }: FeatureQueryControlProps) {
  const [builder, setBuilder] = useState<OsmQueryBuilderViewModel>(() => osmBuilderFromPreset(OSM_PRESETS[0]));
  const [selectedPreset, setSelectedPreset] = useState(() => !value.rawQl && !value.queryLabel ? OSM_PRESETS[0].id : '');
  const [buildError, setBuildError] = useState<OsmBuilderError | null>(null);
  const initialized = useRef(false);

  // Shared workflow starts with an empty form. Match the original's initial Buildings preset once.
  useEffect(() => {
    // Wait for the shared snapshot; changing the form while it loads would cancel that read.
    if (disabled || initialized.current) return;
    initialized.current = true;
    if (!value.rawQl && !value.queryLabel) {
      const initial = osmBuilderFromPreset(OSM_PRESETS[0]);
      const result = buildOsmTemplateQl(initial);
      if (result.ql !== null) onChange({ rawQl: result.ql, queryLabel: initial.label });
    }
  }, [disabled, onChange, value.queryLabel, value.rawQl]);

  function selectPreset(id: string) {
    setSelectedPreset(id);
    const preset = OSM_PRESETS.find(candidate => candidate.id === id);
    if (!preset) return;
    const next = osmBuilderFromPreset(preset);
    setBuilder(next);
    setBuildError(null);
    const result = buildOsmTemplateQl(next);
    if (result.ql !== null) onChange({ rawQl: result.ql, queryLabel: next.label });
  }

  function updateBuilder(next: OsmQueryBuilderViewModel) {
    setBuilder(next);
    setSelectedPreset('');
    const result = buildOsmTemplateQl(next);
    setBuildError(result.error);
    // The shared workflow permits nonempty raw QL; never let an invalid builder submit a comment.
    onChange({ rawQl: result.ql ?? '' });
  }

  function updateClause(id: string, patch: Partial<OsmClauseViewModel>) {
    updateBuilder({ ...builder, clauses: builder.clauses.map(clause => clause.id === id ? { ...clause, ...patch } : clause) });
  }

  function updateFilter(clause: OsmClauseViewModel, filterID: string, patch: Partial<OsmTagFilterViewModel>) {
    updateClause(clause.id, { filters: clause.filters.map(filter => filter.id === filterID ? { ...filter, ...patch } : filter) });
  }

  const preset = OSM_PRESETS.find(candidate => candidate.id === selectedPreset);
  return <div className="feature-query-osm">
    <label className="feature-query-field">
      <span>{text(language, '查询预设', 'Query preset')}</span>
      <select value={selectedPreset} disabled={disabled} onChange={event => selectPreset(event.target.value)}>
        <option value="">{text(language, '自定义', 'Custom')}</option>
        {OSM_PRESETS.map(item => <option key={item.id} value={item.id}>{language === 'zh' ? item.nameZh : item.nameEn}</option>)}
      </select>
    </label>
    {preset && <p className="feature-query-osm-description">{language === 'zh' ? preset.descriptionZh : preset.descriptionEn}</p>}
    <label className="feature-query-field">
      <span>{text(language, '查询名称', 'Query name')}</span>
      <input type="text" value={value.queryLabel} disabled={disabled} onChange={event => onChange({ queryLabel: event.target.value })} />
    </label>

    <details className="feature-query-osm-builder">
      <summary>{text(language, '查询构造器', 'Query builder')}</summary>
      <p>{text(language, '每个条件之间为“或”，同一条件内的标签为“且”。更改构造器会重新生成下方 QL；直接编辑 QL 不会反向修改构造器。', 'Clauses are combined with OR; tags within a clause use AND. Changing the builder regenerates the QL below; editing QL directly does not change the builder.')}</p>
      {builder.clauses.map((clause, index) => <fieldset className="feature-query-osm-clause" key={clause.id} disabled={disabled}>
        <legend>{text(language, `条件 ${index + 1}`, `Clause ${index + 1}`)}</legend>
        <div className="feature-query-osm-kinds" role="group" aria-label={text(language, '元素类型', 'Element types')}>
          <label><input type="checkbox" checked={clause.includeNodes} onChange={event => updateClause(clause.id, { includeNodes: event.target.checked })} />{text(language, '节点', 'Node')}</label>
          <label><input type="checkbox" checked={clause.includeWays} onChange={event => updateClause(clause.id, { includeWays: event.target.checked })} />{text(language, '路径', 'Way')}</label>
          <label><input type="checkbox" checked={clause.includeRelations} onChange={event => updateClause(clause.id, { includeRelations: event.target.checked })} />{text(language, '关系', 'Relation')}</label>
        </div>
        {clause.filters.map((filter, filterIndex) => <div className="feature-query-osm-filter" key={filter.id}>
          <span>{text(language, `标签 ${filterIndex + 1}`, `Tag ${filterIndex + 1}`)}</span>
          <OsmSuggestField backend={backend} backendRevision={backendRevision} language={language} disabled={disabled} field="key"
            keyText={filter.key} value={filter.key} label={text(language, '标签键', 'Tag key')}
            onChange={key => updateFilter(clause, filter.id, { key })} />
          <select aria-label={text(language, '标签比较方式', 'Tag comparison')} value={filter.operator}
            onChange={event => updateFilter(clause, filter.id, { operator: event.target.value as OsmTagOperator })}>
            {OSM_TAG_OPERATORS.map(option => <option key={option.operator} value={option.operator}>{language === 'zh' ? option.zh : option.en}</option>)}
          </select>
          {osmOperatorRequiresValue(filter.operator) && <OsmSuggestField backend={backend} backendRevision={backendRevision} language={language} disabled={disabled} field="value"
            keyText={filter.key} value={filter.value} label={text(language, '标签值', 'Tag value')}
            onChange={newValue => updateFilter(clause, filter.id, { value: newValue })} />}
          <button type="button" onClick={() => updateClause(clause.id, { filters: clause.filters.filter(candidate => candidate.id !== filter.id) })}
            aria-label={text(language, `删除标签 ${filterIndex + 1}`, `Remove tag ${filterIndex + 1}`)}>{text(language, '删除标签', 'Remove tag')}</button>
        </div>)}
        <div className="feature-query-osm-actions">
          <button type="button" onClick={() => updateClause(clause.id, { filters: [...clause.filters, createOsmTagFilter()] })}>{text(language, '添加标签', 'Add tag')}</button>
          <button type="button" onClick={() => updateBuilder({ ...builder, clauses: builder.clauses.filter(candidate => candidate.id !== clause.id) })}>{text(language, '删除条件', 'Remove clause')}</button>
        </div>
      </fieldset>)}
      <button type="button" disabled={disabled} onClick={() => updateBuilder({ ...builder, clauses: [...builder.clauses, createOsmClause()] })}>{text(language, '添加条件', 'Add clause')}</button>
      {buildError && <p role="alert">{builderErrorMessage(buildError, language)}</p>}
    </details>

    <label className="feature-query-field">
      <span>{text(language, 'Overpass QL 查询', 'Overpass QL query')}</span>
      <textarea className="feature-query-osm-raw" value={value.rawQl} disabled={disabled} spellCheck={false} rows={8}
        onChange={event => { setSelectedPreset(''); setBuildError(null); onChange({ rawQl: event.target.value }); }} />
    </label>
    <small>{text(language, '使用 {{bbox}} 代表查询时地图的当前范围；也可在 QL 中指定其他空间范围。', 'Use {{bbox}} for the current map extent when querying, or provide another spatial constraint in QL.')}</small>
  </div>;
}
