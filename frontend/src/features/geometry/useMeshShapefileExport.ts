import { useRef, useState, useSyncExternalStore } from 'react';
import type { BackendGateway, FeaturePageProps } from '../../shared/workbench';
import { MeshShapefileExportController } from './MeshShapefileExportController';
import { meshExportSourceKey, type MeshExportForm, type MeshExportSource } from './meshShapefileExport';

export function useMeshShapefileExport(source: MeshExportSource, backend: BackendGateway, busy: boolean,
  onDirtyStateChange: FeaturePageProps['onDirtyStateChange']) {
  const latest = useRef({ source, backend, busy, onDirtyStateChange });
  latest.current = { source, backend, busy, onDirtyStateChange };
  const ownerCallbacks = useRef(new Map<string, NonNullable<FeaturePageProps['onDirtyStateChange']>>());
  const [controller] = useState(() => new MeshShapefileExportController((owner, dirty) => {
    if (dirty) {
      const callback = latest.current.onDirtyStateChange;
      if (!callback) throw new Error('当前界面缺少工作区写入保护，不能发起导出。');
      ownerCallbacks.current.set(owner, callback);
      callback(owner, true);
    } else {
      ownerCallbacks.current.get(owner)?.(owner, false);
      ownerCallbacks.current.delete(owner);
    }
  }));
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot);
  const start = (form: MeshExportForm): Promise<void> => {
    if (latest.current.busy || !latest.current.onDirtyStateChange) return Promise.resolve();
    const capturedSource = latest.current.source;
    const capturedBackend = latest.current.backend;
    const sourceKey = meshExportSourceKey(capturedSource);
    return controller.start(capturedSource, form, capturedBackend, () => !latest.current.busy &&
      latest.current.backend === capturedBackend && meshExportSourceKey(latest.current.source) === sourceKey);
  };
  // No unmount/context effect cancels or clears an operation that may be publishing files.
  return { state, pending: controller.pending, start, cancel: controller.cancel, resume: controller.resume,
    canResume: controller.canResume(), writeProtected: typeof onDirtyStateChange === 'function', sourceKey: meshExportSourceKey(source) };
}
export type MeshShapefileExportWorkspace = ReturnType<typeof useMeshShapefileExport>;
