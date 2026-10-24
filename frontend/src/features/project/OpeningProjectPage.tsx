import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

interface OpeningProjectPageProps {
  language: Language;
  status?: string;
  fraction?: number;
}

/** Real in-flight state for the Host projectLibrary.open -> project.open sequence. */
export function OpeningProjectPage({ language, status, fraction }: OpeningProjectPageProps) {
  const progress = typeof fraction === 'number' && Number.isFinite(fraction)
    ? Math.max(0, Math.min(1, fraction))
    : undefined;
  return (
    <section className="project-library-opening" role="status" aria-live="polite">
      <span className="project-spinner" aria-hidden="true" />
      <div>
        <strong>{text(language, '正在准备示例工程', 'Preparing example project')}</strong>
        <p>{status ?? text(language, 'Host 正在下载并验证工程文件，随后会由 project.open 确认工程路径、XML 和修订。', 'The Host is downloading and validating the project files. project.open will then confirm its path, XML, and revision.')}</p>
        {progress !== undefined && <progress max={1} value={progress} aria-label={text(language, '示例工程准备进度', 'Example project preparation progress')} />}
      </div>
    </section>
  );
}
