import type { EditorPageProps, ModelTable } from './model';
import { SchemaFieldRenderer } from './SchemaFieldRenderer';
import { ModelDataTable } from './ModelDataTable';
import { editorTableTitle } from './editorLocalization';
import { RasterTerrainInputProperties } from './RasterTerrainInputProperties';
import { FunctionalTerrainInputProperties } from './FunctionalTerrainInputProperties';
import { ChannelModTerrainInputProperties } from './ChannelModTerrainInputProperties';
import { TerrainInputLayerPicker } from './TerrainInputLayerPicker';
import { TerrainInputStats } from './TerrainInputStats';
import { PolygonRasterModificationLayerProperties } from './PolygonRasterModificationLayerProperties';
import { DetailedTerrainHeader } from './DetailedTerrainHeader';
import { DetailedTerrainInputHeader } from './DetailedTerrainInputHeader';
import { TerrainAddControl } from './TerrainAddControl';
import { LeveeModTerrainInputEditor } from './LeveeModTerrainInputEditor';
import { TerrainPolygonMapEditor } from './TerrainPolygonMapEditor';

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
  const polygonRasterModSelected = selected.type === 'PolygonRasterModTerrainInput' || selected.type.endsWith('.PolygonRasterModTerrainInput');
  const hasLeveeTables = selectedInputIsLoaded && (tableByID[leveeFeatureTableID] !== undefined || tableByID[leveePropertyTableID] !== undefined);
  const transform = props.onTerrainTransform;

  return (
    <section className="editor-schema-page editor-terrain-inputs">
      <DetailedTerrainHeader language={language} snapshot={projectedSnapshot} draft={draft} />
      <div className="editor-terrain-layout">
        <div className="editor-terrain-picker-column">
          <TerrainInputLayerPicker language={language} inputs={inputs} selectedId={selected.id} names={draft.fields} canEdit={snapshot.canEdit} onTerrainTransform={transform} />
          <TerrainAddControl language={language} canEdit={snapshot.canEdit} onTerrainTransform={transform} />
        </div>
        <div className="editor-terrain-content">
          <article className="editor-terrain-card">
            <DetailedTerrainInputHeader language={language} input={selected} canEdit={snapshot.canEdit} value={draft.fields[selectedInputNameField] ?? selected.name} onNameChange={props.onFieldChange} onRemove={transform} />
            <TerrainInputStats language={language} inputs={inputs} input={selected} />
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
          </article>

          {hasLeveeTables && <LeveeModTerrainInputEditor {...projectedProps} selected={selected} />}

          {polygonRasterModSelected && selectedInputIsLoaded && <TerrainPolygonMapEditor
            key={`${selected.id}:${snapshot.revision}`}
            backend={props.backend}
            owner={props.terrainMapOwner}
            terrainPath={props.asset.path}
            inputId={selected.id}
            inputName={draft.fields[selectedInputNameField] ?? selected.name}
            inputIndex={selected.sourceVectorIndex}
            sourceRevision={snapshot.revision}
            canEdit={snapshot.canEdit && props.canEditTerrainTopology === true}
            onRun={props.onTerrainTopology}
          />}

          {tables.map(([tableID, table]) => {
            if (channelSchemaSelected && [channelFeatureTableID, channelProfileTableID, channelTerrainProfileTableID].includes(tableID)) return null;
            if (hasLeveeTables && [leveeFeatureTableID, leveePropertyTableID, leveeProfileTableID].includes(tableID)) return null;
            const modelTable = table as ModelTable;
            const rows = draft.rows[tableID] ?? terrain?.rows?.[tableID] ?? [];
            if (tableID === polygonFeatureTableID) return <PolygonRasterModificationLayerProperties key={tableID} language={language} tableID={tableID} table={modelTable} rows={rows} canEdit={snapshot.canEdit} onCellChange={props.onRowCellChange} onTransform={transform} />;
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
