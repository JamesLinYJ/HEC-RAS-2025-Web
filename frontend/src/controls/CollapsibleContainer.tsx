import { useState } from 'react';
import type { ReactNode } from 'react';
import { ControlHeader } from './ControlHeader';

export interface CollapsibleContainerProps {
  title: ReactNode;
  subContent?: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
  className?: string;
}

/** A native disclosure group, initially open to match schema field groups. */
export function CollapsibleContainer({ title, subContent, children, defaultOpen = true, className = '' }: CollapsibleContainerProps) {
  const [open, setOpen] = useState(defaultOpen);
  return <details className={`editor-field-group editor-metadata-group ${className}`.trim()} open={open}
    onToggle={(event) => setOpen(event.currentTarget.open)}>
    <summary><ControlHeader title={title} subContent={subContent} /></summary>
    {children}
  </details>;
}
