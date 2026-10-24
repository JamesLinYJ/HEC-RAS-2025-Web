import type { EditorPageProps } from './model';
import { ClassificationMappingEditor } from './ClassificationMappingEditor';
import type { ModelRow } from './model';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Read-only raster metadata and the source ClassificationMappingEditor child. */
export function RasterClassificationInputProperties({ props, input }: { props: EditorPageProps; input: ClassificationInput }) {
  const { language, snapshot, draft } = props;
  const tableID = input.tableId;
  if (!tableID) return <div className="editor-empty-state">{language === 'zh' ? '后端快照未提供栅格分类表。' : 'The backend snapshot did not include a raster classification table.'}</div>;

  const rows: ModelRow[] = draft.rows[tableID] ?? input.rows;
  const rasterNameField = `${input.scope}/RasterClassificationInputProperties/RasterClassificationInput_Name`;
  const rasterTypeField = `${input.scope}/RasterClassificationInputProperties/RasterClassificationInput_TypeName`;
  const tableSchema = props.tableSchemas?.find((table) => table.id === tableID);
  const columns = tableSchema?.columns.map((column) => ({
    id: column.id,
    title: column.title,
    sourceType: column.sourceType,
    readOnly: column.readOnly || column.id === 'Key',
  }));

  return (
    <section className="editor-terrain-card" aria-label={language === 'zh' ? '栅格分类输入属性' : 'Raster classification input properties'}>
      <h3>{language === 'zh' ? '栅格分类输入属性' : 'Raster classification input properties'}</h3>
      <dl className="editor-readonly-list">
        <dt>{language === 'zh' ? '栅格名称' : 'Raster name'}</dt><dd>{draft.fields[`${input.scope}/Name`] ?? draft.fields[rasterNameField] ?? input.name}</dd>
        <dt>{language === 'zh' ? '栅格类型' : 'Raster type'}</dt><dd>{draft.fields[rasterTypeField] ?? input.type}</dd>
        <dt>{language === 'zh' ? '文件' : 'File'}</dt><dd>{input.filename ?? '—'}</dd>
      </dl>
      <ClassificationMappingEditor
        language={language}
        rows={rows}
        tableID={tableID}
        canEdit={snapshot.canEdit}
        onCellChange={props.onRowCellChange}
        columns={columns}
      />
    </section>
  );
}
