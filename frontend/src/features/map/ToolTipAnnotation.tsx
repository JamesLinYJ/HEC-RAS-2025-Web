import type { MapHoverTooltip } from './useMapHoverIdentify';
import { mapHoverEntryText } from './resultMapTypeText';

export interface ToolTipAnnotationProps {
  tooltip: MapHoverTooltip | null;
  language: 'zh' | 'en';
}

/** Displays the source labels returned by the Host's real map hover sampler. */
export function ToolTipAnnotation({ tooltip, language }: ToolTipAnnotationProps) {
  if (!tooltip || tooltip.entries.length === 0) return null;
  return (
    <div
      className="map-hover-tooltip"
      data-testid="map-hover-tooltip"
      role="tooltip"
      aria-label={language === 'zh' ? '地图值提示' : 'Map value tooltip'}
      style={{ left: tooltip.screen[0], top: tooltip.screen[1] + 24 }}
    >
      {tooltip.entries.map((entry, index) => (
        <div
          className="map-hover-tooltip-entry"
          data-testid="map-hover-tooltip-entry"
          data-source-kind={entry.kind}
          data-source-path={entry.sourcePath ?? undefined}
          data-source-revision={entry.sourceRevision ?? undefined}
          data-map-type={entry.mapType ?? undefined}
          data-profile-index={entry.profileIndex ?? undefined}
          data-value={entry.value ?? undefined}
          data-value-type={entry.valueType ?? undefined}
          key={`${entry.kind}:${entry.sourcePath ?? ''}:${entry.sourceRevision ?? ''}:${index}`}
        >
          {mapHoverEntryText(language, {
            kind: entry.kind,
            mapType: entry.mapType,
            value: entry.value,
            label: entry.label,
          })}
        </div>
      ))}
    </div>
  );
}
