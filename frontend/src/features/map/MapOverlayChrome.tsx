import type { ReactNode } from 'react';
import type { Language } from '../../shared/workbench';

export type MapOverlayPlacement = 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';

export interface MapOverlayChromeProps {
  title: string;
  collapsedTitle?: string;
  icon: ReactNode;
  collapsed: boolean;
  placement: MapOverlayPlacement;
  language: Language;
  onToggle: () => void;
  children: ReactNode;
  className?: string;
}

/** Shared glass chrome for map controls; OverlayItem owns the collapse state. */
export function MapOverlayChrome({ title, collapsedTitle, icon, collapsed, placement, language,
  onToggle, children, className }: MapOverlayChromeProps) {
  const zh = language === 'zh';
  const visibleTitle = collapsed ? collapsedTitle || title : title;
  const collapseLabel = collapsed
    ? (zh ? `展开${title}` : `Expand ${title}`)
    : (zh ? `收起${title}` : `Collapse ${title}`);

  return (
    <section className={`map-overlay-chrome${collapsed ? ' is-collapsed' : ''}${className ? ` ${className}` : ''}`}
      data-map-overlay-placement={placement} aria-label={title}>
      <header className="map-overlay-chrome-header">
        <span className="map-overlay-chrome-icon" aria-hidden="true">{icon}</span>
        <span className="map-overlay-chrome-title">{visibleTitle}</span>
        <button type="button" className="map-overlay-chrome-toggle" onClick={onToggle}
          aria-label={collapseLabel} aria-expanded={!collapsed} title={collapseLabel}>
          <span aria-hidden="true">{collapsed ? '⌄' : '⌃'}</span>
        </button>
      </header>
      {!collapsed && <div className="map-overlay-chrome-content">{children}</div>}
    </section>
  );
}
