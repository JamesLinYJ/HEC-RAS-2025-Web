import type { Language } from '../../shared/workbench';

export interface GeometryTagItem {
  name: string;
  type: string;
  value: string;
}

export interface TagControlProps {
  language: Language;
  tag: GeometryTagItem;
  disabled?: boolean;
  onDeleteRequested: (tag: GeometryTagItem) => void;
}

/** One common selected-feature tag, matching TagControl's value and delete event. */
export function TagControl({ language, tag, disabled = false, onDeleteRequested }: TagControlProps) {
  return <span className="geometry-tag-chip">
    <strong>{tag.name}</strong><span>{tag.value}</span>
    <button type="button" aria-label={language === 'zh' ? `删除标签 ${tag.name}` : `Delete tag ${tag.name}`}
      title={language === 'zh' ? '从所选要素移除此标签' : 'Remove this tag from selected features'} disabled={disabled}
      onClick={() => onDeleteRequested(tag)}>×</button>
  </span>;
}

export default TagControl;
