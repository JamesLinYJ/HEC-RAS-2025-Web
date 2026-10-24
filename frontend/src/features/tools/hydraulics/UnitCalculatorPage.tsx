import { useCallback, useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../../shared/workbench';
import { message } from '../../../shared/workbench';
import type { UnitCalculatorConvertResult, UnitCalculatorLoadResult } from './wilcockTypes';
import { UnitCalculator, type UnitCalculatorDirection } from './UnitCalculator';

type BusyAction = 'loading' | 'converting' | null;

interface ActiveRequest {
  owner: symbol;
  generation: number;
  controller: AbortController;
}

function parseLoad(value: unknown): UnitCalculatorLoadResult {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('单位服务返回的数据不是对象。');
  const raw = value as Record<string, unknown>;
  if (!Array.isArray(raw.fromUnits) || !Array.isArray(raw.toUnits)) throw new Error('单位服务没有返回源单位和目标单位目录。');
  const units = [...raw.fromUnits, ...raw.toUnits];
  if (units.some((unit) => !unit || typeof unit !== 'object' ||
    typeof (unit as Record<string, unknown>).id !== 'string' ||
    typeof (unit as Record<string, unknown>).name !== 'string' ||
    typeof (unit as Record<string, unknown>).unitTypeName !== 'string')) {
    throw new Error('单位服务返回了不完整的单位标识。');
  }
  return value as UnitCalculatorLoadResult;
}

function parseConversion(value: unknown): UnitCalculatorConvertResult {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      typeof (value as Record<string, unknown>).value !== 'number' || !Number.isFinite((value as Record<string, number>).value)) {
    throw new Error('单位服务没有返回有限数值结果。');
  }
  return value as UnitCalculatorConvertResult;
}

function finiteInput(textValue: string): number | null {
  const trimmed = textValue.trim();
  if (!trimmed) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
}

export function UnitCalculatorPage({ language, backend }: FeaturePageProps) {
  const zh = language === 'zh';
  const languageRef = useRef(language);
  languageRef.current = language;
  const [catalog, setCatalog] = useState<UnitCalculatorLoadResult>();
  const [selectedFromID, setSelectedFromID] = useState('');
  const [selectedToID, setSelectedToID] = useState('');
  const [fromValue, setFromValue] = useState('');
  const [toValue, setToValue] = useState('');
  const [direction, setDirection] = useState<UnitCalculatorDirection>('FromTo');
  const [conversion, setConversion] = useState<UnitCalculatorConvertResult>();
  const [conversionKey, setConversionKey] = useState<string>();
  const [busyAction, setBusyAction] = useState<BusyAction>(null);
  const [error, setError] = useState<string>();
  const [status, setStatus] = useState<string>();
  const generation = useRef(0);
  const currentOwner = useRef<symbol | undefined>(undefined);
  const controller = useRef<AbortController | null>(null);

  const beginRequest = useCallback((): ActiveRequest => {
    controller.current?.abort();
    const requestController = new AbortController();
    const owner = Symbol('unit-calculator-owner');
    const requestGeneration = ++generation.current;
    currentOwner.current = owner;
    controller.current = requestController;
    return { owner, generation: requestGeneration, controller: requestController };
  }, []);

  const isCurrent = useCallback((request: ActiveRequest): boolean =>
    currentOwner.current === request.owner && generation.current === request.generation &&
    controller.current === request.controller && !request.controller.signal.aborted, []);

  const loadUnits = useCallback(async (fromUnitId: string | null, toUnitId: string | null) => {
    if (!backend.supports('unitCalculator.load')) {
      setError(languageRef.current === 'zh' ? '当前 C# 服务未注册 unitCalculator.load。' : 'The C# service has not registered unitCalculator.load.');
      return;
    }
    const request = beginRequest();
    setBusyAction('loading');
    setError(undefined);
    setStatus(undefined);
    try {
      const raw = await backend.request<unknown>('unitCalculator.load', { fromUnitId, toUnitId }, { signal: request.controller.signal });
      if (!isCurrent(request)) return;
      const returned = parseLoad(raw);
      setCatalog(returned);
      setSelectedFromID(returned.fromUnitId ?? '');
      setSelectedToID(returned.toUnitId ?? '');
    } catch (requestError) {
      if (isCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (isCurrent(request)) {
        setBusyAction(null);
        controller.current = null;
        currentOwner.current = undefined;
      }
    }
  }, [backend, beginRequest, isCurrent]);

  useEffect(() => {
    void loadUnits(null, null);
    return () => {
      generation.current += 1;
      currentOwner.current = undefined;
      controller.current?.abort();
      controller.current = null;
    };
  }, [loadUnits]);

  const activeValue = direction === 'FromTo' ? fromValue : toValue;
  const inputNumber = finiteInput(activeValue);
  const hasSelection = Boolean(selectedFromID && selectedToID);
  const requestKey = JSON.stringify([selectedFromID, selectedToID, activeValue, direction]);
  const resultStale = Boolean(conversion && conversionKey !== requestKey);

  const changeFrom = (fromUnitId: string): void => {
    setSelectedFromID(fromUnitId);
    setSelectedToID('');
    setDirection('FromTo');
    if (fromUnitId) void loadUnits(fromUnitId, null);
    else void loadUnits(null, null);
  };

  const changeFamily = (unitTypeName: string): void => {
    const source = catalog?.fromUnits.find((unit) => unit.unitTypeName === unitTypeName);
    changeFrom(source?.id ?? '');
  };

  const changeTo = (toUnitId: string): void => {
    setSelectedToID(toUnitId);
    setDirection('FromTo');
    void loadUnits(selectedFromID || null, toUnitId || null);
  };

  const convert = async (): Promise<void> => {
    if (!backend.supports('unitCalculator.convert')) {
      setError(zh ? '当前 C# 服务未注册 unitCalculator.convert。' : 'The C# service has not registered unitCalculator.convert.');
      return;
    }
    if (!hasSelection) {
      setError(zh ? '请先从 C# 单位目录选择源单位和目标单位。' : 'Choose source and target units from the C# unit catalog.');
      return;
    }
    if (inputNumber === null) {
      setError(zh ? '请输入有限数值；空值和非法文本不会按 0 发送。' : 'Enter a finite number. Empty or invalid text is never sent as zero.');
      return;
    }
    const request = beginRequest();
    setBusyAction('converting');
    setError(undefined);
    setStatus(undefined);
    setConversion(undefined);
    setConversionKey(undefined);
    const convertingKey = requestKey;
    try {
      const raw = await backend.request<unknown>('unitCalculator.convert', {
        fromUnitId: selectedFromID,
        toUnitId: selectedToID,
        value: inputNumber,
        direction,
      }, { signal: request.controller.signal });
      if (!isCurrent(request)) return;
      const returned = parseConversion(raw);
      setConversion(returned);
      setConversionKey(convertingKey);
      if (direction === 'FromTo') setToValue(String(returned.value));
      else setFromValue(String(returned.value));
      setStatus('换算完成。');
    } catch (requestError) {
      if (isCurrent(request)) setError(message(requestError, languageRef.current));
    } finally {
      if (isCurrent(request)) {
        setBusyAction(null);
        controller.current = null;
        currentOwner.current = undefined;
      }
    }
  };

  const cancel = (): void => {
    if (!busyAction) return;
    generation.current += 1;
    currentOwner.current = undefined;
    controller.current?.abort();
    controller.current = null;
    setBusyAction(null);
    setStatus('已请求取消；迟到的单位服务回执不会覆盖当前状态。');
  };

  return <main className="hydraulics-workspace unit-calculator-page">
    <header className="hydraulics-header">
      <div><span className="tools-kicker">{zh ? '单位换算' : 'UNIT CONVERSION'}</span>
        <h2>{zh ? '单位计算器' : 'Unit calculator'}</h2>
        <p>{zh ? '选择单位族和成对单位，执行双向换算。' : 'Choose a unit family and unit pair for conversion in either direction.'}</p>
      </div>
      <div className="hydraulics-header-actions">
        <span className={backend.supports('unitCalculator.convert') ? 'hydraulics-runtime is-connected' : 'hydraulics-runtime'}>{backend.supports('unitCalculator.convert') ? '换算服务可用' : '换算服务不可用'}</span>
        {busyAction && <button type="button" className="hydraulics-button hydraulics-button-danger" onClick={cancel}>取消</button>}
      </div>
    </header>
    {!backend.supports('unitCalculator.load') && <p className="hydraulics-warning" role="status">当前服务未注册 <code>unitCalculator.load</code>，不会构造本地单位目录。</p>}
    {busyAction && <p className="hydraulics-status" role="status">{busyAction === 'loading' ? '正在加载单位目录…' : '正在换算…'}</p>}
    {error && <p className="hydraulics-error" role="alert">{error}</p>}
    {status && <p className="hydraulics-status" role="status">{status}</p>}
    <UnitCalculator
      language={language}
      catalog={catalog}
      selectedFromID={selectedFromID}
      selectedToID={selectedToID}
      fromValue={fromValue}
      toValue={toValue}
      direction={direction}
      conversion={conversion}
      conversionStale={resultStale}
      canConvert={backend.supports('unitCalculator.convert') && hasSelection && inputNumber !== null && busyAction === null}
      converting={busyAction !== null}
      onFamilyChange={changeFamily}
      onFromChange={changeFrom}
      onToChange={changeTo}
      onFromValueChange={(value) => { setFromValue(value); setDirection('FromTo'); }}
      onToValueChange={(value) => { setToValue(value); setDirection('ToFrom'); }}
      onDirectionChange={setDirection}
      onConvert={() => void convert()}
    />
    {catalog && <details className="hydraulics-raw-draft"><summary>单位目录详情</summary><pre>{JSON.stringify(catalog, null, 2)}</pre></details>}
    {conversion && <details className="hydraulics-raw-draft"><summary>换算结果详情</summary><pre>{JSON.stringify(conversion, null, 2)}</pre></details>}
  </main>;
}
