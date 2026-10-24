import { useEffect, useMemo, useState } from 'react';
import type { Language } from '../../shared/workbench';
import {
  hasConnectableVectorCapture,
  vectorDeepConnectComponentSelectionKey,
} from './geometryVectorDeepConnect';
import type {
  PersistedVectorDeepConnectComponent,
  PersistedVectorDeepConnectSource,
  TopologyVectorSourceInspection,
  VectorSourceInspectionCandidate,
} from './geometryVectorDeepConnect';

export interface TopologyDeepConnectVectorControlsProps {
  language: Language;
  /** Current saved map's checked, visible persisted vector nodes, including GDB layers. */
  candidates: readonly VectorSourceInspectionCandidate[];
  inspection: TopologyVectorSourceInspection | null;
  selectedComponentKey: string | null;
  busy: boolean;
  canInspect: boolean;
  error?: string | null;
  /** Activates/reloads the parent-owned capture hook; that hook inspects every current candidate. */
  onInspect: () => void;
  /** Selects only a component returned by the Host for the captured source file family. */
  onSelectComponent: (source: PersistedVectorDeepConnectSource | null,
    component: PersistedVectorDeepConnectComponent | null) => void;
  onCancel: () => void;
}

interface ComponentOption {
  key: string;
  source: PersistedVectorDeepConnectSource;
  component: PersistedVectorDeepConnectComponent;
}

function sourceTitle(source: PersistedVectorDeepConnectSource, english: boolean): string {
  const type = source.node.mapType ?? source.node.nodeType;
  const family = source.sourceKind === 'gdb'
    ? (english ? ' · File Geodatabase' : ' · 文件地理数据库')
    : source.sourceKind ? ` · ${source.sourceKind}` : '';
  const state = source.canDeepConnect
    ? english ? 'ready' : '可用'
    : source.diagnosticCode ?? (english ? 'unavailable' : '不可用');
  return `${source.node.nodeName} · ${type}${family} · ${state}`;
}

function componentTitle(component: PersistedVectorDeepConnectComponent, english: boolean): string {
  return english
    ? `FID ${component.featureFID} · path ${component.pathIndex} · ${component.points.length} points`
    : `FID ${component.featureFID} · 路径 ${component.pathIndex} · ${component.points.length} 个点`;
}

export function TopologyDeepConnectVectorControls({
  language,
  candidates,
  inspection,
  selectedComponentKey,
  busy,
  canInspect,
  error,
  onInspect,
  onSelectComponent,
  onCancel,
}: TopologyDeepConnectVectorControlsProps) {
  const english = language === 'en';
  const connectableSources = useMemo(() => (inspection?.sources ?? [])
    .filter(hasConnectableVectorCapture), [inspection]);
  const [selectedSourceKey, setSelectedSourceKey] = useState<string | null>(null);
  const [componentFilter, setComponentFilter] = useState('');
  useEffect(() => {
    const selectedSource = selectedComponentKey
      ? connectableSources.find((source) => source.components.some((component) =>
        vectorDeepConnectComponentSelectionKey(source, component) === selectedComponentKey))
      : null;
    setSelectedSourceKey((current) => selectedSource?.key ??
      (connectableSources.some((source) => source.key === current) ? current : connectableSources[0]?.key ?? null));
    setComponentFilter('');
  }, [inspection?.owner.ownerKey, connectableSources, selectedComponentKey]);

  let selectedComponentOption: ComponentOption | null = null;
  if (selectedComponentKey) {
    for (const source of connectableSources) {
      const component = source.components.find((item) =>
        vectorDeepConnectComponentSelectionKey(source, item) === selectedComponentKey);
      if (component) {
        selectedComponentOption = { key: selectedComponentKey, source, component };
        break;
      }
    }
  }
  const selectedSource = connectableSources.find((source) => source.key === selectedSourceKey)
    ?? selectedComponentOption?.source ?? connectableSources[0] ?? null;
  const filterText = componentFilter.trim().toLocaleLowerCase();
  const filteredSourceComponents = selectedSource?.components.filter((component) => !filterText ||
    `${component.featureFID} ${component.pathIndex} ${component.componentIdentity}`
      .toLocaleLowerCase().includes(filterText)) ?? [];
  const visibleSourceOptions: ComponentOption[] = selectedSource
    ? filteredSourceComponents.slice(0, 500).map((component) => ({
      key: vectorDeepConnectComponentSelectionKey(selectedSource, component), source: selectedSource, component,
    }))
    : [];
  if (selectedSource && selectedComponentOption?.source.key === selectedSource.key &&
      !visibleSourceOptions.some((option) => option.key === selectedComponentOption.key)) {
    visibleSourceOptions.unshift(selectedComponentOption);
  }
  const selectedOption = selectedComponentOption && selectedComponentOption.source.key === selectedSource?.key
    ? selectedComponentOption : null;
  const candidateCount = candidates.length;
  const canRunInspect = canInspect && !busy && candidateCount > 0;

  const selectComponent = (key: string) => {
    if (!key) {
      onSelectComponent(null, null);
      return;
    }
    const option = visibleSourceOptions.find((item) => item.key === key);
    if (option) onSelectComponent(option.source, option.component);
  };

  const status = busy
    ? (english ? 'Reading checked, visible vector layers from the current saved map…' : '正在读取当前已保存地图中已勾选且可见的矢量图层…')
    : !canInspect
      ? (english ? 'The current saved map or Geometry draft capture is unavailable.' : '当前已保存地图或 Geometry 草稿捕获不可用。')
      : candidateCount === 0
            ? (english ? 'No checked, visible vector source layers are available.' : '当前没有已勾选且可见的矢量来源图层。')
        : inspection
          ? (english ? `${inspection.sources.length} layer(s) inspected for this exact map capture.` : `已针对当前地图捕获检查 ${inspection.sources.length} 个图层。`)
          : (english ? 'Inspect these persisted sources before placing anchors.' : '请先检查这些持久化来源，再放置连接锚点。');

  return (
    <section aria-label={english ? 'Persisted vector DeepConnect sources' : '持久化矢量连接来源'}
      data-testid="geometry-vector-deep-connect-controls">
      <h3>{english ? 'Saved vector layer source' : '已保存矢量图层来源'}</h3>
      <p role="status" aria-live="polite" data-testid="vector-deep-connect-status">{status}</p>

      <fieldset disabled>
        <legend>{english ? 'Checked and visible vector layers' : '已勾选且可见的矢量图层'}</legend>
        {candidates.length === 0
          ? <p>{english ? 'There are no checked and visible vector candidates in the current map tree.' : '当前地图目录中没有已勾选且可见的矢量候选图层。'}</p>
          : candidates.map((candidate) => <p key={JSON.stringify(candidate.node)} className="deep-connect-vector-candidate">
            {candidate.node.nodeName} · {candidate.node.mapType ?? candidate.node.nodeType}
            {(candidate.node.nodeType === 'GdbVectorLayer' || candidate.node.mapType === 'GdbVectorLayer') &&
              ` · ${english ? 'GDB directory source' : 'GDB 目录来源'}`}
            {candidate.expectedFamily && ` · ${candidate.expectedFamily.family}`}
          </p>)}
      </fieldset>

      <div className="deep-connect-vector-actions">
        <button type="button" disabled={!canRunInspect} onClick={onInspect} data-testid="vector-deep-connect-inspect">
          {busy ? (english ? 'Inspecting…' : '正在检查…') : (english ? 'Inspect current layers' : '检查当前图层')}
        </button>
        <button type="button" onClick={onCancel} data-testid="vector-deep-connect-cancel">
          {english ? 'Clear source selection' : '清除来源选择'}
        </button>
      </div>

      {error && <p role="alert" data-testid="vector-deep-connect-error">{error}</p>}

      {inspection && <>
        <p className="deep-connect-vector-owner">
          {english ? 'Map capture:' : '地图捕获：'} {inspection.owner.mapName} · {inspection.owner.mapType}
          {' · '}{inspection.owner.visualizationRevision.slice(0, 12)}
        </p>
        {connectableSources.length > 0 && <>
          <label className="deep-connect-vector-source-picker">
            <span>{english ? 'Source layer' : '来源图层'}</span>
            <select value={selectedSource?.key ?? ''} disabled={busy} onChange={(event) => {
              const next = connectableSources.find((source) => source.key === event.currentTarget.value) ?? null;
              setSelectedSourceKey(next?.key ?? null);
              setComponentFilter('');
              onSelectComponent(null, null);
            }} data-testid="vector-deep-connect-source">
              {connectableSources.map((source) => <option key={source.key} value={source.key}>
                {sourceTitle(source, english)}
              </option>)}
            </select>
          </label>
          {selectedSource && <>
            <label className="deep-connect-vector-component-filter">
              <span>{english ? 'Find component by FID or path index' : '按 FID 或路径序号查找组件'}</span>
              <input type="search" value={componentFilter} disabled={busy}
                onChange={(event) => setComponentFilter(event.currentTarget.value)}
                data-testid="vector-deep-connect-component-filter" />
            </label>
            <label className="deep-connect-vector-component-picker">
              <span>{english ? 'Host-confirmed source component' : 'Host 已确认的来源组件'}</span>
              <select value={selectedOption?.key ?? ''} disabled={busy || visibleSourceOptions.length === 0}
                onChange={(event) => selectComponent(event.currentTarget.value)} data-testid="vector-deep-connect-component">
                <option value="">{english ? 'Choose a source component…' : '选择来源组件…'}</option>
                {visibleSourceOptions.map((option) => <option key={option.key} value={option.key}>
                  {componentTitle(option.component, english)}
                </option>)}
              </select>
              {filteredSourceComponents.length > 500 && <small>
                {english
                  ? 'Showing the first 500 matches; narrow the filter to find another component.'
                  : '当前显示前 500 个匹配项；请缩小筛选范围以查找其他组件。'}
              </small>}
              {selectedSource.components.length > 0 && <small>
                {english ? `${selectedSource.components.length} component(s) in this source.` : `此来源共有 ${selectedSource.components.length} 个组件。`}
              </small>}
            </label>
          </>}
        </>}

        {selectedOption && <dl className="deep-connect-vector-capture" data-testid="vector-deep-connect-capture">
          <dt>{selectedOption.source.sourceKind === 'gdb'
            ? (english ? 'Source type' : '来源类型') : (english ? 'Source family' : '来源文件组')}</dt>
          <dd data-testid="vector-deep-connect-source-family">{selectedOption.source.sourceKind === 'gdb'
            ? (english ? 'File Geodatabase directory' : '文件地理数据库目录')
            : selectedOption.source.sourceFamily?.family}</dd>
          <dt>{english ? 'Source path' : '来源路径'}</dt>
          <dd data-testid="vector-deep-connect-source-path"><code title={selectedOption.source.canonicalSourcePath ?? undefined}>{selectedOption.source.canonicalSourcePath}</code></dd>
          {selectedOption.source.sourceKind === 'gdb' && selectedOption.source.gdbSource && selectedOption.source.gdbLayer
            ? <>
              <dt>{english ? 'GDB directory' : 'GDB 目录'}</dt>
              <dd data-testid="vector-deep-connect-gdb-directory"><code>{selectedOption.source.gdbSource.sourcePath}</code></dd>
              <dt>{english ? 'Directory manifest revision' : '目录清单修订'}</dt>
              <dd data-testid="vector-deep-connect-gdb-revision"><code>{selectedOption.source.gdbSource.revision}</code></dd>
              <dt>{english ? 'GDB layer' : 'GDB 图层'}</dt>
              <dd data-testid="vector-deep-connect-gdb-layer">{selectedOption.source.gdbLayer.name} · {selectedOption.source.gdbLayer.vectorType} · {selectedOption.source.gdbLayer.geometryType}</dd>
            </>
            : <>
              <dt>{english ? 'Source SHA-256' : '来源 SHA-256'}</dt>
              <dd data-testid="vector-deep-connect-source-sha256"><code title={selectedOption.source.sourceSHA256 ?? undefined}>{selectedOption.source.sourceSHA256}</code></dd>
            </>}
          <dt>featureFID</dt>
          <dd data-testid="vector-deep-connect-feature-fid">{selectedOption.component.featureFID}</dd>
          <dt>pathIndex</dt>
          <dd data-testid="vector-deep-connect-path-index">{selectedOption.component.pathIndex}</dd>
          <dt>{english ? 'Renderer component identity' : '渲染组件身份'}</dt>
          <dd data-testid="vector-deep-connect-component-identity">{selectedOption.component.componentIdentity}</dd>
          <dt>{english ? 'Source provenance' : '来源序号'}</dt>
          <dd data-testid="vector-deep-connect-component-provenance">{english ? 'Feature' : '要素'} {selectedOption.component.sourceFeatureOrdinal} · {english ? 'part' : '部件'} {selectedOption.component.sourcePartOrdinal}
            {selectedOption.component.sourcePolygonIndex !== null && ` · ${english ? 'polygon' : '多边形'} ${selectedOption.component.sourcePolygonIndex}`}
            {selectedOption.component.sourceIsHole === true && ` · ${english ? 'hole' : '内环'}`}
          </dd>
          {selectedOption.source.sourceKind === 'gdb' && <>
            <dt>{english ? 'SourceID' : '来源要素 ID'}</dt>
            <dd data-testid="vector-deep-connect-component-source-id">{selectedOption.component.sourceID ?? '—'}</dd>
          </>}
        </dl>}

        <div className="deep-connect-vector-diagnostics" aria-label={english ? 'Host source diagnostics' : 'Host 来源诊断'}>
          {inspection.sources.map((source) => <details key={source.key}>
            <summary>{sourceTitle(source, english)}</summary>
            {source.canonicalSourcePath && <p><code>{source.canonicalSourcePath}</code></p>}
            {source.sourceSHA256 && <p><code>{english ? 'Source SHA-256' : '来源 SHA-256'}: {source.sourceSHA256}</code></p>}
            {source.gdbSource && <p data-testid="vector-deep-connect-gdb-revision-diagnostic">
              {english ? 'Directory manifest revision' : '目录清单修订'}: <code>{source.gdbSource.revision}</code>
            </p>}
            {source.diagnosticCode && <p><code>{source.diagnosticCode}</code></p>}
            {source.diagnostic && <p>{source.diagnostic}</p>}
            {source.oversizedComponentCount > 0 && <p role="note">
              {english
                ? `${source.oversizedComponentCount} component(s) exceeded the Host point limit and were omitted.`
                : `${source.oversizedComponentCount} 个组件超过 Host 点数上限，已按服务诊断省略。`}
            </p>}
            {source.unsupportedGeometryTypes.length > 0 && <p>
              {english ? 'Unsupported geometry types:' : '未支持的几何类型：'} {source.unsupportedGeometryTypes.join(', ')}
            </p>}
            {!source.diagnostic && !source.diagnosticCode && !source.unsupportedGeometryTypes.length && source.oversizedComponentCount === 0 &&
              <p>{english ? 'The Host confirmed this source capture.' : 'Host 已确认此来源捕获。'}</p>}
          </details>)}
        </div>
        <p className="deep-connect-vector-boundary">
          {english
            ? 'The Host revalidates the selected component and ordered anchors against the saved map and captured source identity (file-family manifest or full GDB directory manifest and layer). Completing a connection changes only the current Geometry draft; saving the parent Geometry persists the new arc while the original vector source remains read-only.'
            : 'Host 会按已保存地图以及捕获的来源身份（文件组清单，或完整 GDB 目录清单与图层）重新验证组件和锚点顺序。完成连接只更新当前 Geometry 草稿；保存父级 Geometry 后，新弧段才写入几何模型，原矢量来源保持只读。'}
        </p>
      </>}
    </section>
  );
}

export default TopologyDeepConnectVectorControls;
