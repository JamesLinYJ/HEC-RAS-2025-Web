import { useEffect, useId, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { BackgroundMapsVMState } from './BackgroundMapsVM';
import './backgroundMaps.css';

export interface OSMStyleOption {
  id: string;
  name: string;
  description: string;
  previewContentType: string | null;
  preview: string | null;
  warning: string | null;
}

export interface OSMStyleResult {
  requestID: string;
  styles: OSMStyleOption[];
  warning: string | null;
}

type OSMSettings = Pick<BackgroundMapsVMState, 'OsmStyle' | 'Density' | 'ShowLabels'>;

export interface OSMStylePickerProps {
  backend: BackendGateway;
  backendRevision?: number;
  language: Language;
  state: OSMSettings;
  onChange: (update: Partial<OSMSettings>) => void;
  onCacheCleared?: () => void;
}

type StyleLoadStatus = 'loading' | 'ready' | 'empty' | 'failed' | 'unavailable' | 'cancelled';

interface StyleLoadState {
  backend: BackendGateway;
  backendRevision: number;
  status: StyleLoadStatus;
  styles: OSMStyleOption[];
  diagnostic: string | null;
  warning: string | null;
}

interface StyleRequest {
  backend: BackendGateway;
  backendRevision: number;
  generation: number;
  requestID: string;
  controller: AbortController;
}

export interface OSMCacheResult {
  directory: string;
  bytes: number;
  canClear: boolean;
  cleared: boolean;
}

interface CacheState {
  backend: BackendGateway;
  backendRevision: number;
  status: 'checking' | 'ready' | 'clearing' | 'cleared' | 'not-cleared' | 'failed';
  result: OSMCacheResult | null;
  diagnostic: string | null;
  action: 'inspect' | 'clear';
}

interface CacheRequest {
  backend: BackendGateway;
  backendRevision: number;
  generation: number;
  controller: AbortController;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function optionalString(value: unknown): value is string | null | undefined {
  return value === null || value === undefined || typeof value === 'string';
}

/** The request identity belongs to the caller; a response must echo that exact ID. */
export function parseOSMStyleResult(value: unknown, requestID: string): OSMStyleResult {
  if (!isRecord(value) || value.requestID !== requestID || !Array.isArray(value.styles)
    || !optionalString(value.warning)) {
    throw new Error('The OSM style response does not match its original request.');
  }
  const ids = new Set<string>();
  const styles = value.styles.map((entry): OSMStyleOption => {
    if (!isRecord(entry) || typeof entry.id !== 'string' || entry.id.trim().length === 0
      || ids.has(entry.id) || typeof entry.name !== 'string' || typeof entry.description !== 'string'
      || !optionalString(entry.previewContentType) || !optionalString(entry.preview) || !optionalString(entry.warning)) {
      throw new Error('The OSM service returned an invalid style entry.');
    }
    ids.add(entry.id);
    return {
      id: entry.id, name: entry.name, description: entry.description,
      previewContentType: entry.previewContentType ?? null,
      preview: entry.preview ?? null, warning: entry.warning ?? null,
    };
  });
  return { requestID, styles, warning: value.warning ?? null };
}

export function parseOSMCacheResult(value: unknown): OSMCacheResult {
  if (!isRecord(value) || typeof value.directory !== 'string' || value.directory.trim().length === 0
    || typeof value.bytes !== 'number' || !Number.isSafeInteger(value.bytes) || value.bytes < 0
    || typeof value.canClear !== 'boolean' || typeof value.cleared !== 'boolean') {
    throw new Error('The OSM cache response is invalid.');
  }
  return { directory: value.directory, bytes: value.bytes, canClear: value.canClear, cleared: value.cleared };
}

function cacheSize(bytes: number, language: Language): string {
  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
  const unit = bytes > 0 ? Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024))) : 0;
  return `${new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en-US', { maximumFractionDigits: 1 }).format(bytes / 1024 ** unit)} ${units[unit]}`;
}

/** The Host returns byte[] as base64 and can use image/* for an untyped preview. */
export function osmStylePreviewSource(style: OSMStyleOption): string | null {
  if (!style.preview) return null;
  try {
    const header = atob(style.preview.slice(0, 64));
    const declaredType = style.previewContentType?.toLowerCase().split(';')[0].trim();
    let contentType: string | null = null;
    if (header.startsWith('\x89PNG\r\n\x1a\n')) contentType = 'image/png';
    else if (header.startsWith('\xff\xd8\xff')) contentType = 'image/jpeg';
    else if (header.startsWith('GIF87a') || header.startsWith('GIF89a')) contentType = 'image/gif';
    else if (header.startsWith('RIFF') && header.slice(8, 12) === 'WEBP') contentType = 'image/webp';
    else if (header.startsWith('BM')) contentType = 'image/bmp';
    else if (header.slice(4, 8) === 'ftyp' && /avif|avis/.test(header.slice(8))) contentType = 'image/avif';
    else if (declaredType && /^image\/(png|jpeg|gif|webp|bmp|avif|svg\+xml)$/.test(declaredType)) contentType = declaredType;
    return contentType ? `data:${contentType};base64,${style.preview}` : null;
  } catch {
    return null;
  }
}

function StylePreview({ style, language }: { style: OSMStyleOption; language: Language }) {
  const [failedSource, setFailedSource] = useState<string | null>(null);
  const source = osmStylePreviewSource(style);
  return <figure className="osm-style-preview">
    {source && failedSource !== source
      ? <img src={source} alt={text(language, `${style.name || style.id} 样式预览`, `${style.name || style.id} style preview`)}
        onError={() => setFailedSource(source)} />
      : <div className="osm-style-preview-empty">{style.preview
        ? text(language, '预览图片无法显示', 'Preview image could not be displayed')
        : text(language, '此样式未提供预览图片', 'No preview image was provided for this style')}</div>}
    {style.description && <figcaption>{style.description}</figcaption>}
    {style.warning && <details className="osm-style-diagnostic">
      <summary>{text(language, '此样式的预览或说明加载不完整', 'This style has incomplete preview or description data')}</summary>
      <p>{style.warning}</p>
    </details>}
  </figure>;
}

/** React counterpart of OSMStylePicker.cs. Options are exclusively service results. */
export function OSMStylePicker({ backend, backendRevision = 0, language, state, onChange, onCacheCleared }: OSMStylePickerProps) {
  const id = useId();
  const supported = backend.supports('map.background.styles');
  const cacheSupported = backend.supports('map.background.cache');
  const [reload, setReload] = useState(0);
  const [loaded, setLoaded] = useState<StyleLoadState | null>(null);
  const generationRef = useRef(0);
  const activeRequestRef = useRef<StyleRequest | null>(null);
  const identityRef = useRef({ backend, backendRevision, supported, cacheSupported });
  const languageRef = useRef(language);
  const cacheClearedRef = useRef(onCacheCleared);
  const [cache, setCache] = useState<CacheState | null>(null);
  const cacheGenerationRef = useRef(0);
  const cacheRequestRef = useRef<CacheRequest | null>(null);
  // Guard even during the render before the previous request's effect cleanup.
  identityRef.current = { backend, backendRevision, supported, cacheSupported };
  languageRef.current = language;
  cacheClearedRef.current = onCacheCleared;

  useEffect(() => {
    const generation = ++generationRef.current;
    const controller = new AbortController();
    const requestID = `osm-styles-${id}-${generation}-${Date.now().toString(36)}`;
    const capture: StyleRequest = { backend, backendRevision, generation, requestID, controller };
    activeRequestRef.current = capture;
    const current = () => !controller.signal.aborted && activeRequestRef.current === capture
      && generationRef.current === generation && identityRef.current.backend === backend
      && identityRef.current.backendRevision === backendRevision && identityRef.current.supported === supported;
    setLoaded({ backend, backendRevision, status: supported ? 'loading' : 'unavailable', styles: [], diagnostic: null, warning: null });

    if (supported) {
      void backend.request<unknown>('map.background.styles', { requestID }, { signal: controller.signal })
        .then(raw => {
          if (!current()) return;
          const response = parseOSMStyleResult(raw, requestID);
          if (!current()) return;
          setLoaded({ backend, backendRevision,
            status: response.styles.length ? 'ready' : response.warning ? 'failed' : 'empty',
            styles: response.styles, diagnostic: response.styles.length ? null : response.warning,
            warning: response.styles.length ? response.warning : null });
        })
        .catch(error => {
          if (!current()) return;
          setLoaded({ backend, backendRevision, status: 'failed', styles: [],
            diagnostic: message(error, languageRef.current), warning: null });
        });
    }
    return () => {
      if (activeRequestRef.current === capture) activeRequestRef.current = null;
      if (generationRef.current === generation) generationRef.current++;
      controller.abort();
    };
  }, [backend, backendRevision, supported, reload, id]);

  useEffect(() => () => {
    cacheGenerationRef.current++;
    cacheRequestRef.current?.controller.abort();
    cacheRequestRef.current = null;
  }, [backend, backendRevision, cacheSupported]);

  const visible = loaded?.backend === backend && loaded.backendRevision === backendRevision ? loaded : null;
  const status: StyleLoadStatus = supported ? visible?.status ?? 'loading' : 'unavailable';
  const styles = supported ? visible?.styles ?? [] : [];
  const selected = styles.find(style => style.id === state.OsmStyle) ?? null;
  const visibleCache = cache?.backend === backend && cache.backendRevision === backendRevision && cacheSupported ? cache : null;
  const cacheBusy = visibleCache?.status === 'checking' || visibleCache?.status === 'clearing';
  const canClearCache = visibleCache?.result?.canClear === true
    && (visibleCache.status === 'ready' || visibleCache.status === 'cleared');
  const inspectCache = async (clear: boolean) => {
    if (!cacheSupported || cacheRequestRef.current || (clear && !canClearCache)) return;
    const generation = ++cacheGenerationRef.current;
    const controller = new AbortController();
    const capture: CacheRequest = { backend, backendRevision, generation, controller };
    cacheRequestRef.current = capture;
    const previousResult = visibleCache?.result ?? null;
    const current = () => !controller.signal.aborted && cacheRequestRef.current === capture
      && cacheGenerationRef.current === generation && identityRef.current.backend === backend
      && identityRef.current.backendRevision === backendRevision && identityRef.current.cacheSupported;
    setCache({ backend, backendRevision, status: clear ? 'clearing' : 'checking', result: previousResult,
      diagnostic: null, action: clear ? 'clear' : 'inspect' });
    try {
      const raw = await backend.request<unknown>('map.background.cache', { clear }, { signal: controller.signal });
      if (!current()) return;
      const result = parseOSMCacheResult(raw);
      if ((clear && result.directory !== previousResult?.directory) || (!clear && result.cleared)) {
        throw new Error('The OSM cache receipt does not match the requested cache operation.');
      }
      const cleared = clear && result.cleared && result.bytes === 0;
      setCache({ backend, backendRevision, status: clear ? cleared ? 'cleared' : 'not-cleared' : 'ready', result,
        diagnostic: null, action: clear ? 'clear' : 'inspect' });
      if (cleared && current()) cacheClearedRef.current?.();
    } catch (error) {
      if (current()) setCache({ backend, backendRevision, status: 'failed', result: previousResult,
        diagnostic: message(error, languageRef.current), action: clear ? 'clear' : 'inspect' });
    } finally {
      if (cacheRequestRef.current === capture) cacheRequestRef.current = null;
    }
  };
  const cancel = () => {
    const capture = activeRequestRef.current;
    if (!capture || capture.backend !== backend || capture.backendRevision !== backendRevision) return;
    generationRef.current++;
    activeRequestRef.current = null;
    capture.controller.abort();
    setLoaded({ backend, backendRevision, status: 'cancelled', styles: [], diagnostic: null, warning: null });
  };
  const reloadStyles = () => {
    generationRef.current++;
    activeRequestRef.current?.controller.abort();
    activeRequestRef.current = null;
    setLoaded({ backend, backendRevision, status: 'loading', styles: [], diagnostic: null, warning: null });
    setReload(value => value + 1);
  };
  const statusText = status === 'loading' ? text(language, '正在加载开放街图样式…', 'Loading OpenStreetMap styles…')
    : status === 'failed' ? text(language, '开放街图样式加载失败。', 'OpenStreetMap styles could not be loaded.')
    : status === 'empty' ? text(language, '样式服务没有返回可用样式。', 'The style service returned no available styles.')
    : status === 'cancelled' ? text(language, '已取消样式加载。', 'Style loading was cancelled.')
    : status === 'unavailable' ? text(language, '当前服务尚未提供底图样式列表。', 'This service does not provide a background style list.')
    : text(language, `已加载 ${styles.length} 个服务端样式`, `${styles.length} service styles loaded`);

  return <div className="osm-style-picker" data-testid="osm-style-picker">
    <label className="osm-style-select" htmlFor={`${id}-style`}>
      <span>{text(language, '开放街图样式', 'OpenStreetMap style')}</span>
      <select id={`${id}-style`} value={selected?.id ?? ''} disabled={status !== 'ready'}
        onChange={event => {
          const option = styles.find(style => style.id === event.currentTarget.value);
          if (option) onChange({ OsmStyle: option.id });
        }}>
        {!selected && <option value="" disabled>{text(language, '请选择服务端样式', 'Select a service style')}</option>}
        {styles.map(style => <option key={style.id} value={style.id}>{style.name || style.id}</option>)}
      </select>
    </label>
    <div className="osm-style-load-state" data-state={status} aria-busy={status === 'loading'}>
      <p role="status">{statusText}</p>
      {status === 'loading'
        ? <button type="button" onClick={cancel}>{text(language, '取消加载', 'Cancel loading')}</button>
        : supported && <button type="button" onClick={reloadStyles}>{status === 'ready'
          ? text(language, '刷新样式', 'Refresh styles') : text(language, '重试', 'Retry')}</button>}
    </div>
    {status === 'ready' && !selected && <p className="osm-style-notice">{text(language,
      `当前样式“${state.OsmStyle}”未在服务列表中，请选择可用样式。`,
      `The current style “${state.OsmStyle}” is absent from the service list. Select an available style.`)}</p>}
    {visible?.diagnostic && <details className="osm-style-diagnostic">
      <summary>{text(language, '查看加载诊断', 'Show loading diagnostics')}</summary>
      <p>{visible.diagnostic}</p>
    </details>}
    {visible?.warning && <details className="osm-style-diagnostic">
      <summary>{text(language, '部分样式预览或说明加载不完整', 'Some style previews or descriptions are incomplete')}</summary>
      <p>{visible.warning}</p>
    </details>}
    {selected && <StylePreview key={selected.id} style={selected} language={language} />}
    <label className="osm-style-density" htmlFor={`${id}-density`}>
      <span>{text(language, '地图细节密度', 'Map detail density')}<output htmlFor={`${id}-density`}>{state.Density}</output></span>
      <input id={`${id}-density`} type="range" min={-1} max={5} step={1} value={state.Density}
        onChange={event => onChange({ Density: Math.max(-1, Math.min(5, Math.round(Number(event.currentTarget.value)))) })} />
      <span className="osm-style-density-scale"><span>{text(language, '较少（-1）', 'Less (−1)')}</span><span>{text(language, '较多（5）', 'More (5)')}</span></span>
    </label>
    <label className="map-setting-row osm-style-labels">
      <input type="checkbox" checked={state.ShowLabels} onChange={event => onChange({ ShowLabels: event.currentTarget.checked })} />
      <span>{text(language, '显示开放街图标注', 'Show OpenStreetMap labels')}</span>
    </label>
    <details className="osm-cache-panel">
      <summary>{text(language, '服务端开放街图缓存', 'Server OpenStreetMap cache')}</summary>
      <p className="osm-cache-help">{text(language, '检查服务端瓦片缓存的目录和大小；清理后将重新下载所需瓦片。',
        'Inspect the server tile-cache directory and size. Required tiles are downloaded again after clearing.')}</p>
      {visibleCache?.result && <dl className="osm-cache-values">
        <dt>{text(language, '服务端目录', 'Server directory')}</dt>
        <dd><code>{visibleCache.result.directory}</code></dd>
        <dt>{text(language, '检查时大小', 'Size at inspection')}</dt>
        <dd title={`${visibleCache.result.bytes} B`}>{cacheSize(visibleCache.result.bytes, language)}</dd>
      </dl>}
      <p role="status" className="osm-cache-status" aria-busy={cacheBusy}>
        {!cacheSupported ? text(language, '当前服务未提供缓存管理。', 'This service does not provide cache management.')
          : !visibleCache ? text(language, '尚未检查缓存。', 'The cache has not been inspected.')
          : visibleCache.status === 'checking' ? text(language, '正在检查缓存…', 'Inspecting cache…')
          : visibleCache.status === 'clearing' ? text(language, '正在清理缓存…', 'Clearing cache…')
          : visibleCache.status === 'cleared' ? text(language, '服务端已确认缓存清理完成。', 'The server confirmed that the cache was cleared.')
          : visibleCache.status === 'not-cleared' ? text(language, '服务端未确认缓存已清空，请重新检查。', 'The server did not confirm an empty cache. Inspect it again.')
          : visibleCache.status === 'failed' ? visibleCache.action === 'clear'
            ? text(language, '缓存清理结果未确认，请重新检查缓存。', 'The cache-clear outcome is unconfirmed. Inspect the cache again.')
            : text(language, '缓存检查失败，请重试。', 'Cache inspection failed. Try again.')
          : visibleCache.result?.canClear ? text(language, '缓存已检查，可以清理。', 'The cache was inspected and can be cleared.')
          : text(language, '服务端没有可清理的缓存目录。', 'The server has no cache directory available to clear.')}
      </p>
      {visibleCache?.diagnostic && <details className="osm-style-diagnostic">
        <summary>{text(language, '查看缓存诊断', 'Show cache diagnostics')}</summary><p>{visibleCache.diagnostic}</p>
      </details>}
      <div className="osm-cache-actions">
        <button type="button" disabled={!cacheSupported || cacheBusy} onClick={() => { void inspectCache(false); }}>
          {text(language, '检查缓存', 'Inspect cache')}</button>
        <button type="button" disabled={!cacheSupported || cacheBusy || !canClearCache} onClick={() => { void inspectCache(true); }}>
          {text(language, '清理服务端缓存', 'Clear server cache')}</button>
      </div>
    </details>
  </div>;
}
