import { useEffect, useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface CatalogFilePathControlProps {
  language: Language;
  filePath?: string | null;
  dateCreated?: string | null;
  dateLastModified?: string | null;
  showTimestamps?: boolean;
}

function copyWithSelectionFallback(value: string): boolean {
  const input = document.createElement('textarea');
  input.value = value;
  input.setAttribute('readonly', '');
  input.style.position = 'fixed';
  input.style.opacity = '0';
  document.body.appendChild(input);
  input.select();
  try {
    return document.execCommand('copy');
  } finally {
    input.remove();
  }
}

/** Shows source-provided path/timestamps and copies the exact path text. */
export function CatalogFilePathControl({ language, filePath, dateCreated, dateLastModified, showTimestamps = true }: CatalogFilePathControlProps) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const copyGeneration = useRef(0);
  const path = filePath?.trim() ? filePath : null;
  const missing = text(language, '未提供', 'Not provided');

  useEffect(() => {
    setCopyState('idle');
    return () => { copyGeneration.current += 1; };
  }, [filePath]);

  async function copyPath() {
    if (!path) return;
    const generation = ++copyGeneration.current;
    try {
      if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(path);
      else if (!copyWithSelectionFallback(path)) throw new Error('Clipboard copy was unavailable');
      if (copyGeneration.current === generation) setCopyState('copied');
    } catch {
      try {
        if (!copyWithSelectionFallback(path)) throw new Error('Clipboard copy was unavailable');
        if (copyGeneration.current === generation) setCopyState('copied');
      } catch {
        if (copyGeneration.current === generation) setCopyState('failed');
      }
    }
  }

  return (
    <section className="project-catalog-file-path" aria-label={showTimestamps ? text(language, '文件路径与时间', 'File path and timestamps') : text(language, '文件路径', 'File path')}>
      <div className="project-catalog-path-heading">
        <span>{text(language, '文件路径', 'File path')}</span>
        <button type="button" disabled={!path} onClick={copyPath}>
          {copyState === 'copied' ? text(language, '已复制', 'Copied')
            : copyState === 'failed' ? text(language, '复制失败', 'Copy failed')
              : text(language, '复制路径', 'Copy path')}
        </button>
      </div>
      <code className={!path ? 'is-missing' : ''}>{path ?? missing}</code>
      {showTimestamps && <dl className="project-catalog-timestamps">
        <div><dt>{text(language, '创建时间', 'Created')}</dt><dd>{dateCreated?.trim() || missing}</dd></div>
        <div><dt>{text(language, '修改时间', 'Modified')}</dt><dd>{dateLastModified?.trim() || missing}</dd></div>
      </dl>}
      {copyState === 'failed' && <span className="project-catalog-copy-error" role="status">{text(language, '浏览器未允许复制，请手动选择路径。', 'The browser blocked clipboard access. Select the path to copy it manually.')}</span>}
    </section>
  );
}
