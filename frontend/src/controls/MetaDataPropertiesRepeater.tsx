import type { ReactNode } from 'react';

interface MetaDataPropertiesRepeaterProps {
  children: ReactNode;
  orientation?: 'horizontal' | 'vertical';
}

/** Preserves schema field order while laying out the fields of one metadata group. */
export function MetaDataPropertiesRepeater({ children, orientation = 'horizontal' }: MetaDataPropertiesRepeaterProps) {
  return <div className={`editor-field-grid editor-metadata-properties is-${orientation}`} data-orientation={orientation}>
    {children}
  </div>;
}
