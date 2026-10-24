import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { AssetReference, FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { BackendOperationUnavailableError } from '../../shared/workbench';
import { localRow, updateLocalRows, type GeometryCulvertSnapshot, type GeometryField, type GeometryFeatureKind, type GeometryMeshDegreeScan, type GeometryMeshSnapshot, type GeometryModelRequest, type GeometryParentSnapshot, type GeometryRow, type GeometryTable, type GeometryTopologySnapshot } from './geometryTypes';
import { geometryDiagnosticMessage } from './geometryErrors';

interface OwnerValue<T> { owner: string; value: T }
interface GeometryLocalDraft {
  owner: string;
  rows: Partial<Record<GeometryFeatureKind, GeometryRow[]>>;
  overrides: Record<string, string>;
  deleted: Partial<Record<GeometryFeatureKind, string[]>>;
}
interface LoadingState { parent: boolean; topology: boolean; mesh: boolean; culverts: boolean }
interface ErrorState { parent?: string; topology?: string; mesh?: string; culverts?: string; save?: string }
interface GeometryFeaturePageProps extends FeaturePageProps {}

function exactCoreLayerContext(asset: AssetReference | null, context: WorkbenchContext): Record<string, unknown> | undefined {
  const rawLocator = asset?.locator;
  if (typeof rawLocator !== 'object' || rawLocator === null) return undefined;
  const locator = rawLocator as Record<string, unknown>;
  const raw = locator.coreLayerContext ?? locator.CoreLayerContext;
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;
  const candidate = raw as Record<string, unknown>;
  const session = candidate.projectSessionID ?? candidate.ProjectSessionID;
  const project = candidate.projectPath ?? candidate.ProjectPath;
  const identity = candidate.identityToken ?? candidate.IdentityToken;
  if (session !== context.projectSessionID || project !== context.projectPath || typeof identity !== 'string' || identity.length === 0) return undefined;
  return candidate;
}

function sourceStateFor(asset: AssetReference | null): string | undefined {
  const rawLocator = asset?.locator;
  if (typeof rawLocator !== 'object' || rawLocator === null) return undefined;
  const locator = rawLocator as Record<string, unknown>;
  const state = locator.sourceState ?? locator.SourceState;
  return typeof state === 'string' ? state : undefined;
}

function ownerFor(context: WorkbenchContext, asset: AssetReference | null): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml, asset?.path ?? null, asset?.revision ?? '', 'geometry-source']);
}

function requestID(): string {
  return globalThis.crypto?.randomUUID?.().replaceAll('-', '') ?? `${Date.now().toString(16)}${Math.random().toString(16).slice(2).padEnd(16, '0')}`.slice(0, 32);
}

function mapOwner(context: WorkbenchContext, asset: AssetReference | null, revision?: string): string {
  return JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, asset?.path ?? null, revision ?? asset?.revision ?? '', 'geometry']);
}

function localKey(kind: string, rowID: string, fieldID: string): string {
  return `${kind}\u0000${rowID}\u0000${fieldID}`;
}

function visibleTable(table: GeometryTable | undefined, draft: GeometryLocalDraft | undefined, kind: GeometryFeatureKind): GeometryTable | undefined {
  if (!table || !draft) return table;
  const hidden = new Set(draft.deleted[kind] ?? []);
  const rows = table.rows.filter((row) => !hidden.has(row.id)).map((row) => {
    const values = { ...row.values };
    for (const [key, value] of Object.entries(draft.overrides)) {
      const prefix = `${kind}\u0000${row.id}\u0000`;
      if (key.startsWith(prefix)) values[key.slice(prefix.length)] = value;
    }
    return { ...row, values };
  });
  return { ...table, rows };
}

function parseMutations(encoded: string): unknown[] {
  try {
    const value = JSON.parse(encoded) as { mutations?: unknown[] };
    return Array.isArray(value.mutations) ? value.mutations : [];
  } catch { return []; }
}

export function useGeometryWorkbench({ language, context, backend, backendRevision, onContextChange, onDirtyStateChange }: GeometryFeaturePageProps) {
  const asset = context.selectedAsset?.kind === 'Geometry' ? context.selectedAsset : null;
  const sourceOwner = ownerFor(context, asset);
  const sourceOwnerRef = useRef(sourceOwner);
  sourceOwnerRef.current = sourceOwner;
  const model = useMemo<GeometryModelRequest | null>(() => asset ? {
    assetPath: asset.path,
    kind: asset.kind,
    projectPath: context.projectPath,
    projectXml: context.projectXml,
    ...(exactCoreLayerContext(asset, context) ? { coreLayerContext: exactCoreLayerContext(asset, context) } : {}),
  } : null, [asset, context.projectPath, context.projectXml, context.projectSessionID]);

  const [parentEntry, setParentEntry] = useState<OwnerValue<GeometryParentSnapshot> | null>(null);
  const [topologyEntry, setTopologyEntry] = useState<OwnerValue<GeometryTopologySnapshot> | null>(null);
  const [meshEntry, setMeshEntry] = useState<OwnerValue<GeometryMeshSnapshot> | null>(null);
  const [culvertEntry, setCulvertEntry] = useState<OwnerValue<GeometryCulvertSnapshot> | null>(null);
  const parentRef = useRef(parentEntry); parentRef.current = parentEntry;
  const topologyRef = useRef(topologyEntry); topologyRef.current = topologyEntry;
  const meshRef = useRef(meshEntry); meshRef.current = meshEntry;
  const culvertRef = useRef(culvertEntry); culvertRef.current = culvertEntry;
  const [localDraft, setLocalDraft] = useState<GeometryLocalDraft>({ owner: sourceOwner, rows: {}, overrides: {}, deleted: {} });
  const [loading, setLoading] = useState<LoadingState>({ parent: false, topology: false, mesh: false, culverts: false });
  const [errors, setErrors] = useState<ErrorState>({});
  const [busyOperation, setBusyOperation] = useState('');
  const [savedNotice, setSavedNotice] = useState('');
  const [refreshToken, setRefreshToken] = useState(0);
  const [meshUndoStack, setMeshUndoStack] = useState<Array<{ owner: string; draftState: string; selectedRegionIDs: number[] }>>([]);
  const [meshRedoStack, setMeshRedoStack] = useState<Array<{ owner: string; draftState: string; selectedRegionIDs: number[] }>>([]);
  const topologyQueue = useRef<Promise<void>>(Promise.resolve());
  const meshQueue = useRef<Promise<void>>(Promise.resolve());
  const culvertQueue = useRef<Promise<void>>(Promise.resolve());
  const generationRef = useRef(0);
  const previousLoad = useRef<{ identity: string; backend: typeof backend } | null>(null);

  const parent = parentEntry?.owner === sourceOwner ? parentEntry.value : undefined;
  const topology = topologyEntry?.owner === sourceOwner ? topologyEntry.value : undefined;
  const mesh = meshEntry?.owner === sourceOwner ? meshEntry.value : undefined;
  const culverts = culvertEntry?.owner === sourceOwner ? culvertEntry.value : undefined;
  const draft = localDraft.owner === sourceOwner ? localDraft : undefined;
  const localRows = useCallback((kind: GeometryFeatureKind) => draft?.rows[kind] ?? [], [draft]);
  const localOverrides = useCallback((kind: GeometryFeatureKind, row: GeometryRow) => {
    if (!draft) return row;
    const values = { ...row.values };
    const prefix = `${kind}\u0000${row.id}\u0000`;
    for (const [key, value] of Object.entries(draft.overrides)) if (key.startsWith(prefix)) values[key.slice(prefix.length)] = value;
    return { ...row, values };
  }, [draft]);
  const topologyTable = useCallback((kind: 'node' | 'arc' | 'region') => {
    const table = topology?.[kind === 'node' ? 'nodes' : kind === 'arc' ? 'arcs' : 'regions'];
    return visibleTable(table, draft, kind);
  }, [draft, topology]);

  const setLocal = (mutate: (current: GeometryLocalDraft) => GeometryLocalDraft): void => {
    setLocalDraft((current) => mutate(current.owner === sourceOwner ? current : { owner: sourceOwner, rows: {}, overrides: {}, deleted: {} }));
  };
  const setTopology = (value: GeometryTopologySnapshot): void => setTopologyEntry({ owner: sourceOwner, value });
  const setMesh = (value: GeometryMeshSnapshot): void => setMeshEntry({ owner: sourceOwner, value });
  const setCulverts = (value: GeometryCulvertSnapshot): void => setCulvertEntry({ owner: sourceOwner, value });
  const setError = (key: keyof ErrorState, value?: string): void => setErrors((current) => ({ ...current, [key]: value }));
  const supports = (method: string): boolean => backend.supports(method);

  const ensureCurrent = (owner: string): void => {
    if (sourceOwnerRef.current !== owner) throw new DOMException('The geometry request belongs to an earlier project session.', 'AbortError');
  };
  const send = async <T,>(method: string, parameters: unknown, owner: string, options?: { signal?: AbortSignal; onProgress?: (progress: unknown) => void }): Promise<T> => {
    ensureCurrent(owner);
    if (!backend.supports(method)) throw new BackendOperationUnavailableError(method);
    const result = await backend.request<T>(method, parameters, options);
    ensureCurrent(owner);
    return result;
  };

  useEffect(() => {
    const owner = sourceOwner;
    const controller = new AbortController();
    const identity = JSON.stringify([asset?.path, asset?.kind, asset?.revision, context.projectPath,
      context.projectXml, context.projectSessionID, sourceOwner, refreshToken]);
    const previous = previousLoad.current;
    previousLoad.current = { identity, backend };
    if (previous?.identity === identity && previous.backend === backend && parentRef.current?.owner === owner)
      return () => controller.abort();
    const generation = ++generationRef.current;
    const isCurrent = (): boolean => generationRef.current === generation && sourceOwnerRef.current === owner && !controller.signal.aborted;
    setParentEntry(null); setTopologyEntry(null); setMeshEntry(null); setCulvertEntry(null);
    setLocalDraft({ owner, rows: {}, overrides: {}, deleted: {} });
    setMeshUndoStack([]); setMeshRedoStack([]); setErrors({}); setSavedNotice('');
    if (!asset || !model) { setLoading({ parent: false, topology: false, mesh: false, culverts: false }); return () => controller.abort(); }

    const load = async (): Promise<void> => {
      const nextLoading: LoadingState = { parent: false, topology: false, mesh: false, culverts: false };
      if (backend.supports('model.load')) nextLoading.parent = true;
      if (backend.supports('topology.load')) nextLoading.topology = true;
      if (backend.supports('geometry.mesh.load')) nextLoading.mesh = true;
      if (backend.supports('geometry.culverts.load')) nextLoading.culverts = true;
      setLoading(nextLoading);
      const parentPromise = (async (): Promise<void> => {
        if (!backend.supports('model.load')) return;
        try {
          const request = { assetPath: model.assetPath, kind: model.kind, projectPath: model.projectPath, projectXml: model.projectXml, sourceState: sourceStateFor(asset), coreLayerContext: model.coreLayerContext };
          const value = await backend.request<GeometryParentSnapshot>('model.load', request, { signal: controller.signal });
          if (isCurrent()) { setParentEntry({ owner, value }); setError('parent'); }
        } catch (error) { if (isCurrent()) setError('parent', geometryDiagnosticMessage(error)); }
        finally { if (isCurrent()) setLoading((state) => ({ ...state, parent: false })); }
      })();
      const topologyPromise = (async (): Promise<GeometryTopologySnapshot | undefined> => {
        if (!backend.supports('topology.load')) return undefined;
        try {
          const value = await backend.request<GeometryTopologySnapshot>('topology.load', { model, revision: asset.revision }, { signal: controller.signal });
          if (isCurrent()) { setTopologyEntry({ owner, value }); setError('topology'); }
          return value;
        } catch (error) { if (isCurrent()) setError('topology', geometryDiagnosticMessage(error)); return undefined; }
        finally { if (isCurrent()) setLoading((state) => ({ ...state, topology: false })); }
      })();
      const culvertPromise = (async (): Promise<void> => {
        if (!backend.supports('geometry.culverts.load')) return;
        try {
          const value = await backend.request<GeometryCulvertSnapshot>('geometry.culverts.load', { model, revision: asset.revision, draftState: null, selectedBarrelIDs: [] }, { signal: controller.signal });
          if (isCurrent()) { setCulvertEntry({ owner, value }); setError('culverts'); }
        } catch (error) { if (isCurrent()) setError('culverts', geometryDiagnosticMessage(error)); }
        finally { if (isCurrent()) setLoading((state) => ({ ...state, culverts: false })); }
      })();
      const topologyValue = await topologyPromise;
      const meshPromise = (async (): Promise<void> => {
        if (!backend.supports('geometry.mesh.load')) return;
        try {
          const value = await backend.request<GeometryMeshSnapshot>('geometry.mesh.load', {
            model,
            revision: asset.revision,
            draftState: null,
            topologyDraftState: topologyValue?.draftState ?? null,
            selectedRegionIDs: [],
          }, { signal: controller.signal });
          if (isCurrent()) { setMeshEntry({ owner, value }); setError('mesh'); }
        } catch (error) { if (isCurrent()) setError('mesh', geometryDiagnosticMessage(error)); }
        finally { if (isCurrent()) setLoading((state) => ({ ...state, mesh: false })); }
      })();
      await Promise.all([parentPromise, culvertPromise, meshPromise]);
    };
    void load();
    return () => { controller.abort(); if (generationRef.current === generation) generationRef.current += 1; };
  }, [asset?.path, asset?.kind, asset?.revision, context.projectPath, context.projectXml, context.projectSessionID, backend, backendRevision, model, sourceOwner, refreshToken]);

  const topologyDirty = !!topology?.isDirty;
  const meshDirty = !!mesh?.isDirty;
  const culvertDirty = !!culverts?.isDirty;
  const localOnly = !!draft && (Object.values(draft.rows).some((rows) => (rows?.length ?? 0) > 0)
    || Object.keys(draft.overrides).length > 0
    || Object.values(draft.deleted).some((rows) => (rows?.length ?? 0) > 0));
  const dirty = topologyDirty || meshDirty || culvertDirty || localOnly;
  const revision = parent?.revision ?? topology?.revision ?? mesh?.revision ?? culverts?.revision ?? asset?.revision;
  const dirtyOwner = mapOwner(context, asset, revision);

  useEffect(() => {
    if (!onDirtyStateChange) return;
    onDirtyStateChange(dirtyOwner, dirty);
    return () => onDirtyStateChange(dirtyOwner, false);
  }, [dirtyOwner, dirty, onDirtyStateChange]);

  const addLocalRow = (kind: GeometryFeatureKind, table: GeometryTable | undefined): void => {
    if (!table) return;
    const row = localRow(kind, table.columns, table.editableFields);
    setLocal((current) => ({ ...current, rows: { ...current.rows, [kind]: [...(current.rows[kind] ?? []), row] } }));
  };

  const updateLocalRow = (kind: GeometryFeatureKind, row: GeometryRow, field: GeometryField, value: string): void => {
    setLocal((current) => ({ ...current, rows: { ...current.rows, [kind]: updateLocalRows(current.rows[kind] ?? [], row.id, field.id, value) } }));
  };

  const setOverride = (kind: GeometryFeatureKind, row: GeometryRow, field: GeometryField, value: string): void => {
    setLocal((current) => ({ ...current, overrides: { ...current.overrides, [localKey(kind, row.id, field.id)]: value } }));
  };

  const enqueue = <T,>(queue: { current: Promise<void> }, action: () => Promise<T>): Promise<T> => {
    const next = queue.current.then(action, action);
    queue.current = next.then(() => undefined, () => undefined);
    return next;
  };

  const editTopology = (mutation: Record<string, unknown>): Promise<GeometryTopologySnapshot> => enqueue(topologyQueue, async () => {
    const current = topologyRef.current;
    if (!current || current.owner !== sourceOwner) throw new Error('Topology draft is not loaded for this asset.');
    const snapshot = current.value;
    const result = await send<GeometryTopologySnapshot>('topology.edit', { model, revision: snapshot.revision, draftState: snapshot.draftState, mutation }, sourceOwner);
    if (result.revision !== snapshot.revision) throw new Error('The topology response revision does not match the captured source revision.');
    setTopology(result); setError('topology');
    return result;
  });

  const onTopologyChange = (kind: 'node' | 'arc' | 'region', row: GeometryRow, field: GeometryField, value: string, isLocal: boolean): void => {
    if (isLocal) { updateLocalRow(kind, row, field, value); return; }
    const endpointCanEdit = kind !== 'node' && !!topology?.canEdit && supports('topology.edit') && row.fid != null;
    if (!endpointCanEdit) { setOverride(kind, row, field, value); return; }
    const regionInputFields = ['ConstantSize', 'SecondaryConstantSize', 'RotationDegrees', 'BiasInternal', 'TinResolution', 'SizeTolerance'];
    const operation = kind === 'region' && regionInputFields.includes(field.id) ? 'setRegionInput' : 'setField';
    void editTopology({ operation, featureKind: kind, featureIDs: [row.fid], fieldID: field.id, value }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
  };

  const onTopologyCommand = (kind: 'node' | 'arc' | 'region', row: GeometryRow, field: GeometryField, command: string, value?: string): void => {
    if (row.fid == null || !topology?.canEdit || !supports('topology.edit')) return;
    if (command === 'forceResetArcBias' && kind === 'arc' && field.id === 'Bias') {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [row.fid], fieldID: field.id }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
      return;
    }
    if (kind === 'region' && command === 'setRegionInput' && ['ConstantSize', 'SecondaryConstantSize', 'RotationDegrees', 'BiasInternal', 'TinResolution', 'SizeTolerance'].includes(field.id)) {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [row.fid], fieldID: field.id, value: value ?? '' }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
      return;
    }
    if (command === 'forceSetArcChoice' && kind === 'arc' && ['ConstrainStretching', 'LaplaceBC'].includes(field.id) && value != null) {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [row.fid], fieldID: field.id, value }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
      return;
    }
    if (kind === 'region' && command === 'forceSetPatchType' && field.id === 'PatchType' && value != null) {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [], controlPointFeatureIDs: [row.fid], value }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
      return;
    }
    if (kind === 'region' && command === 'forceSetRegionMultipoint' && field.id === 'Multipoint' && value != null) {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [], controlPointFeatureIDs: [row.fid], value }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
      return;
    }
    if (kind === 'region' && command === 'applyRecipe' && field.id === 'Recipe' && value != null) {
      void editTopology({ operation: command, featureKind: kind, featureIDs: [], controlPointFeatureIDs: [row.fid], recipeText: value }).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
    }
  };

  const onTopologyDelete = (kind: 'node' | 'arc' | 'region', rows: GeometryRow[]): void => {
    const localIDs = new Set((draft?.rows[kind] ?? []).map((row) => row.id));
    const local = rows.filter((row) => localIDs.has(row.id));
    const source = rows.filter((row) => !localIDs.has(row.id));
    if (local.length) setLocal((current) => ({ ...current, rows: { ...current.rows, [kind]: (current.rows[kind] ?? []).filter((row) => !local.some((item) => item.id === row.id)) } }));
    if (!source.length) return;
    if (!supports('topology.edit') || !topology?.canEdit || source.some((row) => row.fid == null)) {
      setLocal((current) => ({ ...current, deleted: { ...current.deleted, [kind]: [...new Set([...(current.deleted[kind] ?? []), ...source.map((row) => row.id)])] } }));
      return;
    }
    const ids = source.map((row) => row.fid!);
    const mutation = {
      operation: 'deleteSelection', featureKind: 'selection', featureIDs: [],
      arcFeatureIDs: kind === 'arc' ? ids : [], controlPointFeatureIDs: kind === 'region' ? ids : [], nodeFeatureIDs: kind === 'node' ? ids : [],
    };
    void editTopology(mutation).catch((error: unknown) => setError('topology', geometryDiagnosticMessage(error)));
  };

  const undoTopology = async (): Promise<void> => {
    if (!topology || !topology.canUndo || !supports('topology.edit')) return;
    await editTopology({ operation: 'undo', featureKind: 'selection', featureIDs: [], arcFeatureIDs: [], controlPointFeatureIDs: [], nodeFeatureIDs: [] });
  };
  const redoTopology = async (): Promise<void> => {
    if (!topology || !topology.canRedo || !supports('topology.edit')) return;
    await editTopology({ operation: 'redo', featureKind: 'selection', featureIDs: [], arcFeatureIDs: [], controlPointFeatureIDs: [], nodeFeatureIDs: [] });
  };

  const editMesh = (mutation: Record<string, unknown>, selectedRegionIDs = mesh?.selectedRegionIDs ?? []): Promise<GeometryMeshSnapshot> => enqueue(meshQueue, async () => {
    const current = meshRef.current;
    if (!current || current.owner !== sourceOwner) throw new Error('Mesh draft is not loaded for this asset.');
    const snapshot = current.value;
    const result = await send<GeometryMeshSnapshot>('geometry.mesh.edit', {
      model, revision: snapshot.revision, draftState: snapshot.draftState,
      topologyDraftState: topologyRef.current?.owner === sourceOwner ? topologyRef.current.value.draftState : null,
      selectedRegionIDs, mutation,
    }, sourceOwner);
    if (result.revision !== snapshot.revision) throw new Error('The mesh response revision does not match the captured source revision.');
    setMeshUndoStack((stack) => [...stack, { owner: sourceOwner, draftState: snapshot.draftState, selectedRegionIDs: snapshot.selectedRegionIDs }]);
    setMeshRedoStack([]); setMesh(result); setError('mesh');
    return result;
  });

  const onFlowAreaChange = (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean): void => {
    if (isLocal) { updateLocalRow('flowArea', row, field, value); return; }
    const flowArea = mesh?.flowAreas.find((area) => area.id === row.id);
    if (!flowArea || !mesh?.canEdit || !supports('geometry.mesh.edit')) { setOverride('flowArea', row, field, value); return; }
    void editMesh({ operation: 'setFlowAreaField', flowAreaIDs: [flowArea.id], expectedFlowAreaNames: [flowArea.name], fieldID: field.id, value }).catch((error: unknown) => setError('mesh', geometryDiagnosticMessage(error)));
  };

  const onRegionSelection = (ids: number[]): void => {
    if (!mesh || !supports('geometry.mesh.load')) return;
    const owner = sourceOwner;
    void send<GeometryMeshSnapshot>('geometry.mesh.load', {
      model, revision: mesh.revision, draftState: mesh.draftState,
      topologyDraftState: topology?.draftState ?? null, selectedRegionIDs: ids,
    }, owner).then(setMesh).catch((error: unknown) => setError('mesh', geometryDiagnosticMessage(error)));
  };

  const prepareMesh = (): void => {
    if (!mesh?.canEdit || !supports('geometry.mesh.edit')) return;
    void editMesh({ operation: 'prepare' }).catch((error: unknown) => setError('mesh', geometryDiagnosticMessage(error)));
  };

  const generateMesh = async (onProgress: (progress: unknown) => void): Promise<void> => {
    if (!mesh || !supports('geometry.mesh.generate')) throw new BackendOperationUnavailableError('geometry.mesh.generate');
    const owner = sourceOwner;
    setBusyOperation('geometry.mesh.generate');
    try {
      const operationID = globalThis.crypto?.randomUUID?.() ?? requestID();
      const result = await send<{ operationID?: string; revision: string; cancelled?: boolean; snapshot?: GeometryMeshSnapshot }>('geometry.mesh.generate', {
        model, revision: mesh.revision, draftState: mesh.draftState,
        topologyDraftState: topology?.draftState ?? null, selectedRegionIDs: mesh.selectedRegionIDs, operationID,
      }, owner, { onProgress });
      if (result.revision !== mesh.revision) throw new Error('The generation response belongs to a different source revision.');
      if (result.snapshot) {
        setMeshUndoStack((stack) => [...stack, { owner, draftState: mesh.draftState, selectedRegionIDs: mesh.selectedRegionIDs }]);
        setMeshRedoStack([]); setMesh(result.snapshot);
      }
    } finally { setBusyOperation(''); }
  };

  const degreeScan = async (maxFacesPerNode: number): Promise<GeometryMeshDegreeScan> => {
    if (!mesh || !supports('geometry.mesh.degreeErrors')) throw new BackendOperationUnavailableError('geometry.mesh.degreeErrors');
    setBusyOperation('geometry.mesh.degreeErrors');
    try {
      return await send<GeometryMeshDegreeScan>('geometry.mesh.degreeErrors', {
        model, revision: mesh.revision, draftState: mesh.draftState,
        topologyDraftState: topology?.draftState ?? null, maxFacesPerNode, includeIncidentCells: true,
      }, sourceOwner);
    } finally { setBusyOperation(''); }
  };

  const propertyTables = async (): Promise<unknown> => {
    if (!mesh || !supports('geometry.mesh.computePropertyTables')) throw new BackendOperationUnavailableError('geometry.mesh.computePropertyTables');
    setBusyOperation('geometry.mesh.computePropertyTables');
    try {
      const result = await send<{ snapshot?: GeometryMeshSnapshot; succeeded?: boolean; cancelled?: boolean }>('geometry.mesh.computePropertyTables', {
        model, revision: mesh.revision, draftState: mesh.draftState,
        topologyDraftState: topology?.draftState ?? null, selectedRegionIDs: mesh.selectedRegionIDs,
      }, sourceOwner);
      if (result.snapshot) setMesh(result.snapshot);
      return result;
    } finally { setBusyOperation(''); }
  };

  const undoMesh = async (): Promise<void> => {
    const previous = [...meshUndoStack].reverse().find((entry) => entry.owner === sourceOwner);
    if (!mesh || !previous || !supports('geometry.mesh.load')) return;
    setBusyOperation('geometry.mesh.undo');
    try {
      const result = await send<GeometryMeshSnapshot>('geometry.mesh.load', {
        model, revision: mesh.revision, draftState: previous.draftState,
        topologyDraftState: topology?.draftState ?? null, selectedRegionIDs: previous.selectedRegionIDs,
      }, sourceOwner);
      setMeshRedoStack((stack) => [...stack, { owner: sourceOwner, draftState: mesh.draftState, selectedRegionIDs: mesh.selectedRegionIDs }]);
      setMeshUndoStack((stack) => { const index = stack.lastIndexOf(previous); return index < 0 ? stack : stack.filter((_, itemIndex) => itemIndex !== index); });
      setMesh(result);
    } finally { setBusyOperation(''); }
  };
  const redoMesh = async (): Promise<void> => {
    const next = [...meshRedoStack].reverse().find((entry) => entry.owner === sourceOwner);
    if (!mesh || !next || !supports('geometry.mesh.load')) return;
    setBusyOperation('geometry.mesh.redo');
    try {
      const result = await send<GeometryMeshSnapshot>('geometry.mesh.load', {
        model, revision: mesh.revision, draftState: next.draftState,
        topologyDraftState: topology?.draftState ?? null, selectedRegionIDs: next.selectedRegionIDs,
      }, sourceOwner);
      setMeshUndoStack((stack) => [...stack, { owner: sourceOwner, draftState: mesh.draftState, selectedRegionIDs: mesh.selectedRegionIDs }]);
      setMeshRedoStack((stack) => { const index = stack.lastIndexOf(next); return index < 0 ? stack : stack.filter((_, itemIndex) => itemIndex !== index); });
      setMesh(result);
    } finally { setBusyOperation(''); }
  };

  const editCulvert = (mutation: Record<string, unknown>): Promise<GeometryCulvertSnapshot> => enqueue(culvertQueue, async () => {
    const current = culvertRef.current;
    if (!current || current.owner !== sourceOwner) throw new Error('Culvert draft is not loaded for this asset.');
    const snapshot = current.value;
    const result = await send<GeometryCulvertSnapshot>('geometry.culverts.edit', {
      model, revision: snapshot.revision, draftState: snapshot.draftState,
      mutation, selectedBarrelIDs: [],
    }, sourceOwner);
    if (result.revision !== snapshot.revision) throw new Error('The culvert response revision does not match the captured source revision.');
    setCulverts(result); setError('culverts');
    return result;
  });

  const onCulvertChange = (kind: 'barrel' | 'barrelProperties' | 'openingProperties', row: GeometryRow, field: GeometryField, value: string, isLocal: boolean): void => {
    if (isLocal) { updateLocalRow(kind, row, field, value); return; }
    if (!culverts?.canEdit || !supports('geometry.culverts.edit')) { setOverride(kind, row, field, value); return; }
    void editCulvert({ operation: 'setField', featureKind: kind, featureIDs: [row.id], fieldID: field.id, value }).catch((error: unknown) => setError('culverts', geometryDiagnosticMessage(error)));
  };

  const onCulvertCommand = (kind: 'barrel' | 'barrelProperties' | 'openingProperties', row: GeometryRow, field: GeometryField, command: string, value?: string): void => {
    if (!culverts?.canEdit || !supports('geometry.culverts.edit')) return;
    if (command === 'resetOpeningCoefficient' && kind === 'openingProperties' && ['KIn', 'KOut'].includes(field.id)) {
      void editCulvert({ operation: command, featureKind: kind, featureIDs: [row.id], fieldID: field.id }).catch((error: unknown) => setError('culverts', geometryDiagnosticMessage(error)));
      return;
    }
    if (command === 'forceSetOpeningType' && kind === 'openingProperties' && field.id === 'OpeningType' && value != null) {
      void editCulvert({ operation: command, featureKind: kind, featureIDs: [row.id], fieldID: field.id, value }).catch((error: unknown) => setError('culverts', geometryDiagnosticMessage(error)));
    }
  };

  const onCulvertDelete = (kind: 'barrel' | 'barrelProperties' | 'openingProperties', rows: GeometryRow[]): void => {
    const localIDs = new Set((draft?.rows[kind] ?? []).map((row) => row.id));
    const local = rows.filter((row) => localIDs.has(row.id));
    const source = rows.filter((row) => !localIDs.has(row.id));
    if (local.length) setLocal((current) => ({ ...current, rows: { ...current.rows, [kind]: (current.rows[kind] ?? []).filter((row) => !local.some((item) => item.id === row.id)) } }));
    if (!source.length) return;
    if (!supports('geometry.culverts.edit') || !culverts?.canEdit) {
      setLocal((current) => ({ ...current, deleted: { ...current.deleted, [kind]: [...new Set([...(current.deleted[kind] ?? []), ...source.map((row) => row.id)])] } }));
      return;
    }
    void editCulvert({ operation: 'delete', featureKind: kind, featureIDs: source.map((row) => row.id) }).catch((error: unknown) => setError('culverts', geometryDiagnosticMessage(error)));
  };

  const addCulvertBarrel = async (coordinates: Array<{ x: number; y: number }>): Promise<void> => {
    if (!supports('geometry.culverts.edit') || !culverts?.canEdit) {
      const row: GeometryRow = { ...localRow('barrel', culverts?.barrels.columns ?? [], culverts?.barrels.editableFields ?? []), polyline: coordinates, values: { Name: language === 'zh' ? '本地涵洞' : 'Local culvert', UpstreamInvert: '', DownstreamInvert: '' } };
      setLocal((current) => ({ ...current, rows: { ...current.rows, barrel: [...(current.rows.barrel ?? []), row] } }));
      return;
    }
    await editCulvert({ operation: 'addBarrel', featureKind: 'barrel', featureIDs: [], draftID: requestID(), polyline: coordinates });
  };

  const addCulvertPropertySet = (kind: 'barrelProperties' | 'openingProperties'): void => {
    if (!supports('geometry.culverts.edit') || !culverts?.canEdit) {
      addLocalRow(kind, kind === 'barrelProperties' ? culverts?.barrelProperties : culverts?.openingProperties);
      return;
    }
    void editCulvert({ operation: 'add', featureKind: kind, featureIDs: [], draftID: requestID() }).catch((error: unknown) => setError('culverts', geometryDiagnosticMessage(error)));
  };

  const undoCulverts = async (): Promise<void> => {
    if (!culverts?.isDirty || !supports('geometry.culverts.edit')) return;
    await editCulvert({ operation: 'undo', featureKind: 'barrel', featureIDs: [] });
  };

  const resetDrafts = (): void => {
    setLocalDraft({ owner: sourceOwner, rows: {}, overrides: {}, deleted: {} });
    setMeshUndoStack((stack) => stack.filter((entry) => entry.owner !== sourceOwner));
    setMeshRedoStack((stack) => stack.filter((entry) => entry.owner !== sourceOwner));
    setRefreshToken((value) => value + 1);
  };

  const save = async (): Promise<void> => {
    if (!model || !asset || !parent || !revision || !context.projectPath) throw new Error('Saving geometry requires a loaded geometry, source revision and owning project.');
    if (!dirty) throw new Error('There are no geometry changes to save.');
    if (localOnly) throw new Error('Local-only table changes cannot be included in the parent model transaction.');
    if (!supports('model.save')) throw new BackendOperationUnavailableError('model.save');
    if (topologyDirty && !topology?.draftState) throw new Error('The topology draft state is missing.');
    if (meshDirty && !mesh?.draftState) throw new Error('The mesh draft state is missing.');
    if (culvertDirty && !culverts?.draftState) throw new Error('The culvert draft state is missing.');
    const owner = sourceOwner;
    setBusyOperation('model.save'); setError('save'); setSavedNotice('');
    try {
      const result = await send<GeometryParentSnapshot & { sourceState?: string | null }>('model.save', {
        assetPath: model.assetPath,
        kind: model.kind,
        projectPath: model.projectPath,
        projectXml: model.projectXml,
        sourceState: parent.sourceState ?? sourceStateFor(asset),
        revision,
        fields: parent.fields ?? {},
        images: parent.images ?? {},
        geometryTopologyState: topology?.draftState ?? null,
        geometryMeshState: mesh?.draftState ?? null,
        geometryCulvertState: culverts?.draftState ?? null,
        coreLayerContext: model.coreLayerContext,
      }, owner);
      const nextContext: WorkbenchContext = {
        ...context,
        selectedAsset: { ...asset, revision: result.revision, locator: { ...(asset.locator ?? {}), ...(result.sourceState ? { sourceState: result.sourceState } : {}) } },
      };
      onContextChange(nextContext);
      setSavedNotice('saved');
    } catch (error) {
      const reason = geometryDiagnosticMessage(error);
      setError('save', reason);
      throw error;
    } finally { setBusyOperation(''); }
  };

  const wrappedTopologyTable = (kind: 'node' | 'arc' | 'region'): GeometryTable | undefined => {
    const table = topologyTable(kind);
    if (!table || !draft) return table;
    const deleted = new Set(draft.deleted[kind] ?? []);
    return { ...table, rows: table.rows.filter((row) => !deleted.has(row.id)).map((row) => localOverrides(kind, row)) };
  };
  const wrappedCulvertTable = (kind: 'barrel' | 'barrelProperties' | 'openingProperties'): GeometryTable | undefined => {
    if (!culverts) return undefined;
    const table = kind === 'barrel' ? culverts.barrels : kind === 'barrelProperties' ? culverts.barrelProperties : culverts.openingProperties;
    const deleted = new Set(draft?.deleted[kind] ?? []);
    return { ...table, rows: table.rows.filter((row) => !deleted.has(row.id)).map((row) => localOverrides(kind, row)) };
  };
  const wrappedMeshRegions = (): GeometryTable | undefined => {
    const table = mesh?.regions;
    return table ? visibleTable(table, draft, 'region') : undefined;
  };

  return {
    asset, ownerKey: sourceOwner, model, parent, topology, mesh, culverts, draft, loading, errors, busyOperation, savedNotice,
    topologyDirty, meshDirty, culvertDirty, localOnly, dirty, revision, dirtyOwner,
    supports, localRows, addLocalRow, onTopologyChange, onTopologyCommand, onTopologyDelete, undoTopology, redoTopology,
    onFlowAreaChange, onRegionSelection, prepareMesh, generateMesh, degreeScan, propertyTables, undoMesh, redoMesh,
    onCulvertChange, onCulvertCommand, onCulvertDelete, addCulvertBarrel, addCulvertPropertySet, undoCulverts,
    resetDrafts, save, wrappedTopologyTable, wrappedCulvertTable, wrappedMeshRegions,
    reload: () => setRefreshToken((value) => value + 1), setError,
    canLoadTopology: supports('topology.load'), canLoadMesh: supports('geometry.mesh.load'), canLoadCulverts: supports('geometry.culverts.load'),
    canLoadParent: supports('model.load'), canSave: supports('model.save') && !!parent && !!context.projectPath && !!revision && dirty && !localOnly,
    canGenerate: supports('geometry.mesh.generate'), canPrepare: supports('geometry.mesh.edit'), canDegreeScan: supports('geometry.mesh.degreeErrors'), canPropertyTables: supports('geometry.mesh.computePropertyTables'),
    canUndoTopology: !!topology?.canUndo && supports('topology.edit'), canRedoTopology: !!topology?.canRedo && supports('topology.edit'),
    canUndoMesh: meshUndoStack.some((entry) => entry.owner === sourceOwner) && supports('geometry.mesh.load'), canRedoMesh: meshRedoStack.some((entry) => entry.owner === sourceOwner) && supports('geometry.mesh.load'),
    canUndoCulverts: !!culverts?.isDirty && parseMutations(culverts.draftState).length > 0 && supports('geometry.culverts.edit'),
    saveAction: async (): Promise<void> => { try { await save(); } catch { /* error is surfaced in the page */ } },
  };
}
