import type { MapResultFramesController } from './useMapResultFrames';
import { resultMapTypeText } from './resultMapTypeText';

interface MultiResultVisualizationSettingsProps {
  language: 'zh' | 'en';
  controller: MapResultFramesController;
}

/** Shows the actual map type and render flag returned for the current MultiResult frame. */
export function MultiResultVisualizationSettings({ language, controller }: MultiResultVisualizationSettingsProps) {
  if (controller.activeNode?.node.nodeType !== 'MultiResult') return null;
  const zh = language === 'zh';
  const currentMapType = controller.activeNode.selectedMapType
    ?? (new Set(controller.layers.map((layer) => layer.mapType)).size === 1 ? controller.layers[0]?.mapType ?? null : null);
  const horizontalValues = new Set(controller.layers.map((layer) => layer.forceHorizontalRendering));
  const mapTypeLabel = currentMapType ? resultMapTypeText(language, currentMapType) : (zh ? '服务端未提供' : 'Not provided by the service');
  const horizontalLabel = controller.status === 'loading'
    ? (zh ? '正在读取当前帧…' : 'Reading the current frame…')
    : horizontalValues.size === 0
      ? (zh ? '当前帧未提供' : 'Not provided for this frame')
      : horizontalValues.size > 1
        ? (zh ? '各图层设置不同' : 'Varies by layer')
        : horizontalValues.has(true)
          ? (zh ? '是' : 'Yes')
          : (zh ? '否' : 'No');

  return <details className="map-time-visualization-settings" style={{ display: 'inline-block', marginInlineEnd: 12 }}>
    <summary>{zh ? '多结果渲染信息' : 'Multi-Result rendering'}</summary>
    <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, paddingBlock: 6 }}>
      <label className="map-time-control" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
        <span>{zh ? '当前结果类型' : 'Current result type'}</span>
        <output>{mapTypeLabel}</output>
      </label>
      <label className="map-time-control" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
        <span>{zh ? '当前帧水平渲染' : 'Horizontal rendering for current frame'}</span>
        <output>{horizontalLabel}</output>
      </label>
    </div>
  </details>;
}
