import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps, Language } from '../../shared/workbench';
import { ResultNodeDetails, type ResultInspectorSnapshot } from './controls/ResultNodeDetails';
import { ResultPicker } from './controls/ResultPicker';
import { TimeSeriesPayloadChart, type ChartSettings } from './controls/TimeSeriesPayloadChart';
import { TimeSeriesPayloadTable } from './controls/TimeSeriesPayloadTable';
import { readResultsDraft, writeResultsDraft, type ResultCellDrafts, type ResultsDraftSnapshot } from './ResultsDraftStore';
import { downloadCsv, type ResultDataset, type ResultSeries } from './data';
import './results.css';

interface ResultProfileCatalog {
  resultPath: string;
  revision: string;
  resultName: string;
  outputBlocks: Array<{
    name: string;
    profileCount: number;
    profileNames: string[];
    times: string[];
    variables: Array<{ id: string; name: string; elementKind: string; quantity: string; units: string }>;
  }>;
  flowAreas: Array<{ index: number; name: string; cellCount: number; faceCount: number }>;
}

interface ResultProfileSeriesResponse {
  available: boolean;
  unavailableReason: string | null;
  outputBlockName: string;
  variable: string;
  flowAreaName: string;
  flowAreaIndex: number;
  elementIndex: number;
  elementKind: string;
  quantity: string;
  units: string;
  points: Array<{ profileIndex: number; profileName: string; time: string; value: number | null }>;
}

interface ActiveProfileRequest {
  controller: AbortController;
  generation: number;
  requestKey: string;
  active: boolean;
}

function isResultAsset(asset: FeaturePageProps['context']['selectedAsset']): boolean {
  if (!asset) return false;
  const kind = asset.kind.trim().toLowerCase();
  if (!kind || kind === 'unknown') return /\.(hdf|h5)$/i.test(asset.path);
  return kind.includes('result');
}

const defaultChartSettings: ChartSettings = { grid: true, markers: false, step: false, yMin: '', yMax: '' };

const resultVariableNamesZh: Readonly<Record<string, string>> = {
  'Water Surface': '水面高程',
  'Cell Depth': '单元水深',
  'Face Velocity': '面速度',
  'Face Flow': '面流量',
};

const resultDimensionNamesZh: Readonly<Record<string, string>> = {
  Length: '长度',
  Velocity: '速度',
  Flow: '流量',
  Unknown: '未知',
};

const meshElementNamesZh: Readonly<Record<string, string>> = {
  Cell: '单元',
  Face: '面',
};

function displayResultVariableName(value: string, language: Language): string {
  return language === 'zh' ? resultVariableNamesZh[value] ?? value : value;
}

function displayResultDimensionName(value: string, language: Language): string {
  return language === 'zh' ? resultDimensionNamesZh[value] ?? value : value;
}

function displayMeshElementName(value: string, language: Language): string {
  return language === 'zh' ? meshElementNamesZh[value] ?? value : value;
}

function emptyResultsDraft(): ResultsDraftSnapshot {
  return { dataset: null, originalDataset: null, dirty: false, cellDrafts: {}, source: 'result' };
}

function parseCellDraft(raw: string): number | null | undefined {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : undefined;
}

function invalidDraftCount(drafts: ResultCellDrafts): number {
  return Object.values(drafts).reduce((count, cells) => count + Object.values(cells).filter((raw) => parseCellDraft(raw) === undefined).length, 0);
}

function datasetsEqual(left: ResultDataset | null, right: ResultDataset | null): boolean {
  if (left === right) return true;
  if (!left || !right || left.name !== right.name || left.source !== right.source || left.sourceLabel !== right.sourceLabel) return false;
  if (left.times.length !== right.times.length || left.series.length !== right.series.length) return false;
  if (left.times.some((time, index) => time !== right.times[index])) return false;
  return left.series.every((series, index) => {
    const other = right.series[index];
    return !!other && series.id === other.id && series.name === other.name && series.units === other.units && series.elementIndex === other.elementIndex
      && series.values.length === other.values.length && series.values.every((value, valueIndex) => value === other.values[valueIndex]);
  });
}

function applyCellDrafts(dataset: ResultDataset, drafts: ResultCellDrafts): ResultDataset {
  return {
    ...dataset,
    series: dataset.series.map((series) => {
      const cells = drafts[series.id];
      if (!cells) return series;
      const values = [...series.values];
      for (const [rawIndex, rawValue] of Object.entries(cells)) {
        const index = Number(rawIndex);
        const value = parseCellDraft(rawValue);
        if (Number.isInteger(index) && index >= 0 && index < values.length && value !== undefined) values[index] = value;
      }
      return { ...series, values };
    }),
  };
}

export default function ResultsWorkspace({ language, context, backend, backendRevision, onDirtyStateChange }: FeaturePageProps) {
  const selectedAsset = context.selectedAsset;
  const resultAsset = isResultAsset(selectedAsset) ? selectedAsset : null;
  const userDraftKey = JSON.stringify([context.projectSessionID, context.projectPath ?? '', 'results-user-dataset']);
  const [draftState, setDraftState] = useState(() => ({ owner: userDraftKey, ...(readResultsDraft(userDraftKey) ?? emptyResultsDraft()) }));
  const draftForCurrentOwner = draftState.owner === userDraftKey ? draftState : { owner: userDraftKey, ...(readResultsDraft(userDraftKey) ?? emptyResultsDraft()) };
  const { source, dataset: userDataset, originalDataset: originalUserDataset, dirty: userDirty, cellDrafts } = draftForCurrentOwner;
  const updateDraftState = (update: (current: ResultsDraftSnapshot) => ResultsDraftSnapshot): void => {
    setDraftState((previous) => {
      const current = previous.owner === userDraftKey ? previous : { owner: userDraftKey, ...(readResultsDraft(userDraftKey) ?? emptyResultsDraft()) };
      return { owner: userDraftKey, ...update(current) };
    });
  };
  const setSource = (next: 'result' | 'user'): void => updateDraftState((current) => ({ ...current, source: next }));
  const setUserDataset = (dataset: ResultDataset | null): void => updateDraftState((current) => ({ ...current, dataset }));
  const setOriginalUserDataset = (dataset: ResultDataset | null): void => updateDraftState((current) => ({ ...current, originalDataset: dataset }));
  const setUserDirty = (dirty: boolean): void => updateDraftState((current) => ({ ...current, dirty }));
  const setCellDrafts = (drafts: ResultCellDrafts | ((current: ResultCellDrafts) => ResultCellDrafts)): void => updateDraftState((current) => ({ ...current, cellDrafts: typeof drafts === 'function' ? drafts(current.cellDrafts) : drafts }));
  const [importError, setImportError] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<ResultInspectorSnapshot | null>(null);
  const [catalog, setCatalog] = useState<ResultProfileCatalog | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [selectedBlockName, setSelectedBlockName] = useState('');
  const [selectedFlowArea, setSelectedFlowArea] = useState(0);
  const [selectedVariable, setSelectedVariable] = useState('');
  const [elementIndexInput, setElementIndexInput] = useState('0');
  const [backendSeries, setBackendSeries] = useState<ResultSeries[]>([]);
  const [backendTimes, setBackendTimes] = useState<string[]>([]);
  const [seriesLoading, setSeriesLoading] = useState(false);
  const [seriesError, setSeriesError] = useState<string | null>(null);
  const [hiddenSeries, setHiddenSeries] = useState<Set<string>>(new Set());
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [range, setRange] = useState<[number, number]>([0, 1]);
  const [settings, setSettings] = useState<ChartSettings>(defaultChartSettings);
  const [tableVisible, setTableVisible] = useState(true);
  const draftOwnerMatches = draftState.owner === userDraftKey;
  const invalidCellDraftCount = invalidDraftCount(cellDrafts);

  useEffect(() => {
    if (draftState.owner === userDraftKey) return;
    setDraftState({ owner: userDraftKey, ...(readResultsDraft(userDraftKey) ?? emptyResultsDraft()) });
  }, [draftState.owner, userDraftKey]);

  useEffect(() => {
    if (draftState.owner !== userDraftKey) return;
    const { owner: _owner, ...snapshotToStore } = draftState;
    writeResultsDraft(userDraftKey, snapshotToStore);
  }, [draftState, userDraftKey]);
  const currentDataKey = useMemo(() => [snapshot?.revision ?? '', selectedBlockName, String(selectedFlowArea), selectedVariable].join('|'), [snapshot?.revision, selectedBlockName, selectedFlowArea, selectedVariable]);
  const profileRequestRef = useRef<ActiveProfileRequest | null>(null);
  const profileGenerationRef = useRef(0);
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  const numberLocale = language === 'zh' ? 'zh-CN' : 'en-US';
  const backendFailure = (contextZh: string, contextEn: string, error: unknown): string => {
    const detail = error instanceof Error ? error.message : String(error);
    return language === 'zh' ? `${contextZh}后端原始错误：${detail}` : `${contextEn} Backend error: ${detail}`;
  };

  useEffect(() => {
    if (!resultAsset) {
      setSnapshot(null);
      setCatalog(null);
      setCatalogError(null);
      setCatalogLoading(false);
      setBackendSeries([]);
      setBackendTimes([]);
      setSelectedBlockName('');
      setSelectedVariable('');
      return;
    }
    setSnapshot(null);
    setCatalog(null);
    setBackendSeries([]);
    setBackendTimes([]);
    setCatalogError(null);
    setCatalogLoading(true);

    const controller = new AbortController();
    let active = true;
    const load = async (): Promise<void> => {
      if (!backend.supports('result.inspect')) {
        setCatalogError(text('当前 C# 后端未提供结果元数据读取能力。', 'The current C# backend does not provide result metadata inspection.'));
        setCatalogLoading(false);
        return;
      }
      if (!context.projectPath || !context.projectXml) {
        setCatalogError(text('请先打开已保存的工程，再选择其中的 HDF 结果文件。', 'Open a saved project and select one of its HDF result files.'));
        setCatalogLoading(false);
        return;
      }
      try {
        const inspected = await backend.request<ResultInspectorSnapshot>('result.inspect', {
          projectPath: context.projectPath,
          projectXml: context.projectXml,
          resultPath: resultAsset.path,
        }, { signal: controller.signal });
        if (!active) return;
        setSnapshot(inspected);
        if (!backend.supports('result.profileCatalog')) {
          setCatalogError(text('结果元数据已读取，但当前 C# 后端未提供时间序列目录。', 'Result metadata was read, but the current C# backend does not expose the profile catalog.'));
          setCatalogLoading(false);
          return;
        }
        const profileCatalog = await backend.request<ResultProfileCatalog>('result.profileCatalog', {
          resultPath: inspected.resultPath,
          revision: inspected.revision,
        }, { signal: controller.signal });
        if (!active) return;
        setCatalog(profileCatalog);
        const firstBlock = profileCatalog.outputBlocks[0];
        setSelectedBlockName(firstBlock?.name ?? '');
        setSelectedFlowArea(profileCatalog.flowAreas[0]?.index ?? 0);
        setSelectedVariable(firstBlock?.variables[0]?.name ?? '');
        setCatalogError(null);
      } catch (error) {
        if (!active || controller.signal.aborted) return;
        setCatalogError(backendFailure('读取结果目录失败。', 'Failed to read the result catalog.', error));
      } finally {
        if (active) setCatalogLoading(false);
      }
    };
    void load();
    return () => { active = false; controller.abort(); };
  }, [backend, backendRevision, context.projectPath, context.projectXml, resultAsset?.path]);

  useEffect(() => {
    if (!resultAsset && userDataset) setSource('user');
  }, [resultAsset?.path, userDataset]);

  const activeBlock = catalog?.outputBlocks.find((block) => block.name === selectedBlockName) ?? null;
  const activeVariable = activeBlock?.variables.find((variable) => variable.name === selectedVariable) ?? null;
  const activeData: ResultDataset | null = source === 'user' ? userDataset : null;
  const resultData: ResultDataset | null = backendSeries.length > 0 ? {
    name: snapshot?.name ?? resultAsset?.name ?? text('工程结果', 'Project result'),
    times: backendTimes.length ? backendTimes : (activeBlock?.times ?? []).slice(0, backendSeries[0]?.values.length ?? 0),
    series: backendSeries,
    source: 'backend-result',
    sourceLabel: text('C# 结果数据', 'C# result data'),
  } : null;
  const dataset = activeData ?? (source === 'result' ? resultData : null);
  const visibleSeries = useMemo(() => dataset?.series.filter((item) => !hiddenSeries.has(item.id)) ?? [], [dataset, hiddenSeries]);
  const selectedTimeOptions = dataset?.times ?? (source === 'result' ? activeBlock?.times ?? [] : []);
  const activeIndex = selectedTimeOptions.length === 0 ? 0 : Math.min(selectedIndex, selectedTimeOptions.length - 1);
  const backendMethodsAvailable = backend.supports('result.inspect') && backend.supports('result.profileCatalog') && backend.supports('result.profile');
  const owner = JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    source === 'result' ? resultAsset?.path ?? '' : 'user:' + (userDataset?.name ?? ''),
    source === 'result' ? snapshot?.revision ?? resultAsset?.revision ?? '' : 'local-import',
    'results',
  ]);
  const profileRequestKey = JSON.stringify([source, owner, resultAsset?.path ?? '', currentDataKey, elementIndexInput]);
  const profileRequestKeyRef = useRef(profileRequestKey);
  profileRequestKeyRef.current = profileRequestKey;

  useEffect(() => {
    setSeriesLoading(false);
    setSeriesError(null);
    return () => {
      profileGenerationRef.current += 1;
      const pending = profileRequestRef.current;
      if (pending) {
        pending.active = false;
        pending.controller.abort();
        profileRequestRef.current = null;
      }
    };
  }, [profileRequestKey]);

  const cancelPendingProfileRequest = (): void => {
    profileGenerationRef.current += 1;
    const pending = profileRequestRef.current;
    if (pending) {
      pending.active = false;
      pending.controller.abort();
      profileRequestRef.current = null;
    }
    setSeriesLoading(false);
  };

  useEffect(() => {
    onDirtyStateChange?.(owner, draftOwnerMatches && userDirty && source === 'user');
    return () => onDirtyStateChange?.(owner, false);
  }, [onDirtyStateChange, owner, source, userDirty, draftOwnerMatches]);

  const loadElementSeries = async (): Promise<void> => {
    setSeriesError(null);
    if (!snapshot || !catalog || !activeVariable || !backend.supports('result.profile')) {
      setSeriesError(text('当前 C# 尚未提供真实结果序列读取能力。', 'The current C# build does not expose real result-series reading.'));
      return;
    }
    const elementIndex = Number(elementIndexInput);
    if (elementIndexInput.trim() === '') {
      setSeriesError(text('请输入网格元素编号。', 'Enter a mesh element index.'));
      return;
    }
    const elementCount = activeVariable.elementKind.toLowerCase() === 'face' ? catalog.flowAreas.find((area) => area.index === selectedFlowArea)?.faceCount : catalog.flowAreas.find((area) => area.index === selectedFlowArea)?.cellCount;
    if (!Number.isInteger(elementIndex) || elementIndex < 0 || (elementCount !== undefined && elementIndex >= elementCount)) {
      setSeriesError(text('网格元素编号超出可选范围。', 'The mesh element index is outside the available range.'));
      return;
    }
    const id = selectedVariable + ':' + elementIndex;
    if (backendSeries.some((item) => item.id === id)) {
      setSeriesError(text('该网格元素的序列已经载入。', 'That mesh element is already loaded.'));
      return;
    }
    if (backendSeries.length >= 8) {
      setSeriesError(text('一次最多显示 8 条序列。', 'Load up to 8 series at a time.'));
      return;
    }
    const requestKey = profileRequestKey;
    const controller = new AbortController();
    profileRequestRef.current?.controller.abort();
    if (profileRequestRef.current) profileRequestRef.current.active = false;
    const generation = ++profileGenerationRef.current;
    const pending: ActiveProfileRequest = { controller, generation, requestKey, active: true };
    profileRequestRef.current = pending;
    const isCurrentRequest = (): boolean => pending.active
      && !controller.signal.aborted
      && profileGenerationRef.current === generation
      && profileRequestRef.current === pending
      && profileRequestKeyRef.current === requestKey;
    setSeriesLoading(true);
    try {
      const response = await backend.request<ResultProfileSeriesResponse>('result.profile', {
        resultPath: snapshot.resultPath,
        revision: snapshot.revision,
        outputBlockName: selectedBlockName,
        variable: selectedVariable,
        flowAreaIndex: selectedFlowArea,
        elementIndex,
      }, { signal: controller.signal });
      if (!isCurrentRequest()) return;
      if (!response.available) {
        const reason = response.unavailableReason;
        throw new Error(reason
          ? text(`后端未提供所选结果序列。后端原始信息：${reason}`, `The backend did not provide the selected result series. Backend detail: ${reason}`)
          : text('后端未提供所选结果序列。', 'The backend did not provide the selected result series.'));
      }
      const newSeries: ResultSeries = {
        id,
        name: displayResultVariableName(activeVariable.name || text('数值', 'Value'), language) + ' · ' + displayMeshElementName(activeVariable.elementKind, language) + ' ' + elementIndex,
        units: response.units,
        elementIndex,
        values: response.points.map((point) => point.value),
      };
      if (backendSeries.length === 0) setBackendTimes(response.points.map((point) => point.time));
      setBackendSeries((current) => [...current, newSeries]);
      setSelectedIndex(0);
      setRange([0, 1]);
      setHiddenSeries((current) => new Set([...current].filter((value) => value !== id)));
    } catch (error) {
      if (isCurrentRequest()) {
        const detail = error instanceof Error ? error.message : String(error);
        setSeriesError(language === 'zh' && !detail.startsWith('后端') ? backendFailure('读取结果序列失败。', 'Failed to read the result series.', error) : detail);
      }
    } finally {
      if (profileRequestRef.current === pending && profileGenerationRef.current === generation) {
        pending.active = false;
        profileRequestRef.current = null;
        setSeriesLoading(false);
      }
    }
  };

  const resetBackendSeries = (): void => {
    cancelPendingProfileRequest();
    setBackendSeries([]);
    setBackendTimes([]);
    setHiddenSeries(new Set());
    setSelectedIndex(0);
    setRange([0, 1]);
    setSeriesError(null);
  };

  const changeBlock = (name: string): void => {
    setSelectedBlockName(name);
    setSelectedVariable(catalog?.outputBlocks.find((block) => block.name === name)?.variables[0]?.name ?? '');
    resetBackendSeries();
  };

  const changeVariable = (name: string): void => { setSelectedVariable(name); resetBackendSeries(); };
  const changeArea = (index: number): void => { setSelectedFlowArea(index); resetBackendSeries(); };

  const importUserDataset = (next: ResultDataset): boolean => {
    if ((userDirty || invalidCellDraftCount > 0) && !window.confirm(text(
      '当前导入数据有未导出的草稿。导入新文件会丢弃这些更改，是否继续？',
      'The current imported data has unsaved edits. Importing another file will discard them. Continue?'))) return false;
    cancelPendingProfileRequest();
    setUserDataset(next);
    setOriginalUserDataset(structuredClone(next));
    setUserDirty(false);
    setCellDrafts({});
    setImportError(null);
    setSelectedIndex(0);
    setRange([0, 1]);
    setHiddenSeries(new Set());
    return true;
  };

  const editUserDraft = (index: number, seriesId: string, raw: string): void => {
    setCellDrafts((current) => ({ ...current, [seriesId]: { ...(current[seriesId] ?? {}), [String(index)]: raw } }));
    setUserDirty(true);
  };

  const commitUserDraft = (index: number, seriesId: string, value: number | null): void => {
    if (!userDataset) return;
    const next: ResultDataset = { ...userDataset, series: userDataset.series.map((item) => item.id === seriesId ? { ...item, values: item.values.map((oldValue, row) => row === index ? value : oldValue) } : item) };
    const nextDrafts = { ...cellDrafts };
    const seriesDrafts = { ...(nextDrafts[seriesId] ?? {}) };
    delete seriesDrafts[String(index)];
    if (Object.keys(seriesDrafts).length) nextDrafts[seriesId] = seriesDrafts;
    else delete nextDrafts[seriesId];
    setUserDataset(next);
    setCellDrafts(nextDrafts);
    setUserDirty(Object.keys(nextDrafts).length > 0 || !datasetsEqual(next, originalUserDataset));
  };

  const saveUserCopy = (): void => {
    if (!dataset || (source === 'user' && invalidCellDraftCount > 0)) return;
    const exportDataset = source === 'user' ? applyCellDrafts(dataset, cellDrafts) : dataset;
    const exportSeries = exportDataset.series.filter((item) => !hiddenSeries.has(item.id));
    if (!exportSeries.length) return;
    downloadCsv(exportDataset, exportSeries, language);
    if (source === 'user') {
      setUserDataset(exportDataset);
      setOriginalUserDataset(structuredClone(exportDataset));
      setCellDrafts({});
      setUserDirty(false);
    }
  };

  const restoreUserCopy = (): void => {
    if (!originalUserDataset) return;
    setUserDataset(structuredClone(originalUserDataset));
    setCellDrafts({});
    setUserDirty(false);
  };

  const setSeriesVisible = (id: string, visible: boolean): void => {
    setHiddenSeries((current) => {
      const next = new Set(current);
      if (visible) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return <main className="results-page">
    <header className="results-page-header">
      <div className="results-title-mark" aria-hidden="true"><span /><span /><span /></div>
        <div><p className="results-eyebrow">HEC-RAS 2025 · {text('结果分析', 'Results')}</p><h1>{text('结果查看器', 'Result viewer')}</h1><p className="results-subtitle">{text('查看 C# 读取的工程结果，或导入用户自己的 CSV / JSON 时间序列。', 'Inspect project results read by C#, or import your own CSV / JSON time series.')}</p></div>
      <span className={'results-runtime-pill ' + (source === 'user' ? 'is-local' : backendMethodsAvailable ? 'is-connected' : '')}><i />{source === 'user' ? text('用户文件', 'User file') : backendMethodsAvailable ? 'C#' : text('C# 接口待接通', 'C# link unavailable')}</span>
    </header>

    <ResultPicker
      importOwner={userDraftKey}
      language={language}
      selectedAsset={resultAsset}
      hasResultBackend={backend.supports('result.inspect') && backend.supports('result.profileCatalog')}
      resultReady={!!catalog}
      resultLoading={catalogLoading}
      userDataset={userDataset}
      activeSource={source}
      onSourceChange={(next) => { cancelPendingProfileRequest(); setSource(next); setSelectedIndex(0); setRange([0, 1]); }}
      onUserDataset={importUserDataset}
      onError={setImportError}
    />

    {importError && <div className="results-error-banner" role="alert"><span>!</span>{importError}<button type="button" onClick={() => setImportError(null)} aria-label={text('关闭', 'Dismiss')}>×</button></div>}

    {source === 'result' && <ResultNodeDetails snapshot={snapshot} loading={catalogLoading} error={snapshot ? null : catalogError} language={language} />}

    {source === 'result' && resultAsset && <section className="results-selector-panel">
      <div className="results-selector-heading"><div><span className="results-section-index">01</span><h2>{text('结果数据选择', 'Result data selection')}</h2></div><span>{catalog?.resultName ?? snapshot?.name ?? resultAsset.name}</span></div>
      <div className="results-selector-grid">
        <label className="results-field"><span>{text('输出块', 'Output block')}</span><select value={selectedBlockName} onChange={(event) => changeBlock(event.target.value)} disabled={!catalog || catalogLoading}>
          {catalog?.outputBlocks.map((block) => <option value={block.name} key={block.name}>{block.name} · {block.profileCount} {text('时步', 'profiles')}</option>)}
          {!catalog && <option value="">{text('尚无可用输出块', 'No output block available')}</option>}
        </select></label>
        <label className="results-field"><span>{text('流区', 'Flow area')}</span><select value={selectedFlowArea} onChange={(event) => changeArea(Number(event.target.value))} disabled={!catalog || catalogLoading}>
          {catalog?.flowAreas.map((area) => <option value={area.index} key={area.index}>{area.name} · {(activeVariable?.elementKind === 'Face' ? area.faceCount : area.cellCount).toLocaleString(numberLocale)} {activeVariable?.elementKind === 'Face' ? text('面', 'faces') : text('单元', 'cells')}</option>)}
          {catalog?.flowAreas.length === 0 && <option value={0}>{text('此结果没有可用的二维网格', 'No 2D mesh is available in this result')}</option>}
        </select></label>
        <label className="results-field"><span>{text('变量', 'Variable')}</span><select value={selectedVariable} onChange={(event) => changeVariable(event.target.value)} disabled={!activeBlock || activeBlock.variables.length === 0}>
          {activeBlock?.variables.map((variable) => <option value={variable.name} key={variable.name}>{displayResultVariableName(variable.name, language)} · {displayResultDimensionName(variable.quantity, language)}{variable.units ? ' (' + variable.units + ')' : ''}</option>)}
          {activeBlock?.variables.length === 0 && <option value="">{text('此输出块没有可视变量', 'No plottable variables in this output block')}</option>}
        </select></label>
        <div className="results-load-series-field"><label className="results-field"><span>{text(activeVariable?.elementKind === 'Face' ? '面编号' : '单元编号', activeVariable?.elementKind === 'Face' ? 'Face index' : 'Cell index')}</span><input inputMode="numeric" value={elementIndexInput} onChange={(event) => { cancelPendingProfileRequest(); setElementIndexInput(event.target.value); }} disabled={!activeVariable} /></label>
          <button type="button" className="results-primary-button" onClick={() => void loadElementSeries()} disabled={seriesLoading || catalogLoading || !catalog || !activeVariable || !backend.supports('result.profile')}>
            {seriesLoading ? <span className="results-spinner" /> : <span aria-hidden="true">＋</span>}{text('载入序列', 'Load series')}
          </button>
        </div>
      </div>
      {!backend.supports('result.profile') && <p className="results-capability-note">{text('当前 C# 尚未暴露 result.profile。结果目录和时间步可读，但无法伪造 HDF 数值序列。', 'This C# build does not expose result.profile. The catalog and timestamps may be available, but HDF values are not fabricated.')}</p>}
      {seriesError && <p className="results-inline-error" role="alert">{seriesError}</p>}
    </section>}

    <section className="results-analysis-panel">
      <div className="results-analysis-heading">
        <div><span className="results-section-index">02</span><h2>{text('时间序列', 'Time series')}</h2><span className="results-source-label">{dataset ? (dataset.source === 'user-file' ? text('用户导入数据', 'Imported user data') : text('C# 结果数据', 'C# result data')) : text('暂无序列', 'No series loaded')}</span></div>
        <div className="results-analysis-actions">
          {selectedTimeOptions.length > 0 && <label className="results-time-picker"><span>{text('时间步', 'Time step')}</span><select value={activeIndex} onChange={(event) => setSelectedIndex(Number(event.target.value))}>
            {selectedTimeOptions.map((time, index) => {
              const profileName = source === 'result' ? activeBlock?.profileNames[index] : undefined;
              return <option value={index} key={index}>{String(index + 1).padStart(3, '0')} · {profileName ? profileName + ' · ' : ''}{time}</option>;
            })}
          </select></label>}
          <button type="button" className="results-secondary-button" onClick={() => setTableVisible((visible) => !visible)}>{tableVisible ? text('隐藏表格', 'Hide table') : text('显示表格', 'Show table')}</button>
          <button type="button" className="results-primary-button" onClick={saveUserCopy} disabled={!dataset || visibleSeries.length === 0 || (source === 'user' && invalidCellDraftCount > 0)} title={source === 'user' && invalidCellDraftCount > 0 ? text('请先修正无效单元格再导出。', 'Fix invalid cells before exporting.') : undefined}><span aria-hidden="true">⇩</span>{text('导出 CSV', 'Export CSV')}</button>
        </div>
      </div>

      {dataset && dataset.series.length > 0 && <div className="results-series-toolbar">
        <span>{text('显示序列', 'Visible series')}</span>
        {dataset.series.map((item) => <div key={item.id} className={'results-series-toggle ' + (hiddenSeries.has(item.id) ? 'is-hidden' : '')}>
          <label><input type="checkbox" checked={!hiddenSeries.has(item.id)} onChange={(event) => setSeriesVisible(item.id, event.target.checked)} /><i />{item.name}</label>
          {item.elementIndex !== undefined && source === 'result' && <button type="button" aria-label={text('移除序列', 'Remove series')} onClick={() => setBackendSeries((current) => current.filter((series) => series.id !== item.id))}>×</button>}
        </div>)}
        {userDirty && source === 'user' && <span className="results-draft-status"><i />{invalidCellDraftCount > 0 ? text(`有 ${invalidCellDraftCount} 个无效单元格`, `${invalidCellDraftCount} invalid cells`) : text('有未导出的本地草稿', 'Local draft has unsaved edits')}<button type="button" onClick={restoreUserCopy}>{text('还原', 'Revert')}</button></span>}
      </div>}

      {source === 'result' && resultAsset && !dataset && !catalogLoading && <div className="results-result-empty">
        <div className="results-empty-symbol"><span>≈</span></div><strong>{catalogError ? text('尚未载入结果序列', 'Result series not loaded') : text('选择变量和网格单元以载入真实序列', 'Choose a variable and mesh element to load a real series')}</strong>
        <p>{catalogError ?? text('序列由 C# 从所选 HDF 文件读取；图表不会用示例或模拟值填充。', 'Series values are read from the selected HDF file by C#. No sample or synthetic values are shown.')}</p>
      </div>}
      {source === 'user' && !dataset && <div className="results-result-empty"><div className="results-empty-symbol"><span>⇧</span></div><strong>{text('导入你的 CSV / JSON 时间序列', 'Import your CSV / JSON time series')}</strong><p>{text('目前没有预置示例数据。导入后可查看、编辑草稿并导出 CSV。', 'There is no preset sample data. Import a file to view it, edit a local draft, and export CSV.')}</p></div>}

      <TimeSeriesPayloadChart dataset={dataset} series={visibleSeries} selectedIndex={activeIndex} onSelectIndex={setSelectedIndex} range={range} onRangeChange={setRange} settings={settings} onSettingsChange={setSettings} language={language} />

      {tableVisible && <div className="results-table-section">
        <div className="results-table-heading"><div><h3>{text('数据表', 'Data table')}</h3><span>{dataset ? dataset.times.length.toLocaleString(numberLocale) + ' ' + text('行', 'rows') : text('等待载入', 'Waiting for data')}</span></div><span className="results-table-readonly">{source === 'user' && dataset ? text('本地草稿可编辑', 'Local draft is editable') : text('结果源为只读', 'Result source is read-only')}</span></div>
        <TimeSeriesPayloadTable dataset={dataset} series={visibleSeries} selectedIndex={activeIndex} onSelectIndex={setSelectedIndex} onDraftChange={editUserDraft} onDraftCommit={commitUserDraft} editable={source === 'user' && !!dataset} language={language} drafts={cellDrafts} />
      </div>}
    </section>

    <footer className="results-page-footnote"><span aria-hidden="true">i</span>{source === 'result' ? text('HDF 结果内容只在 C# 请求真实成功后显示；CSV / JSON 导入数据始终标注为用户数据。', 'HDF values appear only after a real successful C# request. Imported CSV / JSON remains labeled as user data.') : text('编辑仅修改当前导入的本地草稿；导出 CSV 才会生成可保存副本。', 'Edits affect only the imported local draft. Export CSV to create a saved copy.')}</footer>
  </main>;
}
