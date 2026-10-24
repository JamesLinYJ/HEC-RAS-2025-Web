import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const MergePolylinesGpTool: GeoprocessingToolDefinition = {
  id: 'merge-polylines', operation: 'MergePolylines', title: { zh: '合并折线', en: 'Merge Polylines' },
  description: { zh: '按连接端点合并折线。', en: 'Merge polylines that have connected endpoints.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'MergePolylinesGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [
    gpField('mergeToleranceText', '合并容差', 'Merge tolerance', '1'),
    gpField('mergeMode', '分支处理', 'Branch handling', 'PreferClosest', 'select', ['PreferClosest', 'RejectBranching']),
  ],
};
