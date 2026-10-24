import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../shared/workbench';
import { message } from '../shared/workbench';
import { FramedContentDialog } from './FramedContentDialog';

interface UnitChoice {
  id: string;
  name: string;
  unitTypeName: string;
}

interface LoadResult {
  fromUnits: UnitChoice[];
  toUnits: UnitChoice[];
  fromUnitId: string | null;
  fromUnitName: string | null;
  unitTypeName: string | null;
  toUnitId: string | null;
}

interface ConvertResult {
  value: number;
}

interface ActiveRequest {
  generation: number;
  identity: string;
  backend: BackendGateway;
  controller: AbortController;
}

function parseLoadResult(value: unknown, sourceUnitId: string, targetUnitId: string): LoadResult {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('单位服务返回的数据不是对象。');
  const raw = value as Record<string, unknown>;
  const isUnit = (unit: unknown): unit is UnitChoice => Boolean(unit && typeof unit === 'object' && !Array.isArray(unit) &&
    typeof (unit as Record<string, unknown>).id === 'string' &&
    typeof (unit as Record<string, unknown>).name === 'string' &&
    typeof (unit as Record<string, unknown>).unitTypeName === 'string');
  if (!Array.isArray(raw.fromUnits) || !Array.isArray(raw.toUnits) ||
    !raw.fromUnits.every(isUnit) || !raw.toUnits.every(isUnit)) {
    throw new Error('单位服务返回的单位目录不完整。');
  }
  if (raw.fromUnitId !== sourceUnitId || raw.toUnitId !== targetUnitId ||
    !raw.fromUnits.some((unit) => unit.id === sourceUnitId) ||
    !raw.toUnits.some((unit) => unit.id === targetUnitId)) {
    throw new Error('当前源单位或显示单位已不在 C# 单位目录中。');
  }
  return value as LoadResult;
}

function parseConvertResult(value: unknown): ConvertResult {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
    typeof (value as Record<string, unknown>).value !== 'number' || !Number.isFinite((value as Record<string, number>).value)) {
    throw new Error('单位服务没有返回有限数值结果。');
  }
  return value as ConvertResult;
}

function parseFiniteInput(value: string): number | null {
  if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(value)) return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function UnitCalculatorButton({
  backend,
  language,
  fieldID,
  fieldTitle,
  sourceUnitId,
  sourceUnitName,
  displayUnitId,
  displayUnitName,
  initialSourceValue,
  disabled,
  onApply,
}: {
  backend: BackendGateway;
  language: Language;
  fieldID: string;
  fieldTitle: string;
  /** Exact unit ID supplied by DisplayUnitData.sourceUnitTypeId. */
  sourceUnitId: string;
  sourceUnitName: string;
  /** Exact unit ID supplied by the selected DisplayUnitChoice.unitTypeId. */
  displayUnitId: string;
  displayUnitName: string;
  /** The editor draft value, still expressed in the field's source unit. */
  initialSourceValue: string;
  disabled: boolean;
  /** Receives a value in displayUnitId; the owning field performs its normal inverse conversion and validation. */
  onApply: (displayValue: string) => void;
}) {
  const zh = language === 'zh';
  const supportsLoad = backend.supports('unitCalculator.load');
  const supportsConvert = backend.supports('unitCalculator.convert');
  const supportsService = supportsLoad && supportsConvert;
  const hasFiniteInitialValue = parseFiniteInput(initialSourceValue) !== null;
  const identity = JSON.stringify([fieldID, sourceUnitId, displayUnitId, initialSourceValue, disabled]);
  const identityRef = useRef(identity);
  identityRef.current = identity;
  const backendRef = useRef(backend);
  backendRef.current = backend;
  const languageRef = useRef(language);
  languageRef.current = language;
  const openRef = useRef(false);
  const generation = useRef(0);
  const activeRequest = useRef<ActiveRequest | null>(null);
  const sourceValueRef = useRef(initialSourceValue);
  const openedIdentity = useRef<string | null>(null);
  const openedBackend = useRef<BackendGateway | null>(null);
  const resultIdentity = useRef<string | null>(null);
  const [open, setOpen] = useState(false);
  const [catalog, setCatalog] = useState<LoadResult>();
  const [sourceValue, setSourceValue] = useState(initialSourceValue);
  const [result, setResult] = useState<ConvertResult>();
  const [busy, setBusy] = useState<'loading' | 'converting' | null>(null);
  const [error, setError] = useState<string>();

  const invalidate = useCallback((): void => {
    generation.current += 1;
    activeRequest.current?.controller.abort();
    activeRequest.current = null;
  }, []);

  const requestIsCurrent = (request: ActiveRequest): boolean =>
    activeRequest.current === request && generation.current === request.generation &&
    identityRef.current === request.identity && backendRef.current === request.backend &&
    openRef.current && !request.controller.signal.aborted;

  const beginRequest = (requestIdentity: string): ActiveRequest => {
    invalidate();
    const request = { generation: generation.current, identity: requestIdentity, backend, controller: new AbortController() };
    activeRequest.current = request;
    return request;
  };

  const close = useCallback((): void => {
    openRef.current = false;
    invalidate();
    openedIdentity.current = null;
    openedBackend.current = null;
    resultIdentity.current = null;
    setOpen(false);
    setBusy(null);
  }, [invalidate]);

  useEffect(() => {
    if (openedIdentity.current !== null && (openedIdentity.current !== identity || openedBackend.current !== backend)) {
      close();
      setCatalog(undefined);
      setResult(undefined);
      setError(zh ? '字段、单位或初始值已变化；请重新打开计算器。' : 'The field, units, or initial value changed. Reopen the calculator.');
    }
  }, [identity, backend, zh, close]);

  useEffect(() => () => {
    openRef.current = false;
    invalidate();
  }, [invalidate]);

  const openCalculator = async (): Promise<void> => {
    if (disabled || !supportsLoad || !supportsConvert || !hasFiniteInitialValue || !sourceUnitId || !displayUnitId) return;
    const requestIdentity = identityRef.current;
    openedIdentity.current = requestIdentity;
    openedBackend.current = backend;
    openRef.current = true;
    setOpen(true);
    setCatalog(undefined);
    setSourceValue(initialSourceValue);
    sourceValueRef.current = initialSourceValue;
    setResult(undefined);
    resultIdentity.current = null;
    setError(undefined);
    setBusy('loading');
    const request = beginRequest(requestIdentity);
    try {
      const response = await backend.request<unknown>('unitCalculator.load', {
        fromUnitId: sourceUnitId,
        toUnitId: displayUnitId,
      }, { signal: request.controller.signal });
      if (!requestIsCurrent(request)) return;
      setCatalog(parseLoadResult(response, sourceUnitId, displayUnitId));
    } catch (requestError) {
      if (requestIsCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (requestIsCurrent(request)) {
        activeRequest.current = null;
        setBusy(null);
      }
    }
  };

  const convert = async (): Promise<void> => {
    const inputText = sourceValueRef.current;
    const inputValue = parseFiniteInput(inputText);
    const requestIdentity = openedIdentity.current;
    if (!catalog || requestIdentity === null || requestIdentity !== identityRef.current || inputValue === null || !supportsConvert) return;
    const request = beginRequest(requestIdentity);
    resultIdentity.current = null;
    setResult(undefined);
    setError(undefined);
    setBusy('converting');
    try {
      const response = await backend.request<unknown>('unitCalculator.convert', {
        fromUnitId: sourceUnitId,
        toUnitId: displayUnitId,
        value: inputValue,
        direction: 'FromTo',
      }, { signal: request.controller.signal });
      if (!requestIsCurrent(request) || sourceValueRef.current !== inputText) return;
      const converted = parseConvertResult(response);
      setResult(converted);
      resultIdentity.current = JSON.stringify([requestIdentity, inputText]);
    } catch (requestError) {
      if (requestIsCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (requestIsCurrent(request)) {
        activeRequest.current = null;
        setBusy(null);
      }
    }
  };

  const applyResult = (): void => {
    const requestIdentity = openedIdentity.current;
    if (!result || requestIdentity === null || requestIdentity !== identityRef.current ||
      resultIdentity.current !== JSON.stringify([requestIdentity, sourceValueRef.current])) return;
    const displayValue = String(result.value);
    close();
    onApply(displayValue);
  };

  const changeSourceValue = (value: string): void => {
    sourceValueRef.current = value;
    setSourceValue(value);
    resultIdentity.current = null;
    setResult(undefined);
    setError(undefined);
    if (activeRequest.current) {
      invalidate();
      setBusy(null);
    }
  };

  const displayUnit = catalog?.toUnits.find((unit) => unit.id === displayUnitId);
  const canConvert = Boolean(catalog && parseFiniteInput(sourceValue) !== null && busy === null);
  const canApply = Boolean(result && busy === null && openedIdentity.current === identityRef.current &&
    resultIdentity.current === JSON.stringify([identityRef.current, sourceValueRef.current]));

  return <>
    <button
      type="button"
      className="editor-button"
      data-testid="unit-calculator-button"
      data-unit-field-id={fieldID}
      aria-label={zh ? `打开 ${fieldTitle} 的单位计算器` : `Open unit calculator for ${fieldTitle}`}
      title={supportsService
        ? (zh ? `换算 ${sourceUnitName} 与 ${displayUnitName}` : `Convert ${sourceUnitName} and ${displayUnitName}`)
        : (zh ? '当前服务未提供单位换算操作。' : 'The current service does not provide unit conversion.')}
      disabled={disabled || !supportsService || !hasFiniteInitialValue}
      onClick={() => void openCalculator()}
    >{zh ? '单位计算器' : 'Unit calculator'}</button>

    <FramedContentDialog
      open={open}
      title={zh ? `单位计算器 · ${fieldTitle}` : `Unit calculator · ${fieldTitle}`}
      onCancel={close}
    >
      <p>{zh
        ? '输入源单位的数值，换算后应用到当前字段。取消不会改动草稿。'
        : 'Enter a value in the source unit, then convert and apply it to this field. Cancel leaves the draft unchanged.'}</p>
      <div className="editor-readonly-list">
        <dt>{zh ? '字段源单位' : 'Field source unit'}</dt><dd>{catalog?.fromUnitName ?? sourceUnitName}</dd>
        <dt>{zh ? '当前显示单位' : 'Current display unit'}</dt><dd>{displayUnit?.name ?? displayUnitName}</dd>
      </div>
      <label className="editor-search-box">
        <span>{zh ? `源数值（${catalog?.fromUnitName ?? sourceUnitName}）` : `Source value (${catalog?.fromUnitName ?? sourceUnitName})`}</span>
        <input
          data-testid="unit-calculator-source-value"
          type="text"
          inputMode="decimal"
          value={sourceValue}
          disabled={busy !== null || !catalog}
          onChange={(event) => changeSourceValue(event.currentTarget.value)}
        />
      </label>
      {busy === 'loading' && <p role="status">{zh ? '正在读取单位目录…' : 'Loading the unit catalog…'}</p>}
      {busy === 'converting' && <p role="status">{zh ? '正在换算…' : 'Converting…'}</p>}
      {error && <p role="alert" className="editor-field-error">{error}</p>}
      {result && <dl className="editor-readonly-list" aria-live="polite">
        <dt>{zh ? `换算结果（${displayUnit?.name ?? displayUnitName}）` : `Converted value (${displayUnit?.name ?? displayUnitName})`}</dt>
        <dd><strong data-testid="unit-calculator-result">{result.value}</strong></dd>
      </dl>}
      <div className="editor-confirm-actions editor-button-row">
        <button type="button" className="editor-button editor-button-primary" disabled={!canConvert} onClick={() => void convert()}>{zh ? '换算' : 'Convert'}</button>
        <button type="button" className="editor-button editor-button-primary" disabled={!canApply} onClick={applyResult}>{zh ? '应用到字段' : 'Apply to field'}</button>
        <button type="button" className="editor-button" onClick={close}>{zh ? '取消' : 'Cancel'}</button>
      </div>
    </FramedContentDialog>
  </>;
}
