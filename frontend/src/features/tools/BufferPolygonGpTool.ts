import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const BufferPolygonGpTool: GeoprocessingToolDefinition = {
  id: 'buffer-polygon', operation: 'BufferPolygon', title: { zh: '多边形缓冲区', en: 'Buffer Polygons' },
  description: { zh: '按距离扩张或收缩多边形。', en: 'Expand or contract polygons by a distance.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'BufferPolygonGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Polygon', fields: [gpField('bufferPolygonDistanceText', '缓冲距离', 'Buffer distance', '10')],
};
