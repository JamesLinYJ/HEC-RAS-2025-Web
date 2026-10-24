import { geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const PolylineToPolygonGpTool: GeoprocessingToolDefinition = {
  id: 'polyline-to-polygon', operation: 'PolylineToPolygon', title: { zh: '折线转多边形', en: 'Convert Polylines to Polygons' },
  description: { zh: '闭合折线并生成多边形。', en: 'Close polylines to create polygons.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'PolylineToPolygonGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polygon', fields: [],
};
