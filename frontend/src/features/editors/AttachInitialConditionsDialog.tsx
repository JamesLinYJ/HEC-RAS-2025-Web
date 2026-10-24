import { useEffect, useRef, useState } from 'react';
import type { BoundaryInitialConditionsInspection, BoundaryResultCatalogItem, EditorPageProps } from './model';

/** React counterpart to Ras.Visual.WinUI.Controls.AttachInitialConditionsDialog. */
export function AttachInitialConditionsDialog({ language, boundaryInitialConditions: workflow }: Pick<EditorPageProps, 'language' | 'boundaryInitialConditions'>) {
  const zh = language === 'zh';
  const workflowRef = useRef(workflow);
  workflowRef.current = workflow;
  const [open, setOpen] = useState(false);
  const [results, setResults] = useState<BoundaryResultCatalogItem[]>([]);
  const [selectedPath, setSelectedPath] = useState('');
  const [timestamp, setTimestamp] = useState('');
  const [inspection, setInspection] = useState<BoundaryInitialConditionsInspection | null>(null);
  const [loadingResults, setLoadingResults] = useState(false);
  const [inspecting, setInspecting] = useState(false);
  const [error, setError] = useState('');
  const [details, setDetails] = useState('');

  useEffect(() => {
    const activeWorkflow = workflowRef.current;
    if (!open || !activeWorkflow) return;
    const controller = new AbortController();
    setLoadingResults(true);
    setError('');
    setDetails('');
    void activeWorkflow.loadResults(controller.signal).then((items) => {
      if (controller.signal.aborted) return;
      setResults(items);
      setSelectedPath((current) => items.some((item) => item.path === current) ? current : '');
    }).catch((cause: unknown) => {
      if (!controller.signal.aborted) {
        setError(zh ? '无法读取当前工程的结果目录。' : 'Could not read the active project result catalog.');
        setDetails(errorMessage(cause));
      }
    }).finally(() => {
      if (!controller.signal.aborted) setLoadingResults(false);
    });
    return () => controller.abort();
  }, [open, zh]);

  const close = (): void => {
    setOpen(false);
    setInspection(null);
    setError('');
    setDetails('');
  };

  const inspect = async (): Promise<void> => {
    if (!workflow?.canStart || !selectedPath || inspecting) return;
    setInspecting(true);
    setInspection(null);
    setError('');
    setDetails('');
    const controller = new AbortController();
    try {
      const result = await workflow.inspect(selectedPath, timestamp, controller.signal);
      setInspection(result);
      if (!result.canAttach) {
        setError(zh ? '所选结果暂不能作为初始条件。' : 'The selected result cannot be attached as an initial condition.');
        setDetails(result.errorMessage ?? '');
      }
    } catch (cause) {
      setError(zh ? '结果检查失败。' : 'Result inspection failed.');
      setDetails(errorMessage(cause));
    } finally {
      setInspecting(false);
    }
  };

  const attach = async (): Promise<void> => {
    if (!workflow?.canStart || !inspection?.canAttach) return;
    setError('');
    setDetails('');
    try {
      const accepted = await workflow.attach(inspection);
      if (accepted) close();
    } catch (cause) {
      setError(zh ? '初始条件事务尚未确认。' : 'The initial-condition transaction is not yet confirmed.');
      setDetails(errorMessage(cause));
    }
  };

  const operation = workflow?.operation;
  const recover = async (): Promise<void> => {
    if (!workflow || workflow.busy) return;
    if (operation?.canRetry) await workflow.retry();
    else await workflow.recover();
  };

  return <section className="editor-table-card" aria-label={zh ? '附加初始条件' : 'Attach initial conditions'}>
    <div className="editor-terrain-table-heading">
      <h3>{zh ? '初始条件' : 'Initial conditions'}</h3>
      <button type="button" className="editor-button" disabled={!workflow?.canStart || workflow.busy || workflow.blockedByOtherOwner}
        data-testid="initial-conditions-open" onClick={() => { setOpen(true); setInspection(null); setTimestamp(''); }}>
        {zh ? '从工程结果附加' : 'Attach from project result'}
      </button>
    </div>
    <p className="editor-field-muted">{zh
      ? '从当前工程目录选择真实 RAS 结果。附加操作会立即更新边界条件文件与工程连接；只有服务端回执被当前工程会话接纳后才会完成。'
      : 'Choose a real RAS result from the active project catalog. Attaching updates the boundary file and project connection immediately; completion waits for the active project session to accept the server receipt.'}</p>
    {!workflow && <p className="editor-field-muted">{zh ? '当前工程会话未提供所需的结果目录、事务或回执接纳能力。' : 'The active project session does not provide the required result catalog, transaction, or receipt-acceptance capability.'}</p>}
    {workflow && !workflow.canStart && !operation && <p className="editor-field-muted">{zh
      ? '请先保存或丢弃草稿，并修正所有字段问题；此事务只允许在干净、可编辑的工程快照上执行。'
      : 'Save or discard the draft and correct validation issues first. This transaction requires a clean, editable project snapshot.'}</p>}
    {workflow?.blockedByOtherOwner && <p className="editor-inline-error" role="status">{workflow.blockedMessage}</p>}
    {operation && <div className={operation.state === 'conflict' ? 'editor-inline-error' : 'editor-save-notice'} role="status" data-testid="initial-conditions-operation" data-state={operation.state}>
      <strong>{operation.state === 'awaitingContext'
        ? (zh ? '工程会话尚未接纳提交回执' : 'The project session has not accepted the commit receipt')
        : operation.state === 'conflict' ? (zh ? '初始条件事务冲突' : 'Initial-condition transaction conflict')
          : (zh ? '待恢复的初始条件事务' : 'Initial-condition transaction needs recovery')}</strong>
      <p>{operation.message}</p>
      <p><span>{zh ? '操作编号' : 'Operation ID'}：</span><code>{operation.operationID}</code></p>
      <p><span>{zh ? '结果修订' : 'Result revision'}：</span><code>{operation.resultRevision}</code></p>
      {operation.state !== 'conflict' && <button type="button" className="editor-button" disabled={workflow.busy}
        data-testid={operation.canRetry ? 'initial-conditions-retry' : 'initial-conditions-recover'}
        onClick={() => { void recover(); }}>{operation.canRetry
          ? (zh ? '按原请求重试' : 'Retry the exact request')
          : operation.state === 'awaitingContext' ? (zh ? '重试接纳回执' : 'Retry receipt acceptance')
            : (zh ? '查询事务状态' : 'Check transaction status')}</button>}
    </div>}

    {open && <div className="editor-confirm-backdrop" role="presentation">
      <section className="editor-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="initial-conditions-title">
        <p className="editor-eyebrow">{zh ? '边界条件' : 'Boundary conditions'}</p>
        <h2 id="initial-conditions-title">{zh ? '附加初始条件' : 'Attach initial conditions'}</h2>
        <p>{zh ? '选择工程目录中的 RAS 结果。结果时间为可选项；如填写，服务端会按数值区域设置验证。' : 'Choose a RAS result from the project catalog. Result time is optional; the server validates a supplied value using the selected number culture.'}</p>
        <label className="editor-field">
          <span className="editor-field-label">{zh ? `工程结果（${results.length}）` : `Project results (${results.length})`}</span>
          <select value={selectedPath} disabled={loadingResults || inspecting || workflow?.busy || results.length === 0}
            data-testid="initial-conditions-result"
            onChange={(event) => { setSelectedPath(event.target.value); setInspection(null); setError(''); setDetails(''); }}>
            <option value="">{zh ? '选择结果文件' : 'Choose a result file'}</option>
            {results.map((result) => <option key={result.id} value={result.path} title={result.path}>{result.name} · {result.path}</option>)}
          </select>
        </label>
        {selectedPath && <p className="editor-field-muted"><code>{selectedPath}</code></p>}
        <label className="editor-field">
          <span className="editor-field-label">{zh ? '结果时间（可选）' : 'Result time (optional)'}</span>
          <input type="datetime-local" value={timestamp} disabled={inspecting || workflow?.busy}
            data-testid="initial-conditions-timestamp"
            onChange={(event) => { setTimestamp(event.target.value); setInspection(null); setError(''); setDetails(''); }} />
        </label>
        {loadingResults && <p className="editor-field-muted" role="status">{zh ? '正在读取工程结果目录…' : 'Reading the project result catalog…'}</p>}
        {!loadingResults && results.length === 0 && <p className="editor-field-muted">{zh ? '当前工程目录没有可选择的结果对象。' : 'The active project catalog contains no selectable result objects.'}</p>}
        {inspection && <dl className="editor-readonly-list">
          <div><dt>{zh ? '服务端检查' : 'Server inspection'}</dt><dd>{inspection.canAttach ? (zh ? '可附加' : 'Ready to attach') : (zh ? '不可附加' : 'Unavailable')}</dd></div>
          <div><dt>{zh ? '结果 SHA-256' : 'Result SHA-256'}</dt><dd><code>{inspection.resultRevision ?? (zh ? '未提供' : 'Not provided')}</code></dd></div>
          <div><dt>{zh ? '已验证时间' : 'Validated time'}</dt><dd>{inspection.timestamp || (zh ? '未指定' : 'Not specified')}</dd></div>
          <div><dt>{zh ? '数值区域' : 'Number culture'}</dt><dd>{inspection.numberCulture}</dd></div>
        </dl>}
        {error && <p className="editor-inline-error" role="alert">{error}{details && <details><summary>{zh ? '详情' : 'Details'}</summary><pre>{details}</pre></details>}</p>}
        <div className="editor-confirm-actions">
          <button type="button" className="editor-button" disabled={inspecting || workflow?.busy} onClick={close}>{zh ? '取消' : 'Cancel'}</button>
          <button type="button" className="editor-button" disabled={!workflow?.canStart || loadingResults || inspecting || workflow.busy || !selectedPath}
            data-testid="initial-conditions-inspect"
            onClick={() => { void inspect(); }}>{inspecting ? (zh ? '检查中…' : 'Inspecting…') : (zh ? '检查结果' : 'Inspect result')}</button>
          <button type="button" className="editor-button editor-button-primary" disabled={!workflow?.canStart || workflow.busy || !inspection?.canAttach}
            data-testid="initial-conditions-attach"
            onClick={() => { void attach(); }}>{workflow?.busy ? (zh ? '提交中…' : 'Committing…') : (zh ? '附加并提交' : 'Attach and commit')}</button>
        </div>
      </section>
    </div>}
  </section>;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
