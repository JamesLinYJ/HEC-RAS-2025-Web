import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const RasterizePolygonsGpTool: GeoprocessingToolDefinition = {
  id: 'rasterize-polygons', operation: 'RasterizePolygons', title: { zh: '多边形栅格化', en: 'Rasterize Polygons' },
  description: { zh: '将多边形填充到规则栅格。', en: 'Rasterize polygons into a filled grid.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'RasterizePolygonsGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Raster', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'), gpField('fillValueText', '填充值', 'Fill value', '1'),
    gpField('backgroundValueText', '背景值', 'Background value', '0'), gpField('outputCellSizeText', '输出像元大小（0 为自动）', 'Output cell size (0 = automatic)', '0'),
  ],
};
