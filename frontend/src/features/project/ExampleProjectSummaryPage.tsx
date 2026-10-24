import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface ExampleProjectDefinition {
  id: string;
  title: string;
  description: string;
  publishDate: string | null;
  tags: string[];
  thumbnailUrl: string | null;
  kind: 'example' | 'synthetic';
}

interface ExampleProjectSummaryPageProps {
  language: Language;
  definition: ExampleProjectDefinition;
  busy: boolean;
  canOpen: boolean;
  onBack: () => void;
  onOpen: () => void;
}

function safeThumbnail(url: string | null): string | undefined {
  if (!url) return undefined;
  try { return new URL(url).protocol === 'https:' ? url : undefined; }
  catch { return undefined; }
}

/** Selected example summary; the primary action runs the existing Host import/open workflow. */
export function ExampleProjectSummaryPage({ language, definition, busy, canOpen, onBack, onOpen }: ExampleProjectSummaryPageProps) {
  const thumbnail = safeThumbnail(definition.thumbnailUrl);
  const date = definition.publishDate ? new Date(definition.publishDate) : null;
  return <article className="project-example-summary" aria-labelledby="project-example-summary-title">
    <header className="project-example-summary-header">
      <div>
        <div className="project-kicker">{text(language, '示例工程 / 摘要', 'EXAMPLE PROJECT / SUMMARY')}</div>
        <h2 id="project-example-summary-title">{definition.title}</h2>
      </div>
      <button type="button" className="project-button project-button-secondary" disabled={busy} onClick={onBack}>{text(language, '返回工程库', 'Back to examples')}</button>
    </header>
    {thumbnail && <img className="project-example-summary-thumbnail" src={thumbnail} alt={text(language, `${definition.title} 的预览图`, `Preview of ${definition.title}`)} />}
    <p className="project-example-summary-description">{definition.description || text(language, 'Host 未提供说明。', 'No description was provided by the Host.')}</p>
    <dl className="project-example-summary-facts">
      <div><dt>{text(language, '工程 ID', 'Project ID')}</dt><dd><code>{definition.id}</code></dd></div>
      {date && <div><dt>{text(language, '发布日期', 'Published')}</dt><dd><time dateTime={definition.publishDate ?? undefined}>{Number.isFinite(date.getTime()) ? date.toLocaleDateString(language === 'zh' ? 'zh-CN' : 'en-US') : definition.publishDate}</time></dd></div>}
      <div><dt>{text(language, '类型', 'Type')}</dt><dd>{definition.kind === 'example' ? text(language, '示例工程', 'Example project') : text(language, '合成数据集', 'Synthetic dataset')}</dd></div>
    </dl>
    {definition.tags.length > 0 && <ul className="project-library-tags" aria-label={text(language, '标签', 'Tags')}>{definition.tags.map((tag, index) => <li key={`${tag}:${index}`}>{tag}</li>)}</ul>}
    <footer className="project-example-summary-actions"><button type="button" className="project-button project-button-primary" disabled={busy || !canOpen} onClick={onOpen}>{text(language, '下载并打开', 'Download and open')}</button></footer>
  </article>;
}
