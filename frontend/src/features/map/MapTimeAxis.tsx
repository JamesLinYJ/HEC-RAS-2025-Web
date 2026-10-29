import { useEffect, useRef } from 'react';
import type { MapResultFramesController } from './useMapResultFrames';
import { MultiResultNodeDetails } from './MultiResultNodeDetails';
import { MultiResultVisualizationSettings } from './MultiResultVisualizationSettings';
import { RasMapResultPicker } from './RasMapResultPicker';
import { RasProfileStateSlider } from './RasProfileStateSlider';
import './mapTimeAxis.css';

interface MapTimeAxisProps {
  interactionDisabled?: boolean;
  language: 'zh' | 'en';
  /** The parent owns the single frame reader; this component only controls it. */
  controller?: MapResultFramesController;
}

/** Orchestrates map result selection, MultiResult controls, and the active profile timeline. */
export function MapTimeAxis({ language, controller, interactionDisabled = false }: MapTimeAxisProps) {
  const zh = language === 'zh';
  const mapID = controller?.sources?.mapID ?? 'none';
  const hasSteps = Boolean(controller?.steps.length);
  const selectedStepIndex = controller?.steps.findIndex((step) => step.id === controller.selectedProfileIndex) ?? -1;
  const currentStep = controller?.steps[selectedStepIndex >= 0 ? selectedStepIndex : 0] ?? null;
  const title = zh ? '结果时间轴' : 'Result time axis';
  const copy = language === 'zh'
    ? {
      idle: '打开已保存的地图文档后，时间轴会读取该地图实际选中的结果。',
      loading: '正在读取服务端结果时间与渲染帧…',
      unavailable: '当前无法读取结果时间轴。',
      empty: '当前地图或视图没有可显示的结果时间帧。',
      noTime: '无可用时刻',
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
  const selectedSource = controller?.activeNode?.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
  const statusKey = JSON.stringify([controller?.activeNodeKey, selectedSource?.resultPath ?? null,
    selectedSource?.revision ?? null, controller?.keyframeNodeKey, controller?.selectedProfileIndex,
    controller?.sources?.contextToken ?? null]);
  const loading = controller?.status === 'loading';
  const settledMessage = controller?.message ?? statusCopy ?? (hasSteps ? '' : copy.noResult);
  const lastSettledMessage = useRef<{ key: string; message: string } | null>(null);
  const displayedMessage = loading
    ? lastSettledMessage.current?.key === statusKey ? lastSettledMessage.current.message : copy.loading
    : settledMessage;
  useEffect(() => {
    if (!loading) lastSettledMessage.current = { key: statusKey, message: settledMessage };
  }, [loading, settledMessage, statusKey]);

  return (
    <section className="map-time-axis" aria-label={title} data-testid={`map-result-time-axis-${mapID}`} inert={interactionDisabled}>
      <div className="map-time-label">
        <span className="map-time-icon" aria-hidden="true">◷</span>
        <strong>{title}</strong>
        {hasSteps ? (
          <span className="map-time-empty" data-testid={`map-result-step-count-${mapID}`}>
            {copy.of} {controller?.steps.length} {copy.steps}
          </span>
        ) : <span className="map-time-empty">{copy.noTime}</span>}
      </div>
      {controller && <>
        <div className="map-time-source-row">
          <RasMapResultPicker language={language} controller={controller} mapID={mapID} />
          <MultiResultNodeDetails language={language} controller={controller} mapID={mapID} />
        </div>
        <MultiResultVisualizationSettings language={language} controller={controller} />
        <RasProfileStateSlider language={language} controller={controller} mapID={mapID} />
      </>}
      <div className="map-time-caption">
        <span className={`map-time-caption-status${loading ? ' is-loading' : ''}`} role="status" aria-live="polite"
          aria-busy={loading} aria-label={loading ? copy.loading : undefined} title={loading ? copy.loading : displayedMessage}>
          {displayedMessage}
        </span>
        <span className="map-time-caption-actions">
          <span className="map-time-caption-label" title={hasSteps ? currentStep?.label ?? currentStep?.time : ''}>
            {hasSteps ? currentStep?.label ?? currentStep?.time : ''}
          </span>
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
