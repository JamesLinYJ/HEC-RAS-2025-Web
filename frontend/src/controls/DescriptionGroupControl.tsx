import type { ReactNode } from 'react';

interface DescriptionGroupControlProps {
  title: string;
  descriptionFieldKey?: string;
  imageFieldKey?: string;
  children: ReactNode;
}

/** Keeps the source description and its embedded-image collection together in schema order. */
export function DescriptionGroupControl({ title, descriptionFieldKey, imageFieldKey, children }: DescriptionGroupControlProps) {
  return <details className="editor-field-group editor-description-group" open
    data-description-field={descriptionFieldKey} data-images-field={imageFieldKey}>
    <summary>{title}</summary>
    {children}
  </details>;
}
