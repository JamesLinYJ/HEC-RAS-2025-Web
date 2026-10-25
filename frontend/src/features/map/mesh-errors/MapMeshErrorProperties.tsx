import { text } from '../../../shared/workbench';
import type { Language } from '../../../shared/workbench';
import type { MapMeshErrorIssue } from './mapMeshErrorsWorkflow';
import type { MapMeshErrorsController } from './useMapMeshErrors';
import './mapMeshErrors.css';

/** Map glue for the original FlowAreaLayerVisualContext's MeshErrorProperties. */
export function MapMeshErrorProperties({ controller, language, onLocate }: {
  controller: MapMeshErrorsController;
  language: Language;
  onLocate: (issue: MapMeshErrorIssue) => void;
}) {
  if (!controller.visible) return null;
  const { state, dirty, pending, busy, current } = controller;
  const snapshot = state.snapshot;
  const editable = current && !!snapshot && !pending && !busy;
  const scanCurrent = current && !pending && !!snapshot;
  const summary = !snapshot || !scanCurrent ? '' : snapshot.status === 'disabled'
    ? text(language, '尚未启用检查。', 'Checks are disabled.')
    : snapshot.status === 'writerProtected'
      ? text(language, '来源正在计算，暂不读取网格。', 'The source has an active compute writer; mesh reads are paused.')
      : snapshot.totalIssueCount === 0
        ? text(language, '未发现超过阈值的节点。', 'No nodes exceed the threshold.')
        : text(language, `${snapshot.totalIssueCount} 个节点超过阈值。`, `${snapshot.totalIssueCount} nodes exceed the threshold.`);
  return <section className="map-mesh-errors" data-testid="map-mesh-errors-panel"
    data-source-path={state.capture?.sourcePath ?? ''} data-source-revision={state.capture?.sourceRevision ?? ''}
    data-visualization-revision={snapshot?.visualizationRevision ?? ''} data-pending={pending}>
    <h3>{text(language, '网格错误检查', 'Mesh error checks')}</h3>
    <p>{text(language, '检查节点连接的边数。设置保存到当前地图，检查读取已保存的网格。',
      'Check the number of faces connected to each node. Settings belong to this map; checks read the saved mesh.')}</p>
    <label className="map-mesh-errors-check">
      <input type="checkbox" data-testid="map-mesh-errors-enabled" checked={state.enabled}
        disabled={!editable} onChange={event => controller.setEnabled(event.target.checked)} />
      {text(language, '启用网格错误检查', 'Enable mesh error checks')}
    </label>
    <label className="map-mesh-errors-threshold">
      <span>{text(language, '每个节点允许的最大边数', 'Maximum faces per node')}</span>
      <input type="number" step="any" data-testid="map-mesh-errors-threshold" value={state.threshold}
        disabled={!editable} onChange={event => controller.setThreshold(event.target.value)} />
    </label>
    <small>{text(language, '保存时取整并限制在 3–64。', 'Saved values are rounded and limited to 3–64.')}
      {dirty && controller.normalizedThreshold !== null && controller.normalizedThreshold !== Number(state.threshold) &&
        text(language, ` 将保存为 ${controller.normalizedThreshold}。`, ` Will save as ${controller.normalizedThreshold}.`)}</small>
    {dirty && !pending && <p className="map-mesh-errors-draft" data-testid="map-mesh-errors-draft">
      {text(language, '设置尚未保存。请先保存或放弃，再切换地图或对象。', 'Unsaved settings. Save or discard before changing maps or objects.')}
    </p>}
    <div className="map-mesh-errors-actions">
      <button type="button" data-testid="map-mesh-errors-save" disabled={!controller.canSave} onClick={() => void controller.save()}>
        {text(language, '保存设置', 'Save settings')}</button>
      <button type="button" data-testid="map-mesh-errors-revert" disabled={!dirty || pending || busy} onClick={controller.revert}>
        {text(language, '放弃更改', 'Discard changes')}</button>
      {!pending && <button type="button" data-testid="map-mesh-errors-refresh" disabled={dirty || busy} onClick={controller.refresh}>
        {text(language, '重新读取', 'Reread')}</button>}
      {pending && <button type="button" data-testid="map-mesh-errors-reconcile" disabled={busy} onClick={() => void controller.reconcile()}>
        {text(language, '重新读取并核对', 'Reread and reconcile')}</button>}
      {busy && <button type="button" data-testid="map-mesh-errors-cancel" onClick={controller.cancel}>
        {text(language, '取消', 'Cancel')}</button>}
    </div>
    <div role="status" data-testid="map-mesh-errors-status" data-state={state.phase}>
      {state.message || (!current && !busy && !pending
        ? text(language, '等待当前地图的准确网格来源。', 'Waiting for an exact mesh source for this map.') : summary)}
    </div>
    {state.message && summary && <p data-testid="map-mesh-errors-summary">{summary}</p>}
    {dirty && scanCurrent && <p>{text(language, '以下检查结果对应上次保存的设置。', 'The results below use the last saved settings.')}</p>}
    {scanCurrent && snapshot && <>
      <ol className="map-mesh-errors-list" data-testid="map-mesh-errors-list">
        {snapshot.issues.map(issue => <li key={issue.id}>
          <button type="button" data-testid="map-mesh-errors-issue" data-issue-id={issue.id}
            data-mesh-index={issue.meshIndex} data-node-index={issue.nodeIndex} data-face-count={issue.faceCount}
            onClick={() => onLocate(issue)}>
            {text(language, `网格 ${issue.meshIndex} · 节点 ${issue.nodeIndex} — ${issue.faceCount} 条边`,
              `Mesh ${issue.meshIndex} · Node ${issue.nodeIndex} — ${issue.faceCount} faces`)}
            <span aria-hidden="true"> ↗</span>
          </button>
        </li>)}
      </ol>
      {snapshot.omittedIssueCount > 0 && <p>{text(language,
        `仅列出前 ${snapshot.issues.length} 条，另有 ${snapshot.omittedIssueCount} 条未显示。`,
        `Showing ${snapshot.issues.length}; ${snapshot.omittedIssueCount} additional issues are omitted.`)}</p>}
    </>}
    {state.capture && <details><summary>{text(language, '检查来源', 'Checked source')}</summary>
      <dl><dt>{text(language, '地图', 'Map')}</dt><dd>{state.capture.map.mapName}</dd>
        <dt>{text(language, '网格文件', 'Mesh file')}</dt><dd>{state.capture.sourcePath}</dd>
        <dt>SHA-256</dt><dd>{state.capture.sourceRevision}</dd></dl>
    </details>}
    {state.diagnostic && <details><summary>{text(language, '诊断详情', 'Diagnostic details')}</summary><pre>{state.diagnostic}</pre></details>}
  </section>;
}
