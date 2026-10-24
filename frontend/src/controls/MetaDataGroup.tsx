import type { ReactNode } from 'react';

interface MetaDataGroupProps {
  title: string;
  children: ReactNode;
}

/** A collapsible source-schema group whose contents are the live field controls. */
export function MetaDataGroup({ title, children }: MetaDataGroupProps) {
  return <details className="editor-field-group editor-metadata-group" open>
    {title && <summary>{title}</summary>}
    {children}
  </details>;
}
