import type { ReactNode } from 'react';

/** EmptySelectionOverlay.cs: replace contextual content only when selection is empty. */
export function EmptySelectionOverlay({ isEmpty, text, children }: {
  isEmpty: boolean;
  text: string;
  children: ReactNode;
}) {
  return isEmpty ? <div className="map-inspector-empty-state" data-testid="empty-selection-overlay">
    <span className="map-inspector-crosshair" aria-hidden="true">＋</span>
    <p>{text}</p>
  </div> : <>{children}</>;
}
