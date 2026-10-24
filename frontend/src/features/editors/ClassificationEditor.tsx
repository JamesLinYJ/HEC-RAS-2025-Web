import type { EditorPageProps } from './model';
import { pageTitle } from './model';
import { ClassificationLayerSummary } from './ClassificationLayerSummary';
import { ClassificationLayersEditor } from './ClassificationLayersEditor';
import { SchemaPage } from './SchemaFieldRenderer';

/** React composition for the source classification summary and input editor. */
export function ClassificationEditor(props: EditorPageProps) {
  const tabs = ['ClassificationCatalogSummary', 'ClassificationLayerSummary', 'ClassificationInputs'];
  const activePage = tabs.includes(props.pageId) ? props.pageId : tabs[0];
  return (
    <div className="source-editor-classification">
      <nav className="editor-page-tabs" aria-label={props.language === 'zh' ? '分类页面' : 'Classification pages'}>
        {tabs.map((tab) => <button type="button" key={tab} className={activePage === tab ? 'selected' : ''} aria-current={activePage === tab ? 'page' : undefined} onClick={() => props.onPageChange(tab)}>{pageTitle(tab, props.language)}</button>)}
      </nav>
      {activePage === 'ClassificationInputs' ? <ClassificationLayersEditor {...props} pageId={activePage} />
        : activePage === 'ClassificationLayerSummary' ? <ClassificationLayerSummary {...props} pageId={activePage} />
          : <SchemaPage {...props} pageId={activePage} />}
    </div>
  );
}
