import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';

export const BufferPolylineGpTool: GeoprocessingToolDefinition = {
  id: 'buffer-polyline', operation: 'BufferPolyline', title: { zh: '折线缓冲区', en: 'Buffer Polylines' },
  description: { zh: '沿折线生成缓冲多边形。', en: 'Create polygons around input polylines.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'BufferPolylineGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polygon', fields: [
    gpField('bufferWidthText', '缓冲宽度', 'Buffer width', '10'),
    gpField('bufferSmoothingMethod', '平滑方法', 'Smoothing method', 'Standard', 'select', ['Standard', 'MovingAverage']),
    gpField('bufferMovingAverageWindowText', '移动平均窗口', 'Moving-average window', '5'),
    gpField('bufferMovingAveragePassesText', '移动平均次数', 'Moving-average passes', '1'),
  ],
};
