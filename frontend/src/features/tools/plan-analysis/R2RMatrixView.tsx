import type { FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics, message } from '../../../shared/workbench';
import { useR2RMatrixVM } from './R2RMatrixVM';
import { r2rMethods } from './r2rMatrixWorkflow';
import { r2rPropertyLabel, r2rReason, r2rRuleLabel, r2rStatusLabel } from './r2rMatrixLabels';
import './r2rMatrix.css';

/** R2RMatrixView / R2RMatrixVM retain the original matrix and explicit emitter workflow. */
export function R2RMatrixView(props: FeaturePageProps) {
  const vm = useR2RMatrixVM(props);
  const run = vm.run;
  const disabled = vm.locked || !!vm.reading;
  const supported = Object.values(r2rMethods).every(method => props.backend.supports(method));
  const rows = vm.inspection?.preview.combinations;
  const receipt = vm.run?.receipt;
  return <section className="r2r-matrix" data-testid="r2r-matrix">
    <header className="r2r-heading">
      <div><span className="r2r-eyebrow">R2R MATRIX</span><h2>计算参数矩阵</h2>
        <p>按多个参数生成待计算文件。先预览组合，再明确导出；这里不会启动求解器。</p></div>
      <span className={`r2r-badge ${supported ? '' : 'r2r-warning'}`}>{supported ? 'C# Host 已接通' : 'Host 接口不可用'}</span>
    </header>

    <div className="r2r-card">
      <div className="r2r-toolbar"><h3>1. 来源与服务器目录</h3>
        <button type="button" data-testid="r2r-load-options" disabled={disabled || !supported || !props.context.projectPath}
          onClick={() => void vm.loadOptions()}>{vm.reading === 'options' ? '正在读取…' : vm.options ? '重新读取并重置参数轴' : '读取计划与参数'}</button></div>
      {!props.context.projectPath && <p className="r2r-callout">请先打开已保存的工程。</p>}
      <div className="r2r-two-columns">
        <label className="r2r-field"><span>源计算计划</span>
          <select data-testid="r2r-plan" value={vm.planPath} disabled={disabled || !vm.options?.plans.length}
            onChange={event => vm.choosePlan(event.target.value)}>
            {!vm.options?.plans.length && <option value={vm.planPath}>{vm.planPath || '读取后选择计划'}</option>}
            {vm.options?.plans.map(plan => <option key={plan.planPath} value={plan.planPath}>{plan.name}</option>)}
          </select><code>{vm.planPath || '尚未选择'}</code>
        </label>
        <label className="r2r-field"><span>服务器输出目录（绝对路径）</span>
          <input data-testid="r2r-output-directory" value={vm.form.outputDirectory} disabled={disabled}
            onChange={event => vm.changeForm(current => ({ ...current, outputDirectory: event.target.value }))}
            placeholder="C# 服务所在机器上的目录" />
          <small>文件写入服务器；浏览器不会弹出本机保存对话框。</small>
        </label>
      </div>
      <div data-testid="r2r-draft-source" className="r2r-source">
        {vm.source?.drafts.some(draft => draft.assetPath === vm.planPath)
          ? '当前来源包含 Plan 编辑器的未保存草稿；分析会捕获原始字段和有序时间窗修改。'
          : 'Host 读取所选源 Plan；若该 Plan 有未保存编辑，将要求完整捕获其草稿。'}
        <small>矩阵设置仅保留在此页面。重新读取会重置参数轴；修改后切换工程须明确放弃设置。</small>
        {vm.formDirty && <button type="button" data-testid="r2r-discard-form" disabled={disabled} onClick={vm.discardForm}>放弃矩阵设置</button>}
      </div>
      {!vm.resolution.ok && !vm.locked && <div className="r2r-callout r2r-warning" data-testid="r2r-draft-blockers">
        <strong>来源暂不可分析</strong><ul>{vm.resolution.blockers.map((blocker, index) => <li key={`${blocker.code}:${index}`}>{blocker.message}</li>)}</ul>
        <small>有 Plan 草稿时，请先在模型编辑器选中该计划，再读取参数。</small>
      </div>}
      {vm.options && !vm.optionsCurrent && !disabled && <p className="r2r-callout" data-testid="r2r-source-changed">来源或编辑草稿已变化，请重新读取参数；旧预览已失效。</p>}
    </div>

    <div className="r2r-grid">
      <div className="r2r-card"><h3>2. 参数轴</h3>
        <p className="r2r-muted">轴与候选值按添加顺序组合。只选一个值会把它固定到每个文件。</p>
        <div className="r2r-add-axis">
          <select data-testid="r2r-property" aria-label="要添加的参数" value={vm.availableProperties.some(item => item.propertyName === vm.propertyToAdd)
            ? vm.propertyToAdd : vm.availableProperties[0]?.propertyName ?? ''}
            disabled={disabled || !vm.optionsCurrent} onChange={event => vm.setPropertyToAdd(event.target.value)}>
            {!vm.availableProperties.length && <option value="">没有可添加的参数</option>}
            {vm.availableProperties.map(property => <option key={property.propertyName} value={property.propertyName}>{r2rPropertyLabel(property.propertyName)} · {property.propertyName}</option>)}
          </select>
          <button type="button" data-testid="r2r-add-axis" disabled={disabled || !vm.optionsCurrent || !vm.availableProperties.length} onClick={vm.addAxis}>添加参数轴</button>
        </div>
        {!vm.form.axes.length && <p className="r2r-empty">尚未添加参数轴。无参数轴时，Host 会预览一个保持计划设置的组合。</p>}
        {vm.form.axes.map(axis => {
          const property = vm.options?.properties.find(item => item.propertyName === axis.propertyName);
          if (!property) return null;
          return <fieldset className="r2r-axis" data-testid="r2r-axis" data-property={axis.propertyName} key={axis.propertyName} disabled={disabled || !vm.optionsCurrent}>
            <legend>{r2rPropertyLabel(axis.propertyName)} <code>{axis.propertyName}</code></legend>
            <div className="r2r-toolbar"><small>{axis.values.filter(value => value.selected).length} 个已选原值 · {property.isClosedSet ? '固定候选集合' : '自由输入'}</small>
              <button type="button" data-testid="r2r-remove-axis" onClick={() => vm.changeForm(current => ({ ...current, axes: current.axes.filter(item => item.propertyName !== axis.propertyName) }))}>删除轴</button></div>
            <div className="r2r-values">{axis.values.map((value, index) => <div className="r2r-value" key={`${index}:${value.value}`}>
              <label><input type="checkbox" data-testid="r2r-value-check" data-raw-value={value.value} checked={value.selected}
                onChange={event => vm.changeForm(current => ({ ...current, axes: current.axes.map(item => item.propertyName === axis.propertyName
                  ? { ...item, values: item.values.map((choice, choiceIndex) => choiceIndex === index ? { ...choice, selected: event.target.checked } : choice) } : item) }))} />
                <span>{value.label}<code>{value.value}</code></span></label>
              {value.removable && <button type="button" aria-label={`删除候选值 ${value.value}`} data-testid="r2r-remove-value"
                onClick={() => vm.changeForm(current => ({ ...current, axes: current.axes.map(item => item.propertyName === axis.propertyName
                  ? { ...item, values: item.values.filter((_, choiceIndex) => choiceIndex !== index) } : item) }))}>×</button>}
            </div>)}</div>
            {!property.isClosedSet && <div className="r2r-add-values">
              <input data-testid="r2r-new-value" aria-label={`${r2rPropertyLabel(axis.propertyName)} 新候选值`} value={axis.newValue}
                placeholder={property.valueType === 'String' ? '输入一个原始字符串' : '输入原值，多个值用逗号或分号分隔'}
                onChange={event => vm.changeForm(current => ({ ...current, axes: current.axes.map(item => item.propertyName === axis.propertyName ? { ...item, newValue: event.target.value } : item) }))} />
              <button type="button" data-testid="r2r-add-values" onClick={() => vm.addValues(axis.propertyName)}>添加值</button>
            </div>}
            <details><summary>Host 类型与输入说明</summary><p>{property.valueType} · {property.inputHint || '由 Host 解析原值'}</p><p>{property.displayName}</p></details>
          </fieldset>;
        })}
      </div>
      <div className="r2r-card"><h3>3. 组合规则与输出选项</h3>
        <div className="r2r-rules">{vm.options?.rules.map(rule => <label key={rule.name} className="r2r-check">
          <input type="checkbox" data-testid="r2r-rule" data-rule={rule.name} checked={vm.form.enabledRuleNames.includes(rule.name)} disabled={disabled || !vm.optionsCurrent}
            onChange={event => vm.changeForm(current => ({ ...current, enabledRuleNames: vm.options!.rules.filter(item => item.name === rule.name ? event.target.checked : current.enabledRuleNames.includes(item.name)).map(item => item.name) }))} />
          <span>{r2rRuleLabel(rule.name)}<small>{r2rReason(rule.reason)}</small></span>
        </label>)}</div>
        <label className="r2r-check"><input type="checkbox" data-testid="r2r-collapse" disabled={disabled} checked={vm.form.collapseIrrelevant}
          onChange={event => vm.changeForm(current => ({ ...current, collapseIrrelevant: event.target.checked }))} /><span>合并仅在不适用参数上不同的组合</span></label>
        <hr />
        <label className="r2r-check"><input type="checkbox" data-testid="r2r-overwrite" disabled={disabled} checked={vm.form.overwrite}
          onChange={event => vm.changeForm(current => ({ ...current, overwrite: event.target.checked }))} /><span>覆盖同名 R2R 文件<small>默认关闭；已存在的 .r2r.h5 文件会逐个跳过。</small></span></label>
        <label className="r2r-check"><input type="checkbox" data-testid="r2r-name-embedded" disabled={disabled} checked={vm.form.nameEmbeddedPlan}
          onChange={event => vm.changeForm(current => ({ ...current, nameEmbeddedPlan: event.target.checked }))} /><span>按输出文件名命名内嵌 Plan</span></label>
        <label className="r2r-check"><input type="checkbox" data-testid="r2r-manifest" disabled={disabled} checked={vm.form.writeManifest}
          onChange={event => vm.changeForm(current => ({ ...current, writeManifest: event.target.checked }))} /><span>生成或更新矩阵清单<small>同目录、同计划的 CSV 清单会更新，独立于 R2R 文件覆盖选项。</small></span></label>
        <div className="r2r-actions"><button type="button" data-testid="r2r-preview" disabled={disabled || !supported || !vm.resolution.ok || !vm.optionsCurrent}
          onClick={() => void vm.preview()}>{vm.reading === 'preview' ? '正在预览…' : '预览组合'}</button>
          <button type="button" className="r2r-primary" data-testid="r2r-emit" disabled={!vm.canEmit} onClick={() => void vm.emit()}>导出 {vm.inspection?.preview.writeCount ?? 0} 个文件</button></div>
        <small>输入、来源或编辑草稿变化后需重新预览。最多 2048 个组合。</small>
      </div>
    </div>

    {vm.error !== undefined && <div role="alert" className="r2r-callout r2r-error" data-testid="r2r-error">
      <strong>{message(vm.error, props.language)}</strong><details><summary>技术诊断</summary><pre>{JSON.stringify(errorDiagnostics(vm.error), null, 2)}</pre></details>
    </div>}
    {vm.inspection && <section className="r2r-card" data-testid="r2r-preview-result">
      <div className="r2r-toolbar"><h3>组合预览</h3><strong>{rows!.length} 个组合 · {vm.inspection.preview.writeCount} 个文件 · {rows!.filter(row => row.isCollapsed).length} 个已合并</strong></div>
      <code>{vm.inspection.preview.outputDirectory}</code>
      {!!vm.inspection.preview.hint && <div className="r2r-callout">部分参数在全部组合中均不适用，因此不会增加文件数。可添加控制其启用的开关参数轴，或返回 Plan 编辑器修改。<details><summary>Host 原始说明</summary><p>{vm.inspection.preview.hint}</p></details></div>}
      <div className="r2r-table-wrap"><table><thead><tr><th>序号</th><th>参数值与适用性</th><th>文件名</th><th>组合状态 / 原因</th></tr></thead>
        <tbody>{rows!.map(row => <tr key={row.index} data-testid="r2r-combination" data-combination-index={row.index}>
          <td>{row.index}</td><td>{row.assignments.length ? row.assignments.map(assignment => <div key={assignment.propertyName} className={!assignment.isRelevant ? 'r2r-ignored' : ''}>
            <span>{r2rPropertyLabel(assignment.propertyName)} = <code>{assignment.value}</code></span>
            {!assignment.isRelevant && <small>不适用：{r2rReason(assignment.irrelevantReason)}</small>}</div>) : '保持源计划设置'}</td>
          <td><code>{row.filename || '—'}</code></td><td>{row.isCollapsed ? '已合并' : row.isExcluded ? '已排除' : '待导出'}
            {row.exclusionReason && <small>{r2rReason(row.exclusionReason)}</small>}</td>
        </tr>)}</tbody></table></div>
      <details><summary>来源与检查身份</summary><p>工程会话：<code>{vm.inspection.source.sessionID}</code></p><p>Plan SHA-256：<code>{vm.inspection.request.planRevision}</code></p><p>检查身份：<code>{vm.inspection.preview.inspectionID}</code></p></details>
    </section>}

    {vm.run && <section className="r2r-card r2r-run" data-testid="r2r-run" data-phase={vm.run.phase}>
      <div className="r2r-toolbar"><h3>{vm.run.phase === 'unknown' ? '导出终态未确认' : vm.run.phase === 'running' ? '正在导出' : receipt?.state === 'cancelled' ? '导出已取消' : receipt?.failed || receipt?.wasAborted ? '导出已结束，含失败项' : '导出任务已结束'}</h3>
        {vm.isTracking && <button type="button" data-testid="r2r-cancel" disabled={vm.run.cancelRequested} onClick={vm.cancel}>{vm.run.cancelRequested ? '已请求取消，等待终态…' : '请求取消'}</button>}
        {vm.run.phase === 'unknown' && <button type="button" data-testid="r2r-resume" disabled={!vm.canResume} onClick={() => void vm.resume()}>继续核对原任务</button>}</div>
      <div className="r2r-source"><span>本次提交的 Plan：<code>{vm.run.inspected.request.planPath}</code></span>
        <span>服务器目录：<code>{vm.run.inspected.preview.outputDirectory}</code></span>
        <span>任务编号：<code data-testid="r2r-operation-id">{vm.run.operationID ?? '尚未取得 Host 任务编号'}</code></span></div>
      <p className="r2r-callout">取消只会请求停止后续处理，已写出的文件会保留。请以逐文件终态和服务器目录为准。</p>
      {vm.run.terminalMessage && <p role="status" className="r2r-callout r2r-warning">{vm.run.terminalMessage}</p>}
      {vm.run.phase === 'unknown' && !vm.run.operationID && <p>服务器是否接受请求尚不明确，无法自动关联任务。原输入与写保护已保留，请先核对服务器输出；此处不会重新提交。</p>}
      {vm.run.progress?.fraction !== undefined && <progress max={1} value={vm.run.progress.fraction} aria-label="R2R 导出进度" />}
      {vm.run.progress?.message && <details><summary>Host 当前进度</summary><p>{vm.run.progress.message}</p></details>}
      {receipt && <>
        <p className="r2r-receipt-summary" data-testid="r2r-receipt-summary">已写入 {receipt.written} · 已跳过 {receipt.skipped} · 失败 {receipt.failed} · 排除 {receipt.excluded}{receipt.wasCancelled ? ' · 已取消' : ''}</p>
        <p data-testid="r2r-source-current">{receipt.sourcesStillCurrent ? 'Host 确认工程、源 Plan 和本次捕获的依赖修订仍一致。' : '工程、源 Plan 或捕获的依赖在导出期间已变化；文件回执保留原提交来源。'}<small>依赖核对包含当前计划引用的几何、边界条件、地形与糙率来源。</small></p>
        <p data-testid="r2r-manifest-path">矩阵清单：<code>{receipt.manifestPath ?? (vm.run.form.writeManifest ? 'Host 未返回清单路径，请检查进度诊断' : '本次未要求生成')}</code></p>
        {receipt.abortReason && <p className="r2r-warning">提前停止：{receipt.abortReason}</p>}
      </>}
      <div className="r2r-table-wrap"><table><thead><tr><th>文件名</th><th>实际状态</th><th>服务器文件 / 字节</th><th>说明</th></tr></thead><tbody>
        {vm.run.inspected.preview.combinations.map((combo, index) => {
          const file = receipt?.files[index], status = file?.status ?? run?.fileProgress[combo.filename];
          return <tr key={combo.index} data-testid="r2r-file-result" data-file-status={file?.status ?? ''}>
            <td><code>{combo.filename || `组合 ${combo.index}`}</code></td><td>{status ? r2rStatusLabel(status) : combo.isExcluded ? '已排除' : '尚未确认'}{!file && status ? '（进度）' : ''}</td>
            <td><code>{file?.filePath ?? '—'}</code>{file && <small>{file.fileSizeBytes} 字节</small>}</td><td>{r2rReason(file?.message ?? combo.exclusionReason)}</td>
          </tr>;
        })}
      </tbody></table></div>
      {vm.run.error !== undefined && <div role="alert" className="r2r-error">{message(vm.run.error, props.language)}</div>}
      <details><summary>本次任务来源与原始回执</summary><p>工程会话：<code>{vm.run.inspected.source.sessionID}</code></p>
        <p>Plan SHA-256：<code>{vm.run.inspected.request.planRevision}</code></p><p>检查身份：<code>{vm.run.inspected.preview.inspectionID}</code></p>
        <pre>{JSON.stringify(vm.run.rawReceipt ?? vm.run.diagnostics ?? {}, null, 2)}</pre></details>
    </section>}
  </section>;
}
