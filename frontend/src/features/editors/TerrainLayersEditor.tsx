import type { EditorPageProps, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';
import { editorTableTitle } from './editorLocalization';
import { RasterTerrainInputProperties } from './RasterTerrainInputProperties';
import { FunctionalTerrainInputProperties } from './FunctionalTerrainInputProperties';
import { ChannelModTerrainInputProperties } from './ChannelModTerrainInputProperties';
import { DetailedTerrainHeader } from './DetailedTerrainHeader';
import { DetailedTerrainInputHeader } from './DetailedTerrainInputHeader';
import { TerrainAddControl } from './TerrainAddControl';
import { LeveeModTerrainInputEditor } from './LeveeModTerrainInputEditor';
import { LeveeModsTable } from './LeveeModsTable';

const selectedInputField = 'TerrainLayersEditor/SelectedInputWithMap';
const channelFeatureTableID = 'ChannelModEditor/Features';
const channelProfileTableID = 'ChannelModEditor/Profile';
const channelTerrainProfileTableID = 'ChannelModEditor/TerrainProfile';
const leveeFeatureTableID = 'LeveeModsTable';
const leveePropertyTableID = 'LeveeModPropertiesEditor/PropertySets';
const leveeProfileTableID = 'LeveeModDetailEditor/Profile';
const polygonFeatureTableID = 'PolygonRasterModificationLayerProperties/Features';
/** React port of Ras.Visual.WinUI.Controls.TerrainEditor and TerrainLayersEditor state. */
export function TerrainLayersEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const terrain = draft.terrainView ?? snapshot.terrain;
  const inputs = terrain?.inputs ?? [];
  const selectedId = draft.terrainView
    ? terrain?.selectedId ?? ''
    : draft.fields[selectedInputField] ?? terrain?.selectedId ?? '';
  const selected = inputs.find((input) => input.id === selectedId) ?? inputs[0];
  if (!selected) return <section className="editor-schema-page editor-terrain-inputs">
    <DetailedTerrainHeader language={language} snapshot={snapshot} draft={draft} />
    <div className="editor-empty-state">{language === 'zh' ? '该地形没有输入图层。' : 'This terrain has no input layers.'}</div>
    <TerrainAddControl language={language} canEdit={snapshot.canEdit} onTerrainTransform={props.onTerrainTransform} />
  </section>;

  const projectedSnapshot = draft.terrainView ? {
    ...snapshot,
    terrain,
    fieldChoices: { ...(snapshot.fieldChoices ?? {}), ...(terrain?.fieldChoices ?? {}) },
    readOnlyFields: [...new Set([...(snapshot.readOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? [])])],
  } : snapshot;
  const projectedProps = { ...props, snapshot: projectedSnapshot };

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
  const selectedIndex = inputs.findIndex((input) => input.id === selected.id);
  const transform = props.onTerrainTransform;

  const runTransform = (command: Parameters<NonNullable<EditorPageProps['onTerrainTransform']>>[0]) => { void transform?.(command); };

  return (
    <section className="editor-schema-page editor-terrain-inputs">
      <DetailedTerrainHeader language={language} snapshot={projectedSnapshot} draft={draft} />
      <div className="editor-terrain-layout">
        <nav className="editor-line-list" aria-label={language === 'zh' ? '地形输入' : 'Terrain inputs'}>
          {inputs.map((input) => <button type="button" key={input.id} className={input.id === selected.id ? 'selected' : ''} disabled={!transform} onClick={() => runTransform({ operation: 'selectInput', itemId: input.id })}>
            {draft.fields[`${input.scope}/Name`] ?? input.name}<small>{input.error ? (language === 'zh' ? '有错误' : 'Error') : (language === 'zh' ? '地形输入' : 'Terrain input')}</small>
          </button>)}
          <TerrainAddControl language={language} canEdit={snapshot.canEdit} onTerrainTransform={transform} />
        </nav>
        <div className="editor-terrain-content">
          <article className="editor-terrain-card">
            <DetailedTerrainInputHeader language={language} input={selected} canEdit={snapshot.canEdit} value={draft.fields[selectedInputNameField] ?? selected.name} onNameChange={props.onFieldChange} onRemove={transform} />
            {inputSchema && selected.schema === 'RasterTerrainInputProperties' && <RasterTerrainInputProperties {...projectedProps} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} />}
            {inputSchema && selected.schema === 'FunctionalTerrainInputProperties' && <FunctionalTerrainInputProperties {...projectedProps} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} />}
            {inputSchema && selected.schema === 'ChannelModTerrainInputProperties' && <ChannelModTerrainInputProperties {...projectedProps} selected={selected} schema={inputSchema} fieldKeyPrefix={fieldKeyPrefix} selectedInputIsLoaded={selectedInputIsLoaded} />}
            {inputSchema && !['RasterTerrainInputProperties', 'FunctionalTerrainInputProperties', 'ChannelModTerrainInputProperties'].includes(inputSchema.id) && <SchemaFieldRenderer
              schema={inputSchema}
              fieldKeyPrefix={fieldKeyPrefix}
              snapshot={projectedSnapshot}
              values={draft.fields}
              images={draft.images}
              canEdit={snapshot.canEdit}
              onFieldChange={props.onFieldChange}
              onImagesChange={props.onImagesChange}
              language={language}
            />}
            {transform && <div className="editor-button-row">
              <button type="button" className="editor-button" disabled={!snapshot.canEdit || selectedIndex <= 0} onClick={() => runTransform({ operation: 'reorder', from: selectedIndex, to: selectedIndex - 1 })}>{language === 'zh' ? '上移图层' : 'Move layer up'}</button>
              <button type="button" className="editor-button" disabled={!snapshot.canEdit || selectedIndex < 0 || selectedIndex >= inputs.length - 1} onClick={() => runTransform({ operation: 'reorder', from: selectedIndex, to: selectedIndex + 1 })}>{language === 'zh' ? '下移图层' : 'Move layer down'}</button>
            </div>}
          </article>

          {hasLeveeTables && <LeveeModTerrainInputEditor {...projectedProps} selected={selected} />}

          {tables.map(([tableID, table]) => {
            if (channelSchemaSelected && [channelFeatureTableID, channelProfileTableID, channelTerrainProfileTableID].includes(tableID)) return null;
            if (hasLeveeTables && [leveeFeatureTableID, leveePropertyTableID, leveeProfileTableID].includes(tableID)) return null;
            const modelTable = table as ModelTable;
            const rows = draft.rows[tableID] ?? terrain?.rows?.[tableID] ?? [];
            if (tableID === polygonFeatureTableID) return <LeveeModsTable key={tableID} tableID={tableID} title={tableTitle(tableID, language)} table={modelTable} rows={rows} language={language} canEdit={snapshot.canEdit} onCellChange={props.onRowCellChange} onTransform={transform} selectionMode="multiple" />;
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
