import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const ResampleRasterGpTool: GeoprocessingToolDefinition = {
  id: 'resample-raster', operation: 'ResampleRaster', title: { zh: '重采样栅格', en: 'Resample Raster' },
  description: { zh: '按新像元大小重采样当前栅格。', en: 'Resample the active raster to a new cell size.' },
  behaviorFile: 'RasterMapLayerBehaviors.cs', sourceFile: 'ResampleRasterGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Raster', outputKind: 'Raster', fields: [gpField('cellSizeText', '目标像元大小', 'Target cell size', '10')],
};
