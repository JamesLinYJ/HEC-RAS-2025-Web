import type { ReactNode } from 'react';

export interface ControlHeaderProps {
  title: ReactNode;
  subContent?: ReactNode;
  className?: string;
}

/** Compact heading used by grouped controls and their accessible disclosures. */
export function ControlHeader({ title, subContent, className = '' }: ControlHeaderProps) {
  return <span className={`editor-control-header ${className}`.trim()}>
    <span className="editor-control-header-title">{title}</span>
    {subContent != null && <span className="editor-control-header-subcontent">{subContent}</span>}
  </span>;
}
