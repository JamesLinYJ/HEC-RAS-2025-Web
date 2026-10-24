export interface RasMapUndoRedoProps {
  canUndo: boolean;
  canRedo: boolean;
  language: 'zh' | 'en';
  onUndo: () => void;
  onRedo: () => void;
}

export function RasMapUndoRedo({ canUndo, canRedo, language, onUndo, onRedo }: RasMapUndoRedoProps) {
  const labels = language === 'zh'
    ? { group: '绘制草稿历史', undo: '撤销', redo: '重做' }
    : { group: 'Drawing draft history', undo: 'Undo', redo: 'Redo' };

  return (
    <div className="map-undo-redo" role="group" aria-label={labels.group}>
      <button type="button" onClick={onUndo} disabled={!canUndo} aria-label={labels.undo} title={`${labels.undo} (Ctrl/Cmd+Z)`}>
        <span aria-hidden="true">↶</span>{labels.undo}
      </button>
      <button type="button" onClick={onRedo} disabled={!canRedo} aria-label={labels.redo} title={`${labels.redo} (Ctrl/Cmd+Shift+Z)`}>
        <span aria-hidden="true">↷</span>{labels.redo}
      </button>
    </div>
  );
}
