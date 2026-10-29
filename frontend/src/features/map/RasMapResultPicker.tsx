import { isMapResultSourcePending } from './useMapResultFrames';
import type { MapResultFramesController, MapResultSourceChoice } from './useMapResultFrames';
import { resultMapTypeText } from './resultMapTypeText';

interface RasMapResultPickerProps {
  language: 'zh' | 'en';
  controller: MapResultFramesController;
  mapID: string;
}

/** Selects the active result layer from the node list already resolved for this map. */
export function RasMapResultPicker({ language, controller, mapID }: RasMapResultPickerProps) {
  const zh = language === 'zh';
  const nodeLabel = zh ? '结果图层' : 'Result layer';
  const sourceLabelText = zh ? '结果源' : 'Result source';
  const noResult = zh ? '尚无结果时间序列。' : 'No Result time series is loaded.';

  const sourceValue = (choice: MapResultSourceChoice): string => `${choice.resultPath}\u0000${choice.revision}`;
  const sourceName = (choice: MapResultSourceChoice): string => {
    const details = [choice.planName, choice.endTime].filter(Boolean).join(' · ');
    return details ? `${choice.resultName} · ${details}` : choice.resultName;
  };
  const nodeName = (node: MapResultFramesController['nodeOptions'][number]['node']): string =>
    `${node.name}${node.selectedMapType ? ` · ${resultMapTypeText(language, node.selectedMapType)}` : ''}`;
  const activeNode = controller.nodeOptions.find(option => option.key === controller.activeNodeKey)?.node;

  return <>
    {controller.nodeOptions.length > 1 && <label className="map-time-control">
      <span>{nodeLabel}</span>
      <select
        aria-label={nodeLabel}
        data-testid={`map-result-node-${mapID}`}
        value={controller.activeNodeKey ?? ''}
        title={activeNode ? nodeName(activeNode) : undefined}
        disabled={controller.resultLayerSettings?.locked}
        onChange={(event) => controller.selectNode(event.currentTarget.value)}
      >
        {controller.nodeOptions.map(({ key, node }) => (
          <option value={key} key={key} title={nodeName(node)}>{nodeName(node)}</option>
        ))}
      </select>
    </label>}
    {controller.activeNode && controller.selectionGroups.map((group) => {
      const options = [...group.choices];
      if (group.selectedResult && !options.some((choice) => sourceValue(choice) === sourceValue(group.selectedResult!))) {
        options.unshift(group.selectedResult);
      }
      const selectedValue = group.selectedResult ? sourceValue(group.selectedResult) : '';
      const testID = `map-result-source-${mapID}-${group.selectionIndex}`;
      return (
        <label className="map-time-control" key={`${controller.activeNodeKey}-${group.selectionIndex}`}>
          <span>{sourceLabelText}{controller.selectionGroups.length > 1 ? ` ${group.selectionIndex + 1}` : ''}</span>
          {(options.length > 1 || (!group.selectedResult && options.length > 0)) ? (
            <select
              aria-label={`${sourceLabelText} ${group.selectionIndex + 1}`}
              data-testid={testID}
              value={selectedValue}
              title={group.selectedResult ? sourceName(group.selectedResult) : undefined}
              disabled={!controller.canChangeSource || controller.resultLayerSettings?.locked}
              onChange={(event) => {
                const selected = options.find((choice) => sourceValue(choice) === event.currentTarget.value);
                if (selected && controller.activeNodeKey) controller.selectSource(controller.activeNodeKey, group.selectionIndex, selected);
              }}
            >
              {!selectedValue && <option value="" disabled>{zh ? '请选择已保存的结果' : 'Choose a saved Result'}</option>}
              {options.map((choice) => (
                <option
                  key={sourceValue(choice)}
                  value={sourceValue(choice)}
                  title={sourceName(choice)}
                  disabled={isMapResultSourcePending(choice)}
                >
                  {sourceName(choice)}{isMapResultSourcePending(choice) ? (zh ? '（计算写入中）' : ' (compute writing)') : ''}
                </option>
              ))}
            </select>
          ) : (
            <output data-testid={testID} title={group.selectedResult ? sourceName(group.selectedResult) : noResult}>
              {group.selectedResult ? sourceName(group.selectedResult) : noResult}
            </output>
          )}
        </label>
      );
    })}
  </>;
}
