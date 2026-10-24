import { isMapResultSourcePending } from './useMapResultFrames';
import type { MapResultFramesController, MapResultSourceChoice } from './useMapResultFrames';
import { resultMapTypeText } from './resultMapTypeText';

interface MapTimeAxisProps {
  language: 'zh' | 'en';
  /** The parent owns the single frame reader; this component only controls it. */
  controller?: MapResultFramesController;
}

function sourceValue(choice: MapResultSourceChoice): string {
  return `${choice.resultPath}\u0000${choice.revision}`;
}

function sourceLabel(choice: MapResultSourceChoice): string {
  const details = [choice.planName, choice.endTime].filter(Boolean).join(' · ');
  return details ? `${choice.resultName} · ${details}` : choice.resultName;
}

export function MapTimeAxis({ language, controller }: MapTimeAxisProps) {
  const zh = language === 'zh';
  const mapID = controller?.sources?.mapID ?? 'none';
  const selectedStepIndex = controller?.steps.findIndex((step) => step.id === controller.selectedProfileIndex) ?? -1;
  const rangeValue = selectedStepIndex >= 0 ? selectedStepIndex : 0;
  const currentStep = controller?.steps[rangeValue] ?? null;
  const title = zh ? '结果时间轴' : 'Result time axis';
  const stepListID = `map-result-steps-${mapID}`;
  const copy = language === 'zh'
    ? {
      idle: '打开已保存的地图文档后，时间轴会读取该地图实际选中的结果。',
      loading: '正在读取服务端结果时间与渲染帧…',
      unavailable: '当前无法读取结果时间轴。',
      empty: '当前地图或视图没有可显示的结果时间帧。',
      noTime: '无可用时刻',
      source: '结果源',
      mapNode: '结果图层',
      addComparison: '添加对比结果',
      removeComparison: '移除对比结果',
      comparisonLimit: '最多可添加 32 个结果对比源。',
      noUnusedComparison: '没有可添加的未使用结果。',
      time: '当前时刻',
      play: '播放',
      pause: '暂停',
      noResult: '尚无结果时间序列。',
      of: '共',
      steps: '个时刻',
      refresh: '刷新',
    }
    : {
      idle: 'Open a saved map document to read its selected Result timeline.',
      loading: 'Reading Result times and rendered frames from the service…',
      unavailable: 'The Result time axis is unavailable.',
      empty: 'No Result frame is available for the current map or view.',
      noTime: 'No time steps',
      source: 'Result source',
      mapNode: 'Result layer',
      addComparison: 'Add comparison result',
      removeComparison: 'Remove comparison result',
      comparisonLimit: 'Up to 32 Result comparison sources are supported.',
      noUnusedComparison: 'No unused Result is available to add.',
      time: 'Selected time',
      play: 'Play',
      pause: 'Pause',
      noResult: 'No Result time series is loaded.',
      of: 'of',
      steps: 'steps',
      refresh: 'Refresh',
    };

  const statusCopy = controller?.status === 'loading' ? copy.loading
    : controller?.status === 'idle' ? copy.idle
      : controller?.status === 'unavailable' ? copy.unavailable
        : controller?.status === 'empty' ? copy.empty
          : controller?.status === 'error' ? copy.unavailable
            : null;
  const hasSteps = Boolean(controller?.steps.length);
  const isMultiResult = controller?.activeNode?.node.nodeType === 'MultiResult';
  const comparisonTitle = (controller?.selectionGroups.length ?? 0) >= 32
    ? copy.comparisonLimit
    : copy.noUnusedComparison;

  return (
    <section className="map-time-axis" aria-label={title} data-testid={`map-result-time-axis-${mapID}`}>
      <div className="map-time-label">
        <span className="map-time-icon" aria-hidden="true">◷</span>
        <strong>{title}</strong>
        {hasSteps ? (
          <span className="map-time-empty" data-testid={`map-result-step-count-${mapID}`}>
            {copy.of} {controller?.steps.length} {copy.steps}
          </span>
        ) : <span className="map-time-empty">{copy.noTime}</span>}
      </div>
      {controller && controller.nodeOptions.length > 1 && (
        <label className="map-time-control" style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
          <span>{copy.mapNode}</span>
          <select
            aria-label={copy.mapNode}
            data-testid={`map-result-node-${mapID}`}
            value={controller.activeNodeKey ?? ''}
            onChange={(event) => controller.selectNode(event.currentTarget.value)}
          >
            {controller.nodeOptions.map(({ key, node }) => (
              <option value={key} key={key}>{node.name}{node.selectedMapType ? ` · ${resultMapTypeText(language, node.selectedMapType)}` : ''}</option>
            ))}
          </select>
        </label>
      )}
      {controller?.activeNode && controller.selectionGroups.map((group) => {
        const options = [...group.choices];
        if (group.selectedResult && !options.some((choice) => sourceValue(choice) === sourceValue(group.selectedResult!))) {
          options.unshift(group.selectedResult);
        }
        const selectedValue = group.selectedResult ? sourceValue(group.selectedResult) : '';
        const testID = `map-result-source-${mapID}-${group.selectionIndex}`;
        return (
          <label className="map-time-control" key={`${controller.activeNodeKey}-${group.selectionIndex}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
            <span>{copy.source}{controller.selectionGroups.length > 1 ? ` ${group.selectionIndex + 1}` : ''}</span>
            {(options.length > 1 || (!group.selectedResult && options.length > 0)) ? (
              <select
                aria-label={`${copy.source} ${group.selectionIndex + 1}`}
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
                    {sourceLabel(choice)}{isMapResultSourcePending(choice) ? (zh ? '（计算写入中）' : ' (compute writing)') : ''}
                  </option>
                ))}
              </select>
            ) : (
              <output data-testid={testID}>{group.selectedResult ? sourceLabel(group.selectedResult) : copy.noResult}</output>
            )}
          </label>
        );
      })}
      {controller && isMultiResult && (
        <span className="map-time-comparison-actions" style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginInlineEnd: 12 }}>
          <button
            type="button"
            data-testid="map-result-add-comparison"
            data-map-id={mapID}
            aria-label={copy.addComparison}
            title={controller.canAddComparison ? copy.addComparison : comparisonTitle}
            onClick={controller.addComparison}
            disabled={!controller.canAddComparison}
          >
            {copy.addComparison}
          </button>
          {controller.selectionGroups.length > 1 && (
            <button
              type="button"
              data-testid="map-result-remove-comparison"
              data-map-id={mapID}
              aria-label={copy.removeComparison}
              title={copy.removeComparison}
              onClick={controller.removeComparison}
              disabled={!controller.canRemoveComparison}
            >
              {copy.removeComparison}
            </button>
          )}
        </span>
      )}
      {controller && hasSteps && (
        <div className="map-time-track" aria-label={copy.time} style={{
          height: 26, display: 'flex', alignItems: 'center', gap: 10,
          background: 'transparent',
        }}>
          <input
            type="range"
            min={0}
            max={Math.max(controller.steps.length - 1, 0)}
            step={1}
            value={rangeValue}
            aria-label={copy.time}
            data-testid={`map-result-profile-${mapID}`}
            data-profile-index={currentStep?.id ?? ''}
            list={stepListID}
            style={{ flex: '1 1 auto', minWidth: 80 }}
            onChange={(event) => {
              const step = controller.steps[Number(event.currentTarget.value)];
              if (step) controller.selectProfile(step.id);
            }}
            disabled={controller.status === 'loading'}
          />
          <datalist id={stepListID}>
            {controller.steps.map((step, index) => (
              <option
                key={step.id}
                value={index}
                label={step.label || step.time}
                data-profile-index={step.id}
                data-testid={`map-result-step-${mapID}-${step.id}`}
              />
            ))}
          </datalist>
          <span className="map-time-current" data-testid={`map-result-time-${mapID}`} style={{
            position: 'static', width: 'auto', height: 'auto', flex: '0 0 auto',
            borderRadius: 0, background: 'transparent', whiteSpace: 'nowrap',
          }}>
            {controller.selectedProfileTime ?? currentStep?.time ?? currentStep?.label ?? copy.noTime}
          </span>
          {(controller.canPlay || controller.isPlaying) && (
            <button
              type="button"
              className="map-time-play"
              data-testid={`map-result-${controller.isPlaying ? 'pause' : 'play'}-${mapID}`}
              aria-label={controller.isPlaying ? copy.pause : copy.play}
              onClick={controller.isPlaying ? controller.pause : controller.play}
              disabled={!controller.isPlaying && controller.status === 'loading'}
            style={{ flex: '0 0 auto' }}
            >
              {controller.isPlaying ? copy.pause : copy.play}
            </button>
          )}
        </div>
      )}
      <div className="map-time-caption">
        <span role="status" aria-live="polite">{controller?.message ?? statusCopy ?? (hasSteps ? '' : copy.noResult)}</span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          {hasSteps ? currentStep?.label ?? currentStep?.time : ''}
          {controller && ['error', 'unavailable', 'empty'].includes(controller.status) && (
            <button
              type="button"
              data-testid={`map-result-refresh-${mapID}`}
              onClick={controller.refresh}
              disabled={controller.status === 'loading'}
            >
              {copy.refresh}
            </button>
          )}
        </span>
      </div>
    </section>
  );
}
