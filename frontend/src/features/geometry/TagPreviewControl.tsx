import type { Language } from '../../shared/workbench';

export interface GeometryRecentTag {
  name: string;
  type: string;
  value: string;
}

export interface TagPreviewControlProps {
  language: Language;
  itemsSource: GeometryRecentTag[];
  disabled?: boolean;
  onTagClicked: (tag: GeometryRecentTag) => void;
}

/** Compact recent-tag preview; clicks preserve the source TagPreviewControl immediate-apply behavior. */
export function TagPreviewControl({ language, itemsSource, disabled = false, onTagClicked }: TagPreviewControlProps) {
  if (!itemsSource.length) return null;
  return <div className="geometry-tag-preview" aria-label={language === 'zh' ? '最近使用的标签' : 'Recent tags'}>
    <span className="geometry-inline-help">{language === 'zh' ? '最近使用' : 'Recent'}:</span>
    {itemsSource.map((tag, index) => <button type="button" className="geometry-tag-preview-item"
      key={`${tag.name}\u0000${tag.type}\u0000${tag.value}\u0000${index}`} disabled={disabled}
      title={`${tag.name} · ${tag.type} · ${tag.value}`} onClick={() => onTagClicked(tag)}>
      <strong>{tag.name}</strong><span>{tag.value}</span>
    </button>)}
  </div>;
}

export default TagPreviewControl;
