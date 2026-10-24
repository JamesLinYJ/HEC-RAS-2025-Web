import { useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import { text } from '../../../shared/workbench';
import { RgbTiffExporterControl } from './RgbTiffExporterControl';

interface Props {
  open: boolean;
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  onClose: () => void;
}

export function RgbTiffExporterDialog({ open, backend, context, language, onClose }: Props) {
  const [busy, setBusy] = useState(false);
  if (!open) return null;
  return <div className="tools-export-dialog-backdrop">
    <section className="tools-export-dialog" role="dialog" aria-modal="true" aria-label={text(language, 'RGB GeoTIFF 导出', 'RGB GeoTIFF export')}>
      <div className="tools-export-dialog-bar">
        <strong>{text(language, 'RGB GeoTIFF 导出', 'RGB GeoTIFF export')}</strong>
        <button className="tools-button tools-button-secondary" type="button" onClick={onClose} disabled={busy}>
          {text(language, '关闭', 'Close')}
        </button>
      </div>
      <RgbTiffExporterControl backend={backend} context={context} language={language} onBusyStateChange={setBusy} />
    </section>
  </div>;
}
