import { useEffect, useRef, useState } from 'react';
import { CumulusLoginWindow } from './CumulusLoginWindow';
import type { EditorPageProps } from './model';
import type {
  CumulusEnvironmentOption, CumulusGridFootprintResult, CumulusGridImportCommand, CumulusGridSourceOption,
  CumulusProductOption, CumulusSessionToken,
} from './cumulusTypes';
import { GriddedDataImporterTimeline } from './GriddedDataImporterTimeline';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';

type CumulusLayerID = Exclude<SpatialBoundaryLayerID, 'evapotranspiration'>;

const utcToday = (): string => new Date().toISOString().slice(0, 10);

function addUtcMonthsClamped(date: Date, months: number): Date {
  const monthIndex = date.getUTCFullYear() * 12 + date.getUTCMonth() + months;
  const year = Math.floor(monthIndex / 12);
  const month = monthIndex - year * 12;
  const day = Math.min(date.getUTCDate(), new Date(Date.UTC(year, month + 1, 0)).getUTCDate());
  return new Date(Date.UTC(year, month, day, date.getUTCHours(), date.getUTCMinutes(), date.getUTCSeconds(), date.getUTCMilliseconds()));
}

function utcRange(afterDate: string, beforeDate: string): { afterUtc: string; beforeUtc: string } | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(afterDate) || !/^\d{4}-\d{2}-\d{2}$/.test(beforeDate)) return null;
  const after = new Date(`${afterDate}T00:00:00.000Z`);
  const before = new Date(`${beforeDate}T23:59:59.999Z`);
  if (!Number.isFinite(after.getTime()) || !Number.isFinite(before.getTime()) || before <= after) return null;
  if (before.getTime() > addUtcMonthsClamped(after, 6).getTime()) return null;
  return { afterUtc: after.toISOString(), beforeUtc: before.toISOString() };
}

function layerName(layerId: CumulusLayerID, language: EditorPageProps['language']): string {
  const names: Record<CumulusLayerID, [string, string]> = {
    precipitation: ['降水', 'Precipitation'], wind: ['风', 'Wind'],
    airPressure: ['气压', 'Air pressure'], airDensity: ['空气密度', 'Air density'],
  };
  return names[layerId][language === 'zh' ? 0 : 1];
}

function validEnvironmentList(value: CumulusEnvironmentOption[]): boolean {
  return Array.isArray(value) && value.every((item) => typeof item.id === 'string' && item.id.length > 0
    && typeof item.name === 'string' && typeof item.requiresSignIn === 'boolean'
    && typeof item.isSelectable === 'boolean');
}

function validProductList(value: CumulusProductOption[]): boolean {
  return Array.isArray(value) && value.every((item) => typeof item.id === 'string' && item.id.length > 0
    && typeof item.name === 'string' && typeof item.isPrecip === 'boolean');
}

function validSourceList(value: CumulusGridSourceOption[]): boolean {
  return Array.isArray(value) && value.every((item) => typeof item.id === 'string' && item.id.length > 0
    && typeof item.datetimeUtc === 'string' && /Z$/i.test(item.datetimeUtc) && Number.isFinite(Date.parse(item.datetimeUtc))
    && typeof item.versionUtc === 'string' && /Z$/i.test(item.versionUtc) && Number.isFinite(Date.parse(item.versionUtc)))
    && new Set(value.map((item) => item.id)).size === value.length;
}

function progressText(value: unknown, language: EditorPageProps['language']): string {
  if (!value || typeof value !== 'object') return '';
  const record = value as { taskName?: unknown; message?: unknown; taskMessage?: unknown; fraction?: unknown };
  const rawMessage = typeof record.message === 'string' ? record.message
    : typeof record.taskMessage === 'string' ? record.taskMessage
      : typeof record.taskName === 'string' ? record.taskName : '';
  const fraction = typeof record.fraction === 'number' && Number.isFinite(record.fraction)
    ? ` ${Math.round(Math.max(0, Math.min(1, record.fraction)) * 100)}%` : '';
  if (!rawMessage) return fraction;
  const translated = language === 'zh' ? rawMessage
    .replace('Resolving the selected Cumulus grid sources…', '正在核对所选 Cumulus 格网…')
    .replace('Preparing the Cumulus grid import…', '正在准备 Cumulus 格网导入…')
    .replace('Applying selected Cumulus grids to the captured Boundary Condition draft…', '正在将所选 Cumulus 格网写入当前边界条件…')
    .replace('Cumulus grid import complete.', 'Cumulus 格网导入已完成。')
    : rawMessage;
  return `${translated}${fraction}`;
}

/** Cumulus catalog/query/coverage and immediate-save flow from the original source panel. */
export function CumulusImportSource(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onClose: () => void;
  onBusyChange?: (busy: boolean) => void;
}) {
  const isSupportedLayer = props.layerId !== 'evapotranspiration';
  const layerId = isSupportedLayer ? props.layerId as CumulusLayerID : null;
  const workflow = props.cumulusImport;
  const workflowRef = useRef(workflow);
  workflowRef.current = workflow;
  const [environments, setEnvironments] = useState<CumulusEnvironmentOption[]>([]);
  const [selectedEnvironmentId, setSelectedEnvironmentId] = useState('');
  const [sessionToken, setSessionToken] = useState<CumulusSessionToken | null>(null);
  const [sessionEnvironmentId, setSessionEnvironmentId] = useState('');
  const [products, setProducts] = useState<CumulusProductOption[]>([]);
  const [selectedProductId, setSelectedProductId] = useState('');
  const [afterDate, setAfterDate] = useState(() => {
    const date = new Date(); date.setUTCDate(date.getUTCDate() - 7); return date.toISOString().slice(0, 10);
  });
  const [beforeDate, setBeforeDate] = useState(utcToday);
  const [sources, setSources] = useState<CumulusGridSourceOption[]>([]);
  const [selectedSourceIds, setSelectedSourceIds] = useState<string[]>([]);
  const [activeSourceId, setActiveSourceId] = useState('');
  const [footprint, setFootprint] = useState<CumulusGridFootprintResult | null>(null);
  const [precipitationDataType, setPrecipitationDataType] = useState<'per-cum' | 'per-avg'>('per-cum');
  const [precipitationUnits, setPrecipitationUnits] = useState<'mm' | 'in'>('mm');
  const [confirming, setConfirming] = useState(false);
  const [loadingEnvironments, setLoadingEnvironments] = useState(true);
  const [loadingProducts, setLoadingProducts] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [importProgress, setImportProgress] = useState('');
  const activeRead = useRef<AbortController | null>(null);
  const environmentRequest = useRef<AbortController | null>(null);
  const importInProgress = useRef(false);
  const readGeneration = useRef(0);
  const ownerKey = `${props.asset.kind}\u0000${props.asset.path}\u0000${props.asset.revision ?? ''}\u0000${props.snapshot.revision}\u0000${props.snapshot.sourceState ?? ''}`;
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  const selectedEnvironment = environments.find((item) => item.id === selectedEnvironmentId) ?? null;
  const selectedProduct = products.find((item) => item.id === selectedProductId) ?? null;
  const selectedSources = sources.filter((item) => selectedSourceIds.includes(item.id));
  const activeSource = sources.find((item) => item.id === activeSourceId) ?? selectedSources[0] ?? null;
  const range = utcRange(afterDate, beforeDate);
  const busy = actionBusy || loadingEnvironments || loadingProducts;
  const authSupported = !!props.backend?.supports('cumulus.token.exchange');
  const tokenIsCurrent = !!sessionToken && !!selectedEnvironment && sessionEnvironmentId === selectedEnvironment.id;
  const tokenExpired = tokenIsCurrent && sessionToken?.kind === 'authenticated' && !!sessionToken.accessExpiresUtc
    && Date.parse(sessionToken.accessExpiresUtc) <= Date.now() + 30_000;
  const canQuery = !!workflow && !!selectedEnvironment && selectedEnvironment.isSelectable && tokenIsCurrent && !tokenExpired
    && !!selectedProductId && !!range && !busy && !loadingProducts;
  const sourceValidity = sources.every((item) => item.id.trim().length > 0 && Number.isFinite(Date.parse(item.datetimeUtc)) && Number.isFinite(Date.parse(item.versionUtc)));
  const distinctTimesteps = new Set(selectedSources.map((item) => Date.parse(item.datetimeUtc))).size === selectedSources.length;
  const canImport = !!workflow?.canImport && !!selectedEnvironment && !!sessionToken && tokenIsCurrent && !tokenExpired
    && !!selectedProduct && !!range && selectedSources.length > 0 && sourceValidity && distinctTimesteps
    && isSupportedLayer && !busy;

  const reportBusy = setActionBusy;

  const resetQuery = () => {
    setSources([]);
    setSelectedSourceIds([]);
    setActiveSourceId('');
    setFootprint(null);
    setConfirming(false);
    setStatus('');
  };

  const cancelRead = () => {
    activeRead.current?.abort();
    activeRead.current = null;
    readGeneration.current++;
  };

  const runRead = async <T,>(operation: (signal: AbortSignal) => Promise<T>, accept: (value: T) => void): Promise<void> => {
    cancelRead();
    const controller = new AbortController();
    const sequence = readGeneration.current;
    activeRead.current = controller;
    reportBusy(true);
    setError('');
    setStatus('');
    try {
      const value = await operation(controller.signal);
      if (!controller.signal.aborted && activeRead.current === controller && readGeneration.current === sequence) accept(value);
    } catch (cause) {
      if (!controller.signal.aborted && activeRead.current === controller && readGeneration.current === sequence)
        setError(cause instanceof Error ? cause.message : label('读取 Cumulus 数据失败。', 'Could not read Cumulus data.'));
    } finally {
      if (activeRead.current === controller && readGeneration.current === sequence) {
        activeRead.current = null;
        reportBusy(false);
      }
    }
  };

  useEffect(() => {
    const controller = new AbortController();
    environmentRequest.current = controller;
    const currentWorkflow = workflowRef.current;
    setLoadingEnvironments(true);
    setError('');
    if (!currentWorkflow) {
      setError(label('当前服务未提供 Cumulus 数据目录。', 'The connected service does not provide the Cumulus catalog.'));
      setLoadingEnvironments(false);
      return () => controller.abort();
    }
    void currentWorkflow.loadEnvironments(controller.signal).then((value) => {
      if (controller.signal.aborted) return;
      if (!validEnvironmentList(value)) throw new Error(label('Cumulus 环境目录返回了不完整的数据。', 'The Cumulus environment catalog is incomplete.'));
      setEnvironments(value);
      const firstSelectable = value.find((item) => item.isSelectable);
      setSelectedEnvironmentId((current) => value.some((item) => item.id === current && item.isSelectable)
        ? current : firstSelectable?.id ?? '');
    }).catch((cause) => {
      if (!controller.signal.aborted)
        setError(cause instanceof Error ? cause.message : label('读取 Cumulus 环境失败。', 'Could not load Cumulus environments.'));
    }).finally(() => {
      if (!controller.signal.aborted) {
        if (environmentRequest.current === controller) environmentRequest.current = null;
        setLoadingEnvironments(false);
      }
    });
    return () => {
      controller.abort();
      if (environmentRequest.current === controller) environmentRequest.current = null;
      props.onBusyChange?.(false);
    };
  // Each source dialog owns one source identity; callbacks re-read through the latest workflow ref.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ownerKey, props.layerId]);

  useEffect(() => {
    if (!selectedEnvironment || !tokenIsCurrent || !sessionToken || tokenExpired || !workflowRef.current) {
      setProducts([]);
      setSelectedProductId('');
      setLoadingProducts(false);
      return;
    }
    const controller = new AbortController();
    const sequence = ++readGeneration.current;
    activeRead.current?.abort();
    activeRead.current = controller;
    const currentWorkflow = workflowRef.current;
    setLoadingProducts(true);
    reportBusy(true);
    setProducts([]);
    setSelectedProductId('');
    resetQuery();
    setError('');
    void currentWorkflow.loadProducts(selectedEnvironment.id, sessionToken.accessToken, controller.signal).then((value) => {
      if (controller.signal.aborted || readGeneration.current !== sequence) return;
      if (!validProductList(value)) throw new Error(label('Cumulus 产品目录返回了不完整的数据。', 'The Cumulus product catalog is incomplete.'));
      setProducts(value);
      setStatus(value.length
        ? label(`已读取 ${value.length} 个产品。`, `Loaded ${value.length} products.`)
        : label('该环境当前没有可用产品。', 'No products are available in this environment.'));
    }).catch((cause) => {
      if (!controller.signal.aborted && readGeneration.current === sequence)
        setError(cause instanceof Error ? cause.message : label('读取 Cumulus 产品失败。', 'Could not load Cumulus products.'));
    }).finally(() => {
      if (!controller.signal.aborted && readGeneration.current === sequence) {
        activeRead.current = null;
        setLoadingProducts(false);
        reportBusy(false);
      }
    });
    return () => {
      controller.abort();
      if (activeRead.current === controller) activeRead.current = null;
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedEnvironmentId, sessionToken?.accessToken, sessionEnvironmentId, tokenExpired, ownerKey]);

  useEffect(() => () => {
    cancelRead();
    environmentRequest.current?.abort();
    props.onBusyChange?.(false);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    props.onBusyChange?.(busy);
  }, [busy, props.onBusyChange]);

  const selectEnvironment = (id: string) => {
    cancelRead();
    reportBusy(false);
    setSelectedEnvironmentId(id);
    setSessionToken(null);
    setSessionEnvironmentId('');
    setProducts([]);
    setSelectedProductId('');
    resetQuery();
    setError('');
  };

  const connectMockEnvironment = () => {
    if (!selectedEnvironment || selectedEnvironment.requiresSignIn || !selectedEnvironment.mockAccessToken) return;
    setSessionToken({ accessToken: selectedEnvironment.mockAccessToken, refreshToken: null, accessExpiresUtc: null, refreshExpiresUtc: null, kind: 'mock' });
    setSessionEnvironmentId(selectedEnvironment.id);
    setError('');
    setStatus(label('已连接 Host 提供的本地模拟数据；这不是 Cumulus 账户。', 'Connected to Host-provided local mock data; this is not a Cumulus account.'));
  };

  const signOut = () => {
    cancelRead();
    setSessionToken(null);
    setSessionEnvironmentId('');
    setProducts([]);
    setSelectedProductId('');
    resetQuery();
    setStatus(label('已清除当前页面内存中的令牌。', 'The in-memory token for this page was cleared.'));
  };

  const queryGrids = () => {
    const currentWorkflow = workflowRef.current;
    if (!currentWorkflow || !selectedEnvironment || !sessionToken || !selectedProductId || !range || !canQuery) return;
    resetQuery();
    void runRead(
      (signal) => currentWorkflow.queryGrids(selectedEnvironment.id, sessionToken.accessToken, selectedProductId, range.afterUtc, range.beforeUtc, signal),
      (value) => {
        if (!validSourceList(value)) throw new Error(label('Cumulus 格网目录缺少稳定 ID 或完整 UTC 时间/版本。', 'Cumulus grid results must include a stable ID, UTC time, and version.'));
        setSources(value);
        setActiveSourceId(value[0]?.id ?? '');
        setStatus(value.length
          ? label(`已读取 ${value.length} 条格网记录。请逐条选择，并保留 UTC 时间与版本。`, `Loaded ${value.length} grids. Select source records and retain their UTC times and versions.`)
          : label('该产品在所选 UTC 时间范围内没有格网。', 'No grids were returned for this product and UTC range.'));
      },
    );
  };

  const toggleSource = (source: CumulusGridSourceOption, checked: boolean) => {
    setFootprint(null);
    setConfirming(false);
    setActiveSourceId(source.id);
    setSelectedSourceIds((current) => {
      if (!checked) return current.filter((id) => id !== source.id);
      const timestamp = Date.parse(source.datetimeUtc);
      const withoutOtherVersions = current.filter((id) => {
        const existing = sources.find((item) => item.id === id);
        return !existing || Date.parse(existing.datetimeUtc) !== timestamp;
      });
      return withoutOtherVersions.includes(source.id) ? withoutOtherVersions : [...withoutOtherVersions, source.id];
    });
  };

  const readFootprint = () => {
    const currentWorkflow = workflowRef.current;
    if (!currentWorkflow || !selectedEnvironment || !sessionToken || !selectedProductId || !range || !activeSource || busy) return;
    setFootprint(null);
    void runRead(async (signal) => {
      const context = await currentWorkflow.loadProjectMapContext(signal);
      if (!context.projectionWkt?.trim()) throw new Error(label('当前工程目录没有可验证的 Project/ProjectionWKT；无法安全转换该格网覆盖范围。', 'The active project catalog has no verified Project/ProjectionWKT, so the grid footprint cannot be transformed safely.'));
      return currentWorkflow.inspectFootprint(selectedEnvironment.id, sessionToken.accessToken, selectedProductId,
        range.afterUtc, range.beforeUtc, activeSource, context.projectionWkt, signal);
    }, setFootprint);
  };

  const importSelected = async () => {
    const currentWorkflow = workflowRef.current;
    if (!currentWorkflow || !canImport || importInProgress.current || !selectedEnvironment || !sessionToken || !selectedProduct || !range || !layerId) return;
    const chosen = selectedSources.map(({ id, datetimeUtc, versionUtc }) => ({ id, datetimeUtc, versionUtc }))
      .sort((left, right) => Date.parse(left.datetimeUtc) - Date.parse(right.datetimeUtc));
    const command: CumulusGridImportCommand = {
      environmentId: selectedEnvironment.id,
      accessToken: sessionToken.accessToken,
      productId: selectedProduct.id,
      afterUtc: range.afterUtc,
      beforeUtc: range.beforeUtc,
      sources: chosen,
      layerId,
      ...(currentWorkflow.clipArea ? {
        clipExtent: currentWorkflow.clipArea.extent,
        clipProjectionWkt: currentWorkflow.clipArea.projectionWkt,
      } : {}),
      precipitationDataType,
      precipitationUnits,
    };
    importInProgress.current = true;
    setError('');
    setImportProgress(label('正在提交导入事务…', 'Submitting import transaction…'));
    reportBusy(true);
    try {
      const accepted = await currentWorkflow.importGrids(command, (progress) => setImportProgress(progressText(progress, props.language)));
      if (accepted) props.onClose();
      else setError(label('工作台未确认 Cumulus 导入回执；请检查工作台状态。', 'The workbench did not confirm the Cumulus import receipt. Check its status.'));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : label('Cumulus 格网导入失败。', 'Cumulus grid import failed.'));
    } finally {
      importInProgress.current = false;
      setImportProgress('');
      reportBusy(false);
    }
  };

  const timelineItems = sources.map((item) => ({
    id: item.id, time: item.datetimeUtc, issueTime: item.versionUtc,
    detail: item.id, included: selectedSourceIds.includes(item.id),
  }));
  const authTokenVisible = tokenIsCurrent && !!sessionToken;
  const supportedDataLayer = layerId ? layerName(layerId, props.language) : label('蒸散发', 'Evapotranspiration');

  return <section className="editor-table-card" aria-label={label('Cumulus 格网来源', 'Cumulus grid source')}>
    <header className="editor-meteorology-layer-header">
      <div><p className="editor-eyebrow">{label('空间边界条件', 'Spatial boundary condition')}</p>
        <h3>{label('Cumulus 数据', 'Cumulus data')}</h3></div>
      <button type="button" className="editor-button" disabled={busy} onClick={props.onClose}>{label('关闭', 'Close')}</button>
    </header>

    {!isSupportedLayer && <p className="editor-field-muted" role="status">{label('蒸散发暂不支持格网导入，请使用点输入。', 'Evapotranspiration does not support grid import. Use point input.')}</p>}
    {!workflow && isSupportedLayer && <p className="editor-field-muted" role="status">{label('当前服务未提供 Cumulus 环境与数据目录操作。', 'The connected service does not provide Cumulus environment and catalog operations.')}</p>}

    {workflow && isSupportedLayer && <>
      <p className="editor-field-muted">{label('选择服务环境、产品和 UTC 日期范围。导入会立即保存当前边界条件；有未保存草稿时，工作台会阻止提交。', 'Choose a service environment, product, and UTC date range. Import saves the current boundary condition immediately; the workbench blocks it while unsaved edits exist.')}</p>
      <div className="editor-field-grid">
        <label className="editor-field"><span>{label('服务环境', 'Environment')}</span>
          <select data-testid="cumulus-environment" value={selectedEnvironmentId} disabled={busy || loadingEnvironments} onChange={(event) => selectEnvironment(event.currentTarget.value)}>
            <option value="">{loadingEnvironments ? label('正在读取环境…', 'Loading environments…') : label('选择服务环境', 'Choose environment')}</option>
            {environments.map((environment) => <option key={environment.id} value={environment.id} disabled={!environment.isSelectable}>
              {environment.name}{environment.isSelectable ? '' : label('（不可用）', ' (unavailable)')}
            </option>)}
          </select>
        </label>
        <label className="editor-field"><span>{label('产品', 'Product')}</span>
          <select data-testid="cumulus-product" value={selectedProductId} disabled={busy || loadingProducts || !authTokenVisible} onChange={(event) => {
            setSelectedProductId(event.currentTarget.value); resetQuery(); setError('');
          }}>
            <option value="">{loadingProducts ? label('正在读取产品…', 'Loading products…') : label('选择产品', 'Choose product')}</option>
            {products.map((product) => <option key={product.id} value={product.id}>{product.name} · {product.parameter ?? product.slug ?? product.id}</option>)}
          </select>
        </label>
      </div>

      {selectedEnvironment?.requiresSignIn && authSupported && props.backend && <CumulusLoginWindow
        environment={selectedEnvironment}
        backend={props.backend}
        busy={busy}
        language={props.language}
        onAuthenticated={(token) => {
          setSessionToken(token); setSessionEnvironmentId(selectedEnvironment.id); setError(''); resetQuery();
          setStatus(label('已连接所选 Cumulus 环境。令牌仅保存在当前页面内存。', 'Connected to the selected Cumulus environment. The token stays in this page’s memory.'));
        }}
        onError={setError}
        onSignedOut={signOut}
      />}
      {selectedEnvironment && !selectedEnvironment.requiresSignIn && <div className="editor-workbench-actions">
        {authTokenVisible
          ? <span className="editor-field-muted" role="status">{sessionToken?.kind === 'mock'
            ? label('本地模拟数据已连接；不代表真实 Cumulus 账户。', 'Local mock data is connected; this is not a real Cumulus account.')
            : label('已连接 Cumulus 环境。', 'Cumulus environment connected.')}</span>
          : <button type="button" className="editor-button" disabled={busy || !selectedEnvironment.mockAccessToken} onClick={connectMockEnvironment}>
            {label('连接本地模拟数据', 'Connect local mock data')}
          </button>}
        {authTokenVisible && <button type="button" className="editor-button" disabled={busy} onClick={signOut}>{label('断开', 'Disconnect')}</button>}
      </div>}
      {selectedEnvironment?.requiresSignIn && !authSupported && <p className="editor-field-muted" role="status">{label('当前服务未发布令牌交换接口，无法连接此账户环境。', 'The connected service has not advertised token exchange for this account environment.')}</p>}
      {authTokenVisible && sessionToken?.kind === 'authenticated' && tokenExpired && <p className="editor-field-error" role="alert">{label('访问令牌即将过期或已过期。请在登录面板刷新令牌后继续。', 'The access token is expired or about to expire. Refresh it in the sign-in panel before continuing.')}</p>}

      <div className="editor-field-grid">
        <label className="editor-field"><span>{label('开始日期（UTC）', 'Start date (UTC)')}</span>
          <input data-testid="cumulus-date-start" type="date" value={afterDate} disabled={busy} onChange={(event) => { setAfterDate(event.currentTarget.value); resetQuery(); setError(''); }} />
        </label>
        <label className="editor-field"><span>{label('结束日期（UTC，含当日）', 'End date (UTC, inclusive)')}</span>
          <input data-testid="cumulus-date-end" type="date" value={beforeDate} disabled={busy} onChange={(event) => { setBeforeDate(event.currentTarget.value); resetQuery(); setError(''); }} />
        </label>
        <button data-testid="cumulus-grid-query" type="button" className="editor-button" disabled={!canQuery} onClick={queryGrids}>
          {busy && !importProgress ? label('读取中…', 'Loading…') : label('查询格网', 'Query grids')}
        </button>
      </div>
      {!range && <p className="editor-field-error" role="alert">{label('日期范围必须有效，结束时间晚于开始时间，且不超过六个月。', 'Choose a valid range with an end after the start and no more than six months.')}</p>}
      {sessionToken?.kind === 'mock' && selectedEnvironment?.id === sessionEnvironmentId && <p className="editor-field-muted">{label('本地模拟令牌由 Host 环境目录提供，没有账户登录或到期时间。', 'The local mock token came from the Host environment catalog and has no account sign-in or expiry.')}</p>}

      {selectedProduct && <section className="editor-confirmation-panel" aria-label={label('产品信息', 'Product details')}>
        <h4>{selectedProduct.name}</h4>
        <p className="editor-field-muted">{[
          selectedProduct.parameter ? `${label('参数', 'Parameter')}: ${selectedProduct.parameter}` : '',
          selectedProduct.unit ? `${label('源单位', 'Source unit')}: ${selectedProduct.unit}` : '',
          selectedProduct.temporalResolution ? `${label('时间分辨率', 'Temporal resolution')}: ${selectedProduct.temporalResolution}` : '',
          selectedProduct.temporalDuration ? `${label('持续时间', 'Duration')}: ${selectedProduct.temporalDuration}` : '',
          selectedProduct.dssDatatype ? `${label('DSS 类型', 'DSS type')}: ${selectedProduct.dssDatatype}` : '',
          selectedProduct.afterUtc ? `${label('可用起始 UTC', 'Available from UTC')}: ${selectedProduct.afterUtc}` : '',
          selectedProduct.beforeUtc ? `${label('可用结束 UTC', 'Available through UTC')}: ${selectedProduct.beforeUtc}` : '',
        ].filter(Boolean).join(' · ')}</p>
      </section>}

      {props.layerId === 'precipitation' && <div className="editor-field-grid">
        <label className="editor-field"><span>{label('降水数据类型', 'Precipitation data type')}</span>
          <select value={precipitationDataType} disabled={busy} onChange={(event) => setPrecipitationDataType(event.currentTarget.value as 'per-cum' | 'per-avg')}>
            <option value="per-cum">per-cum</option><option value="per-avg">per-avg</option>
          </select>
        </label>
        <label className="editor-field"><span>{label('降水单位', 'Precipitation units')}</span>
          <select value={precipitationUnits} disabled={busy} onChange={(event) => setPrecipitationUnits(event.currentTarget.value as 'mm' | 'in')}>
            <option value="mm">mm</option><option value="in">in</option>
          </select>
        </label>
      </div>}

      {sources.length > 0 && <>
        <GriddedDataImporterTimeline language={props.language} items={timelineItems} selectedId={activeSourceId} disabled={busy}
          onSelect={(item) => { setActiveSourceId(item.id); setFootprint(null); setConfirming(false); }} />
        <div className="editor-table-scroll"><table className="editor-data-table">
          <thead><tr><th>{label('导入', 'Include')}</th><th>{label('源 ID', 'Source ID')}</th><th>{label('有效时间（UTC）', 'Valid time (UTC)')}</th><th>{label('版本时间（UTC）', 'Version time (UTC)')}</th></tr></thead>
          <tbody>{sources.map((source) => <tr key={source.id} aria-selected={source.id === activeSourceId}>
            <td><input data-testid="cumulus-grid-source-checkbox" aria-label={`${label('导入源', 'Include source')} ${source.id}`} type="checkbox" checked={selectedSourceIds.includes(source.id)} disabled={busy}
              onChange={(event) => toggleSource(source, event.currentTarget.checked)} /></td>
            <td><button type="button" className="editor-button" disabled={busy} onClick={() => { setActiveSourceId(source.id); setFootprint(null); setConfirming(false); }}><code>{source.id}</code></button></td>
            <td><time dateTime={source.datetimeUtc}>{source.datetimeUtc}</time></td>
            <td><time dateTime={source.versionUtc}>{source.versionUtc}</time></td>
          </tr>)}</tbody>
        </table></div>
        <div className="editor-workbench-actions">
          <button data-testid="cumulus-grid-footprint" type="button" className="editor-button" disabled={!activeSource || !selectedProduct || !range || !tokenIsCurrent || !!tokenExpired || busy} onClick={readFootprint}>
            {label('读取所选格网覆盖范围', 'Read selected grid footprint')}
          </button>
          <span className="editor-field-muted">{label(`${selectedSourceIds.length} 条已选；目标图层：${supportedDataLayer}`, `${selectedSourceIds.length} selected; target layer: ${supportedDataLayer}`)}</span>
        </div>
        {footprint && <section className="editor-confirmation-panel" aria-label={label('格网覆盖范围', 'Grid footprint')}>
          <h4>{label('已在工程坐标系读取覆盖范围', 'Footprint read in the project coordinate system')}</h4>
          <p className="editor-field-muted">{label('源投影与目标投影由 Host 真实读取并转换。此覆盖范围仅供核对；当前导入器没有地图绘制采集，不会把它误当作裁剪区域。', 'The Host read and transformed the source and target projections. This footprint is for review; the importer has no map-drawing capture and will not treat it as a clip area.')}</p>
          <p><code>{footprint.sourceId}</code> · {footprint.datetimeUtc} · {footprint.versionUtc}</p>
          <p>{label('坐标范围', 'Extent')}: X {footprint.extent.minX}–{footprint.extent.maxX}, Y {footprint.extent.minY}–{footprint.extent.maxY}</p>
          <p className="editor-field-muted">{label(`边界点：${footprint.points.length}`, `Boundary points: ${footprint.points.length}`)}</p>
        </section>}
        {workflow.clipArea
          ? <p className="editor-field-muted">{label('已接收带工程坐标系的地图裁剪区域。', 'A map clip area with its captured project coordinate system is available.')}</p>
          : <p className="editor-field-muted">{label('当前按所选格网完整范围导入；地图框选裁剪尚未接入。', 'The selected grids will be imported at full extent. Map-drawn clipping is not connected.')}</p>}
        {!confirming
          ? <button data-testid="cumulus-grid-import" type="button" className="editor-button editor-button-primary" disabled={!canImport} onClick={() => setConfirming(true)}>
            {label('检查导入内容…', 'Review import…')} ({selectedSources.length})
          </button>
          : <section className="editor-confirmation-panel" role="group" aria-label={label('确认 Cumulus 导入', 'Confirm Cumulus import')}>
            <h4>{label('确认立即保存格网？', 'Confirm immediate grid save?')}</h4>
            <p className="editor-field-muted">{label(`产品：${selectedProduct?.name ?? ''}；UTC 时段：${range?.afterUtc ?? ''} 至 ${range?.beforeUtc ?? ''}；源数量：${selectedSources.length}；目标：${supportedDataLayer}。${workflow.clipArea ? '将使用已捕获的工程坐标裁剪范围。' : '将导入完整格网范围。'}请先保存或丢弃其他草稿。`, `Product: ${selectedProduct?.name ?? ''}; UTC range: ${range?.afterUtc ?? ''} to ${range?.beforeUtc ?? ''}; sources: ${selectedSources.length}; target: ${supportedDataLayer}. ${workflow.clipArea ? 'A captured project-coordinate clip area will be used.' : 'Full grid extents will be imported.'} Save or discard other drafts first.`)}</p>
            <p className="editor-field-muted">{label(`时间戳和版本将按服务返回值提交；降水为 ${precipitationDataType} / ${precipitationUnits}。`, `Source timestamps and versions are submitted unchanged; precipitation is ${precipitationDataType} / ${precipitationUnits}.`)}</p>
            {importProgress && <p role="status" aria-live="polite">{importProgress}</p>}
            <div className="editor-workbench-actions">
              <button type="button" className="editor-button" disabled={busy} onClick={() => setConfirming(false)}>{label('返回检查', 'Back to review')}</button>
              <button type="button" className="editor-button editor-button-primary" disabled={!canImport} onClick={() => void importSelected()}>
                {busy ? label('正在导入并保存…', 'Importing and saving…') : label('确认导入并保存', 'Confirm import and save')}
              </button>
            </div>
          </section>}
      </>}
    </>}
    {status && <p className="editor-field-muted" role="status">{status}</p>}
    {error && <p className="editor-field-error" role="alert">{error}</p>}
  </section>;
}
