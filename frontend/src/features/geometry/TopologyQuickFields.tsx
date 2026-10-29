import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { gchoice } from './geometryText';
import type { GeometryField } from './geometryTypes';
import './topologyQuickEdit.css';

const labels: Record<string, string> = {
  Constant: '固定尺寸', CountWithBias: '指定点数', Unset: '未设置', Quad: '四边形',
  TrianglePaver: '三角网格', Cartesian: '笛卡尔网格', OneD: '一维', Empty: '空区域',
};

function choiceLabel(language: Language, value: string): string {
  return language === 'zh' ? labels[value] ?? gchoice(language, value) : value;
}

export function QuickChoiceField({ language, label, field, value, disabled, onCommit }: {
  language: Language; label: string; field?: GeometryField; value: string; disabled: boolean;
  onCommit: (value: string) => Promise<void>;
}) {
  const [pending, setPending] = useState(false);
  const [current, setCurrent] = useState(value);
  useEffect(() => { if (!pending) setCurrent(value); }, [value, pending]);
  const choices = field?.choices ?? [];
  const change = (next: string): void => {
    if (!choices.includes(next) || next === value || pending) return;
    setCurrent(next); setPending(true);
    void onCommit(next).catch(() => setCurrent(value)).finally(() => setPending(false));
  };
  return <label className="topology-quick-field">{label}
    <select aria-label={label} value={current} disabled={disabled || pending || !field || !!field.readOnly || !choices.length}
      onChange={(event) => change(event.target.value)}>
      {!choices.includes(current) && <option value={current} disabled>{current || (language === 'zh' ? '无值' : 'No value')}</option>}
      {choices.map((choice) => <option value={choice} key={choice}>{choiceLabel(language, choice)}</option>)}
    </select>
  </label>;
}

type NumberKind = 'arc-count' | 'arc-size' | 'region-size';

function shownNumber(value: string, kind: NumberKind): string {
  return value === 'NaN' || value === '' || (kind === 'arc-count' && value === '-1') ? '' : value;
}

/** Returns null while an input is unfinished or outside the source field's bounds. */
export function quickNumberValue(raw: string, kind: NumberKind, field: GeometryField): string | null {
  const text = raw.trim();
  if (!text) return kind === 'arc-count' ? '-1' : '';
  const syntax = kind === 'arc-count' ? /^[+-]?\d+$/ : /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;
  if (!syntax.test(text)) return null;
  const value = Number(text);
  if (!Number.isFinite(value) || (kind === 'arc-count' && (!Number.isInteger(value) || value < -2147483648 || value > 2147483647)) ||
      (kind !== 'arc-count' && !Number.isFinite(Math.fround(value))) || (kind === 'arc-size' && Math.fround(value) <= 0) ||
      (field.minimum != null && value < field.minimum) || (field.maximum != null && value > field.maximum)) return null;
  return text;
}

export function QuickNumberField({ language, label, field, value, kind, disabled, onCommit }: {
  language: Language; label: string; field?: GeometryField; value: string; kind: NumberKind; disabled: boolean;
  onCommit: (value: string) => Promise<void>;
}) {
  const source = shownNumber(value, kind);
  const [raw, setRaw] = useState(source);
  const [touched, setTouched] = useState(false);
  const [pending, setPending] = useState(false);
  const dirty = useRef(false);
  const inFlight = useRef(false);
  useEffect(() => { if (!dirty.current && !inFlight.current) setRaw(source); }, [source]);
  const parsed = field ? quickNumberValue(raw, kind, field) : null;
  const unchanged = raw.trim() === source.trim();
  const commit = (): void => {
    setTouched(true);
    if (disabled || !field || field.readOnly || parsed === null || inFlight.current) return;
    if (unchanged) { dirty.current = false; setRaw(source); setTouched(false); return; }
    inFlight.current = true; setPending(true);
    void onCommit(parsed).then(() => { dirty.current = false; setTouched(false); })
      .catch(() => undefined).finally(() => { inFlight.current = false; setPending(false); });
  };
  const message = kind === 'arc-count'
    ? (language === 'zh' ? '请输入有效的整数；留空表示无值。' : 'Enter a valid integer; leave blank for no value.')
    : kind === 'arc-size'
      ? (language === 'zh' ? '请输入大于零的有限数字；留空表示无值。' : 'Enter a finite number greater than zero; leave blank for no value.')
      : (language === 'zh' ? '请输入有限数字；留空表示无值。' : 'Enter a finite number; leave blank for no value.');
  return <div className="topology-quick-number">
    <label className="topology-quick-field">{label}
      <input aria-label={label} aria-invalid={touched && parsed === null} type="text"
        inputMode={kind === 'arc-count' ? 'numeric' : 'decimal'} value={raw} disabled={disabled || pending || !field || !!field.readOnly}
        placeholder={language === 'zh' ? '无值' : 'No value'} onChange={(event) => { dirty.current = true; setRaw(event.target.value); }}
        onBlur={commit} onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); commit(); } }} />
    </label>
    {touched && parsed === null && <span className="topology-quick-error" role="status">{message}</span>}
  </div>;
}
