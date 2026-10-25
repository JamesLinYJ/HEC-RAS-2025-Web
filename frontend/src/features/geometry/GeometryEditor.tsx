import { useEffect, useMemo, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { GeometryCulvertsPage } from './GeometryCulvertsPage';
import { GeometryMeshPage } from './GeometryMeshPage';
import { GeometrySummaryPage } from './GeometrySummaryPage';
import { GeometryTopologyArcsPage } from './GeometryTopologyArcsPage';
import { GeometryTopologyNodesPage } from './GeometryTopologyNodesPage';
import { GeometryTopologyRegionsPage } from './GeometryTopologyRegionsPage';
import { GeometryTopologySummaryPage } from './GeometryTopologySummaryPage';
import './geometry.css';
import { gfield, gkind, gt } from './geometryText';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryFeatureKind, GeometryRow } from './geometryTypes';
import { ArcImportDialog } from './ArcImportDialog';
import { useGeometryWorkbench } from './useGeometryWorkbench';
import { useGeometryTopologyMap } from './useGeometryTopologyMap';

type GeometryPageID = 'summary' | 'topologySummary' | 'nodes' | 'arcs' | 'regions' | 'mesh' | 'culverts';

const pages: Array<{ id: GeometryPageID; label: Parameters<typeof gt>[1] }> = [
  { id: 'summary', label: 'summary' }, { id: 'topologySummary', label: 'topology' },
  { id: 'nodes', label: 'nodes' }, { id: 'arcs', label: 'arcs' }, { id: 'regions', label: 'regions' },
  { id: 'mesh', label: 'mesh' }, { id: 'culverts', label: 'culverts' },
];

function localValidation(language: 'zh' | 'en', draft: ReturnType<typeof useGeometryWorkbench>['draft']): string[] {
  if (!draft) return [];
  const problems: string[] = [];
  for (const [kind, rows] of Object.entries(draft.rows) as Array<[GeometryFeatureKind, GeometryRow[] | undefined]>) {
    for (const row of rows ?? []) {
      for (const [fieldID, value] of Object.entries(row.values)) {
        if (!value.trim()) continue;
        const fieldLike = /(?:spacing|rise|span|invert|mannings|depth|kIn|kOut)/i.test(fieldID);
        const location = `${gkind(language, kind)} ${row.id} · ${gfield(language, fieldID)}`;
        if (fieldLike && !Number.isFinite(Number(value))) problems.push(language === 'zh' ? `${location}：${gt(language, 'invalidNumber')}` : `${location}: invalid number`);
        if (/spacing|rise|span/i.test(fieldID) && Number(value) <= 0) problems.push(language === 'zh' ? `${location}：${gt(language, 'positiveFieldError')}。` : `${location}: value must be greater than zero`);
      }
      if ((row.values.Name ?? '').trim() === '' && Object.hasOwn(row.values, 'Name')) {
        const location = `${gkind(language, kind)} ${row.id} · ${gfield(language, 'Name')}`;
        problems.push(language === 'zh' ? `${location}：${gt(language, 'required')}` : `${location}: required`);
      }
    }
  }
  return [...new Set(problems)];
}

function errorPanel(language: 'zh' | 'en', error?: string) {
  return error ? <GeometryError language={language} error={error} title={gt(language, 'loadFailed')} /> : null;
}

function operationLabel(language: 'zh' | 'en', operation: string): string {
  const known: Record<string, readonly [string, string]> = {
    'model.load': ['读取几何', 'Load geometry'],
    'model.save': ['保存几何', 'Save geometry'],
    'topology.load': ['读取拓扑', 'Load topology'],
    'topology.edit': ['修改拓扑', 'Edit topology'],
    'geometry.mesh.load': ['读取网格', 'Load mesh'],
    'geometry.mesh.edit': ['修改网格', 'Edit mesh'],
    'geometry.mesh.generate': ['生成网格', 'Generate mesh'],
    'geometry.mesh.computePropertyTables': ['计算网格属性表', 'Compute mesh property tables'],
    'geometry.culverts.load': ['读取涵洞数据', 'Load culverts'],
    'geometry.culverts.edit': ['修改涵洞数据', 'Edit culverts'],
  };
  if (!operation) return gt(language, 'idle');
  const label = known[operation];
  return label ? label[language === 'zh' ? 0 : 1] : language === 'zh' ? '正在处理' : 'In progress';
}

export default function GeometryPage(props: FeaturePageProps) {
  const [page, setPage] = useState<GeometryPageID>('summary');
  const [arcImportOpen, setArcImportOpen] = useState(false);
  const workspace = useGeometryWorkbench(props);
  const { language } = props;
  const workbenchBusy = !!workspace.busyOperation;
  const topologyMapWorkspace = useGeometryTopologyMap({
    active: page === 'nodes' || page === 'arcs',
    owner: workspace.ownerKey,
    backend: props.backend,
    backendRevision: props.backendRevision,
    context: props.context,
    model: workspace.model,
    topology: workspace.topology,
    canEdit: !!workspace.topology?.canEdit && workspace.supports('topology.edit'),
    busy: workbenchBusy,
    onLoadArcPoints: workspace.loadArcPoints,
    onMapCommand: workspace.editTopologyMapCommand,
  });
  const busy = workbenchBusy || topologyMapWorkspace.interactionBusy;
  useEffect(() => { setArcImportOpen(false); }, [workspace.ownerKey]);
  const validation = useMemo(() => localValidation(language, workspace.draft), [language, workspace.draft]);
  const displayedTopology = workspace.topology;
  const displayedMesh = workspace.mesh ? { ...workspace.mesh, regions: workspace.wrappedMeshRegions() ?? workspace.mesh.regions } : undefined;
  const displayedCulverts = workspace.culverts ? {
    ...workspace.culverts,
    barrels: workspace.wrappedCulvertTable('barrel') ?? workspace.culverts.barrels,
    barrelProperties: workspace.wrappedCulvertTable('barrelProperties') ?? workspace.culverts.barrelProperties,
    openingProperties: workspace.wrappedCulvertTable('openingProperties') ?? workspace.culverts.openingProperties,
  } : undefined;

  const canUndo = page === 'mesh' ? workspace.canUndoMesh : page === 'culverts' ? workspace.canUndoCulverts : workspace.canUndoTopology;
  const canRedo = page === 'mesh' ? workspace.canRedoMesh : workspace.canRedoTopology;
  const undo = (): void => {
    if (page === 'mesh') void workspace.undoMesh().catch((error: unknown) => workspace.setError('mesh', geometryDiagnosticMessage(error)));
    else if (page === 'culverts') void workspace.undoCulverts().catch((error: unknown) => workspace.setError('culverts', geometryDiagnosticMessage(error)));
    else void workspace.undoTopology().catch((error: unknown) => workspace.setError('topology', geometryDiagnosticMessage(error)));
  };
  const redo = (): void => {
    if (page === 'mesh') void workspace.redoMesh().catch((error: unknown) => workspace.setError('mesh', geometryDiagnosticMessage(error)));
    else void workspace.redoTopology().catch((error: unknown) => workspace.setError('topology', geometryDiagnosticMessage(error)));
  };

  const topologyPageProps = (kind: 'node' | 'arc' | 'region') => ({
    language,
    table: workspace.wrappedTopologyTable(kind),
    localRows: workspace.localRows(kind),
    canEdit: !!workspace.topology?.canEdit && workspace.supports('topology.edit'),
    busy,
    onChange: (row: GeometryRow, field: Parameters<typeof workspace.onTopologyChange>[2], value: string, isLocal: boolean) => workspace.onTopologyChange(kind, row, field, value, isLocal),
    onCommand: (row: GeometryRow, field: Parameters<typeof workspace.onTopologyChange>[2], command: string, value?: string) => workspace.onTopologyCommand(kind, row, field, command, value),
    topologySnapshot: workspace.topology,
    canLoadArcPoints: workspace.canLoadArcPoints,
    canLoadRecipe: workspace.canLoadRecipe,
    onLoadArcPoints: workspace.loadArcPoints,
    onLoadRecipe: workspace.loadRecipe,
    onTopologyTagChange: workspace.editTopologyTag,
    onTopologyTagCreate: workspace.createTopologyTag,
    canFilterTopology: workspace.canFilterTopology,
    onTopologyFilter: workspace.filterTopology,
    onApplyTopologyFactor: workspace.applyTopologyFactor,
    onArcPointCoordinateChange: workspace.editArcPointCoordinate,
    onApplyRecipe: workspace.applyTopologyRecipe,
    ...((kind === 'node' || kind === 'arc') ? {
      onSelectionChange: (rows: GeometryRow[]) => topologyMapWorkspace.onTableSelectionChange(kind, rows),
    } : {}),
    onTopologyResize: (featureKind: 'arc' | 'region', row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest') => {
      void workspace.resizeTopology(featureKind, row, operation, axis)
        .catch((error: unknown) => workspace.setError('topology', geometryDiagnosticMessage(error)));
    },
    onDelete: (rows: GeometryRow[]) => workspace.onTopologyDelete(kind, rows),
    onLocalAdd: () => workspace.addLocalRow(kind, workspace.wrappedTopologyTable(kind)),
  });

  const renderFeature = () => {
    switch (page) {
      case 'summary': return <GeometrySummaryPage language={language} asset={workspace.asset} projectPath={props.context.projectPath} sessionID={props.context.projectSessionID} topology={workspace.topology} mesh={workspace.mesh} culverts={workspace.culverts} parent={workspace.parent} loading={Object.values(workspace.loading).some(Boolean)} />;
      case 'topologySummary': return <GeometryTopologySummaryPage language={language} snapshot={displayedTopology} onNavigate={(target) => setPage(target)}
        canEdit={!!workspace.topology?.canEdit && workspace.supports('topology.edit')} busy={busy} onApplyFactor={workspace.applyTopologyFactor} />;
      case 'nodes': return <GeometryTopologyNodesPage key={workspace.ownerKey} {...topologyPageProps('node')} mapWorkspace={topologyMapWorkspace} />;
      case 'arcs': return <GeometryTopologyArcsPage key={workspace.ownerKey} {...topologyPageProps('arc')} mapWorkspace={topologyMapWorkspace} />;
      case 'regions': return <GeometryTopologyRegionsPage key={workspace.ownerKey} {...topologyPageProps('region')} />;
      case 'mesh': return <GeometryMeshPage key={workspace.ownerKey}
        language={language}
        snapshot={displayedMesh}
        context={props.context}
        backend={props.backend}
        backendRevision={props.backendRevision}
        model={workspace.model}
        topologySnapshot={workspace.topology}
        localRegionRows={[]}
        busy={busy}
        canEdit={!!workspace.mesh?.canEdit && workspace.supports('geometry.mesh.edit')}
        canGenerate={workspace.canGenerate}
        canPrepare={workspace.canPrepare}
        canDegreeScan={workspace.canDegreeScan}
        canPropertyTables={workspace.canPropertyTables}
        onFlowAreaChange={workspace.onFlowAreaChange}
        onRegionSelection={workspace.onRegionSelection}
        onPatchMutation={workspace.applyPatchMutation}
        onPrepare={workspace.prepareMesh}
        onGenerate={workspace.generateMesh}
        onDegreeScan={workspace.degreeScan}
        onPropertyTables={workspace.propertyTables}
      />;
      case 'culverts': return <GeometryCulvertsPage key={workspace.ownerKey}
        language={language}
        snapshot={displayedCulverts}
        localBarrels={workspace.localRows('barrel')}
        localBarrelProperties={workspace.localRows('barrelProperties')}
        localOpeningProperties={workspace.localRows('openingProperties')}
        canEdit={!!workspace.culverts?.canEdit && workspace.supports('geometry.culverts.edit')}
        busy={busy}
        onChange={workspace.onCulvertChange}
        onCommand={workspace.onCulvertCommand}
        onBarrelSelection={workspace.onCulvertSelection}
        onSelectionChange={workspace.onCulvertSelectionChange}
        onSelectionCommand={workspace.onCulvertSelectionCommand}
        onDelete={workspace.onCulvertDelete}
        onAddBarrel={workspace.addCulvertBarrel}
        onAddPropertySet={workspace.addCulvertPropertySet}
      />;
    }
  };

  const loadingForPage = page === 'summary' ? Object.values(workspace.loading).some(Boolean) : page === 'mesh' ? workspace.loading.mesh : page === 'culverts' ? workspace.loading.culverts : workspace.loading.topology;
  const pageError = page === 'mesh' ? workspace.errors.mesh : page === 'culverts' ? workspace.errors.culverts : page === 'summary' ? workspace.errors.parent : workspace.errors.topology;
  const status = !workspace.asset ? gt(language, 'noAsset') : busy ? gt(language, 'operationRunning') : workspace.dirty ? gt(language, 'dirty') : gt(language, 'clean');
  const ownerRevision = workspace.revision ?? workspace.asset?.revision ?? '—';

  return (
    <div className="geometry-page">
      <header className="geometry-header">
        <div className="geometry-heading">
          <div className="geometry-eyebrow">HEC-RAS 2025 · {gt(language, 'geometry')}</div>
          <h1>{workspace.asset?.name ?? gt(language, 'geometry')}</h1>
          <p className="geometry-subtitle">{workspace.asset ? `${gkind(language, workspace.asset.kind)} · ${workspace.asset.path}` : gt(language, 'noAsset')}</p>
        </div>
        <div className="geometry-header-actions">
          <span className={`geometry-status-pill ${workspace.dirty ? 'is-dirty' : 'is-clean'}`}>{status}</span>
          <button className="geometry-icon-button" disabled={!canUndo || busy} onClick={undo} title={gt(language, 'undo')}>{gt(language, 'undo')}</button>
          <button className="geometry-icon-button" disabled={!canRedo || busy} onClick={redo} title={gt(language, 'redo')}>{gt(language, 'redo')}</button>
          <button className="geometry-secondary" disabled={busy || !workspace.asset} onClick={workspace.resetDrafts}>{gt(language, 'reset')}</button>
          <button className="geometry-primary" disabled={!workspace.canSave || busy} onClick={() => void workspace.saveAction()}>{busyOperationLabel(language, workspace.busyOperation, workspace.canSave)}</button>
          <button className="geometry-secondary" disabled={!workspace.canInspectVectorImport || busy}
            onClick={() => setArcImportOpen(true)}>{language === 'zh' ? '导入线 / 面' : 'Import lines / polygons'}</button>
          {workspace.asset && <button className="geometry-icon-button" disabled={busy || (workspace.dirty && !workspace.localOnly)} onClick={workspace.reload} title={gt(language, 'load')}>{gt(language, 'load')}</button>}
        </div>
      </header>
      <nav className="geometry-tabstrip" aria-label={gt(language, 'geometry')}>
        {pages.map((item) => <button className="geometry-tab" type="button" key={item.id} aria-current={page === item.id ? 'page' : undefined} onClick={() => setPage(item.id)}>{gt(language, item.label)}</button>)}
      </nav>
      <div className="geometry-toolbar">
        <div className="geometry-toolbar-group">
          <span className="geometry-count-pill">{gt(language, 'revision')}: {ownerRevision}</span>
          {workspace.dirty && <span className="geometry-count-pill">{gt(language, 'parentTxn')}</span>}
          {loadingForPage && <span className="geometry-loading">{gt(language, 'loading')}</span>}
        </div>
        <span className="geometry-toolbar-label">{workspace.canLoadParent ? gt(language, 'sourceBacked') : gt(language, 'localEditMode')}</span>
      </div>
      {errorPanel(language, pageError)}
      {workspace.localOnly && <div className="geometry-local-banner">{gt(language, 'saveBlocked')}</div>}
      {workspace.dirty && !workspace.localOnly && !workspace.supports('model.save') && <div className="geometry-local-banner">{gt(language, 'saveUnavailable')}</div>}
      {renderFeature()}
      <section className="geometry-panel" style={{ marginTop: 14 }}>
        <header className="geometry-panel-heading"><div><h2>{gt(language, 'validation')}</h2><p>{gt(language, 'validationNote')}</p></div><span className={`geometry-status-pill ${validation.length ? 'is-dirty' : 'is-clean'}`}>{validation.length ? `${validation.length} ${gt(language, 'validationCount')}` : gt(language, 'valid')}</span></header>
        {validation.length ? <div className="geometry-validation-list">{validation.map((item) => <div className="geometry-validation-item" key={item}>{item}</div>)}</div> : <div className="geometry-panel-body"><span className="geometry-ok">{gt(language, 'valid')}</span></div>}
      </section>
      {workspace.errors.save && <GeometryError language={language} error={workspace.errors.save} style={{ marginTop: 12 }} />}
      {workspace.savedNotice && <div className="geometry-ok" role="status" style={{ marginTop: 10 }}>{gt(language, workspace.savedNotice === 'saved' ? 'saved' : 'idle')}</div>}
      <footer className="geometry-table-footer"><span>{gt(language, 'dirtyCaption')}</span><span>{gt(language, 'operation')}: {operationLabel(language, workspace.busyOperation)}</span></footer>
      <ArcImportDialog key={`arc-import:${workspace.ownerKey}`} open={arcImportOpen} language={language} context={props.context} backend={props.backend}
        model={workspace.model} topology={workspace.topology} capture={workspace.vectorImportCapture}
        canInspect={workspace.canInspectVectorImport} canApply={workspace.canApplyVectorImport}
        onInspect={workspace.inspectVectorImport} onApply={workspace.applyVectorImport} onClose={() => setArcImportOpen(false)} />
    </div>
  );
}

function busyOperationLabel(language: 'zh' | 'en', operation: string, canSave: boolean): string {
  if (operation === 'model.save') return gt(language, 'saving');
  return canSave ? gt(language, 'save') : gt(language, 'save');
}
