import { useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { LocalFilesImportSource, type LocalSourceFile } from './LocalFilesImportSource';
import { PoiImporter } from './PoiImporter';
import { RasterImporter } from './RasterImporter';
import { DepthShapeExporterDialog } from './exports/DepthShapeExporterDialog';
import { RgbTiffExporterDialog } from './exports/RgbTiffExporterDialog';
import { PlanExporterDialog } from './PlanExporterDialog';
import { ReprojectionToolDialog } from './ReprojectionToolDialog';
import './exports.css';
import './exports/exports.css';

type Props = FeaturePageProps;

export function ImportExportToolControl(props: Props) {
  const { language } = props;
  const zh = language === 'zh';
  const [file, setFile] = useState<LocalSourceFile | null>(null);
  const [depthDialogOpen, setDepthDialogOpen] = useState(false);
  const [rgbDialogOpen, setRgbDialogOpen] = useState(false);
  const [planDialogOpen, setPlanDialogOpen] = useState(false);
  const [reprojectionDialogOpen, setReprojectionDialogOpen] = useState(false);
  return (
    <div className="tools-area tools-export-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '数据交换' : 'DATA EXCHANGE'}</span><h2>{zh ? '导入与导出' : 'Import and export'}</h2>
          <p>{zh ? '检查并导入工程栅格与点数据，导出计算计划、深度等值线和 RGB 栅格。' : 'Inspect and import project raster and point data; export plans, depth contours, and RGB rasters.'}</p></div>
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
        <div className="tools-export-sidepanels">
          <section className="tools-subpanel tools-export-plan-launcher">
            <div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {zh ? '计划导出' : 'PLAN EXPORT'}</span><h3>{zh ? '导出计算计划' : 'Export a plan'}</h3></div>
              <span className="tools-badge">C# Host</span></div>
            <p className="tools-export-callout">{zh
              ? '从 Host 当前工程的计划目录选择计划。检查会固定计划源修订、工程会话和目标路径；只有点击导出才写入服务器。'
              : 'Choose a plan from the active project’s Host catalog. Inspection binds the source revision, project session, and destination; server output is written only after you click Export.'}</p>
            <button type="button" className="tools-button tools-button-primary" onClick={() => setPlanDialogOpen(true)}>
              {zh ? '打开计划导出…' : 'Open plan export…'}
            </button>
          </section>
          <section className="tools-subpanel tools-export-plan-launcher">
            <div className="tools-subpanel-heading"><div><span className="tools-kicker">02 / {zh ? '坐标工具' : 'COORDINATE TOOLS'}</span><h3>{zh ? '投影检查与坐标转换' : 'Projection and coordinate tools'}</h3></div>
              <span className="tools-badge">C# Host</span></div>
            <p className="tools-export-callout">{zh
              ? '对当前资产调用 Host 投影识别，或将逐行坐标交由 C# 投影服务转换。此工具不会写入文件。'
              : 'Identify the selected asset’s projection through the Host or transform coordinate rows through the C# service. This tool does not write files.'}</p>
            <button type="button" className="tools-button tools-button-secondary" onClick={() => setReprojectionDialogOpen(true)}>
              {zh ? '打开坐标转换表…' : 'Open coordinate table…'}
            </button>
          </section>
          <section className="tools-subpanel tools-raster-exports-panel">
            <div className="tools-subpanel-heading"><div><span className="tools-kicker">03 / {zh ? '栅格导出' : 'RASTER EXPORT'}</span>
              <h3>{zh ? '结果与栅格图像' : 'Results and raster images'}</h3></div>
              <span className="tools-badge">C# Host</span></div>
            <p className="tools-export-callout">{zh
              ? '选择已保存地图中的 Result 与单波段栅格源。先检查和预览，只有点击导出后才会写入服务器路径。'
              : 'Choose Result and single-band raster sources from saved maps. Inspect and preview first; server files are written only after you click Export.'}</p>
            <div className="tools-export-actions">
              <button type="button" className="tools-button tools-button-secondary" onClick={() => setDepthDialogOpen(true)}>
                {zh ? '深度等值线 Shapefile…' : 'Depth contour Shapefile…'}
              </button>
              <button type="button" className="tools-button tools-button-secondary" onClick={() => setRgbDialogOpen(true)}>
                {zh ? 'RGB GeoTIFF…' : 'RGB GeoTIFF…'}
              </button>
            </div>
          </section>
        </div>
      </div>
      <PlanExporterDialog open={planDialogOpen} backend={props.backend} context={props.context} language={language} onClose={() => setPlanDialogOpen(false)} />
      <ReprojectionToolDialog open={reprojectionDialogOpen} backend={props.backend} context={props.context} language={language}
        onClose={() => setReprojectionDialogOpen(false)} onDirtyStateChange={props.onDirtyStateChange} />
      <DepthShapeExporterDialog open={depthDialogOpen} backend={props.backend} context={props.context} language={language} onClose={() => setDepthDialogOpen(false)} />
      <RgbTiffExporterDialog open={rgbDialogOpen} backend={props.backend} context={props.context} language={language} onClose={() => setRgbDialogOpen(false)} />
    </div>
  );
}
