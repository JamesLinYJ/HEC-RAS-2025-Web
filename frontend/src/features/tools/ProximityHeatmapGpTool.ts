import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const ProximityHeatmapGpTool: GeoprocessingToolDefinition = {
  id: 'proximity-heatmap', operation: 'ProximityHeatmap', title: { zh: '邻近热力图', en: 'Proximity Heatmap' },
  description: { zh: '按折线距离生成高斯衰减热力栅格。', en: 'Create a Gaussian-decay heatmap around input lines.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'ProximityHeatmapGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Raster', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'), gpField('sigmaCellsText', '标准差（像元）', 'Sigma (cells)', '10'),
    gpField('maxDistanceCellsText', '最大距离（像元）', 'Maximum distance (cells)', '100'),
    gpField('distanceAlgorithm', '距离算法', 'Distance algorithm', 'EDT', 'select', ['EDT', 'SpatialIndex']),
  ],
};
