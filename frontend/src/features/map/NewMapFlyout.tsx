import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { createMapDocument } from './ProjectTreeListener';
import { mapDocumentKind, mapError } from './mapText';
import type { MapDocumentTab } from './mapTypes';

type MapKind = 'Project' | 'Buildable' | 'Geometry' | 'Plan';

interface Props {
  backend: BackendGateway;
  context: WorkbenchContext;
  language: Language;
  visualizationRevision: string | null;
  loading: boolean;
  loadError: unknown;
  onCreated: (document: MapDocumentTab) => void;
  onCreationStateChange?: (busy: boolean) => void;
}

/** The source flyout's blank/project actions share the saved-map transaction. */
export function NewMapFlyout({ backend, context, language, visualizationRevision, loading, loadError, onCreated, onCreationStateChange }: Props) {
  const [kind, setKind] = useState<MapKind>('Project');
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml]);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const sequence = useRef(0);
  const busyRef = useRef(false);
  const menuRef = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    sequence.current++;
    busyRef.current = false;
    setCreating(false);
    setError('');
    if (menuRef.current) menuRef.current.open = false;
    return () => { sequence.current++; };
  }, [owner]);
  const disabled = !context.projectPath || creating || loading || loadError != null || !backend.supports('map.document.create');

  const create = async (requestedKind: MapKind) => {
    if (disabled || busyRef.current) return;
    busyRef.current = true;
    const capturedOwner = owner;
    const request = ++sequence.current;
    onCreationStateChange?.(true);
    setCreating(true);
    setError('');
    try {
      const document = await createMapDocument(backend, context, requestedKind, visualizationRevision);
      if (ownerRef.current !== capturedOwner || sequence.current !== request) return;
      onCreated(document);
      if (menuRef.current) menuRef.current.open = false;
    } catch (cause) {
      if (ownerRef.current === capturedOwner && sequence.current === request) setError(mapError(language, cause));
    } finally {
      onCreationStateChange?.(false);
      if (ownerRef.current === capturedOwner && sequence.current === request) {
        busyRef.current = false;
        setCreating(false);
      }
    }
  };

  return <div className="map-new-document-control">
    <div className="map-document-actions">
      <label className="map-type-select">
        <span className="sr-only">{text(language, '地图类型', 'Map type')}</span>
        <select value={kind} disabled={disabled} onChange={event => setKind(event.currentTarget.value as MapKind)}>
          {(['Project', 'Buildable', 'Geometry', 'Plan'] as const).map(value =>
            <option value={value} key={value}>{mapDocumentKind(language, value)}</option>)}
        </select>
      </label>
      <button type="button" className="map-create-document" disabled={disabled} onClick={() => void create(kind)}
        title={!context.projectPath ? text(language, '打开工程后可创建地图文档', 'Open a project to create a map') : undefined}>
        {creating ? text(language, '正在创建…', 'Creating…') : <><span aria-hidden="true">＋</span>{text(language, '新建地图', 'New map')}</>}
      </button>
      <details ref={menuRef} className="map-new-map-flyout">
        <summary aria-label={text(language, '快捷新建地图', 'Quick map creation')}>▾</summary>
        <div className="map-new-map-menu" role="group" aria-label={text(language, '快捷新建地图', 'Quick map creation')}>
          <button type="button" disabled={disabled} onClick={() => void create('Buildable')}>{text(language, '空白地图', 'Blank map')}</button>
          <button type="button" disabled={disabled} onClick={() => void create('Project')}>{text(language, '工程地图', 'Project map')}</button>
        </div>
      </details>
    </div>
    {error && <div className="map-inline-error" role="alert">{text(language, '创建地图失败：', 'Could not create the map: ')}{error}</div>}
  </div>;
}
