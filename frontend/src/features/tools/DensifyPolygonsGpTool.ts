import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const DensifyPolygonsGpTool: GeoprocessingToolDefinition = {
  id: 'densify-polygons', operation: 'DensifyPolygons', title: { zh: '加密多边形顶点', en: 'Densify Polygons' },
  description: { zh: '插入顶点，使边线段不超过最大长度。', en: 'Add vertices to keep polygon segments within the maximum length.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'DensifyPolygonsGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Polygon', fields: [gpField('maxSegmentLengthText', '最大线段长度', 'Maximum segment length', '10')],
};
