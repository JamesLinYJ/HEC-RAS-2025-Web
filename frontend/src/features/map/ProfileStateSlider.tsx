import type { MapResultFramesController, MapResultRenderStep } from './useMapResultFrames';

interface ProfileStateSliderProps {
  language: 'zh' | 'en';
  mapID: string;
  controller: MapResultFramesController;
  rangeValue: number;
  currentStep: MapResultRenderStep | null;
}

/** Controlled profile selector over the exact native profile IDs returned by the frame reader. */
export function ProfileStateSlider({ language, mapID, controller, rangeValue, currentStep }: ProfileStateSliderProps) {
  const timeLabel = language === 'zh' ? '当前时刻' : 'Selected time';
  const noTime = language === 'zh' ? '无可用时刻' : 'No time steps';
  const startLabel = language === 'zh' ? '首个时刻' : 'First time step';
  const backLabel = language === 'zh' ? '上一个时刻' : 'Previous time step';
  const nextLabel = language === 'zh' ? '下一个时刻' : 'Next time step';
  const endLabel = language === 'zh' ? '最后时刻' : 'Last time step';
  const stepListID = `map-result-steps-${mapID}`;
  const isLoading = controller.status === 'loading';
  const selectStep = (index: number) => {
    const step = controller.steps[index];
    if (step) controller.selectProfile(step.id);
  };

  return <>
    <button type="button" aria-label={startLabel} title={startLabel} onClick={() => selectStep(0)} disabled={isLoading || rangeValue <= 0}>⏮</button>
    <button type="button" aria-label={backLabel} title={backLabel} onClick={() => selectStep(rangeValue - 1)} disabled={isLoading || rangeValue <= 0}>◀</button>
    <input
      type="range"
      min={0}
      max={Math.max(controller.steps.length - 1, 0)}
      step={1}
      value={rangeValue}
      aria-label={timeLabel}
      data-testid={`map-result-profile-${mapID}`}
      data-profile-index={currentStep?.id ?? ''}
      list={stepListID}
      style={{ flex: '1 1 auto', minWidth: 80 }}
      onChange={(event) => {
        const step = controller.steps[Number(event.currentTarget.value)];
        if (step) controller.selectProfile(step.id);
      }}
      disabled={isLoading}
    />
    <button type="button" aria-label={nextLabel} title={nextLabel} onClick={() => selectStep(rangeValue + 1)} disabled={isLoading || rangeValue >= controller.steps.length - 1}>▶</button>
    <button type="button" aria-label={endLabel} title={endLabel} onClick={() => selectStep(controller.steps.length - 1)} disabled={isLoading || rangeValue >= controller.steps.length - 1}>⏭</button>
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
      {controller.selectedProfileTime ?? currentStep?.time ?? currentStep?.label ?? noTime}
    </span>
  </>;
}
