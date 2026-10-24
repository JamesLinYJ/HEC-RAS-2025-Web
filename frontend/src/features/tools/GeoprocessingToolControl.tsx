import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { errorDiagnostics, text } from '../../shared/workbench';
import { geoprocessingCatalog } from './GeoprocessingCatalog';
import { type GeoprocessingFieldValues, type GeoprocessingToolDefinition, type ToolGroupDefinition } from './GeoprocessingToolBase';
import { GeoprocessingInputDetails } from './GeoprocessingInputDetails';
import { GeoprocessingOutputDetails } from './GeoprocessingOutputDetails';
import { ReprojectionToolControl } from './ReprojectionToolControl';
import { ToolGroupButton } from './tool-controls/ToolGroupButton';
import { ToolOptionsCog } from './tool-controls/ToolOptionsCog';
import { ToolOptionsFlyout } from './tool-controls/ToolOptionsFlyout';
import { ToolRichFlyoutControl } from './tool-controls/ToolRichFlyoutControl';
import './tool-controls/toolControls.css';
import './geoprocessingUi.css';

type Props = FeaturePageProps & { onDirtyStateChange?: (owner: string, dirty: boolean) => void };
type ValidationResult = { validationIssues: Array<{ field: string; message: string; severity: string }> };
type FieldValues = GeoprocessingFieldValues;

const groups: ToolGroupDefinition[] = [
  { id: 'polyline', title: { zh: '折线图层工具', en: 'Polyline layer tools' }, ids: ['buffer-polyline', 'line-offset', 'filter', 'reverse-polyline', 'smooth-polylines', 'densify-polylines', 'distance-to-lines', 'proximity-heatmap', 'snap-to-high-ground', 'polyline-to-polygon', 'cull-by-length', 'merge-polylines'] },
  { id: 'polygon', title: { zh: '多边形图层工具', en: 'Polygon layer tools' }, ids: ['buffer-polygon', 'densify-polygons', 'rasterize-polygons', 'polygon-to-polyline', 'filter', 'smooth-polylines', 'snap-to-high-ground', 'laplacian-terrain', 'clip-raster-to-polygon', 'raster-cut-stats'] },
  { id: 'raster', title: { zh: '栅格与地形工具', en: 'Raster and terrain tools' }, ids: ['resample-raster', 'round-raster', 'clip-raster-to-polygon', 'nodata-laplacian-fill', 'raster-cut-stats'] },
  { id: 'point', title: { zh: '点图层工具', en: 'Point layer tools' }, ids: ['flat-area-detector', 'nodata-laplacian-fill'] },
];

function initialValues(tool: GeoprocessingToolDefinition): FieldValues {
  return Object.fromEntries(tool.fields.map(field => [field.key, field.initial]));
}

function downloadJson(value: unknown, language: 'zh' | 'en'): void {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = (language === 'zh' ? '地理处理参数' : 'geoprocessing-parameters') + '.json'; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function isValidationResult(value: unknown): value is ValidationResult {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const issues = (value as { validationIssues?: unknown }).validationIssues;
  return Array.isArray(issues) && issues.every(issue => Boolean(issue) && typeof issue === 'object' &&
    typeof (issue as ValidationResult['validationIssues'][number]).field === 'string' &&
    typeof (issue as ValidationResult['validationIssues'][number]).message === 'string' &&
    typeof (issue as ValidationResult['validationIssues'][number]).severity === 'string');
}

export function GeoprocessingToolControl({ language, context, backend, onDirtyStateChange }: Props) {
  const zh = language === 'zh';
  const [activeToolId, setActiveToolId] = useState<string>('buffer-polyline');
  const activeToolIDRef = useRef(activeToolId); activeToolIDRef.current = activeToolId;
  const [search, setSearch] = useState('');
  const [values, setValues] = useState<FieldValues>(() => initialValues(geoprocessingCatalog[0]!));
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() => new Set(groups.map(group => group.id)));
  const [inputMode, setInputMode] = useState<'All' | 'Selected'>('All');
  const [outputName, setOutputName] = useState('');
  const [outputHandler, setOutputHandler] = useState('Clipboard');
  const [showOptions, setShowOptions] = useState(true);
  const [validation, setValidation] = useState<ValidationResult>();
  const [validationError, setValidationError] = useState<string>();
  const [validationDiagnostic, setValidationDiagnostic] = useState<string>();
  const [validating, setValidating] = useState(false);
  const validationGeneration = useRef(0);
  const validationController = useRef<AbortController | null>(null);
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.selectedAsset?.path,
    context.selectedAsset?.revision, context.selectedAsset?.locator]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
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

  const invalidateValidation = useCallback((): void => {
    validationGeneration.current += 1;
    validationController.current?.abort();
    validationController.current = null;
    setValidating(false); setValidation(undefined); setValidationError(undefined); setValidationDiagnostic(undefined);
  }, []);

  useEffect(() => {
    invalidateValidation();
    setValues(initialValues(activeTool)); setOutputName(''); setInputMode('All');
    setOutputHandler(activeTool.outputKind === 'Raster' ? 'Tiff' : 'Clipboard');
    setShowOptions(true);
  }, [activeTool.id, invalidateValidation]);
  useEffect(() => {
    invalidateValidation();
  }, [owner, invalidateValidation]);
  useEffect(() => () => {
    validationGeneration.current += 1;
    validationController.current?.abort();
    validationController.current = null;
  }, []);
  useEffect(() => { onDirtyStateChange?.('tools.geoprocessing', dirty); return () => onDirtyStateChange?.('tools.geoprocessing', false); }, [dirty, onDirtyStateChange]);

  const filteredTools = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return geoprocessingCatalog.filter(tool => !query || [tool.title.zh, tool.title.en, tool.description.zh, tool.description.en,
      ...tool.fields.flatMap(field => [field.label.zh, field.label.en, field.description?.zh ?? '', field.description?.en ?? ''])]
      .some(value => value.toLocaleLowerCase().includes(query)));
  }, [search]);
  const chooseTool = (tool: GeoprocessingToolDefinition): void => {
    invalidateValidation(); setActiveToolId(tool.id);
    setExpandedGroups(current => new Set([...current, ...groups.filter(group => group.ids.includes(tool.id)).map(group => group.id)]));
  };
  const setField = (field: string, value: string | boolean): void => {
    invalidateValidation(); setValues(current => ({ ...current, [field]: value }));
  };
  const setInputSelection = (value: 'All' | 'Selected'): void => { invalidateValidation(); setInputMode(value); };
  const setOutputChoice = (value: string): void => { invalidateValidation(); setOutputHandler(value); };
  const setOutputTitle = (value: string): void => { invalidateValidation(); setOutputName(value); };

  const validationRequest = (): Record<string, unknown> => ({
    operation: activeTool.operation,
    cultureName: zh ? 'zh-CN' : 'en-US',
    inputMode,
    outputHandler,
    outputName: outputName || null,
    ...values,
  });

  const validate = async (): Promise<void> => {
    invalidateValidation();
    if (!validateAvailable) { setValidationError(text(language, '当前服务未开放参数校验。', 'Parameter validation is unavailable on the current service.')); return; }
    const requestOwner = owner;
    const requestToolID = activeTool.id;
    const requestValues = validationRequest();
    const controller = new AbortController();
    validationController.current = controller;
    const requestGeneration = ++validationGeneration.current;
    const current = (): boolean => requestGeneration === validationGeneration.current && ownerRef.current === requestOwner &&
      activeToolIDRef.current === requestToolID && !controller.signal.aborted;
    setValidating(true);
    try {
      const result: unknown = await backend.request('geoprocessing.validate', requestValues, { signal: controller.signal });
      if (!current()) return;
      if (!isValidationResult(result)) throw new Error(text(language, '服务返回的校验回执格式无效。', 'The service returned an invalid validation receipt.'));
      setValidation(result);
    } catch (error) {
      if (controller.signal.aborted || !current()) return;
      setValidationError(text(language, '服务端未能完成参数校验。', 'The service could not complete parameter validation.'));
      const diagnostic = errorDiagnostics(error);
      try { setValidationDiagnostic(typeof diagnostic === 'string' ? diagnostic : JSON.stringify(diagnostic, null, 2)); }
      catch { setValidationDiagnostic(String(diagnostic)); }
    }
    finally {
      if (requestGeneration === validationGeneration.current) {
        setValidating(false);
        if (validationController.current === controller) validationController.current = null;
      }
    }
  };

  const cancelValidation = (): void => {
    if (!validating) return;
    validationController.current?.abort();
    validationGeneration.current += 1;
    validationController.current = null;
    setValidating(false);
    setValidation(undefined);
    setValidationError(undefined);
    setValidationDiagnostic(undefined);
  };

  const severityLabel = (severity: string): string => {
    if (!zh) return severity;
    const labels: Record<string, string> = { error: '错误', typeerror: '类型错误', warning: '警告', information: '信息', info: '信息' };
    return labels[severity.toLowerCase()] ?? severity;
  };

  const selectedGroup = groups.find(group => group.ids.includes(activeTool.id));
  const toggleGroup = (groupID: string): void => setExpandedGroups(current => {
    const next = new Set(current);
    if (next.has(groupID)) next.delete(groupID); else next.add(groupID);
    return next;
  });
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
          <p>{zh ? '按数据类型分组查找工具，编辑参数并请求服务端校验。' : 'Find tools by data type, edit parameters, and request backend validation.'}</p></div>
        <button className="gp-ui-utility-button" type="button" onClick={() => setShowReprojection(true)}>
          <span aria-hidden="true">◎</span>{zh ? '坐标转换表' : 'Coordinate reprojection'}
        </button>
        <span className={`tools-runtime-pill ${validateAvailable ? 'is-connected' : 'is-unavailable'}`}>{validateAvailable ? (zh ? '校验已接通' : 'Validation connected') : (zh ? '校验未接通' : 'Validation unavailable')}</span>
      </section>
      <div className="tools-geoprocessing-layout">
        <aside className="tools-catalog-panel">
          <label className="tools-search-field"><span aria-hidden="true">⌕</span><input value={search} onChange={event => setSearch(event.target.value)} placeholder={zh ? '搜索工具或参数…' : 'Search tools…'} /></label>
          <div className="tools-catalog-count">{filteredTools.length} / {geoprocessingCatalog.length} {zh ? '个工具' : 'tools'}</div>
          <div className="tools-catalog-groups">{groups.map(group => {
            const tools = geoprocessingCatalog.filter(tool => group.ids.includes(tool.id) && filteredTools.some(item => item.id === tool.id));
            if (!tools.length) return null;
            const visibleGroup = { ...group, ids: tools.map(tool => tool.id) };
            return <div className="tools-catalog-group gp-control-catalog-group" key={group.id}>
              <ToolGroupButton group={visibleGroup} language={language} count={tools.length}
                expanded={expandedGroups.has(group.id)} onToggle={() => toggleGroup(group.id)} />
              {expandedGroups.has(group.id) && <ToolRichFlyoutControl title={group.title[language]} language={language}
                tools={tools} activeToolId={activeTool.id} onSelect={chooseTool} />}
            </div>;
          })}</div>
        </aside>

        <main className="tools-parameter-panel">
          <div className="tools-selected-tool-heading gp-control-selected-heading"><div><span className="tools-kicker">{selectedGroup ? selectedGroup.title[language] : (zh ? '工具' : 'Tool')}</span><h3>{activeTool.title[language]}</h3><p>{activeTool.description[language]}</p></div>
            <ToolOptionsCog language={language} expanded={showOptions} onToggle={() => setShowOptions(open => !open)} /></div>

          <div className="tools-parameter-grid">
            <GeoprocessingInputDetails language={language} context={context} tool={activeTool} inputMode={inputMode} onInputModeChange={setInputSelection} />
            <div className="tools-parameter-fields"><ToolOptionsFlyout open={showOptions} tool={activeTool} language={language} values={values}
              outputOptions={outputOptions} outputHandler={outputHandler} outputName={outputName} fileOutput={fileOutput}
              onFieldChange={setField} onOutputHandlerChange={setOutputChoice} onOutputNameChange={setOutputTitle}
              onExportParameters={() => downloadJson(validationRequest(), language)} onClose={() => setShowOptions(false)} /></div>
          </div>

          <div className="tools-actions tools-gp-actions">
            <button className="tools-button tools-button-secondary" type="button" onClick={() => void validate()} disabled={validating || !validateAvailable}>{validating ? (zh ? '校验中…' : 'Validating…') : (zh ? '校验参数' : 'Validate parameters')}</button>
            {validating && <button className="tools-button tools-button-danger" type="button" onClick={cancelValidation}>{zh ? '取消校验' : 'Cancel validation'}</button>}
            <button className="tools-button tools-button-primary" type="button" disabled title={zh ? '缺少已核验的地图图层定位、源文件修订和输入几何捕获' : 'A verified map-layer locator, source revision, and input-geometry capture are required'}>{zh ? '运行工具' : 'Run tool'}</button>
          </div>

          {validation && <section className="tools-validation-result"><div className="tools-subheading-row"><h4>{zh ? '后端参数校验' : 'Backend parameter validation'}</h4><span className="tools-badge">{validation.validationIssues.length} {zh ? '项' : 'issues'}</span></div>
            {validation.validationIssues.length ? validation.validationIssues.map((issue, index) => {
              const field = activeTool.fields.find(candidate => candidate.key.toLocaleLowerCase() === issue.field.toLocaleLowerCase());
              return <div className={`tools-validation-item severity-${issue.severity.toLowerCase()}`} key={`${issue.field}-${index}`}>
                <strong>{zh ? '字段：' : 'Field: '}{field?.label[language] ?? issue.field}</strong><span>{issue.message}</span><small>{severityLabel(issue.severity)}</small></div>;
            }) : <div className="tools-notice is-success">{zh ? '服务端未返回参数错误。此回执只确认参数校验，不代表输入源或执行已验证。' : 'The service returned no parameter errors. This receipt confirms parameter validation only, not source reading or execution.'}</div>}
          </section>}
          {validationError && <div className="tools-inline-error" role="alert">{validationError}{validationDiagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{validationDiagnostic}</pre></details>}</div>}
          <div className="tools-execution-boundary">{executeAvailable ? (zh ? '当前选择缺少经过核验的地图图层定位、源文件修订和输入几何；运行操作保持禁用。' : 'The current selection lacks a verified map-layer locator, source revision, and input geometry; execution remains disabled.') : (zh ? '当前服务未开放工具执行。参数仍可编辑；校验功能按服务实际发布状态启用。' : 'The current service has not advertised tool execution. Parameters remain editable; validation follows the service capabilities.')}</div>
          <GeoprocessingOutputDetails language={language} running={false} />
        </main>
      </div>
    </div>
  );
}
