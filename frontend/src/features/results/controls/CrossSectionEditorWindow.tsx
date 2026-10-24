import { useCallback, useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../../shared/workbench';
import type { ResultInspectorSnapshot } from './resultDetailsTypes';
import { CrossSectionEditorControl } from './CrossSectionEditorControl';
import type { ClassicCrossSectionCatalog, ClassicCrossSectionProfile, ClassicCrossSectionRow } from './classicCrossSectionTypes';

interface CrossSectionEditorWindowProps {
  backend: BackendGateway;
  backendRevision?: number;
  context: WorkbenchContext;
  snapshot: ResultInspectorSnapshot;
  language: Language;
}

interface RequestState {
  controller: AbortController;
  generation: number;
}

interface ViewState {
  owner: string;
  catalog: ClassicCrossSectionCatalog | null;
  selectedRow: ClassicCrossSectionRow | null;
  profile: ClassicCrossSectionProfile | null;
  catalogLoading: boolean;
  profileLoading: boolean;
  error: string | null;
  profileError: string | null;
}

function validRow(value: unknown, offset: number, end: number): value is ClassicCrossSectionRow {
  if (!value || typeof value !== 'object') return false;
  const row = value as Record<string, unknown>;
  return Number.isInteger(row.ordinal) && Number(row.ordinal) >= offset && Number(row.ordinal) < end
    && typeof row.rowToken === 'string' && row.rowToken.length > 0
    && typeof row.nodeName === 'string' && typeof row.riverName === 'string'
    && typeof row.reachName === 'string' && typeof row.riverStation === 'string'
    && Number.isInteger(row.profilePointCount) && Number(row.profilePointCount) >= 0
    && typeof row.usesOverride === 'boolean';
}

function validPoints(value: unknown): value is Array<{ ordinal: number; station: number | null; elevation: number | null }> {
  return Array.isArray(value) && value.every((point, index) => point && Number.isInteger(point.ordinal) && point.ordinal === index
    && (point.station === null || (typeof point.station === 'number' && Number.isFinite(point.station)))
    && (point.elevation === null || (typeof point.elevation === 'number' && Number.isFinite(point.elevation))));
}

function isSha(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f\d]{64}$/i.test(value);
}

function comparablePath(value: string): string {
  const normalized = value.trim().replaceAll('\\', '/').replace(/\/+$/, '') || '/';
  return /^[a-z]:\//i.test(normalized) ? normalized.toLowerCase() : normalized;
}

function normalizeScope(value: string): string | null {
  const normalized = value.trim().replace(/[{}]/g, '').toUpperCase();
  return /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/.test(normalized) ? normalized : null;
}

function emptyState(owner: string): ViewState {
  return { owner, catalog: null, selectedRow: null, profile: null, catalogLoading: false, profileLoading: false, error: null, profileError: null };
}

export function CrossSectionEditorWindow({ backend, backendRevision, context, snapshot, language }: CrossSectionEditorWindowProps) {
  const zh = language === 'zh';
  const sourcePath = snapshot.geometry?.filename?.trim() ?? '';
  const embedded = snapshot.geometry?.embeddedInResult === true;
  const owner = JSON.stringify([context.projectSessionID, context.projectPath, context.projectRevision, context.projectXml, snapshot.resultPath, snapshot.revision, sourcePath, embedded, backendRevision ?? null]);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const catalogRequestRef = useRef<RequestState | null>(null);
  const profileRequestRef = useRef<RequestState | null>(null);
  const catalogGenerationRef = useRef(0);
  const profileGenerationRef = useRef(0);
  const [view, setView] = useState<ViewState>(() => emptyState(owner));
  const currentView = view.owner === owner ? view : emptyState(owner);
  const scopeID = normalizeScope(context.projectSessionID);
  const absolutePath = /^(?:[a-z]:[\\/]|\/)/i.test(sourcePath);

  const loadCatalog = useCallback(async (offset: number): Promise<void> => {
    catalogRequestRef.current?.controller.abort();
    profileRequestRef.current?.controller.abort();
    profileRequestRef.current = null;
    const controller = new AbortController();
    const generation = ++catalogGenerationRef.current;
    const pending = { controller, generation };
    catalogRequestRef.current = pending;
    const isCurrent = (): boolean => ownerRef.current === owner && !controller.signal.aborted
      && catalogGenerationRef.current === generation && catalogRequestRef.current === pending;
    setView({ ...emptyState(owner), catalogLoading: true });
    if (!backend.supports('classic.crossSections.catalog')) {
      setView({ ...emptyState(owner), error: zh ? '当前 C# Host 未发布经典断面目录读取接口。' : 'The C# Host does not advertise the classic cross-section catalog.' });
      catalogRequestRef.current = null;
      return;
    }
    if (!scopeID || !isSha(snapshot.revision) || !sourcePath || !absolutePath || embedded) {
      const reason = embedded
        ? (zh ? '几何源嵌入在 Result 内；当前只读接口只读取独立的 ClassicGeometry 文件。' : 'The geometry source is embedded in the Result; this read-only endpoint accepts standalone ClassicGeometry files only.')
        : (zh ? '当前 Result 没有可验证的独立几何文件路径或有效会话 / SHA。' : 'The Result has no verifiable standalone geometry path or valid session / SHA.');
      setView({ ...emptyState(owner), error: reason });
      catalogRequestRef.current = null;
      return;
    }
    try {
      const response = await backend.request<ClassicCrossSectionCatalog>('classic.crossSections.catalog', {
        scopeID,
        sourcePath,
        offset,
        limit: 128,
      }, { signal: controller.signal });
      if (!isCurrent()) return;
      if (response.scopeID !== scopeID || comparablePath(response.sourcePath) !== comparablePath(sourcePath)
        || !isSha(response.sourceRevision) || typeof response.contextToken !== 'string' || !response.contextToken
        || !Array.isArray(response.rows) || !Number.isInteger(response.totalCount) || response.totalCount < 0
        || response.offset !== offset || typeof response.sourceName !== 'string'
        || response.rows.some((row, index) => !validRow(row, offset, offset + response.rows.length) || row.ordinal !== offset + index)
        || (response.nextOffset !== null && (!Number.isInteger(response.nextOffset) || response.nextOffset !== offset + response.rows.length))
        || (response.nextOffset === null && offset + response.rows.length < response.totalCount)
        || (response.nextOffset !== null && offset + response.rows.length >= response.totalCount)) {
        throw new Error(zh ? '断面目录返回的会话、源文件修订或分页数据与请求不一致。' : 'The cross-section catalog identity or page does not match the request.');
      }
      if (new Set(response.rows.map((row) => row.ordinal)).size !== response.rows.length
        || new Set(response.rows.map((row) => row.rowToken)).size !== response.rows.length) {
        throw new Error(zh ? '断面目录中存在重复行身份。' : 'The cross-section catalog contains duplicate row identities.');
      }
      setView({ ...emptyState(owner), catalog: response });
    } catch (error) {
      if (isCurrent()) setView({ ...emptyState(owner), error: error instanceof Error ? error.message : String(error) });
    } finally {
      if (catalogRequestRef.current === pending) catalogRequestRef.current = null;
    }
  }, [absolutePath, backend, embedded, owner, scopeID, snapshot.revision, sourcePath, zh]);

  useEffect(() => {
    setView(emptyState(owner));
    void loadCatalog(0);
    return () => {
      const catalog = catalogRequestRef.current;
      if (catalog) { catalog.controller.abort(); catalogRequestRef.current = null; }
      const profile = profileRequestRef.current;
      if (profile) { profile.controller.abort(); profileRequestRef.current = null; }
      catalogGenerationRef.current += 1;
      profileGenerationRef.current += 1;
    };
  }, [loadCatalog, owner]);

  const selectRow = async (row: ClassicCrossSectionRow): Promise<void> => {
    const catalog = currentView.catalog;
    if (!catalog || !scopeID || !isSha(catalog.sourceRevision) || !catalog.contextToken
      || !backend.supports('classic.crossSections.profile')) return;
    profileRequestRef.current?.controller.abort();
    const controller = new AbortController();
    const generation = ++profileGenerationRef.current;
    const pending = { controller, generation };
    profileRequestRef.current = pending;
    const requestOwner = owner;
    const isCurrent = (): boolean => ownerRef.current === requestOwner && !controller.signal.aborted
      && profileGenerationRef.current === generation && profileRequestRef.current === pending;
    setView((current) => current.owner === owner ? { ...current, selectedRow: row, profile: null, profileLoading: true, profileError: null } : current);
    try {
      const profile = await backend.request<ClassicCrossSectionProfile>('classic.crossSections.profile', {
        scopeID,
        sourcePath: catalog.sourcePath,
        expectedRevision: catalog.sourceRevision,
        contextToken: catalog.contextToken,
        rowOrdinal: row.ordinal,
        rowToken: row.rowToken,
      }, { signal: controller.signal });
      if (!isCurrent()) return;
      if (profile.scopeID !== scopeID || comparablePath(profile.sourcePath) !== comparablePath(catalog.sourcePath)
        || !isSha(profile.sourceRevision) || profile.sourceRevision.toUpperCase() !== catalog.sourceRevision.toUpperCase()
        || profile.contextToken !== catalog.contextToken || profile.row.ordinal !== row.ordinal
        || profile.row.rowToken !== row.rowToken || !validRow(profile.row, row.ordinal, row.ordinal + 1)
        || typeof profile.hasProfile !== 'boolean'
        || !validPoints(profile.points) || !validPoints(profile.banks)) {
        throw new Error(zh ? '断面剖面与已选源文件、目录修订或行身份不一致。' : 'The profile does not match the selected source, catalog revision, or row identity.');
      }
      setView((current) => current.owner === owner ? { ...current, profile, profileLoading: false } : current);
    } catch (error) {
      if (isCurrent()) setView((current) => current.owner === owner ? { ...current, profileLoading: false, profileError: error instanceof Error ? error.message : String(error) } : current);
    } finally {
      if (profileRequestRef.current === pending) profileRequestRef.current = null;
    }
  };

  return <section className="results-insight-panel results-cross-section-window" data-testid="classic-cross-section-window">
    <div className="results-insight-heading">
      <div><span className="results-section-index">03</span><div><h2>{zh ? '经典几何断面' : 'Classic geometry cross sections'}<span className="results-readonly-chip">{zh ? '只读' : 'Read only'}</span></h2><p>{zh ? '读取所选 Result 关联的独立经典几何文件；剖面与岸线采样点来自计算断面数据。' : 'Reads the standalone classic geometry file associated with the selected Result; the profile and bank samples come from computation cross-section data.'}</p></div></div>
      {currentView.catalog && <span className="results-insight-count">{currentView.catalog.totalCount.toLocaleString(zh ? 'zh-CN' : 'en-US')} {zh ? '个断面' : 'sections'}</span>}
    </div>
    {currentView.error && <p className="results-insight-state" role="status">{currentView.error}</p>}
    {currentView.catalogLoading && <p className="results-insight-state" role="status">{zh ? '正在读取经典断面目录…' : 'Reading the classic cross-section catalog…'}</p>}
    {currentView.catalog && <div className="results-cross-section-layout">
      <div className="results-cross-section-source-list" role="listbox" aria-label={zh ? '经典几何断面目录' : 'Classic geometry cross-section catalog'}>
        <div className="results-cross-section-source-meta"><strong>{currentView.catalog.sourceName}</strong><code>{currentView.catalog.sourceRevision.slice(0, 12)}…</code></div>
        {currentView.catalog.rows.length === 0 && <p className="results-insight-state">{zh ? '此页没有断面记录。' : 'This page has no cross-section rows.'}</p>}
        {currentView.catalog.rows.map((row) => <button type="button" role="option" aria-selected={currentView.selectedRow?.rowToken === row.rowToken} key={row.rowToken} onClick={() => void selectRow(row)} disabled={currentView.profileLoading && currentView.selectedRow?.rowToken === row.rowToken}>
          <strong>{row.nodeName || row.riverStation || `${zh ? '断面' : 'Section'} ${row.ordinal + 1}`}</strong>
          <span>{[row.riverName, row.reachName, row.riverStation].filter(Boolean).join(' · ') || (zh ? '未提供河段标识' : 'No reach metadata')}</span>
          <small>{row.profilePointCount.toLocaleString(zh ? 'zh-CN' : 'en-US')} {zh ? '个计算点' : 'computation points'}{row.usesOverride ? ` · ${zh ? '覆盖剖面' : 'override profile'}` : ''}</small>
        </button>)}
        <div className="results-cross-section-page">
          <button type="button" disabled={currentView.catalog.offset <= 0 || currentView.catalogLoading} onClick={() => void loadCatalog(Math.max(0, currentView.catalog!.offset - 128))}>{zh ? '上一页' : 'Previous'}</button>
          <span>{currentView.catalog.offset + 1}–{Math.min(currentView.catalog.offset + currentView.catalog.rows.length, currentView.catalog.totalCount)} / {currentView.catalog.totalCount}</span>
          <button type="button" disabled={currentView.catalog.nextOffset === null || currentView.catalogLoading} onClick={() => void loadCatalog(currentView.catalog!.nextOffset!)}>{zh ? '下一页' : 'Next'}</button>
        </div>
      </div>
      <div className="results-cross-section-detail">
        {currentView.selectedRow && <div className="results-cross-section-selected"><strong>{currentView.selectedRow.nodeName || `${zh ? '断面' : 'Section'} ${currentView.selectedRow.ordinal + 1}`}</strong><span>{[currentView.selectedRow.riverName, currentView.selectedRow.reachName, currentView.selectedRow.riverStation].filter(Boolean).join(' · ')}</span></div>}
        {currentView.profileLoading && <p className="results-insight-state" role="status">{zh ? '正在读取选中断面的计算剖面…' : 'Reading the selected computation profile…'}</p>}
        {currentView.profileError && <p className="results-inline-error" role="alert">{currentView.profileError}</p>}
        {currentView.profile && <CrossSectionEditorControl profile={currentView.profile} language={language} />}
        {!currentView.profileLoading && !currentView.profile && !currentView.profileError && <p className="results-insight-state">{zh ? '从左侧目录选择断面以读取真实站距—高程剖面。' : 'Select a section from the catalog to read its actual station-elevation profile.'}</p>}
        {!backend.supports('classic.crossSections.profile') && <p className="results-insight-state">{zh ? '当前 C# Host 未发布断面剖面读取接口。' : 'The C# Host does not advertise cross-section profile reading.'}</p>}
      </div>
    </div>}
  </section>;
}
