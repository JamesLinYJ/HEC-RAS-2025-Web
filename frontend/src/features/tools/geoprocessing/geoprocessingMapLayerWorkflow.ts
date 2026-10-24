import type { BackendGateway, WorkbenchContext } from '../../../shared/workbench';
import { errorDiagnostics } from '../../../shared/workbench';
import type { MapDocumentTab } from '../../map/mapTypes';
import type { MapDocumentReceipt } from '../../map/ProjectTreeListener';
import { sameSourcePath } from '../attributes/attributeTableTypes';
import {
  captureMapOwner,
  inspectShapefileFamily,
  isShapefileMapImportReceipt,
  sameMapSourceFamily,
  validShapefileFamily,
  type CapturedMapOwner,
  type ShapefileFamilyCapture,
} from '../vector-import/vectorImportTypes';

export type GeoprocessingMapLayerWorkflowStage =
  | 'capability'
  | 'source-inspect'
  | 'map-owner'
  | 'preflight'
  | 'map-import';

export type GeoprocessingMapLayerWorkflowResult =
  | {
      status: 'committed';
      outputPath: string;
      receipt: MapDocumentReceipt;
      family: ShapefileFamilyCapture;
      targetMap: MapDocumentTab;
      mapOwner: CapturedMapOwner;
      adopted: true;
    }
  | {
      status: 'receiptNotAdopted';
      outputPath: string;
      receipt: MapDocumentReceipt;
      family: ShapefileFamilyCapture;
      targetMap: MapDocumentTab;
      mapOwner: CapturedMapOwner;
      adoptionError?: unknown;
      diagnostics?: unknown;
    }
  | {
      status: 'notCommitted';
      stage: Exclude<GeoprocessingMapLayerWorkflowStage, 'map-import'>;
      outputPath: string;
      error: unknown;
      diagnostics?: unknown;
    }
  | {
      status: 'uncertain';
      stage: 'map-import';
      outputPath: string;
      targetMap: MapDocumentTab;
      previousVisualizationRevision: string;
      error: unknown;
      diagnostics?: unknown;
      rawReceipt?: unknown;
    };

function failureDiagnostics(error: unknown): unknown {
  const diagnostics = errorDiagnostics(error);
  return diagnostics === error ? undefined : diagnostics;
}

function assertCurrent(signal: AbortSignal, isCurrent: () => boolean): void {
  if (signal.aborted) throw new DOMException('地图图层操作已取消。', 'AbortError');
  if (!isCurrent()) throw new Error('地理处理来源或目标地图已切换；未提交地图变更。');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function isExpectedFamily(value: unknown, outputPath: string): value is ShapefileFamilyCapture {
  if (!isRecord(value) || typeof value.sourcePath !== 'string' || !Array.isArray(value.files)) return false;
  const sourceFile = value.files.find(file => isRecord(file) && file.role === 'source');
  const sourceRevision = isRecord(sourceFile) ? sourceFile.revision : undefined;
  return typeof sourceRevision === 'string' &&
    sameSourcePath(value.sourcePath, outputPath) &&
    validShapefileFamily(value, value.sourcePath, sourceRevision);
}

/**
 * Imports a verified Geoprocessing Shapefile output into the exact map captured
 * for the run. It never creates, copies, or overwrites the output files.
 */
export async function importGeneratedGeoprocessingShapefile(
  backend: BackendGateway,
  context: WorkbenchContext,
  targetMap: MapDocumentTab,
  outputPath: string,
  expectedFamily: ShapefileFamilyCapture,
  signal: AbortSignal,
  isCurrent: () => boolean,
  onCommitted: (receipt: MapDocumentReceipt) => boolean,
): Promise<GeoprocessingMapLayerWorkflowResult> {
  if (!backend.supports('map.vector.importShapefile')) {
    return {
      status: 'notCommitted',
      stage: 'capability',
      outputPath,
      error: new Error('当前 C# 服务未发布 Shapefile 地图导入操作；文件仍只是已生成的输出。'),
    };
  }
  if (!outputPath.trim() || !isExpectedFamily(expectedFamily, outputPath)) {
    return {
      status: 'notCommitted',
      stage: 'preflight',
      outputPath,
      error: new Error('地理处理输出路径与已验证的完整 Shapefile 文件族不一致；未将文件加入地图。'),
    };
  }

  let stage: Exclude<GeoprocessingMapLayerWorkflowStage, 'map-import'> = 'source-inspect';
  let capturedFamily: ShapefileFamilyCapture | undefined;
  let mapOwner: CapturedMapOwner | undefined;

  try {
    assertCurrent(signal, isCurrent);
    const inspected = await inspectShapefileFamily(backend, outputPath, signal);
    assertCurrent(signal, isCurrent);
    if (!sameSourcePath(inspected.source.canonicalPath, outputPath) ||
        !sameMapSourceFamily(expectedFamily, inspected.family)) {
      throw new Error('地理处理输出文件族相对执行回执已变化；未将文件加入地图。');
    }
    capturedFamily = inspected.family;

    stage = 'map-owner';
    assertCurrent(signal, isCurrent);
    mapOwner = await captureMapOwner(backend, context, targetMap, signal);
    assertCurrent(signal, isCurrent);

    stage = 'preflight';
    if (!sameSourcePath(mapOwner.request.projectPath, context.projectPath ?? '') ||
        mapOwner.request.projectXml !== context.projectXml ||
        mapOwner.request.expectedVisualizationRevision?.toLocaleUpperCase() !==
          mapOwner.visualizationRevision.toLocaleUpperCase() ||
        mapOwner.tree.visualizationRevision.toLocaleUpperCase() !== mapOwner.visualizationRevision.toLocaleUpperCase()) {
      throw new Error('目标地图、工程快照或可视化修订在导入前发生变化；未提交地图变更。');
    }

    // Cancellation and owner changes are honored through the last point before
    // the mutation. Once submitted, an abort or context change cannot discard a
    // valid server receipt.
    assertCurrent(signal, isCurrent);
    let rawReceipt: unknown;
    try {
      rawReceipt = await backend.request<unknown>('map.vector.importShapefile', {
        map: mapOwner.request,
        sourcePath: capturedFamily.sourcePath,
        expectedProjectRevision: context.projectRevision,
        expectedFamily: capturedFamily,
      }, { signal });
    } catch (error) {
      return {
        status: 'uncertain',
        stage: 'map-import',
        outputPath,
        targetMap,
        previousVisualizationRevision: mapOwner.visualizationRevision,
        error,
        diagnostics: failureDiagnostics(error),
      };
    }

    if (!isShapefileMapImportReceipt(rawReceipt as MapDocumentReceipt,
      targetMap, mapOwner.visualizationRevision) ||
        (rawReceipt as MapDocumentReceipt).visualizationRevision.toLocaleUpperCase() ===
          mapOwner.visualizationRevision.toLocaleUpperCase()) {
      return {
        status: 'uncertain',
        stage: 'map-import',
        outputPath,
        targetMap,
        previousVisualizationRevision: mapOwner.visualizationRevision,
        error: new Error('地图导入返回了不匹配的目标地图或可视化修订；请重新读取地图目录核对，切勿直接重试。'),
        rawReceipt,
      };
    }

    const receipt = rawReceipt as MapDocumentReceipt;
    try {
      if (onCommitted(receipt)) {
        return { status: 'committed', outputPath, receipt, family: capturedFamily, targetMap, mapOwner, adopted: true };
      }
      return {
        status: 'receiptNotAdopted',
        outputPath,
        receipt,
        family: capturedFamily,
        targetMap,
        mapOwner,
      };
    } catch (adoptionError) {
      return {
        status: 'receiptNotAdopted',
        outputPath,
        receipt,
        family: capturedFamily,
        targetMap,
        mapOwner,
        adoptionError,
        diagnostics: failureDiagnostics(adoptionError),
      };
    }
  } catch (error) {
    return {
      status: 'notCommitted',
      stage,
      outputPath,
      error,
      diagnostics: failureDiagnostics(error),
    };
  }
}
