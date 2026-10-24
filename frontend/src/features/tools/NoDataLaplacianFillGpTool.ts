import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const NoDataLaplacianFillGpTool: GeoprocessingToolDefinition = {
  id: 'nodata-laplacian-fill', operation: 'NoDataLaplacianFill', title: { zh: '拉普拉斯填补 NoData', en: 'Fill NoData (Laplacian)' },
  description: { zh: '使用种子点对栅格 NoData 区域进行拉普拉斯插值。', en: 'Interpolate raster NoData areas from seed points.' },
  behaviorFile: 'RasterMapLayerBehaviors.cs', sourceFile: 'NoDataLaplacianFillGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Point', outputKind: 'Raster', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'),
    gpField('boundarySpacingText', '边界间距', 'Boundary spacing', '10'),
    gpField('interiorGridSpacingText', '内部网格间距', 'Interior grid spacing', '10'),
    gpField('connectivity', '像元邻接', 'Cell connectivity', 'EightCorners', 'select', ['FourSides', 'EightCorners']),
    gpField('noDataOutputMode', '输出范围', 'Output extent', 'Patch', 'select', ['Patch', 'Full']),
  ],
};
