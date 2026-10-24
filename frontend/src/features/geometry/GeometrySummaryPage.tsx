import type { AssetReference, Language } from '../../shared/workbench';
import { gdiagnostic, gkind, gt } from './geometryText';
import type { GeometryCulvertSnapshot, GeometryMeshSnapshot, GeometryParentSnapshot, GeometryTopologySnapshot } from './geometryTypes';

export interface GeometrySummaryPageProps {
  language: Language;
  asset: AssetReference | null;
  projectPath: string | null;
  sessionID: string;
  topology?: GeometryTopologySnapshot;
  mesh?: GeometryMeshSnapshot;
  culverts?: GeometryCulvertSnapshot;
  parent?: GeometryParentSnapshot;
  loading: boolean;
}

function countRows(snapshot: GeometryTopologySnapshot | undefined, key: 'nodes' | 'arcs' | 'regions'): string {
  return snapshot ? String(snapshot[key].rows.length) : '—';
}

export function GeometrySummaryPage({ language, asset, projectPath, sessionID, topology, mesh, culverts, parent, loading }: GeometrySummaryPageProps) {
  if (!asset) return <div className="geometry-panel geometry-asset-missing"><div className="geometry-empty"><span className="geometry-empty-mark">⌖</span><strong>{gt(language, 'noData')}</strong><p>{gt(language, 'noAsset')}</p></div></div>;
  const descriptionID = Object.keys(parent?.fields ?? {}).find((key) => key.toLocaleLowerCase().endsWith('description'));
  const description = descriptionID ? parent?.fields[descriptionID] : undefined;
  const imageCount = Object.values(parent?.images ?? {}).reduce((sum, images) => sum + images.length, 0);
  const stats = [
    [gt(language, 'nodesCount'), countRows(topology, 'nodes')],
    [gt(language, 'arcsCount'), countRows(topology, 'arcs')],
    [gt(language, 'regionsCount'), countRows(topology, 'regions')],
    [gt(language, 'flowAreaCount'), mesh ? String(mesh.flowAreas.length) : '—'],
    [gt(language, 'barrelCount'), culverts ? String(culverts.barrels.rows.length) : '—'],
  ];
  return (
    <div className="geometry-grid">
      <div className="geometry-summary-stats">
        {stats.map(([label, value]) => <div className="geometry-stat" key={label}><span className="geometry-stat-label">{label}</span><strong className="geometry-stat-value">{value}</strong></div>)}
      </div>
      <div className="geometry-grid two">
        <section className="geometry-panel">
          <header className="geometry-panel-heading"><div><h2>{gt(language, 'overview')}</h2><p>{gt(language, 'sourceBacked')}</p></div>{loading && <span className="geometry-loading">{gt(language, 'loading')}</span>}</header>
          <div className="geometry-panel-body">
            <div className="geometry-kv-grid">
              <div className="geometry-kv"><span>{gt(language, 'name')}</span><strong>{asset.name}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'layer')}</span><strong>{gkind(language, asset.kind)}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'file')}</span><strong title={asset.path}>{asset.path}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'revision')}</span><strong>{topology?.revision ?? asset.revision ?? '—'}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'project')}</span><strong>{projectPath ?? '—'}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'session')}</span><strong>{sessionID || '—'}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'source')}</span><strong>{parent ? gt(language, 'sourceBacked') : gt(language, 'localDraft')}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'description')}</span><strong>{description || '—'}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'embeddedImages')}</span><strong>{imageCount}</strong></div>
              <div className="geometry-kv"><span>{gt(language, 'status')}</span><strong>{parent?.canEdit ? gt(language, 'clean') : '—'}</strong></div>
            </div>
          </div>
        </section>
        <section className="geometry-panel">
          <header className="geometry-panel-heading"><div><h2>{gt(language, 'topologySummary')}</h2><p>{gt(language, 'completion')}</p></div><span className={`geometry-status-pill ${topology?.completion?.isComplete ? 'is-clean' : ''}`}>{topology?.completion ? (topology.completion.isComplete ? gt(language, 'complete') : gt(language, 'incomplete')) : '—'}</span></header>
          <div className="geometry-panel-body">
            {topology?.completion?.messages?.length ? <div className="geometry-report-list">{topology.completion.messages.map((item) => <details className={`geometry-report-item ${item.severity.toLowerCase() === 'error' ? 'is-error' : item.severity.toLowerCase() === 'warning' ? 'is-warning' : ''}`} key={item.id}><summary>{gt(language, 'diagnosticDetails')}</summary><pre>{item.code} · {gdiagnostic(language, item.text)}</pre></details>)}</div> : <div className="geometry-empty"><span className="geometry-empty-mark">⌘</span><strong>{topology ? gt(language, topology.completion?.isComplete ? 'complete' : 'noData') : gt(language, 'loading')}</strong><p>{topology?.completion ? gt(language, 'validationNote') : gt(language, 'emptyHint')}</p></div>}
          </div>
        </section>
      </div>
    </div>
  );
}

export default GeometrySummaryPage;
