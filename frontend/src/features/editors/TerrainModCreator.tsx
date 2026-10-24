export type TerrainModTool = 'select' | 'draw' | 'stamp';

/** Source TerrainModCreator drawing workflow controls. */
export function TerrainModCreator({
  tool,
  onToolChange,
  canEdit,
  canDraw,
  canStamp,
  pendingPointCount,
  selectedCount,
  canUndo,
  canRedo,
  busy,
  onFinishDraw,
  onCancelDraw,
  onRemove,
  onUndo,
  onRedo,
}: {
  tool: TerrainModTool;
  onToolChange: (tool: TerrainModTool) => void;
  canEdit: boolean;
  canDraw: boolean;
  canStamp: boolean;
  pendingPointCount: number;
  selectedCount: number;
  canUndo: boolean;
  canRedo: boolean;
  busy: boolean;
  onFinishDraw: () => void;
  onCancelDraw: () => void;
  onRemove: () => void;
  onUndo: () => void;
  onRedo: () => void;
}) {
  return <section className="terrain-mod-creator" aria-label="地形修改绘制工具">
    <div className="terrain-mod-tool-row" role="group" aria-label="地图工具">
      <button type="button" data-testid="terrain-mod-tool-select" className={tool === 'select' ? 'editor-button editor-button-selected' : 'editor-button'} disabled={!canEdit || busy} onClick={() => onToolChange('select')}>选择</button>
      <button type="button" data-testid="terrain-mod-tool-draw" className={tool === 'draw' ? 'editor-button editor-button-selected' : 'editor-button'} disabled={!canEdit || busy || !canDraw} onClick={() => onToolChange('draw')}>绘制多边形</button>
      <button type="button" data-testid="terrain-mod-tool-stamp" className={tool === 'stamp' ? 'editor-button editor-button-selected' : 'editor-button'} disabled={!canEdit || busy || !canStamp} onClick={() => onToolChange('stamp')}>放置形状</button>
    </div>
    <div className="terrain-mod-tool-row">
      {tool === 'draw' && <>
        <span className="editor-field-muted">已采集 {pendingPointCount} 个地图坐标点</span>
        <button type="button" data-testid="terrain-mod-draw-finish" className="editor-button editor-button-primary" disabled={!canEdit || busy || pendingPointCount < 3} onClick={onFinishDraw}>完成绘制</button>
        <button type="button" data-testid="terrain-mod-draw-cancel" className="editor-button" disabled={busy || pendingPointCount === 0} onClick={onCancelDraw}>取消绘制</button>
      </>}
      <button type="button" data-testid="terrain-mod-remove" className="editor-button" disabled={!canEdit || busy || selectedCount === 0} onClick={onRemove}>删除所选修改</button>
      <button type="button" data-testid="terrain-mod-undo" className="editor-button" disabled={!canEdit || busy || !canUndo} onClick={onUndo}>撤销</button>
      <button type="button" data-testid="terrain-mod-redo" className="editor-button" disabled={!canEdit || busy || !canRedo} onClick={onRedo}>重做</button>
    </div>
  </section>;
}
