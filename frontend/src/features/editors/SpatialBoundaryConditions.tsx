import { useState } from 'react';
import type { EditorPageProps, SpatialPointData } from './model';
import { isEditorFieldReadOnly } from './model';
import { SpatialInputs } from './MeteorologyLayerControl';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';

export function SpatialBoundaryConditions(props: EditorPageProps & { layerId: SpatialBoundaryLayerID }) {
  const { language, snapshot, draft, layerId } = props;
  const boundary = draft.boundaryView ?? snapshot.boundary;
  const spatial = boundary?.spatial?.[layerId];
  const layers = boundary?.spatial ?? {};
  const stations = spatial?.stations ?? [];
  const inputs = spatial?.inputs ?? [];
  const series = Object.values(boundary?.series ?? {}).filter((item) => {
    const prefix = `Spatial/${layerId}/Gauge/`;
    return item.scope.startsWith(prefix) && item.pageId === `${item.scope}/Time Series`;
  });
  const [stationChoice, setStationChoice] = useState('');
  const stationId = stations.some((station) => station.id === stationChoice) ? stationChoice : stations[0]?.id ?? '';
  const label = (zh: string, en: string) => language === 'zh' ? zh : en;
  const canEdit = snapshot.canEdit;
  const field = (key: string, fallback = '') => draft.fields[key] ?? snapshot.fields[key] ?? fallback;
  const change = (key: string, value: string) => props.onFieldChange(key, value);
  const spatialKey = (scope: string, property: string) => `${scope}/${property}`;

  const addStation = () => { void props.onSpatialTransform?.(layerId, 'addStation', null); };
  const removeStation = (id: string) => { void props.onSpatialTransform?.(layerId, 'removeStation', id); };
  const addGauge = () => { if (stationId) void props.onSpatialTransform?.(layerId, 'addGauge', stationId); };
  const removeGauge = (id: string) => { void props.onSpatialTransform?.(layerId, 'removeGauge', id); };
  const stationIsUsed = (id: string) => Object.values(layers).some((layer) => layer.inputs.some((input) => {
    const key = spatialKey(input.scope, 'Station');
    return (draft.fields[key] ?? snapshot.fields[key] ?? input.stationId ?? '') === id;
  }));

  return <section className="editor-table-card" aria-label={label('空间边界输入', 'Spatial boundary inputs')}>
    <header className="editor-meteorology-layer-header"><div>
      <p className="editor-eyebrow">{label('空间边界快照', 'Spatial boundary snapshot')}</p>
      <h3>{label('测站与输入', 'Stations and inputs')}</h3>
    </div>
      <div className="editor-workbench-actions">
        <button type="button" className="editor-button" disabled={!canEdit || !props.canTransformSpatial || !props.onSpatialTransform} onClick={addStation}>{label('添加测站', 'Add station')}</button>
        <button type="button" className="editor-button" disabled={!canEdit || !stationId || !props.canTransformSpatial || !props.onSpatialTransform} onClick={addGauge}>{label('为所选测站添加输入', 'Add input for selected station')}</button>
      </div>
    </header>
    <p className="editor-field-muted">{label('测站和输入变更先进入草稿，保存后才写入模型。', 'Station and input changes enter the draft first and are written to the model when saved.')}</p>

    {stations.length === 0 ? <p className="editor-field-muted">{label('此空间边界没有测站。', 'This spatial boundary has no stations.')}</p>
      : <div className="editor-table-scroll"><table className="editor-data-table">
        <thead><tr><th>{label('测站名称', 'Station name')}</th><th>X</th><th>Y</th><th>{label('离地高度', 'Elevation off ground')}</th><th>{label('操作', 'Actions')}</th></tr></thead>
        <tbody>{stations.map((station) => {
          const nameKey = `Spatial/Station/${station.id}/Name`;
          const xKey = `Spatial/Station/${station.id}/X`;
          const yKey = `Spatial/Station/${station.id}/Y`;
          const heightKey = `Spatial/Station/${station.id}/ElevationOffGround`;
          return <tr key={station.id} data-station-id={station.id}>
            <th scope="row"><input aria-label={label('测站名称', 'Station name')} value={field(nameKey, station.name)} readOnly={!canEdit || isEditorFieldReadOnly(props, nameKey)} onChange={(event) => change(nameKey, event.currentTarget.value)} /></th>
            <td><input aria-label="X" inputMode="decimal" value={field(xKey, String(station.x))} readOnly={!canEdit || isEditorFieldReadOnly(props, xKey)} onChange={(event) => change(xKey, event.currentTarget.value)} /></td>
            <td><input aria-label="Y" inputMode="decimal" value={field(yKey, String(station.y))} readOnly={!canEdit || isEditorFieldReadOnly(props, yKey)} onChange={(event) => change(yKey, event.currentTarget.value)} /></td>
            <td><input aria-label={label('离地高度', 'Elevation off ground')} inputMode="decimal" value={field(heightKey)} readOnly={!canEdit || isEditorFieldReadOnly(props, heightKey)} onChange={(event) => change(heightKey, event.currentTarget.value)} /></td>
            <td><button type="button" className="editor-button" disabled={!canEdit || stationIsUsed(station.id) || !props.canTransformSpatial || !props.onSpatialTransform} title={stationIsUsed(station.id) ? label('此测站仍被输入引用；先移除或改派输入。', 'This station is still referenced. Remove or reassign its inputs first.') : undefined} onClick={() => removeStation(station.id)}>{label('移除', 'Remove')}</button></td>
          </tr>;
        })}</tbody>
      </table></div>}

    <div className="editor-field">
      <label htmlFor="spatial-station-for-new-input">{label('新增输入所用测站', 'Station for new input')}</label>
      <select id="spatial-station-for-new-input" value={stationId} onChange={(event) => setStationChoice(event.currentTarget.value)} disabled={!canEdit || stations.length === 0}>
        {stations.map((station) => <option key={station.id} value={station.id}>{station.name || station.id}</option>)}
      </select>
    </div>
    {inputs.length === 0 ? <p className="editor-field-muted">{label('此图层尚无空间输入。', 'This layer has no spatial inputs.')}</p>
      : <div className="editor-meteorology-spatial">
        {inputs.map((input, index) => {
          const stationKey = spatialKey(input.scope, 'Station');
          const enabledKey = spatialKey(input.scope, 'IsEnabled');
          const dataLocationKey = spatialKey(input.scope, 'DataLocation');
          const localDataTypeKey = spatialKey(input.scope, 'LocalDataType');
          const stationValue = field(stationKey, input.stationId ?? '');
          const station = stations.find((candidate) => candidate.id === stationValue);
          const enabledValue = field(enabledKey, 'true');
          const title = station?.name || label(`空间输入 ${index + 1}`, `Spatial input ${index + 1}`);
          const choices = snapshot.fieldChoices?.[dataLocationKey] ?? [];
          return <article className="editor-meteorology-input" key={input.id} data-input-id={input.id} data-input-scope={input.scope}>
            <header className="editor-meteorology-layer-header"><div><h4>{title}</h4><code>{input.scope}</code></div>
              <button type="button" className="editor-button" disabled={!canEdit || !props.canTransformSpatial || !props.onSpatialTransform} onClick={() => removeGauge(input.id)}>{label('移除输入', 'Remove input')}</button>
            </header>
            <div className="editor-field-grid">
              <label className="editor-field"><span>{label('关联测站', 'Station')}</span>
                <select value={stationValue} disabled={!canEdit || isEditorFieldReadOnly(props, stationKey)} onChange={(event) => change(stationKey, event.currentTarget.value)}>
                  <option value="">{label('未关联', 'None')}</option>
                  {stations.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.name || candidate.id}</option>)}
                </select>
              </label>
              <label className="editor-field"><span>{label('启用', 'Enabled')}</span>
                <select value={enabledValue} disabled={!canEdit || isEditorFieldReadOnly(props, enabledKey)} onChange={(event) => change(enabledKey, event.currentTarget.value)}>
                  <option value="true">{label('是', 'Yes')}</option><option value="false">{label('否', 'No')}</option>
                </select>
              </label>
              {choices.length > 0 && <label className="editor-field"><span>{label('数据位置', 'Data location')}</span>
                <select value={field(dataLocationKey)} disabled={!canEdit || isEditorFieldReadOnly(props, dataLocationKey)} onChange={(event) => change(dataLocationKey, event.currentTarget.value)}>
                  {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.title}</option>)}
                </select>
              </label>}
              {field(localDataTypeKey) && <span className="editor-field-muted">{label('本地数据类型', 'Local data type')}: {field(localDataTypeKey)}</span>}
              {(field(localDataTypeKey) === 'Constant' || field(localDataTypeKey) === '') && <GaugeConstantField {...props} scope={input.scope} property="Constant" />}
              {layerId === 'wind' && field(localDataTypeKey) === 'Constant' && <GaugeConstantField {...props} scope={input.scope} property="Constant2" />}
            </div>
          </article>;
        })}
      </div>}
    {inputs.length > 0 && <details className="editor-source-details"><summary>{label('空间时序数据', 'Spatial time-series data')}</summary>
      <SpatialInputs {...props} spatial={spatial as SpatialPointData | undefined} series={series} />
    </details>}
    {!props.onSpatialTransform && <p className="editor-field-muted">{label('当前数据源不支持添加或移除测站与输入。', 'The current data source does not support adding or removing stations and inputs.')}</p>}
    {props.onSpatialTransform && !props.canTransformSpatial && <p className="editor-field-muted">{label('请先修正当前草稿的无效值，再修改空间输入结构。', 'Correct invalid draft values before changing spatial input structure.')}</p>}
  </section>;
}

function GaugeConstantField(props: EditorPageProps & { scope: string; property: string }) {
  const key = `${props.scope}/${props.property}`;
  const value = props.draft.fields[key] ?? props.snapshot.fields[key] ?? '';
  return <label className="editor-field"><span>{props.language === 'zh' ? (props.property === 'Constant2' ? '常数 Y' : '常数值') : props.property}</span>
    <input inputMode="decimal" value={value} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, key)} onChange={(event) => props.onFieldChange(key, event.currentTarget.value)} />
  </label>;
}
