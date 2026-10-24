import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const LineOffsetGpTool: GeoprocessingToolDefinition = {
  id: 'line-offset', operation: 'LineOffset', title: { zh: '偏移折线', en: 'Offset Lines' },
  description: { zh: '按距离向折线一侧或两侧偏移。', en: 'Offset polylines to one or both sides.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'LineOffsetGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [
    gpField('offsetText', '偏移距离', 'Offset distance', '10'),
    gpField('offsetSides', '偏移侧', 'Offset sides', 'Both', 'select', ['Both', 'Left', 'Right']),
    gpField('includeEndpoints', '包含端点', 'Include endpoints', false, 'toggle'),
  ],
};
