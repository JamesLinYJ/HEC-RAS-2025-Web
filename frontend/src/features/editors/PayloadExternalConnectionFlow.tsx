import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import type { CatalogTreeSnapshot } from '../project/CatalogTreeSnapshot';
import type { ExternalConnectionValues } from '../project/ExternalConnectionWorkflow';
import { NewExternalConnectionPage } from '../project/NewExternalConnectionPage';
import { NewFileExternalConnectionPage } from '../project/NewFileExternalConnectionPage';
import { NewResultExternalConnectionPage } from '../project/NewResultExternalConnectionPage';
import type { BoundaryExternalConnectionWorkflow } from './model';

type BoundaryLine = {
  id: string;
};

export interface PayloadExternalConnectionFlowProps {
  line: BoundaryLine;
  language: Language;
  workflow?: BoundaryExternalConnectionWorkflow;
  onCreated: (connectionName: string, lineID: string) => void;
  onCancel: () => void;
  onCreatingChange?: (creating: boolean) => void;
}

type WizardPage = 'choose' | 'file' | 'result';

function describeError(error: unknown, language: Language): string {
  const message = error instanceof Error ? error.message : String(error);
  return language === 'zh' ? `连接流程未完成：${message}` : `Connection workflow incomplete: ${message}`;
}

/** Hosts the original project connection pages inside the payload dialog. */
export function PayloadExternalConnectionFlow({
  line,
  language,
  workflow,
  onCreated,
  onCancel,
  onCreatingChange,
}: PayloadExternalConnectionFlowProps) {
  const [page, setPage] = useState<WizardPage>('choose');
  const [catalog, setCatalog] = useState<CatalogTreeSnapshot | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const catalogRequestRef = useRef(0);
  const currentLineIDRef = useRef(line.id);
  currentLineIDRef.current = line.id;

  useEffect(() => () => { catalogRequestRef.current += 1; }, []);

  const returnToChooser = (): void => {
    catalogRequestRef.current += 1;
    setCatalogLoading(false);
    setCatalog(null);
    setError('');
    setPage('choose');
  };

  const chooseSource = (kind: 'file' | 'result'): void => {
    setError('');
    if (kind === 'file') {
      catalogRequestRef.current += 1;
      setCatalog(null);
      setPage('file');
      return;
    }

    setPage('result');
    setCatalog(null);
    if (!workflow?.canCreate) {
      setError(workflow?.blockedMessage ?? (language === 'zh' ? '当前工程会话未提供连接创建事务。' : 'The current project session cannot create connections.'));
      return;
    }
    const requestID = ++catalogRequestRef.current;
    setCatalogLoading(true);
    void workflow.loadCatalog().then((snapshot) => {
      if (catalogRequestRef.current === requestID) setCatalog(snapshot);
    }).catch((cause: unknown) => {
      if (catalogRequestRef.current === requestID) setError(describeError(cause, language));
    }).finally(() => {
      if (catalogRequestRef.current === requestID) setCatalogLoading(false);
    });
  };

  const createConnection = async (values: ExternalConnectionValues): Promise<void> => {
    if (!workflow?.canCreate || creating) return;
    setError('');
    setCreating(true);
    onCreatingChange?.(true);
    const requestedLineID = line.id;
    try {
      const connectionName = await workflow.create(requestedLineID, values);
      if (typeof connectionName !== 'string' || connectionName.trim().length === 0) {
        throw new Error(language === 'zh' ? '工程服务未返回新连接名称。' : 'The project service did not return the new connection name.');
      }
      onCreated(connectionName, requestedLineID);
    } catch (cause) {
      setError(describeError(cause, language));
    } finally {
      setCreating(false);
      onCreatingChange?.(false);
    }
  };

  const busy = creating || catalogLoading;
  if (!workflow?.canCreate) {
    return <section className="project-external-wizard" data-testid="payload-external-connection-flow" aria-label={language === 'zh' ? '新建外部连接' : 'Create external connection'}>
      <h3>{language === 'zh' ? '新建外部连接' : 'Create external connection'}</h3>
      <p className="project-tree-state" role="status">{workflow?.blockedMessage ?? (language === 'zh' ? '当前工程会话未提供安全的连接创建事务。' : 'The current project session does not provide a safe connection-creation workflow.')}</p>
      <div className="project-footer-actions"><button type="button" className="project-button project-button-secondary" onClick={onCancel}>{language === 'zh' ? '返回载荷' : 'Back to payload'}</button></div>
    </section>;
  }

  return <div data-testid="payload-external-connection-flow">
    {page === 'choose' && <NewExternalConnectionPage language={language} onChoose={chooseSource} onCancel={onCancel} />}
    {page === 'file' && <NewFileExternalConnectionPage language={language} action="add" kind="file"
      busy={creating} onSubmit={(values) => { void createConnection(values); }} onBack={returnToChooser} />}
    {page === 'result' && (catalogLoading
      ? <section className="project-external-wizard" aria-live="polite"><h3>{language === 'zh' ? '读取工程结果' : 'Loading project results'}</h3><p className="project-tree-state">{language === 'zh' ? '正在读取当前工程目录中的真实结果…' : 'Reading actual results from the active project catalog…'}</p><button type="button" className="project-button project-button-secondary" onClick={returnToChooser}>{language === 'zh' ? '返回' : 'Back'}</button></section>
      : catalog
        ? <NewResultExternalConnectionPage language={language} snapshot={catalog} busy={creating}
          onSubmit={(values) => { void createConnection(values); }} onBack={returnToChooser} />
        : <section className="project-external-wizard" aria-labelledby="payload-external-connection-load-error">
          <h3 id="payload-external-connection-load-error">{language === 'zh' ? '无法读取工程结果' : 'Could not read project results'}</h3>
          {error && <p className="project-form-error" role="alert">{error}</p>}
          <div className="project-footer-actions"><button type="button" className="project-button project-button-secondary" onClick={returnToChooser}>{language === 'zh' ? '返回' : 'Back'}</button></div>
        </section>)}
    {page !== 'choose' && error && !(page === 'result' && !catalog && !catalogLoading) && <p className="project-form-error" role="alert" data-testid="payload-external-connection-error">{error}</p>}
    {creating && <p className="project-tree-state" role="status" data-testid="payload-external-connection-creating">{language === 'zh' ? '正在检查并提交工程连接…' : 'Inspecting and committing the project connection…'}</p>}
    {busy && <span className="sr-only" aria-live="polite">{language === 'zh' ? '连接流程处理中' : 'Connection workflow busy'}</span>}
  </div>;
}
