import { useEffect, useRef, useState } from 'react';
import type { EditorPageProps, SpatialGridBand, SpatialGridInspection, SpatialGridSelection } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';
import { RasterPreviewCanvas } from './SpatialDataSummary';
import { SpatialBoundaryConditionBandInput } from './SpatialBoundaryConditionBandInput';
import { GriddedDataImporterTimeline } from './GriddedDataImporterTimeline';

export function GriddedDataImporter(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onClose: () => void;
  onBusyChange?: (busy: boolean) => void;
}) {
  const [paths, setPaths] = useState('');
  const [subdataset, setSubdataset] = useState('');
  const [inspection, setInspection] = useState<SpatialGridInspection | null>(null);
  const [previewId, setPreviewId] = useState('');
  const [units, setUnits] = useState('');
  const [dataType, setDataType] = useState('');
  const [timeBehavior, setTimeBehavior] = useState<SpatialGridSelection['timeBehavior']>('ProjectBackwards');
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const importInProgress = useRef(false);
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  const layerTitle = (props.layerId === 'precipitation' ? label('降水', 'Precipitation')
    : props.layerId === 'wind' ? label('风', 'Wind')
      : props.layerId === 'airPressure' ? label('气压', 'Air pressure')
        : props.layerId === 'airDensity' ? label('空气密度', 'Air density') : label('蒸散发', 'Evapotranspiration'));
  const files = paths.split(/\r?\n/).map((path) => path.trim()).filter(Boolean);
  const rasterBands = inspection?.bands ?? [];
  const included = rasterBands.filter((band) => band.included);
  const isDss = files.length === 1 && /\.dss$/i.test(files[0]);
  const canInspect = props.layerId !== 'evapotranspiration' && !!props.onGridInspect && files.length > 0 && props.snapshot.canEdit;
  const canImport = props.layerId !== 'evapotranspiration' && !!inspection && !isDss && included.length > 0 && props.snapshot.canEdit && !!props.canImportGrid && !!props.onGridImport && !busy;

  useEffect(() => () => { generation.current++; controller.current?.abort(); }, []);

  const inspect = async (dataset?: string, bands?: SpatialGridBand[], selectedPreview = previewId) => {
    if (!props.onGridInspect || files.length === 0) return;
    setConfirming(false);
    controller.current?.abort();
    const current = ++generation.current;
    const request = new AbortController();
    controller.current = request;
    setBusy(true);
    props.onBusyChange?.(true);
    setError('');
    try {
      const next = await props.onGridInspect(props.layerId, files, dataset || undefined, bands, selectedPreview || undefined, request.signal);
      if (request.signal.aborted || generation.current !== current) return;
      setInspection(next);
      setSubdataset(dataset ?? '');
      setPreviewId(selectedPreview || next.bands[0]?.id || '');
      setUnits(next.units);
      setDataType(next.dataType);
    } catch (cause) {
      if (!request.signal.aborted && generation.current === current) setError(cause instanceof Error ? cause.message : label('栅格检查失败。', 'Grid inspection failed.'));
    } finally {
      if (generation.current === current) {
        setBusy(false);
        props.onBusyChange?.(false);
      }
    }
  };

  const updateBand = (changed: SpatialGridBand) => {
    setConfirming(false);
    setInspection((current) => current ? {
      ...current,
      bands: current.bands.map((band) => band.id === changed.id ? changed : band),
    } : current);
  };

  const importGrid = async () => {
    if (!props.onGridImport || !inspection || isDss || included.length === 0 || importInProgress.current) return;
    importInProgress.current = true;
    setBusy(true);
    props.onBusyChange?.(true);
    setError('');
    try {
      const accepted = await props.onGridImport({ layerId: props.layerId, bands: included, units: units || inspection.units, dataType: dataType || inspection.dataType, timeBehavior });
      if (accepted) props.onClose();
      else setError(label('工作台未确认导入；请查看工作台状态与错误详情。', 'The workbench did not confirm the import. Check its status and error details.'));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : label('栅格导入失败。', 'Grid import failed.'));
    } finally {
      importInProgress.current = false;
      setBusy(false);
      props.onBusyChange?.(false);
    }
  };

  const prepareImport = () => {
    setConfirming(true);
    setError('');
  };
  const selectTimelineItem = (id: string) => {
    setConfirming(false);
    setPreviewId(id);
    void inspect(subdataset, rasterBands, id);
  };
  const timelineItems = rasterBands.map((band) => ({
    id: band.id,
    time: band.time ?? '',
    issueTime: band.issueTime,
    detail: `${band.file}${band.dataset ? ` · ${band.dataset}` : ''} · Band ${band.band + 1}`,
    included: band.included,
  }));

  return <section className="editor-table-card" aria-label={label('空间栅格导入器', 'Spatial raster importer')}>
    <header className="editor-meteorology-layer-header">
      <div><p className="editor-eyebrow">{label('空间边界条件', 'Spatial boundary condition')}</p>
        <h3>{label('检查并导入栅格', 'Inspect and import raster')}</h3></div>
      <button type="button" className="editor-button" disabled={busy} onClick={props.onClose}>{label('关闭', 'Close')}</button>
    </header>
    <p className="editor-field-muted">{label('每行输入一个当前项目所在计算机可访问的栅格路径。浏览器中的本地路径不会自动上传。Cumulus 导入需要已连接的账户，目前不可用。', 'Enter one raster path per line that is accessible from the computer running the current project. A browser-local path is not uploaded automatically. Cumulus import requires a connected account and is currently unavailable.')}</p>
    <label className="editor-field"><span>{label('源文件路径', 'Source file paths')}</span>
      <textarea rows={3} value={paths} disabled={busy} placeholder={label('/path/to/raster.tif', '/path/to/raster.tif')} onChange={(event) => { setPaths(event.currentTarget.value); setInspection(null); setPreviewId(''); setConfirming(false); }} />
    </label>
    <div className="editor-workbench-actions">
      <button type="button" className="editor-button" disabled={!canInspect || busy} onClick={() => { void inspect(); }}>{busy ? label('正在处理…', 'Working…') : label('检查文件', 'Inspect files')}</button>
      {inspection?.subdatasets.length && !inspection.dssGridSources?.length ? <label className="editor-field"><span>{label('子数据集', 'Subdataset')}</span>
        <select value={subdataset} disabled={busy} onChange={(event) => { setSubdataset(event.currentTarget.value); setConfirming(false); }}>
          <option value="">{label('选择子数据集', 'Choose subdataset')}</option>
          {inspection.subdatasets.map((item) => <option key={item} value={item}>{item}</option>)}
        </select>
        <button type="button" className="editor-button" disabled={!subdataset || busy} onClick={() => { const first = inspection.bands[0]?.id ?? ''; setPreviewId(first); void inspect(subdataset, inspection.bands, first); }}>{label('读取所选子数据集', 'Read selected subdataset')}</button>
      </label> : null}
    </div>
    {error && <p className="editor-field-error" role="alert">{error}</p>}
    {inspection && <>
      {inspection.preview && <RasterPreviewCanvas language={props.language} preview={inspection.preview} />}
      {inspection.dssGridSources?.length ? <div className="editor-meteorology-spatial">
        <p className="editor-field-muted">{label('已识别 DSS 格网记录。DSS 导入暂不可用；下方列出已检测记录供核对。', 'DSS grid records were detected. DSS import is currently unavailable; the records below are shown for review.')}</p>
        <div className="editor-table-scroll"><table className="editor-data-table"><thead><tr><th>{label('记录组', 'Record group')}</th><th>{label('时刻数', 'Timesteps')}</th><th>{label('单位', 'Units')}</th><th>{label('类型', 'Data type')}</th></tr></thead>
          <tbody>{inspection.dssGridSources.map((source) => <tr key={source.id}><td>{source.title || source.groupPath}</td><td>{source.times.length}</td><td>{source.units}</td><td>{source.dataType}</td></tr>)}</tbody></table></div>
      </div> : rasterBands.length > 0 && <>
        <GriddedDataImporterTimeline
          language={props.language}
          items={timelineItems}
          selectedId={previewId}
          loading={busy}
          disabled={busy}
          onSelect={(item) => selectTimelineItem(item.id)}
        />
        <div className="editor-field-grid">
          <label className="editor-field"><span>{label('单位', 'Units')}</span><select value={units} disabled={busy} onChange={(event) => { setUnits(event.currentTarget.value); setConfirming(false); }}>{inspection.unitOptions.map((unit) => <option key={unit} value={unit}>{unit}</option>)}</select></label>
          <label className="editor-field"><span>{label('数据类型', 'Data type')}</span>
            <select value={dataType} disabled={busy} onChange={(event) => { setDataType(event.currentTarget.value); setConfirming(false); }}>
              {(props.layerId === 'precipitation' ? ['per-avg', 'per-cum'] : ['inst-val']).map((kind) => <option key={kind} value={kind}>{kind}</option>)}
            </select>
          </label>
          <label className="editor-field"><span>{label('时间行为', 'Time behavior')}</span>
            <select value={timeBehavior} disabled={busy} onChange={(event) => { setTimeBehavior(event.currentTarget.value as SpatialGridSelection['timeBehavior']); setConfirming(false); }}>
              <option value="ProjectBackwards">ProjectBackwards</option><option value="ProjectForwards">ProjectForwards</option>
            </select><small>{label('此选项不会改变已列出的波段时刻；请核对每个波段的时间与起报时间。', 'This option does not change the listed band times. Review each band time and issue time.')}</small>
          </label>
        </div>
        <div className="editor-table-scroll"><table className="editor-data-table">
          <thead><tr><th>{label('导入', 'Include')}</th><th>{label('波段', 'Band')}</th><th>{label('时间', 'Time')}</th><th>{label('预报起报时间', 'Issue time')}</th><th>{label('源单位', 'Source units')}</th><th>{label('分量', 'Component')}</th><th>{label('预览', 'Preview')}</th></tr></thead>
          <tbody>{rasterBands.map((band) => <SpatialBoundaryConditionBandInput key={band.id} language={props.language} band={band} components={props.layerId === 'wind' ? ['X', 'Y'] : ['Value']} selected={band.id === previewId} disabled={busy} onChange={updateBand} onPreview={(id) => { setPreviewId(id); setConfirming(false); void inspect(subdataset, rasterBands, id); }} />)}</tbody>
        </table></div>
        {!confirming
          ? <button type="button" className="editor-button editor-button-primary" disabled={!canImport || busy} onClick={prepareImport}>
            {label('检查导入内容…', 'Review import…')} ({included.length})
          </button>
          : <section className="editor-confirmation-panel" role="group" aria-label={label('确认格网导入', 'Confirm grid import')}>
            <h4>{label('确认立即保存格网？', 'Confirm immediate grid save?')}</h4>
            <p className="editor-field-muted">{label(`图层：${layerTitle}；波段：${included.length}；单位：${units || inspection.units}；数据类型：${dataType || inspection.dataType}。将保存到当前模型。请先保存或丢弃其他草稿。`, `Layer: ${layerTitle}; bands: ${included.length}; units: ${units || inspection.units}; data type: ${dataType || inspection.dataType}. This will be saved to the current model. Save or discard other draft edits first.`)}</p>
            <div className="editor-workbench-actions">
              <button type="button" className="editor-button" disabled={busy} onClick={() => setConfirming(false)}>{label('返回检查', 'Back to review')}</button>
              <button type="button" className="editor-button editor-button-primary" disabled={!canImport || busy} onClick={() => { void importGrid(); }}>
                {busy ? label('正在保存…', 'Saving…') : label('确认导入并保存', 'Confirm import and save')}
              </button>
            </div>
          </section>}
        {!props.canImportGrid && <p className="editor-field-muted">{label('导入会立即提交模型文件。请先保存或丢弃当前草稿，避免把未提交编辑一并写入。', 'Import commits the model file immediately. Save or discard the current draft first so pending edits are not committed with it.')}</p>}
      </>}
    </>}
  </section>;
}
