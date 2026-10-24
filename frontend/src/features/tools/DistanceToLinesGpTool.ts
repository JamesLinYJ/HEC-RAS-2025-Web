import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const DistanceToLinesGpTool: GeoprocessingToolDefinition = {
  id: 'distance-to-lines', operation: 'DistanceToLines', title: { zh: '线到栅格距离', en: 'Distance to Lines' },
  description: { zh: '根据折线生成欧氏距离栅格。', en: 'Create a Euclidean-distance raster from polylines.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'DistanceToLinesGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Raster', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'), gpField('fillValueText', '线位置值', 'Line value', '1'),
    gpField('backgroundValueText', '背景值', 'Background value', '0'),
    gpField('maxDistanceCellsText', '最大距离（像元）', 'Maximum distance (cells)', '100'),
    gpField('distanceAlgorithm', '距离算法', 'Distance algorithm', 'EDT', 'select', ['EDT', 'SpatialIndex']),
  ],
};
