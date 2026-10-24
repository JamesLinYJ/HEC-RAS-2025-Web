import type { Language } from '../../shared/workbench';
import type { PlanComputeExecutionResult } from './computeTypes';
import type { ComputeRunState } from './ComputeButton';

const labels = {
  zh: {
    summary: '计划计算摘要', output: '后端原始结果', error: '计算错误', noOutput: '服务端返回终态结果后会在此显示原始 JSON。',
    status: '结果状态', solver: '求解器', version: '软件版本', resultPath: '结果文件', duration: '计算耗时', retries: '全局重试',
    completed: '完成', cancelled: '取消', failed: '失败', pending: '尚无结果', copy: '复制', copied: '已复制', diagnostics: '服务端诊断信息',
  },
  en: {
    summary: 'Plan compute summary', output: 'Raw backend result', error: 'Compute error', noOutput: 'The raw JSON will appear here after the server returns a terminal result.',
    status: 'Result state', solver: 'Solver', version: 'Software version', resultPath: 'Result file', duration: 'Compute duration', retries: 'Global retries',
    completed: 'Completed', cancelled: 'Canceled', failed: 'Failed', pending: 'No result yet', copy: 'Copy', copied: 'Copied', diagnostics: 'Backend diagnostics',
  },
} satisfies Record<Language, Record<string, string>>;

export interface ComputeSummaryControlProps {
  language: Language;
  state: ComputeRunState;
  result?: PlanComputeExecutionResult;
  rawResult?: string;
  rawError?: string;
  rawErrorDetail?: string;
  elapsedMilliseconds?: number;
  copied: boolean;
  onCopy: () => void;
}

function formatNumber(value: number, language: Language, maximumFractionDigits = 4): string {
  return new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en-US', { maximumFractionDigits }).format(value);
}

function formatElapsed(value: number, language: Language): string {
  const totalSeconds = Math.max(0, value / 1000);
  return `${formatNumber(totalSeconds, language, 2)} s`;
}

export function ComputeSummaryControl({ language, state, result, rawResult, rawError, rawErrorDetail, elapsedMilliseconds, copied, onCopy }: ComputeSummaryControlProps) {
  const t = labels[language];
  const statusText = result
    ? t[result.state]
    : rawResult
      ? (state === 'unverified' ? (language === 'zh' ? '未识别' : 'Unrecognized') : t.pending)
      : t.pending;

  const stats: [string, string | undefined][] = [
    [t.status, statusText],
    [t.solver, result?.summary?.solver ?? undefined],
    [t.version, result?.summary?.softwareVersion ?? undefined],
    [t.resultPath, result?.resultPath ?? undefined],
    [t.duration, result?.summary?.totalDurationSeconds == null ? undefined : `${formatNumber(result.summary.totalDurationSeconds, language, 2)} s`],
    [t.retries, result?.summary?.globalRetryCount == null ? undefined : formatNumber(result.summary.globalRetryCount, language, 0)],
    ...(elapsedMilliseconds === undefined ? [] : [[language === 'zh' ? '界面等待时间' : 'UI wait time', formatElapsed(elapsedMilliseconds, language)] as [string, string]]),
  ];

  return (
    <section className="result-section" aria-label={t.summary}>
      <div className="section-row"><h3>{t.summary}</h3>
        {rawResult && <button className="copy-button" onClick={onCopy}>{copied ? t.copied : t.copy}</button>}
      </div>
      {result && <div className="compute-summary-grid">
        {stats.map(([label, value]) => <div className="compute-summary-stat" key={label}>
          <span>{label}</span><strong>{value ?? '—'}</strong>
        </div>)}
      </div>}
      {rawError && <><div className="small-label">{t.error}</div><pre className="code-block error-block">{rawError}</pre></>}
      {rawErrorDetail && <details className="compute-error-details"><summary>{t.diagnostics}</summary><pre className="code-block error-block">{rawErrorDetail}</pre></details>}
      <div className="section-row compute-raw-heading"><h3>{t.output}</h3></div>
      {rawResult
        ? <pre className="code-block result-code" data-compute-output>{rawResult}</pre>
        : <div className="result-empty"><span className="result-braces">{'{ }'}</span>{t.noOutput}</div>}
    </section>
  );
}
