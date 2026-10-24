import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const LaplacianTerrainGpTool: GeoprocessingToolDefinition = {
  id: 'laplacian-terrain', operation: 'LaplacianTerrain', title: { zh: '拉普拉斯曲面', en: 'Laplacian Surface' },
  description: { zh: '在多边形内生成平滑插值曲面。', en: 'Create a smoothly interpolated surface inside polygons.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'LaplacianTerrainGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Raster', fields: [
    gpField('pointsPerRingText', '每环点数', 'Points per ring', '16'), gpField('iterationsText', '迭代次数', 'Iterations', '1'),
    gpField('cellSizeText', '像元大小', 'Cell size', '10', 'number', undefined, 0, 0.1, { key: 'useAdvancedSizing', value: false }),
    gpField('useAdvancedSizing', '高级尺寸设置', 'Advanced sizing', false, 'toggle'),
    gpField('outputCellSizeText', '输出像元大小', 'Output cell size', '10', 'number', undefined, 0, 0.1, { key: 'useAdvancedSizing', value: true }),
    gpField('boundarySpacingText', '边界间距', 'Boundary spacing', '10', 'number', undefined, 0, 0.1, { key: 'useAdvancedSizing', value: true }),
    gpField('interiorGridSpacingText', '内部网格间距', 'Interior grid spacing', '10', 'number', undefined, 0, 0.1, { key: 'useAdvancedSizing', value: true }),
  ],
};
