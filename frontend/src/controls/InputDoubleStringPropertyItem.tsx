import type { ChangeEvent } from 'react';

/** Two independently controlled string inputs from Visual.WinUI.Controls.InputDoubleStringPropertyItem. */
export function InputDoubleStringPropertyItem({
  id,
  xValue,
  yValue,
  xLabel = 'X',
  yLabel = 'Y',
  onXChange,
  onYChange,
  readOnly = false,
  disabled = false,
  xError,
  yError,
}: {
  id: string;
  xValue: string;
  yValue: string;
  xLabel?: string;
  yLabel?: string;
  onXChange: (value: string) => void;
  onYChange: (value: string) => void;
  readOnly?: boolean;
  disabled?: boolean;
  xError?: string | null;
  yError?: string | null;
}) {
  const handleX = (event: ChangeEvent<HTMLInputElement>) => onXChange(event.currentTarget.value);
  const handleY = (event: ChangeEvent<HTMLInputElement>) => onYChange(event.currentTarget.value);
  return <div className="editor-double-string-property">
    <label htmlFor={`${id}-x`}>{xLabel}<input id={`${id}-x`} type="text" inputMode="decimal" value={xValue} readOnly={readOnly} disabled={disabled} aria-invalid={Boolean(xError) || undefined} onChange={handleX} /></label>
    <label htmlFor={`${id}-y`}>{yLabel}<input id={`${id}-y`} type="text" inputMode="decimal" value={yValue} readOnly={readOnly} disabled={disabled} aria-invalid={Boolean(yError) || undefined} onChange={handleY} /></label>
    {xError && <small className="editor-field-error">{xError}</small>}
    {yError && <small className="editor-field-error">{yError}</small>}
  </div>;
}
