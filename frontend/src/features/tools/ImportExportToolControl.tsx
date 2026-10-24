import { useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { LocalFilesImportSource, type LocalSourceFile } from './LocalFilesImportSource';
import { PlanExporterControl } from './PlanExporterControl';
import { PoiImporter } from './PoiImporter';
import { RasterImporter } from './RasterImporter';
import './exports.css';

type Props = FeaturePageProps;

export function ImportExportToolControl(props: Props) {
  const { language } = props;
  const zh = language === 'zh';
  const [file, setFile] = useState<LocalSourceFile | null>(null);
  return (
    <div className="tools-area tools-export-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '数据交换' : 'DATA EXCHANGE'}</span><h2>{zh ? '导入与导出' : 'Import and export'}</h2>
          <p>{zh ? '检查工程栅格来源，导入本地点数据，并按原 C# 工作流导出计算计划。' : 'Inspect project raster sources, import local point data, and export plans through the source C# workflow.'}</p></div>
        <span className="tools-badge">{zh ? 'React · C# Host' : 'React · C# Host'}</span>
      </section>

      <div className="tools-export-layout">
        <section className="tools-subpanel tools-export-import-panel">
          <div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {zh ? '导入' : 'IMPORT'}</span><h3>{zh ? '栅格与点数据' : 'Raster and point data'}</h3></div>
            <span className="tools-badge">{zh ? '工程上下文' : 'Project context'}</span></div>
          <RasterImporter {...props} />
          <div className="tools-export-local-import">
            <LocalFilesImportSource language={language} onFile={setFile} />
            <PoiImporter language={language} source={file} />
          </div>
        </section>
        <PlanExporterControl language={language} context={props.context} backend={props.backend} />
      </div>
    </div>
  );
}
