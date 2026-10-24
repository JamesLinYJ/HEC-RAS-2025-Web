import { useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { text } from '../../../shared/workbench';
import type { DepthExportReceipt } from './exportWorkflow';
import { DepthShapeExporter } from './DepthShapeExporter';

interface Props {
  open: boolean;
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  onClose: () => void;
  onCompleted?: (receipt: DepthExportReceipt) => void;
}

export function DepthShapeExporterDialog({ open, backend, context, language, onClose, onCompleted }: Props) {
  const [busy, setBusy] = useState(false);
  if (!open) return null;
  return <div className="tools-export-dialog-backdrop">
    <section className="tools-export-dialog" role="dialog" aria-modal="true" aria-label={text(language, '深度 Shapefile 导出', 'Depth Shapefile export')}>
      <div className="tools-export-dialog-bar">
        <strong>{text(language, '深度 Shapefile 导出', 'Depth Shapefile export')}</strong>
        <button className="tools-button tools-button-secondary" type="button" onClick={onClose} disabled={busy}>{text(language, '关闭', 'Close')}</button>
      </div>
      <DepthShapeExporter backend={backend} context={context} language={language} onCompleted={onCompleted} onBusyStateChange={setBusy} />
    </section>
  </div>;
}
