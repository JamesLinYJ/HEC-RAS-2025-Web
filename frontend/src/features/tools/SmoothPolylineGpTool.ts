import { gpField, geoprocessingSource, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';
export const SmoothPolylineGpTool: GeoprocessingToolDefinition = {
  id: 'smooth-polylines', operation: 'Smooth', title: { zh: '平滑折线', en: 'Smooth Polylines' },
  description: { zh: '使用移动平均平滑折线。', en: 'Smooth polylines with a moving average.' },
  behaviorFile: 'PolylineLayerBehaviors.cs', sourceFile: 'SmoothPolylineGpTool.cs', sourceDirectory: geoprocessingSource.directory,
  inputKind: 'Polyline', outputKind: 'Polyline', fields: [
    gpField('movingAverageWindowText', '移动平均窗口', 'Moving-average window', '5'),
    gpField('movingAveragePassesText', '移动平均次数', 'Moving-average passes', '1'),
  ],
};
