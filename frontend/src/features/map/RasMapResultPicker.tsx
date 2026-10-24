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

  return <>
    {controller.nodeOptions.length > 1 && <label className="map-time-control" style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
      <span>{nodeLabel}</span>
      <select
        aria-label={nodeLabel}
        data-testid={`map-result-node-${mapID}`}
        value={controller.activeNodeKey ?? ''}
        onChange={(event) => controller.selectNode(event.currentTarget.value)}
      >
        {controller.nodeOptions.map(({ key, node }) => (
          <option value={key} key={key}>{node.name}{node.selectedMapType ? ` · ${resultMapTypeText(language, node.selectedMapType)}` : ''}</option>
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
        <label className="map-time-control" key={`${controller.activeNodeKey}-${group.selectionIndex}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
          <span>{sourceLabelText}{controller.selectionGroups.length > 1 ? ` ${group.selectionIndex + 1}` : ''}</span>
          {(options.length > 1 || (!group.selectedResult && options.length > 0)) ? (
            <select
              aria-label={`${sourceLabelText} ${group.selectionIndex + 1}`}
              data-testid={testID}
              value={selectedValue}
              disabled={!controller.canChangeSource}
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
                  disabled={isMapResultSourcePending(choice)}
                >
                  {sourceName(choice)}{isMapResultSourcePending(choice) ? (zh ? '（计算写入中）' : ' (compute writing)') : ''}
                </option>
              ))}
            </select>
          ) : (
            <output data-testid={testID}>{group.selectedResult ? sourceName(group.selectedResult) : noResult}</output>
          )}
        </label>
      );
    })}
  </>;
}
