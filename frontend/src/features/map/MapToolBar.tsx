import { MAP_TOOL_DESCRIPTORS } from './MapToolsVM';
import type { MapToolID } from './MapToolsVM';
import { MapToolButton } from './MapToolButton';

export interface MapToolBarProps {
  activeTool: MapToolID;
  onToolChange: (tool: MapToolID) => void;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onFit: () => void;
  onZoomToPoint?: () => void;
  canFit: boolean;
  language: 'zh' | 'en';
}

export function MapToolBar({ activeTool, onToolChange, onZoomIn, onZoomOut, onFit, onZoomToPoint, canFit, language }: MapToolBarProps) {
  const labels = language === 'zh'
    ? { select: '选择', pan: '平移', drawPoint: '点', drawLine: '线', drawPolygon: '面', measureLength: '测距', measureArea: '测面积', zoomIn: '放大', zoomOut: '缩小', fit: '适图', group: '地图工具', locate: '定位到坐标或范围' }
    : { select: 'Select', pan: 'Pan', drawPoint: 'Point', drawLine: 'Line', drawPolygon: 'Area', measureLength: 'Length', measureArea: 'Area', zoomIn: 'Zoom in', zoomOut: 'Zoom out', fit: 'Fit', group: 'Map tools', locate: 'Go to coordinate or extent' };
  return <div className="map-tool-strip" role="toolbar" aria-label={labels.group}>
    <div className="map-tool-set">{MAP_TOOL_DESCRIPTORS.map(tool => <MapToolButton
      key={tool.id} tool={tool.id} label={labels[tool.id]} title={language === 'zh' ? labels[tool.id] : tool.title}
      glyph={tool.glyph} selected={activeTool === tool.id} onSelect={onToolChange} />)}</div>
    <div className="map-tool-divider" />
    <div className="map-view-controls" aria-label={language === 'zh' ? '缩放' : 'Zoom controls'}>
      <button type="button" onClick={onZoomIn} aria-label={labels.zoomIn} title={labels.zoomIn}>＋</button>
      <button type="button" onClick={onZoomOut} aria-label={labels.zoomOut} title={labels.zoomOut}>−</button>
      <button type="button" onClick={onFit} disabled={!canFit} aria-label={labels.fit} title={labels.fit}>⌗</button>
      {onZoomToPoint && <button type="button" onClick={onZoomToPoint} aria-label={labels.locate} title={labels.locate} data-testid="map-zoom-to-point">◎</button>}
    </div>
  </div>;
}
