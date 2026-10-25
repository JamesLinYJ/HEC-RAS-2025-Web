import type { FeaturePageProps } from '../../../shared/workbench';
import { errorDiagnostics } from '../../../shared/workbench';
import { TractiveForceControl } from './TractiveForceControl';
import { stableChannelMethods, StableChannelError } from './stableChannelWorkflow';
import { useStableChannel } from './useStableChannel';
import './stableChannel.css';

/** StableChannelWindow: the first persisted design, or explicitly marked in-memory source defaults. */
export function StableChannelWindow(props: FeaturePageProps) {
  const vm = useStableChannel(props);
  const available = props.backend.supports(stableChannelMethods.open) && props.backend.supports(stableChannelMethods.compute);
  const disabled = vm.locked || !vm.current;
  const source = vm.capture?.source;
  return <section className="stable-channel" data-testid="stable-channel">
    <header className="stable-heading"><div><span className="stable-eyebrow">水力设计</span><h2>稳定河道设计</h2>
      <p>使用牵引力法计算稳定断面。选择两个未知量，填写其余参数，计算结果直接回填到表单。</p></div>
      <span className="stable-badge">{available ? '牵引力法' : '服务接口不可用'}</span></header>
    <section className="stable-card">
      <div className="stable-toolbar"><h3>设计来源</h3><button type="button" data-testid="stable-open" disabled={!available || vm.locked || !!vm.reading || vm.dirty}
        onClick={() => void vm.open()}>{vm.reading === 'open' ? '正在读取…' : source ? '重新读取设计' : '读取稳定河道设计'}</button></div>
      <p>{props.context.projectPath ? '读取当前工程中第一个稳定河道设计文件。工程中没有设计时，打开临时设计。' : '当前没有已打开工程，可使用默认参数创建临时设计。'}</p>
      <div className="stable-source" data-testid="stable-source"><strong>{source ? source.inMemory ? '临时设计 · 仅保留在内存中' : '已有设计文件 · 可保存' : '尚未读取设计'}</strong>
        <code>{source?.designPath ?? props.context.projectPath ?? '无工程文件'}</code>
        {source?.inMemory && <span>该设计没有对应文件，计算不会自动创建文件，也不能保存。</span>}
      </div>
      <p className="stable-help">参数单位：水深与底宽 ft，流量 ft³/s，水温 °F，粒径 mm，角度 °。</p>
      {vm.dirty && <div className="stable-toolbar"><strong data-testid="stable-dirty">本页有未保存修改</strong>
        <button type="button" data-testid="stable-discard" disabled={vm.locked} onClick={vm.discard}>放弃本页修改</button></div>}
      {source && !vm.current && <p role="alert" className="stable-warning">当前工程会话、来源或服务连接已变化。原始输入仍保留；请先明确放弃本页修改，再重新读取。</p>}
    </section>

    {vm.draft && <>
      <TractiveForceControl draft={vm.draft} disabled={disabled} onChange={vm.change} />
      <div className="stable-card stable-toolbar"><div>
        <p className="stable-help" data-testid="stable-input-status">{vm.inputError ?? '已选择两个求解目标，已知参数已填写。'}</p>
        <span role="status" data-testid="stable-notice">{vm.reading === 'compute' ? '正在计算…' : vm.notice}</span></div>
        <div className="stable-actions"><button type="button" data-testid="stable-compute" disabled={disabled || !!vm.reading || !available || !!vm.inputError}
          onClick={() => void vm.compute()}>计算</button>
          <button type="button" data-testid="stable-save" className="stable-primary" disabled={!vm.canSave} onClick={() => void vm.save()}>保存设计文件</button></div>
      </div>
    </>}
    {vm.error !== undefined && <div className="stable-error" role="alert" data-testid="stable-error">
      <strong>{vm.error instanceof StableChannelError ? vm.error.message : '稳定河道操作未完成，当前输入已保留。请检查服务连接或来源文件。'}</strong>
      <details><summary>技术诊断</summary><pre>{JSON.stringify(errorDiagnostics(vm.error), null, 2)}</pre></details>
    </div>}
    {vm.saveStatus && <section className="stable-card" data-testid="stable-save-status" data-phase={vm.saveStatus.phase}>
      <div className="stable-toolbar"><h3>{vm.saveStatus.phase === 'saved' ? '保存完成' : vm.saveStatus.phase === 'saving' ? '保存中' : vm.saveStatus.phase === 'unknown' ? '保存结果待确认' : vm.saveStatus.phase === 'unverified' ? '保存回执未通过核对' : '保存任务已终止'}</h3>
        {(vm.saveStatus.phase === 'unknown' || vm.saveStatus.phase === 'unverified') && <button type="button" data-testid="stable-resume" disabled={!vm.canResume} onClick={() => void vm.resume()}>继续核对原保存任务</button>}</div>
      <p role="status">{vm.saveStatus.message}</p><code>{vm.saveStatus.designPath}</code>
      {vm.saveStatus.phase === 'unknown' && !vm.saveStatus.operationID && <p className="stable-warning">尚未取得任务编号，无法自动续查。请保留当前页面并核对服务器上的原设计文件。</p>}
      <details><summary>保存回执与任务身份</summary><p>任务编号：<code data-testid="stable-operation-id">{vm.saveStatus.operationID ?? '尚未取得'}</code></p>
        <pre>{JSON.stringify(vm.saveStatus.receipt ?? vm.saveStatus.diagnostics ?? {}, null, 2)}</pre></details>
    </section>}
    {source && <details className="stable-card"><summary>来源修订与诊断信息</summary>
      <p>工程会话：<code>{vm.capture?.context.projectSessionID}</code></p><p>来源编号：<code>{source.sourceID}</code></p>
      <p>工程 SHA-256：<code>{source.projectRevision ?? '无工程文件'}</code></p>
      <p>设计 SHA-256：<code data-testid="stable-design-revision">{source.designRevision ?? '临时设计'}</code></p>
    </details>}
  </section>;
}
