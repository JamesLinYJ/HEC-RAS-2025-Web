import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import type { PlanComputeExecutionResult } from './computeTypes';

interface ProjectOpenResponse {
  success?: boolean;
  error?: string | null;
  projectPath?: string;
  projectXml?: string | null;
  revision?: string;
}

interface ViewComputeResultsControlProps {
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  result?: PlanComputeExecutionResult;
  hasUnsavedDrafts: boolean;
  onContextChange: (context: WorkbenchContext) => void;
  onNavigate: (feature: 'results') => void;
}

const labels = {
  zh: {
    view: '查看计算结果', opening: '正在刷新工程并打开结果…',
    unavailable: '服务端未提供 project.open，无法安全刷新工程。',
    dirty: '存在未保存草稿，请先保存或放弃草稿。',
    error: '无法打开计算结果。', invalidResponse: '服务端没有返回有效的工程 XML、工程修订或结果身份。',
    stale: '工程或计算结果在打开期间已改变，请重新计算或再次尝试。', diagnostics: '服务端诊断信息',
  },
  en: {
    view: 'View compute results', opening: 'Refreshing the project and opening results…',
    unavailable: 'The server does not provide project.open, so the project cannot be refreshed safely.',
    dirty: 'Unsaved drafts exist. Save or discard them first.',
    error: 'Could not open compute results.', invalidResponse: 'The server did not return valid project XML, project revision, or result identity.',
    stale: 'The project or compute result changed while opening. Recompute or try again.', diagnostics: 'Server diagnostics',
  },
} satisfies Record<Language, Record<string, string>>;

function contextKey(context: WorkbenchContext): string {
  return JSON.stringify([
    context.projectSessionID,
    context.projectPath,
    context.projectXml,
    context.projectRevision,
    context.selectedAsset?.path ?? null,
    context.selectedAsset?.kind ?? null,
    context.selectedAsset?.revision ?? null,
  ]);
}

function resultKey(result: PlanComputeExecutionResult | undefined): string {
  return JSON.stringify([
    result?.state ?? null,
    result?.inspectionId ?? null,
    result?.revision ?? null,
    result?.resultPath ?? null,
    result?.resultRevision ?? null,
  ]);
}

function resultFileName(path: string): string {
  return path.replace(/[\\/]+$/, '').split(/[\\/]/).at(-1) ?? path;
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Opens the server-confirmed output using the refreshed source project snapshot. */
export function ViewComputeResultsControl({
  language,
  context,
  backend,
  result,
  hasUnsavedDrafts,
  onContextChange,
  onNavigate,
}: ViewComputeResultsControlProps) {
  const t = labels[language];
  const sourceContextKey = contextKey(context);
  const currentContext = useRef(context);
  const currentResultKey = useRef(resultKey(result));
  const dirtyRef = useRef(hasUnsavedDrafts);
  currentContext.current = context;
  currentResultKey.current = resultKey(result);
  dirtyRef.current = hasUnsavedDrafts;

  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string>();
  const [errorDetail, setErrorDetail] = useState<string>();
  const generation = useRef(0);
  const activeController = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    generation.current++;
    activeController.current?.abort();
    activeController.current = undefined;
    setOpening(false);
    setError(undefined);
    setErrorDetail(undefined);
    return () => {
      generation.current++;
      activeController.current?.abort();
    };
  }, [sourceContextKey]);

  const resultPath = typeof result?.resultPath === 'string' ? result.resultPath.trim() : '';
  const resultRevision = typeof result?.resultRevision === 'string' ? result.resultRevision.trim() : '';
  const hasConfirmedOutput = result?.state === 'completed'
    && resultPath.length > 0
    && /^[0-9a-f]{64}$/i.test(resultRevision);
  const supportsProjectOpen = backend.supports('project.open');

  if (!hasConfirmedOutput) return null;

  const openResults = async (): Promise<void> => {
    if (opening || dirtyRef.current || !supportsProjectOpen || !context.projectPath) return;
    const source = currentContext.current;
    const sourceKey = contextKey(source);
    const capturedResultKey = resultKey(result);
    const requestedResultPath = resultPath;
    const requestedResultRevision = resultRevision;
    const requestGeneration = ++generation.current;
    const controller = new AbortController();
    activeController.current?.abort();
    activeController.current = controller;
    setOpening(true);
    setError(undefined);
    setErrorDetail(undefined);

    const isCurrent = (): boolean => requestGeneration === generation.current
      && !controller.signal.aborted
      && contextKey(currentContext.current) === sourceKey
      && currentResultKey.current === capturedResultKey
      && !dirtyRef.current;

    try {
      const response = await backend.request<ProjectOpenResponse>('project.open', {
        projectPath: source.projectPath,
      }, { signal: controller.signal });
      if (!isCurrent()) return;
      if (response?.success !== true
        || typeof response.projectPath !== 'string'
        || response.projectPath.trim().length === 0
        || response.projectPath !== source.projectPath
        || typeof response.projectXml !== 'string'
        || response.projectXml.length === 0
        || typeof response.revision !== 'string'
        || !/^[0-9a-f]{64}$/i.test(response.revision)
        || !/^[0-9a-f]{64}$/i.test(requestedResultRevision)) {
        throw new Error(response?.error || t.invalidResponse);
      }
      if (!isCurrent()) return;

      const selectedAsset = {
        kind: 'Result',
        path: requestedResultPath,
        name: resultFileName(requestedResultPath),
        revision: requestedResultRevision,
      };
      setOpening(false);
      onContextChange({
        projectSessionID: source.projectSessionID,
        projectPath: response.projectPath,
        projectXml: response.projectXml,
        projectRevision: response.revision,
        selectedAsset,
      });
      onNavigate('results');
    } catch (caught) {
      if (!isCurrent()) return;
      const diagnostic = errorText(caught);
      const unavailable = diagnostic.includes('project.open');
      setError(unavailable ? t.unavailable : diagnostic === t.invalidResponse ? t.invalidResponse : t.error);
      if (diagnostic !== t.invalidResponse) setErrorDetail(diagnostic);
    } finally {
      if (requestGeneration === generation.current && activeController.current === controller) {
        activeController.current = undefined;
        setOpening(false);
      }
    }
  };

  const disabled = opening || hasUnsavedDrafts || !supportsProjectOpen || !context.projectPath;

  return <div className="compute-view-results-control">
    <button type="button" className="text-button" data-compute-action="open-result" onClick={() => void openResults()} disabled={disabled}>
      {opening ? t.opening : t.view}
    </button>
    {hasUnsavedDrafts && <small role="status">{t.dirty}</small>}
    {!supportsProjectOpen && <small role="status">{t.unavailable}</small>}
    {error && <div className="compute-field-error" role="alert">{error}</div>}
    {errorDetail && <details className="compute-error-details"><summary>{t.diagnostics}</summary><pre className="code-block error-block">{errorDetail}</pre></details>}
  </div>;
}
