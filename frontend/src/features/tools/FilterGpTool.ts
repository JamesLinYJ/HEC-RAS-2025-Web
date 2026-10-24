import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const FilterGpTool: GeoprocessingToolDefinition = {
  id: 'filter', operation: 'Filter', title: { zh: '简化要素', en: 'Filter / Simplify' },
  description: { zh: '按容差简化线或面边界。', en: 'Simplify line or polygon boundaries using a tolerance.' },
  behaviorFile: 'PolygonLayerBehaviors.cs', sourceFile: 'FilterGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Mixed', outputKind: 'Mixed', fields: [gpField('toleranceText', '简化容差', 'Filter tolerance', '0.05', 'number', undefined, 0)],
};
