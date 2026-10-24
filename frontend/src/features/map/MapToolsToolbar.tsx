import { MAP_TOOL_DESCRIPTORS } from './MapToolsVM';
import type { MapToolID } from './MapToolsVM';

export interface MapToolsToolbarProps {
  activeTool: MapToolID;
  onToolChange: (tool: MapToolID) => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onFit: () => void;
  canFit: boolean;
  language: 'zh' | 'en';
}

export function MapToolsToolbar({ activeTool, onToolChange, onZoomIn, onZoomOut, onFit, canFit, language }: MapToolsToolbarProps) {
  const labels = language === 'zh'
    ? { select: '选择', pan: '平移', drawPoint: '点', drawLine: '线', drawPolygon: '面', measureLength: '测距', measureArea: '测面积', zoomIn: '放大', zoomOut: '缩小', fit: '适图', group: '地图工具' }
    : { select: 'Select', pan: 'Pan', drawPoint: 'Point', drawLine: 'Line', drawPolygon: 'Area', measureLength: 'Length', measureArea: 'Area', zoomIn: 'Zoom in', zoomOut: 'Zoom out', fit: 'Fit', group: 'Map tools' };
  return (
    <div className="map-tool-strip" role="toolbar" aria-label={labels.group}>
      <div className="map-tool-set">
        {MAP_TOOL_DESCRIPTORS.map((tool) => (
          <button
            key={tool.id}
            type="button"
            className={'map-tool-button' + (activeTool === tool.id ? ' is-active' : '')}
            title={language === 'zh' ? labels[tool.id] : tool.title}
            aria-label={language === 'zh' ? labels[tool.id] : tool.title}
            aria-pressed={activeTool === tool.id}
            onClick={() => onToolChange(tool.id)}
          >
            <span aria-hidden="true">{tool.glyph}</span>
            <small>{labels[tool.id]}</small>
          </button>
        ))}
      </div>
      <div className="map-tool-divider" />
      <div className="map-view-controls" aria-label={language === 'zh' ? '缩放' : 'Zoom controls'}>
        <button type="button" onClick={onZoomIn} aria-label={labels.zoomIn} title={labels.zoomIn}>＋</button>
        <button type="button" onClick={onZoomOut} aria-label={labels.zoomOut} title={labels.zoomOut}>−</button>
        <button type="button" onClick={onFit} disabled={!canFit} aria-label={labels.fit} title={labels.fit}>⌗</button>
      </div>
    </div>
  );
}
