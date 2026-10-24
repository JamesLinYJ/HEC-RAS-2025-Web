import { geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const ReverseGpTool: GeoprocessingToolDefinition = {
  id: 'reverse-polyline', operation: 'Reverse', title: { zh: '反转折线方向', en: 'Reverse Polylines' },
  description: { zh: '反转每条折线的顶点顺序。', en: 'Reverse the vertex order of each polyline.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'ReverseGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [],
};
