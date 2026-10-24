import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const FlatAreaDetectorGpTool: GeoprocessingToolDefinition = {
  id: 'flat-area-detector', operation: 'FlatAreaDetector', title: { zh: '平坦区域检测', en: 'Flat Area Detector' },
  description: { zh: '从种子点周边检测平坦区域。', en: 'Detect flat areas around seed points.' },
  behaviorFile: 'PointVectorContextBehaviors.cs', sourceFile: 'FlatAreaDetectorGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Point', outputKind: 'Polygon', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'),
    gpField('searchRadiusText', '搜索半径', 'Search radius', '50'), gpField('maxSlopePercentText', '最大坡度百分比', 'Maximum slope percent', '5'),
    gpField('connectivity', '像元邻接', 'Cell connectivity', 'EightCorners', 'select', ['FourSides', 'EightCorners']),
    gpField('simplifyDistanceText', '简化距离', 'Simplify distance', '0'), gpField('minimumRingAreaText', '最小环面积', 'Minimum ring area', '0'),
  ],
};
