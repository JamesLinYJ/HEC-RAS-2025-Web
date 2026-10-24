import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const RasterCutStatsGpTool: GeoprocessingToolDefinition = {
  id: 'raster-cut-stats', operation: 'RasterCutStats', title: { zh: '栅格分区统计', en: 'Raster Cut Stats' },
  description: { zh: '计算每个多边形内的栅格统计与直方图。', en: 'Compute raster statistics and histograms for each polygon.' },
  behaviorFile: 'RasterMapLayerBehaviors.cs', sourceFile: 'RasterCutStatsGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polygon', outputKind: 'Text', fields: [gpField('binCountText', '直方图分箱数', 'Histogram bin count', '25')],
};
