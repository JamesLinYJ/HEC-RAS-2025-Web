import { useEffect, useMemo, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { geoprocessingCatalog } from './GeoprocessingCatalog';
import { displayToolOption, type GeoprocessingToolDefinition, type ToolField } from './GeoprocessingToolBase';
import { GeoprocessingInputDetails } from './GeoprocessingInputDetails';
import { GeoprocessingOutputDetails } from './GeoprocessingOutputDetails';
import { ReprojectionToolControl } from './ReprojectionToolControl';
import './geoprocessingUi.css';

type Props = FeaturePageProps & { onDirtyStateChange?: (owner: string, dirty: boolean) => void };
type ValidationResult = { validationIssues: Array<{ field: string; message: string; severity: string }> };
type FieldValues = Record<string, string | boolean>;
type ToolGroup = { id: string; title: { zh: string; en: string }; source: string; ids: string[] };

const groups: ToolGroup[] = [
  { id: 'polyline', title: { zh: '折线图层工具', en: 'Polyline layer tools' }, source: 'PolylineLayerBehaviors.cs', ids: ['buffer-polyline', 'line-offset', 'filter', 'reverse-polyline', 'smooth-polylines', 'densify-polylines', 'distance-to-lines', 'proximity-heatmap', 'snap-to-high-ground', 'polyline-to-polygon', 'cull-by-length', 'merge-polylines'] },
  { id: 'polygon', title: { zh: '多边形图层工具', en: 'Polygon layer tools' }, source: 'PolygonLayerBehaviors.cs', ids: ['buffer-polygon', 'densify-polygons', 'rasterize-polygons', 'polygon-to-polyline', 'filter', 'smooth-polylines', 'snap-to-high-ground', 'laplacian-terrain', 'clip-raster-to-polygon', 'raster-cut-stats'] },
  { id: 'raster', title: { zh: '栅格与地形工具', en: 'Raster and terrain tools' }, source: 'RasterMapLayerBehaviors.cs · TerrainVisualContextBehaviors.cs', ids: ['resample-raster', 'round-raster', 'clip-raster-to-polygon', 'nodata-laplacian-fill', 'raster-cut-stats'] },
  { id: 'point', title: { zh: '点图层工具', en: 'Point layer tools' }, source: 'PointVectorContextBehaviors.cs', ids: ['flat-area-detector', 'nodata-laplacian-fill'] },
];

const localizedGroups = (group: ToolGroup, language: 'zh' | 'en'): string => group.title[language];

function initialValues(tool: GeoprocessingToolDefinition): FieldValues {
  return Object.fromEntries(tool.fields.map(field => [field.key, field.initial]));
}

function visibleField(field: ToolField, values: FieldValues): boolean {
  return !field.showWhen || values[field.showWhen.key] === field.showWhen.value;
}

function downloadJson(value: unknown, language: 'zh' | 'en'): void {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = (language === 'zh' ? '地理处理参数' : 'geoprocessing-parameters') + '.json'; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function FieldControl({ field, value, onChange, language }: { field: ToolField; value: string | boolean | undefined; onChange: (value: string | boolean) => void; language: 'zh' | 'en' }) {
  const label = field.label[language];
  if (field.kind === 'toggle') return <label className="tools-toggle-field"><span>{label}</span><input type="checkbox" checked={value === true} onChange={event => onChange(event.target.checked)} /></label>;
  if (field.kind === 'select') return <label className="tools-field"><span>{label}</span><select value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)}>{(field.options ?? []).map(option => <option value={option} key={option}>{displayToolOption(option, language)}</option>)}</select></label>;
  return <label className="tools-field"><span>{label}</span><input type={field.kind === 'number' ? 'number' : 'text'} min={field.min} step={field.step ?? 'any'} inputMode={field.kind === 'number' ? 'decimal' : undefined} value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)} /></label>;
}

export function GeoprocessingToolControl({ language, context, backend, onDirtyStateChange }: Props) {
  const zh = language === 'zh';
  const [activeToolId, setActiveToolId] = useState<string>('buffer-polyline');
  const [search, setSearch] = useState('');
  const [values, setValues] = useState<FieldValues>(() => initialValues(geoprocessingCatalog[0]!));
  const [inputMode, setInputMode] = useState<'All' | 'Selected'>('All');
  const [outputName, setOutputName] = useState('');
  const [outputHandler, setOutputHandler] = useState('Clipboard');
  const [validation, setValidation] = useState<ValidationResult>();
  const [validationError, setValidationError] = useState<string>();
  const [validating, setValidating] = useState(false);
  const [showReprojection, setShowReprojection] = useState(false);
  const activeTool = geoprocessingCatalog.find(tool => tool.id === activeToolId) ?? geoprocessingCatalog[0]!;
  const defaults = initialValues(activeTool);
  const dirty = outputName !== '' || inputMode !== 'All' || outputHandler !== (activeTool.outputKind === 'Raster' ? 'Tiff' : 'Clipboard') || JSON.stringify(values) !== JSON.stringify(defaults);
  const fileOutput = ['Shapefile', 'Map Layer', 'Tiff', 'CSV File', 'Text File'].includes(outputHandler);
  const outputOptions = activeTool.outputKind === 'Raster' ? ['Tiff', 'Map Layer']
    : activeTool.outputKind === 'Text' ? ['Clipboard', 'CSV File', 'Text File']
      : ['Clipboard', 'Shapefile', 'Map Layer'];
  const executeAvailable = backend.supports('geoprocessing.execute');
  const validateAvailable = backend.supports('geoprocessing.validate');

  useEffect(() => {
    setValues(initialValues(activeTool)); setOutputName(''); setInputMode('All');
    setOutputHandler(activeTool.outputKind === 'Raster' ? 'Tiff' : 'Clipboard');
    setValidation(undefined); setValidationError(undefined);
  }, [activeTool.id]);
  useEffect(() => { onDirtyStateChange?.('tools.geoprocessing', dirty); return () => onDirtyStateChange?.('tools.geoprocessing', false); }, [dirty, onDirtyStateChange]);

  const filteredTools = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return geoprocessingCatalog.filter(tool => !query || [tool.title.zh, tool.title.en, tool.description.zh, tool.description.en, tool.operation, tool.sourceFile].some(value => value.toLocaleLowerCase().includes(query)));
  }, [search]);
  const chooseTool = (tool: GeoprocessingToolDefinition): void => { setActiveToolId(tool.id); };
  const setField = (field: string, value: string | boolean): void => { setValues(current => ({ ...current, [field]: value })); setValidation(undefined); setValidationError(undefined); };

  const validationRequest = (): Record<string, unknown> => ({
    operation: activeTool.operation,
    cultureName: zh ? 'zh-CN' : 'en-US',
    inputMode,
    outputHandler,
    outputName: outputName || null,
    ...values,
  });

  const validate = async (): Promise<void> => {
    setValidation(undefined); setValidationError(undefined);
    if (!validateAvailable) { setValidationError(text(language, '当前 C# 后端未注册 geoprocessing.validate。', 'geoprocessing.validate is not registered in the current C# backend.')); return; }
    setValidating(true);
    try {
      const result = await backend.request<ValidationResult>('geoprocessing.validate', validationRequest());
      setValidation(result);
    } catch (error) {
      const detail = message(error);
      setValidationError(zh ? `后端参数校验失败。后端原始错误：${detail}` : `Backend parameter validation failed. Backend error: ${detail}`);
    }
    finally { setValidating(false); }
  };

  const severityLabel = (severity: string): string => {
    if (!zh) return severity;
    const labels: Record<string, string> = { error: '错误', typeerror: '类型错误', warning: '警告', information: '信息', info: '信息' };
    return labels[severity.toLowerCase()] ?? severity;
  };

  const selectedGroup = groups.find(group => group.ids.includes(activeTool.id));
  if (showReprojection) {
    return <div className="tools-area tools-geoprocessing-area">
      <ReprojectionToolControl language={language} context={context} backend={backend}
        onClose={() => setShowReprojection(false)} onDirtyStateChange={onDirtyStateChange} />
    </div>;
  }
  return (
    <div className="tools-area tools-geoprocessing-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '地理处理工具' : 'GEOPROCESSING'}</span><h2>{zh ? '地图与栅格工具箱' : 'Map and raster toolbox'}</h2>
          <p>{zh ? '按原 C# 图层行为分组浏览工具；可搜索、编辑参数并调用 C# 参数校验。' : 'Browse tools grouped by the original C# layer behaviors, edit parameters, and run backend parameter validation.'}</p></div>
        <button className="gp-ui-utility-button" type="button" onClick={() => setShowReprojection(true)}>
          <span aria-hidden="true">◎</span>{zh ? '坐标转换表' : 'Coordinate reprojection'}
        </button>
        <span className={`tools-runtime-pill ${validateAvailable ? 'is-connected' : 'is-unavailable'}`}>{validateAvailable ? 'C#' : (zh ? '校验未接通' : 'Validation unavailable')}</span>
      </section>
      <div className="tools-geoprocessing-layout">
        <aside className="tools-catalog-panel">
          <label className="tools-search-field"><span aria-hidden="true">⌕</span><input value={search} onChange={event => setSearch(event.target.value)} placeholder={zh ? '搜索工具或参数…' : 'Search tools…'} /></label>
          <div className="tools-catalog-count">{filteredTools.length} / {geoprocessingCatalog.length} {zh ? '个工具' : 'tools'}</div>
          <div className="tools-catalog-groups">{groups.map(group => {
            const tools = geoprocessingCatalog.filter(tool => group.ids.includes(tool.id) && filteredTools.some(item => item.id === tool.id));
            if (!tools.length) return null;
            return <div className="tools-catalog-group" key={group.id}><div className="tools-group-title">{localizedGroups(group, language)}<small>{zh ? '来源：' : 'Source: '}{group.source}</small></div>
              {tools.map(tool => <button key={`${group.id}-${tool.id}`} type="button" className={`tools-catalog-item ${activeTool.id === tool.id ? 'is-active' : ''}`} onClick={() => chooseTool(tool)}>
                <span className="tools-tool-mark">{tool.outputKind === 'Raster' ? '▧' : tool.outputKind === 'Polygon' ? '⬡' : '⌁'}</span><span><strong>{tool.title[language]}</strong><small>{tool.operation}</small></span><span className="tools-catalog-arrow">›</span>
              </button>)}
            </div>;
          })}</div>
        </aside>

        <main className="tools-parameter-panel">
          <div className="tools-selected-tool-heading"><div><span className="tools-kicker">{selectedGroup ? localizedGroups(selectedGroup, language) : (zh ? '工具' : 'Tool')}</span><h3>{activeTool.title[language]}</h3><p>{activeTool.description[language]}</p></div><span className="tools-operation-tag">{activeTool.operation}</span></div>
          <div className="tools-provenance-row"><span>{zh ? '来源：' : 'Source: '}{activeTool.behaviorFile}</span><span>→</span><span>{activeTool.sourceFile}</span><span>→</span><span>GeoprocessingToolControl.cs</span></div>

          <div className="tools-parameter-grid">
            <GeoprocessingInputDetails language={language} context={context} tool={activeTool} inputMode={inputMode} onInputModeChange={setInputMode} />
            <div className="tools-parameter-fields"><div className="tools-subheading-row"><h4>{zh ? '参数' : 'Parameters'}</h4><button className="tools-text-button" type="button" onClick={() => downloadJson(validationRequest(), language)}>{zh ? '导出参数 JSON' : 'Export parameter JSON'}</button></div>
              {activeTool.fields.filter(field => visibleField(field, values)).length ? <div className="tools-field-grid">{activeTool.fields.filter(field => visibleField(field, values)).map(field => <FieldControl key={field.key} field={field} value={values[field.key]} onChange={value => setField(field.key, value)} language={language} />)}</div> : <p className="tools-muted">{zh ? '此操作没有额外参数。' : 'This operation has no additional parameters.'}</p>}
              <div className="tools-field-grid tools-common-fields">
                <label className="tools-field"><span>{zh ? '输出方式' : 'Output handler'}</span><select value={outputHandler} onChange={event => setOutputHandler(event.target.value)}>{outputOptions.map(option => <option key={option} value={option}>{displayToolOption(option, language)}</option>)}</select></label>
                {fileOutput && <label className="tools-field"><span>{zh ? '输出名称' : 'Output name'}</span><input value={outputName} onChange={event => setOutputName(event.target.value)} placeholder={zh ? '输入名称' : 'Enter a name'} /></label>}
              </div>
              <p className="tools-field-help">{zh ? '输出方式和参数选项来自后端校验契约；写入型输出需要用户给出输出名称。' : 'Output handlers and parameter options follow the backend validation contract. File outputs need a user-provided name.'}</p>
            </div>
          </div>

          <div className="tools-actions tools-gp-actions">
            <button className="tools-button tools-button-secondary" type="button" onClick={() => void validate()} disabled={validating || !validateAvailable}>{validating ? (zh ? '校验中…' : 'Validating…') : (zh ? '校验参数' : 'Validate parameters')}</button>
            <button className="tools-button tools-button-primary" type="button" disabled title={zh ? '缺少地图源捕获、源文件 SHA 修订、输入几何和投影快照' : 'Map source capture, file SHA revision, input geometry, and projection snapshot are missing'}>{zh ? '运行 C# 工具' : 'Run C# tool'}</button>
          </div>

          {validation && <section className="tools-validation-result"><div className="tools-subheading-row"><h4>{zh ? '后端参数校验' : 'Backend parameter validation'}</h4><span className="tools-badge">{validation.validationIssues.length} {zh ? '项' : 'issues'}</span></div>
            {validation.validationIssues.length ? validation.validationIssues.map((issue, index) => <div className={`tools-validation-item severity-${issue.severity.toLowerCase()}`} key={`${issue.field}-${index}`}><strong>{zh ? '字段：' : 'Field: '}{issue.field}</strong><span>{zh ? '后端原始信息：' : 'Backend message: '}{issue.message}</span><small>{severityLabel(issue.severity)}</small></div>) : <div className="tools-notice is-success">{zh ? '后端未返回参数错误。此结果只代表参数校验，不代表输入源或执行已验证。' : 'The backend returned no parameter errors. This validates parameters only, not source inputs or execution.'}</div>}
          </section>}
          {validationError && <div className="tools-inline-error" role="alert">{validationError}</div>}
          <div className="tools-execution-boundary">{executeAvailable ? (zh ? 'geoprocessing.execute 已注册，但当前页面缺少持久地图源身份、源文件 SHA 修订、输入要素捕获与投影快照；执行按钮保持禁用。' : 'geoprocessing.execute is registered, but this page lacks the persisted map-source identity, source-file SHA revision, captured input features, and projection snapshot; execution stays disabled.') : (zh ? 'geoprocessing.execute 尚未注册到此 C# 服务端。参数仍可编辑；只有已实际注册的参数校验方法才会被调用。' : 'geoprocessing.execute is not registered in this C# server. Parameters remain editable; only a genuinely registered validation method can be called.')}</div>
          <GeoprocessingOutputDetails language={language} running={false} />
        </main>
      </div>
    </div>
  );
}
