import { useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import { SearchResultsControl } from './SearchResultsControl';
import type { ProjectSearchResult } from './SearchResultsControl';

interface SearchBarWithResultsPopupProps {
  language: Language;
  query: string;
  results: ProjectSearchResult[];
  disabled?: boolean;
  onQueryChange: (query: string) => void;
  onSelect: (result: ProjectSearchResult) => void;
}

/** Search input and keyboard-accessible popup for entries already returned by projectLibrary.catalog. */
export function SearchBarWithResultsPopup({ language, query, results, disabled = false, onQueryChange, onSelect }: SearchBarWithResultsPopupProps) {
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const root = useRef<HTMLDivElement>(null);
  const select = (result: ProjectSearchResult): void => { onSelect(result); setOpen(false); };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'ArrowDown' && results.length > 0) { event.preventDefault(); setOpen(true); setActiveIndex((index) => index < 0 ? 0 : Math.min(index + 1, results.length - 1)); }
    else if (event.key === 'ArrowUp' && results.length > 0) { event.preventDefault(); setOpen(true); setActiveIndex((index) => index < 0 ? results.length - 1 : Math.max(index - 1, 0)); }
    else if (event.key === 'Enter' && open && results[activeIndex]) { event.preventDefault(); select(results[activeIndex]); }
    else if (event.key === 'Escape') setOpen(false);
  };

  return <div className="project-search-popup" ref={root} onBlur={(event) => { if (!root.current?.contains(event.relatedTarget as Node | null)) setOpen(false); }}>
    <label className="project-library-search" htmlFor="project-library-search-input">
      <span>{text(language, '搜索示例工程', 'Search example projects')}</span>
      <input id="project-library-search-input" type="search" role="combobox" aria-autocomplete="list" aria-expanded={open} aria-controls="project-library-search-results" aria-activedescendant={activeIndex >= 0 ? `project-library-search-option-${activeIndex}` : undefined} aria-label={text(language, '搜索示例工程', 'Search example projects')} value={query} disabled={disabled} onFocus={() => setOpen(true)} onChange={(event) => { onQueryChange(event.currentTarget.value); setActiveIndex(-1); setOpen(true); }} onKeyDown={onKeyDown} placeholder={text(language, '名称、说明或标签', 'Title, description, or tag')} />
    </label>
    {open && !disabled && <div className="project-search-popup-results"><SearchResultsControl language={language} results={results} activeIndex={activeIndex} onSelect={select} /></div>}
  </div>;
}
