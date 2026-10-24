import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';

export interface ProjectSearchResult {
  id: string;
  title: string;
  description: string;
  detail?: string;
}

interface SearchResultsControlProps {
  language: Language;
  results: ProjectSearchResult[];
  activeIndex: number;
  onSelect: (result: ProjectSearchResult) => void;
}

/** Result list used by the project-library search popup. */
export function SearchResultsControl({ language, results, activeIndex, onSelect }: SearchResultsControlProps) {
  return <ul id="project-library-search-results" className="project-search-results" role="listbox" aria-label={text(language, '搜索结果', 'Search results')}>
    {results.length === 0 && <li><div className="project-search-empty" role="option" aria-disabled="true">{text(language, '没有匹配的工程。', 'No matching projects.')}</div></li>}
    {results.map((result, index) => <li key={result.id}>
      <button id={`project-library-search-option-${index}`} type="button" role="option" aria-selected={activeIndex === index} className={activeIndex === index ? 'is-active' : ''} onMouseDown={(event) => event.preventDefault()} onClick={() => onSelect(result)}>
        <strong>{result.title}</strong><span>{result.description || text(language, 'Host 未提供说明。', 'No description was provided by the Host.')}</span>{result.detail && <small>{result.detail}</small>}
      </button>
    </li>)}
  </ul>;
}
