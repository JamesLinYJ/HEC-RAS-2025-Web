import { useEffect, useRef, useState } from 'react';
import type { EditorPageProps } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';
import { GriddedDataImporter } from './GriddedDataImporter';

/** Modal lifetime/cleanup host corresponding to GriddedDataImporterDialog. */
export function GriddedDataImporterDialog(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const label = props.language === 'zh' ? '格网导入对话框' : 'Grid import dialog';

  useEffect(() => {
    const dialog = ref.current;
    if (dialog && !dialog.open) dialog.showModal();
    return () => { if (dialog?.open) dialog.close(); };
  }, []);

  const requestClose = (afterAcceptedImport = false) => {
    if (busy && !afterAcceptedImport) return;
    props.onClose();
  };

  return <dialog
    ref={ref}
    aria-label={label}
    aria-modal="true"
    className="editor-grid-import-dialog"
    style={{ width: 'min(1100px, calc(100vw - 32px))', maxHeight: 'min(92vh, 1000px)', overflow: 'auto', padding: 0, border: '1px solid #dfe5e9', borderRadius: 10 }}
    onCancel={(event) => { event.preventDefault(); requestClose(); }}
    onClick={(event) => { if (event.target === ref.current) requestClose(); }}
  >
    <GriddedDataImporter {...props} onClose={() => requestClose(true)} onBusyChange={setBusy} />
  </dialog>;
}
