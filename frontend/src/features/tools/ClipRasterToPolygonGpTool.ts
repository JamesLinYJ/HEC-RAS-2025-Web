import { geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const ClipRasterToPolygonGpTool: GeoprocessingToolDefinition = {
  id: 'clip-raster-to-polygon', operation: 'ClipRasterToPolygon', title: { zh: '按多边形裁剪栅格', en: 'Clip Raster to Polygon' },
  description: { zh: '使用多边形边界裁剪当前栅格。', en: 'Clip the active raster using polygon boundaries.' },
  behaviorFile: 'RasterMapLayerBehaviors.cs', sourceFile: 'ClipRasterToPolygonGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Raster', fields: [],
};
