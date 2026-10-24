import type { Language } from '../../../shared/workbench';
import type { SavedMapView } from './View';

function numberLabel(value: number): string {
  if (!Number.isFinite(value)) return '—';
  return Number(value.toPrecision(9)).toString();
}

export function ViewViewer({ language, views, selectedName, disabled, onSelect, onNavigate }: {
  language: Language;
  views: SavedMapView[];
  selectedName: string | null;
  disabled?: boolean;
  onSelect: (view: SavedMapView) => void;
  onNavigate: (view: SavedMapView) => void;
}) {
  const zh = language === 'zh';
  if (views.length === 0) {
    return <div className="map-views-empty" data-testid="map-views-empty">{zh ? '当前工程还没有保存的地图视图。' : 'This project has no saved map views yet.'}</div>;
  }

  return (
    <ul className="map-views-list" aria-label={zh ? '已保存的地图视图' : 'Saved map views'}>
      {views.map((view, index) => {
        const selected = selectedName === view.name;
        return (
          <li className={selected ? 'map-views-row is-selected' : 'map-views-row'} key={`${index}:${view.name}:${view.entrySHA256}`} data-testid={`map-view-row-${index}`} data-view-name={view.name}>
            <button type="button" className="map-views-row-select" aria-pressed={selected} disabled={disabled}
              data-testid={`map-view-select-${index}`} data-view-name={view.name}
              onClick={() => onSelect(view)} onDoubleClick={() => onNavigate(view)}>
              <strong>{view.name}</strong>
              <span>{view.shortcut === 'None' ? (zh ? '无快捷键' : 'No shortcut') : view.shortcut}</span>
              <code>{zh ? '左' : 'L'} {numberLabel(view.extent.left)} · {zh ? '下' : 'B'} {numberLabel(view.extent.bottom)} · {zh ? '右' : 'R'} {numberLabel(view.extent.right)} · {zh ? '上' : 'T'} {numberLabel(view.extent.top)}</code>
            </button>
            <button type="button" className="map-views-row-jump" data-testid={`map-view-jump-${index}`} disabled={disabled}
              onClick={() => onNavigate(view)}>{zh ? '定位' : 'Go to'}</button>
          </li>
        );
      })}
    </ul>
  );
}
