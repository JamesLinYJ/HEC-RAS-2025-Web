import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import { BackendOperationUnavailableError } from '../../shared/workbench';
import type { MapDocumentTab } from './mapTypes';
import { projectXmlSha } from './ProjectTreeListener';
import type { MapDocumentReceipt } from './ProjectTreeListener';

export interface MapSourceImportReport {
  receipts: MapDocumentReceipt[];
  imported: string[];
  failures: { fileName: string; error: unknown }[];
}

/** Copies browser-selected files, then commits each through the original map importer. */
export async function importMapSources(backend: BackendGateway, context: WorkbenchContext,
  document: MapDocumentTab | null, visualizationRevision: string | null, files: File[], signal: AbortSignal,
  options?: { directory?: boolean },
): Promise<MapSourceImportReport> {
  if (!backend.uploadMapSources || !context.projectPath || !context.projectRevision) {
    throw new Error('请先打开工程，并连接支持图层上传的服务。');
  }
  const uploaded = await backend.uploadMapSources(context, files, { signal, directory: options?.directory });
  if (signal.aborted) throw new DOMException('Aborted', 'AbortError');
  if (uploaded.projectPath !== context.projectPath || uploaded.projectRevision !== context.projectRevision ||
      uploaded.projectSessionID !== context.projectSessionID) throw new Error('上传的来源不属于当前工程版本。');
  const projectXmlSHA = await projectXmlSha(context.projectXml);
  const report: MapSourceImportReport = { receipts: [], imported: [], failures: [] };
  let currentDocument = document;
  let currentRevision = visualizationRevision;
  for (const source of uploaded.sources) {
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError');
    const map = {
      projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA,
      expectedVisualizationRevision: currentRevision, kind: currentDocument?.mapType ?? 'Project',
      mapID: currentDocument?.mapID, mapIndex: currentDocument?.mapIndex,
      mapName: currentDocument?.name, mapType: currentDocument?.mapType,
    };
    try {
      let method: string;
      let parameters: unknown;
      if (source.kind === 'gdb') {
        const catalog = await backend.request<{ source: { sourcePath: string; revision: string; files: unknown[] } }>(
          'map.gdb.catalog', { sourcePath: source.sourcePath, expectedSourceRevision: source.sourceRevision }, { signal });
        if (catalog.source.sourcePath !== source.sourceCanonicalPath ||
            catalog.source.revision !== source.sourceRevision || !Array.isArray(catalog.source.files) || !catalog.source.files.length) {
          throw new Error('地理数据库目录的检查回执与上传版本不一致。');
        }
        method = 'map.gdb.import';
        parameters = { map, expectedProjectRevision: context.projectRevision, source: catalog.source };
      } else if (source.kind === 'shapefile') {
        const family = await backend.request('map.vector.captureShapefile', { sourcePath: source.sourcePath }, { signal });
        method = 'map.vector.importShapefile';
        parameters = { map, sourcePath: source.sourcePath, expectedProjectRevision: context.projectRevision, expectedFamily: family };
      } else if (source.kind === 'raster') {
        const raster = await backend.request<{ sourcePath: string; sourceCanonicalPath: string; sourceRevision: string; mapLayerType: string }>(
          'map.raster.layer.inspect', { sourcePath: source.sourcePath }, { signal });
        if (raster.sourcePath !== source.sourcePath || !raster.sourceRevision || !raster.mapLayerType ||
            raster.sourceCanonicalPath !== source.sourceCanonicalPath) throw new Error('栅格来源检查与上传回执不一致。');
        method = 'map.raster.layer.import';
        parameters = { map, sourcePath: source.sourcePath, sourceRevision: raster.sourceRevision,
          mapLayerType: raster.mapLayerType, expectedProjectRevision: context.projectRevision,
          expectedSourceCanonicalPath: raster.sourceCanonicalPath };
      } else {
        method = 'map.vector.import';
        parameters = { map, sourcePath: source.sourcePath, sourceRevision: source.sourceRevision,
          expectedProjectRevision: context.projectRevision, expectedSourceCanonicalPath: source.sourceCanonicalPath };
      }
      if (!backend.supports(method)) throw new BackendOperationUnavailableError(method);
      const value = await backend.request<MapDocumentReceipt | { receipt: MapDocumentReceipt }>(method, parameters, { signal });
      const receipt = source.kind === 'gdb' && 'receipt' in value ? value.receipt : value as MapDocumentReceipt;
      if (receipt.previousVisualizationRevision !== currentRevision || !/^[a-f0-9]{64}$/i.test(receipt.visualizationRevision)) {
        throw new Error('图层提交回执的地图版本不一致，请重新读取工程地图。');
      }
      currentRevision = receipt.visualizationRevision;
      currentDocument = { id: currentDocument?.id ?? crypto.randomUUID(), mapID: currentDocument?.mapID ?? crypto.randomUUID(),
        mapIndex: receipt.mapIndex, name: receipt.mapName, mapType: receipt.mapType,
        visualizationRevision: currentRevision, roots: [] };
      report.receipts.push(receipt);
      report.imported.push(source.fileName);
    } catch (error) {
      if (signal.aborted) throw error;
      report.failures.push({ fileName: source.fileName, error });
      // A failed mutation can have an unconfirmed receipt. Re-read before any
      // later write instead of retrying with a possibly obsolete .vis revision.
      break;
    }
  }
  return report;
}
