import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { ReprojectionToolControl } from './ReprojectionToolControl';
import './geoprocessingUi.css';
import './exportDialogs.css';

interface Props {
  open: boolean;
  language: Language;
  context: WorkbenchContext;
  backend: BackendGateway;
  onClose: () => void;
  onDirtyStateChange?: (owner: string, dirty: boolean) => void;
}

export function ReprojectionToolDialog({ open, language, context, backend, onClose, onDirtyStateChange }: Props) {
  if (!open) return null;
  return <div className="tools-task-dialog-backdrop">
    <section className="tools-task-dialog tools-reprojection-dialog" role="dialog" aria-modal="true" aria-labelledby="gp-ui-reprojection-title">
      <ReprojectionToolControl language={language} context={context} backend={backend} onClose={onClose}
        onDirtyStateChange={onDirtyStateChange} presentation="dialog" />
    </section>
  </div>;
}
