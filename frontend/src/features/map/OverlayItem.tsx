import { useState, type ReactNode } from 'react';
import type { Language } from '../../shared/workbench';
import { MapOverlayChrome, type MapOverlayPlacement } from './MapOverlayChrome';

export interface OverlayItemProps {
  title: string;
  collapsedTitle?: string;
  icon?: ReactNode;
  language: Language;
  children: ReactNode;
  placement?: MapOverlayPlacement;
  defaultCollapsed?: boolean;
  className?: string;
}

/** A map overlay item with a live, per-item collapse/expand control. */
export function OverlayItem({ title, collapsedTitle, icon = '▧', language, children,
  placement = 'top-right', defaultCollapsed = false, className }: OverlayItemProps) {
  const [collapsed, setCollapsed] = useState(defaultCollapsed);
  return (
    <MapOverlayChrome title={title} collapsedTitle={collapsedTitle} icon={icon} collapsed={collapsed}
      placement={placement} language={language} onToggle={() => setCollapsed(value => !value)} className={className}>
      {children}
    </MapOverlayChrome>
  );
}
