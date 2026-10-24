import { useEffect, useRef, useState } from 'react';
import type { EditorPageProps, SpatialGridBand, SpatialGridInspection, SpatialGridSelection } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';
import { RasterPreviewCanvas } from './SpatialDataSummary';
import { SpatialBoundaryConditionBandInput } from './SpatialBoundaryConditionBandInput';

export function SpatialBoundaryConditionRasterImporter(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onClose: () => void;
}) {
  const [paths, setPaths] = useState('');
  const [subdataset, setSubdataset] = useState('');
  const [inspection, setInspection] = useState<SpatialGridInspection | null>(null);
  const [previewId, setPreviewId] = useState('');
  const [units, setUnits] = useState('');
  const [dataType, setDataType] = useState('');
  const [timeBehavior, setTimeBehavior] = useState<SpatialGridSelection['timeBehavior']>('ProjectBackwards');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  const files = paths.split(/\r?\n/).map((path) => path.trim()).filter(Boolean);
  const rasterBands = inspection?.bands ?? [];
  const included = rasterBands.filter((band) => band.included);
  const isDss = files.length === 1 && /\.dss$/i.test(files[0]);
  const canInspect = props.layerId !== 'evapotranspiration' && !!props.onGridInspect && files.length > 0 && props.snapshot.canEdit;
  const canImport = props.layerId !== 'evapotranspiration' && !!inspection && !isDss && included.length > 0 && props.snapshot.canEdit && !!props.canImportGrid && !!props.onGridImport && !busy;

  useEffect(() => () => { generation.current++; controller.current?.abort(); }, []);

  const inspect = async (dataset?: string, bands?: SpatialGridBand[], selectedPreview = previewId) => {
    if (!props.onGridInspect || files.length === 0) return;
    controller.current?.abort();
    const current = ++generation.current;
    const request = new AbortController();
    controller.current = request;
    setBusy(true);
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
      if (generation.current === current) setBusy(false);
    }
  };

  const updateBand = (changed: SpatialGridBand) => setInspection((current) => current ? {
    ...current,
    bands: current.bands.map((band) => band.id === changed.id ? changed : band),
  } : current);

  const importGrid = async () => {
    if (!props.onGridImport || !inspection || isDss || included.length === 0) return;
    setBusy(true);
    setError('');
    try {
      const accepted = await props.onGridImport({ layerId: props.layerId, bands: included, units: units || inspection.units, dataType: dataType || inspection.dataType, timeBehavior });
      if (accepted) props.onClose();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : label('栅格导入失败。', 'Grid import failed.'));
    } finally { setBusy(false); }
  };

  return <section className="editor-table-card" aria-label={label('空间栅格导入器', 'Spatial raster importer')}>
    <header className="editor-meteorology-layer-header">
      <div><p className="editor-eyebrow">{label('原控件：SpatialBoundaryConditionRasterImporter', 'Source control: SpatialBoundaryConditionRasterImporter')}</p>
        <h3>{label('检查并导入栅格', 'Inspect and import raster')}</h3></div>
      <button type="button" className="editor-button" disabled={busy} onClick={props.onClose}>{label('关闭', 'Close')}</button>
    </header>
    <p className="editor-field-muted">{label('每行输入一个由 C# 服务主机可访问的文件路径。浏览器不会把本机路径伪装成已上传文件；检查结果由当前 Boundary Conditions 模型快照返回。', 'Enter one file path per line that the C# server host can access. Browser paths are not treated as uploaded files; inspection is bound to the current Boundary Conditions model snapshot.')}</p>
    <label className="editor-field"><span>{label('源文件路径', 'Source file paths')}</span>
      <textarea rows={3} value={paths} disabled={busy} placeholder={label('/path/to/raster.tif', '/path/to/raster.tif')} onChange={(event) => { setPaths(event.currentTarget.value); setInspection(null); }} />
    </label>
    <div className="editor-workbench-actions">
      <button type="button" className="editor-button" disabled={!canInspect || busy} onClick={() => { void inspect(); }}>{busy ? label('正在处理…', 'Working…') : label('检查文件', 'Inspect files')}</button>
      {inspection?.subdatasets.length && !inspection.dssGridSources?.length ? <label className="editor-field"><span>{label('子数据集', 'Subdataset')}</span>
        <select value={subdataset} onChange={(event) => setSubdataset(event.currentTarget.value)}>
          <option value="">{label('选择子数据集', 'Choose subdataset')}</option>
          {inspection.subdatasets.map((item) => <option key={item} value={item}>{item}</option>)}
        </select>
        <button type="button" className="editor-button" disabled={!subdataset || busy} onClick={() => { void inspect(subdataset, inspection.bands); }}>{label('读取所选子数据集', 'Read selected subdataset')}</button>
      </label> : null}
    </div>
    {error && <p className="editor-field-error" role="alert">{error}</p>}
    {inspection && <>
      {inspection.preview && <RasterPreviewCanvas language={props.language} preview={inspection.preview} />}
      {inspection.dssGridSources?.length ? <div className="editor-meteorology-spatial">
        <p className="editor-field-muted">{label('检测到 DSS 格网记录。当前浏览器传输不支持此导入所需的双工取消控制，因此 DSS 导入已停用。', 'DSS grid records were detected. This browser transport does not expose the duplex cancellation control required by this import, so DSS import is disabled.')}</p>
        <div className="editor-table-scroll"><table className="editor-data-table"><thead><tr><th>{label('记录组', 'Record group')}</th><th>{label('时刻数', 'Timesteps')}</th><th>{label('单位', 'Units')}</th><th>{label('类型', 'Data type')}</th></tr></thead>
          <tbody>{inspection.dssGridSources.map((source) => <tr key={source.id}><td>{source.title || source.groupPath}</td><td>{source.times.length}</td><td>{source.units}</td><td>{source.dataType}</td></tr>)}</tbody></table></div>
      </div> : rasterBands.length > 0 && <>
        <div className="editor-field-grid">
          <label className="editor-field"><span>{label('单位', 'Units')}</span><select value={units} onChange={(event) => setUnits(event.currentTarget.value)}>{inspection.unitOptions.map((unit) => <option key={unit} value={unit}>{unit}</option>)}</select></label>
          <label className="editor-field"><span>{label('数据类型', 'Data type')}</span>
            <select value={dataType} onChange={(event) => setDataType(event.currentTarget.value)}>
              {(props.layerId === 'precipitation' ? ['per-avg', 'per-cum'] : ['inst-val']).map((kind) => <option key={kind} value={kind}>{kind}</option>)}
            </select>
          </label>
          <label className="editor-field"><span>{label('时间行为', 'Time behavior')}</span>
            <select value={timeBehavior} onChange={(event) => setTimeBehavior(event.currentTarget.value as SpatialGridSelection['timeBehavior'])}>
              <option value="ProjectBackwards">ProjectBackwards</option><option value="ProjectForwards">ProjectForwards</option>
            </select><small>{label('与原控件一致：此选择会随请求保存，但当前后端导入流程不使用它。', 'As in the source control, this is sent with the request but is not consumed by the current backend import.')}</small>
          </label>
        </div>
        <div className="editor-table-scroll"><table className="editor-data-table">
          <thead><tr><th>{label('导入', 'Include')}</th><th>{label('波段', 'Band')}</th><th>{label('时间', 'Time')}</th><th>{label('预报起报时间', 'Issue time')}</th><th>{label('源单位', 'Source units')}</th><th>{label('分量', 'Component')}</th><th>{label('预览', 'Preview')}</th></tr></thead>
          <tbody>{rasterBands.map((band) => <SpatialBoundaryConditionBandInput key={band.id} language={props.language} band={band} components={props.layerId === 'wind' ? ['X', 'Y'] : ['Value']} selected={band.id === previewId} onChange={updateBand} onPreview={(id) => { setPreviewId(id); void inspect(subdataset, rasterBands, id); }} />)}</tbody>
        </table></div>
        <button type="button" className="editor-button editor-button-primary" disabled={!canImport || busy} onClick={() => { void importGrid(); }}>
          {label('导入并保存格网', 'Import and save grid')} ({included.length})
        </button>
        {!props.canImportGrid && <p className="editor-field-muted">{label('导入会立即提交模型文件。请先保存或丢弃当前草稿，避免把未提交编辑一并写入。', 'Import commits the model file immediately. Save or discard the current draft first so pending edits are not committed with it.')}</p>}
      </>}
    </>}
  </section>;
}
