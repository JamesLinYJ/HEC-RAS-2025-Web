import { useEffect, useRef } from 'react';
import { text } from '../../shared/workbench';

/** Available feature actions follow the active selection; unsupported link actions are omitted. */
export function RasContextualActionsBar({ name, position, language, projectCoordinates, canHide,
  onZoom, onCopy, onHide, onClose }: {
  name: string;
  position: { x: number; y: number };
  language: 'zh' | 'en';
  projectCoordinates: boolean;
  canHide: boolean;
  onZoom: () => void;
  onCopy: () => void;
  onHide: () => void;
  onClose: () => void;
}) {
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previousFocus = document.activeElement;
    root.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
    return () => {
      if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus();
    };
  }, []);
  return <div ref={root} className="map-feature-context-menu" role="menu"
    aria-label={text(language, '所选要素操作', 'Selected feature actions')}
    style={{ left: position.x, top: position.y }} onKeyDown={event => {
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); onClose(); return; }
      if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault(); event.stopPropagation();
      const buttons = Array.from(root.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []);
      if (!buttons.length) return;
      const selected = buttons.indexOf(document.activeElement as HTMLButtonElement);
      const index = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1
        : (selected + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
      buttons[index].focus();
    }}>
    <strong>{name}</strong>
    <button type="button" role="menuitem" onClick={onZoom}>{text(language, '缩放到要素', 'Zoom to feature')}</button>
    <button type="button" role="menuitem" onClick={onCopy}>{projectCoordinates
      ? text(language, '复制工程坐标要素 JSON', 'Copy project-coordinate feature JSON')
      : text(language, '复制 GeoJSON', 'Copy GeoJSON')}</button>
    <button type="button" role="menuitem" disabled={!canHide}
      title={!canHide ? text(language, '请在工程图层树中切换显隐。', 'Change visibility in the project layer tree.') : undefined}
      onClick={onHide}>{text(language, '隐藏图层', 'Hide layer')}</button>
    <button type="button" role="menuitem" onClick={onClose}>{text(language, '关闭', 'Close')}</button>
  </div>;
}
