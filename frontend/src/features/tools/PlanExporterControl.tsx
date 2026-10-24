import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import {
  createPlanExportRequest,
  parsePlanExportDuplexResult,
  parsePlanExportInspection,
  planExportContextKey,
  planExportRequestKey,
  type PlanExportForm,
  type PlanExportInspection,
  type PlanExportReceipt,
} from './exportWorkflow';
import './exports.css';

type Props = Pick<FeaturePageProps, 'language' | 'context' | 'backend'>;
type StoredInspection = { value: PlanExportInspection; requestKey: string; contextKey: string };
type ExportProgress = { fraction?: number; label: string };

const inspectMethod = 'planExport.inspect';
const executeMethod = 'planExport.executeDuplex';

export function PlanExporterControl({ language, context, backend }: Props) {
  const contextKey = planExportContextKey(context);
  const contextKeyRef = useRef(contextKey);
  contextKeyRef.current = contextKey;
  const generation = useRef(0);
  const activeController = useRef<AbortController | null>(null);

  const [planPath, setPlanPath] = useState(context.selectedAsset?.kind === 'Plan' ? context.selectedAsset.path : '');
  const [outputPath, setOutputPath] = useState('');
  const [zip, setZip] = useState(true);
  const [clipRasters, setClipRasters] = useState(true);
  const [resampleEnabled, setResampleEnabled] = useState(false);
  const [resampleCellsize, setResampleCellsize] = useState('10');
  const [externalConnections, setExternalConnections] = useState<PlanExportForm['externalConnections']>('ReferenceByAbsolutePath');
  const [replaceFile, setReplaceFile] = useState(false);
  const [plans, setPlans] = useState<PlanExportInspection['plans']>([]);
  const [inspection, setInspection] = useState<StoredInspection | null>(null);
  const [receipt, setReceipt] = useState<PlanExportReceipt | null>(null);
  const [phase, setPhase] = useState<'inspect' | 'execute' | null>(null);
  const [error, setError] = useState<unknown>();
  const [validationMessage, setValidationMessage] = useState<string | null>(null);
  const [diagnostics, setDiagnostics] = useState<unknown>();
  const [progress, setProgress] = useState<ExportProgress | null>(null);
  const [cancelRequested, setCancelRequested] = useState(false);

  useEffect(() => {
    generation.current += 1;
    activeController.current?.abort('Project context changed.');
    activeController.current = null;
    setPlanPath(context.selectedAsset?.kind === 'Plan' ? context.selectedAsset.path : '');
    setOutputPath('');
    setPlans([]);
    setInspection(null);
    setReceipt(null);
    setPhase(null);
    setError(undefined);
    setValidationMessage(null);
    setDiagnostics(undefined);
    setProgress(null);
    setCancelRequested(false);
  }, [contextKey, context.selectedAsset?.kind, context.selectedAsset?.path]);

  useEffect(() => () => {
    generation.current += 1;
    activeController.current?.abort('Plan export panel closed.');
  }, []);

  const updateForm = (update: () => void): void => {
    update();
    setInspection(null);
    setReceipt(null);
    setError(undefined);
    setValidationMessage(null);
    setDiagnostics(undefined);
  };

  const currentForm = (): PlanExportForm => {
    let cellsize: number | null = null;
    if (resampleEnabled) {
      const parsed = Number(resampleCellsize);
      if (!resampleCellsize.trim() || !Number.isFinite(parsed) || parsed <= 0) {
        throw new Error(text(language, '请输入大于零的有效重采样像元大小。', 'Enter a finite resample cell size greater than zero.'));
      }
      cellsize = parsed;
    }
    return {
      planPath, outputPath, zip, clipRasters, resampleCellsize: cellsize,
      externalConnections, replaceFile: zip && replaceFile,
    };
  };

  const makeProgress = (value: unknown): ExportProgress => {
    if (!isRecord(value)) return { label: String(value) };
    const label = [value.taskName, value.message].filter((item): item is string => typeof item === 'string' && item.length > 0).join(' · ');
    const fraction = typeof value.fraction === 'number' && Number.isFinite(value.fraction)
      ? Math.max(0, Math.min(1, value.fraction)) : undefined;
    return { fraction, label: label || text(language, 'C# 服务正在处理导出任务。', 'The C# service is processing the export.') };
  };

  const inspect = async (): Promise<void> => {
    setError(undefined);
    setDiagnostics(undefined);
    setValidationMessage(null);
    setInspection(null);
    setReceipt(null);
    setProgress(null);
    setCancelRequested(false);

    if (!context.projectPath || !context.projectXml) {
      setError(new Error(text(language, '请先打开已保存的 HEC-RAS 工程。', 'Open a saved HEC-RAS project first.')));
      return;
    }
    if (!context.projectSessionID) {
      setError(new Error(text(language, '当前工程会话标识缺失，请重新载入工程。', 'The active project session is missing. Reload the project.')));
      return;
    }
    if (!backend.supports(inspectMethod)) {
      setError(new Error(text(language, '当前 C# 服务未发布计划导出检查接口。', 'The C# service has not advertised plan export inspection.')));
      return;
    }

    let form: PlanExportForm;
    try { form = currentForm(); }
    catch (caught) { setError(caught); return; }

    activeController.current?.abort('A newer plan export request started.');
    const controller = new AbortController();
    activeController.current = controller;
    const requestGeneration = ++generation.current;
    const requestContextKey = contextKey;
    setPhase('inspect');

    const isCurrent = (): boolean => requestGeneration === generation.current
      && requestContextKey === contextKeyRef.current;
    try {
      let request = createPlanExportRequest(context, form);
      let result = parsePlanExportInspection(await backend.request<unknown>(inspectMethod, request, { signal: controller.signal }));
      if (!isCurrent()) return;

      setPlans(result.plans);
      if (request.planPath && !result.plans.some(plan => plan.id === request.planPath)) {
        setPlanPath('');
        setOutputPath('');
        setValidationMessage(text(language, '当前选择未出现在 Host 返回的计划目录中，请从列表重新选择。', 'The selected path is not in the Host plan catalog. Choose a plan from the returned list.'));
        return;
      }

      // The Host supplies the actual default path. Re-inspect that exact path so
      // the revision covers the final output and all current export options.
      if (request.planPath && !request.outputPath && result.suggestedOutputPath) {
        form = { ...form, outputPath: result.suggestedOutputPath };
        setOutputPath(result.suggestedOutputPath);
        request = createPlanExportRequest(context, form);
        result = parsePlanExportInspection(await backend.request<unknown>(inspectMethod, request, { signal: controller.signal }));
        if (!isCurrent()) return;
        setPlans(result.plans);
      }

      setValidationMessage(result.validationMessage);
      setInspection({ value: result, requestKey: planExportRequestKey(request), contextKey: requestContextKey });
    } catch (caught) {
      if (!isCurrent()) return;
      setError(caught);
      setDiagnostics(errorDiagnostics(caught));
    } finally {
      if (requestGeneration === generation.current) {
        setPhase(null);
        if (activeController.current === controller) activeController.current = null;
      }
    }
  };

  const execute = async (): Promise<void> => {
    setError(undefined);
    setDiagnostics(undefined);
    setReceipt(null);
    setProgress(null);
    setCancelRequested(false);
    if (!backend.supports(executeMethod)) {
      setError(new Error(text(language, '当前 C# 服务未发布可取消的计划导出接口。', 'The C# service has not advertised cancellable plan export.')));
      return;
    }

    let form: PlanExportForm;
    try { form = currentForm(); }
    catch (caught) { setError(caught); return; }
    const request = createPlanExportRequest(context, form, inspection?.value.revision ?? null);
    const requestKey = planExportRequestKey(request);
    if (!inspection?.value.canExecute || !inspection.value.revision || inspection.value.selectedPlanPath !== request.planPath
      || inspection.requestKey !== requestKey || inspection.contextKey !== contextKey) {
      setError(new Error(text(language, '当前选项尚未通过检查，请重新检查后再导出。', 'Inspect the current options again before exporting.')));
      setInspection(null);
      return;
    }

    activeController.current?.abort('A newer plan export request started.');
    const controller = new AbortController();
    activeController.current = controller;
    const requestGeneration = ++generation.current;
    const requestContextKey = contextKey;
    const isCurrent = (): boolean => requestGeneration === generation.current
      && requestContextKey === contextKeyRef.current;
    setPhase('execute');
    try {
      const raw = await backend.request<unknown>(executeMethod, { export: request, uploadAfterExport: false }, {
        signal: controller.signal,
        onProgress: value => { if (isCurrent()) setProgress(makeProgress(value)); },
      });
      if (!isCurrent()) return;
      const result = parsePlanExportDuplexResult(raw);
      if (result.state === 'cancelled') {
        setCancelRequested(false);
        setValidationMessage(text(language, 'C# Host 已确认导出取消。', 'The C# Host confirmed that the export was cancelled.'));
      } else if (result.receipt) {
        if (result.receipt.projectSessionID !== context.projectSessionID
          || result.receipt.projectPath !== context.projectPath
          || result.receipt.planPath !== request.planPath
          || result.receipt.sourceRevision !== inspection.value.revision) {
          throw new Error(text(language, '服务端回执与当前工程或检查修订不匹配，未采纳此回执。', 'The Host receipt does not match the active project or inspected revision.'));
        }
        setReceipt(result.receipt);
        setCancelRequested(false);
        setValidationMessage(null);
      }
    } catch (caught) {
      if (!isCurrent()) return;
      if (isAbortError(caught)) {
        setCancelRequested(false);
        setValidationMessage(text(language, 'C# Host 已确认导出取消。', 'The C# Host confirmed that the export was cancelled.'));
      } else {
        setError(caught);
        setDiagnostics(errorDiagnostics(caught));
      }
    } finally {
      if (requestGeneration === generation.current) {
        setPhase(null);
        if (activeController.current === controller) activeController.current = null;
      }
    }
  };

  const cancel = (): void => {
    if (!activeController.current || phase !== 'execute') return;
    setCancelRequested(true);
    activeController.current.abort('User requested plan export cancellation.');
  };

  let liveKey = '';
  try { liveKey = planExportRequestKey(createPlanExportRequest(context, currentForm())); }
  catch { /* The form error is shown when inspection is requested. */ }
  const canExecute = phase === null && Boolean(backend.supports(executeMethod) && inspection?.value.canExecute
    && inspection.value.revision && inspection.value.selectedPlanPath === planPath
    && inspection.requestKey === liveKey && inspection.contextKey === contextKey && planPath && outputPath);
  const canInspect = phase === null && Boolean(context.projectPath && context.projectXml && context.projectSessionID && backend.supports(inspectMethod));

  return (
    <section className="tools-subpanel export-plan-panel">
      <div className="tools-subpanel-heading">
        <div><span className="tools-kicker">01 / {text(language, '计划导出', 'PLAN EXPORT')}</span><h3>{text(language, '导出计算计划', 'Export a plan')}</h3></div>
        <span className={`tools-runtime-pill ${backend.supports(inspectMethod) && backend.supports(executeMethod) ? 'is-connected' : 'is-unavailable'}`}>
          {backend.supports(inspectMethod) && backend.supports(executeMethod) ? 'C# Host' : text(language, '未接通', 'Unavailable')}
        </span>
      </div>

      {!context.projectPath || !context.projectXml ? <p className="tools-export-callout">{text(language, '请先打开已保存的工程，再检查可导出的计算计划。', 'Open a saved project to inspect its available plans.')}</p> : null}

      <div className="tools-export-grid">
        <label className="tools-field">
          <span>{text(language, '计算计划', 'Plan')}</span>
          <select value={planPath} onChange={event => updateForm(() => { setPlanPath(event.target.value); setOutputPath(''); })} disabled={phase !== null}>
            <option value="">{text(language, '检查工程并选择计划', 'Inspect the project and choose a plan')}</option>
            {plans.map(plan => <option value={plan.id} key={plan.id}>{plan.title} — {plan.id}</option>)}
          </select>
        </label>
        <label className="tools-field">
          <span>{text(language, '输出格式', 'Output format')}</span>
          <select value={zip ? 'zip' : 'folder'} onChange={event => updateForm(() => { setZip(event.target.value === 'zip'); setOutputPath(''); })} disabled={phase !== null}>
            <option value="zip">ZIP</option>
            <option value="folder">{text(language, '目录', 'Folder')}</option>
          </select>
        </label>
      </div>

      <label className="tools-field">
        <span>{text(language, '服务端输出路径', 'Server output path')}</span>
        <input value={outputPath} onChange={event => updateForm(() => setOutputPath(event.target.value))} disabled={phase !== null}
          placeholder={text(language, '检查后可采用 Host 返回的路径', 'Inspect to use a path suggested by the Host')} />
        <small>{text(language, '路径由 C# 服务所在机器解析；检查时可采用 Host 返回的建议路径。', 'The path is resolved on the C# service machine. Inspection can fill the Host suggested path.')}</small>
      </label>

      <div className="tools-export-options">
        <label className="tools-export-check"><input type="checkbox" checked={clipRasters} onChange={event => updateForm(() => setClipRasters(event.target.checked))} disabled={phase !== null} /><span>{text(language, '按几何范围裁剪栅格', 'Clip rasters to the Geometry extent')}</span></label>
        <label className="tools-export-check"><input type="checkbox" checked={resampleEnabled} onChange={event => updateForm(() => setResampleEnabled(event.target.checked))} disabled={phase !== null} /><span>{text(language, '重采样地形像元', 'Resample terrain cell size')}</span></label>
        {resampleEnabled && <label className="tools-field tools-export-cellsize"><span>{text(language, '重采样像元大小', 'Resample cell size')}</span><input inputMode="decimal" value={resampleCellsize} onChange={event => updateForm(() => setResampleCellsize(event.target.value))} disabled={phase !== null} /></label>}
        <label className="tools-field">
          <span>{text(language, '外部连接', 'External connections')}</span>
          <select value={externalConnections} onChange={event => updateForm(() => setExternalConnections(event.target.value as PlanExportForm['externalConnections']))} disabled={phase !== null}>
            <option value="ReferenceByAbsolutePath">ReferenceByAbsolutePath</option>
            <option value="CopyToProject">CopyToProject</option>
          </select>
          <small>{externalConnections === 'ReferenceByAbsolutePath'
            ? text(language, '保留绝对路径引用。', 'Keep absolute path references.')
            : text(language, '将外部连接复制到导出工程。', 'Copy external connections into the exported project.')}</small>
        </label>
        {zip && <label className="tools-export-check"><input type="checkbox" checked={replaceFile} onChange={event => updateForm(() => setReplaceFile(event.target.checked))} disabled={phase !== null} /><span>{text(language, '允许覆盖已有 ZIP 文件', 'Allow replacing an existing ZIP file')}</span></label>}
      </div>

      <div className="tools-actions">
        <button type="button" className="tools-button tools-button-secondary" onClick={() => void inspect()} disabled={!canInspect}>
          {phase === 'inspect' ? text(language, '正在检查…', 'Inspecting…') : text(language, '检查导出设置', 'Inspect export settings')}
        </button>
        <button type="button" className="tools-button tools-button-primary" onClick={() => void execute()} disabled={!canExecute}>
          {phase === 'execute' ? text(language, '正在导出…', 'Exporting…') : text(language, '执行导出', 'Export')}
        </button>
        {phase === 'execute' && <button type="button" className="tools-button tools-button-secondary" onClick={cancel} disabled={cancelRequested}>{cancelRequested ? text(language, '正在请求取消', 'Requesting cancellation') : text(language, '取消', 'Cancel')}</button>}
      </div>

      <p className="tools-export-callout">{text(language, '原始 C# 对话框没有预览步骤；此流程使用 Host 检查修订后，再由用户点击执行。', 'The source C# dialog has no preview step. The Host inspection captures a revision before the user starts export.')}</p>
      <p className="tools-export-callout">{text(language, '原 C# 对话框还提供开发模型库上传；该 Host 操作需要凭据，本页面尚无安全的凭据接入，因此当前不显示上传执行项。', 'The source C# dialog also supports model repository upload. That Host operation requires credentials, and this page has no secure credential integration yet.')}</p>

      {inspection && <div className={`tools-export-inspection ${inspection.value.canExecute ? 'is-valid' : 'is-invalid'}`}>
        <div className="tools-export-status"><strong>{inspection.value.canExecute ? text(language, '检查通过', 'Inspection passed') : text(language, '需要处理检查结果', 'Inspection needs attention')}</strong>
          <span>{text(language, '源修订', 'Source revision')}: {inspection.value.revision || '—'}</span></div>
        {inspection.value.validationMessage && <pre className="tools-export-diagnostic">{inspection.value.validationMessage}</pre>}
        {inspection.value.requiredConnections.length > 0 && <details><summary>{text(language, '必需外部连接', 'Required external connections')} ({inspection.value.requiredConnections.length})</summary><ul>{inspection.value.requiredConnections.map((name, index) => <li key={`${name}-${index}`}>{name}</li>)}</ul></details>}
        {inspection.value.sourceFiles.length > 0 && <details><summary>{text(language, '检查到的源文件', 'Inspected source files')} ({inspection.value.sourceFiles.length})</summary><ul>{inspection.value.sourceFiles.map((path, index) => <li key={`${path}-${index}`}>{path}</li>)}</ul></details>}
      </div>}

      {phase === 'execute' && progress && <div className="tools-export-progress" aria-live="polite">
        <div className="tools-export-progress-label">{progress.label}</div>
        {progress.fraction !== undefined && <progress max={1} value={progress.fraction} />}
      </div>}
      {validationMessage && (!inspection || validationMessage !== inspection.value.validationMessage) && <p className="tools-export-callout" role="status">{validationMessage}</p>}
      {cancelRequested && phase === null && !validationMessage && <p className="tools-export-callout" role="status">{text(language, '取消结果尚未确认；请以服务端操作状态为准。', 'The cancellation outcome is unconfirmed; check the server operation status.')}</p>}

      {receipt && <div className="tools-export-receipt" role="status">
        <div className="tools-export-status"><strong>{text(language, '导出完成', 'Export completed')}</strong><span>{receipt.fileCount} {text(language, '个文件', 'files')}</span></div>
        <dl className="tools-export-receipt-grid">
          <div><dt>{text(language, '输出', 'Output')}</dt><dd>{receipt.outputPath}</dd></div>
          <div><dt>{text(language, '工程文件', 'Project file')}</dt><dd>{receipt.projectFilename}</dd></div>
          <div><dt>{text(language, '导出格式', 'Export format')}</dt><dd>{receipt.zip ? 'ZIP' : text(language, '目录', 'Folder')}</dd></div>
          <div><dt>{text(language, '大小', 'Size')}</dt><dd>{receipt.zip ? `${receipt.outputBytes} bytes` : text(language, 'Host 未计算目录总大小', 'Directory byte total was not reported by the Host')}</dd></div>
          <div><dt>{text(language, '工程路径', 'Project path')}</dt><dd>{receipt.projectPath}</dd></div>
          <div><dt>{text(language, '计划路径', 'Plan path')}</dt><dd>{receipt.planPath}</dd></div>
          <div><dt>{text(language, '工程会话', 'Project session')}</dt><dd>{receipt.projectSessionID}</dd></div>
          <div><dt>{text(language, '输出 SHA-256', 'Output SHA-256')}</dt><dd>{receipt.outputSHA256 ?? '—'}</dd></div>
          <div><dt>{text(language, '源修订', 'Source revision')}</dt><dd>{receipt.sourceRevision}</dd></div>
          <div><dt>{text(language, '核心图层身份', 'Core layer identity')}</dt><dd>{receipt.coreLayerIdentityToken ?? '—'}</dd></div>
          <div><dt>{text(language, '回执 ID', 'Receipt ID')}</dt><dd>{receipt.receiptID}</dd></div>
        </dl>
        <details><summary>{text(language, '完整 Host 回执', 'Full Host receipt')}</summary><pre className="tools-export-diagnostic">{formatDiagnostic(receipt)}</pre></details>
      </div>}

      {error != null && <div className="tools-export-error" role="alert">
        <p>{message(error, language)}</p>
        {diagnostics !== undefined && <details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{formatDiagnostic(diagnostics)}</pre></details>}
      </div>}
    </section>
  );
}

function formatDiagnostic(value: unknown): string {
  try { return JSON.stringify(value, null, 2) ?? String(value); }
  catch { return String(value); }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isAbortError(value: unknown): boolean {
  return isRecord(value) && value.name === 'AbortError';
}
