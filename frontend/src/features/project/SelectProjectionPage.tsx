import { useEffect, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { NewProjectDraft, ProjectionMatch } from './ProjectTypes';
import { getProjectionRequest } from './ProjectTypes';

interface SelectProjectionPageProps {
  language: Language;
  backend: BackendGateway;
  draft: NewProjectDraft;
  disabled: boolean;
  onDraftChange: (next: NewProjectDraft) => void;
}

export function SelectProjectionPage({ language, backend, draft, disabled, onDraftChange }: SelectProjectionPageProps) {
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<ProjectionMatch[]>([]);
  const [searchError, setSearchError] = useState<string>();
  const [searching, setSearching] = useState(false);
  const [prjName, setPrjName] = useState('');
  const searchGeneration = useRef(0);
  const searchAbort = useRef<AbortController | undefined>(undefined);
  const prjGeneration = useRef(0);
  const projectionTextRevision = useRef(0);
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const prjInput = useRef<HTMLInputElement>(null);
  const canSearch = backend.supports('projection.search');

  useEffect(() => () => {
    searchGeneration.current += 1;
    searchAbort.current?.abort();
    prjGeneration.current += 1;
  }, []);

  useEffect(() => {
    searchGeneration.current += 1;
    searchAbort.current?.abort();
    setSearching(false);
  }, [query]);

  const searchProjections = async (): Promise<void> => {
    if (!canSearch) return;
    const generation = ++searchGeneration.current;
    const controller = new AbortController();
    searchAbort.current?.abort();
    searchAbort.current = controller;
    setSearching(true);
    setSearchError(undefined);
    try {
      const result = await backend.request<ProjectionMatch[]>(
        'projection.search',
        { query: query.trim(), limit: 30 },
        { signal: controller.signal },
      );
      if (generation !== searchGeneration.current) return;
      setMatches(Array.isArray(result) ? result : []);
    } catch (error) {
      if (generation !== searchGeneration.current || controller.signal.aborted) return;
      setSearchError(message(error));
      setMatches([]);
    } finally {
      if (generation === searchGeneration.current) setSearching(false);
    }
  };

  const updateProjectionText = (next: NewProjectDraft): void => {
    projectionTextRevision.current += 1;
    onDraftChange(next);
  };

  const updateProjectionMode = (mode: NewProjectDraft['projectionMode']): void => {
    projectionTextRevision.current += 1;
    setPrjName('');
    onDraftChange({ ...draft, projectionMode: mode, projectionText: '' });
  };

  const readPrj = async (file: File | undefined): Promise<void> => {
    if (!file) return;
    const generation = ++prjGeneration.current;
    const modeAtStart = draft.projectionMode;
    const projectionTextAtStart = draft.projectionText;
    const textRevisionAtStart = projectionTextRevision.current;
    const stillSameProjectionDraft = (): boolean => generation === prjGeneration.current &&
      modeAtStart === 'prj' &&
      draftRef.current.projectionMode === modeAtStart &&
      draftRef.current.projectionText === projectionTextAtStart &&
      projectionTextRevision.current === textRevisionAtStart;
    try {
      const source = await file.text();
      if (!stillSameProjectionDraft()) return;
      setPrjName(file.name);
      projectionTextRevision.current += 1;
      onDraftChange({ ...draftRef.current, projectionText: source });
    } catch (error) {
      if (stillSameProjectionDraft()) setSearchError(message(error));
    }
  };

  const projectionRequest = getProjectionRequest(draft);
  const isLocal = draft.projectionMode === 'local';

  return (
    <section className="project-projection" aria-label={text(language, '投影设置', 'Projection settings')}>
      <div className="project-form-label">{text(language, '坐标参考', 'Coordinate reference')}</div>
      <div className="project-projection-modes" role="radiogroup" aria-label={text(language, '投影类型', 'Projection type')}>
        {([
          ['local', text(language, '本地', 'Local')],
          ['epsg', 'EPSG'],
          ['wkt', 'WKT / PROJ'],
          ['prj', '.prj'],
        ] as const).map(([mode, label]) => (
          <label className={`project-choice ${draft.projectionMode === mode ? 'is-selected' : ''}`} key={mode}>
            <input
              type="radio"
              name="project-projection-mode"
              value={mode}
              checked={draft.projectionMode === mode}
              disabled={disabled}
              onChange={() => updateProjectionMode(mode)}
            />
            <span>{label}</span>
          </label>
        ))}
      </div>

      {isLocal ? (
        <div className="project-unit-picker">
          <div className="project-form-label">{text(language, '单位制', 'Unit system')}</div>
          <div className="project-inline-choices">
            <label><input type="radio" name="project-units" checked={draft.unitSystem === 'USCustomary'} disabled={disabled} onChange={() => onDraftChange({ ...draft, unitSystem: 'USCustomary' })} /> {text(language, '美制单位', 'US Customary')}</label>
            <label><input type="radio" name="project-units" checked={draft.unitSystem === 'SI'} disabled={disabled} onChange={() => onDraftChange({ ...draft, unitSystem: 'SI' })} /> {text(language, '国际单位制（SI）', 'SI')}</label>
          </div>
          <p className="project-field-hint">{text(language, '本地坐标系使用所选单位制。', 'Local projection uses the selected unit system.')}</p>
        </div>
      ) : draft.projectionMode === 'epsg' ? (
        <div className="project-projection-content">
          <label className="project-form-label" htmlFor="project-epsg">{text(language, 'EPSG 代码', 'EPSG code')}</label>
          <div className="project-search-row">
            <input id="project-epsg" inputMode="numeric" value={draft.projectionText} disabled={disabled} placeholder="例如 26910" onChange={(event) => updateProjectionText({ ...draft, projectionText: event.currentTarget.value })} />
            <input aria-label={text(language, '搜索坐标系', 'Search coordinate systems')} value={query} disabled={disabled || !canSearch} placeholder={text(language, '搜索名称或代码', 'Search name or code')} onChange={(event) => setQuery(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); void searchProjections(); } }} />
            <button type="button" className="project-button project-button-secondary" disabled={disabled || !canSearch || searching} onClick={() => void searchProjections()}>{searching ? '…' : text(language, '搜索', 'Search')}</button>
          </div>
          {!canSearch && <p className="project-field-hint">{text(language, '投影目录搜索尚未接入。', 'Projection catalogue search is not connected.')}</p>}
          {searchError && <div className="project-inline-error" role="status">{searchError}</div>}
          {matches.length > 0 && (
            <ul className="project-projection-results">
              {matches.map((match) => (
                <li key={match.id}>
                  <button type="button" disabled={disabled} onClick={() => { updateProjectionText({ ...draft, projectionText: String(match.code) }); setMatches([]); }}>
                    <strong>{match.authority}:{match.code}</strong><span>{match.name}</span>{match.area && <small>{match.area}</small>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : (
        <div className="project-projection-content">
          <div className="project-form-label">{draft.projectionMode === 'prj' ? text(language, 'ESRI .prj 定义', 'ESRI .prj definition') : text(language, 'WKT 或 PROJ 字符串', 'WKT or PROJ string')}</div>
          {draft.projectionMode === 'prj' && (
            <div className="project-prj-picker">
              <button type="button" className="project-button project-button-secondary" disabled={disabled} onClick={() => prjInput.current?.click()}>{text(language, '读取 .prj 文件', 'Read .prj file')}</button>
              {prjName && <span>{prjName}</span>}
              <input ref={prjInput} type="file" accept=".prj,text/plain" hidden onChange={(event) => { void readPrj(event.currentTarget.files?.[0]); event.currentTarget.value = ''; }} />
            </div>
          )}
          <textarea value={draft.projectionText} disabled={disabled} rows={draft.projectionMode === 'prj' ? 4 : 6} placeholder={draft.projectionMode === 'prj' ? text(language, 'ESRI 坐标系文本', 'ESRI coordinate system text') : text(language, 'WKT[…] 或 +proj=…', 'WKT[…] or +proj=…')} onChange={(event) => updateProjectionText({ ...draft, projectionText: event.currentTarget.value })} />
          <p className="project-field-hint">{text(language, '投影文本由 C# 后端解释和验证；此处不自行推断坐标系。', 'The C# backend interprets and validates this definition; the UI does not infer a coordinate system.')}</p>
        </div>
      )}
      {!isLocal && !projectionRequest.value.trim() && <div className="project-inline-warning">{text(language, '投影定义不能为空。', 'A projection definition is required.')}</div>}
    </section>
  );
}
