import type { Language } from '../../shared/workbench';
import type { RasterImportInfo } from './RasterImporter';

interface RasterSummaryPropertiesProps {
  language: Language;
  info: RasterImportInfo | null | undefined;
}

function valueOrUnavailable(value: string | null | undefined): string {
  return value === undefined || value === null || value === '' ? '—' : value;
}

export function RasterSummaryProperties({ language, info }: RasterSummaryPropertiesProps) {
  const zh = language === 'zh';
  return <details className="gp-ui-raster-property-group" open>
    <summary><span>{zh ? '栅格摘要' : 'Raster summary'}</span><small>RasterSummaryProperties.cs</small></summary>
    <dl className="gp-ui-raster-info">
      <div><dt>{zh ? '文件名' : 'File name'} <code>FileName</code></dt><dd>{valueOrUnavailable(info?.fileName)}</dd></div>
      <div><dt>{zh ? '投影说明' : 'Projection description'} <code>ProjectionFriendlyText</code></dt><dd>{valueOrUnavailable(info?.projectionFriendlyText)}</dd></div>
      <div><dt>{zh ? '栅格投影' : 'Raster projection'} <code>RasterProjection</code></dt><dd>{valueOrUnavailable(info?.rasterProjection)}</dd></div>
      <div><dt>{zh ? '像元大小' : 'Cell size'} <code>CellSize</code></dt><dd>{valueOrUnavailable(info?.cellSize)}</dd></div>
    </dl>
  </details>;
}
