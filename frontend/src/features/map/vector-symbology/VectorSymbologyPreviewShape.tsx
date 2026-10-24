import type { Language } from '../../../shared/workbench';
import { colorValueToCss, vectorGeometryLabel, vectorPropertyLabel } from './vectorSymbologyWorkflow';
import type { VectorGeometryType, VectorSymbologyOverride } from './vectorSymbologyWorkflow';

interface Props {
  geometryType: VectorGeometryType;
  sourceFeatureIndex: number;
  definitionID: string;
  overrides: VectorSymbologyOverride[];
  language: Language;
}

export function VectorSymbologyPreviewShape({ geometryType, sourceFeatureIndex, definitionID, overrides, language }: Props) {
  const zh = language === 'zh';
  const matching = overrides.filter(item => item.hasMatch);
  const valueFor = (property: VectorSymbologyOverride['property']) => matching.find(item => item.property === property);
  const penColor = valueFor('PenColor');
  const fillColor = valueFor('FillColor');
  const penWidth = valueFor('PenWidth');
  const pointSize = valueFor('PointSize');
  const stroke = colorValueToCss(penColor?.colorValue ?? null) ?? '#667085';
  const fill = colorValueToCss(fillColor?.colorValue ?? null) ?? 'transparent';
  const strokeWidth = bounded(penWidth?.numberValue ?? 2, 1, 12);
  const pointRadius = bounded((pointSize?.integerValue ?? 12) / 2, 3, 16);

  return (
    <section className="vector-symbology-preview-shape" aria-label={zh ? 'Host 条件结果符号预览' : 'Host conditional result symbol preview'}>
      <div className="vector-symbology-preview-title">
        <strong>{zh ? '条件结果符号预览' : 'Conditional result symbol preview'}</strong>
        <span>{vectorGeometryLabel(geometryType, language)} · #{sourceFeatureIndex}</span>
      </div>
      <code className="vector-symbology-definition-id">{definitionID}</code>
      {matching.length > 0 ? <div className="vector-symbology-preview-glyph" aria-hidden="true">
        {geometryType === 'line' && <svg viewBox="0 0 120 44" role="presentation">
          <line x1="10" y1="22" x2="110" y2="22" stroke={stroke} strokeWidth={strokeWidth} strokeLinecap="round" />
        </svg>}
        {geometryType === 'polygon' && <svg viewBox="0 0 120 70" role="presentation">
          <path d="M16 54 L27 17 L78 10 L105 40 L85 60 Z" fill={fill} stroke={stroke} strokeWidth={strokeWidth} strokeLinejoin="round" />
        </svg>}
        {geometryType === 'point' && <svg viewBox="0 0 120 70" role="presentation">
          <circle cx="60" cy="35" r={pointRadius} fill={fill} stroke={stroke} strokeWidth={strokeWidth} />
        </svg>}
      </div> : <p className="vector-symbology-empty">
        {zh ? '所选源要素没有匹配条件；Host 未返回符号覆写。' : 'No condition matched this source feature; the Host returned no symbol override.'}
      </p>}
      <p className="vector-symbology-muted">
        {zh ? '图形只表示 Host 对此源要素返回的匹配覆写；缺失属性用中性示意样式显示，基础地图样式未由此 API 返回。' : 'The glyph represents only matching overrides returned by the Host. Missing values use neutral illustration styling; the base map style is not returned by this API.'}
      </p>
      <dl className="vector-symbology-preview-values">
        {overrides.map(item => <div key={`${item.definitionID}-${item.property}`}>
          <dt>{vectorPropertyLabel(item.property, language)} <code>{item.property}</code></dt>
          <dd>{item.hasMatch ? displayOverrideValue(item) : (zh ? '未匹配' : 'No match')}</dd>
        </div>)}
      </dl>
    </section>
  );
}

function displayOverrideValue(item: VectorSymbologyOverride): string {
  if (item.colorValue !== null) return item.colorValue;
  if (item.numberValue !== null) return String(item.numberValue);
  if (item.integerValue !== null) return String(item.integerValue);
  return '—';
}

function bounded(value: number, minimum: number, maximum: number): number {
  return Number.isFinite(value) ? Math.min(maximum, Math.max(minimum, value)) : minimum;
}
