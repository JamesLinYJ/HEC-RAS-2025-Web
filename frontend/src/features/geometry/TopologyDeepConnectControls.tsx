export interface TopologyDeepConnectControlsProps {
  language: 'zh' | 'en';
  canApply: boolean;
  busy: boolean;
  previewAvailable?: boolean;
  previewState?: 'idle' | 'loading' | 'ready' | 'unavailable' | 'failed';
  sourceKind?: 'topology' | 'vector' | null;
  sourceLabel?: string | null;
  sourceFID: number | null;
  anchorCount: number;
  onCancel: () => void;
}

export function TopologyDeepConnectControls({
  language,
  canApply,
  busy,
  previewAvailable = false,
  previewState = 'unavailable',
  sourceKind,
  sourceLabel,
  sourceFID,
  anchorCount,
  onCancel,
}: TopologyDeepConnectControlsProps) {
  const text = language === 'zh';
  const resolvedSourceKind = sourceKind ?? (sourceLabel?.trim() ? 'vector' : 'topology');
  const hasTopologyFID = Number.isSafeInteger(sourceFID) && (sourceFID ?? -1) >= 0;
  const hasSource = resolvedSourceKind === 'vector' || hasTopologyFID;
  const resolvedSourceLabel = sourceLabel?.trim() || null;
  const sourceDescription = resolvedSourceKind === 'vector'
    ? text
      ? `Host 检查的矢量组件${resolvedSourceLabel ? `：${resolvedSourceLabel}` : ''}`
      : `Host-inspected vector component${resolvedSourceLabel ? `: ${resolvedSourceLabel}` : ''}`
    : hasTopologyFID
      ? text ? `源弧段 FID：${sourceFID}` : `Topology arc FID: ${sourceFID}`
      : null;
  const canCancel = canApply && !busy;

  let status: string;
  if (busy) {
    status = text ? '正在验证同一源组件的锚点并提交一条弧段到当前几何草稿…' : 'Verifying anchors on one source component and adding one arc to the current Geometry draft…';
  } else if (!canApply) {
    status = text ? '当前地图或几何草稿捕获不可用，请等待其加载完成。' : 'The current map or geometry draft capture is unavailable; wait for it to load.';
  } else if (!hasSource || anchorCount === 0) {
    status = text ? '请在当前地图上的一个源组件内依次点选锚点；按 Enter 或双击完成一条弧段，按 Escape 取消。' : 'Click anchors in one source component on the current map; press Enter or double-click to finish one arc, or Escape to cancel.';
  } else {
    status = text
      ? `已在${sourceDescription ?? '当前源组件'}捕获 ${anchorCount} 个锚点；继续在同一精确源组件上采集，按 Enter 或双击完成一条弧段。`
      : `${anchorCount} anchor${anchorCount === 1 ? '' : 's'} captured on ${sourceDescription ?? 'the current source component'}; continue on the same exact source component, then press Enter or double-click to finish one arc.`;
  }

  const previewStatus = !previewAvailable || previewState === 'unavailable'
    ? (text ? '当前服务未提供与此来源和草稿匹配的路径预览；可继续采集锚点，完成时由服务验证。' : 'The service has no path preview for this current source and draft; anchors can still be collected and validated on completion.')
    : previewState === 'loading'
      ? (text ? '正在计算只读路径预览；尚未写入拓扑草稿。' : 'Computing a read-only path preview; nothing has been written to the topology draft.')
      : previewState === 'ready'
        ? (text ? '地图上显示当前锚点对应的只读路径预览；完成后才写入草稿。' : 'The map shows a read-only preview for the current anchors; it enters the draft only when completed.')
        : previewState === 'failed'
          ? (text ? '当前路径预览失败；未提交几何不会写入草稿。' : 'The current path preview failed; unsubmitted geometry is not written to the draft.')
          : (text ? '路径预览能力已就绪；采集锚点后会显示服务返回的只读预览。' : 'Path preview is available; collecting anchors will show the read-only path returned by the service.');

  return (
    <section aria-label={text ? '沿源组件连接' : 'Connect along source component'} data-testid="geometry-topology-deep-connect-controls">
      <h3>{text ? '沿来源组件连接' : 'Connect along source component'}</h3>
      <p role="status" aria-live="polite" data-testid="deep-connect-status">{status}</p>
      <p role="status" aria-live="polite" data-testid="deep-connect-preview-status">{previewStatus}</p>
      {sourceDescription && <p data-testid="deep-connect-source">{sourceDescription}</p>}
      <p>
        {text
          ? '所有锚点必须属于同一个精确源组件。完成时生成一条新弧段并加入当前 Geometry 草稿；保存父级 Geometry 后才写入源文件。'
          : 'All anchors must belong to one exact source component. Completion creates one arc in the current Geometry draft; it reaches the source file when the parent Geometry is saved.'}
      </p>
      <p data-testid="deep-connect-source-context">
        {resolvedSourceKind === 'vector'
          ? text
            ? `当前来源是 Host 检查确认的矢量组件${resolvedSourceLabel ? `：${resolvedSourceLabel}` : ''}；所有锚点必须属于此组件。`
            : `Current source is a Host-inspected vector component${resolvedSourceLabel ? `: ${resolvedSourceLabel}` : ''}; all anchors must belong to this component.`
          : text
            ? `当前来源是拓扑弧段${hasTopologyFID ? ` FID ${sourceFID}` : ''}；所有锚点必须属于同一精确源组件。`
            : `Current source is a topology arc${hasTopologyFID ? ` with FID ${sourceFID}` : ''}; all anchors must belong to the same exact source component.`}
      </p>
      <button type="button" data-testid="deep-connect-cancel" disabled={!canCancel} onClick={onCancel}>
        {text ? (anchorCount > 0 ? '清除锚点并取消' : '取消连接') : (anchorCount > 0 ? 'Clear anchors and cancel' : 'Cancel connect')}
      </button>
    </section>
  );
}

export default TopologyDeepConnectControls;
