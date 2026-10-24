import type { EditorPageProps } from './model';
import { pageTitle } from './model';
import { BoundaryConditionsSummaryPage } from './BoundaryConditionsSummaryPage';
import { BoundaryConditionLineEditorPage } from './BoundaryConditionLineEditorPage';
import { PrecipitationPage } from './PrecipitationPage';
import { EvapotranspirationPage } from './EvapotranspirationPage';
import { WindForcesPage } from './WindForcesPage';
import { WindConstantControl } from './WindConstantControl';
import { AirPressurePage } from './AirPressurePage';
import { AirDensityPage } from './AirDensityPage';
import { SpatialDataSummaryPage } from './SpatialDataSummaryPage';

/** Page IDs retained by EditorWorkbench for the Boundary Conditions source editor. */
export const BOUNDARY_EDITOR_PAGE_IDS = [
  'BoundaryConditionsSummary',
  'BoundaryConditionEditor',
  'PrecipitationControl',
  'SpatialDataSummaryPage',
  'EvapotranspirationControl',
  'WindForcesControl',
  'WindConstantControl',
  'AirPressureControl',
  'AirDensityControl',
] as const;

/** React port of Ras.Interface.WinUI.Controls.BoundaryConditionEditor. */
export function BoundaryConditionEditor(props: EditorPageProps) {
  const tabs = BOUNDARY_EDITOR_PAGE_IDS;
  const activePage = tabs.includes(props.pageId as typeof tabs[number]) ? props.pageId : tabs[0];
  return (
    <div className="source-editor-boundary">
      <nav className="editor-page-tabs" aria-label={props.language === 'zh' ? '边界条件页面' : 'Boundary condition pages'}>
        {tabs.map((tab) => <button type="button" key={tab} className={activePage === tab ? 'selected' : ''} aria-current={activePage === tab ? 'page' : undefined} onClick={() => props.onPageChange(tab)}>{pageTitle(tab, props.language)}</button>)}
      </nav>
      {activePage === 'BoundaryConditionsSummary' ? <BoundaryConditionsSummaryPage {...props} pageId={activePage} />
        : activePage === 'BoundaryConditionEditor' ? <BoundaryConditionLineEditorPage {...props} pageId={activePage} />
          : activePage === 'SpatialDataSummaryPage' ? <SpatialDataSummaryPage {...props} pageId={activePage} />
          : activePage === 'PrecipitationControl' ? <PrecipitationPage {...props} pageId={activePage} />
            : activePage === 'EvapotranspirationControl' ? <EvapotranspirationPage {...props} pageId={activePage} />
              : activePage === 'WindForcesControl' ? <WindForcesPage {...props} pageId={activePage} />
                : activePage === 'WindConstantControl' ? <section className="editor-schema-page editor-meteorology-page"><div className="editor-schema-heading"><div><p className="editor-eyebrow">{props.language === 'zh' ? '风力边界条件' : 'Wind boundary condition'}</p><h2>{pageTitle(activePage, props.language)}</h2></div></div><WindConstantControl {...props} pageId={activePage} /></section>
                  : activePage === 'AirPressureControl' ? <AirPressurePage {...props} pageId={activePage} />
                    : <AirDensityPage {...props} pageId={activePage} />}
    </div>
  );
}
