interface DropTargetHighlightProps {
  active: boolean;
  language: 'zh' | 'en';
}

export function DropTargetHighlight({ active, language }: DropTargetHighlightProps) {
  return <span className={`vector-drop-highlight${active ? ' is-active' : ''}`} aria-hidden="true">
    {active ? (language === 'zh' ? '松开以添加文件' : 'Release to add files') : ''}
  </span>;
}
