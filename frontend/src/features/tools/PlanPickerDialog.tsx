import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { PlanExportChoice } from './exportWorkflow';

interface Props {
  open: boolean;
  plans: PlanExportChoice[];
  selectedPath: string;
  projectPath: string | null;
  projectSessionID: string;
  projectRevision: string | null;
  language: Language;
  onChoose: (path: string) => void;
  onClose: () => void;
}

export function PlanPickerDialog({ open, plans, selectedPath, projectPath, projectSessionID, projectRevision, language, onChoose, onClose }: Props) {
  const [choice, setChoice] = useState(selectedPath);

  useEffect(() => {
    setChoice(plans.some(plan => plan.id === selectedPath) ? selectedPath : '');
  }, [open, plans, selectedPath]);

  if (!open) return null;
  return <div className="tools-task-dialog-backdrop" role="presentation">
    <section className="tools-task-dialog tools-plan-picker-dialog" role="dialog" aria-modal="true" aria-labelledby="tools-plan-picker-title">
      <header className="tools-task-dialog-header">
        <div><span className="tools-kicker">HOST PLAN CATALOG</span><h2 id="tools-plan-picker-title">{text(language, '选择计算计划', 'Choose a plan')}</h2></div>
        <button className="tools-button tools-button-secondary" type="button" onClick={onClose}>{text(language, '取消', 'Cancel')}</button>
      </header>
      <div className="tools-plan-picker-context">
        <span>{text(language, '工程路径', 'Project path')}: <code>{projectPath || '—'}</code></span>
        <span>{text(language, '工程会话', 'Project session')}: <code>{projectSessionID || '—'}</code></span>
        <span>{text(language, '工程修订', 'Project revision')}: <code>{projectRevision || '—'}</code></span>
      </div>
      {plans.length > 0 ? <div className="tools-plan-picker-list" role="radiogroup" aria-label={text(language, 'Host 返回的计算计划', 'Plans returned by the Host')}>
        {plans.map(plan => <label className={`tools-plan-picker-choice ${choice === plan.id ? 'is-selected' : ''}`} key={plan.id}>
          <input type="radio" name="tools-plan-picker-choice" value={plan.id} checked={choice === plan.id} onChange={() => setChoice(plan.id)} />
          <span><strong>{plan.title}</strong><code>{plan.id}</code></span>
        </label>)}
      </div> : <p className="tools-export-callout">{text(language, 'Host 当前工程目录没有返回可选计划。', 'The Host returned no selectable plans for this project.')}</p>}
      <p className="tools-export-callout">{text(language, '列表来自当前工程的 planExport.inspect。选择后仍需重新检查计划源修订与导出选项，检查通过前不会执行导出。', 'This list came from planExport.inspect for the active project. After choosing, inspect the plan revision and export options; export remains disabled until inspection passes.')}</p>
      <footer className="tools-task-dialog-actions">
        <button className="tools-button tools-button-secondary" type="button" onClick={onClose}>{text(language, '返回', 'Back')}</button>
        <button className="tools-button tools-button-primary" type="button" disabled={!choice || !plans.some(plan => plan.id === choice)} onClick={() => { onChoose(choice); onClose(); }}>
          {text(language, '选择此计划', 'Choose plan')}
        </button>
      </footer>
    </section>
  </div>;
}
