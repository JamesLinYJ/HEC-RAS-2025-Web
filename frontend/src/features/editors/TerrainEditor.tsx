import type { EditorPageProps } from './model';
import { pageTitle } from './model';
import { TerrainProperties } from './TerrainProperties';
import { TerrainLayersEditor } from './TerrainLayersEditor';

/** React port of Ras.Visual.WinUI.Controls.TerrainEditor. */
export function TerrainEditor(props: EditorPageProps) {
  const tabs = ['TerrainProperties', 'TerrainLayersEditor'];
  const activePage = tabs.includes(props.pageId) ? props.pageId : tabs[0];
  return (
    <div className="source-editor-terrain">
      <nav className="editor-page-tabs" aria-label={props.language === 'zh' ? '地形页面' : 'Terrain pages'}>
        {tabs.map((tab) => <button type="button" key={tab} className={activePage === tab ? 'selected' : ''} aria-current={activePage === tab ? 'page' : undefined} onClick={() => props.onPageChange(tab)}>{pageTitle(tab, props.language)}</button>)}
      </nav>
      {activePage === 'TerrainLayersEditor' ? <TerrainLayersEditor {...props} pageId={activePage} /> : <TerrainProperties {...props} pageId={activePage} />}
    </div>
  );
}
