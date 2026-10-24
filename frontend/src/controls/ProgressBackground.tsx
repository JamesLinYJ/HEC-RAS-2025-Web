/** Controlled progress fill; indeterminate mode is used only before a sample arrives. */
export function ProgressBackground({ progress, indeterminate = false }: {
  progress?: number;
  indeterminate?: boolean;
}) {
  const value = progress !== undefined && Number.isFinite(progress)
    ? Math.min(100, Math.max(0, progress))
    : undefined;
  return <div
    className="progress-fill"
    data-progress-background
    data-progress-value={value}
    style={indeterminate ? undefined : { width: `${value ?? 0}%` }}
    aria-hidden="true"
  />;
}

export default ProgressBackground;
