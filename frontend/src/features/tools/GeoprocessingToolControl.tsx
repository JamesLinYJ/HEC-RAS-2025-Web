import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendTransportState, FeaturePageProps } from '../../shared/workbench';
import { errorDiagnostics, text } from '../../shared/workbench';
import { geoprocessingCatalog } from './GeoprocessingCatalog';
import { type GeoprocessingFieldValues, type GeoprocessingToolDefinition, type ToolGroupDefinition } from './GeoprocessingToolBase';
import { GeoprocessingInputDetails } from './GeoprocessingInputDetails';
import { GeoprocessingOutputDetails } from './GeoprocessingOutputDetails';
import { readProjectProjection } from './geoprocessingUi';
import { GeoprocessingPolylineExecutionDetails, type GeoprocessingExecutionView, type GeoprocessingPolylineResult } from './geoprocessing/GeoprocessingPolylineExecutionDetails';
import { GeoprocessingVectorSourcePicker, type GeoprocessingPolygonInput, type GeoprocessingPolylineInput, type GeoprocessingSourceSelection } from './geoprocessing/GeoprocessingVectorSourcePicker';
import { inspectShapefileFamily } from './vector-import/vectorImportTypes';
import { sameSourcePath } from './attributes/attributeTableTypes';
import { ReprojectionToolControl } from './ReprojectionToolControl';
import { ToolGroupButton } from './tool-controls/ToolGroupButton';
import { ToolOptionsCog } from './tool-controls/ToolOptionsCog';
import { ToolOptionsFlyout } from './tool-controls/ToolOptionsFlyout';
import { ToolRichFlyoutControl } from './tool-controls/ToolRichFlyoutControl';
import './tool-controls/toolControls.css';
import './geoprocessingUi.css';
import './geoprocessing/geoprocessingSourcePicker.css';

type Props = FeaturePageProps & { onDirtyStateChange?: (owner: string, dirty: boolean) => void };
type ValidationResult = { validationIssues: Array<{ field: string; message: string; severity: string }> };
type FieldValues = GeoprocessingFieldValues;
const supportedPolylineOperations = new Set(['Filter', 'Smooth', 'Reverse', 'CullByLength', 'DensifyPolylines', 'MergePolylines', 'LineOffset']);
const supportedVectorOperations = new Set([...supportedPolylineOperations, 'BufferPolyline', 'PolylineToPolygon', 'BufferPolygon', 'DensifyPolygons', 'PolygonToPolyline']);

function projectDirectory(path: string): string {
  if (!path.trim()) throw new Error('工程路径为空。');
  const normalized = path.replace(/\\/g, '/');
  const end = normalized.endsWith('/') ? normalized.length - 1 : normalized.length;
  const separator = normalized.lastIndexOf('/', end - 1);
  if (separator < 0) throw new Error('无法从工程路径读取工程目录。');
  if (separator === 2 && normalized[1] === ':') return normalized.slice(0, 3);
  const directory = normalized.slice(0, separator);
  return directory || '/';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(item => typeof item === 'string');
}

function parseVectorResult(value: unknown, operation: string, inputKind: 'Polyline' | 'Polygon', outputKind: 'Polyline' | 'Polygon',
  expectedPathIDs: string[], outputHandler: 'Clipboard' | 'Shapefile'): GeoprocessingPolylineResult {
  if (!isRecord(value) || typeof value.canExecute !== 'boolean' || value.operation !== operation ||
      typeof value.outputTitle !== 'string' || !value.outputTitle.trim() ||
      !Number.isInteger(value.inputLineCount) || !Number.isInteger(value.outputLineCount) ||
      !(value.inputPolygonCount === undefined || Number.isInteger(value.inputPolygonCount)) ||
      !(value.outputPolygonCount === undefined || Number.isInteger(value.outputPolygonCount)) ||
      !(value.outputPath === null || typeof value.outputPath === 'string') ||
      !(value.clipboardText === null || typeof value.clipboardText === 'string') ||
      !(value.outputFormat === null || value.outputFormat === undefined || typeof value.outputFormat === 'string') ||
      !Array.isArray(value.outputs) || !Array.isArray(value.validationIssues) ||
      !(value.capturedPathIdentities === undefined || value.capturedPathIdentities === null || isStringArray(value.capturedPathIdentities)) ||
      !(value.capturedPolygonPathIdentities === undefined || value.capturedPolygonPathIdentities === null || isStringArray(value.capturedPolygonPathIdentities)) ||
      typeof value.outputKind !== 'string') {
    throw new Error('服务端返回的矢量执行回执格式无效。');
  }
  const issues = value.validationIssues.map(issue => {
    if (!isRecord(issue) || typeof issue.field !== 'string' || typeof issue.message !== 'string' || typeof issue.severity !== 'string') {
      throw new Error('服务端返回的参数校验结果格式无效。');
    }
    return { field: issue.field, message: issue.message, severity: issue.severity };
  });
  const outputs: GeoprocessingPolylineResult['outputs'] = value.outputs.map(output => {
    if (!isRecord(output) || typeof output.featureIdentity !== 'string' || !output.featureIdentity.trim() ||
        typeof output.pathIdentity !== 'string' || !output.pathIdentity.trim() ||
        !Number.isInteger(output.sourceFeatureIndex) || (output.sourceFeatureIndex as number) < 0 ||
        !Number.isInteger(output.pathIndex) || (output.pathIndex as number) < 0 || !Array.isArray(output.coordinates) ||
        output.coordinates.length < 2 || !output.coordinates.every(point => Array.isArray(point) && point.length === 2 &&
          point.every(coordinate => typeof coordinate === 'number' && Number.isFinite(coordinate))) ||
        !(output.zValues === null || output.zValues === undefined || isStringArray(output.zValues)) ||
        !(output.mValues === null || output.mValues === undefined || isStringArray(output.mValues))) {
      throw new Error('服务端返回的折线输出包含无效身份或坐标。');
    }
    const zValues = isStringArray(output.zValues) ? output.zValues : undefined;
    const mValues = isStringArray(output.mValues) ? output.mValues : undefined;
    if ((zValues && zValues.length !== output.coordinates.length) || (mValues && mValues.length !== output.coordinates.length)) {
      throw new Error('服务端折线输出的 Z/M 顶点数量不匹配。');
    }
    return {
      featureIdentity: output.featureIdentity,
      pathIdentity: output.pathIdentity,
      sourceFeatureIndex: output.sourceFeatureIndex as number,
      pathIndex: output.pathIndex as number,
      coordinates: output.coordinates as number[][],
      ...(zValues ? { zValues } : {}),
      ...(mValues ? { mValues } : {}),
    };
  });
  const polygonRaw = value.polygonOutputs;
  if (!(polygonRaw === undefined || polygonRaw === null || Array.isArray(polygonRaw))) {
    throw new Error('服务端返回的多边形输出格式无效。');
  }
  const polygonOutputs: GeoprocessingPolylineResult['polygonOutputs'] = (Array.isArray(polygonRaw) ? polygonRaw : []).map(output => {
    if (!isRecord(output) || typeof output.featureIdentity !== 'string' || !output.featureIdentity.trim() ||
        typeof output.pathIdentity !== 'string' || !output.pathIdentity.trim() ||
        !Number.isInteger(output.sourceFeatureIndex) || (output.sourceFeatureIndex as number) < 0 ||
        !Number.isInteger(output.pathIndex) || (output.pathIndex as number) < 0 || !Array.isArray(output.coordinates) ||
        output.coordinates.length < 4 || !output.coordinates.every(point => Array.isArray(point) && point.length === 2 &&
          point.every(coordinate => typeof coordinate === 'number' && Number.isFinite(coordinate))) ||
        !(output.zValues === null || output.zValues === undefined || isStringArray(output.zValues)) ||
        !(output.mValues === null || output.mValues === undefined || isStringArray(output.mValues)) ||
        !(output.holes === null || output.holes === undefined || Array.isArray(output.holes)) ||
        !(output.holeZValues === null || output.holeZValues === undefined || Array.isArray(output.holeZValues)) ||
        !(output.holeMValues === null || output.holeMValues === undefined || Array.isArray(output.holeMValues))) {
      throw new Error('服务端返回的多边形输出包含无效身份、环或坐标。');
    }
    const coordinates = output.coordinates as number[][];
    if (coordinates[0]![0] !== coordinates[coordinates.length - 1]![0] ||
        coordinates[0]![1] !== coordinates[coordinates.length - 1]![1]) {
      throw new Error('服务端返回的多边形外环未闭合。');
    }
    const holesRaw = Array.isArray(output.holes) ? output.holes : [];
    if (!holesRaw.every(ring => Array.isArray(ring) && ring.length >= 4 && ring.every(point => Array.isArray(point) && point.length === 2 &&
      point.every(coordinate => typeof coordinate === 'number' && Number.isFinite(coordinate))) &&
      ring[0]![0] === ring[ring.length - 1]![0] && ring[0]![1] === ring[ring.length - 1]![1])) {
      throw new Error('服务端返回的多边形洞环未闭合或坐标无效。');
    }
    const zValues = isStringArray(output.zValues) ? output.zValues : undefined;
    const mValues = isStringArray(output.mValues) ? output.mValues : undefined;
    const holeZRaw = Array.isArray(output.holeZValues) ? output.holeZValues : [];
    const holeMRaw = Array.isArray(output.holeMValues) ? output.holeMValues : [];
    const validHoleOrdinates = (items: unknown[], dimension: string): Array<string[] | null> => {
      if (items.length !== holesRaw.length || !items.every(item => item === null || isStringArray(item))) {
        throw new Error(`服务端返回的多边形洞环 ${dimension} 维度数量不匹配。`);
      }
      return items.map((item, index) => {
        if (item === null) return null;
        if ((item as string[]).length !== holesRaw[index]!.length) throw new Error(`服务端返回的多边形洞环 ${dimension} 顶点数量不匹配。`);
        return item as string[];
      });
    };
    if ((zValues && zValues.length !== coordinates.length) || (mValues && mValues.length !== coordinates.length)) {
      throw new Error('服务端多边形外环 Z/M 顶点数量不匹配。');
    }
    return {
      featureIdentity: output.featureIdentity,
      pathIdentity: output.pathIdentity,
      sourceFeatureIndex: output.sourceFeatureIndex as number,
      pathIndex: output.pathIndex as number,
      coordinates,
      ...(zValues ? { zValues } : {}),
      ...(mValues ? { mValues } : {}),
      holes: holesRaw as number[][][],
      holeZValues: validHoleOrdinates(holeZRaw, 'Z'),
      holeMValues: validHoleOrdinates(holeMRaw, 'M'),
    };
  });
  const inputLineCount = value.inputLineCount as number;
  const outputLineCount = value.outputLineCount as number;
  const inputPolygonCount = (value.inputPolygonCount ?? 0) as number;
  const outputPolygonCount = (value.outputPolygonCount ?? 0) as number;
  const capturedPathIdentities = isStringArray(value.capturedPathIdentities) ? value.capturedPathIdentities : [];
  const capturedPolygonPathIdentities = isStringArray(value.capturedPolygonPathIdentities) ? value.capturedPolygonPathIdentities : [];
  const captured = inputKind === 'Polyline' ? capturedPathIdentities : capturedPolygonPathIdentities;
  const inputCount = inputKind === 'Polyline' ? inputLineCount : inputPolygonCount;
  if (inputCount !== expectedPathIDs.length || captured.length !== expectedPathIDs.length ||
      captured.some((identity, index) => identity !== expectedPathIDs[index])) {
    throw new Error('服务端回执没有绑定到本次提交的真实输入路径。');
  }
  if (outputLineCount !== outputs.length || outputPolygonCount !== polygonOutputs.length ||
      (value.canExecute && value.outputKind !== outputKind)) {
    throw new Error('服务端矢量输出数量或几何类型与执行回执不一致。');
  }
  if (value.canExecute && outputHandler === 'Clipboard' && (value.outputPath !== null || typeof value.clipboardText !== 'string' ||
      (value.outputFormat !== null && value.outputFormat !== undefined && value.outputFormat !== (outputKind === 'Polygon' ? 'application/geo+json' : 'application/x-hecras-polylines+json')))) {
    throw new Error('服务端没有返回与矢量结果类型匹配的文本预览。');
  }
  if (value.canExecute && outputHandler === 'Shapefile' && (typeof value.outputPath !== 'string' || !value.outputPath.trim() ||
      value.clipboardText !== null || (value.outputFormat !== null && value.outputFormat !== undefined && value.outputFormat !== 'application/x-esri-shapefile'))) {
    throw new Error('服务端没有返回可核验的 Shapefile 输出路径和格式回执。');
  }
  if (!value.canExecute && (value.outputPath !== null || value.clipboardText !== null)) {
    throw new Error('服务端拒绝执行，却返回了输出数据。');
  }
  return {
    canExecute: value.canExecute,
    operation,
    outputTitle: value.outputTitle,
    inputLineCount,
    outputLineCount,
    inputPolygonCount,
    outputPolygonCount,
    outputPath: value.outputPath as string | null,
    clipboardText: value.clipboardText as string | null,
    outputFormat: typeof value.outputFormat === 'string' ? value.outputFormat : null,
    outputs,
    polygonOutputs,
    validationIssues: issues,
    capturedPathIdentities: [...capturedPathIdentities],
    capturedPolygonPathIdentities: [...capturedPolygonPathIdentities],
    outputKind: value.outputKind,
    outputHandler,
  };
}

const groups: ToolGroupDefinition[] = [
  { id: 'polyline', title: { zh: '折线图层工具', en: 'Polyline layer tools' }, ids: ['buffer-polyline', 'line-offset', 'filter', 'reverse-polyline', 'smooth-polylines', 'densify-polylines', 'distance-to-lines', 'proximity-heatmap', 'snap-to-high-ground', 'polyline-to-polygon', 'cull-by-length', 'merge-polylines'] },
  { id: 'polygon', title: { zh: '多边形图层工具', en: 'Polygon layer tools' }, ids: ['buffer-polygon', 'densify-polygons', 'rasterize-polygons', 'polygon-to-polyline', 'filter', 'smooth-polylines', 'snap-to-high-ground', 'laplacian-terrain', 'clip-raster-to-polygon', 'raster-cut-stats'] },
  { id: 'raster', title: { zh: '栅格与地形工具', en: 'Raster and terrain tools' }, ids: ['resample-raster', 'round-raster', 'clip-raster-to-polygon', 'nodata-laplacian-fill', 'raster-cut-stats'] },
  { id: 'point', title: { zh: '点图层工具', en: 'Point layer tools' }, ids: ['flat-area-detector', 'nodata-laplacian-fill'] },
];

function initialValues(tool: GeoprocessingToolDefinition): FieldValues {
  return Object.fromEntries(tool.fields.map(field => [field.key, field.initial]));
}

function downloadJson(value: unknown, language: 'zh' | 'en'): void {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a'); link.href = url; link.download = (language === 'zh' ? '地理处理参数' : 'geoprocessing-parameters') + '.json'; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function isValidationResult(value: unknown): value is ValidationResult {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const issues = (value as { validationIssues?: unknown }).validationIssues;
  return Array.isArray(issues) && issues.every(issue => Boolean(issue) && typeof issue === 'object' &&
    typeof (issue as ValidationResult['validationIssues'][number]).field === 'string' &&
    typeof (issue as ValidationResult['validationIssues'][number]).message === 'string' &&
    typeof (issue as ValidationResult['validationIssues'][number]).severity === 'string');
}

export function GeoprocessingToolControl({ language, context, backend, onDirtyStateChange }: Props) {
  const zh = language === 'zh';
  const [activeToolId, setActiveToolId] = useState<string>('buffer-polyline');
  const activeToolIDRef = useRef(activeToolId); activeToolIDRef.current = activeToolId;
  const [search, setSearch] = useState('');
  const [values, setValues] = useState<FieldValues>(() => initialValues(geoprocessingCatalog[0]!));
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() => new Set(groups.map(group => group.id)));
  const [inputMode, setInputMode] = useState<'All' | 'Selected'>('All');
  const [vectorSelection, setVectorSelection] = useState<GeoprocessingSourceSelection>({ capture: null, inputMode: 'All', selectedPathIDs: [] });
  const [outputName, setOutputName] = useState('');
  const [outputHandler, setOutputHandler] = useState('Clipboard');
  const [showOptions, setShowOptions] = useState(true);
  const [validation, setValidation] = useState<ValidationResult>();
  const [validationError, setValidationError] = useState<string>();
  const [validationDiagnostic, setValidationDiagnostic] = useState<string>();
  const [validating, setValidating] = useState(false);
  const validationGeneration = useRef(0);
  const validationController = useRef<AbortController | null>(null);
  const executionGeneration = useRef(0);
  const executionController = useRef<AbortController | null>(null);
  const executionOperationID = useRef<string | null>(null);
  const [execution, setExecution] = useState<GeoprocessingExecutionView>({ kind: 'idle' });
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.selectedAsset?.path,
    context.selectedAsset?.revision, context.selectedAsset?.locator]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const [showReprojection, setShowReprojection] = useState(false);
  const activeTool = geoprocessingCatalog.find(tool => tool.id === activeToolId) ?? geoprocessingCatalog[0]!;
  const isSupportedVectorTool = supportedVectorOperations.has(activeTool.operation) &&
    (activeTool.inputKind === 'Polyline' || activeTool.inputKind === 'Polygon');
  const activeInputMode = isSupportedVectorTool ? vectorSelection.inputMode : inputMode;
  const activeOutputHandler = outputHandler;
  const defaults = initialValues(activeTool);
  const dirty = outputName !== '' || activeInputMode !== 'All' || activeOutputHandler !== (activeTool.outputKind === 'Raster' ? 'Tiff' : 'Clipboard') || JSON.stringify(values) !== JSON.stringify(defaults);
  const fileOutput = isSupportedVectorTool ? outputHandler === 'Shapefile' : ['Shapefile', 'Map Layer', 'Tiff', 'CSV File', 'Text File'].includes(outputHandler);
  const outputOptions = isSupportedVectorTool ? ['Clipboard', 'Shapefile'] : activeTool.outputKind === 'Raster' ? ['Tiff', 'Map Layer']
    : activeTool.outputKind === 'Text' ? ['Clipboard', 'CSV File', 'Text File']
      : ['Clipboard', 'Shapefile', 'Map Layer'];
  const executeAvailable = backend.supports('geoprocessing.execute');
  const validateAvailable = backend.supports('geoprocessing.validate');

  const invalidateValidation = useCallback((): void => {
    validationGeneration.current += 1;
    validationController.current?.abort();
    validationController.current = null;
    setValidating(false); setValidation(undefined); setValidationError(undefined); setValidationDiagnostic(undefined);
  }, []);

  const invalidateExecution = useCallback((): void => {
    executionGeneration.current += 1;
    executionController.current?.abort('The geoprocessing input or parameters changed.');
    executionController.current = null;
    executionOperationID.current = null;
    setExecution({ kind: 'idle' });
  }, []);

  useEffect(() => {
    invalidateValidation();
    invalidateExecution();
    setValues(initialValues(activeTool)); setOutputName(''); setInputMode('All');
    setVectorSelection(current => ({ ...current, inputMode: 'All' }));
    setOutputHandler(activeTool.outputKind === 'Raster' ? 'Tiff' : 'Clipboard');
    setShowOptions(true);
  }, [activeTool.id, invalidateExecution, invalidateValidation]);
  useEffect(() => {
    invalidateValidation();
    invalidateExecution();
  }, [owner, invalidateExecution, invalidateValidation]);
  useEffect(() => () => {
    validationGeneration.current += 1;
    validationController.current?.abort();
    validationController.current = null;
    executionGeneration.current += 1;
    executionController.current?.abort();
    executionController.current = null;
  }, []);
  useEffect(() => { onDirtyStateChange?.('tools.geoprocessing', dirty); return () => onDirtyStateChange?.('tools.geoprocessing', false); }, [dirty, onDirtyStateChange]);

  const filteredTools = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return geoprocessingCatalog.filter(tool => !query || [tool.title.zh, tool.title.en, tool.description.zh, tool.description.en,
      ...tool.fields.flatMap(field => [field.label.zh, field.label.en, field.description?.zh ?? '', field.description?.en ?? ''])]
      .some(value => value.toLocaleLowerCase().includes(query)));
  }, [search]);
  const chooseTool = (tool: GeoprocessingToolDefinition): void => {
    invalidateValidation(); invalidateExecution(); setActiveToolId(tool.id);
    setExpandedGroups(current => new Set([...current, ...groups.filter(group => group.ids.includes(tool.id)).map(group => group.id)]));
  };
  const setField = (field: string, value: string | boolean): void => {
    invalidateValidation(); invalidateExecution(); setValues(current => ({ ...current, [field]: value }));
  };
  const setInputSelection = (value: 'All' | 'Selected'): void => { invalidateValidation(); invalidateExecution(); setInputMode(value); };
  const setOutputChoice = (value: string): void => { invalidateValidation(); invalidateExecution(); setOutputHandler(value); };
  const setOutputTitle = (value: string): void => { invalidateValidation(); invalidateExecution(); setOutputName(value); };
  const handleVectorSelectionChange = useCallback((next: GeoprocessingSourceSelection): void => {
    invalidateValidation(); invalidateExecution(); setVectorSelection(next); setInputMode(next.inputMode);
  }, [invalidateExecution, invalidateValidation]);

  const validationRequest = (): Record<string, unknown> => ({
    operation: activeTool.operation,
    cultureName: zh ? 'zh-CN' : 'en-US',
    inputMode: activeInputMode,
    outputHandler: activeOutputHandler,
    outputName: outputName || null,
    ...values,
  });

  const validate = async (): Promise<void> => {
    invalidateValidation();
    if (!validateAvailable) { setValidationError(text(language, '当前服务未开放参数校验。', 'Parameter validation is unavailable on the current service.')); return; }
    const requestOwner = owner;
    const requestToolID = activeTool.id;
    const requestValues = validationRequest();
    const controller = new AbortController();
    validationController.current = controller;
    const requestGeneration = ++validationGeneration.current;
    const current = (): boolean => requestGeneration === validationGeneration.current && ownerRef.current === requestOwner &&
      activeToolIDRef.current === requestToolID && !controller.signal.aborted;
    setValidating(true);
    try {
      const result: unknown = await backend.request('geoprocessing.validate', requestValues, { signal: controller.signal });
      if (!current()) return;
      if (!isValidationResult(result)) throw new Error(text(language, '服务返回的校验回执格式无效。', 'The service returned an invalid validation receipt.'));
      setValidation(result);
    } catch (error) {
      if (controller.signal.aborted || !current()) return;
      setValidationError(text(language, '服务端未能完成参数校验。', 'The service could not complete parameter validation.'));
      const diagnostic = errorDiagnostics(error);
      try { setValidationDiagnostic(typeof diagnostic === 'string' ? diagnostic : JSON.stringify(diagnostic, null, 2)); }
      catch { setValidationDiagnostic(String(diagnostic)); }
    }
    finally {
      if (requestGeneration === validationGeneration.current) {
        setValidating(false);
        if (validationController.current === controller) validationController.current = null;
      }
    }
  };

  const currentInputLines: GeoprocessingPolylineInput[] = vectorSelection.capture?.shapeType === 'Polyline'
    ? (vectorSelection.inputMode === 'All' ? vectorSelection.capture.lines
      : vectorSelection.capture.lines.filter(line => vectorSelection.selectedPathIDs.includes(line.pathIdentity)))
    : [];
  const currentInputPolygons: GeoprocessingPolygonInput[] = vectorSelection.capture?.shapeType === 'Polygon'
    ? (vectorSelection.inputMode === 'All' ? vectorSelection.capture.polygons
      : vectorSelection.capture.polygons.filter(polygon => vectorSelection.selectedPathIDs.includes(polygon.pathIdentity)))
    : [];
  const currentInputPathIDs = activeTool.inputKind === 'Polyline'
    ? currentInputLines.map(line => line.pathIdentity) : currentInputPolygons.map(polygon => polygon.pathIdentity);
  const projectProjection = readProjectProjection(context.projectXml);
  const hasBlockingValidation = validation?.validationIssues.some(issue =>
    issue.severity.toLowerCase() === 'error' || issue.severity.toLowerCase() === 'typeerror') ?? false;
  const shapefileCaptureAvailable = backend.supports('workspace.file.inspect') && backend.supports('map.vector.captureShapefile');
  const selectedInputCount = activeTool.inputKind === 'Polyline' ? currentInputLines.length : currentInputPolygons.length;
  const sourceCapture = vectorSelection.capture;
  const sourceProjectionVerified = sourceCapture?.sourceFamilyKind !== 'shapefile' || Boolean(sourceCapture.targetProjectionWKT?.trim());
  const operationCannotPreserveNullableOrdinates = Boolean(sourceCapture?.hasNullableOrdinates && !supportedPolylineOperations.has(activeTool.operation));
  const canRunVectorTool = isSupportedVectorTool && executeAvailable && !validating && execution.kind !== 'running' && execution.kind !== 'uncertain' &&
    sourceCapture !== null && sourceCapture.owner === owner && sourceCapture.shapeType === activeTool.inputKind && selectedInputCount > 0 &&
    (outputHandler === 'Clipboard' || (outputHandler === 'Shapefile' && shapefileCaptureAvailable && outputName.trim().length > 0)) &&
    sourceProjectionVerified && !operationCannotPreserveNullableOrdinates && Boolean(context.projectPath && context.projectRevision && context.projectXml && projectProjection) &&
    !hasBlockingValidation;

  const isDirectExportsShapefile = (outputPath: string, projectPath: string): boolean => {
    const normalized = outputPath.replaceAll('\\', '/');
    const leaf = normalized.slice(normalized.lastIndexOf('/') + 1);
    if (!leaf || leaf === '.' || leaf === '..' || !leaf.toLocaleLowerCase().endsWith('.shp')) return false;
    if (!(/^[a-z]:\//i.test(normalized) || normalized.startsWith('//') || normalized.startsWith('/'))) return false;
    const directory = normalized.slice(0, normalized.lastIndexOf('/')) || '/';
    const base = projectDirectory(projectPath).replaceAll('\\', '/').replace(/\/+$/, '') || '/';
    const exportsDirectory = base === '/' ? '/Exports' : `${base}/Exports`;
    return sameSourcePath(directory, exportsDirectory);
  };

  const executeVectorTool = async (): Promise<void> => {
    setExecution({ kind: 'idle' }); executionOperationID.current = null;
    if (!canRunVectorTool || !sourceCapture || !context.projectPath || !context.projectXml || !projectProjection) return;
    const capture = sourceCapture;
    const requestLines = [...currentInputLines];
    const requestPolygons = [...currentInputPolygons];
    const requestPathIDs = [...currentInputPathIDs];
    const requestToolID = activeTool.id;
    const requestOwner = owner;
    const request = {
      sourcePath: capture.sourcePath,
      sourceRevision: capture.sourceRevision,
      projectDirectory: projectDirectory(context.projectPath),
      projectProjectionType: projectProjection.type,
      projectProjectionValue: projectProjection.value,
      projectXml: context.projectXml,
      operation: activeTool.operation,
      inputMode: vectorSelection.inputMode,
      inputLines: requestLines,
      inputPolygons: requestPolygons,
      cultureName: zh ? 'zh-CN' : 'en-US',
      outputHandler,
      outputName: outputHandler === 'Shapefile' ? outputName.trim() : null,
      sourceFamily: capture.sourceFamily,
      targetProjectionWKT: capture.targetProjectionWKT,
      ...values,
    };
    const controller = new AbortController();
    executionController.current?.abort('A newer geoprocessing request started.');
    executionController.current = controller;
    const requestGeneration = ++executionGeneration.current;
    executionOperationID.current = null;
    setExecution({ kind: 'running', cancelRequested: false });
    const isCurrent = (): boolean => requestGeneration === executionGeneration.current &&
      ownerRef.current === requestOwner && activeToolIDRef.current === requestToolID;
    try {
      const raw = await backend.request<unknown>('geoprocessing.execute', request, {
        signal: controller.signal,
        onProgress: progress => {
          if (!isCurrent() || controller.signal.aborted) return;
          setExecution(current => current.kind === 'running' ? { ...current, progress } : current);
        },
        onTransportState: (state: BackendTransportState) => {
          if (!isCurrent()) return;
          executionOperationID.current = state.operationId;
          setExecution(current => current.kind === 'running' ? {
            ...current,
            transport: state,
            cancelRequested: current.cancelRequested || state.state === 'cancelRequested' || state.state === 'cancelRetrying' || state.state === 'cancelSent',
          } : current);
        },
      });
      if (!isCurrent()) return;
      let result = parseVectorResult(raw, activeTool.operation, activeTool.inputKind as 'Polyline' | 'Polygon',
        activeTool.outputKind as 'Polyline' | 'Polygon', requestPathIDs, outputHandler as 'Clipboard' | 'Shapefile');
      if (result.canExecute && outputHandler === 'Shapefile' && result.outputPath) {
        setExecution(current => current.kind === 'running' ? { ...current, verifyingOutput: true } : current);
        try {
          if (!isDirectExportsShapefile(result.outputPath, context.projectPath)) {
            throw new Error('服务端输出路径不在当前工程 Exports 目录的直接子级，未对该路径执行文件检查。');
          }
          const receipt = await inspectShapefileFamily(backend, result.outputPath, controller.signal);
          if (!sameSourcePath(receipt.source.canonicalPath, result.outputPath) ||
              !sameSourcePath(receipt.family.sourcePath, receipt.source.canonicalPath)) {
            throw new Error('检查回执的规范路径与本次服务端输出不一致。');
          }
          result = { ...result, shapefileReceipt: { status: 'verified', inspection: receipt.source, family: receipt.family } };
        } catch (verificationError) {
          if (!isCurrent()) return;
          result = { ...result, shapefileReceipt: { status: 'unverified', error: verificationError } };
        }
      }
      if (!isCurrent()) return;
      setExecution(result.canExecute ? { kind: 'completed', result } : { kind: 'rejected', result });
    } catch (caught) {
      if (!isCurrent()) return;
      if (caught && typeof caught === 'object' && (caught as { name?: unknown }).name === 'AbortError') {
        if (executionOperationID.current) setExecution({ kind: 'cancelled' });
        else setExecution({ kind: 'uncertain', error: caught, diagnostics: errorDiagnostics(caught) });
        return;
      }
      const details = errorDiagnostics(caught);
      const terminalConfirmed = isRecord(details) && details.terminalConfirmed === true;
      setExecution(terminalConfirmed
        ? { kind: 'failed', error: caught, diagnostics: details }
        : { kind: 'uncertain', error: caught, diagnostics: details });
    } finally {
      if (requestGeneration === executionGeneration.current) {
        if (executionController.current === controller) executionController.current = null;
      }
    }
  };

  const cancelVectorExecution = (): void => {
    if (!executionController.current || execution.kind !== 'running') return;
    setExecution(current => current.kind === 'running' ? { ...current, cancelRequested: true } : current);
    executionController.current.abort('The user requested geoprocessing cancellation.');
  };

  const cancelValidation = (): void => {
    if (!validating) return;
    validationController.current?.abort();
    validationGeneration.current += 1;
    validationController.current = null;
    setValidating(false);
    setValidation(undefined);
    setValidationError(undefined);
    setValidationDiagnostic(undefined);
  };

  const severityLabel = (severity: string): string => {
    if (!zh) return severity;
    const labels: Record<string, string> = { error: '错误', typeerror: '类型错误', warning: '警告', information: '信息', info: '信息' };
    return labels[severity.toLowerCase()] ?? severity;
  };

  const selectedGroup = groups.find(group => group.ids.includes(activeTool.id));
  const toggleGroup = (groupID: string): void => setExpandedGroups(current => {
    const next = new Set(current);
    if (next.has(groupID)) next.delete(groupID); else next.add(groupID);
    return next;
  });
  if (showReprojection) {
    return <div className="tools-area tools-geoprocessing-area">
      <ReprojectionToolControl language={language} context={context} backend={backend}
        onClose={() => setShowReprojection(false)} onDirtyStateChange={onDirtyStateChange} />
    </div>;
  }
  return (
    <div className="tools-area tools-geoprocessing-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '地理处理工具' : 'GEOPROCESSING'}</span><h2>{zh ? '地图与栅格工具箱' : 'Map and raster toolbox'}</h2>
          <p>{zh ? '按数据类型分组查找工具，编辑参数并请求服务端校验。' : 'Find tools by data type, edit parameters, and request backend validation.'}</p></div>
        <button className="gp-ui-utility-button" type="button" onClick={() => setShowReprojection(true)}>
          <span aria-hidden="true">◎</span>{zh ? '坐标转换表' : 'Coordinate reprojection'}
        </button>
        <span className={`tools-runtime-pill ${validateAvailable ? 'is-connected' : 'is-unavailable'}`}>{validateAvailable ? (zh ? '校验已接通' : 'Validation connected') : (zh ? '校验未接通' : 'Validation unavailable')}</span>
      </section>
      <div className="tools-geoprocessing-layout">
        <aside className="tools-catalog-panel">
          <label className="tools-search-field"><span aria-hidden="true">⌕</span><input value={search} onChange={event => setSearch(event.target.value)} placeholder={zh ? '搜索工具或参数…' : 'Search tools…'} /></label>
          <div className="tools-catalog-count">{filteredTools.length} / {geoprocessingCatalog.length} {zh ? '个工具' : 'tools'}</div>
          <div className="tools-catalog-groups">{groups.map(group => {
            const tools = geoprocessingCatalog.filter(tool => group.ids.includes(tool.id) && filteredTools.some(item => item.id === tool.id));
            if (!tools.length) return null;
            const visibleGroup = { ...group, ids: tools.map(tool => tool.id) };
            return <div className="tools-catalog-group gp-control-catalog-group" key={group.id}>
              <ToolGroupButton group={visibleGroup} language={language} count={tools.length}
                expanded={expandedGroups.has(group.id)} onToggle={() => toggleGroup(group.id)} />
              {expandedGroups.has(group.id) && <ToolRichFlyoutControl title={group.title[language]} language={language}
                tools={tools} activeToolId={activeTool.id} onSelect={chooseTool} />}
            </div>;
          })}</div>
        </aside>

        <main className="tools-parameter-panel">
          <div className="tools-selected-tool-heading gp-control-selected-heading"><div><span className="tools-kicker">{selectedGroup ? selectedGroup.title[language] : (zh ? '工具' : 'Tool')}</span><h3>{activeTool.title[language]}</h3><p>{activeTool.description[language]}</p></div>
            <ToolOptionsCog language={language} expanded={showOptions} onToggle={() => setShowOptions(open => !open)} /></div>

          <div className="tools-parameter-grid">
            {isSupportedVectorTool
              ? <GeoprocessingVectorSourcePicker language={language} context={context} backend={backend} owner={owner}
                geometryKind={activeTool.inputKind as 'Polyline' | 'Polygon'} selection={vectorSelection} onSelectionChange={handleVectorSelectionChange} />
              : <GeoprocessingInputDetails language={language} context={context} tool={activeTool} inputMode={inputMode} onInputModeChange={setInputSelection} />}
            <div className="tools-parameter-fields"><ToolOptionsFlyout open={showOptions} tool={activeTool} language={language} values={values}
              outputOptions={outputOptions} outputHandler={activeOutputHandler} outputName={outputName} fileOutput={fileOutput}
              onFieldChange={setField} onOutputHandlerChange={setOutputChoice} onOutputNameChange={setOutputTitle}
              onExportParameters={() => downloadJson(validationRequest(), language)} onClose={() => setShowOptions(false)} /></div>
          </div>

          <div className="tools-actions tools-gp-actions">
            <button className="tools-button tools-button-secondary" type="button" onClick={() => void validate()} disabled={validating || execution.kind === 'running' || !validateAvailable}>{validating ? (zh ? '校验中…' : 'Validating…') : (zh ? '校验参数' : 'Validate parameters')}</button>
            {validating && <button className="tools-button tools-button-danger" type="button" onClick={cancelValidation}>{zh ? '取消校验' : 'Cancel validation'}</button>}
            {isSupportedVectorTool
              ? <button className="tools-button tools-button-primary" type="button" onClick={() => void executeVectorTool()} disabled={!canRunVectorTool}
                title={!executeAvailable ? (zh ? '当前服务未开放矢量工具执行。' : 'Vector execution is unavailable on the current service.')
                  : !vectorSelection.capture ? (zh ? `先读取并核验一个已保存的${activeTool.inputKind === 'Polygon' ? 'GeoJSON 多边形' : 'GeoJSON 或 Shapefile 折线'}图层。` : `Read and verify a saved ${activeTool.inputKind.toLowerCase()} layer first.`)
                    : !projectProjection ? (zh ? '当前工程 XML 未提供可读取的投影定义。' : 'The active project XML has no readable projection definition.')
                      : vectorSelection.capture.sourceFamilyKind === 'shapefile' && !vectorSelection.capture.targetProjectionWKT ? (zh ? 'Shapefile 来源缺少服务端确认的目标工程投影 WKT。' : 'The Shapefile source has no service-confirmed target project WKT.')
                        : operationCannotPreserveNullableOrdinates ? (zh ? '此工具要求可表达有限 Z/M 值；来源包含空值，执行已锁定。' : 'This operation requires finite Z/M values; the source contains null ordinates, so execution is locked.')
                        : outputHandler === 'Shapefile' && !shapefileCaptureAvailable ? (zh ? '当前服务未开放输出文件及完整文件族核验。' : 'Output inspection and complete family capture are unavailable on the current service.')
                          : outputHandler === 'Shapefile' && !outputName.trim() ? (zh ? '请填写服务器输出名称。' : 'Enter a server output name.')
                      : hasBlockingValidation ? (zh ? '请先修正后端参数校验中的错误。' : 'Resolve backend parameter validation errors first.')
                        : (zh ? '运行服务端矢量工具' : 'Run the vector tool')}>
                {execution.kind === 'running' ? (zh ? '执行中…' : 'Running…') : execution.kind === 'uncertain' ? (zh ? '终态未确认' : 'Outcome unconfirmed') : (zh ? '运行工具' : 'Run tool')}
              </button>
              : <button className="tools-button tools-button-primary" type="button" disabled title={zh ? '当前工具的真实来源几何与执行回执尚未接通' : 'Verified source geometry and execution receipts are not connected for this tool'}>{zh ? '运行工具' : 'Run tool'}</button>}
          </div>

          {validation && <section className="tools-validation-result"><div className="tools-subheading-row"><h4>{zh ? '后端参数校验' : 'Backend parameter validation'}</h4><span className="tools-badge">{validation.validationIssues.length} {zh ? '项' : 'issues'}</span></div>
            {validation.validationIssues.length ? validation.validationIssues.map((issue, index) => {
              const field = activeTool.fields.find(candidate => candidate.key.toLocaleLowerCase() === issue.field.toLocaleLowerCase());
              return <div className={`tools-validation-item severity-${issue.severity.toLowerCase()}`} key={`${issue.field}-${index}`}>
                <strong>{zh ? '字段：' : 'Field: '}{field?.label[language] ?? issue.field}</strong><span>{issue.message}</span><small>{severityLabel(issue.severity)}</small></div>;
            }) : <div className="tools-notice is-success">{zh ? '服务端未返回参数错误。此回执只确认参数校验，不代表输入源或执行已验证。' : 'The service returned no parameter errors. This receipt confirms parameter validation only, not source reading or execution.'}</div>}
          </section>}
          {validationError && <div className="tools-inline-error" role="alert">{validationError}{validationDiagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{validationDiagnostic}</pre></details>}</div>}
          <div className="tools-execution-boundary">{!isSupportedVectorTool
            ? (zh ? '此工具尚未接通可核验的实际输入与结果回执；可编辑和校验参数，但不能执行。' : 'This tool has no verified input and result path yet; parameters can be edited and validated, but execution remains disabled.')
            : !executeAvailable ? (zh ? '当前服务未开放矢量工具执行。' : 'Vector execution is unavailable on the current service.')
              : operationCannotPreserveNullableOrdinates ? (zh ? '来源含空值 Z/M；当前这类工具要求有限 Z/M 值，因此执行保持禁用。' : 'The source contains null Z/M values; this operation requires finite ordinates, so execution remains disabled.')
              : execution.kind === 'uncertain' ? (zh ? '本次执行终态未确认；请先检查服务端操作状态。修改参数或重新读取来源后可发起新请求。' : 'The execution terminal state is unconfirmed. Check the service operation before starting a new request; changing parameters or rereading the source resets this state.')
                : outputHandler === 'Shapefile' ? (zh ? 'Shapefile 将写入服务器当前工程 Exports 目录；仅在检查主文件并捕获完整文件族后确认可用。地图图层提交仍未开放。' : 'Shapefile output is written under the active project’s server Exports directory and is confirmed only after primary-file inspection and complete family capture. Map-layer commits remain unavailable.')
                  : (zh ? '使用服务端返回的实际文本预览；可复制或下载服务端结果。地图图层提交仍未开放。' : 'Preview the actual text returned by the service and copy or download it. Map-layer commits remain unavailable.')}</div>
          {isSupportedVectorTool
            ? <GeoprocessingPolylineExecutionDetails language={language} tool={activeTool} view={execution}
              operationID={executionOperationID.current} onCancel={cancelVectorExecution} />
            : <GeoprocessingOutputDetails language={language} running={false} />}
        </main>
      </div>
    </div>
  );
}
