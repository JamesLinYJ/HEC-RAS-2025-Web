import { useEffect, useId, useRef } from 'react';
import type { XY } from './mapTypes';
import type { NavigationTarget } from './mapNavigation';
import { ZoomToPoint } from './ZoomToPoint';

export function ZoomToPointDialog({ initialPoint, language, onNavigate, onClose }: {
  initialPoint: XY; language: 'zh' | 'en'; onNavigate: (target: NavigationTarget) => void; onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const title = useId();
  useEffect(() => { ref.current?.showModal(); }, []);
  return <dialog ref={ref} className="map-location-dialog" aria-labelledby={title} onCancel={event => { event.preventDefault(); onClose(); }}>
    <h2 id={title}>{language === 'zh' ? '定位到坐标或范围' : 'Go to coordinate or extent'}</h2>
    <ZoomToPoint initialPoint={initialPoint} language={language} onNavigate={onNavigate} onCancel={onClose} />
  </dialog>;
}
