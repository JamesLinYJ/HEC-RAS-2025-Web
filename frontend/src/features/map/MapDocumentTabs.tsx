import { text } from '../../shared/workbench';
import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import type { MapDocumentTab } from './mapTypes';
import { mapDocumentKind, mapError } from './mapText';
import { NewMapFlyout } from './NewMapFlyout';
import { MapDocumentActions } from './MapDocumentActions';
import type { MapDocumentActionNotice } from './MapDocumentActions';

interface MapDocumentTabsProps {
  context: WorkbenchContext;
  backend: BackendGateway;
  documents: MapDocumentTab[];
  activeID: string | null;
  visualizationRevision: string | null;
  loading: boolean;
  interactionDisabled?: boolean;
  loadError: unknown;
  onSelect: (id: string | null) => void;
  onCreated: (document: MapDocumentTab) => void;
  onCreationStateChange?: (busy: boolean) => void;
  mapActionBusy?: boolean;
  mapActionCanManage?: boolean;
  mapActionCanRename?: boolean;
  mapActionCanDelete?: boolean;
  mapActionBlockedReason?: string;
  mapActionNotice?: MapDocumentActionNotice | null;
  onRenameMap?: (document: MapDocumentTab, name: string) => Promise<void>;
  onDeleteMap?: (document: MapDocumentTab) => Promise<void>;
  onReconcileMapAction?: () => Promise<void>;
  language: 'zh' | 'en';
}

export function MapDocumentTabs({ context, backend, documents, activeID, visualizationRevision, loading, interactionDisabled = false, loadError, onSelect, onCreated, onCreationStateChange, mapActionBusy = false, mapActionCanManage = false, mapActionCanRename, mapActionCanDelete, mapActionBlockedReason, mapActionNotice = null, onRenameMap, onDeleteMap, onReconcileMapAction, language }: MapDocumentTabsProps) {
  const copy = {
    zh: { local: '本地预览', noProject: '打开工程后可创建持久化地图文档', loading: '正在读取工程中已保存的地图…', loadError: '无法读取已保存地图' },
    en: { local: 'Local preview', noProject: 'Open a project to create a saved map document', loading: 'Reading saved project maps…', loadError: 'Could not read saved maps' },
  }[language];
  return (
    <div className="map-document-tabs">
      <div className="map-document-tab-list" role="tablist" aria-label={text(language, '地图文档', 'Map documents')}>
        <button
          type="button"
          className={'map-document-tab' + (activeID === null ? ' is-active' : '')}
          role="tab"
          aria-selected={activeID === null}
          disabled={loading || interactionDisabled}
          onClick={() => onSelect(null)}
        >
          <span className="map-tab-marker" aria-hidden="true" />
          {copy.local}
        </button>
        {documents.map((document) => (
          <button
            key={document.id}
            type="button"
            className={'map-document-tab' + (activeID === document.id ? ' is-active' : '')}
            data-testid="map-document-tab"
            data-map-index={document.mapIndex}
            data-map-name={document.name}
            data-map-type={document.mapType}
            role="tab"
            aria-selected={activeID === document.id}
            disabled={loading || interactionDisabled}
            onClick={() => onSelect(document.id)}
            title={mapDocumentKind(language, document.mapType) + ' · ' + document.name}
          >
            <span className="map-tab-marker" aria-hidden="true" />
            {document.name}
          </button>
        ))}
      </div>
      <MapDocumentActions document={documents.find(document => document.id === activeID) ?? null}
        documents={documents} canManage={mapActionCanManage && !interactionDisabled}
        canRename={mapActionCanRename && !interactionDisabled} canDelete={mapActionCanDelete && !interactionDisabled}
        blockedReason={mapActionBlockedReason} busy={mapActionBusy} notice={mapActionNotice}
        onRename={async name => {
          const document = documents.find(candidate => candidate.id === activeID);
          if (document) await onRenameMap?.(document, name);
        }}
        onDelete={async () => {
          const document = documents.find(candidate => candidate.id === activeID);
          if (document) await onDeleteMap?.(document);
        }}
        onReconcile={async () => { await onReconcileMapAction?.(); }} language={language} />
      <NewMapFlyout context={context} backend={backend} visualizationRevision={visualizationRevision}
        onCreationStateChange={onCreationStateChange}
        loading={loading || interactionDisabled} loadError={loadError} onCreated={onCreated} language={language} />
      {!context.projectPath && <span className="map-document-hint">{copy.noProject}</span>}
      {loading && <span className="map-document-hint" role="status">{copy.loading}</span>}
      {loadError != null && <div className="map-inline-error" role="alert">{copy.loadError}: {mapError(language, loadError)}</div>}
    </div>
  );
}
