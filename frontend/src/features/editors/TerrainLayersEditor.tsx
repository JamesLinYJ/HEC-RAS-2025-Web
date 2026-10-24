import type { EditorPageProps, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';
import { editorTableTitle, editorValueLabel } from './editorLocalization';
import { RasterTerrainInputProperties } from './RasterTerrainInputProperties';
import { FunctionalTerrainInputProperties } from './FunctionalTerrainInputProperties';
import { ChannelModTerrainInputProperties } from './ChannelModTerrainInputProperties';
import { LeveeModDetailEditor } from './LeveeModDetailEditor';
import { LeveeModPropertiesDetailEditor } from './LeveeModPropertiesDetailEditor';

const selectedInputField = 'TerrainLayersEditor/SelectedInputWithMap';
const channelFeatureTableID = 'ChannelModEditor/Features';
const channelProfileTableID = 'ChannelModEditor/Profile';
const channelTerrainProfileTableID = 'ChannelModEditor/TerrainProfile';
const leveeFeatureTableID = 'LeveeModsTable';
const leveePropertyTableID = 'LeveeModPropertiesEditor/PropertySets';
const leveeProfileTableID = 'LeveeModDetailEditor/Profile';

/** React port of Ras.Visual.WinUI.Controls.TerrainEditor and TerrainLayersEditor state. */
export function TerrainLayersEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const terrain = snapshot.terrain;
  const inputs = terrain?.inputs ?? [];
  const selectedId = draft.fields[selectedInputField] ?? terrain?.selectedId ?? '';
  const selected = inputs.find((input) => input.id === selectedId) ?? inputs[0];
  if (!selected) return <div className="editor-empty-state">{language === 'zh' ? '该地形没有可显示的输入图层。' : 'This terrain has no input layers to display.'}</div>;

  const inputSchema = selected.schema ? props.schemas.find((schema) => schema.id === selected.schema) : undefined;
  const fieldKeyPrefix = inputSchema ? `${selected.scope}/${inputSchema.id}` : '';
  const selectedInputNameField = `${selected.scope}/Name`;
  // Tables and feature-specific fields are projected for the input selected by
  // the backend. A local draft selection cannot retarget that projection until
  // the C# service returns a snapshot for it.
  const selectedInputIsLoaded = terrain?.selectedId === selected.id;
  const tables = selectedInputIsLoaded
    ? Object.entries(terrain?.tables ?? {}).filter(([tableID]) => {
      const rowSet = draft.rows[tableID] ?? terrain?.rows?.[tableID];
      return rowSet !== undefined;
    })
    : [];
  const tableByID = terrain?.tables ?? {};
  const channelSchemaSelected = selected.schema === 'ChannelModTerrainInputProperties';
  const hasLeveeTables = selectedInputIsLoaded && (tableByID[leveeFeatureTableID] !== undefined || tableByID[leveePropertyTableID] !== undefined);

  return (
    <section className="editor-schema-page editor-terrain-inputs">
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{language === 'zh' ? '地形编辑器' : 'Terrain editor'}</p><h2>{language === 'zh' ? '输入图层' : 'Input layers'}</h2></div>
        <details className="editor-source-details"><summary>{language === 'zh' ? '来源控件' : 'Source control'}</summary><code>TerrainLayersEditor</code></details>
      </div>
      <div className="editor-terrain-layout">
        <nav className="editor-line-list" aria-label={language === 'zh' ? '地形输入' : 'Terrain inputs'}>
          {inputs.map((input) => <button type="button" key={input.id} className={input.id === selected.id ? 'selected' : ''} onClick={() => props.onFieldChange(selectedInputField, input.id)}>
            {draft.fields[`${input.scope}/Name`] ?? input.name}<small>{editorValueLabel(language, input.type)}{input.error ? ` · ${language === 'zh' ? '有错误' : 'Error'}` : ''}</small>
          </button>)}
        </nav>
        <div className="editor-terrain-content">
          <article className="editor-terrain-card">
            <h3>{draft.fields[selectedInputNameField] ?? selected.name}</h3>
            <div className="editor-field-grid">
              <div className="editor-field">
                <label htmlFor="terrain-input-name">{language === 'zh' ? '输入名称' : 'Input name'}</label>
                <input id="terrain-input-name" type="text" value={draft.fields[selectedInputNameField] ?? selected.name} disabled={!snapshot.canEdit} onChange={(event) => props.onFieldChange(selectedInputNameField, event.currentTarget.value)} />
              </div>
            </div>
            <dl className="editor-readonly-list">
              <dt>{language === 'zh' ? '输入类型' : 'Input type'}</dt><dd>{editorValueLabel(language, selected.type)}</dd>
              <dt>{language === 'zh' ? '源文件' : 'Source file'}</dt><dd>{selected.filename ?? '—'}</dd>
              <dt>{language === 'zh' ? '有效范围' : 'Valid extent'}</dt><dd>{selected.validExtent ? `${selected.validExtent.minX}, ${selected.validExtent.minY} – ${selected.validExtent.maxX}, ${selected.validExtent.maxY}` : '—'}</dd>
              {selected.error && <><dt>{language === 'zh' ? '状态' : 'Status'}</dt><dd className="editor-field-error"><details><summary>{language === 'zh' ? '错误详情' : 'Error details'}</summary><pre>{selected.error}</pre></details></dd></>}
            </dl>
            {inputSchema && selected.schema === 'RasterTerrainInputProperties' && <RasterTerrainInputProperties {...props} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} />}
            {inputSchema && selected.schema === 'FunctionalTerrainInputProperties' && <FunctionalTerrainInputProperties {...props} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} />}
            {inputSchema && selected.schema === 'ChannelModTerrainInputProperties' && <ChannelModTerrainInputProperties {...props} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} selectedInputIsLoaded={selectedInputIsLoaded} />}
            {inputSchema && !['RasterTerrainInputProperties', 'FunctionalTerrainInputProperties', 'ChannelModTerrainInputProperties'].includes(inputSchema.id) && <SchemaFieldRenderer
              schema={inputSchema}
              fieldKeyPrefix={fieldKeyPrefix}
              snapshot={snapshot}
              values={draft.fields}
              images={draft.images}
              canEdit={snapshot.canEdit}
              onFieldChange={props.onFieldChange}
              onImagesChange={props.onImagesChange}
              language={language}
            />}
            {selected.overviews?.length ? <details className="editor-source-details"><summary>{language === 'zh' ? `金字塔层级 (${selected.overviewCount ?? selected.overviews.length})` : `Raster overviews (${selected.overviewCount ?? selected.overviews.length})`}</summary><ul>{selected.overviews.map((item) => <li key={item}>{item}</li>)}</ul></details> : null}
          </article>

          {hasLeveeTables && <>
            <LeveeModDetailEditor {...props} selected={selected} schema={props.schemas.find((schema) => schema.id === 'LeveeModDetailEditor')} />
            <LeveeModPropertiesDetailEditor {...props} selected={selected} schema={props.schemas.find((schema) => schema.id === 'LeveeModPropertiesDetailEditor')} />
          </>}

          {tables.map(([tableID, table]) => {
            if (channelSchemaSelected && [channelFeatureTableID, channelProfileTableID, channelTerrainProfileTableID].includes(tableID)) return null;
            if (hasLeveeTables && [leveeFeatureTableID, leveePropertyTableID, leveeProfileTableID].includes(tableID)) return null;
            const modelTable = table as ModelTable;
            const rows = draft.rows[tableID] ?? terrain?.rows?.[tableID] ?? [];
            return <article className="editor-terrain-card" key={tableID}>
              <div className="editor-terrain-table-heading"><h3>{tableTitle(tableID, language)}</h3><span>{rows.length} {language === 'zh' ? '行' : 'rows'}</span></div>
              <ModelDataTable tableID={tableID} rows={rows} columns={modelTable.columns} canEdit={snapshot.canEdit} onCellChange={props.onRowCellChange} language={language} />
              {(modelTable.canAddRows || modelTable.canDeleteRows || modelTable.canCopyRows) && <small className="editor-field-muted">{language === 'zh' ? '原始编辑器支持行结构操作；当前页面只提交已有行中已修改的单元格。' : 'The source editor supports structural row operations; this page currently submits edits to existing cells only.'}</small>}
            </article>;
          })}
        </div>
      </div>
    </section>
  );
}

function tableTitle(tableID: string, language: 'zh' | 'en'): string {
  return editorTableTitle(language, tableID);
}
