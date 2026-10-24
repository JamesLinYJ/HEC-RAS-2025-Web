import type { ReactNode } from 'react';

export interface LabeledProps {
  id: string;
  label?: ReactNode;
  labelFor?: string | null;
  className: string;
  disabled?: boolean;
  help?: ReactNode;
  unavailableMessage?: ReactNode;
  conditionalMessage?: ReactNode;
  children: ReactNode;
}

/** Provides the label, disabled state, and supporting text for one property control. */
export function Labeled({
  id,
  label,
  labelFor = id,
  className,
  disabled = false,
  help,
  unavailableMessage,
  conditionalMessage,
  children,
}: LabeledProps) {
  const labelID = `${id}-label`;
  const helpID = help == null ? undefined : `${id}-help`;
  const unavailableID = unavailableMessage == null ? undefined : `${id}-unavailable`;
  const conditionalID = conditionalMessage == null ? undefined : `${id}-condition`;
  const descriptionIDs = [helpID, unavailableID, conditionalID].filter(Boolean).join(' ') || undefined;

  return <div className={className} data-property-item={id} aria-disabled={disabled || undefined}
    role={label != null ? 'group' : undefined} aria-labelledby={label != null ? labelID : undefined}
    aria-describedby={descriptionIDs}>
    {label != null && (labelFor === null
      ? <span id={labelID} className="editor-field-label">{label}</span>
      : <label id={labelID} htmlFor={labelFor}>{label}</label>)}
    {children}
    {help != null && <div id={helpID}>{help}</div>}
    {unavailableMessage != null && <div id={unavailableID}>{unavailableMessage}</div>}
    {conditionalMessage != null && <div id={conditionalID}>{conditionalMessage}</div>}
  </div>;
}
