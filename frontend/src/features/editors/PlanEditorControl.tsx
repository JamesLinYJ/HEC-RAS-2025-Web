import type { EditorPageProps } from './model';
import { pageTitle } from './model';
import { PlanSettingsPage } from './PlanSettingsPage';
import { PlanComputeSettingsPage } from './PlanComputeSettingsPage';

/** React port of Ras.Interface.WinUI.Editors.PlanEditorControl. */
export function PlanEditorControl(props: EditorPageProps) {
  const tabs = ['PlanSettingsPage', 'PlanComputeSettingsPage'];
  const activePage = tabs.includes(props.pageId) ? props.pageId : tabs[0];
  return (
    <div className="source-editor-plan">
      <nav className="editor-page-tabs" aria-label={props.language === 'zh' ? '计划页面' : 'Plan pages'}>
        {tabs.map((tab) => <button type="button" key={tab} className={activePage === tab ? 'selected' : ''} aria-current={activePage === tab ? 'page' : undefined} onClick={() => props.onPageChange(tab)}>{pageTitle(tab, props.language)}</button>)}
      </nav>
      {activePage === 'PlanComputeSettingsPage' ? <PlanComputeSettingsPage {...props} pageId={activePage} /> : <PlanSettingsPage {...props} pageId={activePage} />}
    </div>
  );
}
