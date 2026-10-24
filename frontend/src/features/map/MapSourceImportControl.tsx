import { useRef } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export function MapSourceImportControl({ language, enabled, busy, onImport }: {
  language: Language; enabled: boolean; busy: boolean; onImport: (files: File[], options?: { directory?: boolean }) => Promise<void>;
}) {
  const input = useRef<HTMLInputElement>(null);
  const directoryInput = useRef<HTMLInputElement>(null);
  return <div className="map-source-import-control">
    <button type="button" data-testid="map-import-source" className="map-import-button" disabled={!enabled || busy}
      onClick={() => input.current?.click()} title={text(language, '将 GeoJSON、Shapefile 或 GeoTIFF 保存为当前工程地图的图层。Shapefile 需同时选择 .shp、.shx、.dbf 和可选 .prj。', 'Save GeoJSON, a Shapefile family, or GeoTIFF into the project map. Include .shp, .shx, .dbf and optional .prj together.')}>
      {busy ? text(language, '正在导入工程图层…', 'Importing project layers…') : text(language, '导入工程图层', 'Import project layers')}
    </button>
    <button type="button" data-testid="map-import-gdb" className="map-import-button" disabled={!enabled || busy}
      onClick={() => directoryInput.current?.click()} title={text(language, '选择完整的 .gdb 目录，将数据库文件保存在工程中，再读取其矢量和栅格子图层。', 'Choose a complete .gdb directory to retain its files in the project and load its vector and raster layers.')}>
      {text(language, '导入 GDB 目录', 'Import GDB directory')}
    </button>
    <input ref={input} type="file" multiple className="sr-only" data-testid="map-import-source-input"
      accept=".geojson,.json,.shp,.shx,.dbf,.prj,.tif,.tiff" onChange={(event) => {
        const files = Array.from(event.currentTarget.files ?? []);
        event.currentTarget.value = '';
        if (files.length > 0) void onImport(files);
      }} />
    <input ref={directoryInput} type="file" multiple {...{ webkitdirectory: '', directory: '' }} className="sr-only" data-testid="map-import-gdb-input"
      onChange={(event) => {
        const files = Array.from(event.currentTarget.files ?? []);
        event.currentTarget.value = '';
        if (files.length > 0) void onImport(files, { directory: true });
      }} />
  </div>;
}
