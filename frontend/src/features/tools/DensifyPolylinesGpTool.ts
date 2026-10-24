import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const DensifyPolylinesGpTool: GeoprocessingToolDefinition = {
  id: 'densify-polylines', operation: 'DensifyPolylines', title: { zh: '加密折线顶点', en: 'Densify Polylines' },
  description: { zh: '插入顶点，使线段不超过最大长度。', en: 'Add vertices to keep line segments within the maximum length.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'DensifyPolylinesGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [gpField('maxSegmentLengthText', '最大线段长度', 'Maximum segment length', '10')],
};
