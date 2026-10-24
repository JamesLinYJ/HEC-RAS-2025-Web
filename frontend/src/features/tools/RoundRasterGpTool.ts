import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const RoundRasterGpTool: GeoprocessingToolDefinition = {
  id: 'round-raster', operation: 'RoundRaster', title: { zh: '栅格值取整', en: 'Round Raster Values' },
  description: { zh: '按指定精度重采样并取整栅格值。', en: 'Resample and round raster values to a selected precision.' },
  behaviorFile: 'RasterMapLayerBehaviors.cs', sourceFile: 'RoundRasterGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Raster', outputKind: 'Raster', fields: [
    gpField('cellSizeText', '像元大小', 'Cell size', '10'),
    gpField('roundTo', '取整精度', 'Rounding precision', 'Tenths', 'select', ['None', 'Tenths', 'Hundredths', 'Thousandths', 'Sixteenths', 'ThirtySeconds', 'SixtyFourths', 'HundredTwentyEighths', 'ThousandTwentyFourths']),
  ],
};
