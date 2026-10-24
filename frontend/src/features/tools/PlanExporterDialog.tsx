import { useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { PlanExporterControl } from './PlanExporterControl';
import './exportDialogs.css';

interface Props {
  open: boolean;
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  onClose: () => void;
}

export function PlanExporterDialog({ open, backend, context, language, onClose }: Props) {
  const [busy, setBusy] = useState(false);
  return <div className="tools-task-dialog-backdrop" style={{ display: open ? 'grid' : 'none' }} aria-hidden={!open}>
    <section className="tools-task-dialog tools-plan-export-dialog" role="dialog" aria-modal="true" aria-labelledby="tools-plan-export-title">
      <header className="tools-task-dialog-header">
        <div><span className="tools-kicker">PLAN EXPORT</span><h2 id="tools-plan-export-title">{text(language, '导出计算计划', 'Export a plan')}</h2></div>
        <button className="tools-button tools-button-secondary" type="button" onClick={onClose} disabled={busy}>
          {text(language, '关闭', 'Close')}
        </button>
      </header>
      <PlanExporterControl language={language} context={context} backend={backend} onBusyStateChange={setBusy} />
    </section>
  </div>;
}
