import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { text } from '../../../shared/workbench';
import { positionsInGeometry } from '../geoJson';
import type { GeoGeometry, XY } from '../mapTypes';
import type { FeatureQueryDialogProps, FeatureQueryKind } from './featureQueryTypes';
import type { FeatureQueryPreview } from './featureQueryProtocol';
import { useFeatureQuery } from './useFeatureQuery';
import './featureQuery.css';

const kindLabels: Record<FeatureQueryKind, string> = { NLD: 'NLD · 美国堤坝数据库', NSI: 'NSI · 美国建筑清单',
  NFHL: 'NFHL · 洪水危险图层', OSM: 'OpenStreetMap' };
const kindLabelsEn: Record<FeatureQueryKind, string> = { NLD: 'NLD · National Levee Database', NSI: 'NSI · National Structure Inventory',
  NFHL: 'NFHL · National Flood Hazard Layer', OSM: 'OpenStreetMap' };
const displayProperty = (value: unknown): string => {
  if (value === null) return 'null';
  if (typeof value === 'string') return value;
  try { return JSON.stringify(value) ?? String(value); } catch { return String(value); }
};
function displayDiagnostic(value: unknown): string {
  const seen = new WeakSet<object>();
  const format = (item: unknown, indent: number): string => {
    if (item === null || typeof item !== 'object') return String(item);
    if (seen.has(item)) return '[循环引用]';
    seen.add(item);
    const entries = item instanceof Error
      ? Object.entries({ ...item, name: item.name, message: item.message }) : Object.entries(item);
    if (!entries.length) return Array.isArray(item) ? '[]' : '{}';
    return entries.map(([key, child]) => `${' '.repeat(indent)}${key}: ${child !== null && typeof child === 'object'
      ? `\n${format(child, indent + 2)}` : format(child, indent + 2)}`).join('\n');
  };
  return format(value, 0);
}

/** SVG uses the exact project-world coordinates returned by the service; no CRS guessing or reprojection. */
function Preview({ data, language }: { data: FeatureQueryPreview; language: FeatureQueryDialogProps['language'] }) {
  const [selected, setSelected] = useState('0:0');
  const model = useMemo(() => {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const layer of data.layers) for (const feature of layer.features) if (feature.geometry) {
      for (const [x, y] of positionsInGeometry(feature.geometry)) {
        minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
      }
    }
    if (![minX, minY, maxX, maxY].every(Number.isFinite)) return null;
    const spanX = maxX - minX, spanY = maxY - minY;
    const span = Math.max(spanX, spanY, 1e-12);
    const scale = Math.min(920 / Math.max(spanX, span * 0.01), 520 / Math.max(spanY, span * 0.01));
    if (!Number.isFinite(scale) || scale <= 0) return null;
    const centerX = minX + spanX / 2, centerY = minY + spanY / 2;
    const point = ([x, y]: XY): XY => [500 + (x - centerX) * scale, 300 - (y - centerY) * scale];
    return { point, minX, minY, maxX, maxY };
  }, [data]);
  const [layerIndex, featureIndex] = selected.split(':').map(Number);
  const selectedFeature = data.layers[layerIndex]?.features[featureIndex];
  const properties = selectedFeature?.properties ? Object.entries(selectedFeature.properties) : [];
  function pathLine(points: readonly XY[], close = false): string {
    if (!model || !points.length) return '';
    return points.map((raw, index) => { const [x, y] = model.point(raw); return `${index ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`; }).join(' ') + (close ? ' Z' : '');
  }
  function geometryParts(geometry: GeoGeometry, key: string, stroke: string, fill: string, width: number): React.ReactNode {
    if (!model) return null;
    const pointShape = (point: XY, id: string) => { const [x, y] = model.point(point);
      return <circle key={id} cx={x} cy={y} r="3.5" stroke={stroke} fill={fill === '#00000000' ? stroke : fill} strokeWidth={width} />; };
    switch (geometry.type) {
      case 'Point': return pointShape(geometry.coordinates, key);
      case 'MultiPoint': return geometry.coordinates.map((point, i) => pointShape(point, `${key}-${i}`));
      case 'LineString': return <path key={key} d={pathLine(geometry.coordinates)} fill="none" stroke={stroke} strokeWidth={width} />;
      case 'MultiLineString': return <path key={key} d={geometry.coordinates.map(line => pathLine(line)).join(' ')} fill="none" stroke={stroke} strokeWidth={width} />;
      case 'Polygon': return <path key={key} d={geometry.coordinates.map(ring => pathLine(ring, true)).join(' ')} fill={fill} fillRule="evenodd" stroke={stroke} strokeWidth={width} />;
      case 'MultiPolygon': return <path key={key} d={geometry.coordinates.flatMap(polygon => polygon.map(ring => pathLine(ring, true))).join(' ')} fill={fill} fillRule="evenodd" stroke={stroke} strokeWidth={width} />;
      case 'GeometryCollection': return geometry.geometries.map((child, i) => geometryParts(child, `${key}-${i}`, stroke, fill, width));
    }
  }
  return <>
    <div className="feature-query-preview-summary">
      <strong>{text(language, '查询结果预览', 'Query preview')}</strong>
      <span>{text(language, `显示 ${data.returnedFeatureCount} / ${data.totalFeatureCount} 个要素，${data.returnedCoordinateCount} / ${data.totalCoordinateCount} 个坐标`,
        `Showing ${data.returnedFeatureCount} of ${data.totalFeatureCount} features and ${data.returnedCoordinateCount} of ${data.totalCoordinateCount} coordinates`)}</span>
      {data.truncated && <p className="feature-query-warning">{text(language, '预览已截断，仅显示部分完整要素；导出使用完整查询结果。', 'The preview is truncated to complete features; export uses the full query result.')}</p>}
      {data.warnings.map((warning, index) => <p className="feature-query-warning" key={index}>{warning}</p>)}
    </div>
    <div className="feature-query-preview-canvas">
      {model ? <svg className="feature-query-preview-svg" viewBox="0 0 1000 600" role="img"
        aria-label={text(language, '工程坐标下的查询要素几何预览', 'Query feature geometries in project coordinates')}>
        {data.layers.map((layer, layerIndexValue) => <g key={layer.layerId}>
          {layer.features.map((feature, index) => feature.geometry && geometryParts(feature.geometry, `${layerIndexValue}-${index}`,
            layer.lineColor, layer.fillColor, Math.max(1, Math.min(layer.lineWidth, 6))))}
        </g>)}
      </svg> : <p className="feature-query-preview-empty">{text(language, '返回的预览中没有可绘制的坐标。', 'The preview contains no drawable coordinates.')}</p>}
    </div>
    <div className="feature-query-attributes">
      <label className="feature-query-field"><span>{text(language, '查看要素属性', 'Inspect feature properties')}</span>
        <select value={selectedFeature ? selected : ''} onChange={event => setSelected(event.target.value)}>
          {!selectedFeature && <option value="">{text(language, '没有可显示的要素', 'No displayed feature')}</option>}
          {data.layers.flatMap((layer, i) => layer.features.map((feature, j) => <option key={`${i}:${j}`} value={`${i}:${j}`}>
            {layer.name} · {text(language, '要素', 'Feature')} {j + 1}{feature.id == null ? '' : ` · ${String(feature.id)}`}
          </option>))}
        </select>
      </label>
      {selectedFeature && (properties.length ? <dl>{properties.map(([name, value]) => <div key={name}><dt>{name}</dt><dd>{displayProperty(value)}</dd></div>)}</dl>
        : <p>{text(language, '此要素没有属性。', 'This feature has no properties.')}</p>)}
      <details><summary>{text(language, '图层与显示数量', 'Layer and displayed counts')}</summary>
        <ul>{data.layers.map(layer => <li key={layer.layerId}>{layer.name}：{layer.returnedFeatureCount} / {layer.totalFeatureCount}，
          {text(language, '坐标', 'coordinates')} {layer.returnedCoordinateCount} / {layer.totalCoordinateCount}</li>)}</ul>
      </details>
    </div>
  </>;
}

/** One modal hosts the source-specific original form and the shared real query/export workflow. */
export function FeatureQueryDialog(props: FeatureQueryDialogProps) {
  const vm = useFeatureQuery(props);
  const id = useId();
  const dialog = useRef<HTMLDivElement>(null);
  const initializedFocus = useRef(false);
  useEffect(() => {
    const node = dialog.current;
    if (!node) return;
    let timer: number | undefined;
    const restoreOutsideFocus = (event: FocusEvent) => {
      if (event.target instanceof Node && !node.contains(event.target)) node.focus();
    };
    const restoreDisabledFocus = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        if (document.hasFocus() && !node.contains(document.activeElement)) node.focus();
      }, 0);
    };
    document.addEventListener('focusin', restoreOutsideFocus);
    node.addEventListener('focusout', restoreDisabledFocus);
    node.focus();
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener('focusin', restoreOutsideFocus);
      node.removeEventListener('focusout', restoreDisabledFocus);
    };
  }, []);
  useEffect(() => {
    const node = dialog.current;
    if (!node || initializedFocus.current || !vm.snapshot || vm.reading) return;
    initializedFocus.current = true;
    if (document.activeElement === node || !node.contains(document.activeElement)) {
      const firstParameter = node.querySelector<HTMLElement>(
        '.feature-query-controls select:not(:disabled), .feature-query-controls input:not(:disabled), .feature-query-controls textarea:not(:disabled)');
      (firstParameter ?? node).focus();
    }
  }, [vm.snapshot, vm.reading]);
  const t = (zh: string, en: string) => text(props.language, zh, en);
  const closeReason = vm.reading ? t('请先取消或等待查询完成。', 'Cancel or wait for the query first.')
    : vm.locked ? t('写入或恢复结果尚未确认，请先核对原任务。', 'The write or recovery is unconfirmed; check the original task first.') : '';
  function keyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') { event.preventDefault(); if (vm.canClose) props.onClose(); return; }
    if (event.key !== 'Tab' || !dialog.current) return;
    const focusable = [...dialog.current.querySelectorAll<HTMLElement>('button:not([disabled]), select:not([disabled]), input:not([disabled]), textarea:not([disabled]), details > summary, [tabindex]:not([tabindex="-1"])')]
      .filter(element => element.getClientRects().length > 0);
    if (!focusable.length) { event.preventDefault(); dialog.current.focus(); return; }
    const index = focusable.indexOf(document.activeElement as HTMLElement);
    if (event.shiftKey && index <= 0) { event.preventDefault(); focusable[focusable.length - 1].focus(); }
    else if (!event.shiftKey && (index < 0 || index === focusable.length - 1)) { event.preventDefault(); focusable[0].focus(); }
  }
  const controls = props.renderControl({ language: props.language, disabled: vm.locked || !!vm.reading || !vm.snapshot,
    value: vm.form, onChange: vm.change, polygonSources: vm.snapshot?.polygonSources ?? [], extent: props.extent,
    backend: props.backend, backendRevision: props.backendRevision });
  const primaryAction = vm.savedCurrent && vm.saved && (!vm.importState || vm.canImport) ? 'import' : vm.result?.hasFeatures ? 'save' : 'query';
  return <div className="feature-query-backdrop">
    <div ref={dialog} className="feature-query-dialog" role="dialog" aria-modal="true" aria-labelledby={`${id}-title`}
      aria-describedby={closeReason ? `${id}-close-reason` : undefined} tabIndex={-1} onKeyDown={keyDown}>
      <header className="feature-query-header"><h2 id={`${id}-title`}>{t('在线数据', 'Online data')}</h2>
        <button type="button" className="feature-query-close" disabled={!vm.canClose} title={closeReason || t('关闭', 'Close')}
          onClick={props.onClose} aria-label={t('关闭在线数据', 'Close online data')}>×</button>
      </header>
      {closeReason && <p id={`${id}-close-reason`} className="feature-query-progress" role="status">{closeReason}</p>}
      <div className="feature-query-body">
        <section className="feature-query-controls" aria-label={t('查询参数', 'Query parameters')}>
          <label className="feature-query-field"><span>{t('数据来源', 'Data source')}</span>
            <select className="feature-query-select" value={props.kind} disabled={!props.onKindChange || vm.locked || !!vm.reading}
              onChange={event => props.onKindChange?.(event.target.value as FeatureQueryKind)}>
              {(Object.keys(kindLabels) as FeatureQueryKind[]).map(kind => <option key={kind} value={kind}>{props.language === 'zh' ? kindLabels[kind] : kindLabelsEn[kind]}</option>)}
            </select>
          </label>
          {controls}
          <section className="feature-query-export" aria-label={t('导出设置', 'Export settings')}>
            <h3>{t('导出设置', 'Export settings')}</h3>
            <label className="feature-query-field"><span>{t('文件格式', 'File format')}</span>
              <select value={vm.format} disabled={vm.locked} onChange={event => vm.setFormat(event.target.value as 'GeoJson' | 'Shapefile')}>
                <option value="GeoJson">GeoJSON</option><option value="Shapefile">Shapefile</option>
              </select>
            </label>
            <label className="feature-query-field"><span>{t('服务端输出目录（绝对路径）', 'Server output directory (absolute path)')}</span>
              <input type="text" value={vm.directory} disabled={vm.locked} onChange={event => vm.setDirectory(event.target.value)}
                placeholder={t('例如 /data/exports 或 C:\\Exports', 'For example /data/exports or C:\\Exports')} />
            </label>
            <p className="feature-query-field-hint">{t('目录必须已存在于 C# 服务所在的计算机。', 'The directory must exist on the computer running the C# service.')}</p>
            <p className="feature-query-field-hint">{t('若要加入地图，请导出到工程目录内；工程外来源须先登记为外部连接。', 'To add the files to a map, export them within the project directory; sources outside the project must first be registered as external connections.')}</p>
          </section>
        </section>
        <section className="feature-query-preview" aria-label={t('查询预览和属性', 'Query preview and properties')}>
          {vm.preview ? <Preview key={vm.preview.resultId} data={vm.preview} language={props.language} />
            : <div className="feature-query-preview-empty">{vm.reading === 'preview' ? t('正在读取真实要素和属性…', 'Reading feature geometry and properties…')
              : vm.result && !vm.result.hasFeatures ? t('没有返回要素。', 'No features were returned.') : t('查询后在此显示真实要素和属性。', 'Run a query to view actual features and properties here.')}</div>}
          {vm.result?.info && <details><summary>{t('服务端查询信息', 'Server query information')}</summary><pre>{vm.result.info}</pre></details>}
          {vm.saved && <section className="feature-query-export-files"><h3>{t('已导出文件', 'Exported files')}</h3>
            {!vm.savedCurrent && <p className="feature-query-warning">{t('这些文件属于之前的查询；工程地图或查询参数已变化，不能加入当前地图。', 'These files belong to a previous query; the map or query parameters changed and they cannot be added here.')}</p>}
            {vm.saved.receipt.sources.length ? <ul>{vm.saved.receipt.sources.map(source => <li key={source.path}><code>{source.path}</code> · {source.featureCount} {t('个要素', 'features')}</li>)}</ul>
              : <p>{t('服务未返回可加入地图的文件。', 'The service returned no files to add to the map.')}</p>}
            {vm.saved.receipt.warnings.map((warning, index) => <p className="feature-query-warning" key={index}>{warning}</p>)}
            <details><summary>{t('服务端导出信息', 'Server export information')}</summary><pre>{vm.saved.receipt.info}</pre></details>
            {vm.importState && <div className="feature-query-message" role="status"><p>{vm.importState.notice}</p>
              {vm.importState.report?.operationID && <p>{t('操作编号', 'Operation ID')}: <code>{vm.importState.report.operationID}</code></p>}
              {vm.importState.report?.message && <p>{vm.importState.report.message}</p>}
              {!!vm.importState.report?.imported.length && <ul>{vm.importState.report.imported.map(path => <li key={path}>{path}</li>)}</ul>}
              {vm.importState.report?.failures.map((failure, index) => <p className="feature-query-error" key={index}>{failure.path}: {failure.message}</p>)}
            </div>}
          </section>}
        </section>
      </div>
      <div className="feature-query-status" aria-live="polite">
        {vm.reading && <p className="feature-query-progress">{vm.reading === 'snapshot' ? t('正在核对地图和多边形来源…', 'Checking the map and polygon sources…')
          : vm.reading === 'query' ? t('正在查询在线数据…', 'Querying online data…') : t('正在读取预览…', 'Reading the preview…')}</p>}
        {vm.notice && <p className="feature-query-message">{vm.notice}</p>}
        {vm.problem && <div className="feature-query-error" role="alert"><p>{t('操作未完成：', 'Operation incomplete: ')}{vm.problem}</p>
          {vm.problemDiagnostic !== undefined && <details><summary>{t('服务诊断', 'Service diagnostics')}</summary><pre>{displayDiagnostic(vm.problemDiagnostic)}</pre></details>}
        </div>}
        {vm.inputError && !vm.reading && <p className="feature-query-field-hint">{vm.inputError}</p>}
        {vm.saveStatus && <div className={vm.saveStatus.phase === 'failed' || vm.saveStatus.phase === 'unknown' || vm.saveStatus.phase === 'unverified' ? 'feature-query-warning' : 'feature-query-message'} role="status">
          <p>{vm.saveStatus.message}</p>{vm.saveStatus.operationID && <p>{t('操作编号', 'Operation ID')}: <code>{vm.saveStatus.operationID}</code></p>}
          {vm.saveStatus.phase === 'unknown' && !vm.saveStatus.operationID && <p>{t('服务未返回操作编号；请保留此窗口并核对服务端输出目录。', 'No operation ID was returned. Keep this window open and inspect the server output directory.')}</p>}
          {['failed', 'unknown', 'unverified'].includes(vm.saveStatus.phase) && vm.saveStatus.diagnostic !== undefined &&
            <details><summary>{t('服务诊断', 'Service diagnostics')}</summary><pre>{displayDiagnostic(vm.saveStatus.diagnostic)}</pre></details>}
        </div>}
        {!!vm.cleanupErrors.length && <p className="feature-query-warning">{t(`有 ${vm.cleanupErrors.length} 个原查询暂存尚未确认清理。`, `${vm.cleanupErrors.length} query stages have not been confirmed cleaned.`)} <button type="button" onClick={vm.retryCleanup}>{t('重试清理', 'Retry cleanup')}</button></p>}
      </div>
      <footer className="feature-query-footer"><div className="feature-query-actions">
        {vm.reading && vm.reading !== 'snapshot' && <button className="feature-query-secondary" type="button" onClick={vm.cancelRead}>{t('取消查询', 'Cancel query')}</button>}
        {vm.saveStatus?.phase === 'saving' && <button className="feature-query-secondary" type="button" disabled={vm.saveStatus.cancelRequested} onClick={vm.cancelSave}>{t('取消导出', 'Cancel export')}</button>}
        {(vm.saveStatus?.phase === 'unknown' || vm.saveStatus?.phase === 'unverified') && <button className="feature-query-secondary" type="button" disabled={!vm.canResumeSave} onClick={() => void vm.resumeSave()}>{t('继续核对原导出任务', 'Check original export task')}</button>}
        {vm.importState?.report?.status === 'unknown' && <button className="feature-query-secondary" type="button" disabled={!vm.importState.report.resume || vm.importState.busy} onClick={() => void vm.resumeImport()}>{t('继续核对原加入任务', 'Check original import task')}</button>}
        <button className={primaryAction === 'query' ? 'feature-query-primary' : 'feature-query-secondary'} type="button" disabled={!vm.canQuery} title={vm.inputError ?? ''} onClick={() => void vm.query()}>{t('查询区域', 'Query area')}</button>
        <button className={primaryAction === 'save' ? 'feature-query-primary' : 'feature-query-secondary'} type="button" disabled={!vm.canSave} onClick={() => void vm.save()}>{t('导出文件', 'Export files')}</button>
        <button className={primaryAction === 'import' ? 'feature-query-primary' : 'feature-query-secondary'} type="button" disabled={!vm.canImport} onClick={() => void vm.importSources()}>{t('加入地图', 'Add to map')}</button>
      </div></footer>
    </div>
  </div>;
}
