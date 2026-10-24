import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const SnapToHighGroundGpTool: GeoprocessingToolDefinition = {
  id: 'snap-to-high-ground', operation: 'SnapToHighGround', title: { zh: '吸附到高地', en: 'Snap to High Ground' },
  description: { zh: '将线或面顶点吸附到附近高程。', en: 'Snap line or polygon vertices to nearby high ground.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'SnapToHighGroundGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Mixed', outputKind: 'Mixed', fields: [
    gpField('searchRadiusText', '搜索半径', 'Search radius', '50'), gpField('sampleSpacingText', '采样间距', 'Sample spacing', '3'),
    gpField('searchMode', '搜索方式', 'Search mode', 'Perpendicular', 'select', ['Perpendicular', 'Radial']),
    gpField('elevationMode', '选择高程', 'Elevation choice', 'Highest', 'select', ['Highest', 'Lowest']),
    gpField('pointsPerRingText', '每环采样点数（径向）', 'Points per ring (radial)', '16', 'number', undefined, 1, 1, { key: 'searchMode', value: 'Radial' }),
    gpField('iterationsText', '迭代次数', 'Iterations', '1'),
  ],
};
