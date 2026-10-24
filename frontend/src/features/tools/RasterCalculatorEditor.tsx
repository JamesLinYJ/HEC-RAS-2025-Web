import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { displaySourceKind } from './GeoprocessingToolBase';

type Props = FeaturePageProps & { onDirtyStateChange?: (owner: string, dirty: boolean) => void };
type ConstantDraft = { id: string; name: string; value: string };
const noDataBehaviors = ['Allow', 'WriteNDV', 'WriteZero'] as const;
const reservedNames = ['ndv', 'pi', 'e', 'tau'];

export function RasterCalculatorEditor({ language, context, backend, onDirtyStateChange }: Props) {
  const zh = language === 'zh';
  const defaultLayerName = text(language, '栅格计算器', 'Raster Calculator');
  const [expression, setExpression] = useState('');
  const [noDataBehavior, setNoDataBehavior] = useState<(typeof noDataBehaviors)[number]>('WriteNDV');
  const [layerName, setLayerName] = useState(defaultLayerName);
  const previousDefaultLayerName = useRef(defaultLayerName);
  const [constants, setConstants] = useState<ConstantDraft[]>([]);
  const [notice, setNotice] = useState<string>();
  const [draftSaved, setDraftSaved] = useState(false);
  const hasProject = Boolean(context.projectPath && context.projectXml);
  const inspectAvailable = backend.supports('raster.calculator.inspect');
  const evaluateAvailable = backend.supports('raster.calculator.evaluate');
  const dirty = expression !== '' || noDataBehavior !== 'WriteNDV' || layerName !== defaultLayerName || constants.length > 0;

  useEffect(() => {
    setLayerName(current => current === previousDefaultLayerName.current ? defaultLayerName : current);
    previousDefaultLayerName.current = defaultLayerName;
  }, [defaultLayerName]);
  useEffect(() => { onDirtyStateChange?.('tools.raster-calculator', dirty); return () => onDirtyStateChange?.('tools.raster-calculator', false); }, [dirty, onDirtyStateChange]);

  const addConstant = (): void => {
    setDraftSaved(false);
    setConstants(current => [...current, { id: crypto.randomUUID(), name: '', value: '' }]);
  };
  const updateConstant = (id: string, key: 'name' | 'value', value: string): void => {
    setDraftSaved(false);
    setConstants(current => current.map(item => item.id === id ? { ...item, [key]: value } : item));
  };
  const removeConstant = (id: string): void => {
    setDraftSaved(false);
    setConstants(current => current.filter(item => item.id !== id));
  };
  const validateDraft = (): boolean => {
    if (!expression.trim()) { setNotice(text(language, '请输入栅格表达式。', 'Enter a raster expression.')); return false; }
    const names = constants.map(item => item.name.trim().toLowerCase()).filter(Boolean);
    if (names.some(name => reservedNames.includes(name))) { setNotice(text(language, '常量名称不能使用保留字 ndv、pi、e 或 tau。', 'Constant names cannot use reserved words ndv, pi, e, or tau.')); return false; }
    if (new Set(names).size !== names.length) { setNotice(text(language, '常量名称不能重复。', 'Constant names must be unique.')); return false; }
    if (constants.some(item => !item.name.trim() || !item.value.trim() || !Number.isFinite(Number(item.value)))) {
      setNotice(text(language, '每个常量都需要名称和有限数值。', 'Each constant needs a name and a finite numeric value.')); return false;
    }
    setNotice(text(language, '参数草稿有效。连接有效地图快照后才能载入栅格源并执行。', 'The parameter draft is valid. Connect a complete map snapshot to inspect raster sources and evaluate it.'));
    return true;
  };
  const saveLocalDraft = (): void => {
    if (!validateDraft()) return;
    setDraftSaved(true);
  };

  return (
    <div className="tools-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '栅格计算器' : 'RASTER CALCULATOR'}</span><h2>{zh ? '栅格计算器' : 'Raster Calculator'}</h2>
          <p>{zh ? '编辑表达式、常量与 NoData 行为。输入栅格必须来自当前已保存地图的真实源清单。' : 'Edit expressions, constants, and NoData behavior. Input rasters must come from the current saved map source catalog.'}</p></div>
        <span className={`tools-runtime-pill ${inspectAvailable && evaluateAvailable ? 'is-connected' : 'is-unavailable'}`}>{inspectAvailable && evaluateAvailable ? 'C#' : (zh ? 'C# 未接通' : 'C# unavailable')}</span>
      </section>

      <div className="tools-raster-grid">
        <section className="tools-subpanel tools-raster-editor">
          <div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {zh ? '配方' : 'RECIPE'}</span><h3>{zh ? '表达式与输入' : 'Expression and inputs'}</h3></div><span className="tools-badge">{zh ? '栅格计算配方' : 'RasterCalculatorRecipe'}</span></div>
          <label className="tools-field"><span>{zh ? '表达式' : 'Expression'}</span><textarea className="tools-expression" rows={4} spellCheck={false} value={expression} onChange={event => { setExpression(event.target.value); setDraftSaved(false); }} placeholder={zh ? '例如：depth - ndv' : 'For example: depth - ndv'} /></label>
          <p className="tools-field-help">{zh ? '内置保留常量来自原计算器：' : 'Reserved constants from the source calculator:'} <code>{reservedNames.join(', ')}</code></p>
          <div className="tools-subheading-row"><h4>{zh ? '自定义常量' : 'Custom constants'}</h4><button className="tools-text-button" type="button" onClick={addConstant}>{zh ? '+ 添加常量' : '+ Add constant'}</button></div>
          {constants.length === 0 && <p className="tools-muted">{zh ? '尚未添加自定义常量。' : 'No custom constants have been added.'}</p>}
          {constants.map(constant => <div className="tools-constant-row" key={constant.id}>
            <label className="tools-field"><span>{zh ? '名称' : 'Name'}</span><input value={constant.name} onChange={event => updateConstant(constant.id, 'name', event.target.value)} /></label>
            <label className="tools-field"><span>{zh ? '数值' : 'Value'}</span><input inputMode="decimal" value={constant.value} onChange={event => updateConstant(constant.id, 'value', event.target.value)} /></label>
            <button className="tools-remove-button" type="button" aria-label={zh ? '移除常量' : 'Remove constant'} onClick={() => removeConstant(constant.id)}>×</button>
          </div>)}
          <div className="tools-field-grid">
            <label className="tools-field"><span>{zh ? 'NoData 行为' : 'NoData behavior'}</span><select value={noDataBehavior} onChange={event => { setNoDataBehavior(event.target.value as typeof noDataBehavior); setDraftSaved(false); }}>{noDataBehaviors.map(value => <option key={value} value={value}>{zh ? ({ Allow: '允许', WriteNDV: '写入 NoData 值', WriteZero: '写入 0' } as const)[value] : value}</option>)}</select></label>
            <label className="tools-field"><span>{zh ? '输出图层名称' : 'Output layer name'}</span><input value={layerName} onChange={event => { setLayerName(event.target.value); setDraftSaved(false); }} /></label>
          </div>
          <div className="tools-actions"><button className="tools-button tools-button-secondary" type="button" onClick={saveLocalDraft}>{zh ? '检查本地草稿' : 'Validate local draft'}</button>
            <button className="tools-button tools-button-primary" type="button" disabled={!evaluateAvailable || !hasProject} title={zh ? '需要完整 MapTree 快照、可视化修订和源清单' : 'A complete MapTree snapshot, visualization revision, and source catalog are required'} onClick={() => setNotice(text(language, '当前页面上下文没有 MapTree 请求、可视化修订和 Raster Calculator 源快照，无法构造真实 evaluate 请求。', 'The current page context does not include the MapTree request, visualization revision, or raster source snapshot required for a real evaluate request.'))}>{zh ? '计算并预览' : 'Evaluate and preview'}</button></div>
          {notice && <div className={`tools-notice ${draftSaved ? 'is-success' : ''}`} role="status">{notice}</div>}
        </section>

        <section className="tools-subpanel tools-source-inspect">
          <div className="tools-subpanel-heading"><div><span className="tools-kicker">02 / {zh ? '源图层' : 'SOURCE LAYERS'}</span><h3>{zh ? '当前地图栅格源' : 'Current map raster sources'}</h3></div><span className="tools-badge">raster.calculator.inspect</span></div>
          <div className="tools-context-card"><span className={`tools-state-dot ${hasProject ? 'is-ready' : ''}`} /><div><strong>{context.projectPath ? context.projectPath.split(/[\\/]/).pop() : (zh ? '未选择工程' : 'No project selected')}</strong><small>{context.selectedAsset ? `${context.selectedAsset.name} · ${displaySourceKind(context.selectedAsset.kind, language)}` : (zh ? '未选中地图数据源' : 'No map source selected')}</small></div></div>
          <p className="tools-muted">{zh ? '源列表、可用性、范围、NoData、结果图层类型与时序 Profile 均由 raster.calculator.inspect 返回。页面不会用文件名猜测可用栅格。' : 'Source choices, availability, extents, NoData, result map types, and temporal profiles come from raster.calculator.inspect. This page does not infer raster availability from filenames.'}</p>
          <button className="tools-button tools-button-secondary" type="button" disabled={!inspectAvailable || !hasProject} title={zh ? '缺少地图身份与当前 .vis 修订捕获' : 'Map identity and current .vis revision capture are missing'} onClick={() => setNotice(text(language, '无法载入源：FeaturePageProps 目前没有完整的 MapTreeRequest 和当前可视化修订。', 'Sources cannot be inspected because FeaturePageProps does not contain a complete MapTreeRequest and current visualization revision.'))}>{zh ? '载入当前源' : 'Inspect current sources'}</button>
          <div className="tools-callout">{inspectAvailable ? (zh ? 'C# 方法已注册；当前页面仍缺少可审计的地图请求上下文。' : 'The C# method is registered; this page still lacks an auditable map request context.') : (zh ? 'raster.calculator.inspect 尚未注册到当前 C# server。' : 'raster.calculator.inspect is not registered in the current C# server.')}</div>
          <div className="tools-input-bindings"><div className="tools-subheading-row"><h4>{zh ? '输入绑定' : 'Input bindings'}</h4><span className="tools-badge">0</span></div><p className="tools-muted">{zh ? '检查源快照后，才能为变量绑定地形或结果时序剖面。' : 'Inspect the source snapshot before binding variables to Terrain or Result profiles.'}</p></div>
        </section>
      </div>
    </div>
  );
}
