import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps, UploadedMapSource } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { loadMapDocumentCatalog, projectXmlSha, type LoadedMapDocumentCatalog, type MapDocumentReceipt } from '../map/ProjectTreeListener';
import { rgbaFrameImage } from '../map/mapImages';
import type { CatalogTreeNode, MapDocumentTab } from '../map/mapTypes';
import { RasterFileProperties } from './RasterFileProperties';
import { RasterStatProperties } from './RasterStatProperties';
import { RasterSummaryProperties } from './RasterSummaryProperties';
import { readProjectProjection } from './geoprocessingUi';
import './geoprocessingUi.css';

export interface RasterImportInfo {
  fileName?: string | null;
  directory?: string | null;
  rasterProjection?: string | null;
  projectionFriendlyText?: string | null;
  overviewsCount?: number | null;
  overviews?: string[] | null;
  cellSize?: string | null;
  size?: string | null;
  uncompressedSize?: string | null;
  compressionRatio?: string | null;
  driver?: string | null;
  bandCount?: number | null;
  unitSystem?: string | null;
}

export interface RasterImportStats {
  min: string;
  max: string;
  mean: string;
  standardDeviation: string;
}

interface RasterContextSource {
  path: string;
  revision: string;
  info?: RasterImportInfo | null;
  stats?: RasterImportStats | null;
  error?: string | null;
}

interface RasterLayerInspection {
  sourcePath: string;
  sourceCanonicalPath: string;
  sourceRevision: string;
  driver: string;
  dataType: string;
  width: number;
  height: number;
  bandCount: number;
  mapLayerType: string;
  supportedMapLayerTypes: string[];
  projectionWKT?: string | null;
  unitSystem?: string | null;
  cellSizeX: number;
  canRenderPreview: boolean;
  info: RasterImportInfo;
  stats: RasterImportStats;
}

interface RasterPreviewPayload {
  id: string;
  sourceMapID?: string | null;
  name: string;
  width: number;
  height: number;
  rgba: string;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

interface InspectedRaster {
  uploaded: UploadedMapSource;
  context: RasterContextSource;
  layer: RasterLayerInspection;
}

interface PendingRasterImport {
  mapIndex: number;
  mapName: string | null;
  mapType: string;
  previousVisualizationRevision: string | null;
}

interface RasterImporterProps extends FeaturePageProps {
  onImportCommitted?: (receipts: MapDocumentReceipt[]) => void;
}

const importableMapTypes = new Set(['Project', 'Buildable', 'Geometry', 'Plan']);

function mapDocumentKey(document: MapDocumentTab): string {
  return `${document.mapIndex}:${document.name}:${document.mapType}`;
}

function containsRasterSource(nodes: readonly CatalogTreeNode[], sourcePath: string, mapLayerType: string): boolean {
  return nodes.some(node => {
    const locator = node.locator;
    const exactSource = locator?.filename === sourcePath || node.assetID === sourcePath;
    if (locator?.nodeType === 'MapLayer' && locator.mapType === mapLayerType && exactSource) return true;
    return containsRasterSource(node.children ?? [], sourcePath, mapLayerType);
  });
}

function validateUploadedSource(source: UploadedMapSource, context: FeaturePageProps['context'], language: FeaturePageProps['language']): void {
  if (source.kind !== 'raster' || !source.sourcePath || !source.sourceCanonicalPath || !/^[a-f0-9]{64}$/i.test(source.sourceRevision)) {
    throw new Error(text(language, '栅格上传回执缺少文件路径、规范路径或 SHA-256 修订。', 'The raster upload receipt is missing its path, canonical path, or SHA-256 revision.'));
  }
  if (!context.projectPath || !context.projectRevision) throw new Error(text(language, '当前工程缺少可验证的路径或修订。', 'The current project has no verifiable path or revision.'));
}

export function RasterImporter({ language, context, backend, onDirtyStateChange, onImportCommitted }: RasterImporterProps) {
  const zh = language === 'zh';
  const [file, setFile] = useState<File>();
  const [inspected, setInspected] = useState<InspectedRaster>();
  const [catalog, setCatalog] = useState<LoadedMapDocumentCatalog>();
  const [selectedMapKey, setSelectedMapKey] = useState('');
  const [busyAction, setBusyAction] = useState<'inspect' | 'import' | 'refresh' | null>(null);
  const [progress, setProgress] = useState<unknown>();
  const [error, setError] = useState<string>();
  const [receipt, setReceipt] = useState<MapDocumentReceipt>();
  const [readback, setReadback] = useState<'confirmed' | 'incomplete'>();
  const controller = useRef<AbortController | null>(null);
  const requestGeneration = useRef(0);
  const pendingImport = useRef<PendingRasterImport | null>(null);
  const [preview, setPreview] = useState<{ imageSrc: string; raster: RasterPreviewPayload }>();
  const [previewError, setPreviewError] = useState<string>();
  const [postCancelStatus, setPostCancelStatus] = useState<string>();
  const canInspect = backend.supports('raster.import.context.inspect') && backend.supports('map.raster.layer.inspect');
  const canImport = backend.supports('map.raster.layer.import') && backend.supports('map.document.list') && backend.supports('map.tree.load');
  const eligibleMaps = useMemo(() => catalog?.documents.filter(document => importableMapTypes.has(document.mapType)) ?? [], [catalog]);
  const selectedMap = eligibleMaps.find(document => mapDocumentKey(document) === selectedMapKey) ?? null;
  const dirty = Boolean(file || inspected) && !receipt && readback !== 'confirmed';

  useEffect(() => {
    onDirtyStateChange?.('tools.raster-importer', dirty);
    return () => onDirtyStateChange?.('tools.raster-importer', false);
  }, [dirty, onDirtyStateChange]);

  useEffect(() => {
    requestGeneration.current += 1;
    controller.current?.abort();
    controller.current = null;
    setFile(undefined);
    setInspected(undefined);
    setCatalog(undefined);
    setSelectedMapKey('');
    setBusyAction(null);
    setProgress(undefined);
    setError(undefined);
    setReceipt(undefined);
    setReadback(undefined);
    pendingImport.current = null;
    setPreview(undefined);
    setPreviewError(undefined);
    setPostCancelStatus(undefined);
  }, [context.projectPath, context.projectRevision, context.projectSessionID]);

  useEffect(() => () => {
    requestGeneration.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const setNewFile = (next: File | undefined): void => {
    requestGeneration.current += 1;
    controller.current?.abort();
    controller.current = null;
    setBusyAction(null);
    setFile(next);
    setInspected(undefined);
    setCatalog(undefined);
    setSelectedMapKey('');
    setError(undefined);
    setReceipt(undefined);
    setReadback(undefined);
    pendingImport.current = null;
    setPreview(undefined);
    setPreviewError(undefined);
    setPostCancelStatus(undefined);
    setProgress(undefined);
  };

  const beginRequest = (): { controller: AbortController; generation: number } => {
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    const generation = ++requestGeneration.current;
    return { controller: requestController, generation };
  };

  const inspectRaster = async (): Promise<void> => {
    if (!file) return;
    const projectPath = context.projectPath;
    const projectRevision = context.projectRevision;
    if (!projectPath || !projectRevision || !backend.uploadMapSources) {
      setError(text(language, '请先打开工程，并连接支持栅格上传的 C# 服务。', 'Open a project and connect a C# service that supports raster uploads.'));
      return;
    }
    if (!canInspect) {
      setError(text(language, '栅格属性检查接口尚未注册。', 'Raster inspection operations are not registered.'));
      return;
    }
    const extension = file.name.split('.').at(-1)?.toLowerCase();
    if (extension !== 'tif' && extension !== 'tiff') {
      setError(text(language, '当前地图导入接口只接受 TIFF 栅格。', 'The current map import operation accepts TIFF rasters only.'));
      return;
    }
    const { controller: requestController, generation } = beginRequest();
    setBusyAction('inspect');
    setError(undefined);
    setInspected(undefined);
    setReceipt(undefined);
    setReadback(undefined);
    setPreview(undefined);
    setPreviewError(undefined);
    setPostCancelStatus(undefined);
    setProgress(undefined);
    try {
      const uploaded = await backend.uploadMapSources(context, [file], { signal: requestController.signal });
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      if (uploaded.projectPath !== context.projectPath || uploaded.projectRevision !== context.projectRevision ||
          uploaded.projectSessionID !== context.projectSessionID || uploaded.sources.length !== 1) {
        throw new Error(text(language, '上传来源与当前工程会话或工程修订不匹配。', 'The uploaded source does not match the active project session or revision.'));
      }
      const source = uploaded.sources[0]!;
      validateUploadedSource(source, context, language);

      let compareProjectionWKT: string | undefined;
      const projectDefinition = readProjectProjection(context.projectXml);
      if (projectDefinition && backend.supports('projection.evaluate')) {
        try {
          const evaluation = await backend.request<{ isValid: boolean; wkt?: string | null }>(
            'projection.evaluate', projectDefinition, { signal: requestController.signal },
          );
          if (evaluation.isValid && evaluation.wkt) compareProjectionWKT = evaluation.wkt;
        } catch { /* The property read remains useful without a project-projection comparison. */ }
      }

      const contextSnapshot = await backend.request<{ sources: RasterContextSource[] }>(
        'raster.import.context.inspect',
        { filePaths: [source.sourceCanonicalPath], ...(compareProjectionWKT ? { compareProjectionWKT } : {}) },
        { signal: requestController.signal },
      );
      if (!Array.isArray(contextSnapshot.sources) || contextSnapshot.sources.length !== 1) {
        throw new Error(text(language, '栅格属性接口未返回唯一来源。', 'The raster property service did not return exactly one source.'));
      }
      const contextSource = contextSnapshot.sources[0]!;
      if (contextSource.path !== source.sourceCanonicalPath || contextSource.revision.toUpperCase() !== source.sourceRevision.toUpperCase()) {
        throw new Error(text(language, '栅格检查来源或 SHA-256 修订与上传回执不一致。', 'The inspected raster path or SHA-256 revision does not match the upload receipt.'));
      }
      if (contextSource.error) throw new Error(contextSource.error);

      const layer = await backend.request<RasterLayerInspection>(
        'map.raster.layer.inspect', { sourcePath: source.sourcePath }, { signal: requestController.signal },
      );
      if (layer.sourceCanonicalPath !== source.sourceCanonicalPath ||
          layer.sourceRevision.toUpperCase() !== source.sourceRevision.toUpperCase() ||
          layer.sourcePath !== source.sourcePath) {
        throw new Error(text(language, '地图栅格检查与上传来源身份不一致。', 'The map raster inspection does not match the uploaded source identity.'));
      }
      const loadedCatalog = await loadMapDocumentCatalog(backend, context, { signal: requestController.signal });
      if (loadedCatalog.projectRevision !== context.projectRevision) throw new Error(text(language, '读取地图时工程修订已变化。', 'The project revision changed while loading map documents.'));
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      setInspected({ uploaded: source, context: contextSource, layer });
      setCatalog(loadedCatalog);
      const available = loadedCatalog.documents.filter(document => importableMapTypes.has(document.mapType));
      const defaultMap = available.find(document => document.mapIndex === loadedCatalog.activeMapIndex) ?? available[0];
      setSelectedMapKey(defaultMap ? mapDocumentKey(defaultMap) : '');
      if (available.length !== loadedCatalog.documents.length) {
        setError(text(language, '部分地图类型不支持栅格图层导入；仅显示 Project、Buildable、Geometry 和 Plan 地图。', 'Some map types cannot accept raster layers. Only Project, Buildable, Geometry, and Plan maps are listed.'));
      }
      if (!layer.canRenderPreview) {
        setPreviewError(text(language, '该栅格超过原控件的预览尺寸限制。', 'This raster exceeds the original control’s preview-size limit.'));
      } else if (!backend.supports('map.raster.layer.preview')) {
        setPreviewError(text(language, '当前 C# 服务未注册 map.raster.layer.preview。', 'The current C# service has not registered map.raster.layer.preview.'));
      } else {
        try {
          const previewResult = await backend.request<{ raster: RasterPreviewPayload }>(
            'map.raster.layer.preview',
            {
              sourcePath: source.sourcePath,
              sourceRevision: source.sourceRevision,
              mapLayerType: layer.mapLayerType,
              projectPath,
              projectXml: context.projectXml,
              projectXmlSHA: await projectXmlSha(context.projectXml),
            },
            { signal: requestController.signal, onProgress: value => setProgress(value) },
          );
          if (requestController.signal.aborted || generation !== requestGeneration.current) return;
          const raster = previewResult?.raster;
          if (!raster || raster.id !== `preview:${source.sourceRevision}` ||
              typeof raster.rgba !== 'string' || ![raster.minX, raster.minY, raster.maxX, raster.maxY].every(Number.isFinite)) {
            throw new Error(text(language, '服务端预览回执缺少有效栅格帧或来源修订。', 'The preview response is missing a valid raster frame or source revision.'));
          }
          setPreview({ imageSrc: rgbaFrameImage(raster.rgba, raster.width, raster.height), raster });
          setPreviewError(undefined);
        } catch (previewRequestError) {
          if (requestController.signal.aborted || generation !== requestGeneration.current) return;
          setPreviewError(message(previewRequestError, language));
        }
      }
    } catch (requestError) {
      if (!requestController.signal.aborted && generation === requestGeneration.current) setError(message(requestError, language));
    } finally {
      if (generation === requestGeneration.current) {
        setBusyAction(null);
        if (controller.current === requestController) controller.current = null;
      }
    }
  };

  const refreshCatalog = async (signal: AbortSignal): Promise<LoadedMapDocumentCatalog> => {
    const value = await loadMapDocumentCatalog(backend, context, { signal });
    if (value.projectRevision !== context.projectRevision) throw new Error(text(language, '工程修订在读取地图期间发生变化。', 'The project revision changed while reading maps.'));
    return value;
  };

  const verifyCancelledImport = async (): Promise<void> => {
    if (!inspected || !backend.supports('map.raster.layer.inspect')) return;
    const pending = pendingImport.current;
    if (!pending) {
      setReadback('incomplete');
      setPostCancelStatus(text(language,
        '取消时尚未发出栅格写入请求，且没有待核对的目标地图修订；回读不能证明本次提交。',
        'The raster write request had not been sent when cancellation occurred, and there is no pending target-map revision to verify. Read-back cannot prove a commit.'));
      return;
    }
    const { controller: requestController, generation } = beginRequest();
    setBusyAction('refresh');
    setError(undefined);
    try {
      const latest = await refreshCatalog(requestController.signal);
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      const targetMap = latest.documents.find(document => document.mapIndex === pending.mapIndex &&
        document.mapType === pending.mapType && (pending.mapName === null || document.name === pending.mapName));
      if (!targetMap || latest.visualizationRevision === null ||
          latest.visualizationRevision === pending.previousVisualizationRevision) {
        setCatalog(latest);
        setReadback('incomplete');
        setPostCancelStatus(text(language,
          '目标地图不存在，或地图可视化修订没有变化；回读无法证明本次导入已提交。',
          'The target map is missing or its visualization revision did not change. Read-back cannot prove this import committed.'));
        return;
      }
      if (containsRasterSource(targetMap.roots, inspected.uploaded.sourcePath, inspected.layer.mapLayerType) === false) {
        setCatalog(latest);
        setReadback('incomplete');
        setPostCancelStatus(text(language,
          `目标地图 ${targetMap.name} 的修订已变化，但回读没有找到当前来源路径和图层类型；无法确认本次栅格导入。`,
          `Target map ${targetMap.name} changed revision, but read-back did not find this source path and layer type. The raster import is unconfirmed.`));
        return;
      }
      const source = await backend.request<RasterLayerInspection>(
        'map.raster.layer.inspect', { sourcePath: inspected.uploaded.sourcePath }, { signal: requestController.signal },
      );
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      if (source.sourceCanonicalPath !== inspected.uploaded.sourceCanonicalPath ||
          source.sourceRevision.toUpperCase() !== inspected.uploaded.sourceRevision.toUpperCase() ||
          source.mapLayerType !== inspected.layer.mapLayerType) {
        throw new Error(text(language, '地图目录包含该路径，但文件 SHA-256 修订或图层类型与上传回执不匹配。', 'The catalog contains the source path, but its SHA-256 revision or map layer type does not match the upload receipt.'));
      }
      setCatalog(latest);
      setReadback('confirmed');
      setPostCancelStatus(text(language,
        `地图目录回读确认目标地图 ${targetMap.name} 的修订已变化，并含有该来源栅格图层；SHA-256 与上传回执一致。`,
        `The map catalog confirms target map ${targetMap.name} changed revision and contains this source raster layer; its SHA-256 matches the upload receipt.`));
    } catch (requestError) {
      if (!requestController.signal.aborted && generation === requestGeneration.current) {
        setReadback('incomplete');
        setError(message(requestError, language));
      }
    } finally {
      if (generation === requestGeneration.current) {
        setBusyAction(null);
        if (controller.current === requestController) controller.current = null;
      }
    }
  };

  const importRaster = async (): Promise<void> => {
    const projectPath = context.projectPath;
    const projectRevision = context.projectRevision;
    if (!inspected || !projectPath || !projectRevision) return;
    if (!canImport) {
      setError(text(language, '地图文档或栅格导入接口尚未注册。', 'Map document or raster import operations are not registered.'));
      return;
    }
    const { controller: requestController, generation } = beginRequest();
    pendingImport.current = null;
    setBusyAction('import');
    setError(undefined);
    setReadback(undefined);
    setPostCancelStatus(undefined);
    setProgress(undefined);
    setReceipt(undefined);
    try {
      const latestCatalog = await refreshCatalog(requestController.signal);
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      const latestMaps = latestCatalog.documents.filter(document => importableMapTypes.has(document.mapType));
      const target = latestMaps.find(document => mapDocumentKey(document) === selectedMapKey) ??
        (latestMaps.length === 0 ? null : undefined);
      if (latestMaps.length > 0 && !target) {
        throw new Error(text(language, '所选地图已变化，请重新检查栅格并选择目标地图。', 'The selected map changed. Inspect the raster again and choose a target map.'));
      }
      if (latestMaps.length === 0 && latestCatalog.documents.length > 0) {
        throw new Error(text(language, '当前工程没有可导入栅格的地图类型。', 'The current project has no map that accepts raster layers.'));
      }
      pendingImport.current = {
        mapIndex: target?.mapIndex ?? 0,
        mapName: target?.name ?? null,
        mapType: target?.mapType ?? 'Project',
        previousVisualizationRevision: latestCatalog.visualizationRevision,
      };

      const currentLayer = await backend.request<RasterLayerInspection>(
        'map.raster.layer.inspect', { sourcePath: inspected.uploaded.sourcePath },
        { signal: requestController.signal, onProgress: value => setProgress(value) },
      );
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      if (currentLayer.sourceCanonicalPath !== inspected.uploaded.sourceCanonicalPath ||
          currentLayer.sourceRevision.toUpperCase() !== inspected.uploaded.sourceRevision.toUpperCase() ||
          currentLayer.mapLayerType !== inspected.layer.mapLayerType) {
        throw new Error(text(language, '栅格文件在检查后发生变化；请重新上传并检查。', 'The raster changed after inspection. Upload and inspect it again.'));
      }

      const map = {
        projectPath,
        projectXml: context.projectXml,
        projectXmlSHA: await projectXmlSha(context.projectXml),
        expectedVisualizationRevision: latestCatalog.visualizationRevision,
        kind: target?.mapType ?? 'Project',
        ...(target ? { mapID: target.mapID, mapIndex: target.mapIndex, mapName: target.name, mapType: target.mapType } : {}),
      };
      const committed = await backend.request<MapDocumentReceipt>('map.raster.layer.import', {
        map,
        sourcePath: inspected.uploaded.sourcePath,
        sourceRevision: inspected.uploaded.sourceRevision,
        mapLayerType: inspected.layer.mapLayerType,
        expectedProjectRevision: projectRevision,
        expectedSourceCanonicalPath: inspected.uploaded.sourceCanonicalPath,
      }, { signal: requestController.signal, onProgress: value => setProgress(value) });
      if ((target && (committed.mapName !== target.name || committed.mapType !== target.mapType || committed.mapIndex !== target.mapIndex)) ||
          (!target && (committed.mapType !== 'Project' || committed.mapIndex !== 0)) ||
          committed.previousVisualizationRevision !== latestCatalog.visualizationRevision ||
          !/^[a-f0-9]{64}$/i.test(committed.visualizationRevision)) {
        throw new Error(text(language, '服务器未返回与请求地图修订匹配的导入回执。', 'The server returned an import receipt that does not match the requested map revision.'));
      }
      onImportCommitted?.([committed]);
      if (requestController.signal.aborted || generation !== requestGeneration.current) return;
      setReceipt(committed);

      try {
        const confirmed = await refreshCatalog(requestController.signal);
        if (requestController.signal.aborted || generation !== requestGeneration.current) return;
        const committedMap = confirmed.documents.find(document => document.mapIndex === committed.mapIndex && document.name === committed.mapName && document.mapType === committed.mapType);
        if (confirmed.visualizationRevision !== committed.visualizationRevision || !committedMap ||
            !containsRasterSource(committedMap.roots, inspected.uploaded.sourcePath, inspected.layer.mapLayerType)) {
          throw new Error('The saved map catalog did not read back the committed raster import.');
        }
        const source = await backend.request<RasterLayerInspection>(
          'map.raster.layer.inspect', { sourcePath: inspected.uploaded.sourcePath }, { signal: requestController.signal },
        );
        if (requestController.signal.aborted || generation !== requestGeneration.current) return;
        if (source.sourceCanonicalPath !== inspected.uploaded.sourceCanonicalPath ||
            source.sourceRevision.toUpperCase() !== inspected.uploaded.sourceRevision.toUpperCase() ||
            source.mapLayerType !== inspected.layer.mapLayerType) {
          throw new Error(text(language, '回读地图中的栅格来源修订与上传回执不一致。', 'The raster source revision read back from the map does not match the upload receipt.'));
        }
        setCatalog(confirmed);
        setReadback('confirmed');
      } catch (readError) {
        if (!requestController.signal.aborted && generation === requestGeneration.current) {
          setReadback('incomplete');
          setError(text(language, '服务端已返回写入回执，但随后读取地图目录未能确认该修订：', 'The server returned an import receipt, but a follow-up map read did not confirm it: ') + message(readError, language));
        }
      }
    } catch (requestError) {
      if (!requestController.signal.aborted && generation === requestGeneration.current) setError(message(requestError, language));
    } finally {
      if (generation === requestGeneration.current) {
        setBusyAction(null);
        if (controller.current === requestController) controller.current = null;
      }
    }
  };

  const cancel = (): void => {
    const action = busyAction;
    requestGeneration.current += 1;
    controller.current?.abort();
    controller.current = null;
    setBusyAction(null);
    if (action === 'import') {
      setError(text(language, '已请求取消导入；写入状态尚未确认，请重新读取地图目录检查是否提交。', 'Import cancellation was requested. Its write state is unconfirmed; reload the map catalog to check whether it committed.'));
      setReadback('incomplete');
      setPostCancelStatus(text(language, '已请求取消导入；请重新读取地图目录核对来源路径和 SHA-256。', 'Import cancellation was requested; reload the map catalog to verify the source path and SHA-256.'));
    } else if (action === 'inspect') {
      setError(text(language, '已请求取消上传和检查；如果服务端已完成上传，来源副本可能仍保留在工程 Map Sources 目录中。', 'Upload and inspection cancellation was requested. If the server had already finished, the source copy may remain in the project Map Sources directory.'));
    } else if (action === 'refresh') {
      setError(text(language, '已取消地图目录回读；当前仍无法确认导入状态。', 'The map catalog read-back was cancelled; the import state remains unconfirmed.'));
    }
  };

  const info = inspected?.context.info ?? inspected?.layer.info;
  const stats = inspected?.context.stats ?? inspected?.layer.stats;
  const canCommitToNewProjectMap = Boolean(catalog && catalog.documents.length === 0);
  const importEnabled = Boolean(inspected && canImport && (selectedMap || canCommitToNewProjectMap) && !busyAction && (!postCancelStatus || readback === 'confirmed'));

  return <section className="gp-ui-raster-importer" aria-labelledby="gp-ui-raster-importer-title">
    <header className="gp-ui-raster-importer-header">
      <div>
        <span className="tools-kicker">{zh ? '栅格导入' : 'RASTER IMPORT'}</span>
        <h2 id="gp-ui-raster-importer-title">{zh ? '栅格预览与属性' : 'Raster preview and properties'}</h2>
        <p>{zh ? '先检查 GDAL 栅格属性和统计，再选择地图并提交导入。' : 'Inspect GDAL raster properties and statistics, then choose a map and commit the import.'}</p>
      </div>
    </header>

    <div className="gp-ui-dropzone">
      <strong>{file?.name ?? text(language, '选择一个 TIFF 栅格', 'Choose a TIFF raster')}</strong>
      <small>{text(language, '检查操作会把来源复制到当前工程连接目录，并固定其 SHA-256 修订。', 'Inspection copies the source into the current project connections and captures its SHA-256 revision.')}</small>
      <label className="gp-ui-file-read">
        {text(language, '选择栅格文件', 'Choose raster')}
        <input type="file" accept=".tif,.tiff,image/tiff" hidden disabled={busyAction !== null} onChange={event => { setNewFile(event.currentTarget.files?.[0]); event.currentTarget.value = ''; }} />
      </label>
    </div>

    {!context.projectPath && <div className="gp-ui-warning" role="status">{text(language, '需要先打开一个 .ras 工程。', 'Open a .ras project first.')}</div>}
    {!backend.uploadMapSources && <div className="gp-ui-warning" role="status">{text(language, '当前后端不支持将浏览器文件上传到工程连接目录。', 'The current backend cannot upload browser files into project connections.')}</div>}
    {!canInspect && <div className="gp-ui-warning" role="status">{text(language, '栅格属性或地图栅格检查操作未注册。', 'Raster property or map raster inspection operations are not registered.')}</div>}

    <div className="gp-ui-import-actions">
      <button className="gp-ui-button gp-ui-button-primary" type="button" disabled={!file || !context.projectPath || !context.projectRevision || !backend.uploadMapSources || !canInspect || busyAction !== null} onClick={() => void inspectRaster()}>
        {busyAction === 'inspect' ? text(language, '上传并检查中…', 'Uploading and inspecting…') : text(language, '上传并检查', 'Upload and inspect')}
      </button>
      <button className="gp-ui-button gp-ui-button-primary" type="button" disabled={!importEnabled} onClick={() => void importRaster()}>
        {busyAction === 'import' ? text(language, '提交导入中…', 'Importing…') : text(language, '导入到地图', 'Import to map')}
      </button>
      {busyAction && <button className="gp-ui-button gp-ui-button-danger" type="button" onClick={cancel}>{text(language, '取消', 'Cancel')}</button>}
    </div>

    {busyAction && <div className="gp-ui-status" role="status">{busyAction === 'inspect'
      ? text(language, '正在上传来源并读取 GDAL 属性…', 'Uploading the source and reading GDAL properties…')
      : busyAction === 'import'
        ? text(language, '正在验证地图修订并写入栅格图层…', 'Verifying map revisions and writing the raster layer…')
        : text(language, '正在重新读取地图目录和来源修订…', 'Reloading the map catalog and source revision…')}{progress !== undefined && <pre>{typeof progress === 'string' ? progress : JSON.stringify(progress, null, 2)}</pre>}</div>}
    {error && <div className="gp-ui-error" role="alert">{error}</div>}
    {postCancelStatus && <div className={readback === 'confirmed' ? 'gp-ui-success' : 'gp-ui-warning'} role="status">{postCancelStatus}</div>}
    {postCancelStatus && inspected && readback !== 'confirmed' && backend.supports('map.raster.layer.inspect') && <button
      className="gp-ui-button" type="button" disabled={busyAction !== null} onClick={() => void verifyCancelledImport()}>
      {busyAction === 'refresh' ? text(language, '正在回读…', 'Reading back…') : text(language, '重新读取地图目录核对', 'Reload map catalog and verify')}
    </button>}

    {inspected && <>
      <section className="gp-ui-raster-preview" aria-label={zh ? '栅格地图预览' : 'Raster map preview'}>
        <div className="gp-ui-panel-heading"><h3>{zh ? '地图预览' : 'Map preview'}</h3><small>{preview ? `${preview.raster.width} × ${preview.raster.height}` : text(language, 'C# 渲染帧', 'C# rendered frame')}</small></div>
        {preview
          ? <img src={preview.imageSrc} alt={zh ? `${preview.raster.name} 栅格预览` : `${preview.raster.name} raster preview`} />
          : <p className={previewError ? 'gp-ui-warning' : 'gp-ui-hint'} role={previewError ? 'status' : undefined}>{previewError ?? text(language, '正在请求来源栅格预览…', 'Requesting the source raster preview…')}</p>}
        {preview && <p className="gp-ui-hint">{text(language, '预览范围：', 'Preview extent: ')}{preview.raster.minX}, {preview.raster.minY} → {preview.raster.maxX}, {preview.raster.maxY}</p>}
      </section>
      <section className="gp-ui-panel" aria-label={zh ? '栅格来源属性' : 'Raster source properties'}>
        <div className="gp-ui-panel-heading"><h3>{zh ? '来源属性' : 'Source properties'}</h3><small>{inspected.layer.driver} · {inspected.layer.dataType}</small></div>
        <div className="gp-ui-raster-components">
          <RasterSummaryProperties language={language} info={info} />
          <RasterFileProperties language={language} info={info} />
          <RasterStatProperties language={language} stats={stats} />
        </div>
        <dl className="gp-ui-raster-info gp-ui-raster-identity">
          <div><dt>{zh ? '栅格尺寸' : 'Raster dimensions'}</dt><dd>{inspected.layer.width} × {inspected.layer.height}</dd></div>
          <div><dt>{zh ? '地图图层类型' : 'Map layer type'}</dt><dd>{inspected.layer.mapLayerType}</dd></div>
          <div><dt>{zh ? '来源路径' : 'Source path'}</dt><dd title={inspected.uploaded.sourcePath}>{inspected.uploaded.sourcePath}</dd></div>
          <div><dt>SHA-256</dt><dd title={inspected.uploaded.sourceRevision}>{inspected.uploaded.sourceRevision}</dd></div>
        </dl>
      </section>

      <section className="gp-ui-panel" aria-label={zh ? '目标地图' : 'Target map'}>
        <div className="gp-ui-panel-heading"><h3>{zh ? '目标地图' : 'Target map'}</h3><small>{catalog ? `${eligibleMaps.length} ${zh ? '个可导入地图' : 'importable maps'}` : text(language, '尚未读取地图目录', 'Map catalog not loaded')}</small></div>
        {eligibleMaps.length > 0 ? <label className="gp-ui-field"><span>{text(language, '地图', 'Map')}</span>
          <select value={selectedMapKey} onChange={event => setSelectedMapKey(event.currentTarget.value)} disabled={busyAction !== null}>
            {eligibleMaps.map(document => <option key={mapDocumentKey(document)} value={mapDocumentKey(document)}>{document.name} · {document.mapType}</option>)}
          </select>
        </label> : canCommitToNewProjectMap
          ? <p className="gp-ui-hint">{text(language, '当前没有已保存地图；导入操作会按后端契约创建 Project 地图。', 'No saved map exists. Import will create the Project map as defined by the backend contract.')}</p>
          : <p className="gp-ui-hint">{text(language, '工程中没有可接收栅格图层的地图。', 'The project has no map that can accept a raster layer.')}</p>}
        {!canImport && <p className="gp-ui-hint">{text(language, '导入需要 map.document.list、map.tree.load 和 map.raster.layer.import。', 'Import requires map.document.list, map.tree.load, and map.raster.layer.import.')}</p>}
        {!inspected.layer.canRenderPreview && <p className="gp-ui-warning">{text(language, '该栅格超过预览尺寸限制；导入按钮仍按服务端支持类型校验。', 'This raster exceeds the preview-size limit. The import operation still checks its supported type.')}</p>}
      </section>
    </>}

    {receipt && <div className={readback === 'incomplete' ? 'gp-ui-warning' : 'gp-ui-success'} role="status">
      {readback === 'confirmed'
        ? text(language, `C# 服务端已确认导入到 ${receipt.mapName}，地图修订 ${receipt.visualizationRevision}；回读目录也确认了新修订。`, `The C# service confirmed import to ${receipt.mapName} at map revision ${receipt.visualizationRevision}; the catalog read-back confirmed it.`)
        : readback === 'incomplete'
          ? text(language, `C# 服务端返回了 ${receipt.mapName} 的导入回执；地图目录回读未确认。修订 ${receipt.visualizationRevision}`, `The C# service returned an import receipt for ${receipt.mapName}; the map catalog read-back did not confirm it. Revision ${receipt.visualizationRevision}`)
          : text(language, `C# 服务端返回导入回执：${receipt.mapName}，修订 ${receipt.visualizationRevision}`, `The C# service returned an import receipt: ${receipt.mapName}, revision ${receipt.visualizationRevision}`)}
    </div>}
  </section>;
}
