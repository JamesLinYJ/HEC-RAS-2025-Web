import { useEffect, useMemo, useRef, useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { BoundaryConditionEditor, BOUNDARY_EDITOR_PAGE_IDS } from './BoundaryConditionEditor';
import { ClassificationEditor } from './ClassificationEditor';
import { EditorCatalogBrowser } from './EditorCatalogBrowser';
import { NValueEditor } from './NValueEditor';
import type { ClassificationMapData, ClassificationTransformCommand, EditorPageProps, EditorSchema, EditorPageProps as PageProps, ManningTransformOperation, ModelDraft, ModelSnapshot, NValuePreviewData, SeriesTransformOperation, SpatialGridData, SpatialGridInspection, SpatialGridSelection, TerrainTransformCommand } from './model';
import { pageTitle, schemaForAsset, snapshotRows } from './model';
import { changedModelCells, createModelDraft, isModelDraftDirty, updateModelSample, validateChangedSamples } from './modelDraft';
import { validateChangedTableCells, validateModelDraft } from './SchemaFieldRenderer';
import { loadEditorCatalog } from './schemaCatalog';
import { editorAssetKind, editorValueLabel } from './editorLocalization';
import { PlanEditorControl } from './PlanEditorControl';
import { TerrainEditor } from './TerrainEditor';
import { walkCatalogTree } from '../project/CatalogTreeSnapshot';
import type { CatalogTreeSnapshot } from '../project/CatalogTreeSnapshot';
import type { BoundaryInitialConditionsInspection, BoundaryInitialConditionsOperationState, BoundaryInitialConditionsReceipt, BoundaryInitialConditionsStatus, BoundaryInitialConditionsWorkflow, BoundaryResultCatalogItem } from './model';
import { AttachInitialConditionsDialog } from './AttachInitialConditionsDialog';
import { ModelAssociationsPanel } from './associations/ModelAssociationsPanel';
import type { TerrainMapProjectOwner, TerrainMapViewport, TerrainTopologyResponse } from './terrainTopologyTypes';
import { projectXmlSha } from '../map/ProjectTreeListener';

type DisplayMode = 'model' | 'catalog';
interface SourceContext { context: WorkbenchContext; owner: string }
type Transition =
  | { kind: 'page'; pageID: string }
  | { kind: 'view'; view: DisplayMode }
  | { kind: 'source'; source: SourceContext };
interface CatalogState { schemas: EditorSchema[]; tables: NonNullable<PageProps['tableSchemas']> }
interface LoadState { status: 'idle' | 'loading' | 'unavailable' | 'error' | 'ready'; error?: string }
interface BoundaryInitialConditionsAttachRequest {
  model: Record<string, unknown>;
  projectRevision: string;
  resultPath: string;
  resultRevision: string;
  operationID: string;
  timestamp: string;
  numberCulture: string;
  projectXmlFingerprint: string;
}
interface BoundaryInitialConditionsOperation {
  owner: string;
  ownerKey: string;
  generation: number;
  baselineLineIdentity: Array<{ id: string; scope: string }>;
  request: BoundaryInitialConditionsAttachRequest;
  inspection: BoundaryInitialConditionsInspection;
  state: BoundaryInitialConditionsOperationState;
  message: string;
  receipt?: BoundaryInitialConditionsReceipt;
}

export default function EditorWorkbench(props: FeaturePageProps) {
  const { backend, backendRevision, language, onContextChange, onContextCommit, onDirtyStateChange } = props;
  const [source, setSource] = useState<SourceContext>(() => sourceFor(props.context));
  const activeOwner = useRef(source.owner);
  activeOwner.current = source.owner;
  const [snapshot, setSnapshot] = useState<ModelSnapshot | null>(null);
  const snapshotRef = useRef<ModelSnapshot | null>(snapshot);
  snapshotRef.current = snapshot;
  const [draft, setDraft] = useState<ModelDraft | null>(null);
  const draftRef = useRef<ModelDraft | null>(draft);
  draftRef.current = draft;
  const [loadState, setLoadState] = useState<LoadState>({ status: 'idle' });
  const [catalog, setCatalog] = useState<CatalogState>({ schemas: [], tables: [] });
  const [catalogError, setCatalogError] = useState('');
  const [catalogSearch, setCatalogSearch] = useState('');
  const [selectedSchemaID, setSelectedSchemaID] = useState('');
  const [pageID, setPageID] = useState(() => defaultPage(props.context.selectedAsset?.kind ?? ''));
  const [view, setView] = useState<DisplayMode>('model');
  const [pending, setPending] = useState<Transition | null>(null);
  const [busy, setBusy] = useState(false);
  const [saveMessage, setSaveMessage] = useState('');
  const [saveErrorDetails, setSaveErrorDetails] = useState('');
  const [saveErrors, setSaveErrors] = useState<Array<{ fieldID: string; title: string; message: string }>>([]);
  const generation = useRef(0);
  const pendingSaveContinuation = useRef<Transition | null>(null);
  const activeSave = useRef<{ owner: string; controller: AbortController } | null>(null);
  const acceptedReceipt = useRef<{ owner: string; snapshot: ModelSnapshot } | null>(null);
  const loadedOwner = useRef<string | null>(null);
  const [initialConditionsOperation, setInitialConditionsOperation] = useState<BoundaryInitialConditionsOperation | null>(() =>
    restoreInitialConditionsOperation(props.context, source.owner));
  const initialConditionsOperationRef = useRef<BoundaryInitialConditionsOperation | null>(null);
  initialConditionsOperationRef.current = initialConditionsOperation;
  const initialConditionsCatalogRef = useRef<{ owner: string; items: BoundaryResultCatalogItem[] } | null>(null);
  const initialConditionsInspectionRef = useRef<{ owner: string; inspection: BoundaryInitialConditionsInspection } | null>(null);

  const updateInitialConditionsOperation = (operation: BoundaryInitialConditionsOperation | null): void => {
    initialConditionsOperationRef.current = operation;
    setInitialConditionsOperation(operation);
    persistInitialConditionsOperation(operation);
  };

  const desiredSource = useMemo(() => sourceFor(props.context), [props.context]);
  const dirty = useMemo(() => {
    const currentDraft = draft ?? emptyDraft();
    const selectionBaselines = {
      ...(currentDraft.terrainSelectionBaselineFields ?? {}),
      ...(currentDraft.classificationSelectionBaselineFields ?? {}),
    };
    if (!snapshot || Object.keys(selectionBaselines).length === 0) return isModelDraftDirty(snapshot, currentDraft);
    const fields = { ...currentDraft.fields };
    for (const [fieldID, baselineValue] of Object.entries(selectionBaselines)) {
      const currentValue = Object.prototype.hasOwnProperty.call(fields, fieldID) ? fields[fieldID] : null;
      if (currentValue !== baselineValue) continue;
      if (Object.prototype.hasOwnProperty.call(snapshot.fields, fieldID)) fields[fieldID] = snapshot.fields[fieldID];
      else delete fields[fieldID];
    }
    return isModelDraftDirty(snapshot, { ...currentDraft, fields });
  }, [snapshot, draft]);
  const asset = source.context.selectedAsset;

  useEffect(() => {
    let cancelled = false;
    void loadEditorCatalog().then((loaded) => {
      if (cancelled) return;
      setCatalog(loaded);
      setSelectedSchemaID((current) => current || loaded.schemas[0]?.id || '');
    }).catch((error: unknown) => { if (!cancelled) setCatalogError(message(error)); });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (desiredSource.owner === source.owner || pending?.kind === 'source') return;
    if (dirty) setPending({ kind: 'source', source: desiredSource });
    else acceptSource(desiredSource);
  }, [desiredSource, dirty, pending?.kind, source.owner]);

  useEffect(() => {
    const operation = initialConditionsOperationRef.current;
    if (!operation || operation.owner === source.owner
      || operation.ownerKey !== boundaryInitialConditionsOwnerKey(source.context, operation.request.projectXmlFingerprint)) return;
    updateInitialConditionsOperation({ ...operation, owner: source.owner, generation: generation.current });
  }, [source.owner, source.context]);

  useEffect(() => {
    if (!onDirtyStateChange) return;
    onDirtyStateChange(source.owner, dirty);
    return () => onDirtyStateChange(source.owner, false);
  }, [dirty, onDirtyStateChange, source.owner]);

  useEffect(() => () => {
    const request = activeSave.current;
    if (request?.owner === source.owner) {
      activeSave.current = null;
      request.controller.abort();
    }
  }, [source.owner]);

  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController();
    const activeAsset = source.context.selectedAsset;
    // Reconnecting transport must not replace an already loaded editor draft.
    if (loadedOwner.current === source.owner) return () => controller.abort();
    loadedOwner.current = null;
    const receipt = acceptedReceipt.current;
    if (receipt?.owner === source.owner) {
      loadedOwner.current = source.owner;
      setSnapshot(receipt.snapshot);
      setDraft(createModelDraft(receipt.snapshot));
      setLoadState({ status: 'ready' });
      return () => { controller.abort(); };
    }
    acceptedReceipt.current = null;
    setSnapshot(null);
    setDraft(null);
    setSaveMessage('');
    setSaveErrors([]);
    setSaveErrorDetails('');
    if (!activeAsset || pagesFor(activeAsset.kind).length === 0) {
      setLoadState({ status: 'idle' });
      return () => { controller.abort(); };
    }
    if (!backend.supports('model.load')) {
      setLoadState({ status: 'unavailable' });
      return () => { controller.abort(); };
    }
    setLoadState({ status: 'loading' });
    const parameters = modelRequest(source.context, activeAsset);
    void backend.request<ModelSnapshot>('model.load', parameters, { signal: controller.signal }).then((result) => {
      if (generation.current !== current || controller.signal.aborted) return;
      assertModelSnapshot(result);
      loadedOwner.current = source.owner;
      setSnapshot(result);
      setDraft(createModelDraft(result));
      setLoadState({ status: 'ready' });
      setPageID((page) => pagesFor(activeAsset.kind, result).includes(page) ? page : defaultPage(activeAsset.kind, result));
    }).catch((error: unknown) => {
      if (generation.current !== current || controller.signal.aborted) return;
      setLoadState({ status: 'error', error: message(error) });
    });
    return () => { controller.abort(); if (generation.current === current) generation.current += 1; };
  }, [backend, backendRevision, source.owner]);

  function acceptSource(next: SourceContext): void {
    activeOwner.current = next.owner;
    const unresolvedInitialConditions = initialConditionsOperationRef.current;
    if (unresolvedInitialConditions?.state === 'attaching' && unresolvedInitialConditions.owner !== next.owner) {
      updateInitialConditionsOperation({
        ...unresolvedInitialConditions,
        state: 'uncertain',
        message: text(language, '请求期间切换了模型；请返回原工程会话查询同一操作编号。', 'The source changed while the request was running. Return to the original project session to check the same operation ID.'),
      });
    } else if (unresolvedInitialConditions?.state === 'conflict' && unresolvedInitialConditions.owner !== next.owner) {
      updateInitialConditionsOperation(null);
    }
    if (activeSave.current) {
      activeSave.current.controller.abort();
      activeSave.current = null;
    }
    if (acceptedReceipt.current?.owner !== next.owner) acceptedReceipt.current = null;
    setPending(null);
    setBusy(false);
    setSource(next);
    setPageID(defaultPage(next.context.selectedAsset?.kind ?? ''));
    setView(next.context.selectedAsset ? 'model' : 'catalog');
    setSaveMessage('');
    setSaveErrors([]);
    setSaveErrorDetails('');
  }

  const pageIDs = asset ? pagesFor(asset.kind, snapshot ?? undefined) : [];
  const activePageID = pageIDs.includes(pageID) ? pageID : pageIDs[0] ?? '';
  const schemasForModel = useMemo(() => {
    const supported = new Set(schemaForAsset(asset));
    const required = new Set(pageIDs);
    return catalog.schemas.filter((schema) => supported.has(schema.id) || required.has(schema.id));
  }, [asset?.kind, catalog.schemas, pageIDs.join('|')]);
  const validationSnapshot = snapshot && draft?.boundaryView
    ? {
      ...snapshot,
      boundary: draft.boundaryView,
      fieldGroups: draft.boundaryFieldGroups ?? snapshot.fieldGroups,
      readOnlyFields: [...new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? [])])],
    }
    : snapshot && draft?.terrainReadOnlyFields
      ? { ...snapshot, readOnlyFields: [...new Set([...(snapshot.readOnlyFields ?? []), ...draft.terrainReadOnlyFields])] }
      : snapshot;
  const validations = snapshot && draft ? [
    ...validateModelDraft(draft, schemasForModel, validationSnapshot ?? snapshot, language),
    ...validateChangedTableCells(draft, validationSnapshot ?? snapshot, catalog.tables, language),
    ...validateChangedSamples(validationSnapshot ?? snapshot, draft, language),
  ] : [];

  const onFieldChange = (fieldID: string, value: string): void => {
    if (!snapshot?.canEdit || activeSave.current) return;
    setDraft((current) => {
      if (!current) return current;
      const next = { ...current, fields: { ...current.fields, [fieldID]: value } };
      if (fieldID === 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput') {
        next.classificationSelectionBaselineFields = {
          ...(current.classificationSelectionBaselineFields ?? {}),
          [fieldID]: value,
        };
      }
      return next;
    });
    setSaveMessage('');
  };
  const onImagesChange = (fieldID: string, images: ModelDraft['images'][string]): void => {
    if (!snapshot?.canEdit || activeSave.current || activeOwner.current !== source.owner) return;
    setDraft((current) => current ? { ...current, images: { ...current.images, [fieldID]: images } } : current);
    setSaveMessage('');
  };
  const onRowCellChange = (tableID: string, rowID: string, columnID: string, value: string): void => {
    if (!snapshot?.canEdit || activeSave.current) return;
    setDraft((current) => {
      if (!current) return current;
      const rows = current.rows[tableID] ?? [];
      const nextRows = rows.map((row) => row.id === rowID ? { ...row, values: { ...row.values, [columnID]: value } } : row);
      const next = { ...current, rows: { ...current.rows, [tableID]: nextRows } };
      return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
    });
    setSaveMessage('');
  };
  const onSampleChange: PageProps['onSampleChange'] = (seriesID, sampleID, column, value) => {
    if (!snapshot?.canEdit || activeSave.current) return;
    setDraft((current) => {
      if (!current) return current;
      const boundary = current.boundaryView ?? snapshot.boundary;
      const currentSnapshot = boundary ? { ...snapshot, boundary } : snapshot;
      return updateModelSample(current, currentSnapshot, seriesID, sampleID, column, value);
    });
    setSaveMessage('');
  };

  const onSeriesTransform: PageProps['onSeriesTransform'] = async (pageID, operation) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return;
    const series = (draft?.boundaryView ?? snapshot?.boundary)?.series[pageID];
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !series?.operations.includes(operation)) return;
    if (!backend.supports('model.seriesTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供时序调整和保存能力。', 'The C# server has not advertised time-series editing and save capabilities.'));
      return;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能调整时序行。', 'Correct invalid draft values before changing time-series rows.'));
      return;
    }
    const currentRows = series.tableId ? (draft.rows[series.tableId] ?? series.rows) : null;
    const currentSamples = draft.samples[pageID] ?? series.samples;
    const currentRowCount = currentRows?.length ?? currentSamples.length;
    if (operation === 'removeLastRow' && currentRowCount === 0) return;
    const currentRowIDs = currentRows?.map((row) => row.id) ?? currentSamples.map(sampleIdentityKey);
    const currentStationRows = series.tableId
      ? draft.rows[`${pageID}/Stations`] ?? series.stations
      : [];
    const currentStationIDs = currentStationRows.map((row) => row.id);
    if (new Set(currentRowIDs).size !== currentRowIDs.length || currentRowIDs.some((id) => !id)
      || currentSamples.some((sample) => !sample.id)
      || new Set(currentStationIDs).size !== currentStationIDs.length || currentStationIDs.some((id) => !id)) {
      setSaveMessage(text(language, '当前时序行身份不唯一，无法调整结构。', 'The current time-series row identities are not unique, so the structure cannot be changed.'));
      return;
    }

    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const boundaryReadOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? [])]);
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => value !== snapshot.fields[id] && !boundaryReadOnlyFields.has(id)));
    const request = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: {},
      sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
        ? draft.sourceState
        : snapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows: draft.rows,
      samples: samplesForBoundaryDraft(snapshot, draft),
      changedCells: changedModelCells(draft, snapshotRows(snapshot)),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.seriesTransform', { model: request, pageId: pageID, operation }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision) throw new Error('The series transform returned a different source revision.');
      const transformedSeries = result.boundary?.series[pageID];
      if (!transformedSeries) throw new Error('The backend did not return the transformed time series.');
      if (transformedSeries.pageId !== series.pageId || transformedSeries.scope !== series.scope
        || transformedSeries.schemaId !== series.schemaId || transformedSeries.tableId !== series.tableId) {
        throw new Error('The backend returned a different time-series identity.');
      }
      const transformedRowCount = transformedSeries.tableId ? transformedSeries.rows.length : transformedSeries.samples.length;
      const rowDelta = operation === 'appendRow' ? 1 : operation === 'removeLastRow' ? -1 : 0;
      const expectedRowCount = currentRowCount + rowDelta;
      if (transformedRowCount !== expectedRowCount) throw new Error('The backend time-series row count did not match the requested operation.');
      const transformedRowIDs = transformedSeries.tableId
        ? transformedSeries.rows.map((row) => row.id)
        : transformedSeries.samples.map(sampleIdentityKey);
      const retainedRowIDs = operation === 'removeLastRow' ? currentRowIDs.slice(0, -1) : currentRowIDs;
      if (new Set(transformedRowIDs).size !== transformedRowIDs.length
        || transformedRowIDs.some((id) => !id)
        || retainedRowIDs.some((id, index) => transformedRowIDs[index] !== id)
        || (operation === 'appendRow' && currentRowIDs.includes(transformedRowIDs[transformedRowIDs.length - 1]))) {
        throw new Error('The backend time-series transform did not preserve stable row identities.');
      }
      const currentSampleIDs = currentSamples.map(sampleIdentityKey);
      const transformedSampleIDs = transformedSeries.samples.map(sampleIdentityKey);
      if (new Set(transformedSampleIDs).size !== transformedSampleIDs.length || transformedSampleIDs.some((id) => !id)
        || transformedSeries.samples.some((sample) => !sample.id)) {
        throw new Error('The backend time-series transform returned duplicate sample identities.');
      }
      const currentSampleSet = new Set(currentSampleIDs);
      const survivingSamples = transformedSampleIDs.filter((id) => currentSampleSet.has(id));
      const lastRowID = currentRowIDs[currentRowIDs.length - 1];
      const expectedSamples = operation === 'removeLastRow'
        ? series.tableId
          ? currentSampleIDs.filter((id) => !id.startsWith(`${lastRowID}\u0000`))
          : currentSampleIDs.slice(0, -1)
        : currentSampleIDs;
      if (survivingSamples.length !== expectedSamples.length || expectedSamples.some((id, index) => survivingSamples[index] !== id)) {
        throw new Error('The backend time-series transform did not preserve stable sample identities.');
      }
      const sampleDelta = series.tableId ? series.columns.filter((column) => column.id !== 'Time').length : 1;
      const expectedSampleCount = operation === 'appendRow' ? currentSampleIDs.length + sampleDelta
        : operation === 'removeLastRow' ? Math.max(0, currentSampleIDs.length - sampleDelta)
          : currentSampleIDs.length;
      if (operation === 'addStation' ? transformedSampleIDs.length < currentSampleIDs.length
        : transformedSampleIDs.length !== expectedSampleCount) {
        throw new Error('The backend changed the number of samples outside the requested operation.');
      }
      if (currentRows && transformedSeries.tableId && ['appendRow', 'removeLastRow', 'addStation'].includes(operation)) {
        const retainedRows = operation === 'removeLastRow' ? currentRows.slice(0, -1) : currentRows;
        if (retainedRows.some((row, index) => {
          const transformed = transformedSeries.rows[index];
          return !transformed || series.columns.some((column) => !equivalentCellValue(row.values[column.id] ?? '', transformed.values[column.id] ?? '', column.sourceType));
        })) {
          throw new Error('The backend did not preserve edited time-series cells while changing its structure.');
        }
      } else if (!currentRows && ['appendRow', 'removeLastRow'].includes(operation)) {
        const retainedSamples = operation === 'removeLastRow' ? currentSamples.slice(0, -1) : currentSamples;
        const transformedByID = new Map(transformedSeries.samples.map((sample) => [sampleIdentityKey(sample), sample]));
        if (retainedSamples.some((sample) => {
          const transformed = transformedByID.get(sampleIdentityKey(sample));
          return !transformed || transformed.x !== sample.x || transformed.y !== sample.y || transformed.series !== sample.series;
        })) {
          throw new Error('The backend did not preserve edited time-series samples while changing its structure.');
        }
      }
      if (operation === 'addStation') {
        const transformedStationIDs = transformedSeries.stations.map((row) => row.id);
        const transformedColumnIDs = transformedSeries.columns.map((column) => column.id);
        const newStationID = `station/${currentStationIDs.length}`;
        if (!series.tableId || transformedStationIDs.length !== currentStationIDs.length + 1
          || currentStationIDs.some((id, index) => transformedStationIDs[index] !== id)
          || transformedStationIDs[transformedStationIDs.length - 1] !== newStationID
          || transformedColumnIDs.length !== series.columns.length + 1
          || series.columns.some((column, index) => transformedColumnIDs[index] !== column.id)
          || transformedColumnIDs[transformedColumnIDs.length - 1] !== newStationID) {
          throw new Error('The backend did not preserve station identities while adding a station.');
        }
        if (currentStationRows.some((row, index) => {
          const transformed = transformedSeries.stations[index];
          return !transformed || !equivalentCellValue(row.values.Station ?? '', transformed.values.Station ?? '', 'double');
        })) {
          throw new Error('The backend did not preserve edited station positions while adding a station.');
        }
        const payloadID = /\/Payload\/([0-9a-f]{32})$/i.exec(series.scope)?.[1];
        const addedSamples = transformedSeries.samples.filter((sample) => !currentSampleSet.has(sampleIdentityKey(sample)));
        const addedSampleIDs = addedSamples.map((sample) => sample.id);
        if (!payloadID || addedSamples.some((sample) => sample.datasetId !== `${payloadID}/${newStationID}`)
          || !addedSampleIDs.every((id, index) => id === String(index))) {
          throw new Error('The backend did not bind new station samples to the payload and stable time-row identities.');
        }
      }
      const transformedDraft = createModelDraft(result);
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: transformedDraft.fields,
          rows: transformedDraft.rows,
          samples: transformedDraft.samples,
          sampleText: transformedDraft.sampleText,
          ...(result.boundary ? { boundaryView: result.boundary } : {}),
          boundaryFieldGroups: result.fieldGroups,
          boundaryReadOnlyFields: result.readOnlyFields ?? [],
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      const messages: Record<SeriesTransformOperation, [string, string]> = {
        appendRow: ['已在草稿中追加时序末行；点击“保存”后才会写入模型文件。', 'The last time-series row was added to the draft. Save to write it to the model file.'],
        removeLastRow: ['已在草稿中移除时序末行；点击“保存”后才会写入模型文件。', 'The last time-series row was removed from the draft. Save to write it to the model file.'],
        clear: ['已清空草稿中的时序数值；点击“保存”后才会写入模型文件。', 'Time-series values were cleared in the draft. Save to write the change to the model file.'],
        interpolate: ['已在草稿中完成时序插值；点击“保存”后才会写入模型文件。', 'Time-series values were interpolated in the draft. Save to write the change to the model file.'],
        addStation: ['已在草稿中添加测站；点击“保存”后才会写入模型文件。', 'A station was added to the draft. Save to write the change to the model file.'],
      };
      setSaveMessage(text(language, messages[operation][0], messages[operation][1]));
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '时序行调整失败。', 'The time-series row could not be changed.'));
        setSaveErrorDetails(message(error));
      }
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onBoundaryTransform: PageProps['onBoundaryTransform'] = async (lineID, operation, payloadID, payloadFields) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const boundaryView = draft?.boundaryView ?? snapshot?.boundary;
    const line = boundaryView?.lines.find((candidate) => candidate.id === lineID);
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !line) return false;
    if (!backend.supports('model.boundaryTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供载荷调整和保存能力。', 'The C# server has not advertised payload editing and save capabilities.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能调整边界载荷。', 'Correct invalid draft values before changing boundary payloads.'));
      return false;
    }
    const beforePayloadIDs = line.payloads.map((payload) => payload.id);
    if (operation === 'create' && (payloadID !== null || beforePayloadIDs.some((id) => id.length === 0))) return false;
    if (operation === 'remove' && (!payloadID || !beforePayloadIDs.includes(payloadID) || Object.keys(payloadFields).length > 0)) return false;

    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const readOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? [])]);
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => value !== snapshot.fields[id] && !readOnlyFields.has(id)));
    const previousSourceState = draft.sourceState !== (snapshot.sourceState ?? null)
      ? draft.sourceState
      : snapshot.sourceState ?? sourceStateFor(asset) ?? null;
    const request = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: {},
      sourceState: previousSourceState,
      rows: draft.rows,
      samples: samplesForBoundaryDraft(snapshot, draft),
      changedCells: changedModelCells(draft, snapshotRows(snapshot)),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.boundaryTransform', {
        model: request, lineId: lineID, operation, payloadId: payloadID, fields: payloadFields,
      }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision) throw new Error('The boundary transform returned a different source revision.');
      if (!result.boundary || !result.sourceState || result.sourceState === previousSourceState) {
        throw new Error('The backend did not return a changed, revision-bound boundary draft.');
      }
      const transformedLine = result.boundary.lines.find((candidate) => candidate.id === lineID);
      if (!transformedLine) throw new Error('The boundary transform returned another line selection.');
      if (operation === 'create') {
        if (transformedLine.payloads.length !== beforePayloadIDs.length + 1
          || beforePayloadIDs.some((id, index) => transformedLine.payloads[index]?.id !== id)) {
          throw new Error('The backend did not preserve existing payload identities while creating a payload.');
        }
        const created = transformedLine.payloads[transformedLine.payloads.length - 1];
        if (!created || beforePayloadIDs.includes(created.id) || !/^[0-9a-f]{32}$/i.test(created.id)
          || created.scope !== `${line.scope}/Payload/${created.id}`) {
          throw new Error('The backend did not return a stable payload identity and source scope.');
        }
      } else if (transformedLine.payloads.some((payload) => payload.id === payloadID)
        || transformedLine.payloads.length !== beforePayloadIDs.length - 1
        || beforePayloadIDs.filter((id) => id !== payloadID).some((id, index) => transformedLine.payloads[index]?.id !== id)) {
        throw new Error('The backend did not preserve remaining payload identities while removing a payload.');
      }
      const transformedDraft = createModelDraft(result);
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: transformedDraft.fields,
          rows: transformedDraft.rows,
          samples: transformedDraft.samples,
          sampleText: transformedDraft.sampleText,
          boundaryView: result.boundary,
          boundaryFieldGroups: result.fieldGroups,
          boundaryReadOnlyFields: result.readOnlyFields ?? [],
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      setSaveMessage(operation === 'create'
        ? text(language, '载荷已加入草稿；点击“保存”后才会写入模型文件。', 'The payload was added to the draft. Save to write it to the model file.')
        : text(language, '载荷已从草稿移除；点击“保存”后才会写入模型文件。', 'The payload was removed from the draft. Save to write it to the model file.'));
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(operation === 'create'
          ? text(language, '添加边界载荷失败。', 'The boundary payload could not be added.')
          : text(language, '移除边界载荷失败。', 'The boundary payload could not be removed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const initialConditionsModelRequest = () => {
    const currentSnapshot = snapshotRef.current;
    const currentAsset = source.context.selectedAsset;
    if (!currentSnapshot || !currentAsset || !source.context.projectPath || !source.context.projectRevision
      || !source.context.projectXml || currentAsset.kind !== 'Boundary Conditions') {
      throw new Error('需要已确认的边界条件、所属工程和源修订。');
    }
    return {
      ...modelRequest(source.context, currentAsset),
      // The initial-conditions operation changes only the retained initial-condition
      // source. Keep its exact retry body small and independent of editor table drafts.
      projectXml: null,
      revision: currentSnapshot.revision,
      fields: {},
      images: {},
      sourceState: currentSnapshot.sourceState ?? sourceStateFor(currentAsset) ?? null,
      rows: {},
      samples: {},
      changedCells: {},
    };
  };

  const onInitialConditionsLoadResults: NonNullable<PageProps['boundaryInitialConditions']>['loadResults'] = async (signal) => {
    const currentSnapshot = snapshotRef.current;
    const currentAsset = source.context.selectedAsset;
    if (!currentSnapshot || !currentAsset || currentAsset.kind !== 'Boundary Conditions'
      || !source.context.projectPath || !source.context.projectXml || !source.context.projectRevision
      || !backend.supports('project.tree.load')) throw new Error('当前工程结果目录不可用。');
    const owner = source.owner;
    const requestGeneration = generation.current;
    const projectRevision = source.context.projectRevision;
    const tree = await backend.request<CatalogTreeSnapshot>('project.tree.load', {
      projectPath: source.context.projectPath,
      projectXml: source.context.projectXml,
      projectSessionID: source.context.projectSessionID,
      mode: 'Project',
      ...(coreLayerContext(source.context, currentAsset) ? { coreLayerContext: coreLayerContext(source.context, currentAsset) } : {}),
    }, { signal });
    if (signal?.aborted || activeOwner.current !== owner || generation.current !== requestGeneration
      || source.context.projectRevision !== projectRevision || snapshotRef.current?.revision !== currentSnapshot.revision) {
      throw new DOMException('工程结果目录属于已切换的工程会话。', 'AbortError');
    }
    if (!tree || !Array.isArray(tree.roots)) throw new Error('工程目录服务返回了不完整的结果目录。');
    const candidates = walkCatalogTree(tree.roots).filter((node) => node.sourceClass === 'ObservableResult'
      && typeof node.assetID === 'string' && node.assetID.length > 0);
    const paths = new Set<string>();
    const items = candidates.flatMap((node): BoundaryResultCatalogItem[] => {
      const path = node.assetID!;
      if (sameServerPath(path, currentAsset.path) || sameServerPath(path, source.context.projectPath!) || paths.has(path)) return [];
      paths.add(path);
      return [{ id: node.id, name: node.name, path }];
    });
    initialConditionsCatalogRef.current = { owner, items };
    return items;
  };

  const onInitialConditionsInspect: NonNullable<PageProps['boundaryInitialConditions']>['inspect'] = async (resultPath, timestamp, signal) => {
    const currentSnapshot = snapshotRef.current;
    const currentDraft = draftRef.current;
    const currentAsset = source.context.selectedAsset;
    if (!currentSnapshot || !currentDraft || !currentAsset || currentAsset.kind !== 'Boundary Conditions'
      || !source.context.projectPath || !source.context.projectRevision || !source.context.projectXml
      || !currentSnapshot.canEdit || dirty || validations.length > 0 || activeSave.current || busy) {
      throw new Error('请先保存或丢弃草稿，并修正字段问题；初始条件只能从干净的可编辑快照检查。');
    }
    if (!backend.supports('boundary.initialConditions.inspect') || !backend.supports('project.tree.load'))
      throw new Error('当前服务未提供初始条件检查和工程结果目录能力。');
    if (!resultPath || !isSha256(currentSnapshot.revision) || !isSha256(source.context.projectRevision))
      throw new Error('当前边界条件或工程修订无效。');
    if (initialConditionsCatalogRef.current?.owner !== source.owner
      || !initialConditionsCatalogRef.current.items.some((result) => sameServerPath(result.path, resultPath))) {
      throw new Error('请选择刚从当前工程目录读取的真实结果。');
    }

    const owner = source.owner;
    const requestGeneration = generation.current;
    const controller = new AbortController();
    const abortFromCaller = () => controller.abort();
    signal?.addEventListener('abort', abortFromCaller, { once: true });
    const requestState = { owner, controller };
    activeSave.current = requestState;
    setBusy(true);
    setSaveMessage('');
    setSaveErrorDetails('');
    try {
      const projectXmlFingerprint = await sha256Text(source.context.projectXml);
      if (controller.signal.aborted || activeSave.current !== requestState || activeOwner.current !== owner
        || generation.current !== requestGeneration || snapshotRef.current?.revision !== currentSnapshot.revision)
        throw new DOMException('工程或边界条件在检查前已切换。', 'AbortError');
      const inspection = await backend.request<BoundaryInitialConditionsInspection>('boundary.initialConditions.inspect', {
        model: initialConditionsModelRequest(),
        projectRevision: source.context.projectRevision,
        resultPath,
        timestamp: timestamp.trim() || null,
        numberCulture: getNumberCulture(),
        projectXmlFingerprint,
      }, { signal: controller.signal });
      if (activeSave.current !== requestState || activeOwner.current !== owner || generation.current !== requestGeneration
        || controller.signal.aborted || snapshotRef.current?.revision !== currentSnapshot.revision)
        throw new DOMException('初始条件检查属于已切换的工程或边界条件修订。', 'AbortError');
      assertInitialConditionsInspection(inspection, {
        assetPath: currentAsset.path,
        assetRevision: currentSnapshot.revision,
        projectPath: source.context.projectPath,
        projectRevision: source.context.projectRevision,
        projectXmlFingerprint,
        resultPath,
      });
      initialConditionsInspectionRef.current = { owner, inspection };
      return inspection;
    } finally {
      signal?.removeEventListener('abort', abortFromCaller);
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const attachInitialConditionsReceipt = async (
    operation: BoundaryInitialConditionsOperation,
    receipt: BoundaryInitialConditionsReceipt,
    isCurrentRequest: () => boolean,
  ): Promise<boolean> => {
    assertModelSnapshot(receipt?.model);
    await assertInitialConditionsReceipt(receipt, operation);
    if (initialConditionsOperationRef.current?.request.operationID !== operation.request.operationID) return false;
    if (!isCurrentRequest()) {
      updateInitialConditionsOperation({
        ...operation,
        receipt,
        state: 'awaitingContext',
        message: text(language, '服務端回執已核驗，但原工程會話已切換；返回原會話後可重新接納回執。', 'The server receipt is verified, but the original project session is no longer active. Return to that session to accept it.'),
      });
      return false;
    }

    const currentAsset = source.context.selectedAsset;
    if (!currentAsset || currentAsset.kind !== 'Boundary Conditions'
      || !sameServerPath(currentAsset.path, operation.inspection.assetPath)) return false;
    const nextContext = updatedContextAfterInitialConditions(source.context, currentAsset, receipt);
    const nextSource = sourceFor(nextContext);
    const accepted = onContextCommit?.(source.context, nextContext) === true;
    if (!accepted) {
      updateInitialConditionsOperation({
        ...operation,
        receipt,
        state: 'awaitingContext',
        message: text(language, '工程会话尚未接纳新工程 XML、工程修订和边界条件快照；可重试接纳回执，不会再次写入。', 'The project session has not accepted the new project XML, project revision, and boundary snapshot. Retry receipt acceptance without repeating the write.'),
      });
      setSaveMessage(text(language, '初始条件已由服务端提交，正在等待工程会话接纳回执。', 'Initial conditions were committed by the service; waiting for the project session to accept the receipt.'));
      return false;
    }

    acceptedReceipt.current = { owner: nextSource.owner, snapshot: receipt.model };
    onDirtyStateChange?.(operation.owner, false);
    activeOwner.current = nextSource.owner;
    setSnapshot(receipt.model);
    setDraft(createModelDraft(receipt.model));
    setSource(nextSource);
    updateInitialConditionsOperation(null);
    setSaveErrors([]);
    setSaveErrorDetails('');
    setSaveMessage(text(language, '初始条件已提交，并已从完整事务回执刷新工程与边界条件快照。', 'Initial conditions were committed, and the project and boundary snapshot were refreshed from the complete transaction receipt.'));
    return true;
  };

  const runInitialConditionsAttach = async (operation: BoundaryInitialConditionsOperation): Promise<boolean> => {
    if (activeSave.current || activeOwner.current !== operation.owner || source.owner !== operation.owner) return false;
    if (!backend.supports('boundary.initialConditions.attach') || !onContextCommit) {
      setSaveMessage(text(language, '当前服务或工程会话未提供可确认的初始条件提交接纳能力。', 'The service or project session cannot confirm initial-condition commit acceptance.'));
      return false;
    }
    const requestState = { owner: operation.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState && activeOwner.current === operation.owner
      && source.owner === operation.owner && generation.current === requestGeneration
      && !requestState.controller.signal.aborted
      && initialConditionsOperationRef.current?.request.operationID === operation.request.operationID;
    updateInitialConditionsOperation({
      ...operation,
      state: 'attaching',
      message: text(language, '正在按已检查的结果修订和工程修订执行同一事务。', 'Submitting the exact inspected result and project revisions as one transaction.'),
    });
    setBusy(true);
    setSaveMessage('');
    setSaveErrorDetails('');
    setSaveErrors([]);
    try {
      const receipt = await backend.request<BoundaryInitialConditionsReceipt>('boundary.initialConditions.attach', operation.request, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) {
        updateInitialConditionsOperation({ ...operation, state: 'uncertain', message: text(language, '提交响应到达时工程会话已变化；请使用同一操作编号查询事务状态。', 'The response arrived after the project session changed. Check status using the same operation ID.') });
        return false;
      }
      return await attachInitialConditionsReceipt(operation, receipt, isCurrentRequest);
    } catch (error) {
      if (requestState.controller.signal.aborted) {
        updateInitialConditionsOperation({ ...operation, state: 'uncertain', message: text(language, '提交期间请求被取消或工程会话切换；结果未确认，请按同一操作编号查询状态。', 'The request was cancelled or the project session changed during commit. Its result is unconfirmed; check status with the same operation ID.') });
        return false;
      }
      if (!isCurrentRequest()) return false;
      try {
        const status = await backend.request<BoundaryInitialConditionsStatus>('boundary.initialConditions.status', operation.request, { signal: requestState.controller.signal });
        if (!isCurrentRequest()) return false;
        return await applyInitialConditionsStatus(operation, status, isCurrentRequest, message(error));
      } catch (statusError) {
        if (isCurrentRequest()) updateInitialConditionsOperation({
          ...operation,
          state: 'uncertain',
          message: text(language, '提交响应丢失，且暂时无法查询事务状态；请稍后查询或按完全相同的请求重试。', 'The commit response was lost and transaction status is currently unavailable. Check later or retry the exact same request.') + ` ${message(statusError)}`,
        });
        return false;
      }
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const applyInitialConditionsStatus = async (
    operation: BoundaryInitialConditionsOperation,
    status: BoundaryInitialConditionsStatus,
    isCurrentRequest: () => boolean,
    priorError?: string,
  ): Promise<boolean> => {
    if (!status || typeof status.state !== 'string') throw new Error('事务状态响应不完整。');
    if (status.state === 'committed') {
      if (!status.receipt) throw new Error('已提交状态缺少完整事务回执。');
      return attachInitialConditionsReceipt(operation, status.receipt, isCurrentRequest);
    }
    if (!isCurrentRequest()) return false;
    const states: Record<string, BoundaryInitialConditionsOperationState> = {
      notFound: 'notFound', prepared: 'prepared', rolledBack: 'rolledBack', conflict: 'conflict',
    };
    const state = states[status.state] ?? 'uncertain';
    updateInitialConditionsOperation({
      ...operation,
      state,
      message: status.errorMessage ?? (priorError
        ? `${text(language, '提交失败，已查询事务状态。', 'The commit failed; transaction status was checked.')} ${priorError}`
        : text(language, `事务状态：${status.state}。`, `Transaction state: ${status.state}.`)),
    });
    if (state === 'conflict') setSaveMessage(text(language, '事务与当前工程或边界条件修订冲突，不能自动重试。', 'The transaction conflicts with the current project or boundary revision and cannot be retried automatically.'));
    return false;
  };

  const runInitialConditionsStatus = async (operation: BoundaryInitialConditionsOperation): Promise<boolean> => {
    if (operation.owner !== source.owner || activeOwner.current !== operation.owner) return false;
    if (operation.receipt) {
      const requestGeneration = generation.current;
      const isCurrentRequest = () => activeOwner.current === operation.owner && source.owner === operation.owner
        && generation.current === requestGeneration
        && initialConditionsOperationRef.current?.request.operationID === operation.request.operationID;
      return attachInitialConditionsReceipt(operation, operation.receipt, isCurrentRequest);
    }
    if (!backend.supports('boundary.initialConditions.status') || activeSave.current) return false;
    const requestState = { owner: operation.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState && activeOwner.current === operation.owner
      && source.owner === operation.owner && generation.current === requestGeneration
      && !requestState.controller.signal.aborted
      && initialConditionsOperationRef.current?.request.operationID === operation.request.operationID;
    setBusy(true);
    try {
      const status = await backend.request<BoundaryInitialConditionsStatus>('boundary.initialConditions.status', operation.request, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      return await applyInitialConditionsStatus(operation, status, isCurrentRequest);
    } catch (error) {
      if (isCurrentRequest()) updateInitialConditionsOperation({
        ...operation,
        state: 'uncertain',
        message: text(language, '无法读取事务状态；请稍后使用相同操作编号重试。', 'The transaction status could not be read. Retry later with the same operation ID.') + ` ${message(error)}`,
      });
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onInitialConditionsAttach: NonNullable<PageProps['boundaryInitialConditions']>['attach'] = async (inspection) => {
    const currentSnapshot = snapshotRef.current;
    const currentAsset = source.context.selectedAsset;
    if (!currentSnapshot || !currentAsset || !source.context.projectPath || !source.context.projectRevision
      || !source.context.projectXml || !currentSnapshot.canEdit || dirty || validations.length > 0 || activeSave.current || busy
      || initialConditionsOperationRef.current || !backend.supports('boundary.initialConditions.attach') || !onContextCommit) return false;
    if (!inspection.canAttach || !isSha256(inspection.resultRevision) || !isSha256(inspection.assetRevision)
      || inspection.assetRevision.toUpperCase() !== currentSnapshot.revision.toUpperCase()
      || inspection.projectRevision.toUpperCase() !== source.context.projectRevision.toUpperCase()
      || !sameServerPath(inspection.assetPath, currentAsset.path) || !sameServerPath(inspection.projectPath, source.context.projectPath)
      || initialConditionsInspectionRef.current?.owner !== source.owner
      || JSON.stringify(initialConditionsInspectionRef.current.inspection) !== JSON.stringify(inspection)
      || initialConditionsCatalogRef.current?.owner !== source.owner
      || !initialConditionsCatalogRef.current.items.some((result) => sameServerPath(result.path, inspection.resultPath))
      || !inspection.numberCulture) {
      setSaveMessage(text(language, '检查回执已过期或来源与当前工程不匹配；请重新检查结果。', 'The inspection is stale or belongs to another source. Inspect the result again.'));
      return false;
    }
    const projectXmlFingerprint = inspection.projectXmlFingerprint;
    if (!isSha256(projectXmlFingerprint)) return false;
    const operation: BoundaryInitialConditionsOperation = {
      owner: source.owner,
      ownerKey: boundaryInitialConditionsOwnerKey(source.context, projectXmlFingerprint),
      generation: generation.current,
      baselineLineIdentity: (currentSnapshot.boundary?.lines ?? []).map((line) => ({ id: line.id, scope: line.scope })),
      inspection,
      request: {
        model: initialConditionsModelRequest(),
        projectRevision: source.context.projectRevision,
        resultPath: inspection.resultPath,
        resultRevision: inspection.resultRevision,
        operationID: requestIdentity(),
        timestamp: inspection.timestamp,
        numberCulture: inspection.numberCulture,
        projectXmlFingerprint,
      },
      state: 'attaching',
      message: text(language, '准备提交初始条件事务。', 'Preparing the initial-condition transaction.'),
    };
    updateInitialConditionsOperation(operation);
    return runInitialConditionsAttach(operation);
  };

  const onInitialConditionsRecover = async (): Promise<boolean> => {
    const operation = initialConditionsOperationRef.current;
    if (!operation || operation.owner !== source.owner || operation.state === 'conflict') return false;
    return runInitialConditionsStatus(operation);
  };
  const onInitialConditionsRetry = async (): Promise<boolean> => {
    const operation = initialConditionsOperationRef.current;
    if (!operation || operation.owner !== source.owner || !['notFound', 'prepared', 'rolledBack'].includes(operation.state)) return false;
    return runInitialConditionsAttach(operation);
  };

  const onSpatialTransform: PageProps['onSpatialTransform'] = async (layerID, operation, itemID) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const boundary = draft?.boundaryView ?? snapshot?.boundary;
    const selectedLayer = boundary?.spatial?.[layerID];
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !selectedLayer) return false;
    if (!backend.supports('model.spatialTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供空间测站和输入调整能力。', 'The C# server has not advertised spatial station and input transforms.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能调整空间输入。', 'Correct invalid draft values before changing spatial inputs.'));
      return false;
    }
    const beforeStations = selectedLayer.stations.map((station) => station.id);
    const beforeInputs = selectedLayer.inputs.map((input) => input.id);
    const beforeSpatial = boundary?.spatial ?? {};
    const beforeStation = itemID ? selectedLayer.stations.find((station) => station.id === itemID) : undefined;
    const beforeInput = itemID ? selectedLayer.inputs.find((input) => input.id === itemID) : undefined;
    if ((operation === 'addStation' && itemID !== null)
      || (operation === 'addGauge' && (!itemID || !selectedLayer.stations.some((station) => station.id === itemID)))
      || (operation === 'removeStation' && !beforeStation)
      || (operation === 'removeGauge' && !beforeInput)) return false;
    if (operation === 'removeStation') {
      const referenced = Object.values(boundary?.spatial ?? {}).some((layer) => layer.inputs.some((input) => {
        const key = `${input.scope}/Station`;
        return (draft.fields[key] ?? snapshot.fields[key] ?? input.stationId ?? '') === itemID;
      }));
      if (referenced) {
        setSaveMessage(text(language, '此测站仍被空间输入引用；先移除或改派输入。', 'This station is still referenced by a spatial input. Remove or reassign the input first.'));
        return false;
      }
    }
    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const readOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? [])]);
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => value !== snapshot.fields[id] && !readOnlyFields.has(id)));
    const previousSourceState = draft.sourceState !== (snapshot.sourceState ?? null)
      ? draft.sourceState
      : snapshot.sourceState ?? sourceStateFor(asset) ?? null;
    const request = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: {},
      sourceState: previousSourceState,
      rows: draft.rows,
      samples: samplesForBoundaryDraft(snapshot, draft),
      changedCells: changedModelCells(draft, snapshotRows(snapshot)),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.spatialTransform', {
        model: request, layerId: layerID, operation, itemId: itemID,
      }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision || !result.boundary || !result.sourceState || result.sourceState === previousSourceState)
        throw new Error('The spatial transform did not return a changed, revision-bound draft projection.');
      const transformedLayer = result.boundary.spatial?.[layerID];
      if (!transformedLayer) throw new Error('The spatial transform returned another layer selection.');
      const expectStableOrder = (current: string[], next: string[], expectedCount: number, removedID?: string) => {
        if (next.length !== expectedCount) return false;
        const retained = removedID ? current.filter((id) => id !== removedID) : current;
        return retained.every((id, index) => next[index] === id);
      };
      if (operation === 'addStation' || operation === 'removeStation') {
        const nextIDs = transformedLayer.stations.map((station) => station.id);
        const expected = beforeStations.length + (operation === 'addStation' ? 1 : -1);
        if (!expectStableOrder(beforeStations, nextIDs, expected, operation === 'removeStation' ? itemID ?? undefined : undefined))
          throw new Error('The spatial transform did not preserve station identities and ordering.');
        if (operation === 'addStation') {
          const created = transformedLayer.stations.at(-1);
          if (!created || beforeStations.includes(created.id) || !/^[0-9a-f]{32}$/i.test(created.id) || created.scope !== `Spatial/Station/${created.id}`)
            throw new Error('The backend did not return a stable station identity and source scope.');
        } else if (transformedLayer.stations.some((station) => station.id === itemID)) {
          throw new Error('The removed station is still present in the backend projection.');
        }
        if (operation === 'removeStation' && Object.values(result.boundary.spatial ?? {}).some((layer) => layer.inputs.some((input) => input.stationId === itemID)))
          throw new Error('The backend left an input referencing the removed station.');
      } else {
        const nextIDs = transformedLayer.inputs.map((input) => input.id);
        const expected = beforeInputs.length + (operation === 'addGauge' ? 1 : -1);
        if (!expectStableOrder(beforeInputs, nextIDs, expected, operation === 'removeGauge' ? itemID ?? undefined : undefined))
          throw new Error('The spatial transform did not preserve input identities and ordering.');
        if (operation === 'addGauge') {
          const created = transformedLayer.inputs.at(-1);
          if (!created || beforeInputs.includes(created.id) || !/^[0-9a-f]{32}$/i.test(created.id)
            || created.scope !== `Spatial/${layerID}/Gauge/${created.id}` || created.stationId !== itemID)
            throw new Error('The backend did not return the new input with its stable identity, scope, and selected station.');
        } else if (transformedLayer.inputs.some((input) => input.id === itemID)) {
          throw new Error('The removed spatial input is still present in the backend projection.');
        }
      }
      for (const [otherLayerID, oldLayer] of Object.entries(beforeSpatial)) {
        const nextLayer = result.boundary.spatial?.[otherLayerID];
        if (!nextLayer) throw new Error('The spatial transform omitted a previously loaded layer.');
        const stationIDs = operation === 'addStation' || operation === 'removeStation'
          ? transformedLayer.stations.map((station) => station.id)
          : oldLayer.stations.map((station) => station.id);
        if (nextLayer.stations.length !== stationIDs.length || nextLayer.stations.some((station, index) =>
          station.id !== stationIDs[index] || station.scope !== `Spatial/Station/${station.id}`))
          throw new Error('The spatial transform changed station identities or source scopes outside the requested operation.');
        const inputIDs = otherLayerID === layerID && (operation === 'addGauge' || operation === 'removeGauge')
          ? transformedLayer.inputs.map((input) => input.id)
          : oldLayer.inputs.map((input) => input.id);
        if (nextLayer.inputs.length !== inputIDs.length || nextLayer.inputs.some((input, index) =>
          input.id !== inputIDs[index] || input.scope !== `Spatial/${otherLayerID}/Gauge/${input.id}`))
          throw new Error('The spatial transform changed input identities or source scopes outside the requested operation.');
      }
      const transformedDraft = createModelDraft(result);
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: transformedDraft.fields,
          rows: transformedDraft.rows,
          samples: transformedDraft.samples,
          sampleText: transformedDraft.sampleText,
          boundaryView: result.boundary,
          boundaryFieldGroups: result.fieldGroups,
          boundaryReadOnlyFields: result.readOnlyFields ?? [],
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      const messageText = operation === 'addStation' ? '测站已加入草稿；点击“保存”后才会写入模型文件。'
        : operation === 'removeStation' ? '测站已从草稿移除；点击“保存”后才会写入模型文件。'
          : operation === 'addGauge' ? '空间输入已加入草稿；点击“保存”后才会写入模型文件。'
            : '空间输入已从草稿移除；点击“保存”后才会写入模型文件。';
      setSaveMessage(text(language, messageText, 'The spatial change is in the draft. Save to write it to the model file.'));
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '空间边界调整失败。', 'The spatial boundary could not be changed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const readGridModelRequest = () => {
    if (!snapshot || !asset) throw new Error('No loaded Boundary Conditions model is active.');
    const clean = createModelDraft(snapshot);
    return {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields: {},
      images: {},
      sourceState: snapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows: clean.rows,
      samples: samplesForBoundaryDraft(snapshot, clean),
      changedCells: {},
    };
  };
  const isCurrentGridRead = (owner: string, revision: string, requestGeneration: number, signal: AbortSignal) =>
    activeOwner.current === owner && generation.current === requestGeneration
      && snapshotRef.current?.revision === revision && !signal.aborted;

  const onGridInspect: PageProps['onGridInspect'] = async (layerID, files, subdataset, bands, previewID, signal) => {
    const supportedLayers = ['precipitation', 'wind', 'airPressure', 'airDensity'];
    if (!snapshot || !asset || asset.kind !== 'Boundary Conditions' || !source.context.projectPath || !snapshot.canEdit
      || !supportedLayers.includes(layerID) || !backend.supports('model.gridInspect')) throw new Error('Grid inspection is unavailable for the active source.');
    const controller = signal ? null : new AbortController();
    const activeSignal = signal ?? controller!.signal;
    const owner = source.owner;
    const revision = snapshot.revision;
    const requestGeneration = generation.current;
    const result = await backend.request<SpatialGridInspection>('model.gridInspect', {
      model: readGridModelRequest(), layerId: layerID, files: [...files], subdataset: subdataset ?? null,
      bands: bands ?? null, previewId: previewID ?? null,
    }, { signal: activeSignal });
    if (!isCurrentGridRead(owner, revision, requestGeneration, activeSignal)) throw new DOMException('The grid inspection belongs to a stale model selection.', 'AbortError');
    if (!result || !Array.isArray(result.bands) || !Array.isArray(result.subdatasets) || !Array.isArray(result.unitOptions))
      throw new Error('The C# service returned an incomplete grid inspection.');
    return result;
  };

  const onGridFrame: PageProps['onGridFrame'] = async (layerID, timeID, component = 'Value', signal) => {
    const supportedLayers = ['precipitation', 'wind', 'airPressure', 'airDensity'];
    if (!snapshot || !asset || asset.kind !== 'Boundary Conditions' || !source.context.projectPath
      || !supportedLayers.includes(layerID) || !backend.supports('model.gridFrame')) throw new Error('Grid-frame reading is unavailable for the active source.');
    const controller = signal ? null : new AbortController();
    const activeSignal = signal ?? controller!.signal;
    const owner = source.owner;
    const revision = snapshot.revision;
    const requestGeneration = generation.current;
    const result = await backend.request<SpatialGridData>('model.gridFrame', {
      model: readGridModelRequest(), layerId: layerID, timeId: timeID ?? null, component,
    }, { signal: activeSignal });
    if (!isCurrentGridRead(owner, revision, requestGeneration, activeSignal)) throw new DOMException('The grid frame belongs to a stale model selection.', 'AbortError');
    if (!result || result.layerId !== layerID || !Array.isArray(result.times) || !Array.isArray(result.components))
      throw new Error('The C# service returned an incomplete grid frame.');
    return result;
  };

  const onGridImport: PageProps['onGridImport'] = async (selection: SpatialGridSelection) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const supportedLayers = ['precipitation', 'wind', 'airPressure', 'airDensity'];
    if (!snapshot || !draft || !asset || asset.kind !== 'Boundary Conditions' || !source.context.projectPath || !snapshot.canEdit
      || !supportedLayers.includes(selection.layerId)) return false;
    if (dirty) {
      setSaveMessage(text(language, '栅格导入会立即提交模型。请先保存或丢弃当前草稿。', 'Grid import commits the model immediately. Save or discard the current draft first.'));
      return false;
    }
    if (!backend.supports('model.gridImport') || !backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供栅格导入和模型保存能力。', 'The C# server has not advertised grid import and model save.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正或丢弃后再导入栅格。', 'Correct or discard invalid draft values before importing a grid.'));
      return false;
    }
    if (!selection.bands.length || selection.bands.some((band) => !band.included || !band.id || !band.revision)
      || new Set(selection.bands.map((band) => band.id)).size !== selection.bands.length) return false;

    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState && activeOwner.current === requestState.owner
      && generation.current === requestGeneration && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const requestModel = readGridModelRequest();
    try {
      const result = await backend.request<ModelSnapshot>('model.gridImport', { model: requestModel, selection }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision === snapshot.revision) throw new Error('Grid import returned no new committed model revision.');
      const imported = result.grids?.[selection.layerId];
      if (!imported || imported.layerId !== selection.layerId || !Array.isArray(imported.times))
        throw new Error('The save receipt did not include the imported grid layer.');
      const nextContext = updatedContext(source.context, asset, result);
      const nextSource = sourceFor(nextContext);
      acceptedReceipt.current = { owner: nextSource.owner, snapshot: result };
      if (onContextCommit && !onContextCommit(source.context, nextContext)) {
        acceptedReceipt.current = null;
        return false;
      }
      onDirtyStateChange?.(source.owner, false);
      activeOwner.current = nextSource.owner;
      setSnapshot(result);
      setDraft(createModelDraft(result));
      setSource(nextSource);
      setSaveMessage(text(language, '栅格已导入；工作台已按 C# 保存回执刷新基线。', 'The grid was imported. The workbench baseline was refreshed from the C# save receipt.'));
      if (!onContextCommit) onContextChange(nextContext);
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '栅格导入失败。', 'Grid import failed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onTerrainTransform: PageProps['onTerrainTransform'] = async (command: TerrainTransformCommand) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const terrain = draft?.terrainView ?? snapshot?.terrain;
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !terrain) return false;
    if (!backend.supports('model.terrainTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供地形图层调整和保存能力。', 'The C# server has not advertised terrain layer editing and save capabilities.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能调整地形图层。', 'Correct invalid draft values before changing terrain layers.'));
      return false;
    }

    const beforeInputIDs = terrain.inputs.map((input) => input.id);
    const beforeRows = (tableID: string) => draft.rows[tableID] ?? terrain.rows?.[tableID] ?? [];
    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const readOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? [])]);
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => {
      const navigationValue = draft.terrainSelectionBaselineFields?.[id];
      if (Object.prototype.hasOwnProperty.call(draft.terrainSelectionBaselineFields ?? {}, id) && value === navigationValue) return false;
      return value !== snapshot.fields[id] && !readOnlyFields.has(id);
    }));
    // TerrainEditor captures the selected input before applying table selections;
    // unlike a persistent input field, this navigation field must be sent even
    // when it still equals the saved snapshot value.
    fields['TerrainLayersEditor/SelectedInputWithMap'] = terrain.selectedId ?? '';
    const tableSelections = Object.fromEntries(Object.entries(terrain.tables ?? {}).flatMap(([id, table]) => {
      const selection = table.selectedRowIDs ?? table.requestedSelectionRowIDs;
      return selection ? [[id, selection]] : [];
    }));
    const model = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: {},
      sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
        ? draft.sourceState
        : snapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows: draft.rows,
      samples: samplesForBoundaryDraft(snapshot, draft),
      tableSelections,
      changedCells: changedModelCells(draft, snapshotRows(snapshot)),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.terrainTransform', { model, ...command }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision) throw new Error('The terrain transform returned a different source revision.');
      if (!result.terrain || typeof result.sourceState !== 'string' || result.sourceState.length === 0) {
        throw new Error('The backend did not return a revision-bound terrain draft projection.');
      }

      const afterInputIDs = result.terrain.inputs.map((input) => input.id);
      if (new Set(afterInputIDs).size !== afterInputIDs.length || afterInputIDs.some((id) => !id))
        throw new Error('The backend returned duplicate terrain input identities.');
      for (const [tableID, rows] of Object.entries(result.terrain.rows ?? {})) {
        const rowIDs = rows.map((row) => row.id);
        if (rowIDs.some((id) => !id) || new Set(rowIDs).size !== rowIDs.length)
          throw new Error(`The backend returned duplicate or empty terrain row identities for ${tableID}.`);
      }
      if (command.operation === 'add') {
        if (afterInputIDs.length !== beforeInputIDs.length + 1
          || beforeInputIDs.some((id, index) => afterInputIDs[index] !== id)
          || afterInputIDs.slice(0, -1).includes(afterInputIDs[afterInputIDs.length - 1])
          || result.terrain.selectedId !== afterInputIDs[afterInputIDs.length - 1])
          throw new Error('The backend did not preserve existing terrain input identities while adding a layer.');
      } else if (command.operation === 'remove') {
        if (!command.itemId || afterInputIDs.length !== beforeInputIDs.length - 1
          || beforeInputIDs.filter((id) => id !== command.itemId).some((id, index) => afterInputIDs[index] !== id))
          throw new Error('The backend did not preserve remaining terrain input identities while removing a layer.');
      } else if (command.operation === 'reorder') {
        if (command.from === undefined || command.to === undefined || command.from < 0 || command.to < 0
          || command.from >= beforeInputIDs.length || command.to >= beforeInputIDs.length)
          throw new Error('The terrain input order request was invalid.');
        const expected = beforeInputIDs.slice();
        const [moved] = expected.splice(command.from, 1);
        expected.splice(command.to, 0, moved);
        if (afterInputIDs.some((id, index) => id !== expected[index]))
          throw new Error('The backend did not return the requested terrain input order.');
      } else if (command.operation === 'selectInput') {
        if ((result.terrain.selectedId ?? null) !== (command.itemId ?? null))
          throw new Error('The backend did not return the requested terrain input selection.');
      }

      if (command.operation === 'selectRows') {
        if (!command.tableId || !result.terrain.tables?.[command.tableId] || !result.terrain.rows?.[command.tableId])
          throw new Error('The backend did not return the selected terrain table.');
        const selectedIDs = result.terrain.tables[command.tableId].selectedRowIDs ?? [];
        const availableIDs = new Set(result.terrain.rows[command.tableId].map((row) => row.id));
        if (selectedIDs.length !== (command.rowIDs ?? []).length || selectedIDs.some((id) => !availableIDs.has(id)))
          throw new Error('The backend returned an incomplete or stale terrain row selection.');
        if (command.tableId !== 'LeveeModPropertiesEditor/PropertySets'
          && (selectedIDs.length !== (command.rowIDs ?? []).length
            || selectedIDs.some((id) => !(command.rowIDs ?? []).includes(id))))
          throw new Error('The backend changed a selected terrain feature identity.');
      } else if (command.operation === 'addRow') {
        if (!command.tableId || (result.terrain.rows?.[command.tableId]?.length ?? 0) !== beforeRows(command.tableId).length + 1)
          throw new Error('The backend did not add exactly one terrain table row.');
        if (command.tableId === 'ChannelModEditor/Profile' || command.tableId === 'LeveeModDetailEditor/Profile') {
          const before = beforeRows(command.tableId);
          const after = result.terrain.rows?.[command.tableId] ?? [];
          const columns = terrain.tables?.[command.tableId]?.columns ?? [];
          if (before.some((row, index) => columns.some((column) => !equivalentCellValue(
            row.values[column.id] ?? '', after[index]?.values[column.id] ?? '', column.sourceType))))
            throw new Error('The backend did not preserve existing terrain profile rows when adding a row.');
        }
      } else if (command.operation === 'removeRows') {
        if (!command.tableId || (result.terrain.rows?.[command.tableId]?.length ?? 0) !== beforeRows(command.tableId).length - (command.rowIDs?.length ?? 0))
          throw new Error('The backend did not remove the requested terrain table rows.');
      } else if (command.operation === 'copyRows') {
        const destinationRows = result.terrain.rows?.['ChannelModEditor/Profile'] ?? [];
        if (!command.rowIDs || destinationRows.length !== command.rowIDs.length)
          throw new Error('The backend did not copy the sampled terrain profile rows.');
        const oldFeatureIDs = beforeRows('ChannelModEditor/Features').map((row) => row.id);
        const newFeatureIDs = result.terrain.rows?.['ChannelModEditor/Features']?.map((row) => row.id) ?? [];
        if (oldFeatureIDs.length !== newFeatureIDs.length || oldFeatureIDs.some((id, index) => id !== newFeatureIDs[index]))
          throw new Error('The backend changed channel feature identities while copying the sampled profile.');
      }

      const transformedDraft = createModelDraft(result);
      const previousSelectionBaseline = draft.terrainSelectionBaselineFields ?? {};
      const selectionFieldIDs = new Set([
        ...Object.keys(previousSelectionBaseline),
        ...Object.keys(result.fields).filter((fieldID) => fieldID.startsWith('TerrainSelection/')),
        'TerrainLayersEditor/SelectedInputWithMap',
      ]);
      const terrainSelectionBaselineFields: Record<string, string | null> = {};
      for (const fieldID of selectionFieldIDs) {
        const hasPreviousBaseline = Object.prototype.hasOwnProperty.call(previousSelectionBaseline, fieldID);
        const currentValue = Object.prototype.hasOwnProperty.call(draft.fields, fieldID) ? draft.fields[fieldID] : null;
        const expectedValue = hasPreviousBaseline
          ? previousSelectionBaseline[fieldID]
          : Object.prototype.hasOwnProperty.call(snapshot.fields, fieldID) ? snapshot.fields[fieldID] : null;
        // A value changed by the user before this navigation command remains a
        // real edit; only untouched selection-scoped values are normalized out.
        if (currentValue !== expectedValue) continue;
        terrainSelectionBaselineFields[fieldID] = Object.prototype.hasOwnProperty.call(result.fields, fieldID)
          ? result.fields[fieldID]
          : null;
      }
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: transformedDraft.fields,
          rows: transformedDraft.rows,
          samples: transformedDraft.samples,
          sampleText: transformedDraft.sampleText,
          terrainView: result.terrain,
          terrainReadOnlyFields: result.readOnlyFields ?? [],
          terrainSelectionBaselineFields,
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      const draftOnly = command.operation !== 'selectRows' && command.operation !== 'selectInput';
      setSaveMessage(draftOnly
        ? text(language, '地形更改已进入草稿；点击“保存”后才会写入模型文件。', 'The terrain change is in the draft. Save to write it to the model file.')
        : text(language, '地形选择已更新。', 'The terrain selection was updated.'));
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '地形图层操作失败。', 'The terrain layer operation failed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onTerrainTopology: NonNullable<PageProps['onTerrainTopology']> = async (command) => {
    const ownerAtStart = source.owner;
    const generationAtStart = generation.current;
    const baseline = snapshotRef.current;
    const currentDraft = draftRef.current;
    const terrain = currentDraft?.terrainView ?? baseline?.terrain;
    if (activeSave.current || busy || !baseline || !currentDraft || !asset || !terrain
      || !baseline.canEdit || !source.context.projectPath
      || !backend.supports('model.terrainTopology') || !backend.supports('model.save')) return null;
    const selectedInput = terrain.inputs.find((input) => input.id === command.inputId);
    const isPolygonInput = isPolygonRasterModInput(selectedInput?.type);
    if (validations.length || command.inputId !== terrain.selectedId || !selectedInput) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '请先修正草稿中的无效值，并重新选择当前地形输入。', 'Correct invalid draft values and reselect the current terrain input.'));
      return null;
    }
    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    activeSave.current = requestState;
    setBusy(true);
    setSaveMessage('');
    setSaveErrorDetails('');
    setSaveErrors([]);
    try {
      const readOnly = new Set([...(baseline.readOnlyFields ?? []), ...(currentDraft.terrainReadOnlyFields ?? [])]);
      const fields = Object.fromEntries(Object.entries(currentDraft.fields).filter(([id, value]) => {
        if (Object.prototype.hasOwnProperty.call(currentDraft.terrainSelectionBaselineFields ?? {}, id)
          && value === currentDraft.terrainSelectionBaselineFields?.[id]) return false;
        return value !== baseline.fields[id] && !readOnly.has(id);
      }));
      fields['TerrainLayersEditor/SelectedInputWithMap'] = terrain.selectedId ?? '';
      if (isPolygonInput) {
        const mapContext = command.mapContext;
        const inputIndex = selectedInput.sourceVectorIndex;
        const map = mapContext?.map;
        const expectedCoreContext = coreLayerContext(source.context, asset);
        const capturedCoreIdentity = coreContextIdentity(map?.coreLayerContext);
        const expectedCoreIdentity = coreContextIdentity(expectedCoreContext);
        const coreContextMatches = expectedCoreContext
          ? capturedCoreIdentity !== null && capturedCoreIdentity === expectedCoreIdentity
          : !map?.coreLayerContext;
        let currentXmlSHA: string | null;
        try {
          currentXmlSHA = await projectXmlSha(source.context.projectXml);
        } catch (error) {
          throw new Error(`无法核对当前工程快照：${message(error)}`);
        }
        if (!isCurrentRequest() || activeOwner.current !== ownerAtStart || generation.current !== generationAtStart
          || snapshotRef.current?.revision !== baseline.revision || draftRef.current !== currentDraft || source.owner !== ownerAtStart) return null;
        if (!mapContext || !map || !Number.isInteger(inputIndex) || inputIndex! < 0
          || mapContext.expectedInputIndex !== inputIndex
          || mapContext.expectedSourceClass !== 'PolygonRasterModTerrainInputVisualContext'
          || !sameTerrainSourcePath(mapContext.expectedTerrainPath, asset.path)
          || mapContext.expectedTerrainRevision?.toUpperCase() !== baseline.revision.toUpperCase()
          || map.projectPath !== source.context.projectPath
          || map.projectXml !== source.context.projectXml
          || map.projectXmlSHA?.toUpperCase() !== currentXmlSHA?.toUpperCase()
          || map.projectSessionID !== source.context.projectSessionID
          || !coreContextMatches
          || !map.mapID || !map.mapName || !map.mapType || !Number.isInteger(map.mapIndex) || map.mapIndex < 0
          || !map.expectedVisualizationRevision
          || (mapContext.renderOptions && !validTerrainViewport(mapContext.renderOptions.viewport))) {
          throw new Error('地图来源与当前工程或地形修订不一致，请重新读取地图图层。');
        }
      } else if (command.mapContext || command.polygonSettings) {
        throw new Error('当前输入不支持此地形修改操作。');
      }
      const model = {
        ...modelRequest(source.context, asset), revision: baseline.revision, fields, images: {},
        sourceState: currentDraft.sourceState ?? baseline.sourceState ?? sourceStateFor(asset) ?? null,
        rows: currentDraft.rows, samples: samplesForBoundaryDraft(baseline, currentDraft),
        tableSelections: Object.fromEntries(Object.entries(terrain.tables ?? {}).flatMap(([id, table]) => {
          const selection = table.selectedRowIDs ?? table.requestedSelectionRowIDs;
          return selection ? [[id, selection]] : [];
        })),
        changedCells: changedModelCells(currentDraft, snapshotRows(baseline)),
      };
      const result = await backend.request<TerrainTopologyResponse>('model.terrainTopology',
        { ...command, model }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return null;
      assertModelSnapshot(result.snapshot);
      if (result.snapshot.revision !== baseline.revision || !result.snapshot.terrain
        || typeof result.snapshot.sourceState !== 'string' || !result.snapshot.sourceState
        || result.topology?.inputId !== command.inputId || result.topology.sourceRevision !== baseline.revision
        || !isSha256(result.topology.draftIdentity) || !Array.isArray(result.topology.features)
        || !Array.isArray(result.topology.selectedFeatureIDs)
        || result.snapshot.terrain.selectedId !== command.inputId)
        throw new Error('The terrain topology response does not match the captured terrain draft.');
      const beforeIDs = terrain.inputs.map((input) => input.id);
      const afterIDs = result.snapshot.terrain.inputs.map((input) => input.id);
      const featureIDs = result.topology.features.map((feature) => feature.id);
      if (JSON.stringify(beforeIDs) !== JSON.stringify(afterIDs) || featureIDs.some((id) => !id)
        || new Set(featureIDs).size !== featureIDs.length
        || result.topology.selectedFeatureIDs.some((id) => !featureIDs.includes(id)))
        throw new Error('The terrain topology response changed the captured input or feature identities.');
      if (isPolygonInput && (result.topology.inputIndex !== selectedInput.sourceVectorIndex
        || result.topology.features.some((feature) => !feature.id.startsWith(`${command.mapContext!.inputNodeID}:feature:`))))
        throw new Error('The polygon response does not match the selected saved terrain-input node.');
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        return projectTerrainSnapshot(baseline, current, result.snapshot);
      });
      setSaveMessage(command.operation === 'load' || command.operation === 'select'
        ? text(language, '地形修改选择已更新。', 'Terrain modification selection updated.')
        : text(language, '地形拓扑更改已进入父地形草稿；保存后才写入文件。', 'Terrain topology changes are in the parent terrain draft. Save to write the file.'));
      return result;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, isPolygonInput ? '地形多边形操作失败。' : '渠道拓扑操作失败。', isPolygonInput ? 'Terrain polygon editing failed.' : 'Channel topology editing failed.'));
        setSaveErrorDetails(message(error));
      }
      return null;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onClassificationTransform: PageProps['onClassificationTransform'] = async (command: ClassificationTransformCommand) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const classification = draft?.classificationView ?? snapshot?.classification;
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !classification) return false;
    if (!backend.supports('model.classificationTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, '当前工作环境暂不支持分类输入调整和保存。', 'The active model service does not support classification input editing and save.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能调整分类输入。', 'Correct invalid draft values before changing classification inputs.'));
      return false;
    }

    const beforeInputIDs = classification.inputs.map((input) => input.id);
    if (new Set(beforeInputIDs).size !== beforeInputIDs.length || beforeInputIDs.some((id) => !id)) {
      setSaveMessage(text(language, '分类输入身份不唯一，无法执行结构调整。', 'Classification input identities are not unique, so the structure cannot be changed.'));
      return false;
    }
    const classificationTables = new Map(classification.inputs.flatMap((input) => input.tableId ? [[input.tableId, input] as const] : []));
    const beforeRows = new Map<string, ModelDraft['rows'][string]>();
    for (const [tableID, input] of classificationTables) beforeRows.set(tableID, draft.rows[tableID] ?? input.rows);
    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const readOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? [])]);
    const selectionBaselines = draft.classificationSelectionBaselineFields ?? {};
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => {
      if (Object.prototype.hasOwnProperty.call(selectionBaselines, id) && value === selectionBaselines[id]) return false;
      return value !== snapshot.fields[id] && !readOnlyFields.has(id);
    }));
    // The selected input is navigation state, but transforms still need it to
    // keep the same input active in the revision-bound draft.
    const selectionField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';
    fields[selectionField] = draft.fields[selectionField] ?? classification.selectedId ?? '';
    const rows = Object.fromEntries(Object.entries(draft.rows).filter(([tableID]) => classificationTables.has(tableID)));
    const changedCells = changedModelCells(draft, snapshotRows(snapshot));
    const model = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: draft.images,
      sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
        ? draft.sourceState
        : snapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows,
      samples: draft.samples,
      changedCells: Object.fromEntries(Object.entries(changedCells).filter(([tableID]) => classificationTables.has(tableID))),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.classificationTransform', { model, ...command }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision) throw new Error('The classification transform returned a different source revision.');
      if (!result.classification || typeof result.sourceState !== 'string' || result.sourceState.length === 0) {
        throw new Error('The backend did not return a revision-bound classification draft projection.');
      }
      const afterInputIDs = result.classification.inputs.map((input) => input.id);
      if (new Set(afterInputIDs).size !== afterInputIDs.length || afterInputIDs.some((id) => !id))
        throw new Error('The backend returned duplicate classification input identities.');
      if (command.operation === 'remove') {
        if (!command.itemId || afterInputIDs.length !== beforeInputIDs.length - 1
          || beforeInputIDs.filter((id) => id !== command.itemId).some((id, index) => afterInputIDs[index] !== id))
          throw new Error('The backend did not preserve remaining classification input identities while removing an input.');
      } else if (command.operation === 'reorder') {
        if (command.from === undefined || command.to === undefined || command.from < 0 || command.to < 0
          || command.from >= beforeInputIDs.length || command.to >= beforeInputIDs.length)
          throw new Error('The classification input order request was invalid.');
        const expected = beforeInputIDs.slice();
        const [moved] = expected.splice(command.from, 1);
        expected.splice(command.to, 0, moved);
        if (afterInputIDs.some((id, index) => id !== expected[index]))
          throw new Error('The backend did not return the requested classification input order.');
      } else if (command.operation === 'geometry') {
        const retained = afterInputIDs.filter((id) => beforeInputIDs.includes(id));
        if (retained.length !== beforeInputIDs.length || retained.some((id, index) => id !== beforeInputIDs[index]))
          throw new Error('The backend changed existing classification input identities during polygon editing.');
        if (command.geometry?.operation === 'create' && afterInputIDs.length > beforeInputIDs.length + 1)
          throw new Error('The backend created more than one polygon input for a geometry edit.');
      }
      if (result.classification.selectedId && !afterInputIDs.includes(result.classification.selectedId))
        throw new Error('The backend returned a stale classification input selection.');
      for (const input of result.classification.inputs) {
        const rowsForInput = input.tableId ? (result.classification.inputs.find((item) => item.id === input.id)?.rows ?? []) : [];
        if (input.tableId && (rowsForInput.some((row) => !row.id) || new Set(rowsForInput.map((row) => row.id)).size !== rowsForInput.length))
          throw new Error(`The backend returned duplicate or empty classification mapping row identities for ${input.tableId}.`);
        const previous = beforeRows.get(input.tableId ?? '');
        if (previous && command.operation !== 'remove' && previous.length !== rowsForInput.length)
          throw new Error('The classification transform changed raster mapping row counts outside the requested operation.');
      }
      for (const input of result.classification.inputs) {
        const polygonIDs = input.polygons.map((polygon) => polygon.id);
        if (polygonIDs.some((id) => !id) || new Set(polygonIDs).size !== polygonIDs.length)
          throw new Error(`The backend returned duplicate or empty polygon identities for ${input.id}.`);
      }

      const transformedDraft = createModelDraft(result);
      const mergedFields = { ...draft.fields, ...transformedDraft.fields };
      const liveClassificationScopes = new Set(result.classification.inputs.map((input) => `${input.scope}/`));
      for (const fieldID of Object.keys(mergedFields)) {
        if (fieldID.startsWith('ClassificationInputs/') && ![...liveClassificationScopes].some((scope) => fieldID.startsWith(scope))) delete mergedFields[fieldID];
      }
      const liveClassificationTables = new Set(result.classification.inputs.flatMap((input) => input.tableId ? [input.tableId] : []));
      const mergedRows = { ...draft.rows };
      for (const tableID of classificationTables.keys()) if (!liveClassificationTables.has(tableID)) delete mergedRows[tableID];
      for (const tableID of liveClassificationTables) {
        if (Object.prototype.hasOwnProperty.call(transformedDraft.rows, tableID)) mergedRows[tableID] = transformedDraft.rows[tableID];
      }
      const previousBaseline = draft.classificationSelectionBaselineFields ?? {};
      const currentSelection = Object.prototype.hasOwnProperty.call(draft.fields, selectionField) ? draft.fields[selectionField] : null;
      const expectedSelection = Object.prototype.hasOwnProperty.call(previousBaseline, selectionField)
        ? previousBaseline[selectionField]
        : Object.prototype.hasOwnProperty.call(snapshot.fields, selectionField) ? snapshot.fields[selectionField] : null;
      const classificationSelectionBaselineFields = { ...previousBaseline };
      if (currentSelection === expectedSelection) {
        classificationSelectionBaselineFields[selectionField] = Object.prototype.hasOwnProperty.call(result.fields, selectionField)
          ? result.fields[selectionField] : null;
      }
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: mergedFields,
          images: current.images,
          rows: mergedRows,
          samples: current.samples,
          sampleText: current.sampleText,
          classificationView: result.classification,
          classificationSelectionBaselineFields,
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      setSaveMessage(text(language, '分类更改已进入草稿；点击“保存”后才会写入模型文件。', 'Classification changes are in the draft. Save to write them to the model file.'));
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '分类输入操作失败。', 'The classification input operation failed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onClassificationMap: PageProps['onClassificationMap'] = async (signal) => {
    const currentSnapshot = snapshotRef.current;
    const currentDraft = draftRef.current;
    const currentClassification = currentDraft?.classificationView ?? currentSnapshot?.classification;
    if (!currentSnapshot || !currentDraft || !asset || !source.context.projectPath || !currentClassification
      || !backend.supports('model.classificationMap')) throw new Error('分类地图预览当前不可用。');
    const owner = source.owner;
    const revision = currentSnapshot.revision;
    const requestGeneration = generation.current;
    const selectedField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';
    const selectedInputID = currentDraft.fields[selectedField] ?? currentClassification.selectedId ?? currentClassification.inputs[0]?.id ?? '';
    const fields = Object.fromEntries(Object.entries(currentDraft.fields).filter(([id]) =>
      id === selectedField || id.startsWith('ClassificationInputs/')));
    fields[selectedField] = selectedInputID;
    const tableIDs = new Set(currentClassification.inputs.flatMap((input) => input.tableId ? [input.tableId] : []));
    const model = {
      ...modelRequest(source.context, asset),
      revision,
      fields,
      images: {},
      sourceState: currentDraft.sourceState !== (currentSnapshot.sourceState ?? null)
        ? currentDraft.sourceState
        : currentSnapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows: Object.fromEntries(Object.entries(currentDraft.rows).filter(([tableID]) => tableIDs.has(tableID))),
      samples: {},
      changedCells: Object.fromEntries(Object.entries(changedModelCells(currentDraft, snapshotRows(currentSnapshot))).filter(([tableID]) => tableIDs.has(tableID))),
    };
    const signalOrAborted = signal ?? new AbortController().signal;
    const draftStamp = JSON.stringify({ sourceState: currentDraft.sourceState, fields: model.fields, rows: model.rows });
    const result = await backend.request<ClassificationMapData>('model.classificationMap', { model }, { signal: signalOrAborted });
    if (signalOrAborted.aborted || activeOwner.current !== owner || generation.current !== requestGeneration
      || snapshotRef.current?.revision !== revision) throw new DOMException('分类地图预览属于已切换的模型源。', 'AbortError');
    const latestDraft = draftRef.current;
    const latestClassification = latestDraft?.classificationView ?? snapshotRef.current?.classification;
    const latestTableIDs = new Set(latestClassification?.inputs.flatMap((input) => input.tableId ? [input.tableId] : []) ?? []);
    const latestFields = Object.fromEntries(Object.entries(latestDraft?.fields ?? {}).filter(([id]) => id === selectedField || id.startsWith('ClassificationInputs/')));
    const latestModelStamp = JSON.stringify({
      sourceState: latestDraft?.sourceState,
      fields: { ...latestFields, [selectedField]: latestDraft?.fields[selectedField] ?? latestClassification?.selectedId ?? latestClassification?.inputs[0]?.id ?? '' },
      rows: Object.fromEntries(Object.entries(latestDraft?.rows ?? {}).filter(([tableID]) => latestTableIDs.has(tableID))),
    });
    if (latestModelStamp !== draftStamp) throw new DOMException('分类输入在地图预览期间发生了变化。', 'AbortError');
    if (!result || !Array.isArray(result.legend) || (result.raster && (!Number.isInteger(result.raster.width) || !Number.isInteger(result.raster.height) || typeof result.raster.rgba !== 'string')))
      throw new Error('分类地图预览数据不完整。');
    return result;
  };

  const onManningTransform: PageProps['onManningTransform'] = async (operation: ManningTransformOperation) => {
    setSaveErrorDetails('');
    if (activeSave.current || busy) return false;
    const manning = draft?.manningView ?? snapshot?.manning;
    if (!snapshot || !draft || !asset || !source.context.projectPath || !snapshot.canEdit || !manning) return false;
    if (!backend.supports('model.manningTransform') || !backend.supports('model.save')) {
      setSaveMessage(text(language, '当前工作环境暂不支持糙率变换和保存。', 'The active model service does not support Manning transforms and save.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '当前草稿有无效值；修正后才能应用糙率操作。', 'Correct invalid draft values before applying Manning operations.'));
      return false;
    }
    const requestState = { owner: source.owner, controller: new AbortController() };
    const requestGeneration = generation.current;
    activeSave.current = requestState;
    const isCurrentRequest = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner
      && generation.current === requestGeneration
      && !requestState.controller.signal.aborted;
    setBusy(true);
    setSaveMessage('');
    setSaveErrors([]);
    const readOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? [])]);
    const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) => value !== snapshot.fields[id] && !readOnlyFields.has(id)));
    const tableID = 'ClassificationLayerManningNEditor/InterpretationTable';
    const rows = Object.prototype.hasOwnProperty.call(draft.rows, tableID) ? { [tableID]: draft.rows[tableID] } : {};
    const model = {
      ...modelRequest(source.context, asset),
      revision: snapshot.revision,
      fields,
      images: draft.images,
      sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
        ? draft.sourceState
        : snapshot.sourceState ?? sourceStateFor(asset) ?? null,
      rows,
      samples: draft.samples,
      changedCells: Object.fromEntries(Object.entries(changedModelCells(draft, snapshotRows(snapshot))).filter(([id]) => id === tableID)),
    };
    try {
      const result = await backend.request<ModelSnapshot>('model.manningTransform', { model, operation }, { signal: requestState.controller.signal });
      if (!isCurrentRequest()) return false;
      assertModelSnapshot(result);
      if (result.revision !== snapshot.revision) throw new Error('The Manning transform returned a different source revision.');
      if (!result.manning || typeof result.sourceState !== 'string' || result.sourceState.length === 0)
        throw new Error('The backend did not return a revision-bound Manning draft projection.');
      const manning = result.manning;
      const rowIDs = manning.rows.map((row) => row.id);
      if (rowIDs.some((id) => !id) || new Set(rowIDs).size !== rowIDs.length)
        throw new Error('The backend returned duplicate or empty Manning row identities.');
      const transformedDraft = createModelDraft(result);
      setDraft((current) => {
        if (!current || activeOwner.current !== requestState.owner || generation.current !== requestGeneration) return current;
        const next = {
          ...current,
          sourceState: transformedDraft.sourceState,
          fields: { ...current.fields, ...transformedDraft.fields },
          images: current.images,
          rows: { ...current.rows, [tableID]: transformedDraft.rows[tableID] ?? current.rows[tableID] ?? manning.rows },
          samples: current.samples,
          sampleText: current.sampleText,
          manningView: manning,
        };
        return { ...next, changedCells: changedModelCells(next, snapshotRows(snapshot)) };
      });
      setSaveMessage(text(language, '糙率更改已进入草稿；点击“保存”后才会写入模型文件。', 'Manning changes are in the draft. Save to write them to the model file.'));
      return true;
    } catch (error) {
      if (isCurrentRequest()) {
        setSaveMessage(text(language, '糙率操作失败。', 'The Manning operation failed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const onNValuePreview: PageProps['onNValuePreview'] = async (signal) => {
    const currentSnapshot = snapshotRef.current;
    const currentDraft = draftRef.current;
    if (!currentSnapshot || !currentDraft || !asset || !source.context.projectPath || !source.context.projectRevision
      || !currentSnapshot.manning || !backend.supports('nvalue.preview')) throw new Error('糙率预览当前不可用。');
    if (isModelDraftDirty(currentSnapshot, currentDraft)) throw new Error('请先保存或丢弃糙率草稿，再生成对应源修订的预览。');
    const owner = source.owner;
    const revision = currentSnapshot.revision;
    const projectRevision = source.context.projectRevision;
    const requestGeneration = generation.current;
    const result = await backend.request<NValuePreviewData>('nvalue.preview', {
      projectPath: source.context.projectPath,
      assetPath: asset.path,
      projectRevision,
      revision,
    }, { signal });
    if (signal?.aborted || activeOwner.current !== owner || generation.current !== requestGeneration
      || snapshotRef.current?.revision !== revision || source.context.projectRevision !== projectRevision
      || isModelDraftDirty(snapshotRef.current, draftRef.current ?? emptyDraft()))
      throw new DOMException('糙率预览属于已切换或已修改的模型修订。', 'AbortError');
    if (!result || result.revision !== revision || result.projectRevision !== projectRevision
      || !Array.isArray(result.legendStops) || (result.raster && typeof result.raster.rgba !== 'string'))
      throw new Error('糙率预览与当前模型修订不匹配。');
    return result;
  };

  const requestTransition = (next: Transition): void => {
    if (pending || busy) return;
    if (next.kind === 'page' && next.pageID === activePageID || next.kind === 'view' && next.view === view) return;
    if (dirty) setPending(next);
    else applyTransition(next);
  };
  const applyTransition = (next: Transition): void => {
    setPending(null);
    if (next.kind === 'page') setPageID(next.pageID);
    else if (next.kind === 'view') setView(next.view);
    else acceptSource(next.source);
  };

  const save = async (): Promise<boolean> => {
    setSaveErrorDetails('');
    if (activeSave.current) return false;
    const initialOperation = initialConditionsOperationRef.current;
    if (initialOperation?.owner === source.owner && initialOperation.state !== 'conflict') {
      setSaveMessage(text(language, '初始条件事务尚未完成回执接纳；请先恢复同一操作，再编辑或保存。', 'The initial-condition transaction is waiting for receipt acceptance. Recover the same operation before editing or saving.'));
      return false;
    }
    if (!snapshot || !draft || !asset || !source.context.projectPath) {
      setSaveMessage(text(language, '需要已载入的模型和所属项目才能保存。', 'A loaded model and its owning project are required to save.'));
      return false;
    }
    if (!snapshot.canEdit) {
      setSaveMessage(text(language, '当前模型快照为只读。', 'The current model snapshot is read-only.'));
      return false;
    }
    if (!backend.supports('model.save')) {
      setSaveMessage(text(language, 'C# 服务端尚未提供 model.save；草稿仍保留在当前页面。', 'The C# server does not provide model.save yet; the draft remains on this page.'));
      return false;
    }
    if (!dirty) {
      setSaveMessage(text(language, '没有待保存的更改。', 'There are no changes to save.'));
      return false;
    }
    if (validations.length) {
      setSaveErrors(validations);
      setSaveMessage(text(language, '存在无效字段；修正后才能保存。', 'Some values are invalid. Correct them before saving.'));
      return false;
    }
    setBusy(true);
    const requestState = { owner: source.owner, controller: new AbortController() };
    activeSave.current = requestState;
    const isCurrentSave = () => activeSave.current === requestState
      && activeOwner.current === requestState.owner && !requestState.controller.signal.aborted;
    setSaveMessage('');
    setSaveErrors([]);
    setSaveErrorDetails('');
    try {
      const planFields = snapshot.planTimeWindowFieldIDs;
      const planIDs = planFields ? [planFields.mode, planFields.start, planFields.end] : [];
      // The source adapter accepts edits, not a replay of every loaded field.
      // Snapshots also contain read-only catalog fields and derived values.
      const boundaryReadOnlyFields = new Set([...(snapshot.readOnlyFields ?? []), ...(draft.boundaryReadOnlyFields ?? []), ...(draft.terrainReadOnlyFields ?? [])]);
      const fields = Object.fromEntries(Object.entries(draft.fields).filter(([id, value]) =>
        !planIDs.includes(id) && value !== snapshot.fields[id] && !boundaryReadOnlyFields.has(id)));
      const terrainProjection = draft.terrainView ?? snapshot.terrain;
      if (terrainProjection) fields['TerrainLayersEditor/SelectedInputWithMap'] = terrainProjection.selectedId ?? '';
      const planTimeWindowEdits = planFields ? planIDs.flatMap((fieldID) => {
        const value = draft.fields[fieldID];
        return value === undefined || value === snapshot.fields[fieldID] ? [] : [{ id: requestIdentity(), fieldID, value }];
      }) : undefined;
      const tableSelections = Object.fromEntries(Object.entries(draft.terrainView?.tables ?? snapshot.terrain?.tables ?? {}).flatMap(([id, table]) => {
        const selections = table.requestedSelectionRowIDs ?? table.selectedRowIDs;
        return selections ? [[id, selections]] : [];
      }));
      const request = {
        ...modelRequest(source.context, asset),
        revision: snapshot.revision,
        fields,
        images: Object.fromEntries(Object.entries(draft.images).filter(([id, images]) =>
          JSON.stringify(images) !== JSON.stringify(snapshot.images[id] ?? []))),
        sourceState: draft.sourceState !== (snapshot.sourceState ?? null)
          ? draft.sourceState
          : snapshot.sourceState ?? sourceStateFor(asset) ?? null,
        rows: draft.rows,
        samples: samplesForBoundaryDraft(snapshot, draft),
        planTimeWindowEdits: planTimeWindowEdits ?? null,
        tableSelections,
        changedCells: changedModelCells(draft, snapshotRows(snapshot)),
      };
      const result = await backend.request<ModelSnapshot>('model.save', request, { signal: requestState.controller.signal });
      if (!isCurrentSave()) return false;
      assertModelSnapshot(result);
      const nextContext = updatedContext(source.context, asset, result);
      const nextSource = sourceFor(nextContext);
      acceptedReceipt.current = { owner: nextSource.owner, snapshot: result };
      if (onContextCommit && !onContextCommit(source.context, nextContext)) {
        acceptedReceipt.current = null;
        return false;
      }
      onDirtyStateChange?.(source.owner, false);
      activeOwner.current = nextSource.owner;
      setSnapshot(result);
      setDraft(createModelDraft(result));
      setSource(nextSource);
      setSaveMessage(text(language, '已从后端保存回执刷新模型。', 'The model was refreshed from the backend save receipt.'));
      if (!onContextCommit) onContextChange(nextContext);
      return true;
    } catch (error) {
      if (isCurrentSave()) {
        setSaveMessage(text(language, '保存失败。', 'Save failed.'));
        setSaveErrorDetails(message(error));
      }
      return false;
    } finally {
      if (activeSave.current === requestState) {
        activeSave.current = null;
        setBusy(false);
      }
    }
  };

  const discard = (): void => {
    if (pending?.kind === 'source') {
      acceptSource(pending.source);
      return;
    }
    if (pending) {
      if (snapshot) setDraft(createModelDraft(snapshot));
      applyTransition(pending);
      return;
    }
    if (snapshot) setDraft(createModelDraft(snapshot));
    setSaveErrors([]);
    setSaveMessage(text(language, '已丢弃本页未保存更改。', 'Unsaved changes on this page were discarded.'));
    setSaveErrorDetails('');
  };

  const saveAndContinue = async (): Promise<void> => {
    if (!pending) return;
    pendingSaveContinuation.current = pending;
    const succeeded = await save();
    const continuation = pendingSaveContinuation.current;
    pendingSaveContinuation.current = null;
    if (succeeded && continuation) applyTransition(continuation);
  };

  const activeInitialConditionsOperation = initialConditionsOperation?.owner === source.owner ? initialConditionsOperation : null;
  const initialConditionsBlocksDraft = !!activeInitialConditionsOperation && activeInitialConditionsOperation.state !== 'conflict';
  const initialConditionsOperationBlocksOtherOwner = !!initialConditionsOperation
    && initialConditionsOperation.owner !== source.owner && initialConditionsOperation.state !== 'conflict';
  const boundaryInitialConditionsWorkflow: BoundaryInitialConditionsWorkflow | undefined = asset
    && asset.kind === 'Boundary Conditions'
    && backend.supports('project.tree.load')
    && backend.supports('boundary.initialConditions.inspect')
    && backend.supports('boundary.initialConditions.attach')
    && backend.supports('boundary.initialConditions.status')
    && onContextCommit
      ? {
        canStart: !!snapshot?.canEdit && !!source.context.projectPath && !!source.context.projectRevision && !!source.context.projectXml
          && !dirty && validations.length === 0 && !busy && !activeSave.current
          && !initialConditionsOperation,
        busy,
        blockedByOtherOwner: initialConditionsOperationBlocksOtherOwner,
        ...(initialConditionsOperationBlocksOtherOwner ? { blockedMessage: text(language,
          '另一工程会话仍有未确认的初始条件事务；请先返回该会话查询或恢复同一操作编号。',
          'Another project session has an unconfirmed initial-condition transaction. Return to that session to check or recover the same operation ID.') } : {}),
        operation: activeInitialConditionsOperation ? {
          operationID: activeInitialConditionsOperation.request.operationID,
          resultPath: activeInitialConditionsOperation.request.resultPath,
          resultRevision: activeInitialConditionsOperation.request.resultRevision,
          state: activeInitialConditionsOperation.state,
          message: activeInitialConditionsOperation.message,
          canRetry: ['notFound', 'prepared', 'rolledBack'].includes(activeInitialConditionsOperation.state),
        } : null,
        loadResults: onInitialConditionsLoadResults,
        inspect: onInitialConditionsInspect,
        attach: onInitialConditionsAttach,
        recover: onInitialConditionsRecover,
        retry: onInitialConditionsRetry,
      }
      : undefined;

  const pageProps: EditorPageProps | null = snapshot && draft && asset ? {
    backend, language, asset, snapshot, draft, schemas: catalog.schemas, tableSchemas: catalog.tables,
    pageId: activePageID, onPageChange: (next) => requestTransition({ kind: 'page', pageID: next }),
    onFieldChange, onImagesChange, onRowCellChange, onSampleChange,
    ...(boundaryInitialConditionsWorkflow ? { boundaryInitialConditions: boundaryInitialConditionsWorkflow } : {}),
    canTransformSeries: snapshot.canEdit && !!source.context.projectPath && validations.length === 0,
    ...(backend.supports('model.seriesTransform') && backend.supports('model.save') ? { onSeriesTransform } : {}),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.boundaryTransform') && backend.supports('model.save') ? { onBoundaryTransform } : {}),
    canTransformSpatial: snapshot.canEdit && !!source.context.projectPath && validations.length === 0 && !busy
      && backend.supports('model.spatialTransform') && backend.supports('model.save'),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.spatialTransform') && backend.supports('model.save') ? { onSpatialTransform } : {}),
    canImportGrid: snapshot.canEdit && !!source.context.projectPath && !dirty && validations.length === 0 && !busy
      && backend.supports('model.gridImport') && backend.supports('model.save'),
    ...(source.context.projectPath && backend.supports('model.gridInspect') ? { onGridInspect } : {}),
    ...(source.context.projectPath && backend.supports('model.gridFrame') ? { onGridFrame } : {}),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.gridImport') && backend.supports('model.save') ? { onGridImport } : {}),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.terrainTransform') && backend.supports('model.save') ? { onTerrainTransform } : {}),
    ...(snapshot.canEdit && source.context.projectPath && source.context.projectXml && source.context.projectRevision
      ? { terrainMapOwner: terrainMapProjectOwner(source.context, asset) } : {}),
    canEditTerrainTopology: snapshot.canEdit && !!source.context.projectPath && !busy && validations.length === 0
      && backend.supports('model.terrainTopology') && backend.supports('model.save'),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.terrainTopology')
      && backend.supports('model.save') ? { onTerrainTopology } : {}),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.classificationTransform') && backend.supports('model.save') ? { onClassificationTransform } : {}),
    ...(!!source.context.projectPath && backend.supports('model.classificationMap') ? { onClassificationMap } : {}),
    ...(snapshot.canEdit && !!source.context.projectPath && backend.supports('model.manningTransform') && backend.supports('model.save') ? { onManningTransform } : {}),
    canPreviewNValue: !!source.context.projectPath && !!source.context.projectRevision && !!snapshot.manning
      && !dirty && !busy && backend.supports('nvalue.preview'),
    ...(!!source.context.projectPath && !!source.context.projectRevision && backend.supports('nvalue.preview') ? { onNValuePreview } : {}),
  } : null;
  const canSave = !!snapshot?.canEdit && !!source.context.projectPath && backend.supports('model.save') && dirty && !busy && !initialConditionsBlocksDraft;

  return (
    <main className="editor-workbench">
      <header className="editor-workbench-header">
        <div className="editor-workbench-title">
          <p className="editor-eyebrow">{text(language, 'HEC-RAS 2025 · 模型编辑器', 'HEC-RAS 2025 · Model editors')}</p>
          <h1>{asset?.name ?? text(language, '模型编辑器', 'Model editors')}</h1>
          {asset && <p className="editor-workbench-subtitle">{editorAssetKind(language, asset.kind)} · {asset.path}</p>}
        </div>
        <div className="editor-workbench-actions">
          <button type="button" className={view === 'model' ? 'editor-button editor-button-selected' : 'editor-button'} disabled={!asset} onClick={() => requestTransition({ kind: 'view', view: 'model' })}>{text(language, '模型页面', 'Model')}</button>
          <button type="button" className={view === 'catalog' ? 'editor-button editor-button-selected' : 'editor-button'} onClick={() => requestTransition({ kind: 'view', view: 'catalog' })}>{text(language, '控件目录 · 开发预览', 'Control catalog · preview')}</button>
          {view === 'model' && <button type="button" className="editor-button" disabled={!dirty || busy || !snapshot} onClick={discard}>{text(language, '丢弃草稿', 'Discard draft')}</button>}
          {view === 'model' && <button type="submit" form="source-model-editor-form" className="editor-button editor-button-primary" disabled={!canSave}>{busy ? text(language, '保存中…', 'Saving…') : text(language, '保存', 'Save')}</button>}
        </div>
      </header>

      {asset && <div className="editor-status-strip" role="status">
        <span className={loadState.status === 'ready' ? 'editor-status-dot editor-status-ready' : 'editor-status-dot'} />
        {statusText(loadState, language)}
        {snapshot && <span>{text(language, '修订', 'Revision')}: <code>{snapshot.revision}</code></span>}
        {snapshot && <span>{snapshot.canEdit ? text(language, '可编辑', 'Editable') : text(language, '只读', 'Read only')}</span>}
        {dirty && <strong>{text(language, '有未保存更改', 'Unsaved changes')}</strong>}
      </div>}

      {catalogError && <div className="editor-inline-error" role="alert">{text(language, '无法载入 C# 控件目录。', 'Could not load the C# control catalog.')}<details><summary>{text(language, '错误详情', 'Error details')}</summary><pre>{catalogError}</pre></details></div>}

      {view === 'catalog' ? <EditorCatalogBrowser schemas={catalog.schemas} selectedSchemaID={selectedSchemaID} search={catalogSearch} language={language} onSelect={setSelectedSchemaID} onSearch={setCatalogSearch} /> : (
        <>
          {asset && !['Plan', 'Boundary Conditions', 'Terrain'].includes(asset.kind) && (asset.kind !== 'Surface Layers' || !!snapshot?.manning) && pageIDs.length > 0 && <nav className="editor-page-tabs editor-workbench-tabs" aria-label={text(language, '模型编辑器页面', 'Model editor pages')}>
            {pageIDs.map((id) => <button type="button" key={id} className={id === activePageID ? 'selected' : ''} aria-current={id === activePageID ? 'page' : undefined} onClick={() => requestTransition({ kind: 'page', pageID: id })}>{pageTitle(id, language)}</button>)}
          </nav>}
          {asset && snapshot && loadState.status === 'ready' && ['Plan', 'Geometry'].includes(asset.kind) &&
            <ModelAssociationsPanel {...props} context={source.context} activeAsset={asset}
              modelRevision={snapshot.revision} modelDirty={dirty || busy || initialConditionsBlocksDraft} />}
          <form id="source-model-editor-form" onSubmit={(event) => { event.preventDefault(); void save(); }}>
            {!asset && <div className="editor-config-notice">{text(language, '当前没有载入模型。请从项目浏览器选择计划、地形或边界条件；此处不会创建虚构模型。控件目录可作为开发预览查看源编辑器字段定义。', 'No model is loaded. Select a plan, terrain, or boundary condition from the project browser; this page will not create a model. The control catalog is available as a development preview of source editor field definitions.')}</div>}
            {asset && loadState.status === 'loading' && <div className="editor-empty-state" aria-live="polite">{text(language, '正在从 C# 服务端载入模型…', 'Loading the model from the C# server…')}</div>}
            {asset && loadState.status === 'unavailable' && <div className="editor-config-notice" role="status">{text(language, 'C# 能力未就绪：model.load 当前不可用。此页面不会伪造模型或保存结果。打开“控件目录”可浏览原始 UI 定义。', 'Backend capability is not ready: model.load is unavailable. No model or save result is fabricated. Open the control catalog to browse source UI definitions.')}</div>}
            {asset && loadState.status === 'error' && <div className="editor-inline-error" role="alert">{text(language, '模型载入失败。', 'Model load failed.')}<details><summary>{text(language, '错误详情', 'Error details')}</summary><pre>{loadState.error}</pre></details></div>}
            {asset && !source.context.projectPath && snapshot && <div className="editor-config-notice">{text(language, '已载入模型快照，但当前会话没有所属项目路径；此快照不可保存。', 'The model snapshot is loaded, but this session has no owning project path; it cannot be saved.')}</div>}
            {pageProps && <div key={source.owner} className="editor-model-page">
              <fieldset disabled={busy || initialConditionsBlocksDraft} style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}>{renderEditor(pageProps)}</fieldset>
              {validations.length > 0 && dirty && <section className="editor-validation-summary" aria-live="polite"><h2>{text(language, '需要修正', 'Needs attention')}</h2><ul>{validations.slice(0, 20).map((error) => <li key={error.fieldID}><strong>{error.title}</strong> · {error.message} <code>{error.fieldID}</code></li>)}</ul>{validations.length > 20 && <small>{text(language, `另有 ${validations.length - 20} 项`, `${validations.length - 20} more issues`)}</small>}</section>}
            </div>}
            {asset?.kind === 'Boundary Conditions' && activePageID === 'BoundaryConditionsSummary' &&
              <AttachInitialConditionsDialog language={language} boundaryInitialConditions={boundaryInitialConditionsWorkflow} />}
            {asset && !snapshot && loadState.status !== 'loading' && <div className="editor-no-snapshot"><div className="editor-empty-state">{text(language, '尚无可编辑的模型快照。', 'There is no editable model snapshot.')}</div><button type="button" className="editor-button" onClick={() => setView('catalog')}>{text(language, '浏览原始控件配置', 'Browse source control metadata')}</button></div>}
            {saveMessage && <div className={saveErrorDetails ? 'editor-inline-error' : 'editor-save-notice'} role="status">{saveMessage}{saveErrorDetails && <details><summary>{text(language, '错误详情', 'Error details')}</summary><pre>{saveErrorDetails}</pre></details>}</div>}
            {saveErrors.length > 0 && <ul className="editor-validation-summary">{saveErrors.slice(0, 12).map((error) => <li key={error.fieldID}>{error.title}: {error.message} <code>{error.fieldID}</code></li>)}</ul>}
          </form>
        </>
      )}

      {pending && <div className="editor-confirm-backdrop" role="presentation">
        <section className="editor-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="editor-pending-title">
          <p className="editor-eyebrow">{text(language, '草稿保护', 'Draft protection')}</p>
          <h2 id="editor-pending-title">{pending.kind === 'source' ? text(language, '要切换模型吗？', 'Switch models?') : text(language, '要离开当前编辑页吗？', 'Leave this editor page?')}</h2>
          <p>{text(language, '当前模型有未保存更改。先保存、丢弃，或留在此页。', 'The current model has unsaved changes. Save them, discard them, or stay on this page.')}</p>
          <div className="editor-confirm-actions">
            <button type="button" className="editor-button" onClick={() => { if (pending.kind === 'source') onContextChange(source.context); setPending(null); }}>{text(language, '留在此页', 'Stay')}</button>
            <button type="button" className="editor-button" onClick={discard}>{text(language, '丢弃并继续', 'Discard and continue')}</button>
            <button type="button" className="editor-button editor-button-primary" disabled={busy || !backend.supports('model.save') || !snapshot?.canEdit || !source.context.projectPath} onClick={() => void saveAndContinue()}>{busy ? text(language, '保存中…', 'Saving…') : text(language, '保存并继续', 'Save and continue')}</button>
          </div>
        </section>
      </div>}
    </main>
  );
}

function renderEditor(props: EditorPageProps) {
  switch (props.asset.kind) {
    case 'Plan': return <PlanEditorControl {...props} />;
    case 'Geometry': return <SchemaPageBridge {...props} />;
    case 'Terrain': return <TerrainEditor {...props} />;
    case 'Boundary Conditions': return <BoundaryConditionEditor {...props} />;
    case 'Surface Layers': return props.snapshot.manning ? <NValueEditor {...props} /> : <ClassificationEditor {...props} />;
    default: return <div className="editor-empty-state">{text(props.language, `${editorValueLabel(props.language, props.asset.kind)}尚未接入 C# 模型编辑器。`, `No C# model editor is connected for ${props.asset.kind}.`)}</div>;
  }
}

function SchemaPageBridge(props: EditorPageProps) {
  const schema = props.schemas.find((item) => item.id === props.pageId);
  if (!schema) return <div className="editor-empty-state">{text(props.language, '几何摘要字段不在当前目录中。', 'The geometry summary schema is not in the loaded catalog.')}</div>;
  return <SchemaPageBridgeCompat {...props} />;
}

function pagesFor(kind: string, snapshot?: ModelSnapshot): string[] {
  if (kind === 'Plan') return ['PlanSettingsPage', 'PlanComputeSettingsPage'];
  if (kind === 'Geometry') return ['GeometrySummaryPage'];
  if (kind === 'Terrain') return ['TerrainProperties', 'TerrainLayersEditor'];
  if (kind === 'Boundary Conditions') return [...BOUNDARY_EDITOR_PAGE_IDS];
  if (kind === 'Surface Layers') {
    if (snapshot?.manning) return ['NValueCatalogSummary', 'ManningsNEditor'];
    if (snapshot?.classification) return ['ClassificationCatalogSummary', 'ClassificationLayerSummary', 'ClassificationInputs'];
    return ['ClassificationCatalogSummary', 'ClassificationLayerSummary', 'ClassificationInputs', 'NValueCatalogSummary', 'ManningsNEditor'];
  }
  return [];
}

function defaultPage(kind: string, snapshot?: ModelSnapshot): string {
  return pagesFor(kind, snapshot)[0] ?? '';
}

function statusText(load: LoadState, language: 'zh' | 'en'): string {
  if (load.status === 'loading') return text(language, '正在载入模型', 'Loading model');
  if (load.status === 'ready') return text(language, '已载入真实后端模型快照', 'Loaded a real backend model snapshot');
  if (load.status === 'unavailable') return text(language, 'C# model.load 未就绪', 'C# model.load is unavailable');
  if (load.status === 'error') return text(language, '模型未载入', 'Model not loaded');
  return text(language, '未载入项目模型', 'No project model loaded');
}

function sourceFor(context: WorkbenchContext): SourceContext {
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectXml, context.projectRevision, context.selectedAsset?.path ?? null, context.selectedAsset?.kind ?? null, context.selectedAsset?.revision ?? null, sourceStateFor(context.selectedAsset)]);
  return { context, owner };
}

const initialConditionsStorageKey = 'hec-ras.boundary-initial-conditions.pending.v1';

function boundaryInitialConditionsOwnerKey(context: WorkbenchContext, projectXmlFingerprint: string): string {
  const asset = context.selectedAsset;
  return JSON.stringify([
    context.projectSessionID ?? null,
    context.projectPath,
    context.projectRevision,
    asset?.path ?? null,
    asset?.kind ?? null,
    asset?.revision ?? null,
    sourceStateFor(asset) ?? null,
    projectXmlFingerprint.toUpperCase(),
  ]);
}

function persistInitialConditionsOperation(operation: BoundaryInitialConditionsOperation | null): void {
  if (typeof window === 'undefined') return;
  try {
    if (!operation || operation.state === 'conflict') {
      window.sessionStorage.removeItem(initialConditionsStorageKey);
      return;
    }
    window.sessionStorage.setItem(initialConditionsStorageKey, JSON.stringify({
      version: 1,
      ownerKey: operation.ownerKey,
      baselineLineIdentity: operation.baselineLineIdentity,
      request: operation.request,
      inspection: operation.inspection,
      state: operation.state === 'attaching' || operation.state === 'awaitingContext' ? 'uncertain' : operation.state,
      message: operation.state === 'uncertain' ? operation.message : '',
    }));
  } catch {
    // Keep the live operation usable when browser storage is unavailable or full.
  }
}

function restoreInitialConditionsOperation(
  context: WorkbenchContext,
  owner: string,
): BoundaryInitialConditionsOperation | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(initialConditionsStorageKey);
    if (!raw) return null;
    const saved = JSON.parse(raw) as Partial<BoundaryInitialConditionsOperation> & { version?: number; ownerKey?: string };
    const request = saved.request as BoundaryInitialConditionsAttachRequest | undefined;
    const inspection = saved.inspection as BoundaryInitialConditionsInspection | undefined;
    const baselineLineIdentity = saved.baselineLineIdentity;
    const states: BoundaryInitialConditionsOperationState[] = ['uncertain', 'notFound', 'prepared', 'rolledBack', 'conflict'];
    if (saved.version !== 1 || typeof saved.ownerKey !== 'string' || !request || !inspection
      || typeof request.operationID !== 'string' || !request.operationID
      || !isSha256(request.resultRevision) || !isSha256(request.projectRevision)
      || !isSha256(request.projectXmlFingerprint) || !request.model || typeof request.model !== 'object'
      || typeof request.resultPath !== 'string' || !Array.isArray(baselineLineIdentity)
      || !baselineLineIdentity.every((line) => !!line && typeof line.id === 'string' && typeof line.scope === 'string')
      || !states.includes(saved.state as BoundaryInitialConditionsOperationState)) {
      window.sessionStorage.removeItem(initialConditionsStorageKey);
      return null;
    }
    const ownerKey = saved.ownerKey;
    return {
      owner: ownerKey === boundaryInitialConditionsOwnerKey(context, request.projectXmlFingerprint) ? owner : ownerKey,
      ownerKey,
      generation: 0,
      baselineLineIdentity: baselineLineIdentity as Array<{ id: string; scope: string }>,
      request,
      inspection,
      state: saved.state as BoundaryInitialConditionsOperationState,
      message: typeof saved.message === 'string' && saved.message
        ? saved.message
        : '上次会话中的初始条件事务尚未确认，请查询同一操作编号。',
    };
  } catch {
    return null;
  }
}

function sourceStateFor(asset: WorkbenchContext['selectedAsset']): string | undefined {
  const locator = asset?.locator;
  if (!locator) return undefined;
  const value = (locator as Record<string, unknown>).sourceState ?? (locator as Record<string, unknown>).SourceState;
  return typeof value === 'string' ? value : undefined;
}

function coreLayerContext(context: WorkbenchContext, asset: NonNullable<WorkbenchContext['selectedAsset']>): Record<string, unknown> | undefined {
  const raw = asset.locator as Record<string, unknown> | undefined;
  const candidate = raw?.coreLayerContext ?? raw?.CoreLayerContext;
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return undefined;
  const value = candidate as Record<string, unknown>;
  const session = value.projectSessionID ?? value.ProjectSessionID;
  const project = value.projectPath ?? value.ProjectPath;
  const identity = value.identityToken ?? value.IdentityToken;
  return session === context.projectSessionID && project === context.projectPath && typeof identity === 'string' && identity.length > 0 ? value : undefined;
}

function coreContextIdentity(value: Record<string, unknown> | null | undefined): string | null {
  if (!value) return null;
  const session = value.projectSessionID ?? value.ProjectSessionID;
  const project = value.projectPath ?? value.ProjectPath;
  const identity = value.identityToken ?? value.IdentityToken;
  return typeof session === 'string' && session.length > 0
    && typeof project === 'string' && project.length > 0
    && typeof identity === 'string' && identity.length > 0
    ? JSON.stringify([session, project, identity])
    : null;
}

function terrainMapProjectOwner(context: WorkbenchContext, asset: NonNullable<WorkbenchContext['selectedAsset']>): TerrainMapProjectOwner {
  if (!context.projectPath || context.projectXml == null || !context.projectRevision)
    throw new Error('A saved project snapshot is required for terrain polygon map editing.');
  const core = coreLayerContext(context, asset);
  return {
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    projectRevision: context.projectRevision,
    projectSessionID: context.projectSessionID,
    ...(core ? { coreLayerContext: core } : {}),
  };
}

function isPolygonRasterModInput(type?: string): boolean {
  return type === 'PolygonRasterModTerrainInput' || type?.endsWith('.PolygonRasterModTerrainInput') === true;
}

function sameTerrainSourcePath(left: string, right: string): boolean {
  const normalize = (value: string) => value.trim().replace(/\\/g, '/').replace(/\/+$/, '');
  return normalize(left) === normalize(right);
}

function validTerrainViewport(viewport: TerrainMapViewport): boolean {
  return Number.isFinite(viewport.minX) && Number.isFinite(viewport.minY)
    && Number.isFinite(viewport.maxX) && Number.isFinite(viewport.maxY)
    && viewport.maxX > viewport.minX && viewport.maxY > viewport.minY
    && Number.isFinite(viewport.pixelWidth) && viewport.pixelWidth > 0
    && Number.isFinite(viewport.pixelHeight) && viewport.pixelHeight > 0;
}

function modelRequest(context: WorkbenchContext, asset: NonNullable<WorkbenchContext['selectedAsset']>) {
  const core = coreLayerContext(context, asset);
  return {
    assetPath: asset.path,
    kind: asset.kind,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    sourceState: sourceStateFor(asset) ?? null,
    ...(core ? { coreLayerContext: core } : {}),
  };
}

function updatedContext(context: WorkbenchContext, asset: NonNullable<WorkbenchContext['selectedAsset']>, snapshot: ModelSnapshot): WorkbenchContext {
  const locator: Record<string, unknown> = { ...(asset.locator as Record<string, unknown> | undefined) };
  delete locator.SourceState;
  if (snapshot.sourceState == null) delete locator.sourceState;
  else locator.sourceState = snapshot.sourceState;
  return { ...context, selectedAsset: { ...asset, revision: snapshot.revision, locator } };
}

function updatedContextAfterInitialConditions(
  context: WorkbenchContext,
  asset: NonNullable<WorkbenchContext['selectedAsset']>,
  receipt: BoundaryInitialConditionsReceipt,
): WorkbenchContext {
  const locator: Record<string, unknown> = { ...(asset.locator as Record<string, unknown> | undefined) };
  delete locator.SourceState;
  if (receipt.model.sourceState == null) delete locator.sourceState;
  else locator.sourceState = receipt.model.sourceState;
  return {
    ...context,
    projectXml: receipt.projectXml,
    projectRevision: receipt.projectRevision,
    selectedAsset: { ...asset, revision: receipt.model.revision, locator },
  };
}

function sameServerPath(left: string, right: string): boolean {
  const normalize = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '');
  const a = normalize(left);
  const b = normalize(right);
  const windows = (value: string): boolean => /^[A-Za-z]:\//.test(value) || value.startsWith('//');
  return windows(a) && windows(b) ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value);
}

async function sha256Text(value: string): Promise<string> {
  if (!globalThis.crypto?.subtle) throw new Error('此运行环境无法核验工程 XML SHA-256。');
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
}

function getNumberCulture(): string {
  try { return Intl.DateTimeFormat().resolvedOptions().locale || 'en-US'; }
  catch { return 'en-US'; }
}

function assertInitialConditionsInspection(
  value: BoundaryInitialConditionsInspection,
  expected: { assetPath: string; assetRevision: string; projectPath: string; projectRevision: string; projectXmlFingerprint: string; resultPath: string },
): void {
  if (!value || typeof value !== 'object' || typeof value.canAttach !== 'boolean'
    || typeof value.timestamp !== 'string' || typeof value.numberCulture !== 'string'
    || !isSha256(value.assetRevision) || !isSha256(value.projectRevision) || !isSha256(value.projectXmlFingerprint)
    || !sameServerPath(value.assetPath, expected.assetPath) || !sameServerPath(value.projectPath, expected.projectPath)
    || !sameServerPath(value.resultPath, expected.resultPath)
    || value.assetRevision.toUpperCase() !== expected.assetRevision.toUpperCase()
    || value.projectRevision.toUpperCase() !== expected.projectRevision.toUpperCase()
    || value.projectXmlFingerprint.toUpperCase() !== expected.projectXmlFingerprint.toUpperCase()) {
    throw new Error('初始条件检查回执与当前边界条件、工程 XML 或结果文件来源不匹配。');
  }
  if (value.canAttach && !isSha256(value.resultRevision))
    throw new Error('服务端允许附加，但没有返回结果文件 SHA-256。');
}

async function assertInitialConditionsReceipt(
  receipt: BoundaryInitialConditionsReceipt,
  operation: BoundaryInitialConditionsOperation,
): Promise<void> {
  if (!receipt || typeof receipt !== 'object' || receipt.operationID !== operation.request.operationID
    || !sameServerPath(receipt.assetPath, operation.inspection.assetPath)
    || !sameServerPath(receipt.projectPath, operation.inspection.projectPath)
    || !sameServerPath(receipt.resultPath, operation.request.resultPath)
    || !isSha256(receipt.resultRevision) || receipt.resultRevision.toUpperCase() !== operation.request.resultRevision.toUpperCase()
    || !isSha256(receipt.projectRevision) || typeof receipt.projectXml !== 'string'
    || typeof receipt.connectionName !== 'string' || receipt.connectionName.length === 0
    || receipt.timestamp !== operation.inspection.timestamp) {
    throw new Error('初始条件提交回执缺少匹配的操作编号、来源、结果 SHA-256、工程 XML 或连接名称。');
  }
  if (!isSha256(receipt.model.revision) || !receipt.model.boundary
    || !Array.isArray(receipt.model.boundary.lines) || receipt.model.canEdit !== true) {
    throw new Error('事务回执没有返回可编辑的边界条件模型快照。');
  }
  const xmlSHA = await sha256Text(receipt.projectXml);
  if (xmlSHA !== receipt.projectRevision.toUpperCase())
    throw new Error('事务回执中的工程 XML 与工程修订 SHA-256 不一致。');
  const beforeLines = operation.baselineLineIdentity;
  const afterLines = receipt.model.boundary.lines;
  if (beforeLines.length !== afterLines.length || beforeLines.some((line, index) =>
    line.id !== afterLines[index]?.id || line.scope !== afterLines[index]?.scope)) {
    throw new Error('事务回执改变了边界条件线身份或顺序。');
  }
}

function assertModelSnapshot(value: unknown): asserts value is ModelSnapshot {
  if (!value || typeof value !== 'object') throw new Error('The backend did not return a model snapshot.');
  const candidate = value as Partial<ModelSnapshot>;
  if (typeof candidate.revision !== 'string' || !candidate.fields || typeof candidate.fields !== 'object' || !candidate.images || typeof candidate.images !== 'object' || typeof candidate.canEdit !== 'boolean') {
    throw new Error('The backend returned an incomplete model snapshot.');
  }
}

function requestIdentity(): string {
  return globalThis.crypto?.randomUUID?.() ?? 'edit-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
}

function projectTerrainSnapshot(baseline: ModelSnapshot, current: ModelDraft, result: ModelSnapshot): ModelDraft {
  const projected = createModelDraft(result);
  const previous = current.terrainSelectionBaselineFields ?? {};
  const selectionFields = new Set([
    ...Object.keys(previous), ...Object.keys(result.fields).filter((id) => id.startsWith('TerrainSelection/')),
    'TerrainLayersEditor/SelectedInputWithMap',
  ]);
  const selectionBaselines: Record<string, string | null> = {};
  for (const id of selectionFields) {
    const before = Object.prototype.hasOwnProperty.call(current.fields, id) ? current.fields[id] : null;
    const initial = Object.prototype.hasOwnProperty.call(previous, id) ? previous[id]
      : Object.prototype.hasOwnProperty.call(baseline.fields, id) ? baseline.fields[id] : null;
    if (before === initial)
      selectionBaselines[id] = Object.prototype.hasOwnProperty.call(result.fields, id) ? result.fields[id] : null;
  }
  const next: ModelDraft = {
    ...current, sourceState: projected.sourceState, fields: projected.fields, rows: projected.rows,
    samples: projected.samples, sampleText: projected.sampleText,
    terrainView: result.terrain, terrainReadOnlyFields: result.readOnlyFields ?? [],
    terrainSelectionBaselineFields: selectionBaselines,
  };
  return { ...next, changedCells: changedModelCells(next, snapshotRows(baseline)) };
}

function emptyDraft(): ModelDraft {
  return { sourceState: null, fields: {}, images: {}, rows: {}, samples: {}, sampleText: {}, changedCells: {} };
}

/** Rows are authoritative for every table-backed series present in either the baseline or the draft projection. */
function samplesForBoundaryDraft(snapshot: ModelSnapshot, draft: ModelDraft): ModelDraft['samples'] {
  const tablePages = new Set([
    ...Object.values(snapshot.boundary?.series ?? {}),
    ...Object.values(draft.boundaryView?.series ?? {}),
  ].filter((series) => !!series.tableId).map((series) => series.pageId));
  return Object.fromEntries(Object.entries(draft.samples).filter(([pageID]) => !tablePages.has(pageID)));
}

function sampleIdentityKey(sample: { id: string; datasetId?: string | null }): string {
  return `${sample.id}\u0000${sample.datasetId ?? ''}`;
}

function equivalentCellValue(left: string, right: string, sourceType: string): boolean {
  if (left === right) return true;
  if (sourceType === 'DateTime') {
    const leftTime = Date.parse(left);
    const rightTime = Date.parse(right);
    return Number.isFinite(leftTime) && leftTime === rightTime;
  }
  if (sourceType === 'double' || sourceType === 'float' || sourceType === 'int') {
    if (left.trim() === '' || right.trim() === '') return left.trim() === right.trim();
    const leftNumber = Number(left);
    return Number.isFinite(leftNumber) && leftNumber === Number(right);
  }
  return false;
}

import { SchemaPage } from './SchemaFieldRenderer';
function SchemaPageBridgeCompat(props: EditorPageProps) { return <SchemaPage {...props} />; }
