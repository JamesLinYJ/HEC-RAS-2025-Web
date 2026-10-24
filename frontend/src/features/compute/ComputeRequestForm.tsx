import type { Language } from '../../shared/workbench';
import type { ComputeIssueCode, ComputeValidationIssues, NumericComputeField, RawComputeInputs } from './computeTypes';

const labels = {
  zh: {
    request: '计算请求', method: '时间推进', equation: '方程组', explicit: '显式', implicit: '隐式', swe: '浅水方程（SWE）',
    terrainElevation: '地面高程 (m)', initialStage: '初始水位 (m)', roughness: '曼宁糙率', precipitationRate: '降雨强度 (m/s)',
    timestepSeconds: '时间步长 (s)', durationSeconds: '模拟时长 (s)', sample: '内存湿网格算例', preview: '有效请求预览', invalid: '输入有误，修正前不会发送请求。',
    required: '必填。', number: '请输入有效的有限数值。', range: '超出此算例支持范围。', stageAboveTerrain: '初始水位必须高于地面高程。',
    durationAtLeastTimestep: '模拟时长必须大于或等于时间步长。', stepLimit: '时间步数不得超过 6000。',
    unsupportedOption: '当前算例不支持此选项。',
  },
  en: {
    request: 'Compute request', method: 'Time stepping', equation: 'Equation set', explicit: 'Explicit', implicit: 'Implicit', swe: 'Shallow water equations (SWE)',
    terrainElevation: 'Terrain elevation (m)', initialStage: 'Initial stage (m)', roughness: 'Manning roughness', precipitationRate: 'Precipitation rate (m/s)',
    timestepSeconds: 'Time step (s)', durationSeconds: 'Simulation duration (s)', sample: 'In-memory wet-grid sample', preview: 'Valid request preview', invalid: 'Input is invalid; no request will be sent until corrected.',
    required: 'Required.', number: 'Enter a finite numeric value.', range: 'Outside the supported range for this sample.', stageAboveTerrain: 'Initial stage must be above terrain elevation.',
    durationAtLeastTimestep: 'Duration must be at least one time step.', stepLimit: 'The request cannot exceed 6,000 time steps.',
    unsupportedOption: 'This option is not supported by the current sample.',
  },
} satisfies Record<Language, Record<string, string>>;

const fields: { key: NumericComputeField; step: string; min: string; max: string }[] = [
  { key: 'terrainElevation', step: 'any', min: '-10000', max: '10000' },
  { key: 'initialStage', step: 'any', min: '-10000', max: '10000' },
  { key: 'roughness', step: 'any', min: '0.005', max: '0.2' },
  { key: 'precipitationRate', step: 'any', min: '0', max: '0.01' },
  { key: 'timestepSeconds', step: 'any', min: '0.01', max: '1' },
  { key: 'durationSeconds', step: 'any', min: '0', max: '60' },
];

const issueText = (language: Language, issue: ComputeIssueCode): string => labels[language][issue];

export interface ComputeRequestFormProps {
  language: Language;
  raw: RawComputeInputs;
  issues: ComputeValidationIssues;
  requestPreview?: string;
  disabled: boolean;
  onChange: <K extends keyof RawComputeInputs>(field: K, value: RawComputeInputs[K]) => void;
}

export function ComputeRequestForm({ language, raw, issues, requestPreview, disabled, onChange }: ComputeRequestFormProps) {
  const t = labels[language];
  return (
    <section className="request-card compute-request-form" aria-label={t.request}>
      <div className="request-header"><span>{t.request}</span><span className="request-type">{t.sample}</span></div>
      <div className="compute-form-selects">
        <label className="compute-field">
          <span>{t.equation}</span>
          <select value="SWE" disabled aria-label={t.equation}>
            <option value="SWE">{t.swe}</option>
          </select>
        </label>
        <label className="compute-field">
          <span>{t.method}</span>
          <select data-compute-field="timeStepping" value={raw.timeStepping} disabled={disabled} aria-invalid={Boolean(issues.timeStepping)} onChange={(event) => onChange('timeStepping', event.currentTarget.value as RawComputeInputs['timeStepping'])}>
            <option value="Explicit">{t.explicit}</option>
            <option value="Implicit">{t.implicit}</option>
          </select>
          {issues.timeStepping && <span className="compute-field-error" role="alert">{issueText(language, issues.timeStepping)}</span>}
        </label>
      </div>
      <div className="compute-form-grid">
        {fields.map(({ key, step, min, max }) => {
          const issue = issues[key];
          return (
            <label className="compute-field" key={key}>
              <span>{t[key]}</span>
              <input
                type="text"
                inputMode="decimal"
                value={raw[key]}
                disabled={disabled}
                step={step}
                data-compute-field={key}
                aria-invalid={Boolean(issue)}
                aria-describedby={issue ? `compute-error-${key}` : undefined}
                onChange={(event) => onChange(key, event.currentTarget.value)}
              />
              <span className="compute-range">{min} – {max}</span>
              {issue && <span className="compute-field-error" id={`compute-error-${key}`} role="alert">{issueText(language, issue)}</span>}
            </label>
          );
        })}
      </div>
      <div className="compute-preview">
        <div className="small-label">{t.preview}</div>
        {requestPreview
          ? <code>{requestPreview}</code>
          : <div className="compute-invalid-hint" role="status">{t.invalid}</div>}
      </div>
    </section>
  );
}
