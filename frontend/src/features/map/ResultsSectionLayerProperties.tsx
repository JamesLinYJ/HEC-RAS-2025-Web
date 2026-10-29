import { useEffect, useRef } from 'react';
import type { MapResultFramesController } from './useMapResultFrames';
import { isMapResultSourcePending } from './useMapResultFrames';
import { resultMapTypeText } from './resultMapTypeText';
import { RESULT_MAP_TYPES } from './results-layer-settings/resultLayerSettingsTransaction';
import './results-layer-settings/resultsLayerSettings.css';

interface Props { language: 'zh' | 'en'; controller: MapResultFramesController }

/** The Results section controls every checked MultiResult; its keyframe is a runtime view setting. */
export function ResultsSectionLayerProperties({ language, controller }: Props) {
  // Loading does not establish whether the selected source has a matching
  // frame. Preserve only a settled indication for the same source and time;
  // transient text must not resize the viewport that caused the render.
  const lastSettled = useRef<{ key: string; unavailable: boolean } | null>(null);
  const source = controller.activeNode?.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
  const availabilityKey = JSON.stringify([controller.activeNodeKey, source?.resultPath ?? null, source?.revision ?? null,
    controller.keyframeNodeKey, controller.selectedProfileIndex, controller.sources?.contextToken ?? null]);
  const pendingFrame = controller.status === 'loading';
  const unavailable = pendingFrame
    ? lastSettled.current?.key === availabilityKey && lastSettled.current.unavailable
    : controller.activeResultProfileReady === false;
  useEffect(() => {
    if (!pendingFrame) lastSettled.current = { key: availabilityKey, unavailable: controller.activeResultProfileReady === false };
  }, [availabilityKey, controller.activeResultProfileReady, pendingFrame]);
  const settings = controller.resultLayerSettings;
  if (!settings) return null;
  const zh = language === 'zh';
  const value = settings.currentType && RESULT_MAP_TYPES.some(type => type === settings.currentType)
    ? settings.currentType : '';
  const state = settings.state;
  return <div className="results-section-properties" data-testid="results-section-layer-properties">
    <span className="results-section-target" title={settings.groupNames.join('、')}>
      {settings.groupNames.length ? (zh ? `活动结果组（${settings.groupNames.length}）：` : `Active groups (${settings.groupNames.length}): `)
        + settings.groupNames.join('、') : (zh ? '无活动结果组' : 'No active result groups')}
    </span>
    <label className="map-time-control">
      <span>{zh ? '组地图类型' : 'Group map type'}</span>
      <select aria-label={zh ? '组地图类型' : 'Group map type'} value={value} disabled={!settings.canChange}
        data-testid="results-section-map-type" onChange={event => settings.selectType(event.currentTarget.value)}>
        {!value && <option value="" disabled>{settings.mixed ? (zh ? '各组不同' : 'Mixed')
          : settings.groupNames.length ? (zh ? '当前类型未知' : 'Unknown') : (zh ? '无活动结果组' : 'No active groups')}</option>}
        {RESULT_MAP_TYPES.map(type => <option value={type} key={type}>{resultMapTypeText(language, type)}</option>)}
      </select>
    </label>
    <label className="map-time-control">
      <span>{zh ? '共享时刻来源' : 'Shared time source'}</span>
      <select aria-label={zh ? '共享时刻来源' : 'Shared time source'} value={controller.keyframeNodeKey ?? ''}
        title={controller.keyframeOptions.find(option => option.key === controller.keyframeNodeKey)?.node.name}
        data-testid="results-section-keyframe" disabled={settings.locked || controller.status === 'loading' || !controller.keyframeOptions.length}
        onChange={event => controller.selectKeyframe(event.currentTarget.value)}>
        {!controller.keyframeNodeKey && <option value="" disabled>{zh ? '无可用首个来源' : 'No available first source'}</option>}
        {controller.keyframeOptions.map(option => {
          const source = option.node.selections.find(selection => selection.selectionIndex === 0)?.selectedResult;
          const disabled = !source || isMapResultSourcePending(source);
          return <option key={option.key} value={option.key} disabled={disabled}>{option.node.name}
            {disabled ? (zh ? '（无可用来源）' : ' (source unavailable)') : ''}</option>;
        })}
      </select>
    </label>
    <button type="button" data-testid="results-section-resync" onClick={controller.resync}
      disabled={settings.locked || settings.mixed || !controller.keyframeNodeKey || controller.status === 'loading'}>
      {zh ? '重新同步' : 'Resynchronize'}
    </button>
    <span className="results-section-runtime">{zh ? '共享时刻仅用于当前视图' : 'Shared time applies to this view only'}</span>
    {unavailable && <span className="results-section-runtime">
      {zh ? '当前组首个来源的时刻未读回，结果悬停和时刻分析暂不可用' : 'Result hover and time-specific analysis await the first source frame'}
    </span>}
    {state && <span className="results-section-status" role="status">{state.message}
      {settings.locked && !state.busy && <button type="button" onClick={settings.resume} disabled={!settings.canResume}
        data-testid="results-section-resume">{zh ? '继续核对' : 'Resume verification'}</button>}
      {settings.locked && (state.operationID || state.diagnostic) && <details>
        <summary>{zh ? '操作详情' : 'Operation details'}</summary>
        {state.operationID && <div>{zh ? '原操作' : 'Original operation'} <code>{state.operationID}</code></div>}
        {state.diagnostic && <code>{state.diagnostic}</code>}
      </details>}
    </span>}
    {controller.groupRenderMessages.length > 0 && <details className="results-section-status">
      <summary>{zh ? `部分结果不可用（${controller.groupRenderMessages.length}）` : `Some results unavailable (${controller.groupRenderMessages.length})`}</summary>
      {controller.groupRenderMessages.map(message => <div key={message}>{message}</div>)}
    </details>}
  </div>;
}
