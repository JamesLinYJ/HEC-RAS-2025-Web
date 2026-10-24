import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BackendGateway, Language, WorkbenchContext } from '../../shared/workbench';
import { errorDiagnostics, message, text } from '../../shared/workbench';
import { isAbsoluteServerPath } from './ProjectTypes';
import { OpeningProjectPage } from './OpeningProjectPage';
import { ExampleProjectSummaryPage } from './ExampleProjectSummaryPage';
import { SearchBarWithResultsPopup } from './SearchBarWithResultsPopup';

interface ProjectLibraryRow {
  id: string;
  title: string;
  description: string;
  publishDate: string | null;
  tags: string[];
  thumbnailUrl: string | null;
  kind: 'example' | 'synthetic';
}

interface ProjectLibraryCatalogResponse {
  examples: ProjectLibraryRow[];
  synthetic: ProjectLibraryRow[];
}

interface ProjectLibraryOpenResponse {
  projectPath: string;
}

interface ExampleAndSyntheticDatasetsPageProps {
  language: Language;
  backend: BackendGateway;
  context: WorkbenchContext;
  backendRevision?: number;
  busy: boolean;
  importBlockedReason?: string;
  onBack: () => void;
  onBusyChange: (busy: boolean) => void;
  onOpenProjectPath: (projectPath: string, source: WorkbenchContext) => Promise<boolean>;
}

function isProjectLibraryRow(value: unknown): value is ProjectLibraryRow {
  if (!value || typeof value !== 'object') return false;
  const row = value as Partial<ProjectLibraryRow>;
  return typeof row.id === 'string' && typeof row.title === 'string' &&
    typeof row.description === 'string' && Array.isArray(row.tags) &&
    row.tags.every((tag) => typeof tag === 'string') &&
    (row.publishDate === null || typeof row.publishDate === 'string') &&
    (row.thumbnailUrl === null || typeof row.thumbnailUrl === 'string') &&
    (row.kind === 'example' || row.kind === 'synthetic');
}

function safeThumbnail(url: string | null): string | undefined {
  if (!url) return undefined;
  try { return new URL(url).protocol === 'https:' ? url : undefined; }
  catch { return undefined; }
}

function rawErrorDetails(error: unknown): string {
  const details = errorDiagnostics(error);
  if (details instanceof Error) return `${details.name}: ${details.message}${details.stack ? `\n${details.stack}` : ''}`;
  try { return JSON.stringify(details, null, 2) ?? String(details); }
  catch { return String(details); }
}

function userErrorMessage(error: unknown, language: Language, fallback: string): string {
  if (error && typeof error === 'object' && typeof (error as { localizedMessage?: unknown }).localizedMessage === 'function') {
    return message(error, language);
  }
  return fallback;
}

export function ExampleAndSyntheticDatasetsPage({ language, backend, context, backendRevision, busy, importBlockedReason, onBack, onBusyChange, onOpenProjectPath }: ExampleAndSyntheticDatasetsPageProps) {
  const [catalog, setCatalog] = useState<ProjectLibraryCatalogResponse | null>(null);
  const [catalogBusy, setCatalogBusy] = useState(false);
  const [catalogError, setCatalogError] = useState<{ message: string; details: string } | null>(null);
  const [openError, setOpenError] = useState<{ message: string; details: string; detailsLabel?: string } | null>(null);
  const [destinationDirectory, setDestinationDirectory] = useState('');
  const [query, setQuery] = useState('');
  const [selectedExample, setSelectedExample] = useState<ProjectLibraryRow | null>(null);
  const [opening, setOpening] = useState(false);
  const [progress, setProgress] = useState<number>();
  const catalogGeneration = useRef(0);
  const catalogAbort = useRef<AbortController | undefined>(undefined);
  const openingAbort = useRef<AbortController | undefined>(undefined);
  const openingGeneration = useRef(0);

  const canCatalog = backend.supports('projectLibrary.catalog');
  const canLibraryOpen = backend.supports('projectLibrary.open');
  const canOpenProject = backend.supports('project.open');
  const canOpenSelection = canLibraryOpen && canOpenProject && isAbsoluteServerPath(destinationDirectory.trim()) && !busy && !opening && !importBlockedReason;
  const visibleExamples = useMemo(() => {
    const rows = catalog?.examples ?? [];
    const filter = query.trim().toLocaleLowerCase(language === 'zh' ? 'zh-CN' : 'en-US');
    if (!filter) return rows;
    return rows.filter((row) => [row.title, row.description, ...row.tags].join('\n').toLocaleLowerCase(language === 'zh' ? 'zh-CN' : 'en-US').includes(filter));
  }, [catalog?.examples, language, query]);
  const searchResults = useMemo(() => visibleExamples.map((row) => ({
    id: row.id,
    title: row.title,
    description: row.description,
    detail: row.tags.join(' · '),
  })), [visibleExamples]);

  const loadCatalog = useCallback(async (): Promise<void> => {
    if (!backend.supports('projectLibrary.catalog')) {
      setCatalog(null);
      setCatalogError({ message: text(language, '当前 Host 未发布 projectLibrary.catalog。', 'The connected Host has not advertised projectLibrary.catalog.'), details: 'projectLibrary.catalog' });
      return;
    }
    const generation = ++catalogGeneration.current;
    const controller = new AbortController();
    catalogAbort.current?.abort();
    catalogAbort.current = controller;
    setCatalogBusy(true);
    setCatalogError(null);
    try {
      const response = await backend.request<ProjectLibraryCatalogResponse>('projectLibrary.catalog', { debugMode: false }, { signal: controller.signal });
      if (generation !== catalogGeneration.current || controller.signal.aborted) return;
      if (!response || !Array.isArray(response.examples) || !response.examples.every(isProjectLibraryRow) ||
          !Array.isArray(response.synthetic) || !response.synthetic.every(isProjectLibraryRow)) {
        throw new Error(text(language, 'Host 返回的工程库目录格式无效。', 'The Host returned an invalid project-library catalog.'));
      }
      setCatalog({ ...response, examples: response.examples.filter((row) => row.kind === 'example'), synthetic: [] });
    } catch (error) {
      if (generation === catalogGeneration.current && !controller.signal.aborted) {
        setCatalog(null);
        setCatalogError({
          message: userErrorMessage(error, language, text(language, '读取 Host 示例目录失败。展开查看原始诊断。', 'Could not load the Host example catalog. Expand the raw diagnostics for details.')),
          details: rawErrorDetails(error),
        });
      }
    } finally {
      if (generation === catalogGeneration.current && !controller.signal.aborted) setCatalogBusy(false);
    }
  }, [backend, language]);

  useEffect(() => {
    void loadCatalog();
    return () => {
      catalogGeneration.current += 1;
      catalogAbort.current?.abort();
      openingGeneration.current += 1;
      openingAbort.current?.abort();
      onBusyChange(false);
    };
  }, [loadCatalog, onBusyChange, backendRevision]);

  const openExample = async (project: ProjectLibraryRow): Promise<void> => {
    if (!canOpenSelection) return;
    const generation = ++openingGeneration.current;
    const controller = new AbortController();
    openingAbort.current?.abort();
    openingAbort.current = controller;
    const source = context;
    setOpening(true);
    setOpenError(null);
    setProgress(undefined);
    onBusyChange(true);
    try {
      const response = await backend.request<ProjectLibraryOpenResponse>('projectLibrary.open', {
        projectId: project.id,
        synthetic: false,
        debugMode: false,
        destinationDirectory: destinationDirectory.trim(),
      }, {
        signal: controller.signal,
        onProgress: (raw) => {
          if (!raw || typeof raw !== 'object') return;
          const value = (raw as { fraction?: unknown; Fraction?: unknown }).fraction ?? (raw as { Fraction?: unknown }).Fraction;
          if (typeof value === 'number' && Number.isFinite(value)) setProgress(value);
        },
      });
      if (generation !== openingGeneration.current || controller.signal.aborted) return;
      if (!response || typeof response.projectPath !== 'string' || !isAbsoluteServerPath(response.projectPath)) {
        throw new Error(text(language, 'Host 没有返回可打开的绝对工程路径。', 'The Host did not return an absolute project path that can be opened.'));
      }
      const opened = await onOpenProjectPath(response.projectPath, source);
      if (generation !== openingGeneration.current || controller.signal.aborted) return;
      if (!opened) setOpenError({
        message: text(language, '示例文件已由 Host 准备，但没有切换为当前工程。请检查工程状态或原始诊断。', 'The Host prepared the example files, but they did not become the active project. Check the project state or raw diagnostics.'),
        details: response.projectPath,
        detailsLabel: text(language, 'Host 返回的工程路径', 'Project path returned by Host'),
      });
    } catch (error) {
      if (generation === openingGeneration.current && !controller.signal.aborted) {
        setOpenError({
          message: userErrorMessage(error, language, text(language, 'Host 未能准备示例工程。展开查看原始诊断。', 'The Host could not prepare the example project. Expand the raw diagnostics for details.')),
          details: rawErrorDetails(error),
        });
      }
    } finally {
      if (generation === openingGeneration.current && !controller.signal.aborted) {
        setOpening(false);
        setProgress(undefined);
        onBusyChange(false);
      }
    }
  };

  if (selectedExample) {
    return <section className="project-library-page">
      <ExampleProjectSummaryPage
        language={language}
        definition={selectedExample}
        busy={busy || opening} canOpen={canOpenSelection}
        onBack={() => setSelectedExample(null)}
        onOpen={() => void openExample(selectedExample)}
      />
      {!canOpenSelection && <p className="project-field-hint">{importBlockedReason ?? (!isAbsoluteServerPath(destinationDirectory.trim())
        ? text(language, '请返回列表并填写服务器目标目录后再打开。', 'Return to the list and enter a server destination directory before opening.')
        : text(language, '当前服务暂不能打开此示例工程。', 'The connected service cannot open this example project right now.'))}</p>}
      {opening && <OpeningProjectPage language={language} fraction={progress} />}
      {openError && <div className="project-library-error" role="alert"><strong>{text(language, '示例工程未打开', 'Example project was not opened')}</strong><p>{openError.message}</p><details><summary>{openError.detailsLabel ?? text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{openError.details}</pre></details></div>}
    </section>;
  }

  return (
    <section className="project-library-page">
      <header className="project-library-heading">
        <div>
          <div className="project-kicker">{text(language, '工程管理 / 工程库', 'PROJECT EXPLORER / PROJECT LIBRARY')}</div>
          <h2>{text(language, '示例工程', 'Example projects')}</h2>
          <p>{text(language, '目录内容来自当前 C# Host 的 projectLibrary.catalog；打开后由 Host 校验下载并返回服务器工程路径。', 'Catalog entries come from projectLibrary.catalog on the connected C# Host. The Host validates the download and returns the server project path when opened.')}</p>
        </div>
        <button type="button" className="project-button project-button-secondary" disabled={opening || busy} onClick={onBack}>{text(language, '返回工程入口', 'Back to project start')}</button>
      </header>

      {!canCatalog ? <div className="project-library-empty">{text(language, '当前后端未提供 projectLibrary.catalog。', 'The connected backend does not provide projectLibrary.catalog.')}</div> : <>
        <div className="project-library-controls">
          <SearchBarWithResultsPopup
            language={language}
            query={query}
            results={searchResults}
            disabled={catalogBusy || opening || busy}
            onQueryChange={setQuery}
            onSelect={(result) => {
              const match = visibleExamples.find((project) => project.id === result.id);
              if (match) setSelectedExample(match);
            }}
          />
          <label className="project-library-destination"><span>{text(language, '服务器目标目录', 'Server destination directory')}</span><input value={destinationDirectory} disabled={opening || busy} onChange={(event) => setDestinationDirectory(event.currentTarget.value)} placeholder={text(language, '输入 Host 机器上已存在的绝对目录', 'Existing absolute directory on the Host machine')} /></label>
        </div>
        {!canLibraryOpen && <p className="project-field-hint">{text(language, '当前后端未发布 projectLibrary.open，目录可浏览但不能导入。', 'The backend has not advertised projectLibrary.open. The catalog is viewable, but entries cannot be imported.')}</p>}
        {!canOpenProject && <p className="project-field-hint">{text(language, '当前后端未发布 project.open，导入后的工程不能切换为当前工程。', 'The backend has not advertised project.open, so an imported project cannot become the active project.')}</p>}
        <p className="project-field-hint">{text(language, '合成数据集受 Host DebugMode 控制；此目录请求使用 DebugMode=false，因此这里只列示例工程。', 'Synthetic datasets are gated by Host DebugMode. This catalog request uses DebugMode=false, so it lists example projects only.')}</p>
        {!isAbsoluteServerPath(destinationDirectory.trim()) && <p className="project-field-hint">{text(language, '请输入服务器上已存在的绝对目标目录。', 'Enter an existing absolute destination directory on the server.')}</p>}
        {importBlockedReason && <p className="project-field-hint">{importBlockedReason}</p>}

        {opening && <OpeningProjectPage language={language} fraction={progress} />}
        {catalogBusy && <div className="project-library-empty" role="status"><span className="project-spinner" />{text(language, '正在读取 Host 工程库…', 'Loading the Host project library…')}</div>}
        {catalogError && <div className="project-library-error" role="alert"><strong>{text(language, '工程库读取失败', 'Could not load the project library')}</strong><p>{catalogError.message}</p><details><summary>{text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{catalogError.details}</pre></details><button type="button" className="project-button project-button-secondary" onClick={() => void loadCatalog()}>{text(language, '重试', 'Retry')}</button></div>}
        {openError && <div className="project-library-error" role="alert"><strong>{text(language, '示例工程未打开', 'Example project was not opened')}</strong><p>{openError.message}</p><details><summary>{openError.detailsLabel ?? text(language, '原始诊断', 'Raw diagnostics')}</summary><pre>{openError.details}</pre></details></div>}
        {!catalogBusy && !catalogError && catalog && (visibleExamples.length === 0
          ? <div className="project-library-empty">{text(language, query ? '没有符合筛选条件的示例工程。' : 'Host 当前没有返回示例工程。', query ? 'No examples match this filter.' : 'The Host returned no example projects.')}</div>
          : <div className="project-library-grid">{visibleExamples.map((project) => {
            const thumbnail = safeThumbnail(project.thumbnailUrl);
            return <article className="project-library-card" key={project.id}>
              {thumbnail && <img src={thumbnail} alt="" loading="lazy" />}
              <div className="project-library-card-copy">
                <div className="project-kicker">{project.kind === 'example' ? text(language, '示例工程', 'EXAMPLE') : project.kind}</div>
                <h3>{project.title}</h3>
                <p>{project.description || text(language, 'Host 未提供说明。', 'No description was provided by the Host.')}</p>
                {project.publishDate && <time dateTime={project.publishDate}>{new Date(project.publishDate).toLocaleDateString(language === 'zh' ? 'zh-CN' : 'en-US')}</time>}
                {project.tags.length > 0 && <ul className="project-library-tags">{project.tags.map((tag, index) => <li key={`${tag}:${index}`}>{tag}</li>)}</ul>}
              </div>
              <footer><code title={project.id}>{project.id}</code><button type="button" className="project-button project-button-primary" onClick={() => setSelectedExample(project)}>{text(language, '查看摘要', 'View summary')}</button></footer>
            </article>;
          })}</div>)}
      </>}
    </section>
  );
}
