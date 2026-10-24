export interface TopologyDeepConnectControlsProps {
  language: 'zh' | 'en';
  canApply: boolean;
  busy: boolean;
  sourceFID: number | null;
  anchorCount: 0 | 1;
  onCancel: () => void;
}

export function TopologyDeepConnectControls({
  language,
  canApply,
  busy,
  sourceFID,
  anchorCount,
  onCancel,
}: TopologyDeepConnectControlsProps) {
  const text = language === 'zh';
  const hasSource = Number.isSafeInteger(sourceFID) && (sourceFID ?? -1) >= 0;
  const canCancel = canApply && !busy;

  let status: string;
  if (busy) {
    status = text ? '正在验证源弧段并提交到当前几何草稿…' : 'Verifying the source arc and adding it to the current geometry draft…';
  } else if (!canApply) {
    status = text ? '当前地图或几何草稿捕获不可用，请等待其加载完成。' : 'The current map or geometry draft capture is unavailable; wait for it to load.';
  } else if (!hasSource || anchorCount === 0) {
    status = text ? '请在当前地图上的拓扑弧段点选第一个锚点。' : 'Click the first anchor on a topology arc in the current map.';
  } else {
    status = text ? '起点已捕获，请在同一源弧段上点击终点。' : 'Start anchor captured; click the same source arc to capture the end.';
  }

  return (
    <section aria-label={text ? '沿源弧段连接' : 'Connect along source arc'}>
      <h3>{text ? '沿源弧段连接' : 'Connect along source arc'}</h3>
      <p role="status" aria-live="polite">{status}</p>
      {hasSource && <p>{text ? `源弧段 FID：${sourceFID}` : `Source arc FID: ${sourceFID}`}</p>}
      <p>
        {text
          ? '该操作会把新弧段写入当前 Geometry 草稿；保存父级 Geometry 后才会写入源文件。'
          : 'The new arc is added to the current Geometry draft and reaches the source file when the parent Geometry is saved.'}
      </p>
      <p>
        {text
          ? '此入口只接受当前 Geometry 拓扑弧段。已保存的矢量图层来源尚未接入。'
          : 'This entry accepts current Geometry topology arcs only. Persisted vector-layer sources are not connected here.'}
      </p>
      <button type="button" disabled={!canCancel} onClick={onCancel}>
        {text ? (anchorCount > 0 ? '清除锚点并取消' : '取消连接') : (anchorCount > 0 ? 'Clear anchors and cancel' : 'Cancel connect')}
      </button>
    </section>
  );
}

export default TopologyDeepConnectControls;
