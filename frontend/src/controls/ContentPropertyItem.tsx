import type { ReactNode } from 'react';

interface ContentPropertyItemProps {
  id: string;
  label?: ReactNode;
  labelFor?: string;
  className: string;
  help?: ReactNode;
  unavailableMessage?: ReactNode;
  conditionalMessage?: ReactNode;
  children: ReactNode;
}

/** Hosts one schema-backed value control under its source property label. */
export function ContentPropertyItem({
  id,
  label,
  labelFor = id,
  className,
  help,
  unavailableMessage,
  conditionalMessage,
  children,
}: ContentPropertyItemProps) {
  return <div className={className} data-property-item={id}>
    {label != null && <label htmlFor={labelFor}>{label}</label>}
    {children}
    {help}
    {unavailableMessage}
    {conditionalMessage}
  </div>;
}
