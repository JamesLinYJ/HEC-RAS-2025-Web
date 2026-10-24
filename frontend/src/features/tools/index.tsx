import { lazy, Suspense, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import styles from './tools.module.css';
import { GeoprocessingToolControl } from './GeoprocessingToolControl';
import { ImportExportToolControl } from './ImportExportToolControl';
import { RasterCalculatorEditor } from './RasterCalculatorEditor';
import { SelectProjectionPage } from './SelectProjectionPage';
const SourceManagerControl = lazy(() => import('./SourceManagerControl').then(module => ({ default: module.SourceManagerControl })));
import { WilcockMainControl } from './hydraulics/WilcockMainControl';
import { UnitCalculatorPage } from './hydraulics/UnitCalculatorPage';
import { USGSTerrainImportControl } from './usgs/USGSTerrainImportControl';

type ToolsPage = 'geoprocessing' | 'import-export' | 'raster-calculator' | 'projection' | 'sources' | 'wilcock' | 'unit-calculator' | 'usgs-terrain';

const pages: Array<{ id: ToolsPage; zh: string; en: string; mark: string }> = [
  { id: 'geoprocessing', zh: '地理处理', en: 'Geoprocessing', mark: '⌁' },
  { id: 'import-export', zh: '导入 / 导出', en: 'Import / Export', mark: '⇄' },
  { id: 'raster-calculator', zh: '栅格计算器', en: 'Raster calculator', mark: '▦' },
  { id: 'projection', zh: '投影', en: 'Projection', mark: '◎' },
  { id: 'sources', zh: '数据源', en: 'Data sources', mark: '▤' },
  { id: 'wilcock', zh: 'Wilcock 输沙', en: 'Wilcock transport', mark: '≈' },
  { id: 'unit-calculator', zh: '单位计算器', en: 'Unit calculator', mark: '↔' },
  { id: 'usgs-terrain', zh: 'USGS 地形', en: 'USGS terrain', mark: '⌖' },
];

/** Web and Electron entry point for the tools workspace. */
export default function ToolsPageView(props: FeaturePageProps) {
  const [active, setActive] = useState<ToolsPage>('geoprocessing');
  const [mountedPages, setMountedPages] = useState<Set<ToolsPage>>(() => new Set(['geoprocessing', 'import-export', 'raster-calculator', 'projection']));
  const zh = props.language === 'zh';

  return (
    <div className={styles.toolsFeatureRoot}>
      <header className={styles.toolsWorkspaceHeader}>
        <div className={styles.toolsWorkspaceTitle}>
          <span className={styles.toolsWorkspaceMark} aria-hidden="true">⌘</span>
          <div>
            <span className={styles.toolsWorkspaceEyebrow}>{zh ? '空间数据工作区' : 'SPATIAL DATA WORKSPACE'}</span>
            <h1>{zh ? '工具' : 'Tools'}</h1>
          </div>
        </div>
        <p>{zh ? '地理处理、导入导出、栅格表达式与坐标参考系。' : 'Geoprocessing, data exchange, raster expressions, and coordinate reference systems.'}</p>
      </header>

      <nav className={styles.toolsWorkspaceNav} aria-label={zh ? '工具页面' : 'Tools pages'} role="tablist">
        {pages.map(page => (
          <button
            type="button"
            role="tab"
            aria-selected={active === page.id}
            aria-controls={`tools-panel-${page.id}`}
            id={`tools-tab-${page.id}`}
            className={active === page.id ? styles.toolsWorkspaceTabActive : styles.toolsWorkspaceTab}
            key={page.id}
            onClick={() => {
              setActive(page.id);
              setMountedPages(current => current.has(page.id) ? current : new Set(current).add(page.id));
            }}
          >
            <span aria-hidden="true">{page.mark}</span>{zh ? page.zh : page.en}
          </button>
        ))}
      </nav>

      <div className={styles.toolsWorkspaceContent}>
        <section id="tools-panel-geoprocessing" role="tabpanel" aria-labelledby="tools-tab-geoprocessing" hidden={active !== 'geoprocessing'}>
          <GeoprocessingToolControl {...props} />
        </section>
        <section id="tools-panel-import-export" role="tabpanel" aria-labelledby="tools-tab-import-export" hidden={active !== 'import-export'}>
          <ImportExportToolControl {...props} />
        </section>
        <section id="tools-panel-raster-calculator" role="tabpanel" aria-labelledby="tools-tab-raster-calculator" hidden={active !== 'raster-calculator'}>
          <RasterCalculatorEditor {...props} />
        </section>
        <section id="tools-panel-projection" role="tabpanel" aria-labelledby="tools-tab-projection" hidden={active !== 'projection'}>
          <SelectProjectionPage {...props} />
        </section>
        <section id="tools-panel-sources" role="tabpanel" aria-labelledby="tools-tab-sources" hidden={active !== 'sources'}>
          {mountedPages.has('sources') && <Suspense fallback={<p role="status">{zh ? '正在加载数据源管理…' : 'Loading data sources…'}</p>}>
            <SourceManagerControl {...props} />
          </Suspense>}
        </section>
        <section id="tools-panel-wilcock" role="tabpanel" aria-labelledby="tools-tab-wilcock" hidden={active !== 'wilcock'}>
          {mountedPages.has('wilcock') && <WilcockMainControl {...props} />}
        </section>
        <section id="tools-panel-unit-calculator" role="tabpanel" aria-labelledby="tools-tab-unit-calculator" hidden={active !== 'unit-calculator'}>
          {mountedPages.has('unit-calculator') && <UnitCalculatorPage {...props} />}
        </section>
        <section id="tools-panel-usgs-terrain" role="tabpanel" aria-labelledby="tools-tab-usgs-terrain" hidden={active !== 'usgs-terrain'}>
          {mountedPages.has('usgs-terrain') && <USGSTerrainImportControl {...props} />}
        </section>
      </div>
    </div>
  );
}
