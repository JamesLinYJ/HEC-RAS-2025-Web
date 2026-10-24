import { useEffect, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import { VectorSymbologyPropertyItem } from './VectorSymbologyPropertyItem';
import { supportedProperties, vectorGeometryLabel, vectorPropertyLabel } from './vectorSymbologyWorkflow';
import type { VectorGeometryType, VectorSymbologyBucket, VectorSymbologyCondition, VectorSymbologyRuleDraft } from './vectorSymbologyWorkflow';

interface Props {
  buckets: VectorSymbologyBucket[];
  geometryType: VectorGeometryType;
  rules: VectorSymbologyRuleDraft[];
  language: Language;
  disabled?: boolean;
  onGeometryChange: (geometryType: VectorGeometryType) => void;
  onRuleAdd: (definitionID: string, property: VectorSymbologyRuleDraft['property']) => void;
  onRuleRemove: (ruleID: string) => void;
  onConditionAdd: (ruleID: string) => void;
  onConditionChange: (ruleID: string, conditionID: string, change: Partial<VectorSymbologyCondition>) => void;
  onConditionRemove: (ruleID: string, conditionID: string) => void;
}

export function VectorSymbologyOptionEditor({ buckets, geometryType, rules, language, disabled = false,
  onGeometryChange, onRuleAdd, onRuleRemove, onConditionAdd, onConditionChange, onConditionRemove }: Props) {
  const zh = language === 'zh';
  const bucket = buckets.find(item => item.geometryType === geometryType);
  const properties = bucket ? supportedProperties(bucket.geometryType) : [];
  const definitionKey = bucket?.supportedDefinitionIDs.join('\u0000') ?? '';
  const [definitionID, setDefinitionID] = useState('');
  const [property, setProperty] = useState<VectorSymbologyRuleDraft['property'] | ''>('');

  useEffect(() => {
    setDefinitionID(bucket?.supportedDefinitionIDs[0] ?? '');
    setProperty(properties[0] ?? '');
  }, [geometryType, definitionKey]);

  const alreadyAdded = rules.some(rule => rule.definitionID === definitionID && rule.property === property);
  const atPropertyLimit = rules.length >= 64;
  const canAdd = !disabled && Boolean(bucket?.ordinalBindingProven && bucket.hasAttributeTable
    && definitionID && property && !alreadyAdded && !atPropertyLimit);

  return (
    <section className="vector-symbology-option-editor">
      <div className="vector-symbology-selection-grid">
        <label className="vector-symbology-field">
          <span>{zh ? '几何类型' : 'Geometry type'}</span>
          <select value={geometryType} disabled={disabled} onChange={event => {
            const next = buckets.find(item => item.geometryType === event.currentTarget.value);
            if (next) onGeometryChange(next.geometryType);
          }}>
            {buckets.map(item => <option key={item.geometryType} value={item.geometryType}>
              {vectorGeometryLabel(item.geometryType, language)} ({item.renderFeatureCount})
            </option>)}
          </select>
        </label>
        <div className="vector-symbology-binding-status" role="status">
          <strong>{bucket?.ordinalBindingProven ? (zh ? '序号绑定已证明' : 'Ordinal binding proven') : (zh ? '序号绑定未证明' : 'Ordinal binding unproven')}</strong>
          <span>{zh ? `渲染要素 ${bucket?.renderFeatureCount ?? 0} · 属性表行 ${bucket?.tableRowCount ?? 0}` : `${bucket?.renderFeatureCount ?? 0} render features · ${bucket?.tableRowCount ?? 0} table rows`}</span>
        </div>
      </div>

      {bucket && !bucket.ordinalBindingProven && <p className="vector-symbology-warning">
        {zh ? 'Host 未能证明此几何分组的属性表行对应原 GeoJSON 序号，因此条件求值已禁用。' : 'The Host could not prove attribute rows correspond to original GeoJSON indices, so evaluation is disabled for this geometry group.'}
      </p>}
      {bucket && !bucket.hasAttributeTable && <p className="vector-symbology-warning">
        {zh ? 'Host 未提供可用于条件规则的属性表。' : 'The Host did not provide an attribute table for conditional rules.'}
      </p>}

      <div className="vector-symbology-rule-builder">
        <label className="vector-symbology-field">
          <span>{zh ? '符号定义 ID' : 'Symbology definition ID'}</span>
          <select value={definitionID} disabled={disabled || !bucket?.supportedDefinitionIDs.length}
            onChange={event => setDefinitionID(event.currentTarget.value)}>
            {bucket?.supportedDefinitionIDs.map(id => <option key={id} value={id}>{id}</option>)}
          </select>
        </label>
        <label className="vector-symbology-field">
          <span>{zh ? '条件属性' : 'Conditional property'}</span>
          <select value={property} disabled={disabled || properties.length === 0}
            onChange={event => setProperty(event.currentTarget.value as VectorSymbologyRuleDraft['property'])}>
            {properties.map(item => <option key={item} value={item}>{vectorPropertyLabel(item, language)} · {item}</option>)}
          </select>
        </label>
        <button type="button" className="vector-symbology-primary-button" disabled={!canAdd}
          onClick={() => { if (definitionID && property) onRuleAdd(definitionID, property); }}>
          {atPropertyLimit ? (zh ? '已达 Host 的 64 个属性组上限' : 'Host limit of 64 property groups reached')
            : alreadyAdded ? (zh ? '该定义属性已添加' : 'Definition property already added') : (zh ? '添加条件属性' : 'Add conditional property')}
        </button>
      </div>
      <p className="vector-symbology-muted">
        {zh ? 'DefinitionID 与 Property 将原样提交给 C# Host；可用属性按原 C# 符号定义的几何类型能力列出。' : 'DefinitionID and Property are sent unchanged to the C# Host; available properties follow the original C# geometry-definition capabilities.'}
      </p>

      {rules.length > 0 ? <div className="vector-symbology-property-list">
        {rules.map(rule => <VectorSymbologyPropertyItem key={rule.id} rule={rule} columns={bucket?.columns ?? []}
          language={language} disabled={disabled} onRemove={() => onRuleRemove(rule.id)}
          onConditionAdd={() => onConditionAdd(rule.id)}
          onConditionChange={(conditionID, change) => onConditionChange(rule.id, conditionID, change)}
          onConditionRemove={conditionID => onConditionRemove(rule.id, conditionID)} />)}
      </div> : <p className="vector-symbology-empty">
        {zh ? '尚未添加条件属性。属性规则只用于本页 Host 求值预览，不会写入地图样式。' : 'No conditional properties have been added. Rules are evaluated for this page preview and are not saved to map styling.'}
      </p>}
    </section>
  );
}
