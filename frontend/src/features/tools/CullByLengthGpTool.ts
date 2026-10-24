import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const CullByLengthGpTool: GeoprocessingToolDefinition = {
  id: 'cull-by-length', operation: 'CullByLength', title: { zh: '按长度筛除折线', en: 'Cull by Length' },
  description: { zh: '删除短于指定长度的折线。', en: 'Remove polylines shorter than a specified length.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'CullByLengthGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [gpField('minimumLengthText', '最小长度', 'Minimum length', '10')],
};
