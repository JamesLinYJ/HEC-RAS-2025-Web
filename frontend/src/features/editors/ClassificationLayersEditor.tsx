import type { EditorPageProps } from './model';
import { ModelDataTable } from './ModelDataTable';
import { editorValueLabel } from './editorLocalization';

const selectedClassificationField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';

/** React port of Ras.Visual.WinUI.Controls.ClassificationLayersEditor. */
export function ClassificationLayersEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const inputs = snapshot.classification?.inputs ?? [];
  const selectedID = draft.fields[selectedClassificationField] ?? snapshot.classification?.selectedId ?? '';
  const selected = inputs.find((input) => input.id === selectedID) ?? inputs[0];
  if (!selected) return <div className="editor-empty-state">{language === 'zh' ? '该图层没有分类输入。' : 'This layer has no classification inputs.'}</div>;
  const inputNameKey = `${selected.scope}/Name`;
  const rasterNameKey = `${selected.scope}/RasterClassificationInputProperties/RasterClassificationInput_Name`;
  const rasterTypeKey = `${selected.scope}/RasterClassificationInputProperties/RasterClassificationInput_TypeName`;
  const tableSchema = selected.tableId ? props.tableSchemas?.find((table) => table.id === selected.tableId) : undefined;
  const columns = tableSchema?.columns ?? [];
  const rows = selected.tableId ? draft.rows[selected.tableId] ?? selected.rows : [];
  return (
    <section className="editor-schema-page editor-classification-inputs">
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{language === 'zh' ? '分类输入' : 'Classification inputs'}</p><h2>{language === 'zh' ? '图层与映射' : 'Layers and mappings'}</h2></div>
        <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source control'}</summary><code>ClassificationLayersEditor</code></details>
      </div>
      <div className="editor-classification-layout">
        <nav className="editor-line-list" aria-label={language === 'zh' ? '分类输入' : 'Classification inputs'}>
          {inputs.map((input) => <button type="button" key={input.id} className={input.id === selected.id ? 'selected' : ''} onClick={() => props.onFieldChange(selectedClassificationField, input.id)}>{input.name}<small>{editorValueLabel(language, input.type)}</small></button>)}
        </nav>
        <div className="editor-classification-content">
          <article className="editor-terrain-card">
            <h3>{selected.name}</h3>
            <dl className="editor-readonly-list">
              <dt>{language === 'zh' ? '类型' : 'Type'}</dt><dd>{editorValueLabel(language, selected.type)}</dd>
              <dt>{language === 'zh' ? '来源' : 'Source'}</dt><dd>{selected.filename ?? selected.extent ?? '—'}</dd>
            </dl>
            <div className="editor-field-grid">
              <div className="editor-field"><label htmlFor="classification-input-name">{language === 'zh' ? '输入名称' : 'Input name'}</label><input id="classification-input-name" type="text" value={draft.fields[inputNameKey] ?? selected.name} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(inputNameKey, event.currentTarget.value)} /></div>
              {draft.fields[rasterNameKey] !== undefined && <div className="editor-field"><label htmlFor="classification-raster-name">{language === 'zh' ? '栅格名称' : 'Raster name'}</label><input id="classification-raster-name" type="text" value={draft.fields[rasterNameKey]} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(rasterNameKey, event.currentTarget.value)} /></div>}
              {draft.fields[rasterTypeKey] !== undefined && <div className="editor-field"><label htmlFor="classification-raster-type">{language === 'zh' ? '栅格类型' : 'Raster type'}</label><input id="classification-raster-type" type="text" value={draft.fields[rasterTypeKey]} readOnly /></div>}
            </div>
            {selected.tableId && <div className="editor-table-card">
              <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '分类值映射' : 'Classification value mapping'}</h3><span>{rows.length} {language === 'zh' ? '项' : 'entries'}</span></div>
              <ModelDataTable tableID={selected.tableId} rows={rows} columns={columns.map((column) => ({ id: column.id, title: column.title, sourceType: column.sourceType, readOnly: column.readOnly }))} canEdit={snapshot.canEdit} onCellChange={props.onRowCellChange} language={language} />
            </div>}
            {selected.polygons.length > 0 && <div className="editor-table-card">
              <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '分类多边形' : 'Classification polygons'}</h3><span>{selected.polygons.length} {language === 'zh' ? '个' : 'features'}</span></div>
              <div className="editor-polygon-list">{selected.polygons.map((polygon) => {
                const item = polygon;
                const field = `${item.scope}/PolygonClassificationInputProperties/ClassifierMod_Classification`;
                return <div className="editor-polygon-row" key={item.id}><span>{item.vertexCount} {language === 'zh' ? '个顶点' : 'vertices'}</span><input type="text" value={draft.fields[field] ?? item.classification} list="classification-classes" disabled={!snapshot.canEdit} aria-label={language === 'zh' ? '多边形分类' : 'Polygon classification'} onChange={(event) => props.onFieldChange(field, event.currentTarget.value)} /></div>;
              })}<datalist id="classification-classes">{(snapshot.classification?.classes ?? []).map((value) => <option value={value} key={value} />)}</datalist></div>
            </div>}
            {snapshot.classification?.canUndoGeometry || snapshot.classification?.canRedoGeometry ? <p className="editor-field-muted">{language === 'zh' ? '检测到几何编辑历史；当前页面保留并提交分类属性，几何命令仍需对应后端操作。' : 'Geometry edit history is present. This page preserves classification properties; geometry commands still require their backend operation.'}</p> : null}
          </article>
        </div>
      </div>
    </section>
  );
}
