import type { MapResultFramesController } from './useMapResultFrames';

interface MultiResultNodeDetailsProps {
  language: 'zh' | 'en';
  controller: MapResultFramesController;
  mapID: string;
}

/** ResultSelection collection actions for the active MultiResult node. */
export function MultiResultNodeDetails({ language, controller, mapID }: MultiResultNodeDetailsProps) {
  if (controller.activeNode?.node.nodeType !== 'MultiResult') return null;
  const zh = language === 'zh';
  const add = zh ? '添加对比结果' : 'Add comparison result';
  const remove = zh ? '移除对比结果' : 'Remove comparison result';
  const comparisonTitle = controller.selectionGroups.length >= 32
    ? (zh ? '最多可添加 32 个结果对比源。' : 'Up to 32 Result comparison sources are supported.')
    : (zh ? '没有可添加的未使用结果。' : 'No unused Result is available to add.');

  return <span className="map-time-comparison-actions" style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
    <button
      type="button"
      data-testid="map-result-add-comparison"
      data-map-id={mapID}
      aria-label={add}
      title={controller.canAddComparison ? add : comparisonTitle}
      onClick={controller.addComparison}
      disabled={!controller.canAddComparison}
    >
      {add}
    </button>
    {controller.selectionGroups.length > 1 && (
      <button
        type="button"
        data-testid="map-result-remove-comparison"
        data-map-id={mapID}
        aria-label={remove}
        title={remove}
        onClick={controller.removeComparison}
        disabled={!controller.canRemoveComparison}
      >
        {remove}
      </button>
    )}
  </span>;
}
