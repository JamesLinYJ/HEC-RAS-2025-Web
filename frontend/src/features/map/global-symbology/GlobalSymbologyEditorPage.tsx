import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import {
  globalSymbologyMethods,
  isGlobalSymbologyDirty,
  parseGlobalSymbologySnapshot,
  propertyUsesColor,
  globalSymbologyPropertyLabel,
  resetGlobalSymbology,
  toGlobalSymbologySaveStyles,
  updateGlobalSymbologyValue,
} from './globalSymbologyWorkflow';
import type {
  GlobalSymbologyDefinition,
  GlobalSymbologyGroup,
  GlobalSymbologyProperty,
  GlobalSymbologySnapshot,
  GlobalSymbologyValues,
} from './globalSymbologyWorkflow';
import './globalSymbology.css';

export interface GlobalSymbologyEditorPageProps {
  backend: BackendGateway;
  language: Language;
  backendRevision?: number;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
}

type Phase = 'loading' | 'ready' | 'failed';

/** Edits the user's persisted defaults; the current SVG map canvas does not consume these styles yet. */
export function GlobalSymbologyEditorPage({ backend, language, backendRevision, onDirtyStateChange }: GlobalSymbologyEditorPageProps) {
  const zh = language === 'zh';
  const [phase, setPhase] = useState<Phase>('loading');
  const [busy, setBusy] = useState(false);
  const [snapshot, setSnapshot] = useState<GlobalSymbologySnapshot | null>(null);
  const [groups, setGroups] = useState<GlobalSymbologyGroup[]>([]);
  const [error, setError] = useState<unknown | null>(null);
  const [status, setStatus] = useState('');
  const [requestLabel, setRequestLabel] = useState('');
  const generationRef = useRef(0);
  const activeControllerRef = useRef<AbortController | null>(null);
  const mountedRef = useRef(false);
  const dirtyCallbackRef = useRef(onDirtyStateChange);
  dirtyCallbackRef.current = onDirtyStateChange;
  const dirty = useMemo(() => isGlobalSymbologyDirty(groups), [groups]);
  const snapshotRef = useRef(snapshot);
  const dirtyRef = useRef(dirty);
  snapshotRef.current = snapshot;
  dirtyRef.current = dirty;

  const cancelActiveRequest = useCallback(() => {
    generationRef.current++;
    activeControllerRef.current?.abort();
    activeControllerRef.current = null;
  }, []);

  const perform = useCallback(async (method: string, parameters: unknown, label: string,
    onResult: (value: unknown) => void) => {
    if (!backend.supports(method)) {
      setError(new Error(zh
        ? `C# Host 尚未发布 ${method} 操作。`
        : `The C# Host has not published ${method}.`));
      setPhase(snapshot ? 'ready' : 'failed');
      return;
    }
    cancelActiveRequest();
    const id = generationRef.current;
    const controller = new AbortController();
    activeControllerRef.current = controller;
    setBusy(true);
    setRequestLabel(label);
    setError(null);
    setStatus('');
    try {
      const value = await backend.request(method, parameters, { signal: controller.signal });
      if (!mountedRef.current || id !== generationRef.current) return;
      onResult(value);
    } catch (requestError) {
      if (mountedRef.current && id === generationRef.current && !controller.signal.aborted) {
        setError(requestError);
        setPhase(snapshot ? 'ready' : 'failed');
      }
    } finally {
      if (mountedRef.current && id === generationRef.current) {
        activeControllerRef.current = null;
        setBusy(false);
        setRequestLabel('');
      }
    }
  }, [backend, cancelActiveRequest, snapshot, zh]);

  const inspect = useCallback(async (discardDraft: boolean) => {
    if (!discardDraft && dirty) {
      setError(new Error(zh
        ? '当前有未保存的修改。保存或恢复草稿后，再重新读取全局设置。'
        : 'There are unsaved changes. Save or reset the draft before reloading global settings.'));
      return;
    }
    setPhase(snapshot ? 'ready' : 'loading');
    await perform(globalSymbologyMethods.inspect, {}, zh ? '读取全局样式' : 'Reading global styles', value => {
      const next = parseGlobalSymbologySnapshot(value);
      setSnapshot(next);
      setGroups(next.groups);
      setPhase('ready');
      setStatus(zh
        ? `已读取 ${next.groups.reduce((count, group) => count + group.options.length, 0)} 个全局样式定义。`
        : `Loaded ${next.groups.reduce((count, group) => count + group.options.length, 0)} global style definitions.`);
    });
  }, [dirty, perform, snapshot, zh]);
  const inspectRef = useRef(inspect);
  inspectRef.current = inspect;

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      cancelActiveRequest();
      dirtyCallbackRef.current?.('global-symbology', false);
    };
  }, [cancelActiveRequest]);

  useEffect(() => {
    dirtyCallbackRef.current?.('global-symbology', dirty);
  }, [dirty]);

  useEffect(() => {
    if (!snapshotRef.current || !dirtyRef.current) void inspectRef.current(false);
    else if (backendRevision !== undefined) {
      setStatus(zh
        ? '后端操作列表已更新；为保留未保存草稿，暂未自动重载。'
        : 'The backend operation list changed; the editor kept its unsaved draft and did not reload.');
    }
  }, [backendRevision, zh]);

  const updateValue = useCallback((definitionID: string, change: Partial<GlobalSymbologyValues>) => {
    setGroups(current => updateGlobalSymbologyValue(current, definitionID, change));
  }, []);

  const save = useCallback(async () => {
    if (!snapshot || !dirty) return;
    await perform(globalSymbologyMethods.save, {
      expectedRevision: snapshot.revision,
      styles: toGlobalSymbologySaveStyles(groups),
    }, zh ? '保存全局样式' : 'Saving global styles', value => {
      const saved = parseGlobalSymbologySnapshot(value);
      setSnapshot(saved);
      setGroups(saved.groups);
      setPhase('ready');
      setStatus(zh ? '已保存全局样式，配置以用户设置文件持久化。' : 'Global styles saved to the user settings file.');
    });
  }, [dirty, groups, perform, snapshot, zh]);

  const resetAll = useCallback(() => {
    setGroups(current => resetGlobalSymbology(current));
    setStatus(zh ? '所有样式草稿已恢复默认值；点击“保存”后才会写入设置。' : 'All drafts now use defaults; save to persist the changes.');
    setError(null);
  }, [zh]);

  const resetOne = useCallback((definition: GlobalSymbologyDefinition) => {
    updateValue(definition.definitionID, definition.defaultValues);
    const name = definitionNameLabel(definition.name, language);
    setStatus(zh
      ? `“${name}”草稿已恢复默认值；点击“保存”后才会写入设置。`
      : `“${name}” draft now uses defaults; save to persist the change.`);
    setError(null);
  }, [language, updateValue, zh]);

  const errorText = error === null ? '' : message(error, language);
  const diagnostics = error === null ? null : errorDiagnostics(error);

  return (
    <section className="global-symbology-editor" aria-label={zh ? '全局矢量符号设置' : 'Global vector symbology'}>
      <header className="global-symbology-header">
        <div>
          <p className="global-symbology-eyebrow">{zh ? '用户默认值' : 'USER DEFAULTS'}</p>
          <h2>{zh ? '全局矢量符号' : 'Global vector symbology'}</h2>
          <p className="global-symbology-muted">
            {zh
              ? '按原始定义分组编辑点、线、面和文字样式。只提交到当前用户设置，不覆盖工程内已有的显式样式。'
              : 'Edit point, line, polygon, and text styles by their original definitions. Changes are saved to this user’s settings and do not override explicit project styles.'}
          </p>
        </div>
        <div className="global-symbology-actions">
          <button type="button" className="global-symbology-secondary" disabled={busy || dirty}
            onClick={() => void inspect(true)}>
            {zh ? '重新读取' : 'Reload'}
          </button>
          <button type="button" className="global-symbology-secondary" disabled={busy || !groups.length || !snapshot}
            onClick={resetAll}>
            {zh ? '全部恢复默认' : 'Restore all defaults'}
          </button>
          <button type="button" className="global-symbology-primary" disabled={busy || !dirty || !snapshot}
            onClick={() => void save()}>
            {busy ? (zh ? '处理中…' : 'Working…') : (zh ? '保存设置' : 'Save settings')}
          </button>
        </div>
      </header>

      <p className="global-symbology-limitation" role="note">
        {zh
          ? '当前 Web 地图主画布仍使用其现有 SVG 符号；全局默认值目前由明确请求全局 fallback 的 C# 矢量绘图路径读取。'
          : 'The current Web map canvas still uses its existing SVG symbols. Global defaults are currently read only by the C# vector renderer when a request explicitly selects global fallback.'}
      </p>

      {busy && <p className="global-symbology-loading" role="status">{requestLabel || (zh ? '处理中…' : 'Working…')}</p>}
      {errorText && <div className="global-symbology-error" role="alert">
        <p>{errorText}</p>
        {diagnostics !== null && <details><summary>{zh ? '诊断信息' : 'Diagnostics'}</summary><pre>{formatDiagnostics(diagnostics)}</pre></details>}
        <button type="button" className="global-symbology-secondary" disabled={busy}
          onClick={() => void inspect(true)}>{zh ? '重新读取并放弃草稿' : 'Reload and discard draft'}</button>
      </div>}
      {status && !errorText && <p className="global-symbology-status" role="status">{status}</p>}

      {phase === 'loading' && !snapshot && <p className="global-symbology-empty">{zh ? '正在读取全局样式…' : 'Loading global styles…'}</p>}
      {phase === 'failed' && !snapshot && !errorText && <p className="global-symbology-empty">{zh ? '全局样式不可用。' : 'Global styles are unavailable.'}</p>}
      {snapshot && groups.map(group => (
        <details className="global-symbology-group" key={group.layerName} open>
          <summary>
            <span>{layerLabel(group.layerName, language)}</span>
            <span className="global-symbology-count">{group.options.length}</span>
          </summary>
          <div className="global-symbology-options">
            {group.options.map(definition => (
              <GlobalSymbologyOption key={definition.definitionID} definition={definition} language={language}
                busy={busy} onChange={change => updateValue(definition.definitionID, change)}
                onReset={() => resetOne(definition)} />
            ))}
          </div>
        </details>
      ))}
      {snapshot && groups.length === 0 && <p className="global-symbology-empty">{zh ? 'Host 未注册可编辑的全局符号定义。' : 'The Host has no editable global style definitions.'}</p>}
      {snapshot && <p className="global-symbology-revision">
        {zh ? '设置修订：' : 'Settings revision: '}<code>{snapshot.revision}</code>
      </p>}
    </section>
  );
}

interface OptionProps {
  definition: GlobalSymbologyDefinition;
  language: Language;
  busy: boolean;
  onChange: (change: Partial<GlobalSymbologyValues>) => void;
  onReset: () => void;
}

function GlobalSymbologyOption({ definition, language, busy, onChange, onReset }: OptionProps) {
  const zh = language === 'zh';
  const changed = !sameValues(definition.values, definition.savedValues);
  return (
    <article className="global-symbology-option" data-definition-id={definition.definitionID}>
      <header>
        <div>
          <h3>{definitionNameLabel(definition.name || definition.definitionID, language)}</h3>
          <code>{definition.definitionID}</code>
          <span className="global-symbology-type">{vectorTypeLabel(definition.vectorType, language)}</span>
        </div>
        <div className="global-symbology-option-actions">
          {definition.isCustomized && <span className="global-symbology-customized">{zh ? '已自定义' : 'Customized'}</span>}
          {changed && <span className="global-symbology-draft-badge">{zh ? '未保存' : 'Unsaved'}</span>}
          <button type="button" className="global-symbology-secondary" disabled={busy}
            onClick={onReset} aria-label={zh ? `恢复 ${definitionNameLabel(definition.name, language)} 默认值` : `Restore ${definition.name} defaults`}>
            {zh ? '恢复默认' : 'Restore default'}
          </button>
        </div>
      </header>
      <div className="global-symbology-properties">
        {definition.editableProperties.map(property => (
          <GlobalSymbologyField key={property} property={property} values={definition.values}
            language={language} disabled={busy} onChange={onChange} />
        ))}
      </div>
    </article>
  );
}

interface FieldProps {
  property: GlobalSymbologyProperty;
  values: GlobalSymbologyValues;
  language: Language;
  disabled: boolean;
  onChange: (change: Partial<GlobalSymbologyValues>) => void;
}

function GlobalSymbologyField({ property, values, language, disabled, onChange }: FieldProps) {
  const zh = language === 'zh';
  const label = propertyLabel(property, language);
  const value = values[property];
  if (propertyUsesColor(property)) {
    const color = String(value);
    const alpha = Number.parseInt(color.slice(1, 3), 16);
    const rgb = `#${color.slice(3)}`;
    return (
      <label className="global-symbology-field global-symbology-color-field">
        <span>{label}</span>
        <span className="global-symbology-color-control">
          <input type="color" value={rgb} disabled={disabled} aria-label={label}
            onChange={event => onChange({ [property]: `#${color.slice(1, 3)}${event.currentTarget.value.slice(1)}` } as Partial<GlobalSymbologyValues>)} />
          <code>{color}</code>
        </span>
        <span className="global-symbology-alpha-row">
          <span>{zh ? '不透明度' : 'Opacity'}</span>
          <input type="range" min="0" max="255" step="1" value={Number.isFinite(alpha) ? alpha : 255}
            disabled={disabled} aria-label={`${label} ${zh ? '不透明度' : 'opacity'}`}
            onChange={event => {
              const nextAlpha = Math.round(Number(event.currentTarget.value)).toString(16).padStart(2, '0').toUpperCase();
              onChange({ [property]: `#${nextAlpha}${color.slice(3)}` } as Partial<GlobalSymbologyValues>);
            }} />
        </span>
      </label>
    );
  }

  const integer = property === 'pointSize' || property === 'textEmphasis';
  return (
    <label className="global-symbology-field">
      <span>{label}</span>
      <input type="number" min="0" max="10000" step={integer ? 1 : 0.1} value={Number(value)} disabled={disabled}
        onChange={event => {
          const next = event.currentTarget.valueAsNumber;
          if (Number.isFinite(next) && next >= 0 && next <= 10000) {
            onChange({ [property]: integer ? Math.round(next) : next } as Partial<GlobalSymbologyValues>);
          }
        }} />
    </label>
  );
}

function propertyLabel(property: GlobalSymbologyProperty, language: Language): string {
  return globalSymbologyPropertyLabel(property, language);
}

function layerLabel(layerName: string, language: Language): string {
  if (language === 'en') return layerName || 'Other';
  const labels: Record<string, string> = {
    'Boundary Condition Line': '边界条件线', 'Conceptual Mesh': '概念网格', 'Culvert Barrels': '涵洞管段',
    'GDB Vector': 'GDB 矢量', 'Generic Point Layer': '通用点图层', 'Generic Polygon Layer': '通用面图层',
    'Generic Polyline Layer': '通用线图层', Graph: '图结构', 'Map Labels': '地图标注', Mesh: '网格',
    'Point Map Layer': '点地图图层', 'Polygon Map Layer': '面地图图层', 'Polyline Map Layer': '线地图图层',
    Point: '点', Points: '点', Line: '线', Lines: '线', Polyline: '线', Polygon: '面', Polygons: '面',
    Text: '文字', Label: '文字', Geometry: '几何图层', 'Map Layer': '地图图层',
  };
  return labels[layerName] ?? (layerName || '其他');
}

function vectorTypeLabel(vectorType: string, language: Language): string {
  if (language === 'en') return vectorType;
  return ({ Point: '点', Line: '线', Polygon: '面', Text: '文字' } as Record<string, string>)[vectorType] ?? vectorType;
}

function definitionNameLabel(name: string, language: Language): string {
  if (language === 'en') return name;
  return ({
    Arc: '弧线', Node: '节点', 'Arc Internal Vertex': '弧线内部顶点', 'Deletable Node': '可删除节点',
    Face: '网格面', Vertex: '顶点', Point: '点', Polyline: '折线', Polygon: '多边形', Text: '文字', Line: '线',
  } as Record<string, string>)[name] ?? name;
}

function sameValues(left: GlobalSymbologyValues, right: GlobalSymbologyValues): boolean {
  return left.penColor === right.penColor && left.penWidth === right.penWidth && left.fillColor === right.fillColor &&
    left.pointSize === right.pointSize && left.haloColor === right.haloColor && left.haloThickness === right.haloThickness &&
    left.textEmphasis === right.textEmphasis;
}

function formatDiagnostics(value: unknown): string {
  if (typeof value === 'string') return value;
  try { return JSON.stringify(value, null, 2); }
  catch { return String(value); }
}
