import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { errorDiagnostics, message } from '../../../shared/workbench';
import type { GlobalSymbologySnapshot } from '../global-symbology/globalSymbologyWorkflow';
import type { VectorStyleProperty } from './vectorRenderTypes';
import type {
  MapVectorPlotStyle,
  VectorLayerSource,
  VectorLayerStyleEditorProps,
  VectorLayerSymbologySnapshot,
  VectorPlotOptionDescriptor,
  VectorProjectCondition,
  VectorRenderPreviewDraft,
  VectorStyleValues,
} from './vectorRenderTypes';
import {
  changedEnabledPlotOptions,
  hasSourceFeatureOverrides,
  isValidStyleDraft,
  parseGlobalVectorSymbology,
  parseVectorLayerSymbologySnapshot,
  plotOptionBaseline,
  resetStyleToGlobal,
  rebaseSourceOverrides,
  sameStyleDraft,
  serializeStylesForSave,
  updateStyleValue,
  vectorLayerSymbologyMethods,
  vectorLayerSymbologyRequest,
} from './vectorRenderWorkflow';
import { VectorPlotOptionsEditor } from './VectorPlotOptionsEditor';
import { VectorStyleDefinitionEditor } from './VectorStyleDefinitionEditor';
import './vectorRender.css';

type Phase = 'idle' | 'loading' | 'ready' | 'failed';
type BusyAction = 'inspect' | 'save' | null;

function sourceIdentity(source: VectorLayerSource | null): string {
  if (!source) return '';
  return JSON.stringify([source.key, source.expectedProjectRevision, source.map, source.node,
    source.layerID, source.expectedFamily, source.sourceRevision]);
}

function isRevision(value: unknown): value is string {
  return typeof value === 'string' && /^[\da-f]{64}$/i.test(value);
}

function isReadySource(source: VectorLayerSource | null): source is VectorLayerSource {
  return Boolean(source && source.key.trim() && source.map.projectPath.trim() &&
    source.map.projectSessionID.trim() && source.map.mapID.trim() && source.map.mapName.trim() &&
    isRevision(source.expectedProjectRevision) && isRevision(source.sourceRevision) &&
    source.layerID.trim() && source.node.nodeName.trim() &&
    (!source.expectedFamily || isRevision(source.expectedFamily.revision)));
}

function hasValidViewport(viewport: VectorLayerStyleEditorProps['viewport']): boolean {
  return Boolean(viewport && Number.isFinite(viewport.minX) && Number.isFinite(viewport.minY) &&
    Number.isFinite(viewport.maxX) && Number.isFinite(viewport.maxY) &&
    viewport.minX < viewport.maxX && viewport.minY < viewport.maxY &&
    Number.isInteger(viewport.pixelWidth) && viewport.pixelWidth > 0 &&
    Number.isInteger(viewport.pixelHeight) && viewport.pixelHeight > 0);
}

function optionsAreValid(options: VectorPlotOptionDescriptor[], draft: Record<string, string>): boolean {
  return options.every(option => {
    const value = draft[option.propertyName];
    if (typeof value !== 'string') return false;
    if (option.kind === 'bool') return value === 'true' || value === 'false';
    if (option.kind === 'enum') return option.enumValues.some(choice => choice.value === value);
    return value === option.value;
  });
}

function diagnosticsText(error: unknown): string {
  try { return JSON.stringify(errorDiagnostics(error), null, 2) ?? String(error); }
  catch { return String(error); }
}

function styleValueDefaults(snapshot: GlobalSymbologySnapshot | null): Map<string, VectorStyleValues> {
  if (!snapshot) return new Map();
  return new Map(snapshot.groups.flatMap(group => group.options.map(option => [option.definitionID, option.values] as const)));
}

function optionValuesFrom(snapshot: VectorLayerSymbologySnapshot): Record<string, string> {
  return plotOptionBaseline(snapshot.plotOptions);
}

function previewDraft(styles: MapVectorPlotStyle[], options: VectorPlotOptionDescriptor[],
  conditions: VectorProjectCondition[],
  initialOptions: Record<string, string>, draftOptions: Record<string, string>): VectorRenderPreviewDraft {
  return {
    styles: rebaseSourceOverrides(styles, conditions),
    plotOptions: changedEnabledPlotOptions(options, initialOptions, draftOptions),
  };
}

export function VectorLayerStyleEditor({
  backend,
  language,
  sourceCapture,
  viewport,
  onReceipt,
  onPreviewChange,
  onCommitStateChange,
  onDirtyStateChange,
  settingsEpoch,
  hasUnsavedDrafts = false,
  dirtyOwners,
}: VectorLayerStyleEditorProps) {
  const zh = language === 'zh';
  const sourceKey = sourceIdentity(sourceCapture);
  const dirtyOwner = `map.vector.style:${sourceCapture?.key ?? 'unselected'}`;
  const [phase, setPhase] = useState<Phase>('idle');
  const [busyAction, setBusyAction] = useState<BusyAction>(null);
  const [inspection, setInspection] = useState<VectorLayerSymbologySnapshot | null>(null);
  const [styles, setStyles] = useState<MapVectorPlotStyle[]>([]);
  const [plotOptionValues, setPlotOptionValues] = useState<Record<string, string>>({});
  const [globalSnapshot, setGlobalSnapshot] = useState<GlobalSymbologySnapshot | null>(null);
  const [error, setError] = useState<unknown | null>(null);
  const [status, setStatus] = useState('');
  const [invalidFields, setInvalidFields] = useState<Set<string>>(() => new Set());
  const [inspectionNonce, setInspectionNonce] = useState(0);
  const generationRef = useRef(0);
  const activeControllerRef = useRef<AbortController | null>(null);
  const sourceRef = useRef(sourceCapture);
  sourceRef.current = sourceCapture;
  const previewCallbackRef = useRef(onPreviewChange);
  previewCallbackRef.current = onPreviewChange;
  const receiptCallbackRef = useRef(onReceipt);
  receiptCallbackRef.current = onReceipt;
  const commitStateCallbackRef = useRef(onCommitStateChange);
  commitStateCallbackRef.current = onCommitStateChange;
  const dirtyCallbackRef = useRef(onDirtyStateChange);
  dirtyCallbackRef.current = onDirtyStateChange;
  const mountedRef = useRef(false);
  const settingsEpochRef = useRef(settingsEpoch);
  const deferredSettingsRefreshRef = useRef(false);
  const callbackSourceRef = useRef(sourceCapture);
  const dirty = useMemo(() => Boolean(inspection && !sameStyleDraft(styles, inspection.styles)), [styles, inspection]);
  const initialPlotOptions = useMemo(() => inspection ? optionValuesFrom(inspection) : {}, [inspection]);
  const globalValues = useMemo(() => styleValueDefaults(globalSnapshot), [globalSnapshot]);
  const globalRevisionMatches = Boolean(inspection && globalSnapshot &&
    inspection.globalSymbologyRevision.toUpperCase() === globalSnapshot.revision.toUpperCase());
  const sourceOverridesPresent = hasSourceFeatureOverrides(styles);
  const sourceMatchesInspection = Boolean(sourceCapture && inspection &&
    inspection.projectSessionID.toUpperCase() === sourceCapture.map.projectSessionID.toUpperCase() &&
    inspection.projectRevision.toUpperCase() === sourceCapture.expectedProjectRevision.toUpperCase() &&
    inspection.layerID === sourceCapture.layerID);
  const hasOtherDirtyOwners = dirtyOwners
    ? Array.from(dirtyOwners).some(owner => owner !== dirtyOwner)
    : false;
  const blockedByOtherDrafts = hasUnsavedDrafts && (dirtyOwners ? hasOtherDirtyOwners : true);
  const hasInvalidPlotValues = inspection !== null && !optionsAreValid(inspection.plotOptions, plotOptionValues);
  const stylesAreValid = isValidStyleDraft(styles) && invalidFields.size === 0;
  const previewPreparation = useMemo(() => {
    if (!inspection) return { styles: null as MapVectorPlotStyle[] | null, error: '' };
    if (inspection.hasUnsupportedProjectConditions) {
      return { styles: null, error: zh
        ? '当前来源的条件样式无法安全绑定到矢量要素；应用预览已停用。'
        : 'Conditional styles cannot be safely bound to this vector source. Preview is disabled.' };
    }
    if (inspection.hasUnknownProjectSymbologyContent) {
      return { styles: null, error: zh
        ? '工程含有未识别的样式内容；为避免用基础样式覆盖未知绘制规则，应用预览已停用。'
        : 'The project contains unknown styling content. Preview is disabled to avoid overriding unknown rendering rules.' };
    }
    try {
      return { styles: rebaseSourceOverrides(styles, inspection.projectConditions), error: '' };
    } catch (prepareError) {
      return { styles: null, error: message(prepareError, language) };
    }
  }, [inspection, styles, zh, language]);
  const saveDisabledReason = !sourceMatchesInspection
    ? (zh ? '当前来源身份尚未通过检查。' : 'The selected source has not passed inspection.')
    : hasOtherDirtyOwners
      ? (zh ? '请先保存或清除其他未保存的绘图草稿。' : 'Save or clear the other unsaved drawing drafts first.')
      : !dirty
        ? (zh ? '基础样式没有待保存的更改。' : 'There are no base-style changes to save.')
        : !stylesAreValid
          ? (zh ? '请先修正无效的样式输入。' : 'Correct invalid style values before saving.')
          : styles.some(style => style.isProjectOverride === null ||
            !inspection?.definitions.some(definition => definition.definitionID === style.definitionID))
              ? (zh ? '服务端没有确认样式来源，不能安全保存。' : 'The renderer did not confirm the style source, so saving is disabled.')
              : globalSnapshot && !globalRevisionMatches
                ? (zh ? '全局默认值已变化，请重新读取图层后再保存。' : 'Global defaults changed. Reinspect this layer before saving.')
                : '';

  const clearPreview = useCallback((source: VectorLayerSource | null = sourceRef.current) => {
    if (source) previewCallbackRef.current(source, null);
  }, []);

  useEffect(() => {
    if (settingsEpochRef.current !== settingsEpoch) {
      settingsEpochRef.current = settingsEpoch;
      if (dirty) {
        deferredSettingsRefreshRef.current = true;
        setStatus(zh ? '全局默认值已变化；请先处理当前草稿，再重新读取图层。' : 'Global defaults changed. Resolve the current draft before reloading this layer.');
        return;
      }
      deferredSettingsRefreshRef.current = false;
      clearPreview(sourceRef.current);
      setInspectionNonce(value => value + 1);
      return;
    }
    if (deferredSettingsRefreshRef.current && !dirty) {
      deferredSettingsRefreshRef.current = false;
      clearPreview(sourceRef.current);
      setInspectionNonce(value => value + 1);
    }
  }, [settingsEpoch, dirty, clearPreview, zh]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      generationRef.current++;
      activeControllerRef.current?.abort();
      activeControllerRef.current = null;
    };
  }, []);

  useEffect(() => {
    const currentSource = sourceCapture;
    const requestKey = sourceKey;
    const generation = ++generationRef.current;
    activeControllerRef.current?.abort();
    activeControllerRef.current = null;
    setInspection(null);
    setStyles([]);
    setPlotOptionValues({});
    setGlobalSnapshot(null);
    setInvalidFields(new Set());
    setError(null);
    setStatus('');
    setBusyAction(null);
    clearPreview(callbackSourceRef.current);
    callbackSourceRef.current = currentSource;
    dirtyCallbackRef.current?.(dirtyOwner, false);

    if (!currentSource) {
      setPhase('idle');
      return () => {
        clearPreview(currentSource);
        dirtyCallbackRef.current?.(dirtyOwner, false);
      };
    }
    if (!isReadySource(currentSource)) {
      setPhase('failed');
      setError(new Error(zh ? '缺少已验证的地图图层、工程会话或矢量来源修订。' : 'The saved map layer, project session, or source revision is incomplete.'));
      return () => {
        clearPreview(currentSource);
        dirtyCallbackRef.current?.(dirtyOwner, false);
      };
    }
    if (!backend.supports(vectorLayerSymbologyMethods.inspect)) {
      setPhase('failed');
      setError(new Error(zh ? '当前服务尚未发布矢量图层符号检查操作。' : 'The service has not advertised vector layer symbology inspection.'));
      return () => {
        clearPreview(currentSource);
        dirtyCallbackRef.current?.(dirtyOwner, false);
      };
    }

    const controller = new AbortController();
    activeControllerRef.current = controller;
    setPhase('loading');
    setBusyAction('inspect');
    void (async () => {
      try {
        const response = await backend.request(vectorLayerSymbologyMethods.inspect,
          vectorLayerSymbologyRequest(currentSource), { signal: controller.signal });
        if (!mountedRef.current || controller.signal.aborted || generation !== generationRef.current ||
            sourceIdentity(sourceRef.current) !== requestKey) return;
        const parsed = parseVectorLayerSymbologySnapshot(response, currentSource);
        let defaults: GlobalSymbologySnapshot | null = null;
        if (backend.supports(vectorLayerSymbologyMethods.globalInspect)) {
          const globalResponse = await backend.request(vectorLayerSymbologyMethods.globalInspect, {}, { signal: controller.signal });
          if (!mountedRef.current || controller.signal.aborted || generation !== generationRef.current ||
              sourceIdentity(sourceRef.current) !== requestKey) return;
          defaults = parseGlobalVectorSymbology(globalResponse);
          if (defaults.revision.toUpperCase() !== parsed.globalSymbologyRevision.toUpperCase()) {
            throw new Error(zh
              ? '读取图层符号期间，全局默认值发生了变化。请重新读取后再编辑。'
              : 'Global defaults changed while the layer was being inspected. Reload before editing.');
          }
        }
        setInspection(parsed);
        setStyles(parsed.styles.map(style => ({ ...style, base: { ...style.base }, sourceOverrides: [...style.sourceOverrides] })));
        setPlotOptionValues(optionValuesFrom(parsed));
        setGlobalSnapshot(defaults);
        setPhase('ready');
        setStatus(zh ? '已读取当前矢量图层样式和绘图选项。' : 'Layer styles and drawing options were inspected.');
      } catch (requestError) {
        if (mountedRef.current && !controller.signal.aborted && generation === generationRef.current) {
          setError(requestError);
          setPhase('failed');
        }
      } finally {
        if (activeControllerRef.current === controller && generation === generationRef.current) {
          activeControllerRef.current = null;
          setBusyAction(null);
        }
      }
    })();
    return () => {
      controller.abort();
      if (activeControllerRef.current === controller) activeControllerRef.current = null;
      generationRef.current++;
      clearPreview(currentSource);
      dirtyCallbackRef.current?.(dirtyOwner, false);
    };
  }, [backend, sourceKey, inspectionNonce, dirtyOwner, clearPreview, zh]);

  useEffect(() => {
    dirtyCallbackRef.current?.(dirtyOwner, dirty);
    return () => dirtyCallbackRef.current?.(dirtyOwner, false);
  }, [dirty, dirtyOwner]);

  useEffect(() => {
    commitStateCallbackRef.current?.(busyAction === 'save');
    return () => commitStateCallbackRef.current?.(false);
  }, [busyAction]);

  const currentSourceIsActive = useCallback((requestSource: VectorLayerSource): boolean =>
    mountedRef.current && sourceIdentity(sourceRef.current) === sourceIdentity(requestSource), []);

  const changeStyleValue = (definitionID: string, property: VectorStyleProperty, value: string | number) => {
    setStyles(current => updateStyleValue(current, definitionID, property, value));
    setInvalidFields(current => {
      const next = new Set(current);
      next.delete(`${definitionID}:${property}`);
      return next;
    });
  };

  const markFieldValidity = (definitionID: string, property: VectorStyleProperty, invalid: boolean) => {
    const key = `${definitionID}:${property}`;
    setInvalidFields(current => {
      if (current.has(key) === invalid) return current;
      const next = new Set(current);
      if (invalid) next.add(key);
      else next.delete(key);
      return next;
    });
  };

  const onApplyToView = () => {
    const currentSource = sourceRef.current;
    if (!currentSource || !inspection || !previewPreparation.styles || !stylesAreValid || hasInvalidPlotValues || !hasValidViewport(viewport)) return;
    try {
      const draft = previewDraft(styles, inspection.plotOptions, inspection.projectConditions,
        initialPlotOptions, plotOptionValues);
      previewCallbackRef.current(currentSource, draft);
      setStatus(zh ? '已请求将当前基础样式和绘图选项应用到地图视图。' : 'Requested the current base styles and drawing options for this map view.');
    } catch (prepareError) {
      setError(prepareError);
    }
  };

  const onResetPreview = () => {
    clearPreview(sourceRef.current);
    setStatus(zh ? '已清除临时预览，恢复地图当前已确认的绘制结果。' : 'Cleared the temporary preview and restored the confirmed map rendering.');
  };

  const onRestoreGlobal = (definitionID: string) => {
    const values = globalValues.get(definitionID);
    if (!values || !inspection || !globalRevisionMatches) return;
    setStyles(current => resetStyleToGlobal(current, definitionID, values,
      inspection.projectConditions.some(condition => condition.definitionID === definitionID)));
    setInvalidFields(current => {
      const next = new Set(Array.from(current).filter(key => !key.startsWith(`${definitionID}:`)));
      return next;
    });
    setStatus(zh ? '已将基础值恢复为读取到的全局默认；条件和未知 .vis 内容仍会保留。' : 'Base values were reset to the inspected global defaults; existing conditions and unknown .vis content remain preserved.');
  };

  const onSaveStyles = async () => {
    const currentSource = sourceRef.current;
    if (!currentSource || !inspection || !backend.supports(vectorLayerSymbologyMethods.save) || saveDisabledReason) return;
    activeControllerRef.current?.abort();
    const controller = new AbortController();
    activeControllerRef.current = controller;
    const generation = ++generationRef.current;
    const requestKey = sourceIdentity(currentSource);
    setBusyAction('save');
    setError(null);
    setStatus('');
    try {
      const receiptValue = await backend.request(vectorLayerSymbologyMethods.save, {
        ...vectorLayerSymbologyRequest(currentSource),
        expectedStyleSHA256: inspection.styleSHA256,
        expectedGlobalSymbologyRevision: inspection.globalSymbologyRevision,
        styles: serializeStylesForSave(styles),
      }, { signal: controller.signal });
      if (!currentSourceIsActive(currentSource) || controller.signal.aborted || generation !== generationRef.current ||
          sourceIdentity(sourceRef.current) !== requestKey) return;
      const receipt = parseVectorLayerSymbologySnapshot(receiptValue, currentSource, true);
      clearPreview(currentSource);
      if (!receiptCallbackRef.current(currentSource, receipt)) {
        setError(new Error(zh
          ? '保存回执属于旧的地图图层会话，未安装到当前视图。请重新读取当前图层。'
          : 'The save receipt belongs to an old map layer session and was not adopted. Reinspect the active layer.'));
        return;
      }
      setInspection(receipt);
      setStyles(receipt.styles.map(style => ({ ...style, base: { ...style.base }, sourceOverrides: [...style.sourceOverrides] })));
      setPlotOptionValues(optionValuesFrom(receipt));
      setStatus(zh ? '图层基础样式已保存，地图会话已接收服务回执。绘图选项仍仅作用于当前视图。' : 'Base styles were saved and the map session accepted the receipt. Drawing options remain view-only.');
    } catch (requestError) {
      if (mountedRef.current && !controller.signal.aborted && generation === generationRef.current) setError(requestError);
    } finally {
      if (activeControllerRef.current === controller && generation === generationRef.current) {
        activeControllerRef.current = null;
        setBusyAction(null);
      }
    }
  };

  const onReload = () => {
    if (dirty) return;
    clearPreview(sourceRef.current);
    setInspectionNonce(value => value + 1);
  };

  const diagnostics = error === null ? '' : diagnosticsText(error);
  const phaseText = phase === 'loading'
    ? (busyAction === 'save' ? (zh ? '正在保存图层样式…' : 'Saving layer styles…') : (zh ? '正在检查矢量图层…' : 'Inspecting vector layer…'))
    : phase === 'ready' ? (busyAction === 'save' ? (zh ? '正在保存…' : 'Saving…') : status || (zh ? '已就绪' : 'Ready'))
      : phase === 'failed' ? (zh ? '不可用' : 'Unavailable') : (zh ? '选择已保存的矢量图层' : 'Select a saved vector layer');
  const previewDisabled = phase !== 'ready' || busyAction !== null || !hasValidViewport(viewport) || !previewPreparation.styles ||
    !stylesAreValid || hasInvalidPlotValues || Boolean(globalSnapshot && !globalRevisionMatches);
  const saveDisabled = phase !== 'ready' || busyAction !== null || !backend.supports(vectorLayerSymbologyMethods.save) ||
    Boolean(saveDisabledReason) || blockedByOtherDrafts;

  return (
    <section className="vector-layer-style-editor" data-testid="vector-layer-style-editor" data-source-key={sourceCapture?.key ?? ''}>
      <header className="vector-render-header">
        <div>
          <p className="vector-render-eyebrow">{zh ? '地图矢量图层' : 'Map vector layer'}</p>
          <h3>{zh ? '符号与绘图选项' : 'Symbology and drawing options'}</h3>
          {sourceCapture && <>
            <code className="vector-render-source-path">{inspection?.sourcePath ?? sourceCapture.expectedFamily?.sourcePath ?? sourceCapture.node.filename ?? sourceCapture.node.assetID ?? '—'}</code>
            <span className="vector-render-source-revision">SHA-256: {sourceCapture.sourceRevision}</span>
          </>}
        </div>
        <div className="vector-render-status" role="status" aria-live="polite">{phaseText}</div>
      </header>

      {!sourceCapture && <div className="vector-render-empty-state">
        {zh ? '在地图图层树中选择一个已保存的 GeoJSON、Shapefile 或 GDB 矢量图层。' : 'Select a saved GeoJSON, Shapefile, or GDB vector layer in the map tree.'}
      </div>}

      {sourceCapture && phase === 'loading' && <p className="vector-render-loading" role="status">{phaseText}</p>}
      {sourceCapture && error !== null && <div className="vector-render-error" role="alert">
        <p>{message(error, language)}</p>
        <details><summary>{zh ? '服务诊断' : 'Service diagnostics'}</summary><pre>{diagnostics}</pre></details>
      </div>}

      {inspection && phase === 'ready' && <>
        <section className="vector-render-identity" aria-label={zh ? '当前来源身份' : 'Current source identity'}>
          <span>{zh ? '工程修订' : 'Project revision'} <code>{inspection.projectRevision}</code></span>
          <span>{zh ? '地图修订' : 'Map revision'} <code>{inspection.visualizationRevision ?? (zh ? '尚无 .vis 修订' : 'No .vis revision')}</code></span>
          <span>{zh ? '符号快照' : 'Style snapshot'} <code>{inspection.styleSHA256}</code></span>
        </section>

        {inspection.hasUnsupportedProjectConditions && <p className="vector-render-warning" role="note">
          {zh ? '存在无法安全匹配到当前矢量要素的条件式样式。条件内容会保留，但本视图的临时预览已停用。' : 'Conditional styling cannot be safely matched to this vector source. Conditions are preserved, and temporary preview is disabled.'}
        </p>}
        {inspection.hasUnknownProjectSymbologyContent && <p className="vector-render-warning" role="note">
          {zh ? '工程中含有未识别的符号 XML。基础样式保存会由服务保留这些内容。' : 'The project contains unrecognized symbology XML. The service preserves it when base styles are saved.'}
        </p>}
        {globalSnapshot && !globalRevisionMatches && <p className="vector-render-warning" role="alert">
          {zh ? '全局默认值修订与图层检查不一致；请重新读取，当前不允许保存或恢复默认。' : 'The global-default revision differs from the layer inspection. Reload before saving or restoring defaults.'}
        </p>}
        {!globalSnapshot && <p className="vector-render-muted">
          {zh ? '全局默认值快照不可用。当前仍可检查图层来源，恢复默认按钮保持禁用。' : 'The global-default snapshot is unavailable. Layer provenance remains visible, while restore-default actions stay disabled.'}
        </p>}

        <section className="vector-render-style-list" aria-label={zh ? '基础符号样式' : 'Base symbology'}>
          <div className="vector-render-section-heading">
            <div><h4>{zh ? '基础符号' : 'Base symbology'}</h4>
              <p>{zh ? '编辑基础颜色和尺寸。显式 .vis 样式显示为工程覆盖；无显式样式时继承全局默认。' : 'Edit base colors and sizes. An explicit .vis entry is a project override; otherwise the layer inherits global defaults.'}</p>
            </div>
            <span className="vector-render-provenance-revision">{zh ? '全局默认修订' : 'Global defaults'}: {inspection.globalSymbologyRevision}</span>
          </div>
          {styles.map(style => {
            const definition = inspection.definitions.find(item => item.definitionID === style.definitionID);
            if (!definition) return <p className="vector-render-warning" role="note" key={style.definitionID}>
              {zh ? '服务端返回了缺少定义元数据的样式，保持只读。' : 'The service returned a style without definition metadata. It remains read-only.'} <code>{style.definitionID}</code>
            </p>;
            return <VectorStyleDefinitionEditor key={style.definitionID} style={style} definition={definition}
            language={language} disabled={busyAction !== null || phase !== 'ready'}
            globalValues={globalValues.get(style.definitionID)} canResetToGlobal={globalRevisionMatches}
            onResetToGlobal={onRestoreGlobal} onChange={changeStyleValue} onValidityChange={markFieldValidity} />;
          })}
          {styles.length === 0 && <p className="vector-render-empty-options">{zh ? '此图层没有可编辑的基础符号定义。' : 'This layer has no editable base style definitions.'}</p>}
        </section>

        <VectorPlotOptionsEditor options={inspection.plotOptions} values={plotOptionValues} language={language}
          disabled={busyAction !== null || phase !== 'ready'}
          onChange={(propertyName, value) => setPlotOptionValues(current => ({ ...current, [propertyName]: value }))} />

        {inspection.projectConditions.length > 0 && <details className="vector-render-conditions">
          <summary>{zh ? `保留的条件式样式（${inspection.projectConditions.length} 组，只读）` : `Preserved conditional styles (${inspection.projectConditions.length}, read-only)`}</summary>
          {inspection.projectConditions.map((group, index) => <div className="vector-render-condition" key={`${group.definitionID}-${group.property}-${index}`}>
            <code>{group.definitionID}</code><code>{group.property}</code>
            {group.conditions.map((condition, conditionIndex) => <code key={conditionIndex}>
              {condition.column} {condition.operator} {condition.threshold} → {condition.resultValue}
            </code>)}
          </div>)}
        </details>}

        {!hasValidViewport(viewport) && <p className="vector-render-warning" role="note">
          {zh ? '地图当前没有有效的相机范围；保存样式仍可用，应用到视图暂不可用。' : 'The map has no valid camera extent. Saving styles is available, but view rendering is not.'}
        </p>}

        <footer className="vector-render-actions">
          <button type="button" className="vector-render-secondary-button" disabled={previewDisabled}
            data-testid="vector-render-apply-to-view" onClick={onApplyToView}>
            {zh ? '应用到当前视图' : 'Apply to current view'}
          </button>
          <button type="button" className="vector-render-secondary-button" disabled={busyAction !== null}
            data-testid="vector-render-clear-preview" onClick={onResetPreview}>
            {zh ? '清除临时预览' : 'Clear temporary preview'}
          </button>
          <button type="button" className="vector-render-primary-button" disabled={saveDisabled}
            title={blockedByOtherDrafts ? (zh ? '其他未保存绘图草稿需要先处理。' : 'Other unsaved drawing drafts must be resolved first.') : saveDisabledReason || undefined}
            data-testid="vector-render-save-styles" onClick={() => void onSaveStyles()}>
            {busyAction === 'save' ? (zh ? '正在保存…' : 'Saving…') : (zh ? '保存图层样式' : 'Save layer styles')}
          </button>
          <button type="button" className="vector-render-secondary-button" disabled={busyAction !== null || dirty}
            data-testid="vector-render-reinspect" onClick={onReload}>
            {dirty ? (zh ? '先保存或放弃草稿再读取' : 'Save or discard the draft before reloading') : (zh ? '重新读取' : 'Reload')}
          </button>
        </footer>
        {sourceOverridesPresent && <p className="vector-render-muted" role="note">
          {zh ? '来源条件结果为只读数据。保存只提交基础值，服务会保留工程条件并在回执中重新计算结果。' : 'Source-condition results are read-only. Saving submits base values only; the service preserves project rules and recalculates results in the receipt.'}
        </p>}
        {previewPreparation.error && <p className="vector-render-warning" role="note">{previewPreparation.error}</p>}
        {saveDisabledReason && <p className="vector-render-muted" role="note">{saveDisabledReason}</p>}
        {hasInvalidPlotValues && <p className="vector-render-warning" role="alert">
          {zh ? '绘图选项草稿包含无效或不受支持的值，无法应用到视图。' : 'The drawing-option draft contains invalid or unsupported values and cannot be applied.'}
        </p>}
      </>}
    </section>
  );
}
