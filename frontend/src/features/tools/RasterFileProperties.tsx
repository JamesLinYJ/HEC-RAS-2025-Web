import type { Language } from '../../shared/workbench';
import type { RasterImportInfo } from './RasterImporter';

interface RasterFilePropertiesProps {
  language: Language;
  info: RasterImportInfo | null | undefined;
}

function valueOrUnavailable(value: string | number | null | undefined): string {
  return value === undefined || value === null || value === '' ? '—' : String(value);
}

export function RasterFileProperties({ language, info }: RasterFilePropertiesProps) {
  const zh = language === 'zh';
  return <details className="gp-ui-raster-property-group" open>
    <summary><span>{zh ? '栅格文件属性' : 'Raster file properties'}</span><small>RasterFileProperties.cs</small></summary>
    <dl className="gp-ui-raster-info">
      <div><dt>{zh ? '文件名' : 'File name'} <code>FileName</code></dt><dd>{valueOrUnavailable(info?.fileName)}</dd></div>
      <div><dt>{zh ? '目录' : 'Directory'} <code>Directory</code></dt><dd>{valueOrUnavailable(info?.directory)}</dd></div>
      <div><dt>{zh ? '像元大小' : 'Cell size'} <code>CellSize</code></dt><dd>{valueOrUnavailable(info?.cellSize)}</dd></div>
      {info?.unitSystem !== null && info?.unitSystem !== undefined && <div><dt>{zh ? '单位制' : 'Unit system'} <code>UnitSystem</code></dt><dd>{info.unitSystem}</dd></div>}
      <div><dt>{zh ? '文件大小' : 'File size'} <code>Size</code></dt><dd>{valueOrUnavailable(info?.size)}</dd></div>
      <div><dt>{zh ? '未压缩大小' : 'Uncompressed size'} <code>UncompressedSize</code></dt><dd>{valueOrUnavailable(info?.uncompressedSize)}</dd></div>
      <div><dt>{zh ? '压缩比' : 'Compression ratio'} <code>CompressionRatio</code></dt><dd>{valueOrUnavailable(info?.compressionRatio)}</dd></div>
      <div><dt>{zh ? '驱动' : 'Driver'} <code>Driver</code></dt><dd>{valueOrUnavailable(info?.driver)}</dd></div>
      <div><dt>{zh ? '波段数' : 'Band count'} <code>BandCount</code></dt><dd>{valueOrUnavailable(info?.bandCount)}</dd></div>
      <div><dt>{zh ? '金字塔层级' : 'Overviews'} <code>Overviews</code></dt><dd>{(info?.overviews ?? []).join(', ') || '—'}</dd></div>
    </dl>
  </details>;
}
