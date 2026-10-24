import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import { loadMapDocumentCatalog } from '../map/ProjectTreeListener';
import type { LoadedMapDocumentCatalog } from '../map/ProjectTreeListener';
import { displaySourceKind } from './GeoprocessingToolBase';
import { CoreLayerImportDialog } from './CoreLayerImportDialog';
import { ClassificationImporter } from './ClassificationImporter';
import { TerrainImporter } from './TerrainImporter';
import { AttributeTableEditorControl } from './attributes/AttributeTableEditorControl';
import './coreImportWorkflow.css';

type Props = FeaturePageProps;

export function SourceManagerControl({ language, context, backend, onContextChange, onContextCommit, onNavigate, hasUnsavedDrafts }: Props) {
  const zh = language === 'zh';
  const source = context.selectedAsset;
  const methods = ['map.document.list', 'map.tree.load', 'workspace.file.inspect', 'workspace.import.coreLayer.inspect', 'workspace.import.coreLayer.perform', 'map.vector.captureShapefile', 'attributeTable.load', 'attributeTable.transform'];
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
  const ownerRef = useRef(owner); ownerRef.current = owner;
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [catalog, setCatalog] = useState<LoadedMapDocumentCatalog>();
  const [catalogBusy, setCatalogBusy] = useState(false);
  const [catalogError, setCatalogError] = useState<string>();
  const [catalogDiagnostic, setCatalogDiagnostic] = useState<string>();
  const [coreDialogOpen, setCoreDialogOpen] = useState(false);

  useEffect(() => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    setCatalog(undefined); setCatalogBusy(false); setCatalogError(undefined); setCatalogDiagnostic(undefined);
  }, [owner]);

  useEffect(() => () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }, []);

  const readMapCatalog = async (): Promise<void> => {
    if (!context.projectPath || !context.projectRevision || context.projectXml === null) {
      setCatalogError(text(language, '地图目录需要已保存工程、XML 快照和工程修订。', 'The map catalog requires a saved project, XML snapshot, and project revision.'));
      return;
    }
    const requestOwner = owner;
    controller.current?.abort();
    const requestController = new AbortController();
    controller.current = requestController;
    const requestGeneration = ++generation.current;
    setCatalogBusy(true); setCatalogError(undefined); setCatalogDiagnostic(undefined); setCatalog(undefined);
    try {
      const value = await loadMapDocumentCatalog(backend, context, { signal: requestController.signal });
      if (requestController.signal.aborted || generation.current !== requestGeneration || ownerRef.current !== requestOwner) return;
      if (value.projectRevision.toUpperCase() !== context.projectRevision.toUpperCase()) {
        throw new Error(text(language, '地图目录回执与当前工程修订不匹配。', 'The map catalog receipt does not match the active project revision.'));
      }
      setCatalog(value);
    } catch (caught) {
      if (!requestController.signal.aborted && generation.current === requestGeneration && ownerRef.current === requestOwner) {
        setCatalogError(message(caught, language));
        setCatalogDiagnostic(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      if (generation.current === requestGeneration) setCatalogBusy(false);
    }
  };

  const activeMap = catalog?.documents.find(document => document.mapIndex === catalog.activeMapIndex) ?? null;
  const activeMapLabel = activeMap ? `${activeMap.name} · ${activeMap.mapType} · #${activeMap.mapIndex}` : null;

  return (
    <div className="tools-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '工程与地图源' : 'PROJECT AND MAP SOURCES'}</span><h2>{zh ? '数据源' : 'Data sources'}</h2>
          <p>{zh ? '读取当前工程和真实地图目录，并按已检查的来源身份执行核心图层复制、地形或分类导入。' : 'Read the active project and saved map catalog, then use inspected source identities for core-layer copy, terrain import, or classification import.'}</p></div>
        <span className="tools-badge">{zh ? 'C# Host 工作流' : 'C# Host workflow'}</span>
      </section>
      <div className="tools-source-grid">
        <section className="tools-subpanel"><div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {zh ? '工程' : 'PROJECT'}</span><h3>{zh ? '当前工程' : 'Current project'}</h3></div></div>
          <dl className="tools-source-details"><div><dt>{zh ? '会话' : 'Session'}</dt><dd>{context.projectSessionID || '—'}</dd></div><div><dt>{zh ? '工程路径' : 'Project path'}</dt><dd>{context.projectPath || (zh ? '未打开' : 'Not open')}</dd></div><div><dt>{zh ? '工程修订' : 'Project revision'}</dt><dd>{context.projectRevision || (zh ? '未捕获' : 'Not captured')}</dd></div><div><dt>{zh ? 'XML 快照' : 'XML snapshot'}</dt><dd>{context.projectXml ? `${context.projectXml.length.toLocaleString()} ${zh ? '字符' : 'characters'}` : (zh ? '未提供' : 'Not provided')}</dd></div></dl>
        </section>
        <section className="tools-subpanel"><div className="tools-subpanel-heading"><div><span className="tools-kicker">02 / {zh ? '选中源' : 'SELECTED SOURCE'}</span><h3>{zh ? '当前选中资产' : 'Selected asset'}</h3></div></div>
          {source ? <dl className="tools-source-details"><div><dt>{zh ? '名称' : 'Name'}</dt><dd>{source.name}</dd></div><div><dt>{zh ? '类型' : 'Kind'}</dt><dd>{displaySourceKind(source.kind, language)}</dd></div><div><dt>{zh ? '路径' : 'Path'}</dt><dd>{source.path}</dd></div><div><dt>{zh ? '修订' : 'Revision'}</dt><dd>{source.revision || (zh ? '载入时捕获' : 'Captured on load')}</dd></div><div><dt>{zh ? '定位信息' : 'Locator'}</dt><dd>{source.locator ? Object.keys(source.locator).join(', ') || '—' : (zh ? '未提供' : 'Not provided')}</dd></div></dl> : <p className="tools-muted">{zh ? '当前没有选中的工程数据源。' : 'No project asset is selected.'}</p>}
        </section>
      </div>
      <AttributeTableEditorControl language={language} context={context} backend={backend} onContextChange={onContextChange}
        onNavigate={onNavigate} onContextCommit={onContextCommit} hasUnsavedDrafts={hasUnsavedDrafts} />
      <section className="tools-subpanel tools-source-method-panel"><div className="tools-subpanel-heading"><div><span className="tools-kicker">03 / {zh ? '地图目录与来源操作' : 'MAP CATALOG AND SOURCE ACTIONS'}</span><h3>{zh ? '工程地图目录' : 'Project map catalog'}</h3></div></div>
        <p className="tools-muted">{zh ? '目录由 map.document.list 和 map.tree.load 从 C# Host 读取，并使用当前工程路径、XML、会话和修订核验。' : 'The catalog is read from C# through map.document.list and map.tree.load and checked against the active project path, XML, session, and revision.'}</p>
        <div className="ci-actions">
          <button type="button" className="ci-button ci-primary" disabled={catalogBusy || !context.projectPath || !context.projectRevision || !context.projectXml || !backend.supports('map.document.list') || !backend.supports('map.tree.load')} onClick={() => void readMapCatalog()}>
            {catalogBusy ? (zh ? '读取地图目录中…' : 'Reading map catalog…') : (zh ? '读取工程地图目录' : 'Read project map catalog')}</button>
          <button type="button" className="ci-button" disabled={!context.projectPath || !context.projectXml} onClick={() => setCoreDialogOpen(true)}>{zh ? '检查 / 导入核心图层' : 'Inspect / import core layer'}</button>
        </div>
        {catalogError && <div className="ci-error" role="alert"><strong>{catalogError}</strong>{catalogDiagnostic && <details><summary>{zh ? '原始诊断' : 'Raw diagnostic'}</summary><pre>{catalogDiagnostic}</pre></details>}</div>}
        {catalog && <>
          <div className="tools-service-grid"><div className="tools-service-item"><code>{zh ? '地图数量' : 'Map count'}</code><span>{catalog.maps.length}</span></div><div className="tools-service-item"><code>{zh ? '活动地图' : 'Active map'}</code><span>{activeMapLabel || (zh ? '未设置' : 'Not set')}</span></div><div className="tools-service-item"><code>{zh ? '可视化修订' : 'Visualization revision'}</code><span>{catalog.visualizationRevision || '—'}</span></div></div>
          {catalog.documents.length > 0 && <ul className="ci-operation-list">{catalog.documents.map(document => <li key={document.id}><strong>{document.mapType}</strong><span>#{document.mapIndex} · {document.name}{document.mapIndex === catalog.activeMapIndex ? (zh ? '（活动）' : ' (active)') : ''}</span><small>{document.roots.length} {zh ? '个顶层节点' : 'top-level nodes'}</small></li>)}</ul>}
        </>}
        <div className="tools-service-grid">{methods.map(method => <div className="tools-service-item" key={method}><code>{method}</code><span className={`tools-method-state ${backend.supports(method) ? 'is-connected' : ''}`}>{backend.supports(method) ? (zh ? 'C# 可调用' : 'C# available') : (zh ? '未注册' : 'Not registered')}</span></div>)}</div>
      </section>
      {source?.kind === 'Terrain' && <TerrainImporter language={language} context={context} backend={backend} onContextChange={onContextChange}
        onContextCommit={onContextCommit} onNavigate={onNavigate} hasUnsavedDrafts={hasUnsavedDrafts} />}
      {source?.kind === 'Surface Layers' && <ClassificationImporter language={language} context={context} backend={backend} onContextChange={onContextChange}
        onContextCommit={onContextCommit} onNavigate={onNavigate} hasUnsavedDrafts={hasUnsavedDrafts} />}
      <CoreLayerImportDialog open={coreDialogOpen} language={language} context={context} source={source} backend={backend} activeMapLabel={activeMapLabel}
        onClose={() => setCoreDialogOpen(false)} />
    </div>
  );
}
