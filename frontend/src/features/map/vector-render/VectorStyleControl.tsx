import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { VectorStyleValues } from './vectorRenderTypes';

export type VectorStyleProperty = keyof VectorStyleValues;

const colorPropertyLabels: Record<'penColor' | 'fillColor' | 'haloColor', readonly [string, string]> = {
  penColor: ['线条颜色', 'Pen color'],
  fillColor: ['填充颜色', 'Fill color'],
  haloColor: ['文字光晕颜色', 'Text halo color'],
};

const numberPropertyLabels: Record<Exclude<VectorStyleProperty, 'penColor' | 'fillColor' | 'haloColor'>, readonly [string, string]> = {
  penWidth: ['线宽', 'Line width'],
  pointSize: ['点大小', 'Point size'],
  haloThickness: ['文字光晕厚度', 'Text halo thickness'],
  textEmphasis: ['文字强调级别', 'Text emphasis'],
};

function isARGB(value: string): boolean {
  return /^#[0-9a-f]{8}$/i.test(value);
}

function rgbValue(value: string): string | null {
  return isARGB(value) ? `#${value.slice(3)}` : null;
}

function alphaValue(value: string): number | null {
  if (!isARGB(value)) return null;
  return Number.parseInt(value.slice(1, 3), 16);
}

function withRGB(current: string, rgb: string): string {
  const alpha = alphaValue(current);
  return alpha === null ? current : `#${alpha.toString(16).padStart(2, '0')}${rgb.slice(1)}`.toUpperCase();
}

function withAlpha(current: string, alpha: number): string {
  const rgb = rgbValue(current);
  return rgb === null ? current : `#${Math.round(alpha).toString(16).padStart(2, '0')}${rgb.slice(1)}`.toUpperCase();
}

export function VectorStyleControl({
  property,
  values,
  disabled,
  language,
  onChange,
  onValidityChange,
}: {
  property: VectorStyleProperty;
  values: VectorStyleValues;
  disabled: boolean;
  language: Language;
  onChange: (property: VectorStyleProperty, value: string | number) => void;
  onValidityChange?: (property: VectorStyleProperty, invalid: boolean) => void;
}) {
  const value = values[property];
  const integer = property === 'pointSize' || property === 'textEmphasis';
  const withinRange = (candidate: number) => {
    if (property === 'penWidth' || property === 'pointSize' || property === 'haloThickness') {
      return candidate >= 0 && candidate <= 10_000;
    }
    return true;
  };
  const [rawValue, setRawValue] = useState<string | null>(null);
  const validityCallbackRef = useRef(onValidityChange);
  validityCallbackRef.current = onValidityChange;
  useEffect(() => {
    setRawValue(null);
    validityCallbackRef.current?.(property, false);
  }, [value, property]);

  if (property === 'penColor' || property === 'fillColor' || property === 'haloColor') {
    const sourceValue = values[property];
    const rgb = rgbValue(sourceValue);
    const alpha = alphaValue(sourceValue);
    const [zhLabel, enLabel] = colorPropertyLabels[property];
    const label = language === 'zh' ? zhLabel : enLabel;
    return (
      <div className="vector-render-field" data-style-property={property}>
        <label>
          <span>{label}</span>
          <span className="vector-render-color-input">
            <input
              type="color"
              value={rgb ?? '#000000'}
              disabled={disabled || rgb === null}
              aria-label={label}
              onChange={(event) => onChange(property, withRGB(sourceValue, event.currentTarget.value))}
            />
            <code>{sourceValue}</code>
          </span>
        </label>
        <label className="vector-render-alpha-field">
          <span>{language === 'zh' ? '不透明度' : 'Opacity'}</span>
          <input
            type="number"
            min="0"
            max="255"
            step="1"
            value={alpha ?? ''}
            disabled={disabled || alpha === null}
            aria-label={language === 'zh' ? `${label}不透明度` : `${label} opacity`}
            onChange={(event) => {
              const next = Number(event.currentTarget.value);
              if (Number.isInteger(next) && next >= 0 && next <= 255) onChange(property, withAlpha(sourceValue, next));
            }}
          />
        </label>
        {alpha === null && <small className="vector-render-invalid-value">{sourceValue}</small>}
      </div>
    );
  }

  const [zhLabel, enLabel] = numberPropertyLabels[property];
  const label = language === 'zh' ? zhLabel : enLabel;
  const onNumericTextChange = (nextText: string) => {
    setRawValue(nextText);
    const next = nextText.trim() === '' ? Number.NaN : Number(nextText);
    const isValid = Number.isFinite(next) && (!integer || Number.isInteger(next)) && withinRange(next);
    onValidityChange?.(property, !isValid);
    if (isValid) {
      onChange(property, next);
      setRawValue(null);
    }
  };
  return (
    <div className="vector-render-field" data-style-property={property}>
      <label>
        <span>{label}</span>
        <input
          type="text"
          inputMode="decimal"
          value={rawValue ?? (Number.isFinite(value) ? String(value) : String(value))}
          disabled={disabled || !Number.isFinite(value)}
          aria-label={label}
          onChange={(event) => onNumericTextChange(event.currentTarget.value)}
        />
      </label>
      {(!Number.isFinite(value) || rawValue !== null) && <small className="vector-render-invalid-value">{rawValue ?? String(value)}</small>}
    </div>
  );
}
