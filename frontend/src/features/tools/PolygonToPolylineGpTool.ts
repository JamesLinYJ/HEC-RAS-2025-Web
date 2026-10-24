import { geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const PolygonToPolylineGpTool: GeoprocessingToolDefinition = {
  id: 'polygon-to-polyline', operation: 'PolygonToPolyline', title: { zh: '多边形转折线', en: 'Convert Polygons to Polylines' },
  description: { zh: '将多边形边界转换为折线。', en: 'Convert polygon boundaries to polylines.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'PolygonToPolylineGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Polyline', fields: [],
};
