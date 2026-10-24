import type { EditorPageProps } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { BasicHydraulicBoundaryConditionsSummary } from './BasicHydraulicBoundaryConditionsSummary';

const SUMMARY_SCHEMA_ID = 'BoundaryConditionsSummary';

/** Fields and navigation composed by the original BoundaryConditionsSummary control. */
export function BoundaryConditionsSummary(props: EditorPageProps) {
  const schema = props.schemas.find((item) => item.id === SUMMARY_SCHEMA_ID);
  const links = [
    ['BoundaryConditionEditor', props.language === 'zh' ? '水力边界线' : 'Hydraulic boundary lines'],
    ['PrecipitationControl', props.language === 'zh' ? '降水' : 'Precipitation'],
    ['EvapotranspirationControl', props.language === 'zh' ? '蒸散发' : 'Evapotranspiration'],
    ['WindForcesControl', props.language === 'zh' ? '风力' : 'Wind forces'],
    ['AirPressureControl', props.language === 'zh' ? '气压' : 'Air pressure'],
  ] as const;

  return <section className="editor-table-card" aria-label={props.language === 'zh' ? '边界条件摘要字段' : 'Boundary condition summary fields'}>
    <div className="editor-terrain-table-heading">
      <h3>{props.language === 'zh' ? '边界条件设置' : 'Boundary condition settings'}</h3>
      {schema && <details className="editor-source-details"><summary>{props.language === 'zh' ? '原始控件' : 'Source control'}</summary><code>{schema.source}</code></details>}
    </div>
    {schema
      ? <SchemaFieldRenderer
          schema={schema}
          snapshot={props.snapshot}
          values={props.draft.fields}
          images={props.draft.images}
          canEdit={props.snapshot.canEdit}
          onFieldChange={props.onFieldChange}
          onImagesChange={props.onImagesChange}
          language={props.language}
          fieldKeyPrefix={SUMMARY_SCHEMA_ID}
        />
      : <p className="editor-field-muted">{props.language === 'zh' ? '服务端尚未提供边界摘要字段定义。' : 'The Host has not supplied the boundary summary field schema.'}</p>}
    <nav className="editor-page-tabs" aria-label={props.language === 'zh' ? '边界条件页面导航' : 'Boundary condition page navigation'}>
      {links.map(([pageID, title]) => <button type="button" key={pageID} onClick={() => props.onPageChange(pageID)}>{title}</button>)}
    </nav>
    <BasicHydraulicBoundaryConditionsSummary {...props} />
  </section>;
}
