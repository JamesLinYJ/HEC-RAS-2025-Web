import { useRef, useState } from 'react';
import { text } from '../../shared/workbench';
import type { BackendGateway, WorkbenchContext } from '../../shared/workbench';
import { createMapDocument } from './ProjectTreeListener';
import type { MapDocumentTab } from './mapTypes';
import { mapDocumentKind, mapError } from './mapText';

interface MapDocumentTabsProps {
  context: WorkbenchContext;
  backend: BackendGateway;
  documents: MapDocumentTab[];
  activeID: string | null;
  visualizationRevision: string | null;
  loading: boolean;
  loadError: unknown;
  onSelect: (id: string | null) => void;
  onCreated: (document: MapDocumentTab) => void;
  language: 'zh' | 'en';
}

type DocumentKind = 'Project' | 'Buildable' | 'Geometry' | 'Plan';

export function MapDocumentTabs({ context, backend, documents, activeID, visualizationRevision, loading, loadError, onSelect, onCreated, language }: MapDocumentTabsProps) {
  const [kind, setKind] = useState<DocumentKind>('Project');
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const contextKey = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
  const contextKeyRef = useRef(contextKey);
  contextKeyRef.current = contextKey;
  const copy = {
    zh: { local: '本地预览', add: '新建地图', adding: '正在创建…', noProject: '打开工程后可创建持久化地图文档', type: '地图类型', error: '创建地图失败', loading: '正在读取工程中已保存的地图…', loadError: '无法读取已保存地图' },
    en: { local: 'Local preview', add: 'New map', adding: 'Creating…', noProject: 'Open a project to create a saved map document', type: 'Map type', error: 'Could not create the map', loading: 'Reading saved project maps…', loadError: 'Could not read saved maps' },
  }[language];
  const create = async () => {
    setCreating(true);
    setError('');
    const requestContextKey = contextKey;
    try {
      const document = await createMapDocument(backend, context, kind, visualizationRevision);
      if (contextKeyRef.current !== requestContextKey) return;
      onCreated(document);
    } catch (cause) {
      if (contextKeyRef.current === requestContextKey) setError(mapError(language, cause));
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="map-document-tabs">
      <div className="map-document-tab-list" role="tablist" aria-label={text(language, '地图文档', 'Map documents')}>
        <button
          type="button"
          className={'map-document-tab' + (activeID === null ? ' is-active' : '')}
          role="tab"
          aria-selected={activeID === null}
          disabled={loading}
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
            role="tab"
            aria-selected={activeID === document.id}
            disabled={loading}
            onClick={() => onSelect(document.id)}
            title={mapDocumentKind(language, document.mapType) + ' · ' + document.name}
          >
            <span className="map-tab-marker" aria-hidden="true" />
            {document.name}
          </button>
        ))}
      </div>
      <div className="map-document-actions">
        <label className="map-type-select">
          <span className="sr-only">{copy.type}</span>
          <select value={kind} onChange={(event) => setKind(event.currentTarget.value as DocumentKind)} disabled={!context.projectPath || creating || loading || Boolean(loadError)}>
            {(['Project', 'Buildable', 'Geometry', 'Plan'] as const).map((value) => <option value={value} key={value}>{mapDocumentKind(language, value)}</option>)}
          </select>
        </label>
        <button type="button" className="map-create-document" onClick={() => void create()} disabled={!context.projectPath || creating || loading || Boolean(loadError) || !backend.supports('map.document.create')} title={!context.projectPath ? copy.noProject : loadError ? copy.loadError : copy.add}>
          {creating ? copy.adding : <><span aria-hidden="true">＋</span>{copy.add}</>}
        </button>
      </div>
      {!context.projectPath && <span className="map-document-hint">{copy.noProject}</span>}
      {loading && <span className="map-document-hint" role="status">{copy.loading}</span>}
      {loadError != null && <div className="map-inline-error" role="alert">{copy.loadError}: {mapError(language, loadError)}</div>}
      {error && <div className="map-inline-error" role="alert">{copy.error}: {error}</div>}
    </div>
  );
}
