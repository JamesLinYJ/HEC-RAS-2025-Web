import type { Language } from '../../shared/workbench';
import type { RasterImportStats } from './RasterImporter';

interface RasterStatPropertiesProps {
  language: Language;
  stats: RasterImportStats | null | undefined;
}

export function RasterStatProperties({ language, stats }: RasterStatPropertiesProps) {
  const zh = language === 'zh';
  if (!stats) return null;
  return <details className="gp-ui-raster-property-group" open>
    <summary><span>{zh ? '栅格统计' : 'Raster statistics'}</span><small>RasterStatProperties.cs</small></summary>
    <dl className="gp-ui-raster-stats">
      <div><dt>{zh ? '最小值' : 'Minimum'} <code>Min</code></dt><dd>{stats.min}</dd></div>
      <div><dt>{zh ? '最大值' : 'Maximum'} <code>Max</code></dt><dd>{stats.max}</dd></div>
      <div><dt>{zh ? '平均值' : 'Mean'} <code>Mean</code></dt><dd>{stats.mean}</dd></div>
      <div><dt>{zh ? '标准差' : 'Standard deviation'} <code>StandardDeviation</code></dt><dd>{stats.standardDeviation}</dd></div>
    </dl>
  </details>;
}
